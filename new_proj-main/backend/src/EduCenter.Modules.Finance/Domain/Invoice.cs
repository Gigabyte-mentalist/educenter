using EduCenter.SharedKernel;

namespace EduCenter.Modules.Finance.Domain;

public enum InvoiceStatus
{
    Pending,
    PartiallyPaid,
    Paid,
    Cancelled,
    /// <summary>Divided into instalments (<see cref="Invoice.SplitInto"/>): the parts are the debt from then on, and this invoice only says what they add up to.
    /// It takes no payment and is left out of every sum — counting it as well as its parts would owe the student twice.</summary>
    Split
}

/// <summary>
/// One billing period of one enrollment (docs/00_TZ.md §25, docs/05_phase3_technical_plan.md §1).
/// <para>
/// Money on an invoice: <see cref="Amount"/> is what the period costs; a <see cref="DiscountAmount"/> lowers
/// what is owed; a refund moves money back out of <see cref="PaidAmount"/> <em>and</em> lowers what is owed by the
/// same amount (<see cref="CreditedAmount"/>) — the student got money back for service they didn't receive, so
/// no debt appears. A voided payment (a mistake) only lowers <see cref="PaidAmount"/>, re-opening the balance.
/// </para>
/// "Overdue" is not stored: it depends on today's date in the branch's time zone, so it is evaluated
/// with <see cref="IsOverdue"/> instead of being flipped by a job and drifting out of date.
/// </summary>
public class Invoice : BranchScopedEntity
{
    private Invoice() { } // EF Core

    public Invoice(Guid organizationId, Guid branchId, Guid studentId, Guid enrollmentId, Guid groupId,
        string description, DateOnly periodStart, DateOnly periodEnd, DateOnly dueDate, decimal amount, string currency)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Invoice amount must be positive.");
        if (periodEnd <= periodStart) throw new ArgumentException("Period end must be after period start.");

