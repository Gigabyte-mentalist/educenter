using EduCenter.Modules.Students.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduCenter.Modules.Students.Infrastructure;

public class StudentConfiguration : IEntityTypeConfiguration<Student>
{
    public void Configure(EntityTypeBuilder<Student> builder)
    {
        builder.ToTable("students");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.StudentNumber).HasMaxLength(50).IsRequired();
        builder.Property(s => s.CurrentLevel).HasMaxLength(50);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(s => s.UserId).IsUnique();
        builder.HasIndex(s => s.StudentNumber).IsUnique();
    }
}

public class ParentConfiguration : IEntityTypeConfiguration<Parent>
{
    public void Configure(EntityTypeBuilder<Parent> builder)
    {
        builder.ToTable("parents");
        builder.HasKey(p => p.Id);
        builder.HasIndex(p => p.UserId).IsUnique();
    }
}

public class StudentParentConfiguration : IEntityTypeConfiguration<StudentParent>
{
    public void Configure(EntityTypeBuilder<StudentParent> builder)
    {
        builder.ToTable("student_parents");
        builder.HasKey(sp => new { sp.StudentId, sp.ParentId });
        builder.Property(sp => sp.Relationship).HasConversion<string>().HasMaxLength(20);
    }
}

public class EnrollmentConfiguration : IEntityTypeConfiguration<Enrollment>
{
    public void Configure(EntityTypeBuilder<Enrollment> builder)
    {
        builder.ToTable("enrollments");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.EndReason).HasMaxLength(500);
        builder.HasIndex(e => e.StudentId);
        builder.HasIndex(e => e.GroupId);
    }
}

public class SupportCaseConfiguration : IEntityTypeConfiguration<SupportCase>
{
    public void Configure(EntityTypeBuilder<SupportCase> builder)
    {
        builder.ToTable("support_cases");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Source).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.SignalCode).HasMaxLength(40).IsRequired();
        builder.Property(c => c.Reason).HasMaxLength(500).IsRequired();
        builder.Property(c => c.ResolutionNote).HasMaxLength(1000);
        builder.HasIndex(c => new { c.OrganizationId, c.Status });
        builder.HasIndex(c => new { c.StudentId, c.GroupId, c.SignalCode });
    }
}

public class StudentDocumentConfiguration : IEntityTypeConfiguration<StudentDocument>
{
    public void Configure(EntityTypeBuilder<StudentDocument> builder)
    {
        builder.ToTable("student_documents", t => t.HasCheckConstraint(
            "ck_student_documents_link_or_file", "(\"Url\" IS NOT NULL) <> (\"StorageKey\" IS NOT NULL)"));
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Title).HasMaxLength(200).IsRequired();
        builder.Property(d => d.Url).HasMaxLength(2000);
        builder.Property(d => d.Description).HasMaxLength(2000);
        builder.Property(d => d.StorageKey).HasMaxLength(64);
        builder.Property(d => d.FileName).HasMaxLength(255);
        builder.Property(d => d.FileContentType).HasMaxLength(128);
        builder.HasIndex(d => new { d.OrganizationId, d.StudentId });
    }
}
