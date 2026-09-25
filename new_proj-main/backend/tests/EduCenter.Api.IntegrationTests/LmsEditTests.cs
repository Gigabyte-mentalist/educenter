using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduCenter.Infrastructure;
using EduCenter.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// "I entered it wrong" without a trip to the database (docs/21.09-audit-jurnali S02.03): an exam can be renamed or re-dated,
/// a wrong result taken back, a homework's deadline or maximum fixed, an unused exam, homework or course erased. Anything that
/// already carries someone's work — results, submissions, groups — refuses the delete and says how much.
/// </summary>
[Collection("Api")]
public class LmsEditTests(EduCenterWebApplicationFactory factory)
{
    [Fact]
    public async Task Exam_CanBeFixedEvenAfterResultsAreIn()
    {
        var admin = await ClientAsync("admin@demo-center.test");
        var a = await ArrangeAsync(admin);
        var exam = await CreateExamAsync(admin, a.GroupId);

        var fixedExam = await admin.PatchAsJsonAsync($"/api/exams/{exam}", new { name = "  Fixed name ", type = "Final", date = "2031-05-06" });
        Assert.Equal(HttpStatusCode.OK, fixedExam.StatusCode);
        var dto = (await fixedExam.Content.ReadFromJsonAsync<ExamDto>())!;
        Assert.Equal(("Fixed name", "Final", "2031-05-06", a.GroupId), (dto.Name, dto.Type, dto.Date, dto.GroupId));

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchAsJsonAsync($"/api/exams/{exam}", new { name = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchAsJsonAsync($"/api/exams/{exam}", new { })).StatusCode);

        await RecordResultAsync(admin, exam, a.StudentId);
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/exams/{exam}", new { name = "Renamed again" })).StatusCode);
    }

