using EduCenter.Api.Authorization;
using EduCenter.Infrastructure;
using EduCenter.Modules.Ai.Domain;
using EduCenter.SharedKernel.Authorization;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Ai;

/// <summary>The three numbers of AI use that fit on a dashboard card: how many requests, what they cost, and how many students used it.</summary>
public record AiUsageBrief(int Requests, decimal CostUsd, int ActiveStudents);

/// <summary>
/// Which AI requests a report covers and how it counts them — shared by the AI usage report (<c>/ai/usage</c>) and the dashboard's "AI
/// ishlatilishi" card, so the two screens can never show different numbers for the same period. Periods are UTC days: usage monitoring is
/// a technical measure, not a branch-local business fact.
/// </summary>
public static class AiUsageQueries
{
    /// <summary>The requests in the period the caller may see. An organization-wide viewer also sees requests that carry no branch (staff
    /// accounts outside a branch), unless one branch is asked for; a branch-scoped one sees only their own branches.</summary>
    public static IQueryable<AiRequest> Requests(
        this AppDbContext db, ResolvedScope scope, IReadOnlyCollection<Guid> visibleBranchIds, Guid? branchId, DateOnly from, DateOnly to)
    {
        var fetchFrom = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var fetchTo = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var query = db.AiRequests.AsNoTracking()
            .Where(r => r.OrganizationId == scope.OrganizationId && r.CreatedAt >= fetchFrom && r.CreatedAt < fetchTo);

        if (branchId is not null)
            return query.Where(r => r.BranchId == branchId);
        if (scope.Scope != ScopeType.AllOrganization)
        {
            var ids = visibleBranchIds.ToList();
            return query.Where(r => r.BranchId != null && ids.Contains(r.BranchId.Value));
        }

        return query;
    }

    /// <summary>The brief, counted in the database (a dashboard opens far more often than the report, and needs no rows).</summary>
    public static async Task<AiUsageBrief> BriefAsync(this IQueryable<AiRequest> requests, CancellationToken ct = default)
    {
        var row = await requests.GroupBy(_ => 1)
            .Select(g => new
            {
                Requests = g.Count(),
                Cost = g.Sum(r => r.CostUsd),
                Students = g.Where(r => r.StudentId != null).Select(r => r.StudentId).Distinct().Count(),
            })
            .FirstOrDefaultAsync(ct);
        return row is null ? new AiUsageBrief(0, 0, 0) : new AiUsageBrief(row.Requests, row.Cost, row.Students);
    }

    public static AiUsageTotalsDto Totals(IReadOnlyCollection<AiRequest> requests)
    {
        var succeeded = requests.Where(r => r.Status == AiRequestStatus.Succeeded).ToList();
        return new AiUsageTotalsDto(
            requests.Count,
            succeeded.Count,
            requests.Count(r => r.Status == AiRequestStatus.Failed),
            requests.Count(r => r.Status == AiRequestStatus.Rejected),
            requests.Sum(r => r.InputTokens),
            requests.Sum(r => r.OutputTokens),
            requests.Sum(r => r.CostUsd),
            Average(requests.Where(r => r.LatencyMs > 0).Select(r => r.LatencyMs)),
            (int)Math.Round(succeeded.Sum(r => r.AudioSeconds) / 60.0),
            succeeded.Count(r => r.Feature == AiFeature.Tutor && r.TaskKind == AiTaskKind.TutorAnswer),
            succeeded.Count(r => r.TaskKind == AiTaskKind.WritingEvaluation));
    }

    public static int Average(IEnumerable<int> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? 0 : (int)Math.Round(list.Average());
    }
}
