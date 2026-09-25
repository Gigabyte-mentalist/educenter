using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduCenter.Infrastructure.Seed;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// Student Transfer / Freeze (docs/00_TZ.md §20-21, audit §3.5/§5.3). Both moves are enrollment-level: a
/// transfer ends one enrollment and starts another, a freeze suspends every active enrollment and follows
/// through to the student's own status. <c>student.transfer</c>/<c>student.freeze</c> stop at the branch —
/// a teacher never holds either, and a branch Administrator may not reach a student outside their branches.
/// </summary>
[Collection("Api")]
public class EnrollmentLifecycleTests(EduCenterWebApplicationFactory factory)
{
    [Fact]
    public async Task TransferTargets_ListsGroupsWithFreeSeats_ExcludesFullAndCurrentGroup()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        var branchId = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var course = await CreateCourseAsync(admin);

        var sourceGroup = await CreateGroupAsync(admin, branchId, course, capacity: 5);
        var targetOpen = await CreateGroupAsync(admin, branchId, course, capacity: 2);
        var targetFull = await CreateGroupAsync(admin, branchId, course, capacity: 1);

        var (studentId, _) = await ConvertAsync(operatorClient, branchId, sourceGroup);
        await ConvertAsync(operatorClient, branchId, targetFull); // fills targetFull to capacity

        var targets = await admin.GetFromJsonAsync<List<TransferTargetDto>>($"/api/students/{studentId}/transfer-targets");

