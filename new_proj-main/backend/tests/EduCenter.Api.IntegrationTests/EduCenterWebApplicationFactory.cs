using EduCenter.Api.Files;
using EduCenter.Api.Notifications.Delivery;
using EduCenter.Infrastructure;
using EduCenter.Modules.Notifications.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// Runs the real Api against its own Postgres database (<see cref="TestDatabase"/>) on the docker-compose
/// server, so Development-only seeding populates roles/permissions/demo users on startup without writing
/// test rows into the `educenter` dev database the UI runs on. The database is dropped and re-migrated
/// once per run, which keeps every run starting from the same seeded state.
/// Requires `docker compose up -d` in backend/ before running this test project.
/// </summary>
public class EduCenterWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string TestDatabase = "educenter_test";

    // Host/port of the compose Postgres. Overridable so the suite can also run inside a container on the compose
    // network (scripts/dotnet-in-docker.sh sets EDUCENTER_TEST_DB_SERVER="Host=educenter-postgres;Port=5432").
    private static readonly string DatabaseServer =
        Environment.GetEnvironmentVariable("EDUCENTER_TEST_DB_SERVER") ?? "Host=localhost;Port=5433";

    private static readonly string ConnectionString =
        $"{DatabaseServer};Database={TestDatabase};Username=educenter;Password=educenter_dev_only";

    /// <summary>Where homework files land during the tests — a fixed folder the tests can count files in.</summary>
    public static readonly string HomeworkFilesPath = Path.Combine(Path.GetTempPath(), "educenter-homework-test-files");

    public EduCenterWebApplicationFactory()
    {
        // Program.cs reads configuration at the top of top-level statements, before the deferred host
        // builder's ConfigureAppConfiguration hooks run — an in-memory config source added there arrives
        // too late. Process environment variables are read synchronously by WebApplication.CreateBuilder,
        // so setting them here (before the host starts) is what actually reaches Program.cs. The suite logs
        // in dozens of times per run from one IP; without the rate-limit override it trips its own
        // brute-force guard on /api/auth/login.
        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", ConnectionString);
        Environment.SetEnvironmentVariable("Automation__Enabled", "false"); // tests call FollowUpRules directly
        // A branch dashboard is kept for minutes; the tests add data and read it straight back, so the cache is off — the one test
        // about the cache turns it on for itself.
        Environment.SetEnvironmentVariable("Dashboard__BranchCacheSeconds", "0");
        Environment.SetEnvironmentVariable("Chat__StoragePath", Path.Combine(Path.GetTempPath(), "educenter-chat-test-files"));
        Environment.SetEnvironmentVariable("Ai__StoragePath", Path.Combine(Path.GetTempPath(), "educenter-knowledge-test-files"));
        Environment.SetEnvironmentVariable("Homework__StoragePath", HomeworkFilesPath);
        Environment.SetEnvironmentVariable("OrganizationLogo__StoragePath", Path.Combine(Path.GetTempPath(), "educenter-org-logo-test-files"));
        Environment.SetEnvironmentVariable("RateLimiting__GlobalPermitPerMinute", "5000");
        Environment.SetEnvironmentVariable("RateLimiting__LoginPermitPerMinute", "1000");
        Environment.SetEnvironmentVariable("RateLimiting__AiPermitPerMinute", "1000");

        // AI Gateway: tests must never reach a paid or local model, even when appsettings.Local.json enables one on this
        // machine — environment variables win over every file, so the real providers are forced off here.
        Environment.SetEnvironmentVariable("Ai__Providers__deepseek__Enabled", "false");
        Environment.SetEnvironmentVariable("Ai__Providers__deepseek__ApiKey", ""); // second lock: no key, no paid call
        Environment.SetEnvironmentVariable("Ai__Providers__local__Enabled", "false");
        Environment.SetEnvironmentVariable("Ai__Providers__whisper__Enabled", "false"); // speech goes to the fake provider too
        Environment.SetEnvironmentVariable("Ai__SpeakingStoragePath", Path.Combine(Path.GetTempPath(), "educenter-speaking-test-files"));
        Environment.SetEnvironmentVariable("Ai__Providers__tts__Enabled", "false"); // a real voice service is never called either
        // A voice is opt-in: no route means questions stay text. The suite names the fake one, and one test switches it off again.
        Environment.SetEnvironmentVariable("Ai__Routes__SpeechSynthesis__0", "fake");

        // The Development settings route everything to the fake provider. A second fake that always fails
        // gives the router something to fall back from (TopicClassification) or to fail on entirely (InjectionCheck).
        Environment.SetEnvironmentVariable("Ai__Providers__broken__Type", "Fake");
        Environment.SetEnvironmentVariable("Ai__Providers__broken__FailureMode", "AlwaysFail");
        Environment.SetEnvironmentVariable("Ai__Routes__TopicClassification__0", "broken");
        Environment.SetEnvironmentVariable("Ai__Routes__TopicClassification__1", "fake");
        Environment.SetEnvironmentVariable("Ai__Routes__InjectionCheck__0", "broken");

        // Program.cs seeds but never migrates, so the schema has to exist before the host boots.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString, npgsql => npgsql.UseVector())
            .Options;
        using var db = new AppDbContext(options);
        db.Database.EnsureDeleted();
        db.Database.Migrate();
    }

    /// <summary>The Email and SMS senders of the test host. Off until a test turns one on, so the rest of the suite sees no outside delivery.
    /// The real ones — a mail server, a paid SMS gateway — are never reachable from here.</summary>
    public FakeNotificationSender FakeEmail { get; } = new(NotificationChannel.Email);

    public FakeNotificationSender FakeSms { get; } = new(NotificationChannel.Sms);

    /// <summary>The virus scanner of the test host: it finds the EICAR test string and nothing else, and counts what it was shown. The real
    /// ClamAV is never reachable from the suite.</summary>
    public FakeFileScanner FakeScanner { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IFileScanner>();
            services.AddSingleton<IFileScanner>(FakeScanner);
            services.RemoveAll<INotificationSender>();
            services.AddSingleton<INotificationSender>(FakeEmail);
            services.AddSingleton<INotificationSender>(FakeSms);
            // A daily SMS allowance small enough to reach in a test, and an address for the links in outgoing messages.
            services.PostConfigure<SmsOptions>(o => o.DailyLimit = 2);
            services.PostConfigure<AppOptions>(o => o.PublicUrl = "https://educenter.test");
        });
    }
}
