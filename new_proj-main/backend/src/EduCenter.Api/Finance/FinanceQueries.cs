using EduCenter.Api.Authorization;
using EduCenter.Api.Automation;
using EduCenter.Infrastructure;
using System.Linq.Expressions;
using EduCenter.Modules.Finance.Domain;
using EduCenter.SharedKernel;
using EduCenter.SharedKernel.Authorization;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Finance;

public record InvoiceDiscountDto(Guid Id, DiscountType Type, decimal Value, decimal Amount, string Reason,
    string? PromoCode, string? AppliedByName, DateTimeOffset CreatedAt);

public record InvoiceDto(
    Guid Id, Guid StudentId, string StudentName, string StudentNumber, Guid GroupId, string GroupName, Guid BranchId, Guid EnrollmentId,
    string Description, DateOnly PeriodStart, DateOnly PeriodEnd, DateOnly DueDate,
    decimal Amount, decimal DiscountAmount, decimal CreditedAmount, decimal NetAmount, decimal PaidAmount, decimal Balance,
    string Currency, InvoiceStatus Status, bool IsOverdue, int DaysOverdue, string? CancelReason,
    InvoiceDiscountDto? Discount, DateTimeOffset CreatedAt,
    Guid? ParentInvoiceId = null, int? InstallmentNumber = null, int? InstallmentCount = null);

public record PaymentDto(
    Guid Id, Guid InvoiceId, Guid StudentId, string StudentName, string InvoiceDescription,
    decimal Amount, decimal RefundedAmount, PaymentMethod Method,
    DateTimeOffset PaidAt, Guid ReceivedBy, string? ReceivedByName, string? Note,
    bool IsVoided, DateTimeOffset? VoidedAt, string? VoidReason);

public record RefundDto(Guid Id, Guid PaymentId, Guid InvoiceId, Guid StudentId, string StudentName, decimal Amount,
    string Reason, string? ApprovedByName, DateTimeOffset RefundedAt);

public record DebtorDto(
    Guid StudentId, string StudentName, string StudentNumber, string? Phone, decimal TotalDebt, int OverdueInvoices,
    DateOnly OldestDueDate, int DaysOverdue, List<string> Groups);

/// <param name="OverdueDebt">Balance of every open invoice that is past due.</param>
/// <param name="TodayIncome">Payments taken today (branch calendar); null when the caller may not see payments.</param>
public record FinanceOverviewDto(decimal OverdueDebt, int Debtors, int OpenInvoices, decimal? TodayIncome);

public record StudentFinanceDto(decimal TotalBalance, decimal OverdueAmount, DateOnly? NextDueDate,
    List<InvoiceDto> Invoices, List<PaymentDto> Payments, List<RefundDto> Refunds);

/// <summary>
/// Scoping and read models shared by the finance controllers. Every finance row carries its BranchId and
/// StudentId, so organization/branch scopes filter by branch and personal scopes (Self, MyChildren) by student.
/// </summary>
public class FinanceQueries(AppDbContext db)
{
    public async Task<IQueryable<Invoice>> ScopedInvoicesAsync(ResolvedScope scope)
    {
        var query = db.Invoices.Where(i => i.OrganizationId == scope.OrganizationId);
        switch (scope.Scope)
        {
            case ScopeType.AllOrganization:
                return query;
            case ScopeType.MyBranches:
                return query.Where(i => scope.BranchIds.Contains(i.BranchId));
            case ScopeType.Self:
            case ScopeType.MyChildren:
                var studentIds = await scope.VisibleStudentIdsAsync(db);
                return query.Where(i => studentIds.Contains(i.StudentId));
            default:
                return query.Where(_ => false);
        }
    }

    public async Task<IQueryable<Payment>> ScopedPaymentsAsync(ResolvedScope scope)
    {
        var query = db.Payments.Where(p => p.OrganizationId == scope.OrganizationId);
        switch (scope.Scope)
        {
            case ScopeType.AllOrganization:
                return query;
            case ScopeType.MyBranches:
                return query.Where(p => scope.BranchIds.Contains(p.BranchId));
            case ScopeType.Self:
            case ScopeType.MyChildren:
                var studentIds = await scope.VisibleStudentIdsAsync(db);
                return query.Where(p => studentIds.Contains(p.StudentId));
            default:
                return query.Where(_ => false);
        }
    }

    public async Task<IQueryable<Refund>> ScopedRefundsAsync(ResolvedScope scope)
    {
        var query = db.Refunds.Where(r => r.OrganizationId == scope.OrganizationId);
        switch (scope.Scope)
        {
            case ScopeType.AllOrganization:
                return query;
            case ScopeType.MyBranches:
                return query.Where(r => scope.BranchIds.Contains(r.BranchId));
            case ScopeType.Self:
            case ScopeType.MyChildren:
                var studentIds = await scope.VisibleStudentIdsAsync(db);
                return query.Where(r => studentIds.Contains(r.StudentId));
            default:
                return query.Where(_ => false);
        }
    }

