using EduCenter.Modules.Timeline.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduCenter.Modules.Timeline.Infrastructure;

public class StudentTimelineEventConfiguration : IEntityTypeConfiguration<StudentTimelineEvent>
{
    public void Configure(EntityTypeBuilder<StudentTimelineEvent> builder)
    {
        builder.ToTable("student_timeline_events");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.EventType).HasMaxLength(50).IsRequired();
        builder.HasIndex(e => new { e.StudentId, e.OccurredAt });
    }
}
