using EduCenter.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Modules.Academic.Domain;

public class Course : TenantEntity
{
    private Course() { } // EF Core

    public Course(Guid organizationId, string name, string levelCategory, decimal monthlyFee = 0)
    {
        if (monthlyFee < 0) throw new ArgumentOutOfRangeException(nameof(monthlyFee), "Monthly fee cannot be negative.");

        OrganizationId = organizationId;
        Name = name;
        LevelCategory = levelCategory;
        MonthlyFee = monthlyFee;
    }

    public string Name { get; private set; } = default!;
    public string LevelCategory { get; private set; } = default!;

    /// <summary>Tuition per month (docs/05_phase3_technical_plan.md §1). 0 means the course is not billed.
    /// A change applies to invoices issued from now on; existing invoices keep the amount they were issued with.</summary>
    [Precision(18, 2)]
    public decimal MonthlyFee { get; private set; }

    public void Update(string name, string levelCategory, decimal monthlyFee)
    {
        if (monthlyFee < 0) throw new ArgumentOutOfRangeException(nameof(monthlyFee), "Monthly fee cannot be negative.");

        Name = name;
        LevelCategory = levelCategory;
        MonthlyFee = monthlyFee;
        MarkUpdated();
    }
}
