using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduCenter.Infrastructure.Seed;
using EduCenter.Infrastructure;
using EduCenter.Modules.Academic.Domain;
using EduCenter.Modules.Lms.Domain;
using EduCenter.Modules.Students.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// End-to-end: group staffing → lesson → enrollment → attendance → homework → submission → review.
/// This is also the regression test for two real bugs found while building this flow by hand:
///  1. POST /api/groups/{id}/staff threw DbUpdateConcurrencyException — a child entity with its own
///     Guid Id, appended to an already-tracked parent's collection navigation (not through a direct
///     DbSet.Add), was misread by EF as an existing row and updated instead of inserted. Fixed by
///     marking every entity's Id column ValueGeneratedNever in AppDbContext.
///  2. GET /api/homework (Self scope) failed to translate a LINQ query built over a ValueTuple of two
///     joined entities. Fixed by projecting to a flat anonymous type instead.
/// </summary>
[Collection("Api")]
public class LmsTests(EduCenterWebApplicationFactory factory)
{
    [Fact]
    public async Task FullLessonLifecycle_AttendanceHomeworkSubmissionReview_WorksEndToEnd()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var branches = await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches");
        var branchId = branches![0].Id;

        var course = await (await admin.PostAsJsonAsync("/api/courses",
            new { name = "LMS Test Course", levelCategory = "A1" })).Content.ReadFromJsonAsync<CourseDto>();
        var group = await (await admin.PostAsJsonAsync("/api/groups",
            new { branchId, courseId = course!.Id, name = $"LMS-{Guid.NewGuid():N}", capacity = 10 }))
            .Content.ReadFromJsonAsync<GroupDto>();

        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var teacherUserId = (await superAdmin.GetFromJsonAsync<PagedResponse<UserDto>>("/api/users?role=TEACHER"))!.Items
            .First(u => u.Email == "teacher@demo-center.test").Id;

        // Regression check for bug #1: this used to 500.
        var assignResponse = await superAdmin.PostAsJsonAsync($"/api/groups/{group!.Id}/staff",
            new { userId = teacherUserId, role = "MainTeacher" });
        Assert.Equal(HttpStatusCode.NoContent, assignResponse.StatusCode);

        // A unique far-future slot: the suite runs against a persistent dev database, and the conflict
        // detector (correctly) rejects a second lesson for the same teacher in an overlapping window.
        var startsAt = DateTimeOffset.UtcNow.Date.AddDays(Random.Shared.Next(400, 4000)).AddMinutes(Random.Shared.Next(0, 1440));
        var lessonResponse = await superAdmin.PostAsJsonAsync("/api/schedule/lessons", new
        {
            branchId,
            groupId = group.Id,
            teacherId = teacherUserId,
            roomName = $"R-{Guid.NewGuid():N}"[..8],
            startsAt,
            endsAt = startsAt.AddHours(1)
        });
        Assert.Equal(HttpStatusCode.Created, lessonResponse.StatusCode);
        var lesson = await lessonResponse.Content.ReadFromJsonAsync<LessonDto>();

        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        var lead = await (await operatorClient.PostAsJsonAsync("/api/leads", new
        {
            branchId,
            fullName = "LMS Test Student",
            phone = "+998900000077",
            source = "Website",
            interestedCourse = "A1"
        })).Content.ReadFromJsonAsync<LeadDto>();

        var studentEmail = $"lms-test-{Guid.NewGuid():N}@demo-center.test";
        var student = await (await operatorClient.PostAsJsonAsync($"/api/leads/{lead!.Id}/convert", new
        {
            groupId = group.Id,
            email = studentEmail,
            password = DbSeeder.DemoPassword
        })).Content.ReadFromJsonAsync<StudentDto>();

        var teacher = await AuthenticatedClientAsync("teacher@demo-center.test");

        var attendanceResponse = await teacher.PostAsJsonAsync($"/api/lessons/{lesson!.Id}/attendance",
            new { items = new[] { new { studentId = student!.Id, status = "Present" } } });
        Assert.Equal(HttpStatusCode.NoContent, attendanceResponse.StatusCode);

