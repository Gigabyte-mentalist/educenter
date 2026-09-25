using System.Text.Json;
using System.Text.RegularExpressions;
using EduCenter.Api.Authorization;
using EduCenter.Api.Automation;
using EduCenter.Infrastructure;
using EduCenter.Modules.Finance.Domain;
using EduCenter.Modules.Timeline.Domain;
using EduCenter.SharedKernel.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EduCenter.Api.Finance;

public record ApplyDiscountRequest(DiscountType Type, decimal Value, string? Reason);
public record ApplyPromoCodeRequest(string? Code);
public record RefundPaymentRequest(decimal Amount, string? Reason);
public record FinanceLimitsDto(decimal? MaxDiscountPercent, decimal? MaxRefundAmount);

public record PromoCodeDto(Guid Id, string Code, string? Description, DiscountType Type, decimal Value,
    DateOnly? ValidFrom, DateOnly? ValidUntil, int? MaxUses, int UsedCount, Guid? CourseId, string? CourseName,
    bool IsActive, DateTimeOffset CreatedAt);

public record CreatePromoCodeRequest(string? Code, string? Description, DiscountType Type, decimal Value,
    DateOnly? ValidFrom, DateOnly? ValidUntil, int? MaxUses, Guid? CourseId);

/// <summary>
/// Discounts, promo codes and refunds (docs/05_phase3_technical_plan.md Sprint 2, matrix §3.7).
/// <para>
/// Limits follow the scope, not the role name (matrix §4 rule 6): whoever holds <c>discount.apply</c> /
/// <c>refund.approve</c> organization-wide is not capped; a branch-scoped holder is capped by
/// <see cref="FinanceOptions.AdministratorMaxDiscountPercent"/> and <see cref="FinanceOptions.AdministratorMaxRefundAmount"/>.
/// </para>
/// </summary>
[ApiController]
[Route("api/finance")]
public partial class FinanceAdjustmentsController(
    AppDbContext db, FinanceQueries finance, BillingRules billing, IOptions<FinanceOptions> options) : ControllerBase
{
    [HttpGet("limits")]
    [Authorize(Policy = "discount.apply")]
    public ActionResult<FinanceLimitsDto> Limits()
    {
        var scope = HttpContext.RequireResolvedScope();
        return IsCapped(scope)
            ? new FinanceLimitsDto(options.Value.AdministratorMaxDiscountPercent, options.Value.AdministratorMaxRefundAmount)
            : new FinanceLimitsDto(null, null);
    }

    [HttpPost("invoices/{id:guid}/discounts")]
    [Authorize(Policy = "discount.apply")]
    public async Task<ActionResult<InvoiceDto>> ApplyDiscount(Guid id, ApplyDiscountRequest request)
    {
        var scope = HttpContext.RequireResolvedScope();

        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > 500)
            return BadRequest(new { error = "Chegirma sababini kiriting (500 belgigacha)." });
        if (!Enum.IsDefined(request.Type))
            return BadRequest(new { error = "Chegirma turini tanlang." });
        if (request.Type == DiscountType.Percent && (request.Value <= 0 || request.Value > 100))
            return BadRequest(new { error = "Foiz 0 dan 100 gacha bo'lishi kerak." });
        if (request.Type == DiscountType.Fixed && request.Value <= 0)
            return BadRequest(new { error = "Chegirma summasi musbat bo'lishi kerak." });

        var (invoice, error) = await LoadOpenInvoiceWithoutDiscountAsync(scope, id);
        if (error is not null) return error;

        var amount = Discount.CalculateAmount(request.Type, request.Value, invoice!.Amount);
        if (amount <= 0)
            return BadRequest(new { error = "Chegirma summasi juda kichik." });
        if (amount > invoice.Balance)
            return BadRequest(new { error = $"Chegirma qoldiqdan ({invoice.Balance:0.##}) oshmasligi kerak." });

        var cap = options.Value.AdministratorMaxDiscountPercent;
        if (IsCapped(scope) && amount * 100m / invoice.Amount > cap)
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = $"Filial darajasida {cap:0.##}% gacha chegirma berish mumkin. Kattaroq chegirmani Super Admin beradi."
            });

        invoice.ApplyDiscount(amount);
        var discount = new Discount(scope.OrganizationId, invoice.BranchId, invoice.Id, invoice.StudentId,
            request.Type, request.Value, amount, reason, scope.UserId, promoCodeId: null);
        db.Discounts.Add(discount);
        AddTimeline(scope, invoice.StudentId, TimelineEventType.DiscountApplied,
            new { invoiceId = invoice.Id, type = request.Type.ToString(), value = request.Value, amount, reason });

        await db.SaveChangesAsync();
        await billing.SyncDebtStatusAsync(DateTimeOffset.UtcNow, scope.OrganizationId, invoice.StudentId);

        return Ok(await ProjectInvoiceAsync(invoice.Id));
    }

    [HttpPost("invoices/{id:guid}/promo")]
    [Authorize(Policy = "discount.apply")]
    public async Task<ActionResult<InvoiceDto>> ApplyPromoCode(Guid id, ApplyPromoCodeRequest request)
    {
        var scope = HttpContext.RequireResolvedScope();

        var code = PromoCode.Normalize(request.Code ?? "");
        if (code.Length == 0) return BadRequest(new { error = "Promo-kodni kiriting." });

        var (invoice, error) = await LoadOpenInvoiceWithoutDiscountAsync(scope, id);
        if (error is not null) return error;

        var promo = await db.PromoCodes.FirstOrDefaultAsync(p => p.OrganizationId == scope.OrganizationId && p.Code == code);
        if (promo is null) return NotFound(new { error = "Promo-kod topilmadi." });

        var context = await (
            from g in db.Groups
            join b in db.Branches on g.BranchId equals b.Id
            where g.Id == invoice!.GroupId
            select new { g.CourseId, b.Timezone }
        ).FirstAsync();

        var problem = promo.Check(BranchClock.Today(context.Timezone, DateTimeOffset.UtcNow), context.CourseId);
        if (problem is not null)
            return Conflict(new
            {
                error = problem switch
                {
                    PromoCodeProblem.Inactive => "Promo-kod faol emas.",
                    PromoCodeProblem.NotStarted => "Promo-kod amal qilish muddati hali boshlanmagan.",
                    PromoCodeProblem.Expired => "Promo-kod muddati tugagan.",
                    PromoCodeProblem.UsageLimitReached => "Promo-kodning foydalanish limiti tugagan.",
                    _ => "Promo-kod bu kurs uchun amal qilmaydi."
                }
            });

        // A pre-approved code: no branch cap, but never more than what is still owed.
        var amount = Math.Min(Discount.CalculateAmount(promo.Type, promo.Value, invoice!.Amount), invoice.Balance);
        if (amount <= 0) return Conflict(new { error = "Hisob-fakturada chegirma uchun qoldiq yo'q." });

        invoice.ApplyDiscount(amount);
        promo.RegisterUse();
        db.Discounts.Add(new Discount(scope.OrganizationId, invoice.BranchId, invoice.Id, invoice.StudentId,
            promo.Type, promo.Value, amount, $"Promo-kod {promo.Code}", scope.UserId, promo.Id));
        AddTimeline(scope, invoice.StudentId, TimelineEventType.DiscountApplied,
            new { invoiceId = invoice.Id, promoCode = promo.Code, amount });

        await db.SaveChangesAsync();
        await billing.SyncDebtStatusAsync(DateTimeOffset.UtcNow, scope.OrganizationId, invoice.StudentId);

        return Ok(await ProjectInvoiceAsync(invoice.Id));
    }

    [HttpPost("discounts/{id:guid}/revoke")]
    [Authorize(Policy = "discount.apply")]
    public async Task<IActionResult> RevokeDiscount(Guid id, FinanceReasonRequest request)
    {
        var scope = HttpContext.RequireResolvedScope();

        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > 500)
            return BadRequest(new { error = "Bekor qilish sababini kiriting (500 belgigacha)." });

        var discount = await finance.ScopedDiscounts(scope).FirstOrDefaultAsync(d => d.Id == id);
        if (discount is null)
        {
            var exists = await db.Discounts.AnyAsync(d => d.Id == id && d.OrganizationId == scope.OrganizationId);
            return exists ? Forbid() : NotFound();
        }
        if (!discount.IsActive) return Conflict(new { error = "Chegirma allaqachon bekor qilingan." });

        var invoice = await db.Invoices.FirstAsync(i => i.Id == discount.InvoiceId);
        if (invoice.Status == InvoiceStatus.Cancelled)
            return Conflict(new { error = "Bekor qilingan hisob-fakturadagi chegirmani o'zgartirib bo'lmaydi." });

        invoice.RevokeDiscount(discount.Amount);
        discount.Revoke(scope.UserId, reason);
        if (discount.PromoCodeId is not null)
            (await db.PromoCodes.FirstAsync(p => p.Id == discount.PromoCodeId)).ReleaseUse();
        AddTimeline(scope, discount.StudentId, TimelineEventType.DiscountRevoked,
            new { invoiceId = invoice.Id, amount = discount.Amount, reason });

        await db.SaveChangesAsync();
        await billing.SyncDebtStatusAsync(DateTimeOffset.UtcNow, scope.OrganizationId, discount.StudentId);
        return NoContent();
    }

    /// <summary>Money handed back to the student. The invoice's obligation shrinks by the same amount, so a refund
    /// never creates debt — a payment entered by mistake is voided instead.</summary>
    [HttpPost("payments/{id:guid}/refunds")]
    [Authorize(Policy = "refund.approve")]
    public async Task<ActionResult<RefundDto>> RefundPayment(Guid id, RefundPaymentRequest request)
    {
        var scope = HttpContext.RequireResolvedScope();

        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > 500)
            return BadRequest(new { error = "Refund sababini kiriting (500 belgigacha)." });
        if (request.Amount <= 0)
            return BadRequest(new { error = "Refund summasi musbat bo'lishi kerak." });

        var payment = await (await finance.ScopedPaymentsAsync(scope)).FirstOrDefaultAsync(p => p.Id == id);
        if (payment is null)
        {
            var exists = await db.Payments.AnyAsync(p => p.Id == id && p.OrganizationId == scope.OrganizationId);
            return exists ? Forbid() : NotFound();
        }
        if (payment.IsVoided)
            return Conflict(new { error = "Bekor qilingan to'lovdan refund qilinmaydi." });
        if (request.Amount > payment.RefundableAmount)
            return BadRequest(new { error = $"Refund summasi qaytarilishi mumkin bo'lgan summadan ({payment.RefundableAmount:0.##}) oshmasligi kerak." });

        var cap = options.Value.AdministratorMaxRefundAmount;
        if (IsCapped(scope) && request.Amount > cap)
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = $"Filial darajasida bir martada {cap:0.##} so'mgacha refund qilish mumkin. Kattaroq refundni Super Admin tasdiqlaydi."
            });

        var invoice = await db.Invoices.FirstAsync(i => i.Id == payment.InvoiceId);
        if (invoice.Status == InvoiceStatus.Cancelled)
            return Conflict(new { error = "Bekor qilingan hisob-faktura bo'yicha refund qilinmaydi." });

        payment.RegisterRefund(request.Amount);
        invoice.ApplyRefund(request.Amount);
        var refund = new Refund(scope.OrganizationId, payment.BranchId, payment.Id, invoice.Id, payment.StudentId,
            request.Amount, reason, scope.UserId);
        db.Refunds.Add(refund);
        AddTimeline(scope, payment.StudentId, TimelineEventType.RefundIssued,
            new { paymentId = payment.Id, amount = request.Amount, reason });

        await db.SaveChangesAsync();
        await billing.SyncDebtStatusAsync(DateTimeOffset.UtcNow, scope.OrganizationId, payment.StudentId);

        var dto = (await finance.ProjectRefundsAsync(db.Refunds.Where(r => r.Id == refund.Id))).Single();
        return StatusCode(StatusCodes.Status201Created, dto);
    }

    [HttpGet("promo-codes")]
    [Authorize(Policy = "discount.apply")]
    public async Task<ActionResult<List<PromoCodeDto>>> PromoCodes()
    {
        var scope = HttpContext.RequireResolvedScope();
        var codes = await ProjectPromoCodes(db.PromoCodes.Where(p => p.OrganizationId == scope.OrganizationId)).ToListAsync();
        return Ok(codes.OrderByDescending(p => p.IsActive).ThenByDescending(p => p.CreatedAt).ToList());
    }

    [HttpPost("promo-codes")]
    [Authorize(Policy = "promo.manage")]
    public async Task<ActionResult<PromoCodeDto>> CreatePromoCode(CreatePromoCodeRequest request)
    {
        var scope = HttpContext.RequireResolvedScope();

        var code = PromoCode.Normalize(request.Code ?? "");
        if (!PromoCodePattern().IsMatch(code))
            return BadRequest(new { error = "Kod 3–32 belgidan iborat bo'lsin: lotin harflari, raqamlar, '-' yoki '_'." });
        if (!Enum.IsDefined(request.Type))
            return BadRequest(new { error = "Chegirma turini tanlang." });
        if (request.Type == DiscountType.Percent && (request.Value <= 0 || request.Value > 100))
            return BadRequest(new { error = "Foiz 0 dan 100 gacha bo'lishi kerak." });
        if (request.Type == DiscountType.Fixed && request.Value <= 0)
            return BadRequest(new { error = "Chegirma summasi musbat bo'lishi kerak." });
        if (request.MaxUses is <= 0)
            return BadRequest(new { error = "Foydalanish limiti musbat bo'lishi kerak." });
        if (request.ValidFrom is not null && request.ValidUntil is not null && request.ValidUntil < request.ValidFrom)
            return BadRequest(new { error = "Tugash sanasi boshlanish sanasidan oldin bo'lmasin." });
        if (request.Description?.Length > 200)
            return BadRequest(new { error = "Izoh 200 belgidan oshmasligi kerak." });
        if (request.CourseId is not null && !await db.Courses.AnyAsync(c => c.Id == request.CourseId && c.OrganizationId == scope.OrganizationId))
            return BadRequest(new { error = "Kurs topilmadi." });
        if (await db.PromoCodes.AnyAsync(p => p.OrganizationId == scope.OrganizationId && p.Code == code))
            return Conflict(new { error = "Bunday promo-kod allaqachon mavjud." });

        var promo = new PromoCode(scope.OrganizationId, code, string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            request.Type, request.Value, request.ValidFrom, request.ValidUntil, request.MaxUses, request.CourseId, scope.UserId);
        db.PromoCodes.Add(promo);
        await db.SaveChangesAsync();

        var dto = await ProjectPromoCodes(db.PromoCodes.Where(p => p.Id == promo.Id)).FirstAsync();
        return CreatedAtAction(nameof(PromoCodes), dto);
    }

    [HttpPost("promo-codes/{id:guid}/deactivate")]
    [Authorize(Policy = "promo.manage")]
    public async Task<IActionResult> DeactivatePromoCode(Guid id)
    {
        var scope = HttpContext.RequireResolvedScope();
        var promo = await db.PromoCodes.FirstOrDefaultAsync(p => p.Id == id && p.OrganizationId == scope.OrganizationId);
        if (promo is null) return NotFound();

        promo.Deactivate();
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static bool IsCapped(ResolvedScope scope) => scope.Scope != ScopeType.AllOrganization;

    private async Task<(Invoice? Invoice, ActionResult? Error)> LoadOpenInvoiceWithoutDiscountAsync(ResolvedScope scope, Guid id)
    {
        var invoice = await (await finance.ScopedInvoicesAsync(scope)).FirstOrDefaultAsync(i => i.Id == id);
        if (invoice is null)
        {
            var exists = await db.Invoices.AnyAsync(i => i.Id == id && i.OrganizationId == scope.OrganizationId);
            return (null, exists ? Forbid() : NotFound());
        }
        if (invoice.Status == InvoiceStatus.Split)
            return (null, Conflict(new { error = "Bo'lingan hisob-fakturaga chegirma berilmaydi — uning qismlaridan biriga bering." }));
        if (!invoice.IsOpen)
            return (null, Conflict(new { error = "Chegirma faqat to'lanmagan yoki qisman to'langan hisob-fakturaga beriladi." }));
        if (await db.Discounts.AnyAsync(d => d.InvoiceId == id && d.RevokedAt == null))
            return (null, Conflict(new { error = "Bu hisob-fakturada chegirma bor — boshqasini berish uchun avval uni bekor qiling." }));

        return (invoice, null);
    }

    private async Task<InvoiceDto> ProjectInvoiceAsync(Guid invoiceId) =>
        (await finance.ProjectInvoicesAsync(db.Invoices.Where(i => i.Id == invoiceId), DateTimeOffset.UtcNow)).Single();

    private IQueryable<PromoCodeDto> ProjectPromoCodes(IQueryable<PromoCode> codes) =>
        from p in codes
        select new PromoCodeDto(p.Id, p.Code, p.Description, p.Type, p.Value, p.ValidFrom, p.ValidUntil, p.MaxUses, p.UsedCount,
            p.CourseId, db.Courses.Where(c => c.Id == p.CourseId).Select(c => c.Name).FirstOrDefault(), p.IsActive, p.CreatedAt);

    private void AddTimeline(ResolvedScope scope, Guid studentId, string eventType, object payload) =>
        db.StudentTimelineEvents.Add(new StudentTimelineEvent(scope.OrganizationId, studentId, eventType, JsonSerializer.Serialize(payload)));

    [GeneratedRegex("^[A-Z0-9_-]{3,32}$")]
    private static partial Regex PromoCodePattern();
}
