using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using EduCenter.Api.Auth;
using EduCenter.Api.Deployment;
using EduCenter.Infrastructure;
using EduCenter.Modules.Identity.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// What has to hold before this application is reachable from the internet: development defaults are refused, one
/// account cannot be guessed at from many addresses, an empty production database gets an owner but no demo accounts,
/// and nothing but the sign-in routes answers an anonymous caller.
/// </summary>
public class ProductionSettingsGuardTests
{
    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static Dictionary<string, string?> SafeSettings() => new()
    {
        ["Jwt:SigningKey"] = new string('k', 64),
        ["ConnectionStrings:Postgres"] = "Host=db;Database=educenter;Username=app;Password=long-random-value",
        ["AllowedHosts"] = "school.example.net;localhost",
    };

    [Fact]
    public void ExplicitProductionSettings_AreAccepted()
    {
        Assert.Empty(ProductionSettingsGuard.Problems(Config(SafeSettings())));
    }

    [Fact]
    public void TheDevelopmentDefaultsFromAppsettings_AreAllRefused()
    {
        var problems = ProductionSettingsGuard.Problems(Config(new()
        {
            ["Jwt:SigningKey"] = "dev-only-signing-key-change-me-in-production-0123456789",
            ["ConnectionStrings:Postgres"] = "Host=localhost;Port=5433;Database=educenter;Username=educenter;Password=educenter_dev_only",
            ["AllowedHosts"] = "*",
        }));

        Assert.Equal(3, problems.Count);
        Assert.Contains(problems, p => p.Contains("Jwt:SigningKey"));
        Assert.Contains(problems, p => p.Contains("database password"));
        Assert.Contains(problems, p => p.Contains("AllowedHosts"));
    }

    [Theory]
    [InlineData("Jwt:SigningKey", "too-short")]
    [InlineData("Jwt:SigningKey", "")]
    [InlineData("AllowedHosts", "")]
    [InlineData("Ai:AllowFakeProvider", "true")]
    [InlineData("Cors:AllowedOrigins:0", "*")]
    public void EachUnsafeSetting_IsReportedOnItsOwn(string key, string value)
    {
        var settings = SafeSettings();
        settings[key] = value;

        Assert.Single(ProductionSettingsGuard.Problems(Config(settings)));
    }

    [Theory]
    [InlineData("Demo", "Passw0rd!")]
    [InlineData("Demo", "")]
    [InlineData("demo", "short-secret")]
    public void ADemoDeployment_CannotUseAWeakOrThePublishedPassword(string mode, string demoPassword)
    {
        var settings = SafeSettings();
        settings["Bootstrap:Mode"] = mode;
        settings["Bootstrap:DemoPassword"] = demoPassword;

        Assert.Single(ProductionSettingsGuard.Problems(Config(settings)));
    }

    [Fact]
    public void ADemoDeployment_WithItsOwnSecretPassword_IsAccepted()
    {
        var settings = SafeSettings();
        settings["Bootstrap:Mode"] = "Demo";
        settings["Bootstrap:DemoPassword"] = "a-long-random-demo-password-1";

        Assert.Empty(ProductionSettingsGuard.Problems(Config(settings)));
    }

    [Fact]
    public void EnsureSafe_ThrowsOnce_NamingEveryProblem()
    {
        var settings = SafeSettings();
        settings["Jwt:SigningKey"] = "short";
        settings["AllowedHosts"] = "*";

        var error = Assert.Throws<InvalidOperationException>(() => ProductionSettingsGuard.EnsureSafe(Config(settings)));

        Assert.Contains("Jwt:SigningKey", error.Message);
        Assert.Contains("AllowedHosts", error.Message);
    }
}

