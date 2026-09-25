using System.Data.Common;
using System.Diagnostics;
using System.Runtime.InteropServices;
using EduCenter.Infrastructure;
using Hangfire;
using Hangfire.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Observability;

public record MinuteBucketDto(DateTimeOffset Minute, int Requests, int Errors);
public record EndpointMetricDto(string Method, string Route, int Requests, int Errors, int P50Ms, int P95Ms, int P99Ms);

/// <param name="WindowMinutes">The span every number below is computed over.</param>
public record RequestMetricsDto(
    int WindowMinutes, DateTimeOffset CollectingSince, int Requests, double RequestsPerMinute, int ServerErrors, int ClientErrors,
    double ErrorRate, int P50Ms, int P95Ms, int P99Ms, List<MinuteBucketDto> Timeline, List<EndpointMetricDto> Endpoints);

public record ActiveQueryDto(int Pid, double RunningSeconds, string Query);
public record SlowStatementDto(string Query, long Calls, double MeanMs, double TotalMs);

/// <param name="StatementStatsAvailable">pg_stat_statements is an extension the server must preload; without it only the
/// queries running right now can be listed, and the screen says so instead of showing an empty "no slow queries".</param>
public record DatabaseMetricsDto(
    int Connections, int MaxConnections, List<ActiveQueryDto> ActiveQueries, bool StatementStatsAvailable, List<SlowStatementDto> SlowStatements);

public record StorageFolderDto(string Name, long Bytes, int Files);
public record StorageDto(long? DiskTotalBytes, long? DiskFreeBytes, List<StorageFolderDto> Folders);
public record SystemMetricsDto(RequestMetricsDto Requests, DatabaseMetricsDto Database, StorageDto Storage);

public record JobRecurringDto(string Id, string Cron, DateTimeOffset? NextRun, DateTimeOffset? LastRun, string? LastState);
public record JobFailureDto(string Id, string Job, string? Reason, DateTimeOffset? FailedAt);
public record JobsDto(
    bool Available, string? Note, long Enqueued, long Processing, long Scheduled, long Succeeded, long Failed, int Servers,
    List<JobRecurringDto> Recurring, List<JobFailureDto> RecentFailures);

public record DeploymentDto(
    string Version, string Environment, string Runtime, string Os, string MachineName, DateTimeOffset StartedAt, long MemoryBytes,
    string? LastMigration, List<string> AppliedMigrations, List<string> PendingMigrations);

/// <summary>
/// The rest of the Developer's list in docs/00_TZ.md §3.8 / §53 — request latency and error rate, slow queries, storage,
/// background jobs, server errors, the deployed version. Everything is measured; where a figure cannot be read (Hangfire not
/// running, no pg_stat_statements) the response says so rather than showing a zero. None of it contains student data, which is
/// why a Developer may hold these permissions without asking for anything.
/// </summary>
[ApiController]
[Route("api/system")]
public class ObservabilityController(AppDbContext db, RequestMetrics metrics, IWebHostEnvironment env) : ControllerBase
{
    private const int WindowMinutes = 60;
    private const int TimelineMinutes = 30;

    [HttpGet("metrics")]
    [Authorize(Policy = "system.metrics.view")]
    public async Task<ActionResult<SystemMetricsDto>> Metrics(CancellationToken ct)
    {
        return Ok(new SystemMetricsDto(BuildRequestMetrics(), await BuildDatabaseMetricsAsync(ct), BuildStorage()));
    }

    [HttpGet("jobs")]
    [Authorize(Policy = "system.jobs.view")]
    public ActionResult<JobsDto> Jobs()
    {
        try
        {
            var api = JobStorage.Current.GetMonitoringApi();
            var stats = api.GetStatistics();

            List<JobRecurringDto> recurring;
            using (var connection = JobStorage.Current.GetConnection())
            {
                recurring = connection.GetRecurringJobs()
                    .Select(j => new JobRecurringDto(j.Id, j.Cron, ToOffset(j.NextExecution), ToOffset(j.LastExecution), j.LastJobState))
                    .ToList();
            }

            var failures = api.FailedJobs(0, 10)
                .Select(f => new JobFailureDto(f.Key, f.Value.Job?.ToString() ?? "?", f.Value.ExceptionMessage ?? f.Value.Reason, ToOffset(f.Value.FailedAt)))
                .ToList();

            return Ok(new JobsDto(true, null, stats.Enqueued, stats.Processing, stats.Scheduled, stats.Succeeded, stats.Failed,
                (int)stats.Servers, recurring, failures));
        }
        catch (InvalidOperationException)
        {
            // JobStorage.Current throws until AddHangfire has run — the background server is switched off in this environment.
            return Ok(new JobsDto(false, "Fon vazifalari serveri bu muhitda ishga tushirilmagan.", 0, 0, 0, 0, 0, 0, [], []));
        }
    }

    [HttpGet("logs")]
    [Authorize(Policy = "logs.view")]
    public ActionResult<List<ServerError>> Logs() => Ok(metrics.RecentErrors());

    [HttpGet("deployment")]
    [Authorize(Policy = "deployments.view")]
    public async Task<ActionResult<DeploymentDto>> Deployment(CancellationToken ct)
    {
        var applied = (await db.Database.GetAppliedMigrationsAsync(ct)).ToList();
        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
        var process = Process.GetCurrentProcess();

        return Ok(new DeploymentDto(
            typeof(ObservabilityController).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            env.EnvironmentName,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            System.Environment.MachineName,
            process.StartTime.ToUniversalTime(),
            process.WorkingSet64,
            applied.LastOrDefault(),
            applied.TakeLast(8).Reverse().ToList(),
            pending));
    }

