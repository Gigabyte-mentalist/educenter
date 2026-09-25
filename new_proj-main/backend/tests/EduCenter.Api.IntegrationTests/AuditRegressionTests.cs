using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduCenter.Infrastructure.Seed;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// Regression tests for the findings of the full-codebase audit (see backend/README.md "Audit").
/// Each test pins one hole that the audit closed:
///   1. Privilege escalation — a branch Administrator could create a SUPER_ADMIN.
///   2. Cross-branch write — an Administrator could edit users of a branch they don't belong to.
///   3. AllowedBranchIds == null used to mean "Forbid" for group-scoped roles, so teachers and
///      students got 403 on /api/schedule and /api/groups instead of their own groups.
///   4. A student could submit homework to any group in the organization.
///   5. Duplicate staff assignment surfaced as a 500 (unique-index violation) instead of a 409.
/// </summary>
[Collection("Api")]
public class AuditRegressionTests(EduCenterWebApplicationFactory factory)
{
    [Fact]
    public async Task Administrator_CannotCreateSuperAdmin()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");

        var response = await admin.PostAsJsonAsync("/api/users", new
        {
            fullName = "Escalation Attempt",
            email = $"escalate-{Guid.NewGuid():N}@demo-center.test",
            phone = "+998900000099",
            password = DbSeeder.DemoPassword,
            roleCode = "SUPER_ADMIN",
            branchIds = Array.Empty<Guid>()
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Administrator_CannotEditUserOfAnotherBranch()
    {
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var branches = await superAdmin.GetFromJsonAsync<List<BranchDto>>("/api/branches");
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var adminBranch = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var otherBranch = branches!.First(b => b.Id != adminBranch).Id;

        var created = await (await superAdmin.PostAsJsonAsync("/api/users", new
        {
            fullName = "Other Branch Teacher",
            email = $"other-branch-{Guid.NewGuid():N}@demo-center.test",
            phone = "+998900000098",
            password = DbSeeder.DemoPassword,
            roleCode = "TEACHER",
            branchIds = new[] { otherBranch }
        })).Content.ReadFromJsonAsync<UserDto>();

        var response = await admin.PatchAsJsonAsync($"/api/users/{created!.Id}", new { fullName = "Hijacked", phone = "+998900000097" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Teacher_CanListScheduleAndGroups()
    {
        // Teacher's schedule.view / groups.view grants are MyGroups-scoped: the list must succeed and
        // contain only groups the teacher is staffed on (never a 403).
        var teacher = await AuthenticatedClientAsync("teacher@demo-center.test");

        var scheduleResponse = await teacher.GetAsync("/api/schedule");
        Assert.Equal(HttpStatusCode.OK, scheduleResponse.StatusCode);

        var groupsResponse = await teacher.GetAsync("/api/groups");
        Assert.Equal(HttpStatusCode.OK, groupsResponse.StatusCode);

        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var teacherId = (await superAdmin.GetFromJsonAsync<PagedResponse<UserDto>>("/api/users?role=TEACHER"))!.Items
            .First(u => u.Email == "teacher@demo-center.test").Id;

        var groups = await groupsResponse.Content.ReadFromJsonAsync<List<GroupDto>>();
        Assert.All(groups!, g => Assert.Contains(g.Staff, s => s.UserId == teacherId));
    }

    [Fact]
    public async Task Student_CannotSubmitHomeworkOfAnotherGroup()
    {
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var branchId = (await superAdmin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var teacherId = (await superAdmin.GetFromJsonAsync<PagedResponse<UserDto>>("/api/users?role=TEACHER"))!.Items
            .First(u => u.Email == "teacher@demo-center.test").Id;

        var course = await (await superAdmin.PostAsJsonAsync("/api/courses",
            new { name = "Audit Course", levelCategory = "IELTS" })).Content.ReadFromJsonAsync<CourseDto>();

        // Group A: where the student is enrolled. Group B: where the homework lives.
        var groupA = await (await superAdmin.PostAsJsonAsync("/api/groups",
            new { branchId, courseId = course!.Id, name = $"AUD-A-{Guid.NewGuid():N}", capacity = 5 }))
            .Content.ReadFromJsonAsync<GroupDto>();
        var group = await (await superAdmin.PostAsJsonAsync("/api/groups",
            new { branchId, courseId = course.Id, name = $"AUD-B-{Guid.NewGuid():N}", capacity = 5 }))
            .Content.ReadFromJsonAsync<GroupDto>();
        await superAdmin.PostAsJsonAsync($"/api/groups/{group!.Id}/staff", new { userId = teacherId, role = "MainTeacher" });

        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        var lead = await (await operatorClient.PostAsJsonAsync("/api/leads", new
        {
            branchId, fullName = "Audit Outsider", phone = "+998900000096", source = "Website", interestedCourse = "IELTS"
        })).Content.ReadFromJsonAsync<LeadDto>();
        var studentEmail = $"audit-outsider-{Guid.NewGuid():N}@demo-center.test";
        var convert = await operatorClient.PostAsJsonAsync($"/api/leads/{lead!.Id}/convert",
            new { groupId = groupA!.Id, email = studentEmail, password = DbSeeder.DemoPassword });
        Assert.Equal(HttpStatusCode.Created, convert.StatusCode);

        // Unique far-future slot — the suite shares a persistent dev DB with earlier runs.
        var startsAt = DateTimeOffset.UtcNow.Date.AddDays(Random.Shared.Next(400, 4000)).AddMinutes(Random.Shared.Next(0, 1440));
        var lessonResponse = await superAdmin.PostAsJsonAsync("/api/schedule/lessons", new
        {
            branchId, groupId = group.Id, teacherId, roomName = $"R-{Guid.NewGuid():N}"[..8],
            startsAt, endsAt = startsAt.AddMinutes(60), topic = "Audit lesson"
        });
        Assert.Equal(HttpStatusCode.Created, lessonResponse.StatusCode);
        var lesson = await lessonResponse.Content.ReadFromJsonAsync<LessonDto>();

        var teacher = await AuthenticatedClientAsync("teacher@demo-center.test");
        var homeworkResponse = await teacher.PostAsJsonAsync("/api/homework", new
        {
            lessonId = lesson!.Id, title = "Audit HW", description = (string?)null, type = "Writing",
            deadline = DateTimeOffset.UtcNow.AddDays(31), maxScore = 100
        });
        Assert.Equal(HttpStatusCode.Created, homeworkResponse.StatusCode);
        var homework = await homeworkResponse.Content.ReadFromJsonAsync<HomeworkDto>();

        var student = await AuthenticatedClientAsync(studentEmail);
        var submit = await student.PostAsJsonAsync($"/api/homework/{homework!.Id}/submissions",
            new { contentText = "I am not in this group." });

        Assert.Equal(HttpStatusCode.Forbidden, submit.StatusCode);
    }

    [Fact]
    public async Task Student_ListsOnlyExamsOfOwnGroups()
    {
        // Regression: the Self-scoped exam list 500'd (OrderBy over a projected DTO is untranslatable)
        // and, before that, returned every exam in the organization.
        var student = await AuthenticatedClientAsync("student@demo-center.test");

        var groupsResponse = await student.GetAsync("/api/groups");
        Assert.Equal(HttpStatusCode.OK, groupsResponse.StatusCode);
        var ownGroupIds = (await groupsResponse.Content.ReadFromJsonAsync<List<GroupDto>>())!.Select(g => g.Id).ToHashSet();

        var examsResponse = await student.GetAsync("/api/exams");
        Assert.Equal(HttpStatusCode.OK, examsResponse.StatusCode);
        var exams = (await examsResponse.Content.ReadFromJsonAsync<PagedResponse<ExamDto>>())!.Items;

        Assert.All(exams, e => Assert.Contains(e.GroupId, ownGroupIds));
    }

    [Fact]
    public async Task AssigningSameStaffTwice_ReturnsConflictNotServerError()
    {
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var branchId = (await superAdmin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var teacherId = (await superAdmin.GetFromJsonAsync<PagedResponse<UserDto>>("/api/users?role=TEACHER"))!.Items
            .First(u => u.Email == "teacher@demo-center.test").Id;

        var course = await (await superAdmin.PostAsJsonAsync("/api/courses",
            new { name = "Audit Course 2", levelCategory = "IELTS" })).Content.ReadFromJsonAsync<CourseDto>();
        var group = await (await superAdmin.PostAsJsonAsync("/api/groups",
            new { branchId, courseId = course!.Id, name = $"DUP-{Guid.NewGuid():N}", capacity = 5 }))
            .Content.ReadFromJsonAsync<GroupDto>();

        var first = await superAdmin.PostAsJsonAsync($"/api/groups/{group!.Id}/staff", new { userId = teacherId, role = "MainTeacher" });
        var second = await superAdmin.PostAsJsonAsync($"/api/groups/{group.Id}/staff", new { userId = teacherId, role = "MainTeacher" });

        Assert.True(first.IsSuccessStatusCode, $"First assignment should succeed, got {first.StatusCode}");
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
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
    private record BranchDto(Guid Id, string Name, string Address, string Timezone);
    private record UserDto(Guid Id, string FullName, string Email, string Phone, string Status, List<string> Roles, List<Guid> BranchIds);
    private record CourseDto(Guid Id, string Name, string LevelCategory);
    private record GroupStaffDto(Guid UserId, string Role);
    private record GroupDto(Guid Id, Guid BranchId, Guid CourseId, string Name, int Capacity, string Status, List<GroupStaffDto> Staff);
    private record LessonDto(Guid Id, Guid GroupId, Guid TeacherId, string RoomName, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? Topic);
    private record HomeworkDto(Guid Id, Guid LessonId, string Title, string? Description, string Type, DateTimeOffset Deadline, int MaxScore);
    private record ExamDto(Guid Id, Guid GroupId, string Name, string Type, DateOnly Date);
    private record LeadDto(Guid Id, Guid BranchId, string FullName, string Phone, string Source,
        string InterestedCourse, Guid OperatorId, string Status, string? Notes, string? LostReason,
        Guid? ConvertedStudentId, DateTimeOffset CreatedAt);
}