public class HttpUrlPolicyTests
{
    [Theory]
    [InlineData("https://drive.google.com/file/d/abc/view")]
    [InlineData("http://example.com/a?b=c#d")]
    public void PlainHttpLinks_AreAccepted(string url) => Assert.True(HttpUrlPolicy.IsValid(url));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("javascript:alert(1)")]
    [InlineData("JaVaScRiPt:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/f")]
    [InlineData("//evil.example/x")]
    [InlineData("not a url")]
    [InlineData("https://user:secret@example.com/")]
    public void EverythingElse_IsRefused(string? url) => Assert.False(HttpUrlPolicy.IsValid(url));

    [Fact]
    public void ALinkLongerThanTheColumn_IsRefused() =>
        Assert.False(HttpUrlPolicy.IsValid("https://example.com/" + new string('a', HttpUrlPolicy.MaxLength)));
}

public class LoginThrottleTests
{
    private sealed class ManualClock : TimeProvider
    {
        // Starts at the real "now": the throttle hands the lock's end to MemoryCache as an absolute expiry, and MemoryCache reads the
        // wall clock — a fixed start date in the past would make every entry expire the moment it is written.
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    private static (LoginThrottle Throttle, ManualClock Clock) Create(int maxFailures = 3, int lockoutMinutes = 15)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimiting:LoginMaxFailuresPerAccount"] = maxFailures.ToString(),
            ["RateLimiting:LoginLockoutMinutes"] = lockoutMinutes.ToString(),
        }).Build();
        var clock = new ManualClock();
        return (new LoginThrottle(new MemoryCache(new MemoryCacheOptions()), configuration, clock), clock);
    }

    [Fact]
    public void AccountLocks_OnTheConfiguredFailure_AndOnlyThatAccount()
    {
        var (throttle, _) = Create(maxFailures: 3);

        throttle.RecordFailure("a@x.test");
        throttle.RecordFailure("a@x.test");
        Assert.False(throttle.IsLocked("a@x.test", out _));

        throttle.RecordFailure("a@x.test");
        Assert.True(throttle.IsLocked("a@x.test", out var retryAfter));
        Assert.InRange(retryAfter.TotalMinutes, 14, 15);
        Assert.False(throttle.IsLocked("b@x.test", out _));
    }

    [Fact]
    public void ASuccessfulSignIn_ClearsTheCount()
    {
        var (throttle, _) = Create(maxFailures: 3);
        throttle.RecordFailure("a@x.test");
        throttle.RecordFailure("a@x.test");

        throttle.RecordSuccess("a@x.test");
        throttle.RecordFailure("a@x.test");
        throttle.RecordFailure("a@x.test");

        Assert.False(throttle.IsLocked("a@x.test", out _));
    }

    [Fact]
    public void WhenTheLockRunsOut_TheAccountStartsFromZero()
    {
        var (throttle, clock) = Create(maxFailures: 2, lockoutMinutes: 10);
        throttle.RecordFailure("a@x.test");
        throttle.RecordFailure("a@x.test");
        Assert.True(throttle.IsLocked("a@x.test", out _));

        clock.Advance(TimeSpan.FromMinutes(11));
        Assert.False(throttle.IsLocked("a@x.test", out _));

        // One typo after the lock ends must not lock it again straight away.
        throttle.RecordFailure("a@x.test");
        Assert.False(throttle.IsLocked("a@x.test", out _));
    }
}

public class ProductionBootstrapperTests
{
    private const string Database = "educenter_bootstrap_test";

    private static AppDbContext CreateDb()
    {
        var server = Environment.GetEnvironmentVariable("EDUCENTER_TEST_DB_SERVER") ?? "Host=localhost;Port=5433";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql($"{server};Database={Database};Username=educenter;Password=educenter_dev_only", npgsql => npgsql.UseVector())
            .Options;
        return new AppDbContext(options);
    }

    private static BootstrapOptions ValidOptions() => new()
    {
        OrganizationName = "Acme School",
        TenantSlug = "Acme",
        SuperAdminEmail = "Owner@Acme.Example",
        SuperAdminFullName = "Acme Owner",
        SuperAdminPassword = "a-long-random-password-1",
    };

