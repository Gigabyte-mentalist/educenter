using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduCenter.Api.Automation;
using EduCenter.Infrastructure;
using EduCenter.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// The lifecycle's last moves, which had no path in before this (docs/21.09-audit-jurnali S01.03): joining a
/// second group without ending the first, leaving one with no destination, and finishing the course. All three —
/// and Transfer/Freeze/Resume, tested separately in <see cref="EnrollmentLifecycleTests"/> — now funnel the
/// student's own status through one rule (<c>StudentStatusRules.Recompute</c>) instead of each hand-setting it.
/// A trial booking or a lead conversion can no longer target a full or closed group either (previously unchecked).
/// </summary>
[Collection("Api")]
public class EnrollmentManagementTests(EduCenterWebApplicationFactory factory)
{
    [Fact]
    public async Task Enroll_SecondGroup_StudentEndsUpActiveInBoth()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var branchId = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var course = await CreateCourseAsync(admin);
        var firstGroup = await CreateGroupAsync(admin, branchId, course, capacity: 5);
        var secondGroup = await CreateGroupAsync(admin, branchId, course, capacity: 5);
        var (studentId, _) = await RegisterAsync(admin, firstGroup);

        var response = await admin.PostAsJsonAsync($"/api/students/{studentId}/enrollments", new { groupId = secondGroup });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var move = (await response.Content.ReadFromJsonAsync<EnrollmentMoveDto>())!;
        Assert.Equal("Active", move.Status);
        Assert.Contains(move.Enrollments, e => e.GroupId == firstGroup && e.Status == "Active");
        Assert.Contains(move.Enrollments, e => e.GroupId == secondGroup && e.Status == "Active");
    }

    [Fact]
    public async Task Enroll_SameGroupAgain_ReturnsConflict()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var branchId = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var group = await CreateGroupAsync(admin, branchId, await CreateCourseAsync(admin), capacity: 5);
        var (studentId, _) = await RegisterAsync(admin, group);

        var response = await admin.PostAsJsonAsync($"/api/students/{studentId}/enrollments", new { groupId = group });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Enroll_FullGroup_ReturnsConflict()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var branchId = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var course = await CreateCourseAsync(admin);
        var full = await CreateGroupAsync(admin, branchId, course, capacity: 1);
        await RegisterAsync(admin, full); // fills the only seat
        var (studentId, _) = await RegisterAsync(admin, await CreateGroupAsync(admin, branchId, course, capacity: 5));

        var response = await admin.PostAsJsonAsync($"/api/students/{studentId}/enrollments", new { groupId = full });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Enroll_ClosedGroup_ReturnsConflict()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var branchId = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var course = await CreateCourseAsync(admin);
        var closed = await CreateGroupAsync(admin, branchId, course, capacity: 5);
        await CancelGroupAsync(closed);
        var (studentId, _) = await RegisterAsync(admin, await CreateGroupAsync(admin, branchId, course, capacity: 5));

        var response = await admin.PostAsJsonAsync($"/api/students/{studentId}/enrollments", new { groupId = closed });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Drop_RequiresReason_EndsTheEnrollment_AndLeavesStudentActiveIfAnotherRemains()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var branchId = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var course = await CreateCourseAsync(admin);
        var firstGroup = await CreateGroupAsync(admin, branchId, course, capacity: 5);
        var secondGroup = await CreateGroupAsync(admin, branchId, course, capacity: 5);
        var (studentId, _) = await RegisterAsync(admin, firstGroup);
        var enrolled = (await (await admin.PostAsJsonAsync($"/api/students/{studentId}/enrollments", new { groupId = secondGroup }))
            .Content.ReadFromJsonAsync<EnrollmentMoveDto>())!;
        var firstEnrollmentId = enrolled.Enrollments.Single(e => e.GroupId == firstGroup).Id;
        var secondEnrollmentId = enrolled.Enrollments.Single(e => e.GroupId == secondGroup).Id;

        var noReason = await admin.PostAsJsonAsync($"/api/students/{studentId}/enrollments/{firstEnrollmentId}/drop", new { reason = "" });
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);

        var dropFirst = await admin.PostAsJsonAsync($"/api/students/{studentId}/enrollments/{firstEnrollmentId}/drop",
            new { reason = "Boshqa markazga o'tdi" });
        Assert.Equal(HttpStatusCode.OK, dropFirst.StatusCode);
        var afterFirst = (await dropFirst.Content.ReadFromJsonAsync<EnrollmentMoveDto>())!;
        Assert.Equal("Active", afterFirst.Status); // the second group is still active
        Assert.Contains(afterFirst.Enrollments, e => e.GroupId == firstGroup && e.Status == "Dropped");

        var dropSecond = await admin.PostAsJsonAsync($"/api/students/{studentId}/enrollments/{secondEnrollmentId}/drop",
            new { reason = "Ko'chib ketdi" });
        Assert.Equal(HttpStatusCode.OK, dropSecond.StatusCode);
        var afterSecond = (await dropSecond.Content.ReadFromJsonAsync<EnrollmentMoveDto>())!;
        Assert.Equal("Dropped", afterSecond.Status); // no open enrollment left at all

        var timeline = (await admin.GetFromJsonAsync<List<TimelineDto>>($"/api/students/{studentId}/timeline"))!;
        Assert.Contains(timeline, e => e.EventType == "ENROLLMENT_DROPPED");
    }

    [Fact]
    public async Task Graduate_CompletesOpenEnrollments_AndReEnrollingBringsTheStudentBackActive()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var branchId = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var course = await CreateCourseAsync(admin);
        var group = await CreateGroupAsync(admin, branchId, course, capacity: 5);
        var (studentId, _) = await RegisterAsync(admin, group);

        var graduate = await admin.PostAsJsonAsync($"/api/students/{studentId}/graduate", new { });
        Assert.Equal(HttpStatusCode.OK, graduate.StatusCode);
        var graduated = (await graduate.Content.ReadFromJsonAsync<EnrollmentMoveDto>())!;
        Assert.Equal("Graduated", graduated.Status);
        Assert.Contains(graduated.Enrollments, e => e.GroupId == group && e.Status == "Completed");

        // Nothing open left to graduate a second time.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/students/{studentId}/graduate", new { })).StatusCode);

        var another = await CreateGroupAsync(admin, branchId, course, capacity: 5);
        var reEnroll = await admin.PostAsJsonAsync($"/api/students/{studentId}/enrollments", new { groupId = another });
        Assert.Equal(HttpStatusCode.OK, reEnroll.StatusCode);
        Assert.Equal("Active", (await reEnroll.Content.ReadFromJsonAsync<EnrollmentMoveDto>())!.Status);

        var timeline = (await admin.GetFromJsonAsync<List<TimelineDto>>($"/api/students/{studentId}/timeline"))!;
        Assert.Contains(timeline, e => e.EventType == "GRADUATED");
    }

    [Fact]
    public async Task Drop_IssuesNoNewInvoice_OnTheNextBillingSweep()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var branchId = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var course = await CreateCourseAsync(admin, monthlyFee: 300000);
        var group = await CreateGroupAsync(admin, branchId, course, capacity: 5);
        var (studentId, enrollmentId) = await RegisterAsync(admin, group);

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/students/{studentId}/enrollments/{enrollmentId}/drop",
            new { reason = "Sinov" })).StatusCode);

        var result = await RunBillingAsync(DateTimeOffset.UtcNow.AddMonths(2), studentId);
        Assert.Equal(0, result.InvoicesIssued);
    }

    [Fact]
    public async Task DebtStudent_EnrollingIntoAnotherGroup_StaysDebt()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var branchId = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var course = await CreateCourseAsync(admin, monthlyFee: 250000);
        var group = await CreateGroupAsync(admin, branchId, course, capacity: 5);
        var (studentId, _) = await RegisterAsync(admin, group);

        await RunBillingAsync(DateTimeOffset.UtcNow.AddDays(5), studentId); // the first invoice falls overdue
        Assert.Equal("Debt", (await admin.GetFromJsonAsync<StudentDto>($"/api/students/{studentId}"))!.Status);

        var second = await CreateGroupAsync(admin, branchId, course, capacity: 5);
        var response = await admin.PostAsJsonAsync($"/api/students/{studentId}/enrollments", new { groupId = second });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Debt", (await response.Content.ReadFromJsonAsync<EnrollmentMoveDto>())!.Status);
    }

    [Fact]
    public async Task Enroll_ByTeacher_IsForbidden_ByOperator_Succeeds_ByAdministratorAcrossBranch_IsForbidden()
    {
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        var teacher = await AuthenticatedClientAsync("teacher@demo-center.test");
        var branchId = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var course = await CreateCourseAsync(admin);
        var group = await CreateGroupAsync(admin, branchId, course, capacity: 5);
        var another = await CreateGroupAsync(admin, branchId, course, capacity: 5);
        var (studentId, _) = await RegisterAsync(admin, group);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await teacher.PostAsJsonAsync($"/api/students/{studentId}/enrollments", new { groupId = another })).StatusCode);

        var operatorGroup = await CreateGroupAsync(admin, branchId, course, capacity: 5);
        Assert.Equal(HttpStatusCode.OK,
            (await operatorClient.PostAsJsonAsync($"/api/students/{studentId}/enrollments", new { groupId = operatorGroup })).StatusCode);

        var otherGroup = await GroupInOtherBranchAsync(superAdmin, admin);
        var created = await superAdmin.PostAsJsonAsync("/api/students", ValidStudentBody(NewEmail(), otherGroup));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var otherBranchStudentId = (await created.Content.ReadFromJsonAsync<StudentDto>())!.Id;
        var thirdGroup = await CreateGroupAsync(admin, branchId, course, capacity: 5);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.PostAsJsonAsync($"/api/students/{otherBranchStudentId}/enrollments", new { groupId = thirdGroup })).StatusCode);
    }

    [Fact]
    public async Task LeadConversion_IntoAFullOrClosedGroup_ReturnsConflict()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        var branchId = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var course = await CreateCourseAsync(admin);

        var full = await CreateGroupAsync(admin, branchId, course, capacity: 1);
        await RegisterAsync(admin, full); // fills it

        var leadIntoFull = await CreateLeadAsync(operatorClient, branchId);
        var convertFull = await operatorClient.PostAsJsonAsync($"/api/leads/{leadIntoFull}/convert",
            new { groupId = full, email = NewEmail(), password = DbSeeder.DemoPassword });
        Assert.Equal(HttpStatusCode.Conflict, convertFull.StatusCode);

        var closed = await CreateGroupAsync(admin, branchId, course, capacity: 5);
        await CancelGroupAsync(closed);
        var leadIntoClosed = await CreateLeadAsync(operatorClient, branchId);
        var convertClosed = await operatorClient.PostAsJsonAsync($"/api/leads/{leadIntoClosed}/convert",
            new { groupId = closed, email = NewEmail(), password = DbSeeder.DemoPassword });
        Assert.Equal(HttpStatusCode.Conflict, convertClosed.StatusCode);

        // A trial booking is refused the same way.
        var leadForTrial = await CreateLeadAsync(operatorClient, branchId);
        var trial = await operatorClient.PostAsJsonAsync($"/api/leads/{leadForTrial}/trial",
            new { groupId = full, scheduledAt = DateTimeOffset.UtcNow.AddDays(1) });
        Assert.Equal(HttpStatusCode.Conflict, trial.StatusCode);
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static string NewEmail() => $"enroll-mgmt-{Guid.NewGuid():N}@demo-center.test";

    private static object ValidStudentBody(string email, Guid? groupId = null) => new
    {
        fullName = "Enrollment Management Student", phone = "+998900000099", email, password = DbSeeder.DemoPassword, groupId,
    };

    /// <summary>Registers a fresh student straight into the group and returns their (only) enrollment id too,
    /// so drop tests do not have to look it up separately.</summary>
    private static async Task<(Guid StudentId, Guid EnrollmentId)> RegisterAsync(HttpClient admin, Guid groupId)
    {
        var response = await admin.PostAsJsonAsync("/api/students", ValidStudentBody(NewEmail(), groupId));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var studentId = (await response.Content.ReadFromJsonAsync<StudentDto>())!.Id;

        var overview = (await admin.GetFromJsonAsync<OverviewDto>($"/api/students/{studentId}/overview"))!;
        return (studentId, overview.Enrollments.Single().Id);
    }

    private static async Task<Guid> CreateLeadAsync(HttpClient operatorClient, Guid branchId)
    {
        var response = await operatorClient.PostAsJsonAsync("/api/leads", new
        {
            branchId, fullName = "Enrollment Management Lead", phone = "+998900000098", source = "Website", interestedCourse = "IELTS",
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LeadDto>())!.Id;
    }

    private static async Task<Guid> CreateCourseAsync(HttpClient client, decimal monthlyFee = 0)
    {
        var response = await client.PostAsJsonAsync("/api/courses",
            new { name = $"Enroll Mgmt Course {Guid.NewGuid():N}"[..25], levelCategory = "IELTS", monthlyFee });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CourseDto>())!.Id;
    }

    private static async Task<Guid> CreateGroupAsync(HttpClient client, Guid branchId, Guid courseId, int capacity)
    {
        var response = await client.PostAsJsonAsync("/api/groups",
            new { branchId, courseId, name = $"EM-{Guid.NewGuid():N}", capacity });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<GroupDto>())!.Id;
    }

    private static async Task<Guid> GroupInOtherBranchAsync(HttpClient superAdmin, HttpClient admin)
    {
        var mine = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))!.Select(b => b.Id).ToHashSet();
        var other = (await superAdmin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))!.First(b => !mine.Contains(b.Id)).Id;
        return await CreateGroupAsync(superAdmin, other, await CreateCourseAsync(superAdmin), capacity: 5);
    }

    /// <summary>Setup shortcut: flips the row directly, the same way other integration tests reach into
    /// <see cref="AppDbContext"/>. Unlike <c>POST /api/groups/{id}/status</c> it leaves the enrollments untouched.</summary>
    private async Task CancelGroupAsync(Guid groupId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE groups SET \"Status\" = 'Cancelled' WHERE \"Id\" = {groupId}");
    }

    private async Task<BillingResult> RunBillingAsync(DateTimeOffset now, Guid studentId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BillingRules>().RunAsync(now, studentId: studentId);
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
    private record CourseDto(Guid Id, string Name);
    private record GroupDto(Guid Id);
    private record LeadDto(Guid Id);
    private record StudentDto(Guid Id, string Status);
    private record EnrollmentSummaryDto(Guid Id, Guid GroupId, string GroupName, string Status, DateTimeOffset EnrolledAt);
    private record EnrollmentMoveDto(Guid StudentId, string Status, List<EnrollmentSummaryDto> Enrollments);
    private record OverviewDto(List<EnrollmentSummaryDto> Enrollments);
    private record TimelineDto(string EventType);
}
