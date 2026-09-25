using System.Text.Json;
using EduCenter.Api.Authorization;
using EduCenter.Api.Automation;
using EduCenter.Infrastructure;
using EduCenter.Modules.Finance.Domain;
using EduCenter.Modules.Students.Domain;
using EduCenter.Modules.Timeline.Domain;
using EduCenter.SharedKernel;
using EduCenter.SharedKernel.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Finance;

public record RecordPaymentRequest(decimal Amount, PaymentMethod Method, DateTimeOffset? PaidAt, string? Note);
public record FinanceReasonRequest(string? Reason);
public record AdvanceInvoicesRequest(Guid GroupId, int Months);

public record SplitPartRequest(decimal Amount, DateOnly DueDate);
public record SplitInvoiceRequest(List<SplitPartRequest>? Parts);

/// <param name="Invoice">The invoice that was cut — now Split, no longer owed itself.</param>
/// <param name="Parts">The instalments, in order: what the student owes from now on.</param>
public record SplitInvoiceResult(InvoiceDto Invoice, List<InvoiceDto> Parts);
public record AdvanceInvoicesResult(int InvoicesIssued);

/// <summary>
/// Finance core (docs/00_TZ.md §25, docs/05_phase3_technical_plan.md Sprint 1). Two view permissions on
/// purpose: <c>finance.debt.view</c> (invoices, debtors — the operator's follow-up needs) and
/// <c>payments.view</c> (who paid what, how, and who took the money — managers, and a student/parent for
/// their own account). Discounts, refunds and promo codes: <see cref="FinanceAdjustmentsController"/>.
/// </summary>
[ApiController]
[Route("api/finance")]
public class FinanceController(AppDbContext db, FinanceQueries finance, BillingRules billing, PermissionResolver permissions) : ControllerBase
{
    [HttpGet("invoices")]
    [Authorize(Policy = "finance.debt.view")]
    public async Task<ActionResult<Page<InvoiceDto>>> Invoices(
        [FromQuery] Guid? studentId, [FromQuery] InvoiceStatus? status, [FromQuery] bool? overdue, [FromQuery] bool? open,
        [FromQuery] string? search, [FromQuery] PageRequest? paging, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        var now = DateTimeOffset.UtcNow;

        var query = await finance.ScopedInvoicesAsync(scope);
        if (studentId is not null) query = query.Where(i => i.StudentId == studentId);
        if (status is not null) query = query.Where(i => i.Status == status);
        // "Open" = still to be paid (pending or part-paid); one status filter cannot say that.
        if (open == true) query = query.Where(i => i.Status == InvoiceStatus.Pending || i.Status == InvoiceStatus.PartiallyPaid);
        if (overdue == true) query = await finance.OpenOverdueAsync(query, now);
        else if (overdue == false)
        {
            var late = (await finance.OpenOverdueAsync(query, now)).Select(i => i.Id);
            query = query.Where(i => !late.Contains(i.Id));
        }

        return Ok(await finance.ProjectInvoicePageAsync(query, search, paging, now, ct));
    }

    /// <summary>Students who owe money past a due date, biggest debt first. Grouped and paged in the database — this used to load
    /// every open invoice of the organization into memory on each visit to the finance screen.</summary>
    [HttpGet("debtors")]
    [Authorize(Policy = "finance.debt.view")]
    public async Task<ActionResult<Page<DebtorDto>>> Debtors(
        [FromQuery] string? search, [FromQuery] PageRequest? paging, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        var now = DateTimeOffset.UtcNow;
        var page = paging ?? new PageRequest();

        var overdue = await finance.OpenOverdueAsync(await finance.ScopedInvoicesAsync(scope), now);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = PagingExtensions.ContainsPattern(search);
            overdue = overdue.Where(i => db.Students.Any(s => s.Id == i.StudentId
                && (EF.Functions.ILike(s.StudentNumber, term)
                    || db.Users.Any(u => u.Id == s.UserId && (EF.Functions.ILike(u.FullName, term) || EF.Functions.ILike(u.Phone, term))))));
        }

        var grouped = overdue
            .GroupBy(i => i.StudentId)
            .Select(g => new
            {
                StudentId = g.Key,
                Total = g.Sum(i => i.Amount - i.DiscountAmount - i.CreditedAmount - i.PaidAmount),
                Count = g.Count(),
                Oldest = g.Min(i => i.DueDate),
            });

        var total = await grouped.CountAsync(ct);
        if (total == 0) return Ok(new Page<DebtorDto>([], 0, page.Number, page.Take));