        var homework = await (await teacher.PostAsJsonAsync("/api/homework", new
        {
            lessonId = lesson.Id,
            title = "LMS Test Homework",
            description = "Write something.",
            type = "Writing",
            deadline = DateTimeOffset.UtcNow.AddDays(7),
            maxScore = 100
        })).Content.ReadFromJsonAsync<HomeworkDto>();

        var studentClient = factory.CreateClient();
        var studentLogin = await studentClient.PostAsJsonAsync("/api/auth/login",
            new { email = studentEmail, password = DbSeeder.DemoPassword });
        var studentTokens = await studentLogin.Content.ReadFromJsonAsync<TokenResponseDto>();
        studentClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", studentTokens!.AccessToken);

        // Regression check for bug #2: this used to 500 (LINQ translation failure on Self scope).
        var visibleHomework = await studentClient.GetFromJsonAsync<List<HomeworkDto>>("/api/homework");
        Assert.Contains(visibleHomework!, h => h.Id == homework!.Id);

        // The teacher opens the attached link from their own signed-in session: anything but plain http(s) would run there.
        foreach (var hostile in new[] { "javascript:alert(document.domain)", "data:text/html,<script>1</script>", "//evil.example/x", "ftp://example.com/f" })
        {
            var refused = await studentClient.PostAsJsonAsync($"/api/homework/{homework!.Id}/submissions",
                new { contentText = "My submission text.", contentUrl = hostile });
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        }

        var submission = await (await studentClient.PostAsJsonAsync($"/api/homework/{homework!.Id}/submissions",
            new { contentText = "My submission text.", contentUrl = "https://drive.google.com/file/d/abc/view" })).Content.ReadFromJsonAsync<SubmissionDto>();
        Assert.NotNull(submission);
        Assert.Null(submission!.Score);

        var reviewResponse = await teacher.PatchAsJsonAsync($"/api/homework/submissions/{submission.Id}/review",
            new { score = 90, feedback = "Well done." });
        Assert.Equal(HttpStatusCode.NoContent, reviewResponse.StatusCode);

        var submissions = await teacher.GetFromJsonAsync<List<SubmissionDto>>($"/api/homework/{homework.Id}/submissions");
        var reviewed = Assert.Single(submissions!);
        Assert.Equal(90, reviewed.Score);

        var resourceResponse = await teacher.PostAsJsonAsync("/api/resources", new
        {
            lessonId = lesson.Id, title = "Reading material", url = "https://example.com/reading", description = "Read before class."
        });
        Assert.Equal(HttpStatusCode.Created, resourceResponse.StatusCode);
        var resource = await resourceResponse.Content.ReadFromJsonAsync<ResourceRow>();
        var visibleResources = await studentClient.GetFromJsonAsync<List<ResourceRow>>($"/api/resources?lessonId={lesson.Id}");
        Assert.Equal(resource!.Id, Assert.Single(visibleResources!).Id);
        var deniedResource = await studentClient.PostAsJsonAsync("/api/resources", new
        {
            lessonId = lesson.Id, title = "Not allowed", url = "https://example.com/"
        });
        Assert.Equal(HttpStatusCode.Forbidden, deniedResource.StatusCode);
        var unsafeResource = await teacher.PostAsJsonAsync("/api/resources", new
        {
            lessonId = lesson.Id, title = "Invalid URL", url = "javascript:alert(1)"
        });
        Assert.Equal(HttpStatusCode.BadRequest, unsafeResource.StatusCode);

        var profileAttendance = await studentClient.GetFromJsonAsync<List<ProfileAttendance>>($"/api/students/{student.Id}/attendance");
        Assert.Equal("Present", Assert.Single(profileAttendance!).Status);
        var profileHomework = await studentClient.GetFromJsonAsync<List<ProfileHomework>>($"/api/students/{student.Id}/homework");
        var profileRow = Assert.Single(profileHomework!);
        Assert.Equal(homework.Id, profileRow.Id);
        Assert.Equal(90, profileRow.Score);
        Assert.Equal("Well done.", profileRow.Feedback);

