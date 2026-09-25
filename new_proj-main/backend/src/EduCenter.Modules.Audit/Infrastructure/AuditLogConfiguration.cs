using EduCenter.Modules.Audit.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduCenter.Modules.Audit.Infrastructure;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Action).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Resource).HasMaxLength(50).IsRequired();
        builder.Property(a => a.Reason).HasMaxLength(500);
        builder.HasIndex(a => new { a.Resource, a.ResourceId, a.CreatedAt });
    }
}
