using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using EduCenter.Api.Ai;
using EduCenter.Api.Ai.Providers;
using EduCenter.Api.Auth;
using EduCenter.Api.Automation;
using EduCenter.Api.Chat;
using EduCenter.Api.Authorization;
using EduCenter.Api.Deployment;
using EduCenter.Api.Notifications.Delivery;
using EduCenter.Api.Observability;
using EduCenter.Infrastructure;
using EduCenter.Infrastructure.Auditing;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

// Docker HEALTHCHECK: the runtime image has no curl, so the app probes itself (see HealthProbe).
if (args is ["--healthcheck"])
    Environment.Exit(await HealthProbe.RunAsync());

var builder = WebApplication.CreateBuilder(args);

// Machine-local settings and secrets (AI provider keys, local model URL). Git-ignored and optional — see
// appsettings.Local.example.json.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// Configuration sources are last-wins, so the file above would otherwise override environment variables — and the
// test host switches the paid providers off through exactly those. Re-adding them here restores their precedence;
// without this, `dotnet test` on a developer machine would call (and bill) the real DeepSeek API.
builder.Configuration.AddEnvironmentVariables();

// appsettings.json carries development defaults (committed JWT key, dev DB password, AllowedHosts "*"). Anywhere but a
// developer machine those are holes, so a host that still uses them does not start.
if (!builder.Environment.IsDevelopment())
    ProductionSettingsGuard.EnsureSafe(builder.Configuration);

// The Server: Kestrel header tells a scanner exactly which stack to aim at.
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

// Behind a reverse proxy every request arrives from the proxy's address. Without this the per-IP rate limiters below
// see one "client" — a single visitor could exhaust the login window for everybody. Only the listed networks
// (the proxy's docker network) are believed; from anyone else the X-Forwarded-* headers are ignored, so a client cannot
// pick its own IP. Empty (the default) leaves forwarded headers off, as on a developer machine.
var trustedProxyNetworks = builder.Configuration.GetSection("Deployment:TrustedProxyNetworks").Get<string[]>() ?? [];
if (trustedProxyNetworks.Length > 0)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var cidr in trustedProxyNetworks)
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(cidr));
    });
}

// Add services to the container.

// Enums serialize/deserialize as their string names (e.g. "CallLater"), not raw ints — every enum in
// every request/response DTO across the API depends on this, not just CRM's.
builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuditSaveChangesInterceptor>();
builder.Services.AddScoped<NotificationDeliveryInterceptor>();

// Blocked-account enforcement (S01.04): a 30s-TTL cache of User.Status, checked on every authenticated request
// by AccountStatusMiddleware, evicted immediately on activate/deactivate/delete by AccountBlocker.
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<UserStatusCache>();
builder.Services.AddScoped<AccountBlocker>();
builder.Services.AddScoped<PasswordResetService>();

// Per-account lockout after repeated wrong passwords (the per-IP limiter alone cannot stop a distributed guess).
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<LoginThrottle>();

builder.Services.AddDbContext<AppDbContext>((serviceProvider, options) =>
    options
        .UseNpgsql(builder.Configuration.GetConnectionString("Postgres"), npgsql => npgsql.UseVector())
        .AddInterceptors(
            serviceProvider.GetRequiredService<AuditSaveChangesInterceptor>(),
            serviceProvider.GetRequiredService<NotificationDeliveryInterceptor>()));

// Auth: JWT bearer + permission-code-as-policy-name authorization (see docs/01_role_permission_matrix.md).
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddScoped<PermissionResolver>();

// CRM follow-ups + Support Needed Engine signals (docs/00_TZ.md §8, §10): evaluated on the attendance
// write path and on a timer for the time-based rules. Disable with Automation__Enabled=false.
builder.Services.AddScoped<StaffResolver>();
builder.Services.AddScoped<EduCenter.Api.Notifications.NotificationDispatcher>();
builder.Services.AddScoped<EduCenter.Api.Notifications.NotificationRecipients>();

// Email and SMS (docs/00_TZ.md §39): a delivery row per person per channel, sent away from the request by a background job. Both channels are
// off until configured (Notifications:Email / Notifications:Sms), and a channel that is off gets no rows. Tests swap the senders for fakes.
builder.Services.Configure<AppOptions>(builder.Configuration.GetSection("App"));
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Notifications:Email"));
builder.Services.Configure<SmsOptions>(builder.Configuration.GetSection("Notifications:Sms"));
builder.Services.AddHttpClient(SmsSender.HttpClientName);
builder.Services.AddSingleton<INotificationSender, SmtpEmailSender>();
builder.Services.AddSingleton<INotificationSender, SmsSender>();
builder.Services.AddScoped<NotificationDeliveryJob>();
builder.Services.AddScoped<NotificationRules>();
builder.Services.AddScoped<FollowUpRules>();
builder.Services.AddScoped<BillingRules>();
builder.Services.AddScoped<EduCenter.Api.Finance.FinanceQueries>();
builder.Services.Configure<FinanceOptions>(builder.Configuration.GetSection("Finance"));
builder.Services.Configure<AutomationOptions>(builder.Configuration.GetSection("Automation"));

