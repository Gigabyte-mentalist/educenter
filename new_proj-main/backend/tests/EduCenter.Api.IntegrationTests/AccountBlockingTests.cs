using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduCenter.Infrastructure;
using EduCenter.Infrastructure.Seed;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// A blocked account stops working the moment it is blocked, not at the next login or when its access token
/// happens to expire (docs/21.09-audit-jurnali S01.04). Covers the login/refresh messaging, the mid-session cutoff
/// (<c>AccountStatusMiddleware</c> + <c>UserStatusCache</c> eviction), the chat hub push, and the role-level guard
/// on <c>UsersController.CanTouchUserAsync</c> that this step also closed (a branch Administrator could otherwise
/// block, edit or delete a Super Admin).
/// </summary>
[Collection("Api")]
public class AccountBlockingTests(EduCenterWebApplicationFactory factory)
{
    [Fact]
    public async Task Login_Blocked_ReturnsForbiddenWithCode_WrongPasswordAndUnknownEmail_StayGeneric()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var (userId, email) = await RegisterStudentAsync(admin);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/users/{userId}/deactivate", null)).StatusCode);

        var blocked = await admin.PostAsJsonAsync("/api/auth/login", new { email, password = DbSeeder.DemoPassword });
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        var body = await blocked.Content.ReadFromJsonAsync<ErrorBody>();
        Assert.Equal("ACCOUNT_BLOCKED", body!.Code);

        // A blocked account does not leak through a wrong password or an unknown email either.
        var wrongPassword = await admin.PostAsJsonAsync("/api/auth/login", new { email, password = "totally-wrong-1" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Null((await wrongPassword.Content.ReadFromJsonAsync<ErrorBody>())!.Code);

        var unknown = await admin.PostAsJsonAsync("/api/auth/login", new { email = NewEmail(), password = "whatever-123" });
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
    }

    [Fact]
    public async Task Deactivate_SignedInUser_ImmediatelyForbidsPolicyAndPolicyLessEndpoints_ThenActivateRestoresIt()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var (userId, email) = await RegisterStudentAsync(admin);
        var (accessToken, password) = await SignInAsync(email);
        var student = ClientWith(accessToken);

        // Working normally before the block: one policy'd endpoint, one policy-less.
        Assert.Equal(HttpStatusCode.OK, (await student.GetAsync("/api/me/permissions")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/users/{userId}/deactivate", null)).StatusCode);

        // No wait for the cache TTL: AccountBlocker evicts on the write path.
        var policyLess1 = await student.GetAsync("/api/me/permissions");
        var policyLess2 = await student.GetAsync("/api/notifications/unread-count");
        var policied = await student.GetAsync("/api/students");
        foreach (var response in new[] { policyLess1, policyLess2, policied })
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("ACCOUNT_BLOCKED", (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
        }

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/users/{userId}/activate", null)).StatusCode);

        // The same (never revoked) access token works again — no re-login needed.
        Assert.Equal(HttpStatusCode.OK, (await student.GetAsync("/api/me/permissions")).StatusCode);
        var freshLogin = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, freshLogin.StatusCode);
    }

    [Fact]
    public async Task Refresh_AfterDeactivate_ReturnsForbiddenWithCode_TokenNotDeleted_ButLogoutRevokesIt()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var (userId, email) = await RegisterStudentAsync(admin);

        var login = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password = DbSeeder.DemoPassword });
        var tokens = await login.Content.ReadFromJsonAsync<TokenResponseDto>();

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/users/{userId}/deactivate", null)).StatusCode);

        var refresh = await factory.CreateClient().PostAsJsonAsync("/api/auth/refresh", new { refreshToken = tokens!.RefreshToken });
        Assert.Equal(HttpStatusCode.Forbidden, refresh.StatusCode);
        Assert.Equal("ACCOUNT_BLOCKED", (await refresh.Content.ReadFromJsonAsync<ErrorBody>())!.Code);