        OrganizationId = organizationId;
        BranchId = branchId;
        StudentId = studentId;
        EnrollmentId = enrollmentId;
        GroupId = groupId;
        Description = description;
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        DueDate = dueDate;
        Amount = amount;
        Currency = currency;
        Status = InvoiceStatus.Pending;
    }

    /// <summary>Set on an instalment: the invoice it was cut from. An instalment keeps its parent's billing period.</summary>
    public Guid? ParentInvoiceId { get; private set; }

    /// <summary>"2" of "2/3" — set with <see cref="ParentInvoiceId"/>.</summary>
    public int? InstallmentNumber { get; private set; }
    public int? InstallmentCount { get; private set; }

    public Guid StudentId { get; private set; }
    public Guid EnrollmentId { get; private set; }
    public Guid GroupId { get; private set; }
    public string Description { get; private set; } = default!;

    /// <summary>Inclusive start of the billed period.</summary>
    public DateOnly PeriodStart { get; private set; }

    /// <summary>Exclusive end of the billed period (the next period's start).</summary>
    public DateOnly PeriodEnd { get; private set; }

    public DateOnly DueDate { get; private set; }
    public decimal Amount { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public decimal CreditedAmount { get; private set; }
    public decimal PaidAmount { get; private set; }
    public string Currency { get; private set; } = default!;
    public InvoiceStatus Status { get; private set; }
    public string? CancelReason { get; private set; }
    public Guid? CancelledBy { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>What the student actually owes for the period.</summary>
    public decimal NetAmount => Amount - DiscountAmount - CreditedAmount;

    public decimal Balance => NetAmount - PaidAmount;

    public bool IsOpen => Status is InvoiceStatus.Pending or InvoiceStatus.PartiallyPaid;

    public bool IsInstallment => ParentInvoiceId is not null;

    public bool IsOverdue(DateOnly today) => IsOpen && Balance > 0 && DueDate < today;

    /// <summary>
    /// Cuts an unpaid invoice into instalments — the same money, each part with its own due date. The parts together must be exactly what
    /// the student owes (<see cref="NetAmount"/>, to the tiyin), at least two, due in rising order and not before the period begins. This
    /// invoice becomes <see cref="InvoiceStatus.Split"/>; the returned parts carry its student, enrollment, group and period, so billing
    /// still sees the period as issued and nothing is invoiced twice.
    /// </summary>
    public List<Invoice> SplitInto(IReadOnlyList<(decimal Amount, DateOnly DueDate)> parts)
    {
        if (Status == InvoiceStatus.Split) throw new InvalidOperationException("Bu hisob-faktura allaqachon bo'lingan.");
        if (IsInstallment) throw new InvalidOperationException("Bo'lingan qismni qayta bo'lib bo'lmaydi.");
        if (Status != InvoiceStatus.Pending || PaidAmount > 0)
            throw new InvalidOperationException("Faqat to'lov qilinmagan hisob-fakturani bo'lish mumkin.");
        if (parts.Count < 2) throw new ArgumentException("Kamida ikki qism kerak.");
        if (parts.Any(p => p.Amount <= 0 || decimal.Round(p.Amount, 2) != p.Amount))
            throw new ArgumentException("Har bir qism summasi musbat bo'lishi va tiyinigacha aniq berilishi kerak.");
        if (parts.Sum(p => p.Amount) != NetAmount)
            throw new ArgumentException($"Qismlar yig'indisi to'lanadigan summaga ({NetAmount:0.##}) teng bo'lishi kerak.");
        if (parts.Any(p => p.DueDate < PeriodStart)) throw new ArgumentException("Qism muddati davr boshlanishidan oldin bo'lmasin.");
        for (var i = 1; i < parts.Count; i++)
            if (parts[i].DueDate <= parts[i - 1].DueDate) throw new ArgumentException("Qism muddatlari ketma-ket o'sib borishi kerak.");

        var installments = parts.Select((p, i) => new Invoice(OrganizationId, BranchId, StudentId, EnrollmentId, GroupId,
            $"{Description} ({i + 1}/{parts.Count})", PeriodStart, PeriodEnd, p.DueDate, p.Amount, Currency)
        {
            ParentInvoiceId = Id, InstallmentNumber = i + 1, InstallmentCount = parts.Count,
        }).ToList();

        Status = InvoiceStatus.Split;
        MarkUpdated();
        return installments;
    }

    public void ApplyPayment(decimal amount)
    {
        if (Status == InvoiceStatus.Split) throw new InvalidOperationException("Bo'lingan hisob-fakturaga to'lov qabul qilinmaydi — uning qismlariga to'lang.");
        if (Status == InvoiceStatus.Cancelled) throw new InvalidOperationException("A cancelled invoice cannot be paid.");
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Payment amount must be positive.");
        if (amount > Balance) throw new InvalidOperationException("Payment exceeds the invoice balance.");

        PaidAmount += amount;
        Recalculate();
    }

    public void RevertPayment(decimal amount)
    {
        if (amount <= 0 || amount > PaidAmount) throw new InvalidOperationException("Reverted amount exceeds what was paid.");

        PaidAmount -= amount;
        Recalculate();
    }

    public void ApplyDiscount(decimal amount)
    {
        if (!IsOpen) throw new InvalidOperationException("Only an open invoice can be discounted.");
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Discount must be positive.");
        if (amount > Balance) throw new InvalidOperationException("Discount exceeds the invoice balance.");

        DiscountAmount += amount;
        Recalculate();
    }

    public void RevokeDiscount(decimal amount)
    {
        if (Status == InvoiceStatus.Cancelled) throw new InvalidOperationException("A cancelled invoice cannot change.");
        if (amount <= 0 || amount > DiscountAmount) throw new InvalidOperationException("Revoked discount exceeds the discount given.");

        DiscountAmount -= amount;
        Recalculate();
    }

    /// <summary>Money returned to the student: it leaves <see cref="PaidAmount"/> and the obligation shrinks by the same amount.</summary>
    public void ApplyRefund(decimal amount)
    {
        if (Status == InvoiceStatus.Cancelled) throw new InvalidOperationException("A cancelled invoice cannot change.");
        if (amount <= 0 || amount > PaidAmount) throw new InvalidOperationException("Refund exceeds what was paid.");

        PaidAmount -= amount;
        CreditedAmount += amount;
        Recalculate();
    }

    public void Cancel(Guid userId, string reason)
    {
        if (Status == InvoiceStatus.Split) throw new InvalidOperationException("Bo'lingan hisob-fakturani bekor qilib bo'lmaydi — uning qismlarini bekor qiling.");
        if (Status == InvoiceStatus.Cancelled) throw new InvalidOperationException("Invoice is already cancelled.");
        if (PaidAmount > 0) throw new InvalidOperationException("An invoice with payments cannot be cancelled; void the payments first.");

        Status = InvoiceStatus.Cancelled;
        CancelReason = reason;
        CancelledBy = userId;
        CancelledAt = DateTimeOffset.UtcNow;
        MarkUpdated();
    }

    private void Recalculate()
    {
        Status = PaidAmount >= NetAmount ? InvoiceStatus.Paid
            : PaidAmount > 0 ? InvoiceStatus.PartiallyPaid
            : InvoiceStatus.Pending;
        MarkUpdated();
    }
}
