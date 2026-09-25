using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduCenter.Infrastructure.Seed;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// Linking a guardian account to a student (TZ §24 Parent Portal) — the account either already exists (a second
/// child for someone already signed up) or is created on the spot; either way the join is <c>StudentParent</c> and
/// a <c>Parent</c> row is reused when the account already has one. Also closes the hole this depended on:
/// <c>POST /api/users</c> used to create a login with a STUDENT or PARENT role and nothing else — no <c>Student</c>
/// or <c>Parent</c> row for the rest of the app (dashboard, scope) to join against.
/// </summary>
[Collection("Api")]
public class StudentParentsTests(EduCenterWebApplicationFactory factory)
{
    [Fact]
    public async Task Link_NewAccount_LogsInAndSeesChildOnParentDashboard_WithTimelineAndNotification()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var (studentId, _) = await RegisterInOwnBranchAsync(admin);
        var email = NewEmail();

        var response = await admin.PostAsJsonAsync($"/api/students/{studentId}/parents", new
        {
            fullName = "Yangi Ota", phone = "+998901112200", email, password = DbSeeder.DemoPassword, relationship = "Father",
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var link = (await response.Content.ReadFromJsonAsync<ParentLinkDto>())!;
        Assert.Equal("Yangi Ota", link.FullName);
        Assert.Equal("Father", link.Relationship);

        var parent = await AuthenticatedClientAsync(email);
        var dashboard = (await parent.GetFromJsonAsync<ParentDashboardDto>("/api/dashboard/parent"))!;
        Assert.Contains(dashboard.Children, c => c.StudentId == studentId);

        var timeline = (await admin.GetFromJsonAsync<List<TimelineDto>>($"/api/students/{studentId}/timeline"))!;
        Assert.Contains(timeline, e => e.EventType == "PARENT_LINKED");

        var notifications = (await parent.GetFromJsonAsync<List<NotificationDto>>("/api/notifications"))!;
        Assert.Contains(notifications, n => n.Type == "PARENT_LINKED");
    }

    [Fact]
    public async Task Link_ExistingUser_RequiresParentRole_AndRejectsADuplicateLink_ButAllowsASecondChild()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var (studentA, _) = await RegisterInOwnBranchAsync(admin);
        var (studentB, _) = await RegisterInOwnBranchAsync(admin);

        var teacherId = await UserIdAsync(admin, "teacher@demo-center.test");
        var roleReject = await admin.PostAsJsonAsync($"/api/students/{studentA}/parents", new { userId = teacherId, relationship = "Guardian" });
        Assert.Equal(HttpStatusCode.BadRequest, roleReject.StatusCode);

        var parentId = await UserIdAsync(admin, "parent@demo-center.test");
        var first = await admin.PostAsJsonAsync($"/api/students/{studentA}/parents", new { userId = parentId, relationship = "Mother" });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var duplicate = await admin.PostAsJsonAsync($"/api/students/{studentA}/parents", new { userId = parentId, relationship = "Mother" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        // The same guardian account may watch a second child — the Parent row is reused, not duplicated.
        var second = await admin.PostAsJsonAsync($"/api/students/{studentB}/parents", new { userId = parentId, relationship = "Mother" });
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        var parent = await AuthenticatedClientAsync("parent@demo-center.test");
        var dashboard = (await parent.GetFromJsonAsync<ParentDashboardDto>("/api/dashboard/parent"))!;
        Assert.Contains(dashboard.Children, c => c.StudentId == studentA);
        Assert.Contains(dashboard.Children, c => c.StudentId == studentB);
    }

    [Fact]
    public async Task Unlink_RemovesTheLink_ButKeepsTheParentRowUsable()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var (studentId, _) = await RegisterInOwnBranchAsync(admin);
        var parentId = await UserIdAsync(admin, "parent@demo-center.test");

        var link = (await (await admin.PostAsJsonAsync($"/api/students/{studentId}/parents",
            new { userId = parentId, relationship = "Guardian" })).Content.ReadFromJsonAsync<ParentLinkDto>())!;

        var parent = await AuthenticatedClientAsync("parent@demo-center.test");
        Assert.Equal(HttpStatusCode.OK, (await parent.GetAsync($"/api/students/{studentId}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.DeleteAsync($"/api/students/{studentId}/parents/{link.ParentId}")).StatusCode);

        // The link is gone — the parent no longer reaches this student...
        Assert.Equal(HttpStatusCode.Forbidden, (await parent.GetAsync($"/api/students/{studentId}")).StatusCode);

        // ...but the account's Parent row was kept, not deleted: re-linking the same user succeeds outright,
        // rather than failing the way it would if the row had vanished underneath the account.
        var relink = await admin.PostAsJsonAsync($"/api/students/{studentId}/parents", new { userId = parentId, relationship = "Guardian" });
        Assert.Equal(HttpStatusCode.Created, relink.StatusCode);
    }

    [Fact]
    public async Task Link_ByTeacher_IsForbidden()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var teacher = await AuthenticatedClientAsync("teacher@demo-center.test");
        var (studentId, _) = await RegisterInOwnBranchAsync(admin);

        var response = await teacher.PostAsJsonAsync($"/api/students/{studentId}/parents",
            new { fullName = "X", phone = "+998900000000", email = NewEmail(), password = DbSeeder.DemoPassword });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Link_And_List_StudentInAnotherBranch_ByBranchAdministrator_AreBothForbidden()
    {
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var otherGroup = await GroupInOtherBranchAsync(superAdmin, admin);

        var created = await superAdmin.PostAsJsonAsync("/api/students", ValidStudentBody(NewEmail(), otherGroup));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var studentId = (await created.Content.ReadFromJsonAsync<StudentDto>())!.Id;

        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync($"/api/students/{studentId}/parents")).StatusCode);
        var response = await admin.PostAsJsonAsync($"/api/students/{studentId}/parents",
            new { fullName = "X", phone = "+998900000000", email = NewEmail(), password = DbSeeder.DemoPassword });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UsersController_Create_RejectsStudent_AndGivesAFreshParentAnEmptyDashboard()
    {
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");

        var studentAttempt = await superAdmin.PostAsJsonAsync("/api/users", new
        {
            fullName = "Should Not Work", email = NewEmail(), phone = "+998900000000",
            password = DbSeeder.DemoPassword, roleCode = "STUDENT", branchIds = Array.Empty<Guid>(),
        });
        Assert.Equal(HttpStatusCode.BadRequest, studentAttempt.StatusCode);

        var email = NewEmail();
        var parentAttempt = await superAdmin.PostAsJsonAsync("/api/users", new
        {
            fullName = "Fresh Parent", email, phone = "+998900000001",
            password = DbSeeder.DemoPassword, roleCode = "PARENT", branchIds = Array.Empty<Guid>(),
        });
        Assert.Equal(HttpStatusCode.Created, parentAttempt.StatusCode);

        // A Parent row was written alongside the User row — the dashboard resolves instead of 404ing on a
        // missing student/parent card, and simply has no children yet.
        var parent = await AuthenticatedClientAsync(email);
        var dashboardResponse = await parent.GetAsync("/api/dashboard/parent");
        Assert.Equal(HttpStatusCode.OK, dashboardResponse.StatusCode);
        var dashboard = (await dashboardResponse.Content.ReadFromJsonAsync<ParentDashboardDto>())!;
        Assert.Empty(dashboard.Children);
    }

    [Fact]
    public async Task UsersController_AssignRole_RejectsStudent()
    {
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var supportId = await UserIdAsync(superAdmin, "support@demo-center.test");

        var response = await superAdmin.PostAsJsonAsync($"/api/users/{supportId}/roles", new { roleCode = "STUDENT" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static string NewEmail() => $"parent-link-{Guid.NewGuid():N}@demo-center.test";

    private static object ValidStudentBody(string email, Guid? groupId = null) => new
    {
        fullName = "Linked Student", phone = "+998900000088", email, password = DbSeeder.DemoPassword, groupId,
    };

    private static async Task<(Guid StudentId, Guid GroupId)> RegisterInOwnBranchAsync(HttpClient admin)
    {
        var branchId = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var course = await CreateCourseAsync(admin);
        var group = await CreateGroupAsync(admin, branchId, course, capacity: 5);
        var response = await admin.PostAsJsonAsync("/api/students", ValidStudentBody(NewEmail(), group));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return ((await response.Content.ReadFromJsonAsync<StudentDto>())!.Id, group);
    }

    private static async Task<Guid> GroupInOtherBranchAsync(HttpClient superAdmin, HttpClient admin)
    {
        var mine = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))!.Select(b => b.Id).ToHashSet();
        var other = (await superAdmin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))!.First(b => !mine.Contains(b.Id)).Id;
        return await CreateGroupAsync(superAdmin, other, await CreateCourseAsync(superAdmin), capacity: 5);
    }

    private static async Task<Guid> CreateCourseAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/courses",
            new { name = $"Parent Link Course {Guid.NewGuid():N}"[..26], levelCategory = "IELTS", monthlyFee = 0 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CourseDto>())!.Id;
    }

    private static async Task<Guid> CreateGroupAsync(HttpClient client, Guid branchId, Guid courseId, int capacity)
    {
        var response = await client.PostAsJsonAsync("/api/groups",
            new { branchId, courseId, name = $"PL-{Guid.NewGuid():N}", capacity });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<GroupDto>())!.Id;
    }

    private static async Task<Guid> UserIdAsync(HttpClient client, string email)
    {
        var page = (await client.GetFromJsonAsync<PagedResponse<UserRowDto>>($"/api/users?search={Uri.EscapeDataString(email)}"))!;
        return page.Items.Single(u => u.Email == email).Id;
    }

    /// <summary>An account created through the API holds a temporary password, so its first real session starts with
    /// the forced change (S01.05) — done here, the way a person signing in for the first time would.</summary>
    private async Task<HttpClient> AuthenticatedClientAsync(string email)
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
    private record BranchDto(Guid Id, string Name);
    private record CourseDto(Guid Id, string Name);
    private record GroupDto(Guid Id);
    private record StudentDto(Guid Id, Guid UserId, string FullName);
    private record UserRowDto(Guid Id, string Email);
    private record ParentLinkDto(Guid ParentId, Guid UserId, string FullName, string Phone, string Email, string Relationship);
    private record ParentChildDto(Guid StudentId, string FullName);
    private record ParentDashboardDto(List<ParentChildDto> Children);
    private record TimelineDto(string EventType);
    private record NotificationDto(Guid Id, string Type);
}