        // The same student also studies in a group this teacher does not staff. Student-level
        // access alone must not expose that group's attendance, homework or exam results.
        using (var serviceScope = factory.Services.CreateScope())
        {
            var db = serviceScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var organizationId = db.Students.Single(s => s.Id == student.Id).OrganizationId;
            var hiddenGroup = new Group(organizationId, branchId, course.Id, "Other teacher group", 10);
            var hiddenLesson = new Lesson(organizationId, branchId, hiddenGroup.Id, teacherUserId,
                "Hidden", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1));
            var hiddenHomework = new Homework(organizationId, hiddenLesson.Id, "Private homework", null,
                HomeworkType.Writing, DateTimeOffset.UtcNow.AddDays(1), 100);
            var hiddenExam = new Exam(organizationId, hiddenGroup.Id, "Private exam", ExamType.Unit,
                DateOnly.FromDateTime(DateTime.UtcNow));
            db.AddRange(hiddenGroup, hiddenLesson, hiddenHomework, hiddenExam,
                LearningResource.ForLink(organizationId, hiddenLesson.Id, teacherUserId, "Hidden resource", "https://example.com/hidden", null),
                new Enrollment(organizationId, student.Id, hiddenGroup.Id),
                new Attendance(organizationId, hiddenLesson.Id, student.Id, AttendanceStatus.Absent),
                new ExamResult(organizationId, hiddenExam.Id, student.Id, 5, 5, 5, 5, 5));
            await db.SaveChangesAsync();
            var hiddenResources = await teacher.GetFromJsonAsync<List<ResourceRow>>($"/api/resources?lessonId={hiddenLesson.Id}");
            Assert.Empty(hiddenResources!);
            var forbiddenResource = await teacher.PostAsJsonAsync("/api/resources", new
            {
                lessonId = hiddenLesson.Id, title = "Not my group", url = "https://example.com/"
            });
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenResource.StatusCode);
        }
        var teacherAttendance = await teacher.GetFromJsonAsync<List<ProfileAttendance>>($"/api/students/{student.Id}/attendance");
        Assert.Single(teacherAttendance!);
        var teacherHomework = await teacher.GetFromJsonAsync<List<ProfileHomework>>($"/api/students/{student.Id}/homework");
        Assert.Single(teacherHomework!);
        var teacherExams = await teacher.GetFromJsonAsync<List<object>>($"/api/students/{student.Id}/exams");
        Assert.Empty(teacherExams!);
        foreach (var tab in new[] { "attendance", "homework", "exams" })
        {
            var denied = await studentClient.GetAsync($"/api/students/{Guid.NewGuid()}/{tab}");
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
    }

    private async Task<HttpClient> AuthenticatedClientAsync(string email)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = DbSeeder.DemoPassword });
        var tokens = await response.Content.ReadFromJsonAsync<TokenResponseDto>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        return client;
    }

    private record TokenResponseDto(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken);
    private record ProfileAttendance(string Status);
    private record ResourceRow(Guid Id);
    private record ProfileHomework(Guid Id, int? Score, string? Feedback);
    private record BranchDto(Guid Id, string Name, string Address, string Timezone);
    private record CourseDto(Guid Id, string Name, string LevelCategory);
    private record GroupDto(Guid Id, Guid BranchId, Guid CourseId, string Name, int Capacity, string Status, object[] Staff);
    private record UserDto(Guid Id, string FullName, string Email, string Phone, string Status, List<string> Roles, List<Guid> BranchIds);
    private record LessonDto(Guid Id, Guid GroupId, Guid TeacherId, string RoomName, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? Topic);
    private record LeadDto(Guid Id, Guid BranchId, string FullName, string Phone, string Source,
        string InterestedCourse, Guid OperatorId, string Status, string? Notes, string? LostReason,
        Guid? ConvertedStudentId, DateTimeOffset CreatedAt);
    private record StudentDto(Guid Id, Guid UserId, string StudentNumber, DateOnly JoinedDate, string? CurrentLevel, string Status);
    private record HomeworkDto(Guid Id, Guid LessonId, string Title, string? Description, string Type, DateTimeOffset Deadline, int MaxScore);
    private record SubmissionDto(Guid Id, Guid HomeworkId, Guid StudentId, string? ContentUrl, string? ContentText,
        DateTimeOffset SubmittedAt, int? Score, string? Feedback, DateTimeOffset? ReviewedAt);
}
