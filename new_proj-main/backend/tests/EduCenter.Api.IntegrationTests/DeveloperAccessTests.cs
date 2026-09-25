using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduCenter.Infrastructure;
using EduCenter.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// The Developer's privileges (docs/00_TZ.md §3.8, §53, matrix §3.12): monitoring they hold by default, and student data they
/// hold only through Request → Reason → Approval → Expiration. By default they still see none of it.
/// </summary>
[Collection("Api")]
public class DeveloperAccessTests(EduCenterWebApplicationFactory factory)
{
    private const string Reason = "Ota-onalar dashboardida davomat foizi noto'g'ri chiqayotganini tekshirish uchun.";

    [Theory]
    [InlineData("/api/system/health")]
    [InlineData("/api/system/metrics")]
    [InlineData("/api/system/jobs")]
    [InlineData("/api/system/logs")]
    [InlineData("/api/system/deployment")]
    public async Task Developer_ReadsTheMonitoringEndpoints(string url)
    {
        var developer = await AuthenticatedClientAsync("developer@demo-center.test");

        Assert.Equal(HttpStatusCode.OK, (await developer.GetAsync(url)).StatusCode);
    }

    [Theory]
    [InlineData("/api/system/metrics")]
    [InlineData("/api/system/jobs")]
    [InlineData("/api/system/logs")]
    public async Task Teacher_CannotReadTheMonitoringEndpoints(string url)
    {
        var teacher = await AuthenticatedClientAsync("teacher@demo-center.test");

        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Metrics_CountRequests_ByRouteTemplate()
    {
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var developer = await AuthenticatedClientAsync("developer@demo-center.test");
        for (var i = 0; i < 3; i++) await superAdmin.GetAsync("/api/branches");

        var metrics = (await developer.GetFromJsonAsync<MetricsDto>("/api/system/metrics"))!;

        Assert.True(metrics.Requests.Requests >= 3);
        // The table lists the 20 slowest endpoints, so a fast one such as /api/branches need not be in it — but whatever is
        // listed is a route template, never a path with a concrete id in it.
        Assert.InRange(metrics.Requests.Endpoints.Count, 1, 20);
        Assert.Contains(metrics.Requests.Endpoints, e => e.Route.Contains('{'));
        Assert.DoesNotContain(metrics.Requests.Endpoints, e => Guid.TryParse(e.Route.Split('/').Last(), out _));
        Assert.True(metrics.Requests.P95Ms >= metrics.Requests.P50Ms);
        Assert.Equal(30, metrics.Requests.Timeline.Count);
        Assert.True(metrics.Database.Connections >= 1);
    }

    [Fact]
    public async Task Deployment_ReportsAppliedAndPendingMigrations()
    {
        var developer = await AuthenticatedClientAsync("developer@demo-center.test");

        var deployment = (await developer.GetFromJsonAsync<DeploymentDto>("/api/system/deployment"))!;

        Assert.NotEmpty(deployment.AppliedMigrations);
        Assert.Empty(deployment.PendingMigrations);
        Assert.Equal(deployment.AppliedMigrations[0], deployment.LastMigration);
    }

    [Fact]
    public async Task Developer_HasNoStudentDataByDefault()
    {
        var developer = await AuthenticatedClientAsync("developer@demo-center.test");

        Assert.Equal(HttpStatusCode.Forbidden, (await developer.GetAsync("/api/students")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await developer.GetAsync("/api/groups")).StatusCode);
    }

    [Fact]
    public async Task ApprovedRequest_OpensStudentData_UntilItIsRevoked()
    {
        var developer = await AuthenticatedClientAsync("developer@demo-center.test");
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");

        var created = await developer.PostAsJsonAsync("/api/system/data-access", new { permissionCode = "groups.view", reason = Reason, hours = 2 });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var request = (await created.Content.ReadFromJsonAsync<AccessRequestDto>())!;
        Assert.Equal("Pending", request.Status);
        Assert.False(request.IsActive);

        // Asking is not having: it stays closed until somebody decides.
        Assert.Equal(HttpStatusCode.Forbidden, (await developer.GetAsync("/api/groups")).StatusCode);
        Assert.Contains((await superAdmin.GetFromJsonAsync<List<AccessRequestDto>>("/api/system/data-access"))!, r => r.Id == request.Id);

        var approved = (await (await superAdmin.PostAsJsonAsync($"/api/system/data-access/{request.Id}/approve", new { hours = 1, note = "Faqat shu tekshiruv uchun." }))
            .Content.ReadFromJsonAsync<AccessRequestDto>())!;
        Assert.Equal("Approved", approved.Status);
        Assert.True(approved.IsActive);
        Assert.NotNull(approved.ExpiresAt);
        Assert.Equal("Super Admin Demo", approved.DecidedByName);

        Assert.Equal(HttpStatusCode.OK, (await developer.GetAsync("/api/groups")).StatusCode);
        Assert.Contains((await developer.GetFromJsonAsync<List<MyPermissionDto>>("/api/me/permissions"))!, p => p.Code == "groups.view");
        // The grant is that one permission, not a role upgrade.
        Assert.Equal(HttpStatusCode.Forbidden, (await developer.GetAsync("/api/students")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await superAdmin.PostAsJsonAsync($"/api/system/data-access/{request.Id}/revoke", new { note = "Tekshiruv tugadi." })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await developer.GetAsync("/api/groups")).StatusCode);
    }

    [Fact]
    public async Task ExpiredGrant_NoLongerOpensAnything()
    {
        var developer = await AuthenticatedClientAsync("developer@demo-center.test");
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var request = (await (await developer.PostAsJsonAsync("/api/system/data-access", new { permissionCode = "exams.view", reason = Reason, hours = 1 }))
            .Content.ReadFromJsonAsync<AccessRequestDto>())!;
        await superAdmin.PostAsJsonAsync($"/api/system/data-access/{request.Id}/approve", new { hours = 1 });
        Assert.Equal(HttpStatusCode.OK, (await developer.GetAsync("/api/exams")).StatusCode);

        // Time passes: the approver's window closes without anyone doing anything.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE data_access_requests SET "ExpiresAt" = now() - interval '1 minute' WHERE "Id" = {request.Id}""");
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await developer.GetAsync("/api/exams")).StatusCode);
        Assert.False((await developer.GetFromJsonAsync<List<AccessRequestDto>>("/api/system/data-access/mine"))!.Single(r => r.Id == request.Id).IsActive);
    }

    [Fact]
    public async Task Request_IsRefused_WhenItAsksForTooMuchOrGivesNoReason()
    {
        var developer = await AuthenticatedClientAsync("developer@demo-center.test");

        Assert.Equal(HttpStatusCode.BadRequest,
            (await developer.PostAsJsonAsync("/api/system/data-access", new { permissionCode = "payments.view", reason = Reason, hours = 2 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await developer.PostAsJsonAsync("/api/system/data-access", new { permissionCode = "students.view", reason = "kerak", hours = 2 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await developer.PostAsJsonAsync("/api/system/data-access", new { permissionCode = "students.view", reason = Reason, hours = 500 })).StatusCode);
    }

    [Fact]
    public async Task Request_CannotBeRepeatedWhileOneIsOpen()
    {
        var developer = await AuthenticatedClientAsync("developer@demo-center.test");
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var first = (await (await developer.PostAsJsonAsync("/api/system/data-access", new { permissionCode = "attendance.view", reason = Reason, hours = 1 }))
            .Content.ReadFromJsonAsync<AccessRequestDto>())!;

        Assert.Equal(HttpStatusCode.Conflict,
            (await developer.PostAsJsonAsync("/api/system/data-access", new { permissionCode = "attendance.view", reason = Reason, hours = 1 })).StatusCode);

        await superAdmin.PostAsJsonAsync($"/api/system/data-access/{first.Id}/reject", new { note = "Kerak emas." });
        // A rejected request is closed: the Developer may ask again with a better reason.
        Assert.Equal(HttpStatusCode.OK,
            (await developer.PostAsJsonAsync("/api/system/data-access", new { permissionCode = "attendance.view", reason = Reason, hours = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await superAdmin.PostAsJsonAsync($"/api/system/data-access/{first.Id}/approve", new { })).StatusCode);
    }

    [Fact]
    public async Task OnlyTheApproverDecides_AndOnlyTheDeveloperAsks()
    {
        var developer = await AuthenticatedClientAsync("developer@demo-center.test");
        var administrator = await AuthenticatedClientAsync("admin@demo-center.test");
        var teacher = await AuthenticatedClientAsync("teacher@demo-center.test");

        Assert.Equal(HttpStatusCode.Forbidden, (await developer.GetAsync("/api/system/data-access")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await administrator.GetAsync("/api/system/data-access")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await teacher.PostAsJsonAsync("/api/system/data-access", new { permissionCode = "students.view", reason = Reason, hours = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await developer.PostAsJsonAsync($"/api/system/data-access/{Guid.NewGuid()}/approve", new { })).StatusCode);
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
    private record MyPermissionDto(string Code, string Scope);
    private record AccessRequestDto(Guid Id, string Status, bool IsActive, DateTimeOffset? ExpiresAt, string? DecidedByName);
    private record EndpointDto(string Method, string Route, int Requests);
    private record TimelineDto(DateTimeOffset Minute, int Requests);
    private record RequestsDto(int Requests, int P50Ms, int P95Ms, List<TimelineDto> Timeline, List<EndpointDto> Endpoints);
    private record DatabaseDto(int Connections);
    private record MetricsDto(RequestsDto Requests, DatabaseDto Database);
    private record DeploymentDto(string? LastMigration, List<string> AppliedMigrations, List<string> PendingMigrations);
}
