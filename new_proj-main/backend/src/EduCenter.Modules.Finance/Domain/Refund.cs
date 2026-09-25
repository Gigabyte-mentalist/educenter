using EduCenter.SharedKernel;

namespace EduCenter.Modules.Finance.Domain;

/// <summary>Money given back from a payment (ERD §5 REFUND). Approved by whoever records it, within their limit.</summary>
public class Refund : BranchScopedEntity
{
    private Refund() { } // EF Core

    public Refund(Guid organizationId, Guid branchId, Guid paymentId, Guid invoiceId, Guid studentId, decimal amount,
        string reason, Guid approvedBy)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Refund must be positive.");

        OrganizationId = organizationId;
        BranchId = branchId;
        PaymentId = paymentId;
        InvoiceId = invoiceId;
        StudentId = studentId;
        Amount = amount;
        Reason = reason;
        ApprovedBy = approvedBy;
        RefundedAt = DateTimeOffset.UtcNow;
    }

    public Guid PaymentId { get; private set; }
    public Guid InvoiceId { get; private set; }
    public Guid StudentId { get; private set; }
    public decimal Amount { get; private set; }
    public string Reason { get; private set; } = default!;
    public Guid ApprovedBy { get; private set; }
    public DateTimeOffset RefundedAt { get; private set; }
}
