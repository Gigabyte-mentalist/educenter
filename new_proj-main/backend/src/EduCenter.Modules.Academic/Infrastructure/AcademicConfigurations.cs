using EduCenter.Modules.Academic.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduCenter.Modules.Academic.Infrastructure;

public class CourseConfiguration : IEntityTypeConfiguration<Course>
{
    public void Configure(EntityTypeBuilder<Course> builder)
    {
        builder.ToTable("courses");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).HasMaxLength(200).IsRequired();
        builder.Property(c => c.LevelCategory).HasMaxLength(50).IsRequired();
    }
}

public class GroupConfiguration : IEntityTypeConfiguration<Group>
{
    public void Configure(EntityTypeBuilder<Group> builder)
    {
        builder.ToTable("groups");
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Name).HasMaxLength(100).IsRequired();
        builder.Property(g => g.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(g => new { g.OrganizationId, g.BranchId });

        builder.HasMany(g => g.Staff).WithOne().HasForeignKey(s => s.GroupId).OnDelete(DeleteBehavior.Cascade);
        // Must come AFTER HasMany — configuring the relationship first, then setting access mode on the
        // navigation, is the order EF Core actually honors; the reverse silently leaves EF reading
        // through the public getter, so appends to the private backing list are missed by change
        // detection and SaveChanges emits an UPDATE for a row that was never inserted (a spurious
        // DbUpdateConcurrencyException — "expected to affect 1 row(s), but actually affected 0").
        builder.Navigation(g => g.Staff).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class GroupStaffConfiguration : IEntityTypeConfiguration<GroupStaff>
{
    public void Configure(EntityTypeBuilder<GroupStaff> builder)
    {
        builder.ToTable("group_staff");
        builder.HasKey(gs => gs.Id);
        builder.Property(gs => gs.Role).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(gs => new { gs.GroupId, gs.UserId }).IsUnique();
    }
}

public class LessonConfiguration : IEntityTypeConfiguration<Lesson>
{
    public void Configure(EntityTypeBuilder<Lesson> builder)
    {
        builder.ToTable("lessons");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.RoomName).HasMaxLength(50).IsRequired();
        builder.Property(l => l.Topic).HasMaxLength(200);
        builder.Property(l => l.Notes).HasMaxLength(2000);        // Rows that existed before cancelling was possible are all scheduled lessons.
        builder.Property(l => l.Status).HasConversion<string>().HasMaxLength(20).HasDefaultValueSql("'Scheduled'");
        builder.Property(l => l.CancelReason).HasMaxLength(500);

        // Both indexes back the conflict detector: is this room/teacher already booked in this window?
        builder.HasIndex(l => new { l.BranchId, l.RoomName, l.StartsAt });
        builder.HasIndex(l => new { l.RoomId, l.StartsAt });
        // A room that has lessons cannot be erased — it is switched off instead (RoomsController).
        builder.HasOne<Room>().WithMany().HasForeignKey(l => l.RoomId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(l => new { l.TeacherId, l.StartsAt });
        builder.HasIndex(l => new { l.GroupId, l.StartsAt });
    }
}

public class RoomConfiguration : IEntityTypeConfiguration<Room>
{
    public void Configure(EntityTypeBuilder<Room> builder)
    {
        builder.ToTable("rooms");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Name).HasMaxLength(Room.MaxNameLength).IsRequired();
        builder.Property(r => r.Equipment).HasMaxLength(Room.MaxEquipmentLength);
        builder.HasIndex(r => new { r.BranchId, r.Name }).IsUnique();
    }
}

public class GroupScheduleSlotConfiguration : IEntityTypeConfiguration<GroupScheduleSlot>
{
    public void Configure(EntityTypeBuilder<GroupScheduleSlot> builder)
    {
        builder.ToTable("group_schedule_slots");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.DayOfWeek).HasConversion<string>().HasMaxLength(12);
        builder.HasIndex(s => s.GroupId);
        // A room in a template cannot be erased under it; a room with lessons is switched off, never deleted, and one without still has its template rows.
        builder.HasOne<Room>().WithMany().HasForeignKey(s => s.RoomId).OnDelete(DeleteBehavior.Restrict);
    }
}
