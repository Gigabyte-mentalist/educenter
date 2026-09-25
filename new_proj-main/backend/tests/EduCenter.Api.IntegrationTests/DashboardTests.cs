using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduCenter.Api.Ai;
using EduCenter.Infrastructure.Seed;
using EduCenter.Modules.Ai.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// The landing screens (docs/00_TZ.md §5, §3.4, §3.6, §24). Every role holds the same <c>dashboard.view</c>
/// code, so the interesting property is not "is it authorised" but "does the scope still cut the data" — a
/// parent must reach the parent shape and nothing wider, and nobody outside management may read the
/// organization figures.
/// </summary>
[Collection("Api")]
public class DashboardTests(EduCenterWebApplicationFactory factory)
{
    [Fact]
    public async Task Organization_WithoutToken_ReturnsUnauthorized()
    {
        var response = await factory.CreateClient().GetAsync("/api/dashboard/organization");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Organization_AsSuperAdmin_CoversEveryBranch()
    {
        var client = await AuthenticatedClientAsync("superadmin@demo-center.test");

        var dashboard = await client.GetFromJsonAsync<OrganizationShape>("/api/dashboard/organization");

        Assert.NotNull(dashboard);
        Assert.True(dashboard!.Branches.Count >= 2, "AllOrganization scope should cover every seeded branch.");
        Assert.True(dashboard.TotalStudents > 0);
    }

    [Fact]
    public async Task Organization_AiCard_IsTheUsageReportsOwnCount_ForThoseWhoMayReadTheReport()
    {
        // Some AI traffic to count: one tutor answer through the gateway, as the usage report's own test does it.
        using (var scope = factory.Services.CreateScope())
        {
            var gateway = scope.ServiceProvider.GetRequiredService<AiGateway>();
            var contexts = scope.ServiceProvider.GetRequiredService<AiCallContextFactory>();
            var db = scope.ServiceProvider.GetRequiredService<EduCenter.Infrastructure.AppDbContext>();
            var studentUserId = db.Users.Single(u => u.Email == "student@demo-center.test").Id;
            var context = (await contexts.ForUserAsync(studentUserId, AiFeature.Tutor))!;
            var prompt = new AiPrompt("test.dashboard.v1", "Answer briefly.", [new AiUntrustedInput("question", "What is a noun?", 200)]);
            Assert.True((await gateway.CompleteTextAsync(context, AiTaskKind.TutorAnswer, prompt)).IsSuccess);
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var period = $"from={today.AddDays(-29):yyyy-MM-dd}&to={today:yyyy-MM-dd}";
        foreach (var email in new[] { "superadmin@demo-center.test", "admin@demo-center.test" })
        {
            var client = await AuthenticatedClientAsync(email);
            var dashboard = (await client.GetFromJsonAsync<AiCardShape>($"/api/dashboard/organization?{period}"))!;
            var report = (await client.GetFromJsonAsync<AiReportShape>($"/api/ai/usage/summary?{period}"))!;

            Assert.Equal(report.Totals.Requests, dashboard.AiRequests);
            Assert.Equal(report.Totals.CostUsd, dashboard.AiCostUsd);
            Assert.True(dashboard.AiRequests > 0, "the tutor answer above is inside the period");
            Assert.True(dashboard.AiActiveStudents > 0);
        }
    }

    [Fact]
    public async Task Organization_AiCard_IsLeftOutForARoleThatMayNotReadTheUsageReport()
    {
        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");

        var dashboard = (await operatorClient.GetFromJsonAsync<AiCardShape>("/api/dashboard/organization"))!;

        Assert.Null(dashboard.AiRequests);
        Assert.Null(dashboard.AiCostUsd);
        Assert.Null(dashboard.AiActiveStudents);
    }

    [Fact]
    public async Task Organization_AsAdministrator_IsCutToTheirOwnBranch()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");

        var mine = await admin.GetFromJsonAsync<OrganizationShape>("/api/dashboard/organization");
        var all = await superAdmin.GetFromJsonAsync<OrganizationShape>("/api/dashboard/organization");

        Assert.Single(mine!.Branches); // MyBranches: the demo Administrator is assigned exactly one
        Assert.True(all!.Branches.Count > mine.Branches.Count);
    }

    [Fact]
    public async Task Organization_AsStudent_IsForbidden()
    {
        // The student holds dashboard.view, but at Self scope — which carries no branch-level visibility at all.
        var client = await AuthenticatedClientAsync("student@demo-center.test");

        var response = await client.GetAsync("/api/dashboard/organization");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Organization_WithReversedPeriod_IsRejected()
    {
        var client = await AuthenticatedClientAsync("superadmin@demo-center.test");

        var response = await client.GetAsync("/api/dashboard/organization?from=2026-09-30&to=2026-09-01");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Student_SeesOnlyTheirOwnCard()
    {
        var client = await AuthenticatedClientAsync("student@demo-center.test");

        var dashboard = await client.GetFromJsonAsync<StudentShape>("/api/dashboard/student");

        Assert.NotNull(dashboard);
        Assert.False(string.IsNullOrWhiteSpace(dashboard!.FullName));
        Assert.InRange(dashboard.AttendanceRate, 0, 1);
    }

    [Fact]
    public async Task Student_AsSuperAdmin_HasNoStudentCard()
    {
        // A super admin is not a student; the endpoint must say so rather than inventing an empty dashboard.
        var client = await AuthenticatedClientAsync("superadmin@demo-center.test");

        var response = await client.GetAsync("/api/dashboard/student");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Parent_SeesExactlyTheChildrenTheirScopeAlreadyAllows()
    {
        // The dashboard is a second door onto the same data, so the set it returns has to match the one the
        // MyChildren scope already permits through /api/students — no wider, and not empty either.
        var parent = await AuthenticatedClientAsync("parent@demo-center.test");

        var dashboard = await parent.GetFromJsonAsync<ParentShape>("/api/dashboard/parent");
        var scoped = (await parent.GetFromJsonAsync<PagedResponse<StudentRow>>("/api/students"))!.Items;

        Assert.NotEmpty(dashboard!.Children);
        Assert.Equal(
            scoped.Select(s => s.Id).OrderBy(id => id),
            dashboard.Children.Select(c => c.StudentId).OrderBy(id => id));
    }

    [Fact]
    public async Task Teacher_CountsOnlyTheirOwnGroups()
    {
        var client = await AuthenticatedClientAsync("teacher@demo-center.test");

        var dashboard = await client.GetFromJsonAsync<TeacherShape>("/api/dashboard/teacher");

        Assert.NotNull(dashboard);
        Assert.True(dashboard!.MyGroups > 0, "The demo teacher is assigned to groups.");
        Assert.InRange(dashboard.AttendanceRate, 0, 1);
    }

    [Fact]
    public async Task SystemHealth_AsDeveloper_ReportsALiveDatabase()
    {
        var client = await AuthenticatedClientAsync("developer@demo-center.test");

        var health = await client.GetFromJsonAsync<SystemShape>("/api/system/health");

        Assert.NotNull(health);
        Assert.True(health!.DatabaseReachable, health.DatabaseError ?? "no error reported");
        Assert.NotEmpty(health.Tables);
    }

    [Fact]
    public async Task SystemHealth_AsAdministrator_IsForbidden()
    {
        // system.health.view is the Developer's alone — an administrator has no business reading table sizes.
        var client = await AuthenticatedClientAsync("admin@demo-center.test");

        var response = await client.GetAsync("/api/system/health");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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

    private record BranchRow(Guid BranchId, string BranchName);
    private record OrganizationShape(int TotalStudents, List<BranchRow> Branches);
    private record AiCardShape(int? AiRequests, decimal? AiCostUsd, int? AiActiveStudents);
    private record AiTotals(int Requests, decimal CostUsd);
    private record AiReportShape(AiTotals Totals);
    private record StudentShape(Guid StudentId, string FullName, double AttendanceRate);
    private record ChildRow(Guid StudentId, string FullName);
    private record StudentRow(Guid Id);
    private record ParentShape(List<ChildRow> Children);
    private record TeacherShape(int MyGroups, double AttendanceRate);
    private record TableRow(string Name, long Rows);
    private record SystemShape(bool DatabaseReachable, string? DatabaseError, List<TableRow> Tables);
}
