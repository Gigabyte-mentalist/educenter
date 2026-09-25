using EduCenter.Api.Authorization;
using EduCenter.Api.Automation;
using EduCenter.Infrastructure;
using EduCenter.Modules.Finance.Domain;
using EduCenter.SharedKernel.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Finance;

public record MethodTotalDto(PaymentMethod Method, decimal Amount, int Count);

public record BranchFinanceDto(Guid BranchId, string BranchName, decimal Collected, decimal Refunded, decimal NetCollected,
    decimal ExpectedRevenue, decimal Discounts, decimal OverdueNow, int DebtorsNow);

/// <summary>
/// Collected/Refunded/NetCollected — cash movement dated inside the period (branch-local dates).
/// Billed/Discounts/Credited/ExpectedRevenue/PaidOnBilled/OutstandingOnBilled — invoices whose period starts inside
/// the period ("what this month should bring in, and how much of it has come in").
/// OverdueNow/DebtorsNow — today's debt, independent of the chosen period.
/// </summary>
public record FinanceSummaryDto(
    DateOnly From, DateOnly To,
    decimal Collected, decimal Refunded, decimal NetCollected,
    decimal Billed, decimal Discounts, decimal Credited, decimal ExpectedRevenue, decimal PaidOnBilled, decimal OutstandingOnBilled,
    decimal OverdueNow, int DebtorsNow,
    List<MethodTotalDto> ByMethod, List<BranchFinanceDto> ByBranch);

