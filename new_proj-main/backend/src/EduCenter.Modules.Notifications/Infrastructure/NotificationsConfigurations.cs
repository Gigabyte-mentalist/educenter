using EduCenter.Modules.Notifications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduCenter.Modules.Notifications.Infrastructure;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Type).HasMaxLength(50).IsRequired();
        builder.Property(n => n.Title).HasMaxLength(200).IsRequired();
        builder.Property(n => n.Body).HasMaxLength(1000).IsRequired();
        builder.Property(n => n.Link).HasMaxLength(300);
        builder.Property(n => n.DedupeKey).HasMaxLength(150);

        builder.HasIndex(n => new { n.UserId, n.CreatedAt });
        builder.HasIndex(n => new { n.UserId, n.ReadAt });
        // A timer-produced reminder reaches each user at most once.
        builder.HasIndex(n => new { n.UserId, n.DedupeKey }).IsUnique().HasFilter("\"DedupeKey\" IS NOT NULL");
    }
}

public class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        builder.ToTable("notification_preferences");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Type).HasMaxLength(50).IsRequired();
        builder.Property(p => p.Channel).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(p => new { p.UserId, p.Type, p.Channel }).IsUnique();
    }
}

public class NotificationDeliveryConfiguration : IEntityTypeConfiguration<NotificationDelivery>
{
    public void Configure(EntityTypeBuilder<NotificationDelivery> builder)
    {
        builder.ToTable("notification_deliveries");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Channel).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.Type).HasMaxLength(50).IsRequired();
        builder.Property(d => d.Subject).HasMaxLength(200).IsRequired();
        builder.Property(d => d.Body).HasMaxLength(1000).IsRequired();
        builder.Property(d => d.DedupeKey).HasMaxLength(150);
        builder.Property(d => d.Error).HasMaxLength(200);

        builder.HasIndex(d => d.UserId);
        builder.HasIndex(d => d.NotificationId);
        // "How many SMS went out today" is asked before every SMS; "what is still pending" by any sweep.
        builder.HasIndex(d => new { d.Channel, d.Status, d.SentAt });
        // A timer-produced reminder reaches each person on each channel at most once.
        builder.HasIndex(d => new { d.UserId, d.Channel, d.DedupeKey }).IsUnique().HasFilter("\"DedupeKey\" IS NOT NULL");
    }
}

public class AnnouncementConfiguration : IEntityTypeConfiguration<Announcement>
{
    public void Configure(EntityTypeBuilder<Announcement> builder)
    {
        builder.ToTable("announcements");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Title).HasMaxLength(200).IsRequired();
        builder.Property(a => a.Body).HasMaxLength(4000).IsRequired();
        builder.Property(a => a.Audience).HasConversion<string>().HasMaxLength(20);

        // The list is "what is live for me, newest first", cut by where it applies.
        builder.HasIndex(a => new { a.OrganizationId, a.PublishedAt });
        builder.HasIndex(a => a.BranchId);
        builder.HasIndex(a => a.GroupId);
    }
}
