using EduCenter.Modules.Crm.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduCenter.Modules.Crm.Infrastructure;

public class LeadConfiguration : IEntityTypeConfiguration<Lead>
{
    public void Configure(EntityTypeBuilder<Lead> builder)
    {
        builder.ToTable("leads");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.FullName).HasMaxLength(200).IsRequired();
        builder.Property(l => l.Phone).HasMaxLength(30).IsRequired();
        builder.Property(l => l.Source).HasMaxLength(50).IsRequired();
        builder.Property(l => l.InterestedCourse).HasMaxLength(200).IsRequired();
        builder.Property(l => l.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(l => l.LostReason).HasMaxLength(500);
        builder.Property(l => l.Priority).HasConversion<string>().HasMaxLength(10);
        builder.Property(l => l.PreferredTime).HasMaxLength(PreferredTimeCodes.MaxLength);
        builder.Property(l => l.CurrentLevel).HasMaxLength(50);
        builder.HasIndex(l => new { l.BranchId, l.Status });
        builder.HasIndex(l => l.OperatorId);
    }
}

public class CrmCallConfiguration : IEntityTypeConfiguration<CrmCall>
{
    public void Configure(EntityTypeBuilder<CrmCall> builder)
    {
        builder.ToTable("crm_calls");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Outcome).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Notes).HasMaxLength(1000);
        builder.HasIndex(c => c.LeadId);
    }
}

public class CrmTaskConfiguration : IEntityTypeConfiguration<CrmTask>
{
    public void Configure(EntityTypeBuilder<CrmTask> builder)
    {
        builder.ToTable("crm_tasks");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.ReasonCode).HasMaxLength(40);
        builder.Property(t => t.Note).HasMaxLength(500);
        builder.HasIndex(t => new { t.AssignedTo, t.Status, t.DueAt });
        builder.HasIndex(t => new { t.LeadId, t.ReasonCode });
        builder.HasIndex(t => new { t.StudentId, t.ReasonCode });
    }
}

public class TrialLessonConfiguration : IEntityTypeConfiguration<TrialLesson>
{
    public void Configure(EntityTypeBuilder<TrialLesson> builder)
    {
        builder.ToTable("trial_lessons");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.AttendanceStatus).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(t => t.LeadId);
        builder.HasIndex(t => t.GroupId);
    }
}

public class PlacementAttemptConfiguration : IEntityTypeConfiguration<PlacementAttempt>
{
    public void Configure(EntityTypeBuilder<PlacementAttempt> builder)
    {
        builder.ToTable("placement_attempts");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.RecommendedLevel).HasMaxLength(30).IsRequired();
        builder.HasIndex(p => p.LeadId);
    }
}

public class ComplaintConfiguration : IEntityTypeConfiguration<Complaint>
{
    public void Configure(EntityTypeBuilder<Complaint> builder)
    {
        builder.ToTable("complaints");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Subject).HasMaxLength(Complaint.MaxSubjectLength).IsRequired();
        builder.Property(c => c.Body).HasMaxLength(Complaint.MaxBodyLength).IsRequired();
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Resolution).HasMaxLength(Complaint.MaxResolutionLength);
        builder.Property(c => c.FeedbackComment).HasMaxLength(Complaint.MaxFeedbackLength);

        // A branch's board is "these statuses, newest first"; a person's own list is "mine"; an executor's is "assigned to me".
        builder.HasIndex(c => new { c.BranchId, c.Status, c.CreatedAt });
        builder.HasIndex(c => c.AuthorUserId);
        builder.HasIndex(c => c.AssignedToUserId);
        builder.HasIndex(c => c.StudentId);
    }
}