        // Reactivate, then a normal logout revokes the refresh token for real — refresh now fails generically.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/users/{userId}/activate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.CreateClient().PostAsJsonAsync("/api/auth/logout", new { refreshToken = tokens.RefreshToken })).StatusCode);
        var afterLogout = await factory.CreateClient().PostAsJsonAsync("/api/auth/refresh", new { refreshToken = tokens.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }

    [Fact]
    public async Task Blocked_CanStillLogOut()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var (userId, email) = await RegisterStudentAsync(admin);
        var login = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password = DbSeeder.DemoPassword });
        var tokens = await login.Content.ReadFromJsonAsync<TokenResponseDto>();

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/users/{userId}/deactivate", null)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent,
            (await factory.CreateClient().PostAsJsonAsync("/api/auth/logout", new { refreshToken = tokens!.RefreshToken })).StatusCode);
    }

    [Fact]
    public async Task SignalR_Blocked_PushesEvent_ThenRefusesCallsAndNewConnections()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var (userId, email) = await RegisterStudentAsync(admin);
        var (accessToken, _) = await SignInAsync(email);

        await using var connection = await ConnectAsync(accessToken);
        var blockedEvent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On("accountBlocked", () => blockedEvent.TrySetResult());
        Assert.True(await connection.InvokeAsync<int>("SyncChats") >= 0);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/users/{userId}/deactivate", null)).StatusCode);

        await blockedEvent.Task.WaitAsync(TimeSpan.FromSeconds(15));

        // The call no longer succeeds — which exception surfaces depends on the transport: over LongPolling (used
        // here) AccountStatusMiddleware already 403s the next poll request before SyncChats' own status check
        // gets a chance to run, so this raises HttpRequestException rather than SyncChats' HubException. Both are
        // "the account is blocked and this connection is done" — a WebSocket transport (a real browser) has no
        // polling request for the middleware to intercept, so there SyncChats' explicit check is what fires.
        await Assert.ThrowsAnyAsync<Exception>(() => connection.InvokeAsync("SyncChats"));

        // A fresh connection attempt is refused at the transport level — AccountStatusMiddleware answers
        // every authenticated /hubs/chat request (including negotiate) with 403 while blocked.
        await Assert.ThrowsAnyAsync<Exception>(() => ConnectAsync(accessToken));
    }

    [Fact]
    public async Task DeletedUsers_TokenReturns_Unauthorized_NotForbidden()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var (userId, email) = await RegisterOperatorAsync(admin); // no student/parent card — deletable outright
        var login = await AuthenticatedClientAsync(email);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/users/{userId}")).StatusCode);

        var response = await login.GetAsync("/api/me/permissions");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RoleProtection_BranchAdministrator_CannotTouchOrgWideRoles_ButCanTouchOrdinaryStaff()
    {
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var superAdminId = await UserIdAsync(superAdmin, "superadmin@demo-center.test");
        var (operatorUserId, _) = await RegisterOperatorAsync(admin);

        // Chilonzor Administrator reaching the (org-wide) Super Admin — every touch point is refused, and none
        // of these actually change Super Admin's state (Forbid happens before any write).
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsync($"/api/users/{superAdminId}/deactivate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsync($"/api/users/{superAdminId}/activate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.PatchAsJsonAsync($"/api/users/{superAdminId}", new { fullName = "Hacked", phone = "+998900000000" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.PostAsJsonAsync($"/api/users/{superAdminId}/roles", new { roleCode = "OPERATOR" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.DeleteAsync($"/api/users/{superAdminId}")).StatusCode);

        // An ordinary branch colleague (Operator) is still fully manageable.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/users/{operatorUserId}/deactivate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/users/{operatorUserId}/activate", null)).StatusCode);
    }

    [Fact]
    public async Task Deactivate_WritesAnAuditLogEntry()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var (userId, _) = await RegisterStudentAsync(admin);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/users/{userId}/deactivate", null)).StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Contains(await db.AuditLogs.Where(a => a.ResourceId == userId).ToListAsync(),
            a => a.Resource == "User" && a.Action == "Modified");
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static string NewEmail() => $"blocking-{Guid.NewGuid():N}@demo-center.test";

    private static async Task<(Guid Id, string Email)> RegisterStudentAsync(HttpClient admin)
    {
        var branchId = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var course = await (await admin.PostAsJsonAsync("/api/courses",
            new { name = $"Blocking Course {Guid.NewGuid():N}"[..24], levelCategory = "IELTS", monthlyFee = 0 }))
            .Content.ReadFromJsonAsync<CourseDto>();
        var group = await (await admin.PostAsJsonAsync("/api/groups",
            new { branchId, courseId = course!.Id, name = $"BL-{Guid.NewGuid():N}", capacity = 5 }))
            .Content.ReadFromJsonAsync<GroupDto>();

        var email = NewEmail();
        var response = await admin.PostAsJsonAsync("/api/students", new
        {
            fullName = "Blocking Test Student", phone = "+998900000066", email, password = DbSeeder.DemoPassword, groupId = group!.Id,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var student = await response.Content.ReadFromJsonAsync<StudentDto>();
        return (student!.UserId, email);
    }

    private static async Task<(Guid Id, string Email)> RegisterOperatorAsync(HttpClient admin)
    {
        var branchId = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var email = NewEmail();
        var response = await admin.PostAsJsonAsync("/api/users", new
        {
            fullName = "Blocking Test Operator", email, phone = "+998900000067",
            password = DbSeeder.DemoPassword, roleCode = "OPERATOR", branchIds = new[] { branchId },
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var user = await response.Content.ReadFromJsonAsync<UserDto>();
        return (user!.Id, email);
    }

    private static async Task<Guid> UserIdAsync(HttpClient client, string email)
    {
        var page = await client.GetFromJsonAsync<PagedResponse<UserDto>>($"/api/users?search={Uri.EscapeDataString(email)}");
        return page!.Items.Single(u => u.Email == email).Id;
    }

    private async Task<HubConnection> ConnectAsync(string token)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "hubs/chat"), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();
        await connection.StartAsync();
        return connection;
    }

    private async Task<HttpClient> AuthenticatedClientAsync(string email) => ClientWith((await SignInAsync(email)).AccessToken);

    private HttpClient ClientWith(string accessToken)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    /// <summary>An account created through the API holds a temporary password, so its first real session starts with the
    /// forced change (S01.05) — done here, the way a person signing in for the first time would. Returns the access token
    /// to work with and the password the account holds afterwards (the demo one for accounts that never had to change it).</summary>
    private async Task<(string AccessToken, string Password)> SignInAsync(string email)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = DbSeeder.DemoPassword });
        var tokens = (await response.Content.ReadFromJsonAsync<TokenResponseDto>())!;
        if (!tokens.MustChangePassword) return (tokens.AccessToken, DbSeeder.DemoPassword);

        const string chosen = "Chosen-Pass-42";
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var change = await client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = DbSeeder.DemoPassword, newPassword = chosen });
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        return ((await change.Content.ReadFromJsonAsync<TokenResponseDto>())!.AccessToken, chosen);
    }

    private record TokenResponseDto(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, bool MustChangePassword);
    private record ErrorBody(string? Error, string? Code);
    private record BranchDto(Guid Id, string Name);
    private record CourseDto(Guid Id, string Name);
    private record GroupDto(Guid Id);
    private record StudentDto(Guid Id, Guid UserId);
    private record UserDto(Guid Id, string Email);
}