/// <summary>Branch finance dashboard numbers (docs/00_TZ.md §5.2 "Finance": revenue, expected revenue, paid, overdue,
/// debt, discounts, refunds), permission <c>finance.reports.view</c>.</summary>
[ApiController]
[Route("api/finance/reports")]
public class FinanceReportsController(AppDbContext db) : ControllerBase
{
    [HttpGet("summary")]
    [Authorize(Policy = "finance.reports.view")]
    public async Task<ActionResult<FinanceSummaryDto>> Summary([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? branchId)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (scope.Scope is not (ScopeType.AllOrganization or ScopeType.MyBranches)) return Forbid();

        var now = DateTimeOffset.UtcNow;
        var utcToday = DateOnly.FromDateTime(now.UtcDateTime);
        var start = from ?? new DateOnly(utcToday.Year, utcToday.Month, 1);
        var end = to ?? start.AddMonths(1).AddDays(-1);
        if (end < start) return BadRequest(new { error = "Tugash sanasi boshlanish sanasidan oldin bo'lmasin." });
        if (end.DayNumber - start.DayNumber > 366) return BadRequest(new { error = "Hisobot davri bir yildan oshmasligi kerak." });

        var branchQuery = db.Branches.Where(b => b.OrganizationId == scope.OrganizationId);
        if (scope.Scope == ScopeType.MyBranches) branchQuery = branchQuery.Where(b => scope.BranchIds.Contains(b.Id));
        if (branchId is not null)
        {
            if (!await branchQuery.AnyAsync(b => b.Id == branchId)) return Forbid();
            branchQuery = branchQuery.Where(b => b.Id == branchId);
        }

        var branches = await branchQuery.OrderBy(b => b.Name).Select(b => new { b.Id, b.Name, b.Timezone }).ToListAsync();
        var branchIds = branches.Select(b => b.Id).ToList();
        var timezone = branches.ToDictionary(b => b.Id, b => b.Timezone);
        bool InPeriod(Guid branch, DateTimeOffset instant)
        {
            var local = BranchClock.LocalDate(timezone[branch], instant);
            return local >= start && local <= end;
        }

        // Cash is fetched with a day of margin on both sides, then filtered by each branch's local calendar date.
        var fetchFrom = new DateTimeOffset(start.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var fetchTo = new DateTimeOffset(end.AddDays(2).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var payments = (await db.Payments
                .Where(p => p.OrganizationId == scope.OrganizationId && branchIds.Contains(p.BranchId) && !p.IsVoided
                    && p.PaidAt >= fetchFrom && p.PaidAt < fetchTo)
                .Select(p => new { p.BranchId, p.Amount, p.Method, p.PaidAt })
                .ToListAsync())
            .Where(p => InPeriod(p.BranchId, p.PaidAt))
            .ToList();

        var refunds = (await db.Refunds
                .Where(r => r.OrganizationId == scope.OrganizationId && branchIds.Contains(r.BranchId)
                    && r.RefundedAt >= fetchFrom && r.RefundedAt < fetchTo)
                .Select(r => new { r.BranchId, r.Amount, r.RefundedAt })
                .ToListAsync())
            .Where(r => InPeriod(r.BranchId, r.RefundedAt))
            .ToList();

        var billed = await db.Invoices
            .Where(i => i.OrganizationId == scope.OrganizationId && branchIds.Contains(i.BranchId)
                // A split invoice is counted through its parts; counting it as well would bill the same money twice.
                && i.Status != InvoiceStatus.Cancelled && i.Status != InvoiceStatus.Split && i.PeriodStart >= start && i.PeriodStart <= end)
            .Select(i => new { i.BranchId, i.Amount, i.DiscountAmount, i.CreditedAmount, i.PaidAmount })
            .ToListAsync();

        var overdue = (await db.Invoices
                .Where(i => i.OrganizationId == scope.OrganizationId && branchIds.Contains(i.BranchId)
                    && (i.Status == InvoiceStatus.Pending || i.Status == InvoiceStatus.PartiallyPaid)
                    && i.Amount - i.DiscountAmount - i.CreditedAmount > i.PaidAmount)
                .Select(i => new { i.BranchId, i.StudentId, i.DueDate, Balance = i.Amount - i.DiscountAmount - i.CreditedAmount - i.PaidAmount })
                .ToListAsync())
            .Where(i => i.DueDate < BranchClock.Today(timezone[i.BranchId], now))
            .ToList();

        var byBranch = branches
            .Select(b =>
            {
                var collected = payments.Where(p => p.BranchId == b.Id).Sum(p => p.Amount);
                var refunded = refunds.Where(r => r.BranchId == b.Id).Sum(r => r.Amount);
                var branchBilled = billed.Where(i => i.BranchId == b.Id).ToList();
                var branchOverdue = overdue.Where(i => i.BranchId == b.Id).ToList();
                return new BranchFinanceDto(b.Id, b.Name, collected, refunded, collected - refunded,
                    branchBilled.Sum(i => i.Amount - i.DiscountAmount - i.CreditedAmount),
                    branchBilled.Sum(i => i.DiscountAmount),
                    branchOverdue.Sum(i => i.Balance), branchOverdue.Select(i => i.StudentId).Distinct().Count());
            })
            .ToList();

        var byMethod = payments
            .GroupBy(p => p.Method)
            .Select(g => new MethodTotalDto(g.Key, g.Sum(p => p.Amount), g.Count()))
            .OrderByDescending(m => m.Amount)
            .ToList();

        var totalCollected = payments.Sum(p => p.Amount);
        var totalRefunded = refunds.Sum(r => r.Amount);
        var expected = billed.Sum(i => i.Amount - i.DiscountAmount - i.CreditedAmount);
        var paidOnBilled = billed.Sum(i => i.PaidAmount);

        return Ok(new FinanceSummaryDto(start, end,
            totalCollected, totalRefunded, totalCollected - totalRefunded,
            billed.Sum(i => i.Amount), billed.Sum(i => i.DiscountAmount), billed.Sum(i => i.CreditedAmount),
            expected, paidOnBilled, expected - paidOnBilled,
            overdue.Sum(i => i.Balance), overdue.Select(i => i.StudentId).Distinct().Count(),
            byMethod, byBranch));
    }
}