    [Fact]
    public async Task Exam_WithResultsCannotBeDeleted_UntilTheResultsAreRemoved()
    {
        var admin = await ClientAsync("admin@demo-center.test");
        var a = await ArrangeAsync(admin);
        var exam = await CreateExamAsync(admin, a.GroupId);
        var resultId = await RecordResultAsync(admin, exam, a.StudentId);

        var refused = await admin.DeleteAsync($"/api/exams/{exam}");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("1 ta natija", await refused.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/exams/{exam}/results/{resultId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/exams/{exam}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/exams/{exam}")).StatusCode);
    }

    [Fact]
    public async Task RemovingAResult_LeavesATimelineEntry_AndTellsTheStudentNothing()
    {
        var admin = await ClientAsync("admin@demo-center.test");
        var a = await ArrangeAsync(admin);
        var exam = await CreateExamAsync(admin, a.GroupId);
        var resultId = await RecordResultAsync(admin, exam, a.StudentId);
        var before = await NotificationCountAsync(a.StudentUserId, "EXAM_RESULT");

        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/exams/{exam}/results/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/exams/{exam}/results/{resultId}")).StatusCode);

        var timeline = (await admin.GetFromJsonAsync<List<TimelineDto>>($"/api/students/{a.StudentId}/timeline"))!;
        Assert.Contains(timeline, e => e.EventType == "EXAM_RESULT_RECORDED");
        Assert.Contains(timeline, e => e.EventType == "EXAM_RESULT_REMOVED");
        Assert.Equal(before, await NotificationCountAsync(a.StudentUserId, "EXAM_RESULT")); // a correction is not news
        Assert.Empty((await admin.GetFromJsonAsync<List<ResultDto>>($"/api/exams/{exam}/results"))!);
    }

    [Fact]
    public async Task Homework_CanBeFixed_AndOnlyAMovedDeadlineIsAnnounced()
    {
        var admin = await ClientAsync("admin@demo-center.test");
        var a = await ArrangeAsync(admin);
        var homework = await CreateHomeworkAsync(admin, a.LessonId);

        var titleOnly = await admin.PatchAsJsonAsync($"/api/homework/{homework.Id}", new { title = "  Better title ", description = "Read chapter 3" });
        Assert.Equal(HttpStatusCode.OK, titleOnly.StatusCode);
        var dto = (await titleOnly.Content.ReadFromJsonAsync<HomeworkDto>())!;
        Assert.Equal(("Better title", "Read chapter 3"), (dto.Title, dto.Description));
        Assert.Equal(0, await NotificationCountAsync(a.StudentUserId, "HOMEWORK_CHANGED"));

        var moved = await admin.PatchAsJsonAsync($"/api/homework/{homework.Id}", new { deadline = homework.Deadline.AddDays(2) });
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        Assert.Equal(1, await NotificationCountAsync(a.StudentUserId, "HOMEWORK_CHANGED"));

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchAsJsonAsync($"/api/homework/{homework.Id}", new { title = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchAsJsonAsync($"/api/homework/{homework.Id}", new { maxScore = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchAsJsonAsync($"/api/homework/{homework.Id}", new { })).StatusCode);
    }

    [Fact]
    public async Task Homework_MaximumCannotDropBelowAScoreAlreadyGiven()
    {
        var admin = await ClientAsync("admin@demo-center.test");
        var a = await ArrangeAsync(admin);
        var homework = await CreateHomeworkAsync(admin, a.LessonId); // maxScore 10
        var submission = await SubmitAsync(a, homework.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PatchAsJsonAsync($"/api/homework/submissions/{submission}/review", new { score = 8, feedback = "Good" })).StatusCode);

        var refused = await admin.PatchAsJsonAsync($"/api/homework/{homework.Id}", new { maxScore = 5 });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("(8)", await refused.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/homework/{homework.Id}", new { maxScore = 8 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/homework/{homework.Id}", new { maxScore = 20 })).StatusCode);
    }

    [Fact]
    public async Task Homework_WithASubmissionCannotBeDeleted_ButAnUntouchedOneCan()
    {
        var admin = await ClientAsync("admin@demo-center.test");
        var a = await ArrangeAsync(admin);
        var untouched = await CreateHomeworkAsync(admin, a.LessonId);
        var handedIn = await CreateHomeworkAsync(admin, a.LessonId);
        await SubmitAsync(a, handedIn.Id);

        var refused = await admin.DeleteAsync($"/api/homework/{handedIn.Id}");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("1 ta topshiriq", await refused.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/homework/{untouched.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/homework/{untouched.Id}")).StatusCode);
    }

    [Fact]
    public async Task Course_IsErasedOnlyWhenNoGroupIsOnIt_ClosedGroupsIncluded()
    {
        var admin = await ClientAsync("admin@demo-center.test");
        var free = await CreateCourseAsync(admin);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/courses/{free}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/courses/{free}")).StatusCode);

        var used = await CreateCourseAsync(admin);
        var group = await CreateGroupAsync(admin, used);
        var refused = await admin.DeleteAsync($"/api/courses/{used}");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("1 ta guruh", await refused.Content.ReadAsStringAsync());

        // A cancelled group is history, and history keeps its course.
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/groups/{group}/status", new { status = "Cancelled" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/courses/{used}")).StatusCode);
    }

