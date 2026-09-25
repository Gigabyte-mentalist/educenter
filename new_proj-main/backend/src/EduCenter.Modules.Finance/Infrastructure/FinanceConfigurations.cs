using EduCenter.Modules.Finance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduCenter.Modules.Finance.Infrastructure;

public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("invoices");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Description).HasMaxLength(300).IsRequired();
        builder.Property(i => i.Amount).HasPrecision(18, 2);
        builder.Property(i => i.DiscountAmount).HasPrecision(18, 2);
        builder.Property(i => i.CreditedAmount).HasPrecision(18, 2);
        builder.Property(i => i.PaidAmount).HasPrecision(18, 2);
        builder.Property(i => i.Currency).HasMaxLength(3).IsRequired();
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.CancelReason).HasMaxLength(500);
        builder.Ignore(i => i.NetAmount);
        builder.Ignore(i => i.Balance);
        builder.Ignore(i => i.IsOpen);

        // One invoice per enrollment per period — also what makes concurrent issuing (sweep + conversion) safe. The instalments of a split
        // invoice share their parent's period on purpose, so the rule is for the invoices billing issues, not for the parts cut from one.
        builder.HasIndex(i => new { i.EnrollmentId, i.PeriodStart }).IsUnique().HasFilter("\"ParentInvoiceId\" IS NULL");
        builder.HasIndex(i => i.ParentInvoiceId);
        builder.HasIndex(i => new { i.BranchId, i.Status, i.DueDate });
        builder.HasIndex(i => i.StudentId);
    }
}

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Amount).HasPrecision(18, 2);
        builder.Property(p => p.RefundedAmount).HasPrecision(18, 2);
        builder.Property(p => p.Method).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Note).HasMaxLength(500);
        builder.Property(p => p.VoidReason).HasMaxLength(500);
        builder.Ignore(p => p.RefundableAmount);
        builder.HasIndex(p => p.InvoiceId);
        builder.HasIndex(p => p.StudentId);
        builder.HasIndex(p => new { p.BranchId, p.PaidAt });
    }
}

public class DiscountConfiguration : IEntityTypeConfiguration<Discount>
{
    public void Configure(EntityTypeBuilder<Discount> builder)
    {
        builder.ToTable("discounts");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.Value).HasPrecision(18, 2);
        builder.Property(d => d.Amount).HasPrecision(18, 2);
        builder.Property(d => d.Reason).HasMaxLength(500).IsRequired();
        builder.Property(d => d.RevokeReason).HasMaxLength(500);
        builder.Ignore(d => d.IsActive);

        // At most one active discount per invoice.
        builder.HasIndex(d => d.InvoiceId).IsUnique().HasFilter("\"RevokedAt\" IS NULL");
        builder.HasIndex(d => d.StudentId);
        builder.HasIndex(d => d.PromoCodeId);
    }
}

public class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        builder.ToTable("refunds");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Amount).HasPrecision(18, 2);
        builder.Property(r => r.Reason).HasMaxLength(500).IsRequired();
        builder.HasIndex(r => r.PaymentId);
        builder.HasIndex(r => r.StudentId);
        builder.HasIndex(r => new { r.BranchId, r.RefundedAt });
    }
}

public class PromoCodeConfiguration : IEntityTypeConfiguration<PromoCode>
{
    public void Configure(EntityTypeBuilder<PromoCode> builder)
    {
        builder.ToTable("promo_codes");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Code).HasMaxLength(32).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(200);
        builder.Property(p => p.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Value).HasPrecision(18, 2);
        builder.HasIndex(p => new { p.OrganizationId, p.Code }).IsUnique();
    }
}
