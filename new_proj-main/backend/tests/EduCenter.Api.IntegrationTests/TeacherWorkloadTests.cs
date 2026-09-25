using System.Net;
using System.Net.Http.Json;
using EduCenter.Infrastructure;
using EduCenter.Infrastructure.Seed;
using EduCenter.Modules.Lms.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// Teacher 360° tabs (docs/21.09-audit-jurnali S05.01): the workload numbers group by group, the support-teacher links, and who may
/// see either. Every test builds its own teacher and groups, so the figures are exact — nobody else's lessons are in them.
/// </summary>
[Collection("Api")]
public class TeacherWorkloadTests(EduCenterWebApplicationFactory factory)
{
    private readonly LmsWorld world = new(factory);

    // ---- the numbers ---------------------------------------------------------------------------

    [Fact]
    public async Task Workload_GivesEveryFigureGroupByGroup_AndTheTotalsWeighTheMarks()
    {
        var w = await SetUpAsync();
        var admin = await world.ClientAsync("superadmin@demo-center.test");
        await MarkAsync(w.Lesson1, (w.S1.Id, "Present"), (w.S2.Id, "Absent"));
        await MarkAsync(w.Lesson2, (w.S3.Id, "Present"));

        var result = await WorkloadAsync(admin, w);

        // G1: one of two came → 50%; G2: one of one → 100%. The total is 2 of 3 marks, not the mean of 50 and 100.
        Assert.Equal((2, 0, 66.7), (result.LessonsHeld, result.LessonsCancelled, result.AttendanceRate));
        var g1 = result.Attendance.Single(a => a.GroupId == w.G1);
        var g2 = result.Attendance.Single(a => a.GroupId == w.G2);
        Assert.Equal((1, 0, 1, 2, 50.0), (g1.LessonsHeld, g1.LessonsCancelled, g1.Attended, g1.Counted, g1.AttendanceRate));
        Assert.Equal((1, 0, 1, 1, 100.0), (g2.LessonsHeld, g2.LessonsCancelled, g2.Attended, g2.Counted, g2.AttendanceRate));
    }

    [Fact]
    public async Task ReviewTime_IsTheMeanWaitOfTheMarkedSubmissions_InHours()
    {
        var w = await SetUpAsync();
        var admin = await world.ClientAsync("superadmin@demo-center.test");
        var h1 = await HomeworkAsync(admin, w.Lesson1);
        var h2 = await HomeworkAsync(admin, w.Lesson2);
        // G1: s1's answer waited 5 h for its mark, s2's has not been marked. G2: s3's waited 2 h.
        await SubmitAsync(h1, w.S1.Id, reviewedAfter: TimeSpan.FromHours(5));
        await SubmitAsync(h1, w.S2.Id, reviewedAfter: null);
        await SubmitAsync(h2, w.S3.Id, reviewedAfter: TimeSpan.FromHours(2));

        var result = await WorkloadAsync(admin, w);

        Assert.Equal((2, 1, 3.5), (result.HomeworkCreated, result.HomeworkPendingReview, result.AverageReviewHours));
        var g1 = result.Homework.Single(h => h.GroupId == w.G1);
        var g2 = result.Homework.Single(h => h.GroupId == w.G2);
        Assert.Equal((1, 1, 5.0), (g1.Created, g1.PendingReview, g1.AverageReviewHours));
        Assert.Equal((1, 0, 2.0), (g2.Created, g2.PendingReview, g2.AverageReviewHours));
    }

    [Fact]
    public async Task Exams_AverageTheOverallBand_PerGroup_WithAMonthlyTrend()
    {
        var w = await SetUpAsync();
        var admin = await world.ClientAsync("superadmin@demo-center.test");
        var lastMonth = w.Day.AddMonths(-1);
        var earlier = await ExamAsync(admin, w.G1, lastMonth);
        var later = await ExamAsync(admin, w.G1, w.Day);
        await ResultAsync(admin, earlier, w.S1.Id, 6.0m);
        await ResultAsync(admin, earlier, w.S2.Id, 7.0m);
        await ResultAsync(admin, later, w.S1.Id, 7.5m);
        await ResultAsync(admin, await ExamAsync(admin, w.G2, w.Day), w.S3.Id, 5.0m);

        var result = await WorkloadAsync(admin, w);

        var g1 = result.Exams.Single(e => e.GroupId == w.G1);
        Assert.Equal((2, 3, 6.83m), (g1.Exams, g1.Results, g1.AverageOverall));
        Assert.Equal(
            new[] { (Month(lastMonth), 6.5m, 2), (Month(w.Day), 7.5m, 1) },
            g1.Trend.Select(t => (t.Month, t.Average, t.Results)).ToArray());
        var g2 = result.Exams.Single(e => e.GroupId == w.G2);
        Assert.Equal((1, 1, 5.0m), (g2.Exams, g2.Results, g2.AverageOverall));
    }

