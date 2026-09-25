using EduCenter.SharedKernel;

namespace EduCenter.Modules.Academic.Domain;

/// <summary>
/// One recurring slot of a group's week (docs/21.09-audit-jurnali S07.04): "Monday 18:00, 90 minutes, room A101". The times are the
/// branch's own wall-clock time — lessons themselves are stored as instants, and generating them converts each slot through the
/// branch's time zone. A group's template is the set of its slots; it is replaced as a whole.
/// </summary>
public class GroupScheduleSlot : TenantEntity
{
    public const int MinDurationMinutes = 15;
    public const int MaxDurationMinutes = 360;
    public const int MaxSlotsPerGroup = 21;

    private GroupScheduleSlot() { } // EF Core

    public GroupScheduleSlot(Guid organizationId, Guid groupId, DayOfWeek dayOfWeek, TimeOnly startTime, int durationMinutes, Guid? roomId, Guid? teacherId)
    {
        OrganizationId = organizationId;
        GroupId = groupId;
        DayOfWeek = dayOfWeek;
        StartTime = startTime;
        DurationMinutes = durationMinutes;
        RoomId = roomId;
        TeacherId = teacherId;
    }

    public Guid GroupId { get; private set; }
    public DayOfWeek DayOfWeek { get; private set; }
    public TimeOnly StartTime { get; private set; }
    public int DurationMinutes { get; private set; }
    /// <summary>The room the lessons of this slot are held in; a template may be saved without one, generating lessons needs it.</summary>
    public Guid? RoomId { get; private set; }
    /// <summary>Who teaches it; null means the group's main teacher.</summary>
    public Guid? TeacherId { get; private set; }

    public TimeOnly EndTime => StartTime.AddMinutes(DurationMinutes);
}