    /// <summary>Discounts are managed by staff only (discount.apply), so there is no personal scope here.</summary>
    public IQueryable<Discount> ScopedDiscounts(ResolvedScope scope)
    {
        var query = db.Discounts.Where(d => d.OrganizationId == scope.OrganizationId);
        return scope.Scope switch
        {
            ScopeType.AllOrganization => query,
            ScopeType.MyBranches => query.Where(d => scope.BranchIds.Contains(d.BranchId)),
            _ => query.Where(_ => false)
        };
    }

    // Member-initialised, not a positional record: EF binds an OrderBy over `r.Invoice.DueDate` through member bindings, but not through
    // constructor arguments.
    private sealed class InvoiceRow
    {
        public required Invoice Invoice { get; init; }
        public required string StudentName { get; init; }
        public required string StudentNumber { get; init; }
        public required string GroupName { get; init; }
        public required string Timezone { get; init; }
    }

    private IQueryable<InvoiceRow> InvoiceRows(IQueryable<Invoice> invoices, string? search = null)
    {
        var rows =
            from i in invoices
            join s in db.Students on i.StudentId equals s.Id
            join u in db.Users on s.UserId equals u.Id
            join g in db.Groups on i.GroupId equals g.Id
            join b in db.Branches on i.BranchId equals b.Id
            select new InvoiceRow { Invoice = i, StudentName = u.FullName, StudentNumber = s.StudentNumber, GroupName = g.Name, Timezone = b.Timezone };

        if (string.IsNullOrWhiteSpace(search)) return rows;
        var term = PagingExtensions.ContainsPattern(search);
        return rows.Where(r => EF.Functions.ILike(r.StudentName, term) || EF.Functions.ILike(r.StudentNumber, term));
    }

    /// <summary>One page of invoices, newest due date first. Search matches the student's name or number.</summary>
    public async Task<Page<InvoiceDto>> ProjectInvoicePageAsync(
        IQueryable<Invoice> invoices, string? search, PageRequest? paging, DateTimeOffset now, CancellationToken ct = default)
    {
        var page = await InvoiceRows(invoices, search)
            .OrderByDescending(r => r.Invoice.DueDate).ThenBy(r => r.Invoice.Id)
            .ToPageAsync(paging, ct);
        return new Page<InvoiceDto>(await ToInvoiceDtosAsync(page.Items, now), page.Total, page.PageNumber, page.PageSize);
    }

    public async Task<List<InvoiceDto>> ProjectInvoicesAsync(IQueryable<Invoice> invoices, DateTimeOffset now) =>
        (await ToInvoiceDtosAsync(await InvoiceRows(invoices).ToListAsync(), now)).OrderByDescending(i => i.DueDate).ToList();

    private async Task<List<InvoiceDto>> ToInvoiceDtosAsync(IReadOnlyList<InvoiceRow> rows, DateTimeOffset now)
    {
        var invoiceIds = rows.Select(r => r.Invoice.Id).ToList();
        var discounts = await (
            from d in db.Discounts
            where invoiceIds.Contains(d.InvoiceId) && d.RevokedAt == null
            select new
            {
                d.InvoiceId,
                Dto = new InvoiceDiscountDto(d.Id, d.Type, d.Value, d.Amount, d.Reason,
                    db.PromoCodes.Where(p => p.Id == d.PromoCodeId).Select(p => p.Code).FirstOrDefault(),
                    db.Users.Where(u => u.Id == d.AppliedBy).Select(u => u.FullName).FirstOrDefault(),
                    d.CreatedAt)
            }
        ).ToDictionaryAsync(x => x.InvoiceId, x => x.Dto);

        return rows
            .Select(r =>
            {
                var i = r.Invoice;
                var today = BranchClock.Today(r.Timezone, now);
                var overdue = i.IsOverdue(today);
                return new InvoiceDto(i.Id, i.StudentId, r.StudentName, r.StudentNumber, i.GroupId, r.GroupName, i.BranchId, i.EnrollmentId,
                    i.Description, i.PeriodStart, i.PeriodEnd, i.DueDate,
                    i.Amount, i.DiscountAmount, i.CreditedAmount, i.NetAmount, i.PaidAmount, i.Balance,
                    i.Currency, i.Status, overdue, overdue ? today.DayNumber - i.DueDate.DayNumber : 0, i.CancelReason,
                    discounts.GetValueOrDefault(i.Id), i.CreatedAt, i.ParentInvoiceId, i.InstallmentNumber, i.InstallmentCount);
            })
            .ToList();
    }

