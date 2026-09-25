using EduCenter.SharedKernel;

namespace EduCenter.Modules.Finance.Domain;

public enum PaymentMethod
{
    Cash,
    Card,
    BankTransfer,
    Click,
    Payme,
    Other
}

/// <summary>
/// Money received against one invoice. Never edited or deleted: a wrong payment is voided with a reason
/// (and re-entered if needed), money given back is a <see cref="Refund"/> — so the audit trail always shows
/// what was taken, what was reversed and what was returned (docs/00_TZ.md §52 "payment edited").
/// </summary>
public class Payment : BranchScopedEntity
{
    private Payment() { } // EF Core

    public Payment(Guid organizationId, Guid branchId, Guid invoiceId, Guid studentId, decimal amount,
        PaymentMethod method, DateTimeOffset paidAt, Guid receivedBy, string? note)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Payment amount must be positive.");

        OrganizationId = organizationId;
        BranchId = branchId;
        InvoiceId = invoiceId;
        StudentId = studentId;
        Amount = amount;
        Method = method;
        PaidAt = paidAt;
        ReceivedBy = receivedBy;
        Note = note;
    }

    public Guid InvoiceId { get; private set; }
    public Guid StudentId { get; private set; }
    public decimal Amount { get; private set; }
    public decimal RefundedAmount { get; private set; }
    public PaymentMethod Method { get; private set; }
    public DateTimeOffset PaidAt { get; private set; }
    public Guid ReceivedBy { get; private set; }
    public string? Note { get; private set; }
    public bool IsVoided { get; private set; }
    public Guid? VoidedBy { get; private set; }
    public DateTimeOffset? VoidedAt { get; private set; }
    public string? VoidReason { get; private set; }

    public decimal RefundableAmount => IsVoided ? 0 : Amount - RefundedAmount;

    public void Void(Guid userId, string reason)
    {
        if (IsVoided) throw new InvalidOperationException("Payment is already voided.");
        if (RefundedAmount > 0) throw new InvalidOperationException("A refunded payment cannot be voided.");

        IsVoided = true;
        VoidedBy = userId;
        VoidedAt = DateTimeOffset.UtcNow;
        VoidReason = reason;
        MarkUpdated();
    }

    public void RegisterRefund(decimal amount)
    {
        if (IsVoided) throw new InvalidOperationException("A voided payment cannot be refunded.");
        if (amount <= 0 || amount > RefundableAmount) throw new InvalidOperationException("Refund exceeds the refundable amount.");

        RefundedAmount += amount;
        MarkUpdated();
    }
}