    private RequestMetricsDto BuildRequestMetrics()
    {
        var now = DateTimeOffset.UtcNow;
        var window = metrics.Since(now.AddMinutes(-WindowMinutes));
        var latencies = window.Select(s => s.ElapsedMs).OrderBy(v => v).ToList();

        var timelineStart = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, TimeSpan.Zero).AddMinutes(-(TimelineMinutes - 1));
        var timeline = Enumerable.Range(0, TimelineMinutes).Select(i =>
        {
            var minute = timelineStart.AddMinutes(i);
            var inMinute = window.Where(s => s.At >= minute && s.At < minute.AddMinutes(1)).ToList();
            return new MinuteBucketDto(minute, inMinute.Count, inMinute.Count(s => s.Status >= 500));
        }).ToList();

        var endpoints = window
            .GroupBy(s => (s.Method, s.Route))
            .Select(g =>
            {
                var sorted = g.Select(s => s.ElapsedMs).OrderBy(v => v).ToList();
                return new EndpointMetricDto(g.Key.Method, g.Key.Route, sorted.Count, g.Count(s => s.Status >= 500),
                    Percentile(sorted, 0.50), Percentile(sorted, 0.95), Percentile(sorted, 0.99));
            })
            .OrderByDescending(e => e.P95Ms)
            .Take(20)
            .ToList();

        // Requests per minute over the span actually observed, so a freshly restarted API is not averaged over an hour it did not see.
        var observedMinutes = Math.Clamp((now - metrics.CollectingSince).TotalMinutes, 1, WindowMinutes);
        var serverErrors = window.Count(s => s.Status >= 500);

        return new RequestMetricsDto(
            WindowMinutes, metrics.CollectingSince, window.Count, Math.Round(window.Count / observedMinutes, 1), serverErrors,
            window.Count(s => s.Status is >= 400 and < 500), window.Count == 0 ? 0 : Math.Round((double)serverErrors / window.Count, 4),
            Percentile(latencies, 0.50), Percentile(latencies, 0.95), Percentile(latencies, 0.99), timeline, endpoints);
    }

    private async Task<DatabaseMetricsDto> BuildDatabaseMetricsAsync(CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync(ct);

        var connections = await ScalarAsync<long>(connection,
            "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database()", ct);
        var maxConnections = await ScalarAsync<long>(connection, "SELECT current_setting('max_connections')::bigint", ct);

        var active = await ReadAsync(connection, """
            SELECT pid, EXTRACT(EPOCH FROM (now() - query_start))::float8, left(query, 300)
            FROM pg_stat_activity
            WHERE datname = current_database() AND state = 'active' AND pid <> pg_backend_pid()
              AND now() - query_start > interval '500 milliseconds'
            ORDER BY query_start
            LIMIT 10
            """, r => new ActiveQueryDto(r.GetInt32(0), Math.Round(r.GetDouble(1), 1), r.GetString(2)), ct);

        var hasStatements = await ScalarAsync<bool>(connection,
            "SELECT EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'pg_stat_statements')", ct);
        var slow = hasStatements
            ? await ReadAsync(connection, """
                SELECT left(query, 300), calls, mean_exec_time, total_exec_time
                FROM pg_stat_statements
                WHERE dbid = (SELECT oid FROM pg_database WHERE datname = current_database())
                ORDER BY mean_exec_time DESC
                LIMIT 10
                """, r => new SlowStatementDto(r.GetString(0), r.GetInt64(1), Math.Round(r.GetDouble(2), 1), Math.Round(r.GetDouble(3), 1)), ct)
            : [];

        return new DatabaseMetricsDto((int)connections, (int)maxConnections, active, hasStatements, slow);
    }

    private StorageDto BuildStorage()
    {
        long? total = null, free = null;
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(env.ContentRootPath) ?? "/");
            total = drive.TotalSize;
            free = drive.AvailableFreeSpace;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }

        var folders = new List<StorageFolderDto>();
        var root = Path.Combine(env.ContentRootPath, "App_Data");
        if (Directory.Exists(root))
        {
            foreach (var directory in Directory.EnumerateDirectories(root).OrderBy(d => d))
            {
                try
                {
                    var files = new DirectoryInfo(directory).EnumerateFiles("*", SearchOption.AllDirectories).ToList();
                    folders.Add(new StorageFolderDto(Path.GetFileName(directory), files.Sum(f => f.Length), files.Count));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }

        return new StorageDto(total, free, folders);
    }

    /// <summary>Nearest-rank percentile of an ascending list; 0 for an empty one.</summary>
    private static int Percentile(List<int> sortedAscending, double p)
    {
        if (sortedAscending.Count == 0) return 0;
        var rank = (int)Math.Ceiling(p * sortedAscending.Count) - 1;
        return sortedAscending[Math.Clamp(rank, 0, sortedAscending.Count - 1)];
    }

    private static DateTimeOffset? ToOffset(DateTime? value) =>
        value is null ? null : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));

    private static async Task<T> ScalarAsync<T>(DbConnection connection, string sql, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)Convert.ChangeType((await command.ExecuteScalarAsync(ct))!, typeof(T));
    }

    private static async Task<List<T>> ReadAsync<T>(DbConnection connection, string sql, Func<DbDataReader, T> map, CancellationToken ct)
    {
        var rows = new List<T>();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) rows.Add(map(reader));
        return rows;
    }
}
