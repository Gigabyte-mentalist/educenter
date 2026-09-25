using EduCenter.Modules.Automation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduCenter.Modules.Automation.Infrastructure;

public class AutomationRuleConfiguration : IEntityTypeConfiguration<AutomationRule>
{
    public void Configure(EntityTypeBuilder<AutomationRule> builder)
    {
        builder.ToTable("automation_rules");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Code).HasMaxLength(50).IsRequired();
        builder.Property(r => r.ParametersJson).HasMaxLength(2000).IsRequired();
        builder.Property(r => r.ActionsJson).HasMaxLength(2000).IsRequired();
        builder.HasIndex(r => new { r.OrganizationId, r.Code }).IsUnique();
    }
}

public class AutomationRunLogConfiguration : IEntityTypeConfiguration<AutomationRunLog>
{
    public void Configure(EntityTypeBuilder<AutomationRunLog> builder)
    {
        builder.ToTable("automation_run_logs");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.RuleCode).HasMaxLength(50).IsRequired();
        builder.Property(r => r.Trigger).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Summary).HasMaxLength(500).IsRequired();
        builder.HasIndex(r => new { r.OrganizationId, r.RanAt });
    }
}