        Assert.Contains(targets!, t => t.GroupId == targetOpen && t.FreeSeats == 2);
        Assert.DoesNotContain(targets!, t => t.GroupId == targetFull);
        Assert.DoesNotContain(targets!, t => t.GroupId == sourceGroup);
    }

    [Fact]
    public async Task Transfer_MovesStudent_EndsOldEnrollmentAndStartsNew()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        var branchId = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var course = await CreateCourseAsync(admin);

        var sourceGroup = await CreateGroupAsync(admin, branchId, course, capacity: 5);
        var targetGroup = await CreateGroupAsync(admin, branchId, course, capacity: 5);
        var (studentId, _) = await ConvertAsync(operatorClient, branchId, sourceGroup);

        var response = await admin.PostAsJsonAsync($"/api/students/{studentId}/transfer",
            new { fromGroupId = sourceGroup, toGroupId = targetGroup, reason = "Jadval mos kelmadi" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var move = await response.Content.ReadFromJsonAsync<EnrollmentMoveDto>();
        Assert.Equal("Active", move!.Status);
        Assert.Contains(move.Enrollments, e => e.GroupId == sourceGroup && e.Status == "Transferred");
        Assert.Contains(move.Enrollments, e => e.GroupId == targetGroup && e.Status == "Active");
    }

    [Fact]
    public async Task Transfer_WithoutReason_ReturnsBadRequest()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        var branchId = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var course = await CreateCourseAsync(admin);

        var sourceGroup = await CreateGroupAsync(admin, branchId, course, capacity: 5);
        var targetGroup = await CreateGroupAsync(admin, branchId, course, capacity: 5);
        var (studentId, _) = await ConvertAsync(operatorClient, branchId, sourceGroup);

        var response = await admin.PostAsJsonAsync($"/api/students/{studentId}/transfer",
            new { fromGroupId = sourceGroup, toGroupId = targetGroup, reason = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Transfer_ToFullGroup_ReturnsBadRequest()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        var branchId = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var course = await CreateCourseAsync(admin);

        var sourceGroup = await CreateGroupAsync(admin, branchId, course, capacity: 5);
        var targetFull = await CreateGroupAsync(admin, branchId, course, capacity: 1);
        var (studentId, _) = await ConvertAsync(operatorClient, branchId, sourceGroup);
        await ConvertAsync(operatorClient, branchId, targetFull);

        var response = await admin.PostAsJsonAsync($"/api/students/{studentId}/transfer",
            new { fromGroupId = sourceGroup, toGroupId = targetFull, reason = "Urinish" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Transfer_SameGroup_ReturnsBadRequest()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        var branchId = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var course = await CreateCourseAsync(admin);

        var group = await CreateGroupAsync(admin, branchId, course, capacity: 5);
        var (studentId, _) = await ConvertAsync(operatorClient, branchId, group);

        var response = await admin.PostAsJsonAsync($"/api/students/{studentId}/transfer",
            new { fromGroupId = group, toGroupId = group, reason = "Xato bosildi" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Freeze_ThenResume_RoundTripsStudentAndEnrollmentStatus()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        var branchId = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var course = await CreateCourseAsync(admin);

        var group = await CreateGroupAsync(admin, branchId, course, capacity: 5);
        var (studentId, _) = await ConvertAsync(operatorClient, branchId, group);

        var freeze = await admin.PostAsJsonAsync($"/api/students/{studentId}/freeze",
            new { reason = "Kasal bo'ldi", resumeOn = (DateOnly?)null });
        Assert.Equal(HttpStatusCode.OK, freeze.StatusCode);
        var frozen = await freeze.Content.ReadFromJsonAsync<EnrollmentMoveDto>();
        Assert.Equal("Frozen", frozen!.Status);
        Assert.Contains(frozen.Enrollments, e => e.GroupId == group && e.Status == "Frozen");

        // Nothing left active to freeze.
        var repeat = await admin.PostAsJsonAsync($"/api/students/{studentId}/freeze", new { reason = "Yana" });
        Assert.Equal(HttpStatusCode.BadRequest, repeat.StatusCode);

        var resume = await admin.PostAsJsonAsync($"/api/students/{studentId}/resume", new { groupId = (Guid?)null });
        Assert.Equal(HttpStatusCode.OK, resume.StatusCode);
        var resumed = await resume.Content.ReadFromJsonAsync<EnrollmentMoveDto>();
        Assert.Equal("Active", resumed!.Status);
        Assert.Contains(resumed.Enrollments, e => e.GroupId == group && e.Status == "Active");
    }

    [Fact]
    public async Task Freeze_WithoutReason_ReturnsBadRequest()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        var branchId = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var course = await CreateCourseAsync(admin);
        var group = await CreateGroupAsync(admin, branchId, course, capacity: 5);
        var (studentId, _) = await ConvertAsync(operatorClient, branchId, group);

        var response = await admin.PostAsJsonAsync($"/api/students/{studentId}/freeze", new { reason = "  " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Teacher_CannotTransferOrFreeze_Forbidden()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        var teacher = await AuthenticatedClientAsync("teacher@demo-center.test");
        var branchId = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var course = await CreateCourseAsync(admin);
        var group = await CreateGroupAsync(admin, branchId, course, capacity: 5);
        var other = await CreateGroupAsync(admin, branchId, course, capacity: 5);
        var (studentId, _) = await ConvertAsync(operatorClient, branchId, group);

        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync($"/api/students/{studentId}/transfer-targets")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PostAsJsonAsync($"/api/students/{studentId}/transfer",
            new { fromGroupId = group, toGroupId = other, reason = "Urinish" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PostAsJsonAsync($"/api/students/{studentId}/freeze",
            new { reason = "Urinish" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PostAsJsonAsync($"/api/students/{studentId}/resume",
            new { groupId = (Guid?)null })).StatusCode);
    }

    [Fact]
    public async Task Administrator_CannotFreezeStudentOutsideTheirBranch()
    {
        // "Nigora Sodiqova" is seeded into the Yunusobod branch; the demo Administrator's MyBranches scope
        // covers only Chilonzor (docs/09_audit_progress.md), so this must be Forbidden, not silently no-op.
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");

        // Searched for, not picked out of the first 200: the suite's own tests register students until the page is full.
        var nigora = (await superAdmin.GetFromJsonAsync<PagedResponse<StudentDto>>("/api/students?search=Nigora%20Sodiqova&size=200"))!.Items
            .Single(s => s.FullName == "Nigora Sodiqova");

        var response = await admin.PostAsJsonAsync($"/api/students/{nigora.Id}/freeze", new { reason = "Urinish" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static async Task<Guid> CreateCourseAsync(HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync("/api/courses",
            new { name = $"Lifecycle Course {Guid.NewGuid():N}"[..24], levelCategory = "IELTS" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CourseDto>())!.Id;
    }

    private static async Task<Guid> CreateGroupAsync(HttpClient admin, Guid branchId, Guid courseId, int capacity)
    {
        var response = await admin.PostAsJsonAsync("/api/groups",
            new { branchId, courseId, name = $"LC-{Guid.NewGuid():N}", capacity });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<GroupDto>())!.Id;
    }

    private static async Task<(Guid StudentId, string Email)> ConvertAsync(HttpClient operatorClient, Guid branchId, Guid groupId)
    {
        var lead = await (await operatorClient.PostAsJsonAsync("/api/leads", new
        {
            branchId, fullName = "Lifecycle Student", phone = "+998900000055", source = "Website", interestedCourse = "IELTS"
        })).Content.ReadFromJsonAsync<LeadDto>();

        var email = $"lifecycle-{Guid.NewGuid():N}@demo-center.test";
        var response = await operatorClient.PostAsJsonAsync($"/api/leads/{lead!.Id}/convert",
            new { groupId, email, password = DbSeeder.DemoPassword });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return ((await response.Content.ReadFromJsonAsync<StudentDto>())!.Id, email);
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
    private record BranchDto(Guid Id, string Name);
    private record CourseDto(Guid Id, string Name, string LevelCategory);
    private record GroupDto(Guid Id);
    private record LeadDto(Guid Id);
    private record StudentDto(Guid Id, string FullName, string Status);
    private record TransferTargetDto(Guid GroupId, string GroupName, Guid BranchId, string BranchName,
        string CourseName, int Capacity, int Enrolled, int FreeSeats, string? MainTeacher);
    private record EnrollmentSummaryDto(Guid GroupId, string GroupName, string Status, DateTimeOffset EnrolledAt);
    private record EnrollmentMoveDto(Guid StudentId, string Status, List<EnrollmentSummaryDto> Enrollments);
}
