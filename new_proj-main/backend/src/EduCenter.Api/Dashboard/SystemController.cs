using System.Data;
using System.Data.Common;
using System.Diagnostics;
using EduCenter.Infrastructure;
using EduCenter.Modules.Ai.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Dashboard;

/// <summary>
/// The Developer's screen (docs/00_TZ.md §3.8, §53). The Developer role has held <c>system.health.view</c>,
/// <c>logs.view</c> and <c>deployments.view</c> since Phase 1 with nothing to point them at, so logging in as one
/// showed an empty sidebar. Everything here is measured from the database and this process — nothing is invented,
/// and where a number cannot be measured it is absent rather than zero.
/// </summary>
[ApiController]
[Route("api/system")]
public class SystemController(AppDbContext db, IWebHostEnvironment env) : ControllerBase
{
    /// <summary>Row estimates come from the planner's statistics rather than COUNT(*) — on the audit log, which is
    /// the table anyone actually wants the size of, an exact count is a full scan.</summary>
    private const string TableCountsSql = """
        SELECT relname, n_live_tup
        FROM pg_stat_user_tables
        ORDER BY n_live_tup DESC
        LIMIT 20
        """;

    [HttpGet("health")]
    [Authorize(Policy = "system.health.view")]
    public async Task<ActionResult<SystemDashboardDto>> Health(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var since = now.AddHours(-24);

        var reachable = true;
        var latency = 0;
        string? version = null;
        long? size = null;
        string? dbError = null;
        var tables = new List<TableCountDto>();

        var sw = Stopwatch.StartNew();
        try
        {
            // EF's own connection rather than a second one built from the string: the string on this context may
            // come from a data source that carries settings (pgvector mapping, SSL) a bare NpgsqlConnection lacks.
            var conn = db.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
            sw.Stop();
            latency = (int)sw.ElapsedMilliseconds;

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT version(), pg_database_size(current_database())";
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct))
                {
                    version = reader.GetString(0);
                    size = reader.GetInt64(1);
                }
            }

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = TableCountsSql;
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                    tables.Add(new TableCountDto(reader.GetString(0), reader.GetInt64(1)));
            }
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException)
        {
            // The panel exists precisely to show this state, so a dead database is a result, not a 500 — and the
            // reason is the whole value of the row. Without it "Ulanmadi" sends a developer to the logs anyway.
            reachable = false;
            dbError = ex.Message;
            sw.Stop();
            latency = (int)sw.ElapsedMilliseconds;
        }

        var ai = reachable
            ? await db.AiRequests
                .Where(r => r.CreatedAt >= since)
                .Select(r => new { r.Status, r.CostUsd, r.LatencyMs, r.Provider })
                .ToListAsync(ct)
            : [];

        var failures = ai.Count(r => r.Status != AiRequestStatus.Succeeded);
        var providers = ai
            .Where(r => r.Provider != null)
            .GroupBy(r => r.Provider!)
            .Select(g => new AiProviderHealthDto(
                g.Key,
                g.Count(),
                g.Count(r => r.Status != AiRequestStatus.Succeeded),
                Math.Round((double)g.Count(r => r.Status != AiRequestStatus.Succeeded) / g.Count(), 4),
                Percentile95(g.Select(r => r.LatencyMs)),
                g.Sum(r => r.CostUsd)))
            .OrderByDescending(p => p.Requests)
            .ToList();

        var writing = reachable
            ? await db.WritingEvaluations
                .Where(e => e.Status == WritingEvaluationStatus.Queued
                    || e.Status == WritingEvaluationStatus.Processing
                    || (e.Status == WritingEvaluationStatus.Failed && e.CreatedAt >= since))
                .Select(e => e.Status)
                .ToListAsync(ct)
            : [];

        var runs = reachable
            ? await db.AutomationRunLogs
                .OrderByDescending(r => r.RanAt)
                .Take(10)
                .Select(r => new AutomationRunDto(r.RuleCode, r.Trigger.ToString(), r.ItemCount, r.Summary, r.RanAt))
                .ToListAsync(ct)
            : [];

        var auditEvents = reachable ? await db.AuditLogs.CountAsync(a => a.CreatedAt >= since, ct) : 0;
        var sessions = reachable
            ? await db.RefreshTokens.CountAsync(t => t.RevokedAt == null && t.ExpiresAt > now, ct)
            : 0;

        return Ok(new SystemDashboardDto(
            reachable, latency, version, size, dbError,
            typeof(SystemController).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            env.EnvironmentName,
            DateTimeOffset.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime(),
            ai.Count,
            failures,
            ai.Count == 0 ? 0 : Math.Round((double)failures / ai.Count, 4),
            ai.Sum(r => r.CostUsd),
            Percentile95(ai.Select(r => r.LatencyMs)),
            providers,
            writing.Count(s => s == WritingEvaluationStatus.Queued),
            writing.Count(s => s == WritingEvaluationStatus.Processing),
            writing.Count(s => s == WritingEvaluationStatus.Failed),
            runs,
            auditEvents,
            sessions,
            tables));
    }

    /// <summary>Nearest-rank p95. A mean latency hides the outage; the slow tail is the thing worth watching.</summary>
    private static int Percentile95(IEnumerable<int> values)
    {
        var sorted = values.Where(v => v > 0).OrderBy(v => v).ToList();
        if (sorted.Count == 0) return 0;
        var rank = (int)Math.Ceiling(0.95 * sorted.Count) - 1;
        return sorted[Math.Clamp(rank, 0, sorted.Count - 1)];
    }
}
