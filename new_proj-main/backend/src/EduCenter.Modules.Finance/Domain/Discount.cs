using EduCenter.SharedKernel;

namespace EduCenter.Modules.Finance.Domain;

public enum DiscountType
{
    Percent,
    Fixed
}

/// <summary>
/// A discount on one invoice (ERD §5 DISCOUNT). At most one active discount per invoice — a different one
/// means revoking the first, which keeps "who gave what, and why" readable. <see cref="PromoCodeId"/> is set
/// when it came from a promo code. Revoked, never deleted.
/// </summary>
public class Discount : BranchScopedEntity
{
    private Discount() { } // EF Core

    public Discount(Guid organizationId, Guid branchId, Guid invoiceId, Guid studentId, DiscountType type, decimal value,
        decimal amount, string reason, Guid appliedBy, Guid? promoCodeId)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Discount must be positive.");

        OrganizationId = organizationId;
        BranchId = branchId;
        InvoiceId = invoiceId;
        StudentId = studentId;
        Type = type;
        Value = value;
        Amount = amount;
        Reason = reason;
        AppliedBy = appliedBy;
        PromoCodeId = promoCodeId;
    }

    public Guid InvoiceId { get; private set; }
    public Guid StudentId { get; private set; }
    public DiscountType Type { get; private set; }

    /// <summary>The percentage (for <see cref="DiscountType.Percent"/>) or the sum (for <see cref="DiscountType.Fixed"/>) as entered.</summary>
    public decimal Value { get; private set; }

    /// <summary>The money actually taken off the invoice.</summary>
    public decimal Amount { get; private set; }

    public string Reason { get; private set; } = default!;
    public Guid AppliedBy { get; private set; }
    public Guid? PromoCodeId { get; private set; }
    public Guid? RevokedBy { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? RevokeReason { get; private set; }

    public bool IsActive => RevokedAt is null;

    public void Revoke(Guid userId, string reason)
    {
        if (!IsActive) throw new InvalidOperationException("Discount is already revoked.");

        RevokedBy = userId;
        RevokedAt = DateTimeOffset.UtcNow;
        RevokeReason = reason;
        MarkUpdated();
    }

    /// <summary>Percent discounts are taken from the invoice's full amount and rounded to whole so'm.</summary>
    public static decimal CalculateAmount(DiscountType type, decimal value, decimal invoiceAmount) =>
        type == DiscountType.Percent
            ? Math.Round(invoiceAmount * value / 100m, 0, MidpointRounding.AwayFromZero)
            : value;
}