// Automation Engine (docs/00_TZ.md §40, §44 "Hangfire at initial stage"): per-organization rule settings, a run journal,
// and one Hangfire recurring job that runs billing, follow-ups and reminders. Tests set Automation__Enabled=false and
// call the rules directly, so no Hangfire server or storage is registered for them.
builder.Services.AddScoped<AutomationSettings>();
builder.Services.AddScoped<AutomationJournal>();
builder.Services.AddScoped<AutomationRunner>();
builder.Services.AddScoped<AutomationSweepJob>();

var automationOptions = builder.Configuration.GetSection("Automation").Get<AutomationOptions>() ?? new AutomationOptions();
if (automationOptions.Enabled)
{
    builder.Services.AddHangfire(configuration => configuration
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UsePostgreSqlStorage(
            storage => storage.UseNpgsqlConnection(builder.Configuration.GetConnectionString("Postgres")),
            new PostgreSqlStorageOptions { SchemaName = "hangfire", PrepareSchemaIfNecessary = true }));
    builder.Services.AddHangfireServer(server => server.WorkerCount = 2);
}

// Developer observability (docs/00_TZ.md §53): request latency/error counters kept in memory for the process lifetime.
builder.Services.AddSingleton<EduCenter.Api.Observability.RequestMetrics>();

// Uploaded files (docs/00_TZ.md §38 steps 1–2, docs/21.09-audit-jurnali S08.03): every *FileStorage asks the guard whether a file is what its
// extension says and whether it is clean before writing a byte. The scanner is ClamAV when Files:Scanner names it; without one, a host that
// is not a developer machine refuses uploads (Files:Scanner:Required) instead of storing files nobody looked at.
builder.Services.Configure<EduCenter.Api.Files.FileScannerOptions>(builder.Configuration.GetSection(EduCenter.Api.Files.FileScannerOptions.Section));
builder.Services.AddSingleton<EduCenter.Api.Files.IFileScanner>(services =>
{
    var scanner = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<EduCenter.Api.Files.FileScannerOptions>>().Value;
    return scanner.UsesClamAv
        ? new EduCenter.Api.Files.ClamAvScanner(scanner, services.GetRequiredService<ILogger<EduCenter.Api.Files.ClamAvScanner>>())
        : new EduCenter.Api.Files.NoopFileScanner();
});
builder.Services.AddScoped<EduCenter.Api.Files.IFileGuard, EduCenter.Api.Files.FileGuard>();

// Chat (docs/00_TZ.md §22): REST for writes, SignalR hub for real-time delivery; attachments on local disk.
builder.Services.AddScoped<ChatAccess>();
builder.Services.AddScoped<ChatFileStorage>();
builder.Services.Configure<ChatFileOptions>(builder.Configuration.GetSection("Chat"));
builder.Services.AddScoped<EduCenter.Api.Lms.ResourceFileStorage>();
builder.Services.Configure<EduCenter.Api.Lms.ResourceFileOptions>(builder.Configuration.GetSection("Resources"));
builder.Services.AddScoped<EduCenter.Api.Students.StudentDocumentFileStorage>();
builder.Services.Configure<EduCenter.Api.Students.StudentDocumentFileOptions>(builder.Configuration.GetSection("StudentDocuments"));
builder.Services.AddScoped<EduCenter.Api.Users.UserPhotoStorage>();
builder.Services.Configure<EduCenter.Api.Users.UserPhotoOptions>(builder.Configuration.GetSection("UserPhotos"));
builder.Services.AddScoped<EduCenter.Api.Organizations.OrganizationLogoStorage>();
builder.Services.Configure<EduCenter.Api.Organizations.OrganizationLogoOptions>(builder.Configuration.GetSection("OrganizationLogo"));
builder.Services.AddSingleton<EduCenter.Api.Dashboard.BranchDashboardCache>();
builder.Services.AddScoped<EduCenter.Api.Users.TeacherCredentialFileStorage>();
builder.Services.Configure<EduCenter.Api.Users.TeacherCredentialFileOptions>(builder.Configuration.GetSection("TeacherCredentials"));
builder.Services.AddScoped<EduCenter.Api.Lms.HomeworkFileStorage>();
builder.Services.Configure<EduCenter.Api.Lms.HomeworkFileOptions>(builder.Configuration.GetSection("Homework"));
builder.Services.AddSignalR()
    .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// AI Gateway (docs/06_phase4_technical_plan.md): providers and routes come from the "Ai" section; every model call goes