    [Fact]
    public async Task ACancelledLesson_IsNotHeld_AndItsMarksDoNotCount()
    {
        var w = await SetUpAsync();
        var admin = await world.ClientAsync("superadmin@demo-center.test");
        await MarkAsync(w.Lesson1, (w.S1.Id, "Present"), (w.S2.Id, "Absent"));
        var second = await ScheduleAsync(admin, w.BranchId, w.G1, w.TeacherId, w.Day, hour: 8);
        await MarkAsync(second, (w.S1.Id, "Absent"), (w.S2.Id, "Absent"));
        var cancel = await admin.PostAsJsonAsync($"/api/schedule/lessons/{second}/cancel", new { reason = "Teacher is ill" });
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);

        var g1 = (await WorkloadAsync(admin, w)).Attendance.Single(a => a.GroupId == w.G1);

        // Counted, the two extra absences would drag G1 down to 25%.
        Assert.Equal((1, 1, 50.0), (g1.LessonsHeld, g1.LessonsCancelled, g1.AttendanceRate));
    }

    [Fact]
    public async Task APeriodWithNothingInIt_IsZeroAndEmpty_NotAnError()
    {
        var w = await SetUpAsync(withStudents: false);
        var admin = await world.ClientAsync("superadmin@demo-center.test");
        var quiet = w.Day.AddYears(-1).AddDays(-100);

        var result = (await admin.GetFromJsonAsync<Workload>($"/api/teachers/{w.TeacherId}/workload?from={quiet:yyyy-MM-dd}&to={quiet.AddDays(10):yyyy-MM-dd}"))!;

        Assert.Equal((0, 0, 0.0, 0, 0), (result.LessonsHeld, result.LessonsCancelled, result.AttendanceRate,
            result.HomeworkCreated, result.HomeworkPendingReview));
        Assert.Null(result.AverageReviewHours);
        Assert.Equal(2, result.Attendance.Count); // the groups are still listed, with nothing in them
        Assert.All(result.Exams, e => Assert.Null(e.AverageOverall));
    }

    [Fact]
    public async Task WithoutADates_TheWindowIsTheLast30Days_AndALongOrBackwardsRangeIsRefused()
    {
        var w = await SetUpAsync(withStudents: false);
        var admin = await world.ClientAsync("superadmin@demo-center.test");

        var byDefault = (await admin.GetFromJsonAsync<Workload>($"/api/teachers/{w.TeacherId}/workload"))!;
        Assert.Equal(30, byDefault.To.DayNumber - byDefault.From.DayNumber);
        Assert.InRange(byDefault.To.DayNumber, DateOnly.FromDateTime(DateTime.UtcNow).DayNumber - 1, DateOnly.FromDateTime(DateTime.UtcNow).DayNumber + 1);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.GetAsync($"/api/teachers/{w.TeacherId}/workload?from=2024-01-01&to=2025-06-01")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.GetAsync($"/api/teachers/{w.TeacherId}/workload?from=2025-06-01&to=2025-05-01")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await admin.GetAsync($"/api/teachers/{w.TeacherId}/workload?from=2025-01-01&to=2026-01-02")).StatusCode); // 366 days: the limit
    }

    // ---- who may see it ------------------------------------------------------------------------

    [Fact]
    public async Task TheTeacherSeesTheirOwnTabs_AColleagueDoesNot()
    {
        var w = await SetUpAsync(withStudents: false);
        var self = await world.ClientAsync(w.TeacherEmail);
        var colleague = await world.ClientAsync("teacher@demo-center.test");

        foreach (var tab in new[] { "workload", "summary", "support-teachers", "performance" })
        {
            Assert.Equal(HttpStatusCode.OK, (await self.GetAsync($"/api/teachers/{w.TeacherId}/{tab}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await colleague.GetAsync($"/api/teachers/{w.TeacherId}/{tab}")).StatusCode);
        }
    }

    [Fact]
    public async Task AnAdministrator_SeesTheirOwnBranchsTeacher_ButNotAnotherBranchs()
    {
        var admin = await world.ClientAsync("admin@demo-center.test");
        var superAdmin = await world.ClientAsync("superadmin@demo-center.test");
        var ownBranch = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var (own, _) = await CreateTeacherAsync(superAdmin, ownBranch, "TEACHER");
        var (abroad, _) = await CreateTeacherAsync(superAdmin, await SharedBranchAsync(), "TEACHER"); // not one of the administrator's branches

        foreach (var tab in new[] { "workload", "summary", "support-teachers" })
        {
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/teachers/{own}/{tab}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync($"/api/teachers/{abroad}/{tab}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await superAdmin.GetAsync($"/api/teachers/{abroad}/{tab}")).StatusCode);
        }
    }

    [Fact]
    public async Task AStudent_AnAnonymousCaller_AndANonTeacher_GetNothing()
    {
        var w = await SetUpAsync(withStudents: false);
        var student = await world.ClientAsync("student@demo-center.test");
        var superAdmin = await world.ClientAsync("superadmin@demo-center.test");
        var nonTeacherId = await world.UserIdAsync("operator@demo-center.test", "OPERATOR");

        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync($"/api/teachers/{w.TeacherId}/workload")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync($"/api/teachers/{w.TeacherId}/workload")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await superAdmin.GetAsync($"/api/teachers/{nonTeacherId}/workload")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await superAdmin.GetAsync($"/api/teachers/{nonTeacherId}/summary")).StatusCode);
    }

    [Fact]
    public async Task Summary_NamesTheBranch_TheStartDate_AndCountsGroupsAndStudents()
    {
        var w = await SetUpAsync();
        var admin = await world.ClientAsync("superadmin@demo-center.test");

        var summary = (await admin.GetFromJsonAsync<Summary>($"/api/teachers/{w.TeacherId}/summary"))!;

        Assert.Equal(w.TeacherEmail, summary.Email);
        Assert.Equal((2, 3), (summary.GroupsCount, summary.ActiveStudents));
        Assert.Single(summary.Branches);
        Assert.InRange(summary.JoinedAt, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddMinutes(1));
    }

    // ---- support teachers ----------------------------------------------------------------------

    [Fact]
    public async Task SupportTeachers_ListsAssistantsInMyGroups_AndWhoseGroupsIAssist()
    {
        var w = await SetUpAsync(withStudents: false);
        var superAdmin = await world.ClientAsync("superadmin@demo-center.test");
        var (assistantId, _) = await CreateTeacherAsync(superAdmin, w.BranchId, "SUPPORT_TEACHER");
        Assert.Equal(HttpStatusCode.NoContent,
            (await superAdmin.PostAsJsonAsync($"/api/groups/{w.G1}/staff", new { userId = assistantId, role = "SupportTeacher" })).StatusCode);

        var lead = (await superAdmin.GetFromJsonAsync<SupportLinks>($"/api/teachers/{w.TeacherId}/support-teachers"))!;
        var assistant = (await superAdmin.GetFromJsonAsync<SupportLinks>($"/api/teachers/{assistantId}/support-teachers"))!;

        Assert.Equal(new[] { (assistantId, w.G1) }, lead.SupportTeachers.Select(s => (s.UserId, s.GroupId)).ToArray());
        Assert.Empty(lead.AssistsIn);
        Assert.Equal(new[] { (w.TeacherId, w.G1) }, assistant.AssistsIn.Select(s => (s.UserId, s.GroupId)).ToArray());
        Assert.Empty(assistant.SupportTeachers);
    }

    // ---- activity history ----------------------------------------------------------------------

    [Fact]
    public async Task ActivityHistory_IsTheAuditLogFilteredByActor_AndOnlyForTheOfficeThatMayReadIt()
    {
        var w = await SetUpAsync(withStudents: false); // the set-up itself creates groups and lessons as the Super Admin: audited actions
        var admin = await world.ClientAsync("admin@demo-center.test");
        var actorId = await UserIdAsync("superadmin@demo-center.test");

        var byActor = (await admin.GetFromJsonAsync<Page<AuditRow>>($"/api/audit-logs?userId={actorId}&size=200"))!;
        var byNobody = (await admin.GetFromJsonAsync<Page<AuditRow>>($"/api/audit-logs?userId={Guid.NewGuid()}"))!;

        Assert.NotEmpty(byActor.Items);
        Assert.All(byActor.Items, row => Assert.Equal(actorId, row.UserId));
        Assert.Empty(byNobody.Items);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await (await world.ClientAsync(w.TeacherEmail)).GetAsync($"/api/audit-logs?userId={w.TeacherId}")).StatusCode);
    }

    // ---- set-up --------------------------------------------------------------------------------

    /// <summary>A fresh teacher who leads two groups — G1 with two students, G2 with one — each with one lesson on <see cref="Classroom.Day"/>.</summary>
    private sealed record Classroom(
        Guid TeacherId, string TeacherEmail, Guid BranchId, DateOnly Day, Guid G1, Guid G2,
        LmsWorld.Student S1, LmsWorld.Student S2, LmsWorld.Student S3, Guid Lesson1, Guid Lesson2);

    /// <param name="withStudents">Off for the tests that are about who may see the tabs, not the figures: the suite's student list is
    /// paged at 200 and every student registered here is one more that other tests have to find their way past.</param>
    private async Task<Classroom> SetUpAsync(bool withStudents = true)
    {
        var superAdmin = await world.ClientAsync("superadmin@demo-center.test");
        var branchId = await SharedBranchAsync();
        var (teacherId, email) = await CreateTeacherAsync(superAdmin, branchId, "TEACHER");

        var course = await superAdmin.PostAsJsonAsync("/api/courses", new { name = $"Load Course {Guid.NewGuid():N}"[..25], levelCategory = "IELTS" });
        Assert.Equal(HttpStatusCode.Created, course.StatusCode);
        var courseId = (await course.Content.ReadFromJsonAsync<IdDto>())!.Id;

        var g1 = await GroupAsync(superAdmin, branchId, courseId, teacherId);
        var g2 = await GroupAsync(superAdmin, branchId, courseId, teacherId);
        var s1 = withStudents ? await StudentAsync(superAdmin, g1) : null!;
        var s2 = withStudents ? await StudentAsync(superAdmin, g1) : null!;
        var s3 = withStudents ? await StudentAsync(superAdmin, g2) : null!;

        var day = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(400);
        return new Classroom(teacherId, email, branchId, day, g1, g2, s1, s2, s3,
            await ScheduleAsync(superAdmin, branchId, g1, teacherId, day, hour: 6),
            await ScheduleAsync(superAdmin, branchId, g2, teacherId, day, hour: 10));
    }

    private static Guid? sharedBranch;
    private static readonly SemaphoreSlim SharedBranchGate = new(1, 1);

    /// <summary>One branch of its own for all these tests, made on first use. The groups they create do not belong among the demo
    /// branch's: other tests ask the branch for group recommendations and expect to find their own group among the best few.</summary>
    private async Task<Guid> SharedBranchAsync()
    {
        await SharedBranchGate.WaitAsync();
        try
        {
            if (sharedBranch is { } known) return known;
            var superAdmin = await world.ClientAsync("superadmin@demo-center.test");
            var response = await superAdmin.PostAsJsonAsync("/api/branches",
                new { name = $"ZZ Workload {Guid.NewGuid():N}"[..28], address = "Test street 2", timezone = "Asia/Tashkent" }); // sorts after the demo branches
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (sharedBranch = (await response.Content.ReadFromJsonAsync<IdDto>())!.Id).Value;
        }
        finally
        {
            SharedBranchGate.Release();
        }
    }

    /// <summary>Named to sort last in the student list, which other tests read the first page of.</summary>
    private static async Task<LmsWorld.Student> StudentAsync(HttpClient superAdmin, Guid groupId)
    {
        var email = $"workload-{Guid.NewGuid():N}@demo-center.test";
        var response = await superAdmin.PostAsJsonAsync("/api/students", new
        {
            fullName = "Zz Workload Student", phone = "+998900000095", email, password = DbSeeder.DemoPassword, groupId,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return new LmsWorld.Student((await response.Content.ReadFromJsonAsync<IdDto>())!.Id, Guid.Empty, email);
    }

    private static async Task<Guid> GroupAsync(HttpClient superAdmin, Guid branchId, Guid courseId, Guid teacherId)
    {
        var group = await superAdmin.PostAsJsonAsync("/api/groups", new { branchId, courseId, name = $"LOAD-{Guid.NewGuid():N}", capacity = 20 });
        Assert.Equal(HttpStatusCode.Created, group.StatusCode);
        var id = (await group.Content.ReadFromJsonAsync<IdDto>())!.Id;
        Assert.Equal(HttpStatusCode.NoContent,
            (await superAdmin.PostAsJsonAsync($"/api/groups/{id}/staff", new { userId = teacherId, role = "MainTeacher" })).StatusCode);
        return id;
    }

    private static async Task<Guid> ScheduleAsync(HttpClient admin, Guid branchId, Guid groupId, Guid teacherId, DateOnly day, int hour)
    {
        var startsAt = new DateTimeOffset(day.ToDateTime(new TimeOnly(hour, 0)), TimeSpan.Zero);
        var response = await admin.PostAsJsonAsync("/api/schedule/lessons", new
        {
            branchId, groupId, teacherId, roomName = $"R-{Guid.NewGuid():N}"[..8], startsAt, endsAt = startsAt.AddHours(1),
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    private static async Task<(Guid Id, string Email)> CreateTeacherAsync(HttpClient superAdmin, Guid branchId, string roleCode)
    {
        var email = $"load-{Guid.NewGuid():N}@demo-center.test";
        var response = await superAdmin.PostAsJsonAsync("/api/users", new
        {
            // Sorts after "Teacher Demo": other tests look the demo teacher up on the first page of the name-ordered user list.
            fullName = $"Workload Teacher {Guid.NewGuid():N}"[..32], email, phone = "+998900000056",
            password = DbSeeder.DemoPassword, roleCode, branchIds = new[] { branchId },
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return ((await response.Content.ReadFromJsonAsync<IdDto>())!.Id, email);
    }

    private async Task MarkAsync(Guid lessonId, params (Guid StudentId, string Status)[] marks)
    {
        var superAdmin = await world.ClientAsync("superadmin@demo-center.test");
        var response = await superAdmin.PostAsJsonAsync($"/api/lessons/{lessonId}/attendance",
            new { items = marks.Select(m => new { studentId = m.StudentId, status = m.Status }) });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<Guid> HomeworkAsync(HttpClient admin, Guid lessonId)
    {
        var response = await admin.PostAsJsonAsync("/api/homework", new
        {
            lessonId, title = "Workload homework", type = "Custom", deadline = DateTimeOffset.UtcNow.AddDays(500), maxScore = 10,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    /// <summary>Puts an answer straight into the table, with the wait between handing in and being marked set exactly: the API stamps both
    /// with the clock, so through it every wait would be a few milliseconds.</summary>
    private async Task SubmitAsync(Guid homeworkId, Guid studentId, TimeSpan? reviewedAfter)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var organizationId = await db.Homework.Where(h => h.Id == homeworkId).Select(h => h.OrganizationId).SingleAsync();
        var submission = new HomeworkSubmission(organizationId, homeworkId, studentId, null, "My answer");
        db.HomeworkSubmissions.Add(submission);
        if (reviewedAfter is { } wait)
        {
            var handedIn = DateTimeOffset.UtcNow.AddDays(-3);
            submission.Review(9, null, Guid.NewGuid());
            db.Entry(submission).Property(s => s.SubmittedAt).CurrentValue = handedIn;
            db.Entry(submission).Property(s => s.ReviewedAt).CurrentValue = handedIn + wait;
        }
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> ExamAsync(HttpClient admin, Guid groupId, DateOnly date)
    {
        var response = await admin.PostAsJsonAsync("/api/exams", new { groupId, name = $"Load exam {date:yyyy-MM-dd}", type = "Unit", date });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    private static async Task ResultAsync(HttpClient admin, Guid examId, Guid studentId, decimal overall) =>
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync($"/api/exams/{examId}/results", new { studentId, overall })).StatusCode);

    private async Task<Guid> UserIdAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
    }

    /// <summary>The window that holds the lessons (on <see cref="Classroom.Day"/>) and both months of exams.</summary>
    private static async Task<Workload> WorkloadAsync(HttpClient client, Classroom w)
    {
        var from = w.Day.AddMonths(-2);
        var to = w.Day.AddDays(1);
        var response = await client.GetAsync($"/api/teachers/{w.TeacherId}/workload?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Workload>())!;
    }

    private static string Month(DateOnly date) => $"{date.Year:D4}-{date.Month:D2}";

    private record IdDto(Guid Id);
    private record BranchDto(Guid Id, string Name);
    private record Page<T>(List<T> Items);
    private record AuditRow(Guid UserId);
    private record Summary(string Email, List<string> Branches, DateTimeOffset JoinedAt, int GroupsCount, int ActiveStudents);
    private record Link(Guid UserId, Guid GroupId);
    private record SupportLinks(List<Link> SupportTeachers, List<Link> AssistsIn);
    private record GroupAttendance(Guid GroupId, int LessonsHeld, int LessonsCancelled, int Attended, int Counted, double AttendanceRate);
    private record GroupHomework(Guid GroupId, int Created, int PendingReview, double? AverageReviewHours);
    private record Month_(string Month, decimal Average, int Results);
    private record GroupExams(Guid GroupId, int Exams, int Results, decimal? AverageOverall, List<Month_> Trend);
    private record Workload(
        DateOnly From, DateOnly To, int LessonsHeld, int LessonsCancelled, double AttendanceRate,
        int HomeworkCreated, int HomeworkPendingReview, double? AverageReviewHours,
        List<GroupAttendance> Attendance, List<GroupHomework> Homework, List<GroupExams> Exams);
}