    [Fact]
    public async Task EmptyDatabase_GetsOneOrganizationAndOneOwner_AndNoDemoAccounts()
    {
        var options = ValidOptions();
        using var db = CreateDb();
        db.Database.EnsureDeleted();
        try
        {
            await ProductionBootstrapper.RunAsync(db, options, NullLogger.Instance);

            var org = await db.Organizations.SingleAsync();
            Assert.Equal("acme", org.TenantSlug);

            var admin = await db.Users.Include(u => u.Roles).SingleAsync(); // exactly one account exists
            Assert.Equal("owner@acme.example", admin.Email);
            Assert.True(admin.MustChangePassword);
            Assert.Equal(PasswordVerificationResult.Success,
                new PasswordHasher<User>().VerifyHashedPassword(admin, admin.PasswordHash, options.SuperAdminPassword));
            var superAdminRole = await db.Roles.SingleAsync(r => r.Code == RoleCode.SuperAdmin);
            Assert.Contains(admin.Roles, r => r.RoleId == superAdminRole.Id);
            Assert.True(await db.Permissions.AnyAsync());
            Assert.True(await db.RolePermissions.AnyAsync());

            // A restart is a no-op: nothing is created twice, and the password is not reset to the configured one.
            await ProductionBootstrapper.RunAsync(db, options, NullLogger.Instance);
            Assert.Equal(1, await db.Organizations.CountAsync());
            Assert.Equal(1, await db.Users.CountAsync());
        }
        finally
        {
            db.Database.EnsureDeleted();
        }
    }

    [Fact]
    public async Task DemoMode_LoadsTheDemoCentre_ButNoAccountAcceptsThePublishedPassword()
    {
        var options = new BootstrapOptions { Mode = BootstrapMode.Demo, DemoPassword = "a-long-random-demo-password-1" };
        using var db = CreateDb();
        db.Database.EnsureDeleted();
        try
        {
            await ProductionBootstrapper.RunAsync(db, options, NullLogger.Instance);

            var org = await db.Organizations.SingleAsync(); // one organization: no tenant boundary is involved
            Assert.Equal("demo-center", org.TenantSlug);
            var users = await db.Users.ToListAsync();
            Assert.True(users.Count > 100, $"expected the bulk demo centre, got {users.Count} users");
            Assert.True(await db.Students.CountAsync() > 100);
            Assert.True(await db.Lessons.AnyAsync());

            // Every distinct password hash in the database: the secret opens the demo accounts, the published one opens none.
            var hasher = new PasswordHasher<User>();
            var superAdmin = users.Single(u => u.Email == "superadmin@demo-center.test");
            Assert.Equal(PasswordVerificationResult.Success,
                hasher.VerifyHashedPassword(superAdmin, superAdmin.PasswordHash, options.DemoPassword));
            foreach (var hash in users.Select(u => u.PasswordHash).Distinct())
                Assert.Equal(PasswordVerificationResult.Failed,
                    hasher.VerifyHashedPassword(superAdmin, hash, EduCenter.Infrastructure.Seed.DbSeeder.DemoPassword));

            // Demo accounts are for showing the product: no forced password change in the way.
            Assert.DoesNotContain(users, u => u.MustChangePassword);

            // A restart changes nothing.
            var students = await db.Students.CountAsync();
            await ProductionBootstrapper.RunAsync(db, options, NullLogger.Instance);
            Assert.Equal(users.Count, await db.Users.CountAsync());
            Assert.Equal(students, await db.Students.CountAsync());
        }
        finally
        {
            db.Database.EnsureDeleted();
        }
    }