// through AiGateway (limits, injection guard, fallback, output validation, usage journal). The provider HttpClient has no
// timeout of its own — the gateway applies the per-provider one.
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection("Ai"));
builder.Services.AddHttpClient(OpenAiCompatibleChatProvider.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan);
builder.Services.AddSingleton<AiProviderRegistry>();
builder.Services.AddScoped<AiLimits>();
builder.Services.AddScoped<AiCallContextFactory>();
builder.Services.AddScoped<AiGateway>();

// RAG (docs/00_TZ.md §31, §38): knowledge-base ingestion in the background, hybrid retrieval, and the student-facing tutor.
builder.Services.AddScoped<EduCenter.Api.Ai.Knowledge.KnowledgeFileStorage>();
builder.Services.AddScoped<EduCenter.Api.Ai.Knowledge.KnowledgeIngestor>();
builder.Services.AddScoped<EduCenter.Api.Ai.Knowledge.KnowledgeRetriever>();
builder.Services.AddScoped<KnowledgeIngestionJob>();
builder.Services.AddScoped<EduCenter.Api.Ai.Tutor.TutorService>();

// Speaking practice (docs/00_TZ.md §34): recordings on local disk, transcription through the gateway.
builder.Services.AddScoped<EduCenter.Api.Ai.Speaking.SpeakingAudioStorage>();
builder.Services.AddScoped<EduCenter.Api.Ai.Speaking.SpeakingService>();

// Writing AI: essays are assessed by a background job — Hangfire where it runs, immediately in-process otherwise (tests).
builder.Services.AddScoped<EduCenter.Api.Ai.Writing.WritingEvaluator>();
builder.Services.AddScoped<WritingEvaluationJob>();
if (automationOptions.Enabled)
    builder.Services.AddScoped<IAiJobQueue, HangfireAiJobQueue>();
else
    builder.Services.AddScoped<IAiJobQueue, InlineAiJobQueue>();

// Email/SMS deliveries follow the same rule: a Hangfire job where it runs, immediately in-process otherwise (tests).
if (automationOptions.Enabled)
    builder.Services.AddScoped<INotificationDeliveryQueue, HangfireDeliveryQueue>();
else
    builder.Services.AddScoped<INotificationDeliveryQueue, InlineDeliveryQueue>();

// Announcements (S06.05): telling a whole audience runs as a background job too.
builder.Services.AddScoped<EduCenter.Api.Announcements.AnnouncementNotifier>();
builder.Services.AddScoped<EduCenter.Api.Announcements.AnnouncementNotificationJob>();
if (automationOptions.Enabled)
    builder.Services.AddScoped<EduCenter.Api.Announcements.IAnnouncementJobQueue, EduCenter.Api.Announcements.HangfireAnnouncementJobQueue>();
else
    builder.Services.AddScoped<EduCenter.Api.Announcements.IAnnouncementJobQueue, EduCenter.Api.Announcements.InlineAnnouncementJobQueue>();

var jwtKey = builder.Configuration["Jwt:SigningKey"]
    ?? throw new InvalidOperationException("Jwt:SigningKey is not configured.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            // Tokens are only ever issued by JwtTokenService with HS256; naming it stops a token signed with any other
            // algorithm from being tried against the key.
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        // Browsers cannot set an Authorization header on a WebSocket, so the SignalR client sends the access token
        // in the query string — accepted for the hub path only.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var token = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    context.Token = token;
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddAuthorization();

const string FrontendCorsPolicy = "Frontend";
builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCorsPolicy, policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            // Dev-only: this box is reachable over several hostnames (localhost, LAN IP, Tailscale), and the
            // frontend's client-side fetches target whichever one the browser used to load the page — so the
            // origin isn't knowable in advance. No cookies/credentials are used (bearer token in a header), so
            // reflecting any origin carries no CSRF risk.
            policy.SetIsOriginAllowed(_ => true);
        }
        else
        {
            // Production serves the frontend and the API from one origin behind the gateway, so by default no
            // cross-origin caller is allowed at all; a separate frontend origin is listed explicitly.
            var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
            if (allowedOrigins.Length > 0) policy.WithOrigins(allowedOrigins);
        }

        policy.AllowAnyHeader().AllowAnyMethod();
    });
});

