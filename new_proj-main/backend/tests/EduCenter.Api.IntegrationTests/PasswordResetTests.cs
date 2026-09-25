using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using EduCenter.Api.Auth;
using EduCenter.Infrastructure;
using EduCenter.Infrastructure.Seed;
using EduCenter.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// "Parolni unutdim" (docs/21.09-audit-jurnali S06.04): a six-digit code by email or SMS, then a new password. The channels are the test host's
/// fakes, so the code is read from what the fake was asked to send — the only place it exists besides its hash.
/// </summary>
[Collection("Api")]
public class PasswordResetTests(EduCenterWebApplicationFactory factory) : IDisposable
{
    private const string NewPassword = "Brand-new-Passw0rd!";

    public void Dispose()
    {
        factory.FakeEmail.Reset();
        factory.FakeSms.Reset();
    }

    [Fact]
    public async Task TheCodeFromTheMail_SetsANewPassword_AndEndsEverySessionOfTheOldOne()
    {
        factory.FakeEmail.IsEnabled = true;
        var account = await NewAccountAsync();
        var oldSession = await LoginAsync(account.Email, DbSeeder.DemoPassword);   // a session somebody may have stolen
        Assert.True(oldSession.MustChangePassword);   // a password set for someone else is temporary until they choose their own

        var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.NoContent, (await anonymous.PostAsJsonAsync("/api/auth/forgot-password", new { identifier = account.Email })).StatusCode);
        var (target, message) = Assert.Single(factory.FakeEmail.Sent);
        Assert.Equal(account.Email, target.Email);
        Assert.Contains("15 daqiqa", message.Body);
        var code = CodeIn(message.Body);

        Assert.Equal(HttpStatusCode.NoContent, (await anonymous.PostAsJsonAsync("/api/auth/reset-password",
            new { identifier = account.Email, code, newPassword = NewPassword })).StatusCode);

        var fresh = await LoginAsync(account.Email, NewPassword);
        Assert.False(fresh.MustChangePassword);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/auth/login", new { email = account.Email, password = DbSeeder.DemoPassword })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = oldSession.RefreshToken })).StatusCode);

