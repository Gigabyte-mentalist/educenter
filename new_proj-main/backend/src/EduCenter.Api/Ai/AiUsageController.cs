using EduCenter.Api.Authorization;
using EduCenter.Infrastructure;
using EduCenter.Modules.Ai.Domain;
using EduCenter.SharedKernel.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Ai;

public record AiUsageTotalsDto(
    int Requests, int Succeeded, int Failed, int Rejected, int InputTokens, int OutputTokens, decimal CostUsd,
    int AverageLatencyMs, int SpeakingMinutes, int TutorQuestions, int WritingEvaluations);

/// <param name="Date">UTC day — usage monitoring is a technical measure, not a branch-local business fact.</param>
public record AiUsageDayDto(DateOnly Date, int Requests, decimal CostUsd);

public record AiUsageGroupDto(string Key, int Requests, int Succeeded, decimal CostUsd, int AverageLatencyMs);

public record AiUsageStudentDto(Guid? StudentId, string Name, int Requests, decimal CostUsd, int SpeakingMinutes);

public record AiUsageBranchDto(Guid? BranchId, string BranchName, int Requests, decimal CostUsd);

public record AiUsageFailureDto(DateTimeOffset At, AiFeature Feature, AiTaskKind TaskKind, string? Reason, string? Detail, string? Provider);

public record AiUsageSummaryDto(
    DateOnly From, DateOnly To, AiUsageTotalsDto Totals, List<AiUsageDayDto> Daily, List<AiUsageGroupDto> ByFeature,
    List<AiUsageGroupDto> ByProvider, List<AiUsageBranchDto> ByBranch, List<AiUsageStudentDto> TopStudents,
    List<AiUsageGroupDto> Reasons, List<AiUsageFailureDto> RecentFailures);

/// <param name="Bias">Mean signed difference (AI − teacher): positive means the AI marks higher than the teacher.</param>
/// <param name="WithinHalfBand">Share of essays where the AI landed within half a band of the teacher.</param>
public record WritingBenchmarkRowDto(
    string PromptVersion, string? Model, int Samples, decimal AiMean, decimal TeacherMean, decimal Bias,
    decimal MeanAbsoluteError, decimal WithinHalfBand,
    decimal? TaskResponseMae, decimal? CoherenceMae, decimal? LexicalMae, decimal? GrammarMae);

public record WritingBenchmarkDto(DateOnly From, DateOnly To, int Completed, int Overridden, List<WritingBenchmarkRowDto> Rows);

/// <summary>
/// AI cost control and quality monitoring (docs/00_TZ.md §54, §55). Everything here is read from the gateway's own
/// journal, so the numbers cover every feature by construction — a new AI feature appears in these reports without
/// touching this controller.
/// </summary>
[ApiController]
[Route("api/ai")]
public class AiUsageController(AppDbContext db) : ControllerBase
{
    private const int MaxPeriodDays = 92;

    [HttpGet("usage/summary")]
    [Authorize(Policy = "ai.usage.view")]
    public async Task<ActionResult<AiUsageSummaryDto>> Summary([FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] Guid? branchId, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        var period = ResolvePeriod(from, to);
        if (period.Error is not null) return BadRequest(new { error = period.Error });

        var branches = await VisibleBranchesAsync(scope, branchId, ct);
        if (branches is null) return Forbid();

        var requests = await db.Requests(scope, branches.Select(b => b.Id).ToList(), branchId, period.From, period.To).ToListAsync(ct);

        var students = await StudentNamesAsync(requests.Select(r => r.StudentId).OfType<Guid>().Distinct().ToList(), ct);
        var branchNames = branches.ToDictionary(b => b.Id, b => b.Name);

        // Counted by AiUsageQueries, which the dashboard's AI card asks as well — the two screens cannot disagree.
        var totals = AiUsageQueries.Totals(requests);

        var daily = requests
            .GroupBy(r => DateOnly.FromDateTime(r.CreatedAt.UtcDateTime))
            .Select(g => new AiUsageDayDto(g.Key, g.Count(), g.Sum(r => r.CostUsd)))
            .OrderBy(d => d.Date)
            .ToList();

        var byFeature = Group(requests, r => r.Feature.ToString());
        var byProvider = Group(requests.Where(r => r.Provider is not null), r => $"{r.Provider}:{r.Model}");
        var reasons = Group(requests.Where(r => r.Reason is not null), r => r.Reason!);

        var byBranch = requests
            .GroupBy(r => r.BranchId)
            .Select(g => new AiUsageBranchDto(g.Key,
                g.Key is { } id ? branchNames.GetValueOrDefault(id, "—") : "Filialsiz",
                g.Count(), g.Sum(r => r.CostUsd)))
            .OrderByDescending(b => b.CostUsd)
            .ToList();

        var topStudents = requests
            .Where(r => r.StudentId is not null)
            .GroupBy(r => r.StudentId)
            .Select(g => new AiUsageStudentDto(g.Key, students.GetValueOrDefault(g.Key!.Value, "—"), g.Count(),
                g.Sum(r => r.CostUsd), (int)Math.Round(g.Sum(r => r.AudioSeconds) / 60.0)))
            .OrderByDescending(s => s.CostUsd)
            .ThenByDescending(s => s.Requests)
            .Take(10)
            .ToList();

        var recentFailures = requests
            .Where(r => r.Status != AiRequestStatus.Succeeded)
            .OrderByDescending(r => r.CreatedAt)
            .Take(20)
            .Select(r => new AiUsageFailureDto(r.CreatedAt, r.Feature, r.TaskKind, r.Reason, r.Detail, r.Provider))
            .ToList();

        return Ok(new AiUsageSummaryDto(period.From, period.To, totals, daily, byFeature, byProvider, byBranch, topStudents,
            reasons, recentFailures));
    }