    [Fact]
    public async Task DemoMode_WithThePublishedPassword_RefusesToSeed()
    {
        var options = new BootstrapOptions { Mode = BootstrapMode.Demo, DemoPassword = EduCenter.Infrastructure.Seed.DbSeeder.DemoPassword };
        using var db = CreateDb();
        db.Database.EnsureDeleted();
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ProductionBootstrapper.RunAsync(db, options, NullLogger.Instance));
            Assert.False(await db.Users.AnyAsync());
        }
        finally
        {
            db.Database.EnsureDeleted();
        }
    }

    [Fact]
    public async Task EmptyDatabase_WithoutAUsablePassword_RefusesInsteadOfCreatingAWeakOwner()
    {
        var options = ValidOptions();
        options.SuperAdminPassword = "short";
        using var db = CreateDb();
        db.Database.EnsureDeleted();
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ProductionBootstrapper.RunAsync(db, options, NullLogger.Instance));
            Assert.False(await db.Users.AnyAsync());
        }
        finally
        {
            db.Database.EnsureDeleted();
        }
    }

    [Theory]
    [InlineData("", "a-long-random-password-1")]
    [InlineData("not-an-email", "a-long-random-password-1")]
    [InlineData("owner@acme.example", "")]
    [InlineData("owner@acme.example", "fifteen-chars!!")]
    public void Validate_RejectsAnOwnerThatCouldNotSafelyBeCreated(string email, string password)
    {
        var options = ValidOptions();
        options.SuperAdminEmail = email;
        options.SuperAdminPassword = password;

        Assert.NotNull(ProductionBootstrapper.Validate(options));
    }

    [Fact]
    public void Validate_AcceptsCompleteOptions() => Assert.Null(ProductionBootstrapper.Validate(ValidOptions()));
}

public class AnonymousSurfaceTests
{
    /// <summary>The only routed actions that may be called without signing in. Adding to this list is a security decision,
    /// which is the point of it living in a test.</summary>
    private static readonly string[] OpenActions =
    [
        "AuthController.ForgotPassword",
        "AuthController.Login",
        "AuthController.Logout",
        "AuthController.Refresh",
        "AuthController.ResetPassword",
    ];

    [Fact]
    public void OnlyTheSignInRoutes_AreOpenToAnonymousCallers()
    {
        var open = typeof(Program).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t))
            .SelectMany(controller => controller
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(action => action.GetCustomAttributes<HttpMethodAttribute>().Any())
                .Where(action => action.GetCustomAttributes<AllowAnonymousAttribute>(true).Any()
                    || !(action.GetCustomAttributes<AuthorizeAttribute>(true).Any()
                        || controller.GetCustomAttributes<AuthorizeAttribute>(true).Any()))
                .Select(action => $"{controller.Name}.{action.Name}"))
            .Order()
            .ToList();

        Assert.Equal(OpenActions, open);
    }
}

[Collection("Api")]
public class ProductionHardeningApiTests(EduCenterWebApplicationFactory factory)
{
    [Fact]
    public async Task Healthz_AnswersWithoutSignIn_AndOffersNothingElse()
    {
        var response = await factory.CreateClient().GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ApiResponses_CannotBeRenderedAsAPageOrCached()
    {
        var response = await factory.CreateClient().GetAsync("/api/me/permissions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("sandbox", string.Join(";", response.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal("no-store", string.Join(",", response.Headers.GetValues("Cache-Control")));
        Assert.Equal("nosniff", string.Join(",", response.Headers.GetValues("X-Content-Type-Options")));
        Assert.False(response.Headers.Contains("Server"));
    }

    [Fact]
    public async Task Login_LocksAnAccount_AfterFiveWrongPasswords_ButNotItsNeighbours()
    {
        var client = factory.CreateClient();
        var email = $"lockout-{Guid.NewGuid():N}@demo-center.test";

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var wrong = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "not-the-password" });
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        }

        var locked = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "not-the-password" });
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
        Assert.True(locked.Headers.Contains("Retry-After"));

        var neighbour = await client.PostAsJsonAsync("/api/auth/login",
            new { email = $"other-{Guid.NewGuid():N}@demo-center.test", password = "not-the-password" });
        Assert.Equal(HttpStatusCode.Unauthorized, neighbour.StatusCode);
    }

    [Fact]
    public async Task Login_WithAnOversizedEmail_IsRefusedWithoutBeingRemembered()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { email = new string('a', 5000) + "@demo-center.test", password = "x" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
