using EduCenter.SharedKernel;

namespace EduCenter.Modules.Crm.Domain;

public enum TrialAttendanceStatus
{
    Scheduled,
    Attended,
    Missed
}

/// <summary>docs/00_TZ.md §13 — an optional trial class a Lead is booked into before enrollment.</summary>
public class TrialLesson : TenantEntity
{
    private TrialLesson() { } // EF Core

    public TrialLesson(Guid organizationId, Guid leadId, Guid groupId, DateTimeOffset scheduledAt)
    {
        OrganizationId = organizationId;
        LeadId = leadId;
        GroupId = groupId;
        ScheduledAt = scheduledAt;
        AttendanceStatus = TrialAttendanceStatus.Scheduled;
    }

    public Guid LeadId { get; private set; }
    public Guid GroupId { get; private set; }
    public DateTimeOffset ScheduledAt { get; private set; }
    public TrialAttendanceStatus AttendanceStatus { get; private set; }

    public void MarkAttended()
    {
        AttendanceStatus = TrialAttendanceStatus.Attended;
        MarkUpdated();
    }

    public void MarkMissed()
    {
        AttendanceStatus = TrialAttendanceStatus.Missed;
        MarkUpdated();
    }
}