        var rows = await grouped.OrderByDescending(x => x.Total).ThenBy(x => x.StudentId).Skip(page.Skip).Take(page.Take).ToListAsync(ct);
        var studentIds = rows.Select(r => r.StudentId).ToList();

        var students = await (
            from s in db.Students
            join u in db.Users on s.UserId equals u.Id
            where studentIds.Contains(s.Id)
            select new { s.Id, u.FullName, u.Phone, s.StudentNumber }
        ).ToDictionaryAsync(x => x.Id, ct);

        // Only the page's own overdue invoices: which groups they are for, and the branch calendar the oldest one is measured on.
        var pageInvoices = await overdue.Where(i => studentIds.Contains(i.StudentId))
            .Select(i => new { i.StudentId, i.GroupId, i.BranchId, i.DueDate }).ToListAsync(ct);
        var groupNames = await db.Groups.Where(g => pageInvoices.Select(x => x.GroupId).Distinct().Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, g => g.Name, ct);
        var zones = await db.Branches.ToDictionaryAsync(b => b.Id, b => b.Timezone, ct);

        var debtors = rows.Select(r =>
        {
            var student = students[r.StudentId];
            var mine = pageInvoices.Where(x => x.StudentId == r.StudentId).ToList();
            var oldest = mine.MinBy(x => x.DueDate)!;
            return new DebtorDto(r.StudentId, student.FullName, student.StudentNumber, student.Phone, r.Total, r.Count, r.Oldest,
                BranchClock.Today(zones.GetValueOrDefault(oldest.BranchId), now).DayNumber - r.Oldest.DayNumber,
                mine.Select(x => groupNames.GetValueOrDefault(x.GroupId, "—")).Distinct().ToList());
        }).ToList();

