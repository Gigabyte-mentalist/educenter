using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduCenter.Infrastructure;
using EduCenter.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// Changing one's own password, an administrator resetting someone else's, and the forced change that a temporary
/// password carries (docs/21.09-audit-jurnali S01.05). Every test that changes or resets a password works on an
/// account it creates itself: the whole suite shares one database and signs the seeded demo accounts in with
/// <see cref="DbSeeder.DemoPassword"/>, so touching those would break unrelated tests depending on run order.
/// </summary>
[Collection("Api")]
public class PasswordTests(EduCenterWebApplicationFactory factory)
{
    private const string ChosenPassword = "Chosen-Pass-42";

    [Fact]
    public async Task ChangePassword_ReplacesThePassword_AndReturnsATokenPairThatIsNoLongerForcedToChange()
    {
        var admin = await SignInAsync("admin@demo-center.test");
        var (_, email) = await RegisterOperatorAsync(admin);

        // A password somebody else chose is a temporary one — the account starts out forced to replace it.
        var first = await LoginAsync(email, DbSeeder.DemoPassword);
        Assert.True(first.MustChangePassword);

        var change = await ClientWith(first.AccessToken).PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = DbSeeder.DemoPassword, newPassword = ChosenPassword });
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        var fresh = (await change.Content.ReadFromJsonAsync<TokenResponseDto>())!;
        Assert.False(fresh.MustChangePassword);
        Assert.NotEqual(first.RefreshToken, fresh.RefreshToken);

        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginRawAsync(email, DbSeeder.DemoPassword)).StatusCode);
        var relogin = await LoginAsync(email, ChosenPassword);
        Assert.False(relogin.MustChangePassword);
    }

    [Fact]
    public async Task ChangePassword_RejectsAWrongCurrentPassword_AShortNewOne_AndOneEqualToTheCurrent_AndNeedsASignIn()
    {
        var admin = await SignInAsync("admin@demo-center.test");
        var (_, email) = await RegisterOperatorAsync(admin);
        var client = ClientWith((await LoginAsync(email, DbSeeder.DemoPassword)).AccessToken);

        var wrongCurrent = await client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = "not-my-password-1", newPassword = ChosenPassword });
        Assert.Equal(HttpStatusCode.BadRequest, wrongCurrent.StatusCode);

        var tooShort = await client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = DbSeeder.DemoPassword, newPassword = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);

        var unchanged = await client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = DbSeeder.DemoPassword, newPassword = DbSeeder.DemoPassword });
        Assert.Equal(HttpStatusCode.BadRequest, unchanged.StatusCode);

        // None of those attempts changed anything: the original password still signs in.
        Assert.Equal(HttpStatusCode.OK, (await LoginRawAsync(email, DbSeeder.DemoPassword)).StatusCode);

        var anonymous = await factory.CreateClient().PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = DbSeeder.DemoPassword, newPassword = ChosenPassword });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_SignsOutEveryOtherDevice_ButTheReturnedPairKeepsWorking()
    {
        var admin = await SignInAsync("admin@demo-center.test");
        var (_, email) = await RegisterOperatorAsync(admin);
        var thisDevice = await LoginAsync(email, DbSeeder.DemoPassword);
        var otherDevice = await LoginAsync(email, DbSeeder.DemoPassword);

        var change = await ClientWith(thisDevice.AccessToken).PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = DbSeeder.DemoPassword, newPassword = ChosenPassword });
        var fresh = (await change.Content.ReadFromJsonAsync<TokenResponseDto>())!;

        // Both refresh tokens issued before the change are dead — including the caller's own previous one.
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(otherDevice.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(thisDevice.RefreshToken)).StatusCode);

        // The pair that came back with the change is what keeps this session alive.
        Assert.Equal(HttpStatusCode.OK, (await ClientWith(fresh.AccessToken).GetAsync("/api/me/permissions")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(fresh.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task ResetPassword_ReturnsATemporaryPassword_ForcesAChange_AndEndsEveryOldSession()
    {
        var superAdmin = await SignInAsync("superadmin@demo-center.test");
        var (userId, email) = await RegisterOperatorAsync(superAdmin);
        var session = await SettleAsync(email);

        var reset = await superAdmin.PostAsJsonAsync($"/api/users/{userId}/reset-password", new { });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        var temporary = (await reset.Content.ReadFromJsonAsync<ResetPasswordDto>())!.TemporaryPassword;
        Assert.Equal(12, temporary.Length);

        // What the holder had before is gone: the old refresh token, the old password, and the old access token
        // (which still verifies, but the account is now waiting on a forced change).
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(session.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginRawAsync(email, ChosenPassword)).StatusCode);
        var stale = await ClientWith(session.AccessToken).GetAsync("/api/me/permissions");
        Assert.Equal(HttpStatusCode.Forbidden, stale.StatusCode);
        Assert.Equal("PASSWORD_CHANGE_REQUIRED", (await stale.Content.ReadFromJsonAsync<ErrorBody>())!.Code);

        var login = await LoginAsync(email, temporary);
        Assert.True(login.MustChangePassword);

        // ...and the way out is to choose a password of one's own.
        var change = await ClientWith(login.AccessToken).PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = temporary, newPassword = "A-Brand-New-Pass-7" });
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        var settled = (await change.Content.ReadFromJsonAsync<TokenResponseDto>())!;
        Assert.Equal(HttpStatusCode.OK, (await ClientWith(settled.AccessToken).GetAsync("/api/me/permissions")).StatusCode);
    }

    [Fact]
    public async Task ResetPassword_UsesTheGivenPassword_WhenOneIsSent_AndRefusesAShortOne()
    {
        var superAdmin = await SignInAsync("superadmin@demo-center.test");
        var (userId, email) = await RegisterOperatorAsync(superAdmin);

        var tooShort = await superAdmin.PostAsJsonAsync($"/api/users/{userId}/reset-password", new { newPassword = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);

        var given = await superAdmin.PostAsJsonAsync($"/api/users/{userId}/reset-password", new { newPassword = "Given-By-Admin-1" });
        Assert.Equal(HttpStatusCode.OK, given.StatusCode);
        Assert.Equal("Given-By-Admin-1", (await given.Content.ReadFromJsonAsync<ResetPasswordDto>())!.TemporaryPassword);
        Assert.True((await LoginAsync(email, "Given-By-Admin-1")).MustChangePassword);
    }

    [Fact]
    public async Task ForcedChange_BlocksEverythingButChangePassword_UntilThePasswordIsReplaced()
    {
        var admin = await SignInAsync("admin@demo-center.test");
        var (_, email) = await RegisterOperatorAsync(admin);
        var login = await LoginAsync(email, DbSeeder.DemoPassword);
        var client = ClientWith(login.AccessToken);

        // A policy'd endpoint and a policy-less one are both refused with the code the frontend redirects on.
        foreach (var path in new[] { "/api/students", "/api/me/permissions", "/api/notifications/unread-count" })
        {
            var blocked = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
            Assert.Equal("PASSWORD_CHANGE_REQUIRED", (await blocked.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
        }

        var change = await client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = DbSeeder.DemoPassword, newPassword = ChosenPassword });
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);

        // The same access token is fine now: the cached "must change" state was dropped by the change itself,
        // not left to expire.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me/permissions")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/students")).StatusCode);
    }

    [Fact]
    public async Task SeededDemoAccounts_AreNotForcedToChange_ButEveryNewRegistrationPathIs()
    {
        foreach (var seeded in new[] { "superadmin@demo-center.test", "teacher@demo-center.test", "student@demo-center.test" })
            Assert.False((await LoginAsync(seeded, DbSeeder.DemoPassword)).MustChangePassword, seeded);

        var superAdmin = await SignInAsync("superadmin@demo-center.test");

        // Through /students — the administrator picked the password, not the student.
        var studentEmail = NewEmail();
        var registered = await superAdmin.PostAsJsonAsync("/api/students", new
        {
            fullName = "Forced Change Student", phone = "+998900000077", email = studentEmail, password = DbSeeder.DemoPassword,
        });
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        Assert.True((await LoginAsync(studentEmail, DbSeeder.DemoPassword)).MustChangePassword);
        var student = (await registered.Content.ReadFromJsonAsync<StudentDto>())!;

        // Through a new guardian account linked to that student.
        var parentEmail = NewEmail();
        var linked = await superAdmin.PostAsJsonAsync($"/api/students/{student.Id}/parents", new
        {
            fullName = "Forced Change Parent", phone = "+998900000078", email = parentEmail,
            password = DbSeeder.DemoPassword, relationship = "Mother",
        });
        Assert.Equal(HttpStatusCode.Created, linked.StatusCode);
        Assert.True((await LoginAsync(parentEmail, DbSeeder.DemoPassword)).MustChangePassword);
    }

    [Fact]
    public async Task ResetPassword_FollowsTheUsersRules_NoOneOutsideTheirReachAndNoSelfReset()
    {
        var superAdmin = await SignInAsync("superadmin@demo-center.test");
        var admin = await SignInAsync("admin@demo-center.test");
        var superAdminId = await UserIdAsync(superAdmin, "superadmin@demo-center.test");
        var adminId = await UserIdAsync(superAdmin, "admin@demo-center.test");

        // A branch Administrator may not reach an org-wide Super Admin (the same guard as block/edit/delete).
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync($"/api/users/{superAdminId}/reset-password", new { })).StatusCode);

        // Nobody resets their own password from here — that is "change password", which needs the current one.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/users/{adminId}/reset-password", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await superAdmin.PostAsJsonAsync($"/api/users/{superAdminId}/reset-password", new { })).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await superAdmin.PostAsJsonAsync($"/api/users/{Guid.NewGuid()}/reset-password", new { })).StatusCode);

        // A user in a branch the Administrator does not share is out of reach; one in their own branch is not.
        var (otherBranchUser, _) = await RegisterOperatorAsync(superAdmin, await BranchOutsideAsync(superAdmin, admin));
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync($"/api/users/{otherBranchUser}/reset-password", new { })).StatusCode);

        var (ownBranchUser, _) = await RegisterOperatorAsync(admin);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/users/{ownBranchUser}/reset-password", new { })).StatusCode);
    }

    [Fact]
    public async Task ResetPassword_NeedsThePermission_OperatorTeacherAndStudentHaveNone()
    {
        var superAdmin = await SignInAsync("superadmin@demo-center.test");
        var (targetId, _) = await RegisterOperatorAsync(superAdmin);

        var (_, operatorEmail) = await RegisterOperatorAsync(superAdmin);
        var operatorClient = ClientWith((await SettleAsync(operatorEmail)).AccessToken);

        foreach (var client in new[]
        {
            operatorClient,
            await SignInAsync("teacher@demo-center.test"),
            await SignInAsync("student@demo-center.test"),
        })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/users/{targetId}/reset-password", new { })).StatusCode);
    }

    [Fact]
    public async Task ChangePassword_IsRefusedForABlockedAccount_WhoseAccessTokenStillVerifies()
    {
        var admin = await SignInAsync("admin@demo-center.test");
        var (userId, email) = await RegisterOperatorAsync(admin);
        var login = await LoginAsync(email, DbSeeder.DemoPassword);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/users/{userId}/deactivate", null)).StatusCode);

        // /api/auth/* skips the status middleware, so this route has to refuse on its own — otherwise a blocked
        // account could trade its last access token for a brand-new pair.
        var response = await ClientWith(login.AccessToken).PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = DbSeeder.DemoPassword, newPassword = ChosenPassword });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("ACCOUNT_BLOCKED", (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Code);

        // Nothing changed: the password is still the original one (a changed one would answer 401 here).
        Assert.Equal(HttpStatusCode.Forbidden, (await LoginRawAsync(email, DbSeeder.DemoPassword)).StatusCode);
    }

    [Fact]
    public async Task PasswordChange_IsAudited_WithoutEverWritingAHashIntoTheLog()
    {
        var admin = await SignInAsync("admin@demo-center.test");
        var (userId, email) = await RegisterOperatorAsync(admin);
        var oldHash = await PasswordHashAsync(userId);

        var login = await LoginAsync(email, DbSeeder.DemoPassword);
        var change = await ClientWith(login.AccessToken).PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = DbSeeder.DemoPassword, newPassword = ChosenPassword });
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        var newHash = await PasswordHashAsync(userId);
        Assert.NotEqual(oldHash, newHash);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entries = await db.AuditLogs.Where(a => a.ResourceId == userId && a.Resource == "User").ToListAsync();
        Assert.Contains(entries, a => a.Action == "Modified");

        foreach (var entry in entries)
            foreach (var json in new[] { entry.OldValueJson, entry.NewValueJson })
            {
                Assert.DoesNotContain("PasswordHash", json ?? "");
                Assert.DoesNotContain(oldHash, json ?? "");
                Assert.DoesNotContain(newHash, json ?? "");
            }

        // What the log does show is the fact and the moment of the change — "who changed a password, and when".
        Assert.Contains(entries, a => a.NewValueJson is not null && a.NewValueJson.Contains("PasswordChangedAt"));
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static string NewEmail() => $"password-{Guid.NewGuid():N}@demo-center.test";

    private HttpClient ClientWith(string accessToken)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    private async Task<HttpResponseMessage> LoginRawAsync(string email, string password) =>
        await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password });

    private async Task<TokenResponseDto> LoginAsync(string email, string password)
    {
        var response = await LoginRawAsync(email, password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TokenResponseDto>())!;
    }

    private async Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
        await factory.CreateClient().PostAsJsonAsync("/api/auth/refresh", new { refreshToken });

    /// <summary>A client for an account that never had to change its password (the seeded demo accounts).</summary>
    private async Task<HttpClient> SignInAsync(string email) => ClientWith((await LoginAsync(email, DbSeeder.DemoPassword)).AccessToken);

    /// <summary>Signs a freshly registered account in and gets past the forced change, ending up holding
    /// <see cref="ChosenPassword"/> and a session that is free to work.</summary>
    private async Task<TokenResponseDto> SettleAsync(string email)
    {
        var login = await LoginAsync(email, DbSeeder.DemoPassword);
        var change = await ClientWith(login.AccessToken).PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = DbSeeder.DemoPassword, newPassword = ChosenPassword });
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        return (await change.Content.ReadFromJsonAsync<TokenResponseDto>())!;
    }

    private static async Task<(Guid Id, string Email)> RegisterOperatorAsync(HttpClient creator, Guid? branchId = null)
    {
        var branch = branchId ?? (await creator.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var email = NewEmail();
        var response = await creator.PostAsJsonAsync("/api/users", new
        {
            fullName = "Password Test Operator", email, phone = "+998900000079",
            password = DbSeeder.DemoPassword, roleCode = "OPERATOR", branchIds = new[] { branch },
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return ((await response.Content.ReadFromJsonAsync<UserDto>())!.Id, email);
    }

    /// <summary>A branch the Super Admin sees and the branch Administrator does not.</summary>
    private static async Task<Guid> BranchOutsideAsync(HttpClient superAdmin, HttpClient admin)
    {
        var mine = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))!.Select(b => b.Id).ToHashSet();
        return (await superAdmin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))!.First(b => !mine.Contains(b.Id)).Id;
    }

    private static async Task<Guid> UserIdAsync(HttpClient client, string email)
    {
        var page = await client.GetFromJsonAsync<PagedResponse<UserDto>>($"/api/users?search={Uri.EscapeDataString(email)}");
        return page!.Items.Single(u => u.Email == email).Id;
    }

    private async Task<string> PasswordHashAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users.Where(u => u.Id == userId).Select(u => u.PasswordHash).SingleAsync();
    }

    private record TokenResponseDto(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, bool MustChangePassword);
    private record ErrorBody(string? Error, string? Code);
    private record ResetPasswordDto(string TemporaryPassword);
    private record BranchDto(Guid Id, string Name);
    private record UserDto(Guid Id, string Email);
    private record StudentDto(Guid Id, Guid UserId);
}