// Rate limiting (docs/00_TZ.md §49): a generous global cap, and a tight per-IP window on login to
// blunt password-guessing — brute-force protection belongs in code, not just infra.
// Limits are configurable (RateLimiting:GlobalPermitPerMinute / LoginPermitPerMinute) so the
// integration test suite — which logs in dozens of times per run from a single IP — can raise them
// instead of tripping its own brute-force guard; production keeps the tight defaults below.
const string LoginRateLimitPolicy = "LoginBruteForce";
var globalPermitPerMinute = builder.Configuration.GetValue("RateLimiting:GlobalPermitPerMinute", 300);
var loginPermitPerMinute = builder.Configuration.GetValue("RateLimiting:LoginPermitPerMinute", 10);
var aiPermitPerMinute = builder.Configuration.GetValue("RateLimiting:AiPermitPerMinute", 20);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { Window = TimeSpan.FromMinutes(1), PermitLimit = globalPermitPerMinute }));

    options.AddPolicy(LoginRateLimitPolicy, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { Window = TimeSpan.FromMinutes(1), PermitLimit = loginPermitPerMinute }));

    // AI security layer 10 (docs/00_TZ.md §37): model calls cost money, so they are limited per signed-in user.
    options.AddPolicy(AiGateway.RateLimitPolicy, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { Window = TimeSpan.FromMinutes(1), PermitLimit = aiPermitPerMinute }));
});

var app = builder.Build();

// A host that requires a virus scan but has no scanner to ask takes no uploads at all — say so once, at the start, rather than leaving
// the first person who attaches a file to find out.
if (!app.Environment.IsDevelopment())
{
    var scanning = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<EduCenter.Api.Files.FileScannerOptions>>().Value;
    if (scanning is { Required: true, UsesClamAv: false })
        app.Logger.LogWarning("Files:Scanner is not configured (Provider={Provider}) and Required is on: every file upload is refused until a "
                              + "scanner is set (Files__Scanner__Provider=clamav, Files__Scanner__Host).", scanning.Provider);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await EduCenter.Infrastructure.Seed.DbSeeder.SeedAsync(db);

    // A centre of a realistic size (~180 students) instead of the handful the base seed makes. Opt-in because the
    // integration tests boot this same Development seed and count its rows.
    if (app.Configuration.GetValue<bool>("Seed:BulkDemoData"))
        await EduCenter.Infrastructure.Seed.DemoDataSeeder.SeedAsync(db);
}
else if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    // Production: migrate, load roles/permissions, and — only on an empty database — create the organization and its
    // SuperAdmin. No demo centre, no shared demo password (see ProductionBootstrapper).
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var bootstrap = app.Configuration.GetSection("Bootstrap").Get<BootstrapOptions>() ?? new BootstrapOptions();
    await ProductionBootstrapper.RunAsync(db, bootstrap, app.Logger);
}

// First, so everything below (rate limiting above all) sees the visitor's address rather than the proxy's.
if (trustedProxyNetworks.Length > 0)
    app.UseForwardedHeaders();

// TLS ends at the reverse proxy in front of this host, which sends plain HTTP here; redirecting would loop.
if (!app.Configuration.GetValue<bool>("Deployment:BehindTlsProxy"))
    app.UseHttpsRedirection();

// Outermost after redirection, so the timing covers CORS, authentication and the endpoint itself.
app.UseRequestMetrics();

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

    // Everything this host returns is consumed by fetch(); none of it is a page. Should a response ever be opened
    // directly (a stored file, a mistyped link), the sandbox stops it running script with the app's origin, and
    // no-store keeps tokens and personal data out of shared and browser caches. The Development-only Hangfire
    // dashboard is a real page, so it is left alone.
    if (!context.Request.Path.StartsWithSegments("/hangfire"))
    {
        headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'; sandbox";
        headers["Cache-Control"] = "no-store";
    }

    await next();
});

app.UseCors(FrontendCorsPolicy);

// Rate limiting runs after authentication so per-user policies (AI) can partition on the signed-in user; the global and
// login limiters key on the client IP and are unaffected by the order.
app.UseAuthentication();
app.UseMiddleware<AccountStatusMiddleware>();
app.UseRateLimiter();
app.UseAuthorization();

if (automationOptions.Enabled)
{
    var sweepMinutes = Math.Clamp(automationOptions.SweepIntervalSeconds / 60, 1, 59);
    app.Services.GetRequiredService<IRecurringJobManager>().AddOrUpdate<AutomationSweepJob>(
        "automation-sweep", job => job.RunAsync(CancellationToken.None), $"*/{sweepMinutes} * * * *");

    // Read-only job dashboard for local development only — it has no bearer-token authentication of its own.
    if (app.Environment.IsDevelopment())
        app.UseHangfireDashboard("/hangfire", new DashboardOptions
        {
            Authorization = [new DevelopmentDashboardAuthorizationFilter()],
            IsReadOnlyFunc = _ => true
        });
}

app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");

// Liveness for the container healthcheck and the gateway. Answers "ok" and touches nothing else — no database, no
// version — so it is safe even if it were ever reachable from outside (the gateway does not route it).
app.MapGet("/healthz", () => Results.Text("ok"));

app.Run();

// Exposes the top-level Program for WebApplicationFactory<Program> in EduCenter.Api.IntegrationTests.
public partial class Program;