        // The code is single-use.
        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.PostAsJsonAsync("/api/auth/reset-password",
            new { identifier = account.Email, code, newPassword = "Another-Passw0rd!" })).StatusCode);

        // The reset is on record, naming the person and never the code.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var audit = await db.AuditLogs.SingleAsync(a => a.ResourceId == account.Id && a.Action == "PasswordReset");
        Assert.Equal(account.Id, audit.UserId);
        Assert.DoesNotContain(code, (audit.OldValueJson ?? "") + (audit.NewValueJson ?? "") + audit.Reason);
    }

    [Fact]
    public async Task AnUnknownAddress_GetsTheSameAnswer_AndNothingIsSent()
    {
        factory.FakeEmail.IsEnabled = true;
        var anonymous = factory.CreateClient();

        var unknown = await anonymous.PostAsJsonAsync("/api/auth/forgot-password", new { identifier = $"nobody-{Guid.NewGuid():N}@demo-center.test" });
        var known = await anonymous.PostAsJsonAsync("/api/auth/forgot-password", new { identifier = "student@demo-center.test" });

        Assert.Equal(HttpStatusCode.NoContent, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, known.StatusCode);
        Assert.Equal(1, factory.FakeEmail.Calls);   // the known one only

        // ...and a failed reset reads the same whether the account exists or not.
        var noAccount = await ReadErrorAsync(anonymous.PostAsJsonAsync("/api/auth/reset-password",
            new { identifier = $"nobody-{Guid.NewGuid():N}@demo-center.test", code = "123456", newPassword = NewPassword }));
        var wrongCode = await ReadErrorAsync(anonymous.PostAsJsonAsync("/api/auth/reset-password",
            new { identifier = "student@demo-center.test", code = "000000", newPassword = NewPassword }));
        Assert.Equal(PasswordResetService.InvalidCodeMessage, noAccount);
        Assert.Equal(noAccount, wrongCode);
    }

    [Fact]
    public async Task FiveWrongGuesses_KillTheCode_EvenForTheRightOne()
    {
        factory.FakeEmail.IsEnabled = true;
        var account = await NewAccountAsync();
        var anonymous = factory.CreateClient();
        await anonymous.PostAsJsonAsync("/api/auth/forgot-password", new { identifier = account.Email });
        var code = CodeIn(factory.FakeEmail.Sent.Last().Message.Body);
        var wrong = code == "000000" ? "111111" : "000000";

        for (var i = 0; i < PasswordResetToken.MaxAttempts; i++)
            Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.PostAsJsonAsync("/api/auth/reset-password",
                new { identifier = account.Email, code = wrong, newPassword = NewPassword })).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.PostAsJsonAsync("/api/auth/reset-password",
            new { identifier = account.Email, code, newPassword = NewPassword })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.PostAsJsonAsync("/api/auth/login", new { email = account.Email, password = DbSeeder.DemoPassword })).StatusCode);   // still the old password
    }

    [Fact]
    public async Task AnExpiredCode_IsRefused()
    {
        factory.FakeEmail.IsEnabled = true;
        var account = await NewAccountAsync();
        var anonymous = factory.CreateClient();
        await anonymous.PostAsJsonAsync("/api/auth/forgot-password", new { identifier = account.Email });
        var code = CodeIn(factory.FakeEmail.Sent.Last().Message.Body);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.PasswordResetTokens.Where(t => t.UserId == account.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }

        var response = await anonymous.PostAsJsonAsync("/api/auth/reset-password", new { identifier = account.Email, code, newPassword = NewPassword });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("muddati o'tgan", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ANewRequest_ReplacesTheEarlierCode()
    {
        factory.FakeEmail.IsEnabled = true;
        var account = await NewAccountAsync();
        var anonymous = factory.CreateClient();

        await anonymous.PostAsJsonAsync("/api/auth/forgot-password", new { identifier = account.Email });
        var first = CodeIn(factory.FakeEmail.Sent.Last().Message.Body);
        string second;
        do
        {
            await anonymous.PostAsJsonAsync("/api/auth/forgot-password", new { identifier = account.Email });
            second = CodeIn(factory.FakeEmail.Sent.Last().Message.Body);
        } while (second == first);   // one time in a million the two draws are the same number

        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.PostAsJsonAsync("/api/auth/reset-password",
            new { identifier = account.Email, code = first, newPassword = NewPassword })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await anonymous.PostAsJsonAsync("/api/auth/reset-password",
            new { identifier = account.Email, code = second, newPassword = NewPassword })).StatusCode);
    }

    [Fact]
    public async Task ABlockedAccount_IsToldNothingAndSentNothing()
    {
        factory.FakeEmail.IsEnabled = true;
        var account = await NewAccountAsync();
        var superAdmin = await LoginClientAsync("superadmin@demo-center.test");
        Assert.Equal(HttpStatusCode.NoContent, (await superAdmin.PostAsync($"/api/users/{account.Id}/deactivate", null)).StatusCode);

        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/forgot-password", new { identifier = account.Email });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, factory.FakeEmail.Calls);
    }

    [Fact]
    public async Task MoreThanThreeRequestsAnHour_AreIgnoredWithoutASignal()
    {
        factory.FakeEmail.IsEnabled = true;
        var account = await NewAccountAsync();
        var anonymous = factory.CreateClient();

        for (var i = 0; i < PasswordResetService.MaxRequestsPerHour + 2; i++)
            Assert.Equal(HttpStatusCode.NoContent, (await anonymous.PostAsJsonAsync("/api/auth/forgot-password", new { identifier = account.Email })).StatusCode);

        Assert.Equal(PasswordResetService.MaxRequestsPerHour, factory.FakeEmail.Sent.Count);
    }

    [Fact]
    public async Task APhoneNumber_GetsTheCodeBySms_ButOnlyIfItBelongsToOneAccount()
    {
        factory.FakeSms.IsEnabled = true;
        var phone = $"+99893{Random.Shared.Next(1_000_000, 9_999_999)}";
        var account = await NewAccountAsync(phone);
        var anonymous = factory.CreateClient();
        var typed = $"93 {phone[6..9]} {phone[9..11]} {phone[11..]}";   // as a person types it: no plus, no country code, spaces

        Assert.Equal(HttpStatusCode.NoContent, (await anonymous.PostAsJsonAsync("/api/auth/forgot-password", new { identifier = typed })).StatusCode);
        var (target, message) = Assert.Single(factory.FakeSms.Sent);
        Assert.Equal(account.Id, target.UserId);
        Assert.Equal(HttpStatusCode.NoContent, (await anonymous.PostAsJsonAsync("/api/auth/reset-password",
            new { identifier = typed, code = CodeIn(message.Body), newPassword = NewPassword })).StatusCode);
        await LoginAsync(account.Email, NewPassword);

        // Two accounts on the same number: nobody can tell which one is asking, so nobody is sent anything.
        await NewAccountAsync(phone);
        factory.FakeSms.Sent.Clear();
        Assert.Equal(HttpStatusCode.NoContent, (await anonymous.PostAsJsonAsync("/api/auth/forgot-password", new { identifier = typed })).StatusCode);
        Assert.Empty(factory.FakeSms.Sent);
    }

    [Fact]
    public async Task ATooWeakPassword_IsRefusedWithoutSpendingTheCode()
    {
        factory.FakeEmail.IsEnabled = true;
        var account = await NewAccountAsync();
        var anonymous = factory.CreateClient();
        await anonymous.PostAsJsonAsync("/api/auth/forgot-password", new { identifier = account.Email });
        var code = CodeIn(factory.FakeEmail.Sent.Last().Message.Body);

        var weak = await anonymous.PostAsJsonAsync("/api/auth/reset-password", new { identifier = account.Email, code, newPassword = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Contains("Parol kamida", await weak.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NoContent, (await anonymous.PostAsJsonAsync("/api/auth/reset-password",
            new { identifier = account.Email, code, newPassword = NewPassword })).StatusCode);
    }

    [Fact]
    public async Task WithNoMailServerConfigured_NobodyIsToldAnything()
    {
        var account = await NewAccountAsync();   // both fakes are off

        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/forgot-password", new { identifier = account.Email });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().PasswordResetTokens.AnyAsync(t => t.UserId == account.Id));
    }

    // ---- helpers ----------------------------------------------------------------------------

    private static string CodeIn(string body) => Regex.Match(body, @"\b\d{6}\b").Value;

    private static async Task<string?> ReadErrorAsync(Task<HttpResponseMessage> request)
    {
        var response = await request;
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ErrorDto>())!.Error;
    }

    private sealed record Account(Guid Id, string Email);

    private async Task<Account> NewAccountAsync(string? phone = null)
    {
        var superAdmin = await LoginClientAsync("superadmin@demo-center.test");
        var branchId = (await superAdmin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var email = $"reset-{Guid.NewGuid():N}@demo-center.test";
        var response = await superAdmin.PostAsJsonAsync("/api/users", new
        {
            // Sorts after "Teacher Demo": other tests look the demo teacher up on the first page of the name-ordered user list.
            fullName = $"Zz Reset Person {Guid.NewGuid():N}"[..27], email, phone = phone ?? $"+99891{Random.Shared.Next(1_000_000, 9_999_999)}",
            password = DbSeeder.DemoPassword, roleCode = "TEACHER", branchIds = new[] { branchId },
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return new Account((await response.Content.ReadFromJsonAsync<IdDto>())!.Id, email);
    }

    private async Task<TokenDto> LoginAsync(string email, string password)
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TokenDto>())!;
    }

    private async Task<HttpClient> LoginClientAsync(string email)
    {
        var tokens = await LoginAsync(email, DbSeeder.DemoPassword);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return client;
    }

    private record TokenDto(string AccessToken, string RefreshToken, bool MustChangePassword);
    private record IdDto(Guid Id);
    private record BranchDto(Guid Id);
    private record ErrorDto(string? Error);
}