    /// <summary>
    /// Restricts invoices to the ones that are open, carry a balance and are past due <em>on the branch's own calendar</em> —
    /// "overdue" is not stored (see <see cref="Invoice"/>), and the date it compares against depends on the branch's time zone. The
    /// comparison is built per distinct zone and runs in SQL, so a list of debtors is a query, not every open invoice in memory.
    /// </summary>
    public async Task<IQueryable<Invoice>> OpenOverdueAsync(IQueryable<Invoice> invoices, DateTimeOffset now)
    {
        var zones = (await db.Branches.Select(b => new { b.Id, b.Timezone }).ToListAsync())
            .GroupBy(b => b.Timezone)
            .Select(g => (BranchIds: g.Select(b => b.Id).ToList(), Today: BranchClock.Today(g.Key, now)))
            .ToList();

        var i = Expression.Parameter(typeof(Invoice), "i");
        Expression? pastDue = null;
        foreach (var (branchIds, today) in zones)
        {
            var inBranches = Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), [typeof(Guid)],
                Expression.Constant(branchIds), Expression.Property(i, nameof(Invoice.BranchId)));
            var before = Expression.LessThan(Expression.Property(i, nameof(Invoice.DueDate)), Expression.Constant(today));
            var both = Expression.AndAlso(inBranches, before);
            pastDue = pastDue is null ? both : Expression.OrElse(pastDue, both);
        }

        var pastDueFilter = Expression.Lambda<Func<Invoice, bool>>(pastDue ?? Expression.Constant(false), i);
        return invoices
            .Where(x => (x.Status == InvoiceStatus.Pending || x.Status == InvoiceStatus.PartiallyPaid)
                        && x.Amount - x.DiscountAmount - x.CreditedAmount > x.PaidAmount)
            .Where(pastDueFilter);
    }

    /// <summary>One page of payments, newest first.</summary>
    public Task<Page<PaymentDto>> ProjectPaymentPageAsync(
        IQueryable<Payment> payments, string? search, PageRequest? paging, CancellationToken ct = default)
    {
        var rows =
            from p in payments
            join s in db.Students on p.StudentId equals s.Id
            join u in db.Users on s.UserId equals u.Id
            join i in db.Invoices on p.InvoiceId equals i.Id
            select new { p, u.FullName, s.StudentNumber, i.Description };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = PagingExtensions.ContainsPattern(search);
            rows = rows.Where(r => EF.Functions.ILike(r.FullName, term) || EF.Functions.ILike(r.StudentNumber, term));
        }

        return rows
            .OrderByDescending(r => r.p.PaidAt).ThenBy(r => r.p.Id)
            .Select(r => new PaymentDto(r.p.Id, r.p.InvoiceId, r.p.StudentId, r.FullName, r.Description, r.p.Amount, r.p.RefundedAmount, r.p.Method,
                r.p.PaidAt, r.p.ReceivedBy, db.Users.Where(x => x.Id == r.p.ReceivedBy).Select(x => x.FullName).FirstOrDefault(),
                r.p.Note, r.p.IsVoided, r.p.VoidedAt, r.p.VoidReason))
            .ToPageAsync(paging, ct);
    }

    /// <summary>One page of refunds, newest first.</summary>
    public Task<Page<RefundDto>> ProjectRefundPageAsync(IQueryable<Refund> refunds, PageRequest? paging, CancellationToken ct = default) =>
        (from r in refunds
         join s in db.Students on r.StudentId equals s.Id
         join u in db.Users on s.UserId equals u.Id
         orderby r.RefundedAt descending, r.Id
         select new RefundDto(r.Id, r.PaymentId, r.InvoiceId, r.StudentId, u.FullName, r.Amount, r.Reason,
             db.Users.Where(a => a.Id == r.ApprovedBy).Select(a => a.FullName).FirstOrDefault(), r.RefundedAt))
        .ToPageAsync(paging, ct);

    public Task<List<PaymentDto>> ProjectPaymentsAsync(IQueryable<Payment> payments) =>
        (from p in payments
         join s in db.Students on p.StudentId equals s.Id
         join u in db.Users on s.UserId equals u.Id
         join i in db.Invoices on p.InvoiceId equals i.Id
         orderby p.PaidAt descending
         select new PaymentDto(p.Id, p.InvoiceId, p.StudentId, u.FullName, i.Description, p.Amount, p.RefundedAmount, p.Method, p.PaidAt,
             p.ReceivedBy, db.Users.Where(r => r.Id == p.ReceivedBy).Select(r => r.FullName).FirstOrDefault(),
             p.Note, p.IsVoided, p.VoidedAt, p.VoidReason))
        .Take(500)
        .ToListAsync();

    public Task<List<RefundDto>> ProjectRefundsAsync(IQueryable<Refund> refunds) =>
        (from r in refunds
         join s in db.Students on r.StudentId equals s.Id
         join u in db.Users on s.UserId equals u.Id
         orderby r.RefundedAt descending
         select new RefundDto(r.Id, r.PaymentId, r.InvoiceId, r.StudentId, u.FullName, r.Amount, r.Reason,
             db.Users.Where(a => a.Id == r.ApprovedBy).Select(a => a.FullName).FirstOrDefault(), r.RefundedAt))
        .Take(500)
        .ToListAsync();
}
