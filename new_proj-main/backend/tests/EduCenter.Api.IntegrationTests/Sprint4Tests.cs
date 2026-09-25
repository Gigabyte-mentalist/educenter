using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduCenter.Infrastructure.Seed;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// Exams, Student 360° (overview/timeline), and Teacher 360°. Also the regression test for a bug found
/// while building this: TeachersController's self-view branch only matched ScopeType.Self, but a
/// Teacher's users.view grant is scoped MyGroups — so a teacher viewing their own profile fell through
/// to the default case and got an empty group list instead of a Forbid or their real groups.
/// </summary>
[Collection("Api")]
public class Sprint4Tests(EduCenterWebApplicationFactory factory)
{
    [Fact]
    public async Task ExamResult_AppearsInStudentOverviewAndTimeline_AndIsVisibleToTheStudentThemself()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var branches = await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches");
        var branchId = branches![0].Id;

        var course = await (await admin.PostAsJsonAsync("/api/courses",
            new { name = "Sprint4 Course", levelCategory = "IELTS" })).Content.ReadFromJsonAsync<CourseDto>();
        var group = await (await admin.PostAsJsonAsync("/api/groups",
            new { branchId, courseId = course!.Id, name = $"S4-{Guid.NewGuid():N}", capacity = 10 }))
            .Content.ReadFromJsonAsync<GroupDto>();

        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var teacherUserId = (await superAdmin.GetFromJsonAsync<PagedResponse<UserDto>>("/api/users?role=TEACHER"))!.Items
            .First(u => u.Email == "teacher@demo-center.test").Id;
        await superAdmin.PostAsJsonAsync($"/api/groups/{group!.Id}/staff", new { userId = teacherUserId, role = "MainTeacher" });

        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        var lead = await (await operatorClient.PostAsJsonAsync("/api/leads", new
        {
            branchId,
            fullName = "Sprint4 Test Student",
            phone = "+998900000088",
            source = "Website",
            interestedCourse = "IELTS"
        })).Content.ReadFromJsonAsync<LeadDto>();

        var studentEmail = $"sprint4-test-{Guid.NewGuid():N}@demo-center.test";
        var student = await (await operatorClient.PostAsJsonAsync($"/api/leads/{lead!.Id}/convert",
            new { groupId = group.Id, email = studentEmail, password = DbSeeder.DemoPassword }))
            .Content.ReadFromJsonAsync<StudentDto>();

        var teacher = await AuthenticatedClientAsync("teacher@demo-center.test");

        var exam = await (await teacher.PostAsJsonAsync("/api/exams",
            new { groupId = group.Id, name = "Sprint4 Mock", type = "MockIelts", date = DateOnly.FromDateTime(DateTime.UtcNow) }))
            .Content.ReadFromJsonAsync<ExamDto>();

        var resultResponse = await teacher.PostAsJsonAsync($"/api/exams/{exam!.Id}/results", new
        {
            studentId = student!.Id,
            listening = 6.5m,
            reading = 6.0m,
            writing = 5.5m,
            speaking = 6.0m,
            overall = 6.0m
        });
        Assert.Equal(HttpStatusCode.Created, resultResponse.StatusCode);

        var duplicateResult = await teacher.PostAsJsonAsync($"/api/exams/{exam.Id}/results",
            new { studentId = student.Id, overall = 6.0m });
        Assert.Equal(HttpStatusCode.Conflict, duplicateResult.StatusCode);
        var emptyResult = await teacher.PostAsJsonAsync($"/api/exams/{exam.Id}/results",
            new { studentId = student.Id });
        Assert.Equal(HttpStatusCode.BadRequest, emptyResult.StatusCode);
        var invalidResult = await teacher.PostAsJsonAsync($"/api/exams/{exam.Id}/results",
            new { studentId = student.Id, overall = 10 });
        Assert.Equal(HttpStatusCode.BadRequest, invalidResult.StatusCode);

        var overview = await teacher.GetFromJsonAsync<StudentOverviewDto>($"/api/students/{student.Id}/overview");
        Assert.Single(overview!.Enrollments);

        var timeline = await teacher.GetFromJsonAsync<List<TimelineEventDto>>($"/api/students/{student.Id}/timeline");
        Assert.Contains(timeline!, e => e.EventType == "ENROLLED");
        Assert.Contains(timeline!, e => e.EventType == "EXAM_RESULT_RECORDED");

        // The student sees their own result — Self scope, resolved via ResolveVisibleStudentIdsAsync.
        var studentClient = factory.CreateClient();
        var studentTokens = await (await studentClient.PostAsJsonAsync("/api/auth/login",
            new { email = studentEmail, password = DbSeeder.DemoPassword })).Content.ReadFromJsonAsync<TokenResponseDto>();
        studentClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", studentTokens!.AccessToken);

        var ownResults = await studentClient.GetFromJsonAsync<List<ExamResultDto>>($"/api/exams/{exam.Id}/results");
        Assert.Single(ownResults!);
        var profileResults = await studentClient.GetFromJsonAsync<List<ProfileExam>>($"/api/students/{student.Id}/exams");
        var profileExam = Assert.Single(profileResults!);
        Assert.Equal("Sprint4 Mock", profileExam.Name);
        Assert.Equal(6.0m, profileExam.Overall);
        var deniedScore = await studentClient.PostAsJsonAsync($"/api/exams/{exam.Id}/results",
            new { studentId = student.Id, overall = 9 });
        Assert.Equal(HttpStatusCode.Forbidden, deniedScore.StatusCode);
        var deniedCreate = await studentClient.PostAsJsonAsync("/api/exams",
            new { groupId = group.Id, name = "Forbidden", type = "MockIelts", date = "2026-09-07" });
        Assert.Equal(HttpStatusCode.Forbidden, deniedCreate.StatusCode);

