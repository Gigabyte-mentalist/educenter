using EduCenter.Modules.Lms.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduCenter.Modules.Lms.Infrastructure;

public class LearningResourceConfiguration : IEntityTypeConfiguration<LearningResource>
{
    public void Configure(EntityTypeBuilder<LearningResource> builder)
    {
        builder.ToTable("learning_resources", t => t.HasCheckConstraint(
            "ck_learning_resources_link_or_file", "(\"Url\" IS NOT NULL) <> (\"StorageKey\" IS NOT NULL)"));
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Title).HasMaxLength(200).IsRequired();
        // Url is no longer required: a material is either a link or an uploaded file, and exactly one of
        // Url / StorageKey is set. The check constraint is what keeps "neither" out of the table.
        builder.Property(r => r.Url).HasMaxLength(2000);
        builder.Property(r => r.Description).HasMaxLength(2000);
        builder.Property(r => r.StorageKey).HasMaxLength(64);
        builder.Property(r => r.FileName).HasMaxLength(255);
        builder.Property(r => r.FileContentType).HasMaxLength(128);
        builder.HasIndex(r => new { r.OrganizationId, r.LessonId });
    }
}

public class AttendanceConfiguration : IEntityTypeConfiguration<Attendance>
{
    public void Configure(EntityTypeBuilder<Attendance> builder)
    {
        builder.ToTable("attendance");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Note).HasMaxLength(500);
        builder.HasIndex(a => new { a.LessonId, a.StudentId }).IsUnique();
        builder.HasIndex(a => a.StudentId);
    }
}

public class HomeworkConfiguration : IEntityTypeConfiguration<Homework>
{
    public void Configure(EntityTypeBuilder<Homework> builder)
    {
        builder.ToTable("homework");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Title).HasMaxLength(200).IsRequired();
        builder.Property(h => h.Description).HasMaxLength(2000);
        builder.Property(h => h.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(h => h.AttachmentKey).HasMaxLength(64);
        builder.Property(h => h.AttachmentName).HasMaxLength(255);
        builder.Property(h => h.AttachmentContentType).HasMaxLength(128);
        builder.HasIndex(h => h.LessonId);
    }
}

public class HomeworkSubmissionConfiguration : IEntityTypeConfiguration<HomeworkSubmission>
{
    public void Configure(EntityTypeBuilder<HomeworkSubmission> builder)
    {
        builder.ToTable("homework_submissions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.ContentUrl).HasMaxLength(1000);
        builder.Property(s => s.ContentText).HasMaxLength(10000);
        builder.Property(s => s.Feedback).HasMaxLength(2000);
        builder.Property(s => s.AttachmentKey).HasMaxLength(64);
        builder.Property(s => s.AttachmentName).HasMaxLength(255);
        builder.Property(s => s.AttachmentContentType).HasMaxLength(128);
        builder.HasIndex(s => new { s.HomeworkId, s.StudentId }).IsUnique();
    }
}

public class ExamConfiguration : IEntityTypeConfiguration<Exam>
{
    public void Configure(EntityTypeBuilder<Exam> builder)
    {
        builder.ToTable("exams");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(e => e.GroupId);
    }
}

public class ExamResultConfiguration : IEntityTypeConfiguration<ExamResult>
{
    public void Configure(EntityTypeBuilder<ExamResult> builder)
    {
        builder.ToTable("exam_results");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Listening).HasPrecision(3, 1);
        builder.Property(r => r.Reading).HasPrecision(3, 1);
        builder.Property(r => r.Writing).HasPrecision(3, 1);
        builder.Property(r => r.Speaking).HasPrecision(3, 1);
        builder.Property(r => r.Overall).HasPrecision(3, 1);
        builder.Property(r => r.Grammar).HasPrecision(4, 1);
        builder.Property(r => r.Vocabulary).HasPrecision(4, 1);
        builder.HasIndex(r => new { r.ExamId, r.StudentId }).IsUnique();
        builder.HasIndex(r => r.StudentId);
    }
}

public class HomeworkTargetConfiguration : IEntityTypeConfiguration<HomeworkTarget>
{
    public void Configure(EntityTypeBuilder<HomeworkTarget> builder)
    {
        builder.ToTable("homework_targets");
        builder.HasKey(t => t.Id);
        // A student is given a homework once; the same pair backs "is this homework for this student" and "who is it for".
        builder.HasIndex(t => new { t.HomeworkId, t.StudentId }).IsUnique();
        builder.HasIndex(t => t.StudentId);
    }
}