    /// <summary>
    /// Writing quality benchmark (docs/00_TZ.md §55): every essay a teacher re-marked is a data point comparing the AI's
    /// estimate with a human's, grouped by prompt version and model so a prompt change can be judged against the previous
    /// one. It reads results that already exist — no essay is sent to a model again, so opening this page costs nothing.
    /// </summary>
    [HttpGet("benchmarks/writing")]
    [Authorize(Policy = "ai.usage.view")]
    public async Task<ActionResult<WritingBenchmarkDto>> WritingBenchmark([FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        var period = ResolvePeriod(from, to, defaultDays: 90);
        if (period.Error is not null) return BadRequest(new { error = period.Error });

        var fetchFrom = new DateTimeOffset(period.From.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var fetchTo = new DateTimeOffset(period.To.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var evaluations = await db.WritingEvaluations.AsNoTracking()
            .Where(e => e.OrganizationId == scope.OrganizationId && e.Status == WritingEvaluationStatus.Completed
                && e.CreatedAt >= fetchFrom && e.CreatedAt < fetchTo)
            .Select(e => new
            {
                e.PromptVersion, e.Model, e.EstimatedBand, e.TeacherBand,
                e.TaskResponseBand, e.TeacherTaskResponseBand, e.CoherenceBand, e.TeacherCoherenceBand,
                e.LexicalBand, e.TeacherLexicalBand, e.GrammarBand, e.TeacherGrammarBand
            })
            .ToListAsync(ct);

        var overridden = evaluations.Where(e => e.TeacherBand is not null && e.EstimatedBand is not null).ToList();
        var rows = overridden
            .GroupBy(e => new { e.PromptVersion, e.Model })
            .Select(g =>
            {
                var deltas = g.Select(e => e.EstimatedBand!.Value - e.TeacherBand!.Value).ToList();
                return new WritingBenchmarkRowDto(
                    g.Key.PromptVersion, g.Key.Model, g.Count(),
                    Round(g.Average(e => e.EstimatedBand!.Value)),
                    Round(g.Average(e => e.TeacherBand!.Value)),
                    Round(deltas.Average()),
                    Round(deltas.Average(d => Math.Abs(d))),
                    Round((decimal)deltas.Count(d => Math.Abs(d) <= 0.5m) / deltas.Count * 100),
                    CriterionMae(g.Select(e => (e.TaskResponseBand, e.TeacherTaskResponseBand))),
                    CriterionMae(g.Select(e => (e.CoherenceBand, e.TeacherCoherenceBand))),
                    CriterionMae(g.Select(e => (e.LexicalBand, e.TeacherLexicalBand))),
                    CriterionMae(g.Select(e => (e.GrammarBand, e.TeacherGrammarBand))));
            })
            .OrderByDescending(r => r.Samples)
            .ToList();

        return Ok(new WritingBenchmarkDto(period.From, period.To, evaluations.Count, overridden.Count, rows));
    }

    private sealed record BranchRow(Guid Id, string Name);

    private async Task<List<BranchRow>?> VisibleBranchesAsync(ResolvedScope scope, Guid? branchId, CancellationToken ct)
    {
        if (scope.Scope is not (ScopeType.AllOrganization or ScopeType.MyBranches)) return null;

        var query = db.Branches.Where(b => b.OrganizationId == scope.OrganizationId);
        if (scope.Scope == ScopeType.MyBranches) query = query.Where(b => scope.BranchIds.Contains(b.Id));

        var branches = await query.OrderBy(b => b.Name).Select(b => new BranchRow(b.Id, b.Name)).ToListAsync(ct);
        return branchId is not null && branches.All(b => b.Id != branchId) ? null : branches;
    }

    private async Task<Dictionary<Guid, string>> StudentNamesAsync(List<Guid> studentIds, CancellationToken ct)
    {
        if (studentIds.Count == 0) return [];
        return await (
            from s in db.Students
            join u in db.Users on s.UserId equals u.Id
            where studentIds.Contains(s.Id)
            select new { s.Id, u.FullName }
        ).ToDictionaryAsync(x => x.Id, x => x.FullName, ct);
    }

    private static (DateOnly From, DateOnly To, string? Error) ResolvePeriod(DateOnly? from, DateOnly? to, int defaultDays = 29)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var start = from ?? today.AddDays(-defaultDays);
        var end = to ?? today;

        if (end < start) return (start, end, "Tugash sanasi boshlanish sanasidan oldin bo'lmasin.");
        if (end.DayNumber - start.DayNumber > MaxPeriodDays) return (start, end, $"Davr {MaxPeriodDays} kundan oshmasligi kerak.");
        return (start, end, null);
    }

    private static List<AiUsageGroupDto> Group(IEnumerable<AiRequest> requests, Func<AiRequest, string> key) =>
        requests
            .GroupBy(key)
            .Select(g => new AiUsageGroupDto(g.Key, g.Count(), g.Count(r => r.Status == AiRequestStatus.Succeeded),
                g.Sum(r => r.CostUsd), Average(g.Where(r => r.LatencyMs > 0).Select(r => r.LatencyMs))))
            .OrderByDescending(g => g.Requests)
            .ToList();

    private static int Average(IEnumerable<int> values) => AiUsageQueries.Average(values);

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>Mean absolute error for one criterion, over the essays where the teacher actually re-marked it.</summary>
    private static decimal? CriterionMae(IEnumerable<(decimal? Ai, decimal? Teacher)> pairs)
    {
        var deltas = pairs.Where(p => p.Ai is not null && p.Teacher is not null)
            .Select(p => Math.Abs(p.Ai!.Value - p.Teacher!.Value))
            .ToList();
        return deltas.Count == 0 ? null : Round(deltas.Average());
    }
}