    [Fact]
    public async Task EveryEndpoint_IsForbidden_ToAnotherTeachersGroup_AndToAStudent()
    {
        var admin = await ClientAsync("admin@demo-center.test");
        var a = await ArrangeAsync(admin);
        var exam = await CreateExamAsync(admin, a.GroupId);
        var resultId = await RecordResultAsync(admin, exam, a.StudentId);
        var homework = await CreateHomeworkAsync(admin, a.LessonId);
        var course = await CreateCourseAsync(admin);

        // The demo teacher holds these permissions — but only over their own groups, and this group is not theirs.
        foreach (var email in new[] { "teacher@demo-center.test", "student@demo-center.test" })
        {
            var client = await ClientAsync(email);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PatchAsJsonAsync($"/api/exams/{exam}", new { name = "Nope" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/exams/{exam}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/exams/{exam}/results/{resultId}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PatchAsJsonAsync($"/api/homework/{homework.Id}", new { title = "Nope" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/homework/{homework.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/courses/{course}")).StatusCode);
        }

        // ...and nothing changed.
        Assert.Equal("Exam to fix", (await admin.GetFromJsonAsync<PagedResponse<ExamDto>>($"/api/exams?groupId={a.GroupId}"))!.Items.Single().Name);
    }

    [Fact]
    public async Task EditingAndErasing_LeaveAnAuditTrail()
    {
        var admin = await ClientAsync("admin@demo-center.test");
        var a = await ArrangeAsync(admin);
        var exam = await CreateExamAsync(admin, a.GroupId);
        var resultId = await RecordResultAsync(admin, exam, a.StudentId);
        var homework = await CreateHomeworkAsync(admin, a.LessonId);

        await admin.PatchAsJsonAsync($"/api/exams/{exam}", new { name = "Audited exam" });
        await admin.PatchAsJsonAsync($"/api/homework/{homework.Id}", new { title = "Audited homework" });
        await admin.DeleteAsync($"/api/exams/{exam}/results/{resultId}");
        await admin.DeleteAsync($"/api/exams/{exam}");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var log = await db.AuditLogs.Where(l => l.ResourceId == exam || l.ResourceId == homework.Id || l.ResourceId == resultId).ToListAsync();
        Assert.Contains(log, l => l.Resource == "Exam" && l.Action == "Modified" && l.NewValueJson!.Contains("Audited exam"));
        Assert.Contains(log, l => l.Resource == "Homework" && l.Action == "Modified" && l.NewValueJson!.Contains("Audited homework"));
        Assert.Contains(log, l => l.Resource == "ExamResult" && l.Action == "Deleted");
        Assert.Contains(log, l => l.Resource == "Exam" && l.Action == "Deleted");
    }

    // ---- Grammar and Vocabulary percentages (S04.01) --------------------------------------------

    [Fact]
    public async Task Result_CarriesGrammarAndVocabularyPercent_ToBothReadEndpoints()
    {
        var admin = await ClientAsync("admin@demo-center.test");
        var a = await ArrangeAsync(admin);
        var exam = await CreateExamAsync(admin, a.GroupId);

        var recorded = await admin.PostAsJsonAsync($"/api/exams/{exam}/results",
            new { studentId = a.StudentId, listening = 6.5, overall = 6.5, grammar = 71.5, vocabulary = 84 });
        Assert.Equal(HttpStatusCode.Created, recorded.StatusCode);
        var created = (await recorded.Content.ReadFromJsonAsync<ResultDto>())!;
        Assert.Equal((71.5m, 84m), (created.Grammar, created.Vocabulary));

        var listed = Assert.Single((await admin.GetFromJsonAsync<List<ResultDto>>($"/api/exams/{exam}/results"))!);
        Assert.Equal((71.5m, 84m, 6.5m), (listed.Grammar, listed.Vocabulary, listed.Listening));

        var row = Assert.Single((await admin.GetFromJsonAsync<List<StudentExamDto>>($"/api/students/{a.StudentId}/exams"))!);
        Assert.Equal((71.5m, 84m), (row.Grammar, row.Vocabulary));
    }

    [Fact]
    public async Task GrammarAndVocabulary_MustBeAPercent_ButBandsKeepTheirZeroToNine()
    {
        var admin = await ClientAsync("admin@demo-center.test");
        var a = await ArrangeAsync(admin);
        var exam = await CreateExamAsync(admin, a.GroupId);

        var tooHigh = await admin.PostAsJsonAsync($"/api/exams/{exam}/results", new { studentId = a.StudentId, grammar = 101 });
        Assert.Equal(HttpStatusCode.BadRequest, tooHigh.StatusCode);
        Assert.Contains("grammar", await tooHigh.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync($"/api/exams/{exam}/results", new { studentId = a.StudentId, vocabulary = -1 })).StatusCode);
        // A percent is not a band: 9.5 is fine as a percentage but not as a Listening score.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync($"/api/exams/{exam}/results", new { studentId = a.StudentId, listening = 9.5 })).StatusCode);

        // Nothing above was stored, and both ends of the range are accepted.
        Assert.Empty((await admin.GetFromJsonAsync<List<ResultDto>>($"/api/exams/{exam}/results"))!);
        var edges = await admin.PostAsJsonAsync($"/api/exams/{exam}/results", new { studentId = a.StudentId, grammar = 100, vocabulary = 0 });
        Assert.Equal(HttpStatusCode.Created, edges.StatusCode);
    }

    [Fact]
    public async Task Result_WithoutGrammarOrVocabulary_StillWorks_AndAPercentAloneIsEnough()
    {
        var admin = await ClientAsync("admin@demo-center.test");
        var a = await ArrangeAsync(admin);
        var exam = await CreateExamAsync(admin, a.GroupId);

        // An IELTS-style result, as before this step: the two new fields simply stay empty.
        var resultId = await RecordResultAsync(admin, exam, a.StudentId);
        var row = Assert.Single((await admin.GetFromJsonAsync<List<StudentExamDto>>($"/api/students/{a.StudentId}/exams"))!);
        Assert.Equal(6.5m, row.Overall);
        Assert.Null(row.Grammar);
        Assert.Null(row.Vocabulary);

        // "At least one score" now counts them: a Unit test may grade only Grammar.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/exams/{exam}/results/{resultId}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync($"/api/exams/{exam}/results", new { studentId = a.StudentId })).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await admin.PostAsJsonAsync($"/api/exams/{exam}/results", new { studentId = a.StudentId, grammar = 60 })).StatusCode);
    }

    [Fact]
    public async Task Result_IsCorrectedByRemovingItAndRecordingItAgainWithTheNewFields()
    {
        var admin = await ClientAsync("admin@demo-center.test");
        var a = await ArrangeAsync(admin);
        var exam = await CreateExamAsync(admin, a.GroupId);
        var resultId = await RecordResultAsync(admin, exam, a.StudentId);

        // A second result for the same student is still refused — there is no upsert.
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync($"/api/exams/{exam}/results", new { studentId = a.StudentId, grammar = 70, vocabulary = 80 })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/exams/{exam}/results/{resultId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await admin.PostAsJsonAsync($"/api/exams/{exam}/results", new { studentId = a.StudentId, overall = 6.5, grammar = 70, vocabulary = 80 })).StatusCode);
        var row = Assert.Single((await admin.GetFromJsonAsync<List<StudentExamDto>>($"/api/students/{a.StudentId}/exams"))!);
        Assert.Equal((70m, 80m), (row.Grammar, row.Vocabulary));
    }

    // ---- helpers -------------------------------------------------------------------------------

    private record Arrangement(Guid GroupId, Guid StudentId, Guid StudentUserId, string StudentEmail, Guid LessonId);

    /// <summary>A fresh group with one enrolled student and one lesson still ahead. The demo teacher takes the lesson: it only names
    /// a teacher, and reusing them keeps this class from adding accounts other tests page through.</summary>
    private async Task<Arrangement> ArrangeAsync(HttpClient admin)
    {
        var group = await CreateGroupAsync(admin, await CreateCourseAsync(admin));
        var branchId = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var email = $"lms-edit-{Guid.NewGuid():N}@demo-center.test";
        var student = await admin.PostAsJsonAsync("/api/students", new
        {
            fullName = "Lms Edit Student", phone = "+998900000092", email, password = DbSeeder.DemoPassword, groupId = group,
        });
        Assert.Equal(HttpStatusCode.Created, student.StatusCode);
        var studentId = (await student.Content.ReadFromJsonAsync<IdDto>())!.Id;

        var superAdmin = await ClientAsync("superadmin@demo-center.test");
        var teacherId = (await superAdmin.GetFromJsonAsync<PagedResponse<UserDto>>("/api/users?role=TEACHER"))!.Items
            .First(u => u.Email == "teacher@demo-center.test").Id;
        var startsAt = DateTimeOffset.UtcNow.Date.AddDays(Random.Shared.Next(400, 4000)).AddMinutes(Random.Shared.Next(0, 1440));
        var lesson = await admin.PostAsJsonAsync("/api/schedule/lessons", new
        {
            branchId, groupId = group, teacherId, roomName = $"R-{Guid.NewGuid():N}"[..8], startsAt, endsAt = startsAt.AddMinutes(60), topic = "Lms edit",
        });
        Assert.Equal(HttpStatusCode.Created, lesson.StatusCode);

        using var scope = factory.Services.CreateScope();
        var userId = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Students
            .Where(s => s.Id == studentId).Select(s => s.UserId).SingleAsync();
        return new Arrangement(group, studentId, userId, email, (await lesson.Content.ReadFromJsonAsync<IdDto>())!.Id);
    }

    private static async Task<Guid> CreateCourseAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/courses", new { name = $"Lms Edit Course {Guid.NewGuid():N}"[..25], levelCategory = "IELTS" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    private static async Task<Guid> CreateGroupAsync(HttpClient client, Guid courseId)
    {
        var branchId = (await client.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var response = await client.PostAsJsonAsync("/api/groups", new { branchId, courseId, name = $"LE-{Guid.NewGuid():N}", capacity = 10 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    private static async Task<Guid> CreateExamAsync(HttpClient admin, Guid groupId)
    {
        var response = await admin.PostAsJsonAsync("/api/exams", new { groupId, name = "Exam to fix", type = "Unit", date = "2031-01-15" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ExamDto>())!.Id;
    }

    private static async Task<Guid> RecordResultAsync(HttpClient admin, Guid examId, Guid studentId)
    {
        var response = await admin.PostAsJsonAsync($"/api/exams/{examId}/results", new { studentId, overall = 6.5 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ResultDto>())!.Id;
    }

    private static async Task<HomeworkDto> CreateHomeworkAsync(HttpClient admin, Guid lessonId)
    {
        var response = await admin.PostAsJsonAsync("/api/homework", new
        {
            lessonId, title = "Homework to fix", type = "Grammar", deadline = DateTimeOffset.UtcNow.AddDays(10), maxScore = 10,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<HomeworkDto>())!;
    }

    private async Task<Guid> SubmitAsync(Arrangement a, Guid homeworkId)
    {
        var student = await ClientAsync(a.StudentEmail);
        var response = await student.PostAsJsonAsync($"/api/homework/{homeworkId}/submissions", new { contentText = "My answer" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    private async Task<int> NotificationCountAsync(Guid userId, string type)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Notifications.CountAsync(n => n.UserId == userId && n.Type == type);
    }

    /// <summary>An account created through the API starts with the forced password change (S01.05).</summary>
    private async Task<HttpClient> ClientAsync(string email)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = DbSeeder.DemoPassword });
        var tokens = (await response.Content.ReadFromJsonAsync<TokenResponseDto>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        if (tokens.MustChangePassword)
        {
            var change = await client.PostAsJsonAsync("/api/auth/change-password",
                new { currentPassword = DbSeeder.DemoPassword, newPassword = "Chosen-Pass-42" });
            Assert.Equal(HttpStatusCode.OK, change.StatusCode);
            tokens = (await change.Content.ReadFromJsonAsync<TokenResponseDto>())!;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        }
        return client;
    }

    private record TokenResponseDto(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, bool MustChangePassword);
    private record IdDto(Guid Id);
    private record BranchDto(Guid Id, string Name);
    private record UserDto(Guid Id, string Email);
    private record ExamDto(Guid Id, Guid GroupId, string Name, string Type, string Date);
    private record ResultDto(Guid Id, decimal? Listening = null, decimal? Grammar = null, decimal? Vocabulary = null);
    private record StudentExamDto(Guid Id, decimal? Overall, decimal? Grammar, decimal? Vocabulary);
    private record HomeworkDto(Guid Id, Guid LessonId, string Title, string? Description, string Type, DateTimeOffset Deadline, int MaxScore);
    private record TimelineDto(string EventType);
}
