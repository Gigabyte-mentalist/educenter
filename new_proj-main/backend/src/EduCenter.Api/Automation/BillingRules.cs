using System.Globalization;
using EduCenter.Api.Notifications;
using EduCenter.Infrastructure;
using EduCenter.Modules.Automation.Domain;
using EduCenter.Modules.Crm.Domain;
using EduCenter.Modules.Finance.Domain;
using EduCenter.Modules.Notifications.Domain;
using EduCenter.Modules.Students.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EduCenter.Api.Automation;

public class FinanceOptions
{
    public string Currency { get; set; } = "UZS";

    /// <summary>Largest discount (percent of the invoice amount) a branch-scoped holder of discount.apply may give.</summary>
    public decimal AdministratorMaxDiscountPercent { get; set; } = 20;

    /// <summary>Largest single refund (so'm) a branch-scoped holder of refund.approve may approve.</summary>
    public decimal AdministratorMaxRefundAmount { get; set; } = 1_000_000;
}

public record BillingResult(int InvoicesIssued, int StudentsMarkedDebt, int StudentsCleared, int DebtTasks);

/// <summary>
/// Monthly tuition billing and its consequences (docs/05_phase3_technical_plan.md §1): issue invoices for periods that
/// have started and keep Student.Status in step with overdue balances — both always on, they are the books — and open
/// a Debt task in the operator's queue per the organization's DEBT_FOLLOW_UP rule (Automation Engine, TZ §40).
/// Every step is idempotent, so it runs on the schedule and on write paths (conversion, payment) alike.
/// </summary>
public class BillingRules(AppDbContext db, StaffResolver staff, NotificationDispatcher notifier, NotificationRecipients recipients,
    AutomationSettings settings, AutomationJournal journal, IOptions<FinanceOptions> options, ILogger<BillingRules> logger)
{
    /// <summary>How far ahead a student may be invoiced when paying in advance.</summary>
    public const int MaxAdvanceMonths = 12;

    public async Task<BillingResult> RunAsync(DateTimeOffset now, Guid? organizationId = null, Guid? studentId = null,
        AutomationTrigger trigger = AutomationTrigger.Sweep, CancellationToken ct = default)
    {
        var issued = await IssueDueInvoicesAsync(now, organizationId: organizationId, studentId: studentId, ct: ct);
        var (debt, cleared) = await SyncDebtStatusAsync(now, organizationId, studentId, ct);
        var tasks = await RunDebtFollowUpsAsync(now, organizationId, studentId, trigger, ct);
        return new BillingResult(issued, debt, cleared, tasks);
    }

    /// <summary>Issues the invoice for every billing period that has started and has none yet. An enrollment
    /// with no invoice at all is billed from its <em>current</em> period — history from before billing was
    /// switched on (or before the course had a price) is never back-billed.</summary>
    public async Task<int> IssueDueInvoicesAsync(DateTimeOffset now, Guid? enrollmentId = null, Guid? organizationId = null,
        Guid? studentId = null, CancellationToken ct = default)
    {
        var candidates = await (
            from e in db.Enrollments
            join g in db.Groups on e.GroupId equals g.Id
            join c in db.Courses on g.CourseId equals c.Id
            join b in db.Branches on g.BranchId equals b.Id
            where e.Status == EnrollmentStatus.Active && c.MonthlyFee > 0
                && (enrollmentId == null || e.Id == enrollmentId)
                && (organizationId == null || e.OrganizationId == organizationId)
                && (studentId == null || e.StudentId == studentId)
            select new
            {
                EnrollmentId = e.Id, e.OrganizationId, e.StudentId, e.GroupId, e.EnrolledAt,
                g.BranchId, GroupName = g.Name, c.MonthlyFee, b.Timezone
            }
        ).ToListAsync(ct);
        if (candidates.Count == 0) return 0;

        var enrollmentIds = candidates.Select(c => c.EnrollmentId).ToList();
        var latestPeriod = await db.Invoices
            .Where(i => enrollmentIds.Contains(i.EnrollmentId))
            .GroupBy(i => i.EnrollmentId)
            .Select(g => new { EnrollmentId = g.Key, Latest = g.Max(i => i.PeriodStart) })
            .ToDictionaryAsync(x => x.EnrollmentId, x => x.Latest, ct);

        var issued = 0;
        foreach (var c in candidates)
        {
            var anchor = BranchClock.LocalDate(c.Timezone, c.EnrolledAt);
            var today = BranchClock.Today(c.Timezone, now);
            var current = PeriodIndex(anchor, today);
            if (current < 0) continue;

            var first = latestPeriod.TryGetValue(c.EnrollmentId, out var latest)
                ? MonthsBetween(anchor, latest) + 1
                : current;

            for (var n = first; n <= current; n++)
            {
                // Always re-derived from the anchor, so a 31st-of-month enrollment bills on the 31st again
                // after a short month instead of drifting to the 28th forever.
                var periodStart = anchor.AddMonths(n);
                var periodEnd = anchor.AddMonths(n + 1);
                var description = $"{c.GroupName} · {periodStart:dd.MM.yyyy} – {periodEnd.AddDays(-1):dd.MM.yyyy}";

                db.Invoices.Add(new Invoice(c.OrganizationId, c.BranchId, c.StudentId, c.EnrollmentId, c.GroupId,
                    description, periodStart, periodEnd, dueDate: periodStart, c.MonthlyFee, options.Value.Currency));
                issued++;
            }
        }

        if (issued > 0) await db.SaveChangesAsync(ct);
        return issued;
    }

    /// <summary>Pay ahead: issues up to <paramref name="months"/> invoices after the enrollment's latest one (or from the
    /// current period if it has none), never more than <see cref="MaxAdvanceMonths"/> periods past the current one.</summary>
    public async Task<int> IssueAdvanceInvoicesAsync(DateTimeOffset now, Guid enrollmentId, int months, CancellationToken ct = default)
    {
        if (months < 1) return 0;

        var c = await (
            from e in db.Enrollments
            join g in db.Groups on e.GroupId equals g.Id
            join course in db.Courses on g.CourseId equals course.Id
            join b in db.Branches on g.BranchId equals b.Id
            where e.Id == enrollmentId && e.Status == EnrollmentStatus.Active && course.MonthlyFee > 0
            select new { e.OrganizationId, e.StudentId, e.GroupId, e.EnrolledAt, g.BranchId, GroupName = g.Name, course.MonthlyFee, b.Timezone }
        ).FirstOrDefaultAsync(ct);
        if (c is null) return 0;

        var anchor = BranchClock.LocalDate(c.Timezone, c.EnrolledAt);
        var current = Math.Max(PeriodIndex(anchor, BranchClock.Today(c.Timezone, now)), 0);
        var latest = await db.Invoices.Where(i => i.EnrollmentId == enrollmentId).Select(i => (DateOnly?)i.PeriodStart).MaxAsync(ct);

        var first = latest is null ? current : MonthsBetween(anchor, latest.Value) + 1;
        var last = Math.Min(first + months - 1, current + MaxAdvanceMonths);

        var issued = 0;
        for (var n = first; n <= last; n++)
        {
            var periodStart = anchor.AddMonths(n);
            var periodEnd = anchor.AddMonths(n + 1);
            db.Invoices.Add(new Invoice(c.OrganizationId, c.BranchId, c.StudentId, enrollmentId, c.GroupId,
                $"{c.GroupName} · {periodStart:dd.MM.yyyy} – {periodEnd.AddDays(-1):dd.MM.yyyy}",
                periodStart, periodEnd, dueDate: periodStart, c.MonthlyFee, options.Value.Currency));
            issued++;
        }

        if (issued > 0) await db.SaveChangesAsync(ct);
        return issued;
    }

    /// <summary>Active ⇄ Debt from overdue balances; also cancels open debt tasks that no longer have a debt
    /// behind them (the student paid, or the invoice was cancelled). Frozen/Dropped/etc. are left alone.</summary>
    public async Task<(int MarkedDebt, int Cleared)> SyncDebtStatusAsync(DateTimeOffset now, Guid? organizationId, Guid? studentId,
        CancellationToken ct = default)
    {
        var open = await (
            from i in db.Invoices
            join b in db.Branches on i.BranchId equals b.Id
            where (i.Status == InvoiceStatus.Pending || i.Status == InvoiceStatus.PartiallyPaid)
                && i.Amount - i.DiscountAmount - i.CreditedAmount > i.PaidAmount
                && (organizationId == null || i.OrganizationId == organizationId)
                && (studentId == null || i.StudentId == studentId)
            select new { i.StudentId, i.DueDate, b.Timezone }
        ).ToListAsync(ct);

        var overdueIds = open
            .Where(x => x.DueDate < BranchClock.Today(x.Timezone, now))
            .Select(x => x.StudentId)
            .Distinct()
            .ToList();

        var toChange = await db.Students
            .Where(s => (organizationId == null || s.OrganizationId == organizationId)
                && (studentId == null || s.Id == studentId)
                && ((s.Status == StudentStatus.Active && overdueIds.Contains(s.Id))
                    || (s.Status == StudentStatus.Debt && !overdueIds.Contains(s.Id))))
            .ToListAsync(ct);

        var markedDebt = 0;
        var cleared = 0;
        foreach (var student in toChange)
        {
            if (student.Status == StudentStatus.Active)
            {
                student.SetStatus(StudentStatus.Debt);
                markedDebt++;
            }
            else
            {
                student.SetStatus(StudentStatus.Active);
                cleared++;
            }
        }

        var staleTasks = await db.CrmTasks
            .Where(t => t.ReasonCode == CrmTaskReason.DebtOverdue && t.Status == CrmTaskStatus.Open
                && t.StudentId != null && !overdueIds.Contains(t.StudentId.Value)
                && (organizationId == null || t.OrganizationId == organizationId)
                && (studentId == null || t.StudentId == studentId))
            .ToListAsync(ct);
        foreach (var task in staleTasks) task.Cancel();

        if (toChange.Count > 0 || staleTasks.Count > 0) await db.SaveChangesAsync(ct);
        return (markedDebt, cleared);
    }

    /// <summary>§10 Debt follow-up: an unpaid invoice the configured number of days past due → one Debt task per student
    /// with the total overdue amount. A new task appears only when a newer invoice crosses the threshold, so a
    /// completed call is not re-queued every run for the same debt.</summary>
    public async Task<int> RunDebtFollowUpsAsync(DateTimeOffset now, Guid? organizationId, Guid? studentId,
        AutomationTrigger trigger = AutomationTrigger.Sweep, CancellationToken ct = default)
    {
        var rows = await (
            from i in db.Invoices
            join b in db.Branches on i.BranchId equals b.Id
            where (i.Status == InvoiceStatus.Pending || i.Status == InvoiceStatus.PartiallyPaid)
                && i.Amount - i.DiscountAmount - i.CreditedAmount > i.PaidAmount
                && (organizationId == null || i.OrganizationId == organizationId)
                && (studentId == null || i.StudentId == studentId)
            select new { i.OrganizationId, i.StudentId, i.BranchId, i.DueDate, Balance = i.Amount - i.DiscountAmount - i.CreditedAmount - i.PaidAmount, b.Timezone }
        ).ToListAsync(ct);

        var created = 0;
        foreach (var organizationRows in rows.GroupBy(r => r.OrganizationId))
        {
            var rule = await settings.GetAsync(organizationRows.Key, AutomationRuleCode.DebtFollowUp, ct);
            if (!rule.Enabled || !rule.Action(AutomationCatalog.Keys.CreateTask)) continue;

            var days = rule.Param(AutomationCatalog.Keys.Days);
            var late = organizationRows
                .Where(r => r.DueDate.AddDays(days) <= BranchClock.Today(r.Timezone, now))
                .GroupBy(r => r.StudentId)
                .ToList();

            var organizationCreated = 0;
            foreach (var student in late)
            {
                var newest = student.MaxBy(r => r.DueDate)!;
                var oldest = student.MinBy(r => r.DueDate)!;
                var triggeredAt = new DateTimeOffset(newest.DueDate.AddDays(days).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

                var covered = await db.CrmTasks.AnyAsync(t =>
                    t.StudentId == student.Key && t.ReasonCode == CrmTaskReason.DebtOverdue
                    && (t.Status == CrmTaskStatus.Open || t.CreatedAt >= triggeredAt), ct);
                if (covered) continue;

                var assignee = await staff.ResolveOperatorForStudentAsync(organizationRows.Key, student.Key, oldest.BranchId, ct);
                if (assignee is null)
                {
                    logger.LogWarning("No operator or administrator found for student {StudentId}; debt task not created.", student.Key);
                    continue;
                }

                var total = student.Sum(r => r.Balance);
                var daysLate = BranchClock.Today(oldest.Timezone, now).DayNumber - oldest.DueDate.DayNumber;

                db.CrmTasks.Add(new CrmTask(organizationRows.Key, leadId: null, student.Key, assignee.Value,
                    CrmTaskType.Debt, DateTimeOffset.UtcNow, CrmTaskReason.DebtOverdue,
                    $"Qarz: {FormatSum(total)} so'm, {daysLate} kun kechikkan. To'lov bo'yicha bog'laning."));
                await notifier.NotifyAsync(organizationRows.Key, [assignee.Value],
                    new NotificationMessage(NotificationType.TaskAssigned, "Yangi vazifa: qarzdorlik",
                        $"{await recipients.StudentNameAsync(student.Key)}: {FormatSum(total)} so'm, {daysLate} kun kechikkan.", "/crm/tasks"), ct);
                await db.SaveChangesAsync(ct);
                organizationCreated++;
            }

            journal.Record(organizationRows.Key, AutomationRuleCode.DebtFollowUp, trigger, organizationCreated,
                AutomationJournal.Describe((organizationCreated, "ta qarz vazifasi")));
            if (organizationCreated > 0) await db.SaveChangesAsync(ct);
            created += organizationCreated;
        }

        return created;
    }

    /// <summary>Index n of the period [anchor+n months, anchor+n+1 months) containing <paramref name="today"/>; -1 before the anchor.</summary>
    private static int PeriodIndex(DateOnly anchor, DateOnly today)
    {
        if (today < anchor) return -1;
        var months = MonthsBetween(anchor, today);
        return anchor.AddMonths(months) > today ? months - 1 : months;
    }

    private static int MonthsBetween(DateOnly from, DateOnly to) => (to.Year - from.Year) * 12 + to.Month - from.Month;

    private static string FormatSum(decimal value) =>
        value.ToString("#,0", CultureInfo.InvariantCulture).Replace(",", " ");
}