        return Ok(new Page<DebtorDto>(debtors, total, page.Number, page.Take));
    }

    /// <summary>The figures at the top of the finance screen, computed in the database — they used to be summed in the browser
    /// from the full debtor and invoice lists. <c>TodayIncome</c> is null for a caller who may see debt but not payments.</summary>
    [HttpGet("overview")]
    [Authorize(Policy = "finance.debt.view")]
    public async Task<ActionResult<FinanceOverviewDto>> Overview(CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        var now = DateTimeOffset.UtcNow;

        var scoped = await finance.ScopedInvoicesAsync(scope);
        var openInvoices = await scoped
            .CountAsync(i => (i.Status == InvoiceStatus.Pending || i.Status == InvoiceStatus.PartiallyPaid)
                             && i.Amount - i.DiscountAmount - i.CreditedAmount > i.PaidAmount, ct);

        var overdue = await finance.OpenOverdueAsync(scoped, now);
        var debt = await overdue.SumAsync(i => (decimal?)(i.Amount - i.DiscountAmount - i.CreditedAmount - i.PaidAmount), ct) ?? 0m;
        var debtors = await overdue.Select(i => i.StudentId).Distinct().CountAsync(ct);

        decimal? todayIncome = null;
        var paymentScope = await permissions.ResolveAsync(scope.UserId, "payments.view", ct);
        if (paymentScope is not null)
        {
            todayIncome = 0m;
            var payments = await finance.ScopedPaymentsAsync(paymentScope);
            foreach (var zone in (await db.Branches.Select(b => new { b.Id, b.Timezone }).ToListAsync(ct)).GroupBy(b => b.Timezone))
            {
                var start = BranchClock.StartOfLocalDay(zone.Key, now);
                var branchIds = zone.Select(b => b.Id).ToList();
                todayIncome += await payments
                    .Where(p => !p.IsVoided && branchIds.Contains(p.BranchId) && p.PaidAt >= start && p.PaidAt < start.AddDays(1))
                    .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;
            }
        }

        return Ok(new FinanceOverviewDto(debt, debtors, openInvoices, todayIncome));
    }

    [HttpPost("invoices/{id:guid}/payments")]
    [Authorize(Policy = "payments.create")]
    public async Task<ActionResult<PaymentDto>> RecordPayment(Guid id, RecordPaymentRequest request)
    {
        var scope = HttpContext.RequireResolvedScope();
        var now = DateTimeOffset.UtcNow;

        var invoice = await (await finance.ScopedInvoicesAsync(scope)).FirstOrDefaultAsync(i => i.Id == id);
        if (invoice is null) return await InvoiceMissingOrForbiddenAsync(scope, id);

        if (invoice.Status == InvoiceStatus.Split)
            return Conflict(new { error = "Bo'lingan hisob-fakturaga to'lov qabul qilinmaydi — uning qismlariga to'lang." });
        if (invoice.Status == InvoiceStatus.Cancelled)
            return Conflict(new { error = "Bekor qilingan hisob-fakturaga to'lov qabul qilinmaydi." });
        if (invoice.Status == InvoiceStatus.Paid)
            return Conflict(new { error = "Bu hisob-faktura to'liq to'langan." });
        if (request.Amount <= 0)
            return BadRequest(new { error = "To'lov summasi musbat bo'lishi kerak." });
        if (request.Amount > invoice.Balance)
            return BadRequest(new { error = $"To'lov summasi qoldiqdan ({invoice.Balance:0.##}) oshmasligi kerak." });
        if (!Enum.IsDefined(request.Method))
            return BadRequest(new { error = "To'lov usulini tanlang." });
        if (request.Note?.Length > 500)
            return BadRequest(new { error = "Izoh 500 belgidan oshmasligi kerak." });

        var paidAt = request.PaidAt ?? now;
        if (paidAt > now.AddMinutes(5))
            return BadRequest(new { error = "To'lov sanasi kelajakda bo'lishi mumkin emas." });

        invoice.ApplyPayment(request.Amount);
        var payment = new Payment(scope.OrganizationId, invoice.BranchId, invoice.Id, invoice.StudentId, request.Amount,
            request.Method, paidAt, scope.UserId, string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim());
        db.Payments.Add(payment);

        db.StudentTimelineEvents.Add(new StudentTimelineEvent(scope.OrganizationId, invoice.StudentId,
            TimelineEventType.PaymentReceived,
            JsonSerializer.Serialize(new { invoiceId = invoice.Id, amount = request.Amount, method = request.Method.ToString() })));

        await db.SaveChangesAsync();
        await billing.SyncDebtStatusAsync(now, scope.OrganizationId, invoice.StudentId);

        var dto = (await finance.ProjectPaymentsAsync(db.Payments.Where(p => p.Id == payment.Id))).Single();
        return CreatedAtAction(nameof(Payments), dto);
    }

    /// <summary>A payment recorded by mistake. Money actually handed back is a refund instead.</summary>
    [HttpPost("payments/{id:guid}/void")]
    [Authorize(Policy = "payments.edit")]
    public async Task<IActionResult> VoidPayment(Guid id, FinanceReasonRequest request)
    {
        var scope = HttpContext.RequireResolvedScope();

        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > 500)
            return BadRequest(new { error = "Bekor qilish sababini kiriting (500 belgigacha)." });

        var payment = await (await finance.ScopedPaymentsAsync(scope)).FirstOrDefaultAsync(p => p.Id == id);
        if (payment is null)
        {
            var exists = await db.Payments.AnyAsync(p => p.Id == id && p.OrganizationId == scope.OrganizationId);
            return exists ? Forbid() : NotFound();
        }
        if (payment.IsVoided)
            return Conflict(new { error = "To'lov allaqachon bekor qilingan." });
        if (payment.RefundedAmount > 0)
            return Conflict(new { error = "Refund qilingan to'lovni bekor qilib bo'lmaydi." });

        var invoice = await db.Invoices.FirstAsync(i => i.Id == payment.InvoiceId);
        invoice.RevertPayment(payment.Amount);
        payment.Void(scope.UserId, reason);

        db.StudentTimelineEvents.Add(new StudentTimelineEvent(scope.OrganizationId, payment.StudentId,
            TimelineEventType.PaymentVoided,
            JsonSerializer.Serialize(new { paymentId = payment.Id, amount = payment.Amount, reason })));

        await db.SaveChangesAsync();
        await billing.SyncDebtStatusAsync(DateTimeOffset.UtcNow, scope.OrganizationId, payment.StudentId);
        return NoContent();
    }

    [HttpPost("invoices/{id:guid}/cancel")]
    [Authorize(Policy = "payments.edit")]
    public async Task<IActionResult> CancelInvoice(Guid id, FinanceReasonRequest request)
    {
        var scope = HttpContext.RequireResolvedScope();

        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > 500)
            return BadRequest(new { error = "Bekor qilish sababini kiriting (500 belgigacha)." });

        var invoice = await (await finance.ScopedInvoicesAsync(scope)).FirstOrDefaultAsync(i => i.Id == id);
        if (invoice is null) return await InvoiceMissingOrForbiddenAsync(scope, id);

        if (invoice.Status == InvoiceStatus.Split)
            return Conflict(new { error = "Bo'lingan hisob-fakturani bekor qilib bo'lmaydi — uning qismlarini bekor qiling." });
        if (invoice.Status == InvoiceStatus.Cancelled)
            return Conflict(new { error = "Hisob-faktura allaqachon bekor qilingan." });
        if (invoice.PaidAmount > 0)
            return Conflict(new { error = "To'lov qilingan hisob-fakturani bekor qilib bo'lmaydi — avval to'lovlarni bekor qiling." });

        invoice.Cancel(scope.UserId, reason);
        await db.SaveChangesAsync();
        await billing.SyncDebtStatusAsync(DateTimeOffset.UtcNow, scope.OrganizationId, invoice.StudentId);
        return NoContent();
    }

    /// <summary>
    /// Instalments (docs/21.09-audit-jurnali S07.05, TZ §25 "payment schedule"): one unpaid invoice cut into two or more parts, each with its
    /// own amount and due date, adding up to exactly what the student owes. The parts are ordinary invoices — they are paid, reminded about
    /// and go overdue on their own dates — and the original becomes <c>Split</c>, which every sum leaves out so the money is never counted
    /// twice. Nothing paid, nothing to cut: a part-paid invoice is refused.
    /// </summary>
    [HttpPost("invoices/{id:guid}/split")]
    [Authorize(Policy = "payments.edit")]
    public async Task<ActionResult<SplitInvoiceResult>> SplitInvoice(Guid id, SplitInvoiceRequest request)
    {
        var scope = HttpContext.RequireResolvedScope();
        var invoice = await (await finance.ScopedInvoicesAsync(scope)).FirstOrDefaultAsync(i => i.Id == id);
        if (invoice is null) return await InvoiceMissingOrForbiddenAsync(scope, id);

        var parts = request.Parts ?? [];
        if (parts.Count > MaxInstallments)
            return BadRequest(new { error = $"Hisob-fakturani {MaxInstallments} tadan ortiq qismga bo'lib bo'lmaydi." });

        List<Invoice> installments;
        try
        {
            installments = invoice.SplitInto(parts.Select(p => (p.Amount, p.DueDate)).ToList());
        }
        catch (InvalidOperationException ex) // the invoice is not in a state that can be cut: split already, part-paid, paid, cancelled
        {
            return Conflict(new { error = ex.Message });
        }
        catch (ArgumentException ex) // the parts are wrong: too few, not adding up, dates out of order
        {
            return BadRequest(new { error = ex.Message });
        }

        db.Invoices.AddRange(installments);
        db.StudentTimelineEvents.Add(new StudentTimelineEvent(scope.OrganizationId, invoice.StudentId, TimelineEventType.InvoiceSplit,
            JsonSerializer.Serialize(new
            {
                invoiceId = invoice.Id, description = invoice.Description, parts = installments.Select(p => new { p.Amount, dueDate = p.DueDate }),
            })));
        await db.SaveChangesAsync();
        // A part may already be due (the parts were dated in the past), which changes whether the student is a debtor.
        await billing.SyncDebtStatusAsync(DateTimeOffset.UtcNow, scope.OrganizationId, invoice.StudentId);

        var shown = await finance.ProjectInvoicesAsync(db.Invoices.Where(i => i.Id == id || i.ParentInvoiceId == id), DateTimeOffset.UtcNow);
        return Ok(new SplitInvoiceResult(shown.Single(i => i.Id == id), shown.Where(i => i.ParentInvoiceId == id).OrderBy(i => i.InstallmentNumber).ToList()));
    }

    /// <summary>The most parts one invoice is cut into; the screen offers fewer.</summary>
    private const int MaxInstallments = 12;

    [HttpGet("payments")]
    [Authorize(Policy = "payments.view")]
    public async Task<ActionResult<Page<PaymentDto>>> Payments(
        [FromQuery] Guid? studentId, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] string? search,
        [FromQuery] PageRequest? paging, [FromQuery] Guid? id = null, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();

        var query = await finance.ScopedPaymentsAsync(scope);
        // One payment by id: what a Universal Search result opens, wherever it would have fallen in the paged list.
        if (id is not null) query = query.Where(p => p.Id == id);
        if (studentId is not null) query = query.Where(p => p.StudentId == studentId);
        if (from is not null) query = query.Where(p => p.PaidAt >= from);
        if (to is not null) query = query.Where(p => p.PaidAt <= to);

        return Ok(await finance.ProjectPaymentPageAsync(query, search, paging, ct));
    }

    [HttpGet("refunds")]
    [Authorize(Policy = "payments.view")]
    public async Task<ActionResult<Page<RefundDto>>> Refunds(
        [FromQuery] Guid? studentId, [FromQuery] PageRequest? paging, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();

        var query = await finance.ScopedRefundsAsync(scope);
        if (studentId is not null) query = query.Where(r => r.StudentId == studentId);

        return Ok(await finance.ProjectRefundPageAsync(query, paging, ct));
    }

    [HttpGet("/api/students/{studentId:guid}/finance")]
    [Authorize(Policy = "payments.view")]
    public async Task<ActionResult<StudentFinanceDto>> StudentFinance(Guid studentId)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (!await scope.CanAccessStudentAsync(db, studentId)) return Forbid();

        var invoices = await finance.ProjectInvoicesAsync((await finance.ScopedInvoicesAsync(scope)).Where(i => i.StudentId == studentId), DateTimeOffset.UtcNow);
        var payments = await finance.ProjectPaymentsAsync((await finance.ScopedPaymentsAsync(scope)).Where(p => p.StudentId == studentId));
        var refunds = await finance.ProjectRefundsAsync((await finance.ScopedRefundsAsync(scope)).Where(r => r.StudentId == studentId));

        var open = invoices.Where(i => i.Status is InvoiceStatus.Pending or InvoiceStatus.PartiallyPaid).ToList();
        var nextDue = open.Where(i => !i.IsOverdue).OrderBy(i => i.DueDate).Select(i => (DateOnly?)i.DueDate).FirstOrDefault();

        return Ok(new StudentFinanceDto(open.Sum(i => i.Balance), open.Where(i => i.IsOverdue).Sum(i => i.Balance), nextDue,
            invoices, payments, refunds));
    }

    /// <summary>Pay ahead (docs/05_phase3_technical_plan.md Sprint 2): issues the next <c>months</c> invoices of one
    /// enrollment now, so they can be paid the normal way. At most 12 periods beyond the current one.</summary>
    [HttpPost("/api/students/{studentId:guid}/advance-invoices")]
    [Authorize(Policy = "payments.create")]
    public async Task<ActionResult<AdvanceInvoicesResult>> AdvanceInvoices(Guid studentId, AdvanceInvoicesRequest request)
    {
        var scope = HttpContext.RequireResolvedScope();

        if (request.Months is < 1 or > BillingRules.MaxAdvanceMonths)
            return BadRequest(new { error = $"Oylar soni 1 dan {BillingRules.MaxAdvanceMonths} gacha bo'lishi kerak." });

        var enrollment = await (
            from e in db.Enrollments
            join g in db.Groups on e.GroupId equals g.Id
            join c in db.Courses on g.CourseId equals c.Id
            where e.StudentId == studentId && e.GroupId == request.GroupId
                && e.OrganizationId == scope.OrganizationId && e.Status == EnrollmentStatus.Active
            select new { e.Id, g.BranchId, c.MonthlyFee }
        ).FirstOrDefaultAsync();

        if (enrollment is null) return NotFound(new { error = "Student bu guruhda faol o'qimaydi." });
        if (scope.Scope != ScopeType.AllOrganization && !scope.BranchIds.Contains(enrollment.BranchId)) return Forbid();
        if (enrollment.MonthlyFee <= 0) return BadRequest(new { error = "Bu guruh kursiga oylik to'lov belgilanmagan." });

        var issued = await billing.IssueAdvanceInvoicesAsync(DateTimeOffset.UtcNow, enrollment.Id, request.Months);
        return Ok(new AdvanceInvoicesResult(issued));
    }

    /// <summary>Runs billing for the caller's organization now instead of waiting for the next sweep — e.g. right
    /// after a course got its price.</summary>
    [HttpPost("billing/run")]
    [Authorize(Policy = "payments.create")]
    public async Task<ActionResult<BillingResult>> RunBilling()
    {
        var scope = HttpContext.RequireResolvedScope();
        return Ok(await billing.RunAsync(DateTimeOffset.UtcNow, organizationId: scope.OrganizationId));
    }

    private async Task<ActionResult> InvoiceMissingOrForbiddenAsync(ResolvedScope scope, Guid id)
    {
        var exists = await db.Invoices.AnyAsync(i => i.Id == id && i.OrganizationId == scope.OrganizationId);
        return exists ? Forbid() : NotFound();
    }
}
