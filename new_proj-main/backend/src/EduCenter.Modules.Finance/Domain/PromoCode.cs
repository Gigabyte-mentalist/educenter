using EduCenter.SharedKernel;

namespace EduCenter.Modules.Finance.Domain;

public enum PromoCodeProblem
{
    Inactive,
    NotStarted,
    Expired,
    UsageLimitReached,
    WrongCourse
}

/// <summary>
/// A pre-approved discount (docs/00_TZ.md §25 "promo code"). Created by someone who may approve discounts
/// organization-wide, so applying it is not subject to the branch administrator's percentage cap.
/// </summary>
public class PromoCode : TenantEntity
{
    private PromoCode() { } // EF Core

    public PromoCode(Guid organizationId, string code, string? description, DiscountType type, decimal value,
        DateOnly? validFrom, DateOnly? validUntil, int? maxUses, Guid? courseId, Guid createdBy)
    {
        if (type == DiscountType.Percent && (value <= 0 || value > 100))
            throw new ArgumentOutOfRangeException(nameof(value), "Percent must be between 0 and 100.");
        if (type == DiscountType.Fixed && value <= 0)
            throw new ArgumentOutOfRangeException(nameof(value), "Fixed discount must be positive.");
        if (maxUses is <= 0) throw new ArgumentOutOfRangeException(nameof(maxUses), "Usage limit must be positive.");
        if (validFrom is not null && validUntil is not null && validUntil < validFrom)
            throw new ArgumentException("Valid-until date is before valid-from date.");

        OrganizationId = organizationId;
        Code = Normalize(code);
        Description = description;
        Type = type;
        Value = value;
        ValidFrom = validFrom;
        ValidUntil = validUntil;
        MaxUses = maxUses;
        CourseId = courseId;
        CreatedBy = createdBy;
        IsActive = true;
    }

    public string Code { get; private set; } = default!;
    public string? Description { get; private set; }
    public DiscountType Type { get; private set; }
    public decimal Value { get; private set; }
    public DateOnly? ValidFrom { get; private set; }
    public DateOnly? ValidUntil { get; private set; }
    public int? MaxUses { get; private set; }
    public int UsedCount { get; private set; }

    /// <summary>When set, the code only applies to invoices of groups of this course.</summary>
    public Guid? CourseId { get; private set; }

    public bool IsActive { get; private set; }
    public Guid CreatedBy { get; private set; }

    public PromoCodeProblem? Check(DateOnly today, Guid courseId)
    {
        if (!IsActive) return PromoCodeProblem.Inactive;
        if (ValidFrom is not null && today < ValidFrom) return PromoCodeProblem.NotStarted;
        if (ValidUntil is not null && today > ValidUntil) return PromoCodeProblem.Expired;
        if (MaxUses is not null && UsedCount >= MaxUses) return PromoCodeProblem.UsageLimitReached;
        if (CourseId is not null && CourseId != courseId) return PromoCodeProblem.WrongCourse;
        return null;
    }

    public void RegisterUse()
    {
        UsedCount++;
        MarkUpdated();
    }

    /// <summary>A revoked promo discount gives its use back, so a mistaken application doesn't burn the limit.</summary>
    public void ReleaseUse()
    {
        if (UsedCount > 0) UsedCount--;
        MarkUpdated();
    }

    public void Deactivate()
    {
        IsActive = false;
        MarkUpdated();
    }

    public static string Normalize(string code) => code.Trim().ToUpperInvariant();
}