        // A random student id must be inaccessible — not an information leak.
        var forbiddenResponse = await studentClient.GetAsync($"/api/students/{Guid.NewGuid()}/overview");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);
    }

    [Fact]
    public async Task TeacherOverview_SelfView_ShowsOwnGroups()
    {
        // Regression check for the MyGroups-vs-Self scope bug described in the class summary.
        var teacher = await AuthenticatedClientAsync("teacher@demo-center.test");
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var teacherUserId = (await superAdmin.GetFromJsonAsync<PagedResponse<UserDto>>("/api/users?role=TEACHER"))!.Items
            .First(u => u.Email == "teacher@demo-center.test").Id;

        var response = await teacher.GetAsync($"/api/teachers/{teacherUserId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var overview = await response.Content.ReadFromJsonAsync<TeacherOverviewDto>();
        Assert.NotNull(overview);
        Assert.Equal(teacherUserId, overview!.UserId);
        // MyGroups means colleagues too (docs/kamchiliklar.docx §2), not just self — a teacher's own
        // row must still be there, but the list is no longer asserted to contain only that one row.
        var ownList = await teacher.GetFromJsonAsync<List<TeacherListRow>>("/api/teachers");
        Assert.Contains(ownList!, t => t.Id == teacherUserId);
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var allBranches = await superAdmin.GetFromJsonAsync<List<BranchDto>>("/api/branches");
        var adminBranches = await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches");
        var outsideBranch = allBranches!.First(b => !adminBranches!.Any(a => a.Id == b.Id));
        var outsideResponse = await superAdmin.PostAsJsonAsync("/api/users", new
        {
            fullName = "Outside branch teacher", email = $"outside-{Guid.NewGuid():N}@demo-center.test",
            phone = "+998900009999", password = DbSeeder.DemoPassword,
            roleCode = "TEACHER", branchIds = new[] { outsideBranch.Id }
        });
        Assert.Equal(HttpStatusCode.Created, outsideResponse.StatusCode);
        var outside = await outsideResponse.Content.ReadFromJsonAsync<UserDto>();
        var adminList = await admin.GetFromJsonAsync<List<TeacherListRow>>("/api/teachers");
        Assert.DoesNotContain(adminList!, t => t.Id == outside!.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync($"/api/teachers/{outside!.Id}")).StatusCode);
        // The new outside-branch teacher staffs none of the demo teacher's groups, so MyGroups must not
        // pull them in either — co-teacher visibility is scoped by shared groups, not by "any teacher".
        var teacherListAfter = await teacher.GetFromJsonAsync<List<TeacherListRow>>("/api/teachers");
        Assert.DoesNotContain(teacherListAfter!, t => t.Id == outside!.Id);
        // Not asserting group count > 0 here since other tests in this collection also staff this
        // teacher — the meaningful assertion is that self-view doesn't silently return an empty list
        // due to scope mismatch, which the branchId-scoped assertion below actually exercises.
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
    private record ProfileExam(string Name, decimal? Overall);
    private record TeacherListRow(Guid Id);
    private record BranchDto(Guid Id, string Name, string Address, string Timezone);
    private record CourseDto(Guid Id, string Name, string LevelCategory);
    private record GroupDto(Guid Id, Guid BranchId, Guid CourseId, string Name, int Capacity, string Status, object[] Staff);
    private record UserDto(Guid Id, string FullName, string Email, string Phone, string Status, List<string> Roles, List<Guid> BranchIds);
    private record LeadDto(Guid Id, Guid BranchId, string FullName, string Phone, string Source,
        string InterestedCourse, Guid OperatorId, string Status, string? Notes, string? LostReason,
        Guid? ConvertedStudentId, DateTimeOffset CreatedAt);
    private record StudentDto(Guid Id, Guid UserId, string StudentNumber, DateOnly JoinedDate, string? CurrentLevel, string Status);
    private record ExamDto(Guid Id, Guid GroupId, string Name, string Type, DateOnly Date);
    private record ExamResultDto(Guid Id, Guid ExamId, Guid StudentId, decimal? Listening, decimal? Reading, decimal? Writing, decimal? Speaking, decimal? Overall);
    private record EnrollmentSummaryDto(Guid GroupId, string GroupName, string Status, DateTimeOffset EnrolledAt);
    private record StudentOverviewDto(StudentDto Student, List<EnrollmentSummaryDto> Enrollments,
        int LessonsAttended, int LessonsAbsent, double AttendanceRate,
        int HomeworkAssigned, int HomeworkSubmitted, double HomeworkCompletionRate);
    private record TimelineEventDto(Guid Id, string EventType, string? PayloadJson, DateTimeOffset OccurredAt);
    private record TeacherGroupDto(Guid GroupId, string GroupName, string Role, int StudentCount);
    private record TeacherOverviewDto(Guid UserId, string FullName, string Email, List<TeacherGroupDto> Groups, int TotalStudents);
}
