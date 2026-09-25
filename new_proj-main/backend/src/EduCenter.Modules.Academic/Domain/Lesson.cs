using EduCenter.SharedKernel;

namespace EduCenter.Modules.Academic.Domain;

/// <summary>A cancelled lesson stays on the schedule (marked) with its attendance, homework and resources, but was never held:
/// it takes no part in conflicts, "held" counts, attendance rates or reminders.</summary>
public enum LessonStatus
{
    Scheduled,
    Cancelled
}

/// <summary>A single scheduled occurrence for a Group. The room is named by <see cref="RoomId"/> (S07.03); <see cref="RoomName"/> stays beside it
/// as the room's name at the time — old lessons never had an id, and every screen that shows a lesson still reads the name.</summary>
public class Lesson : BranchScopedEntity
{
    private Lesson() { } // EF Core

    public Lesson(Guid organizationId, Guid branchId, Guid groupId, Guid teacherId, string roomName,
        DateTimeOffset startsAt, DateTimeOffset endsAt, string? topic = null, Guid? roomId = null)
    {
        if (endsAt <= startsAt)
            throw new ArgumentException("Lesson end time must be after start time.");

        OrganizationId = organizationId;
        BranchId = branchId;
        GroupId = groupId;
        TeacherId = teacherId;
        RoomName = roomName;
        RoomId = roomId;
        StartsAt = startsAt;
        EndsAt = endsAt;
        Topic = topic;
    }

    public Guid GroupId { get; private set; }
    public Guid TeacherId { get; private set; }
    public string RoomName { get; private set; } = default!;
    /// <summary>The <see cref="Room"/> it is held in; null for a lesson that only ever named its room as text.</summary>
    public Guid? RoomId { get; private set; }
    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset EndsAt { get; private set; }
    public string? Topic { get; private set; }
    /// <summary>The teacher's own note on how the lesson went (docs/00_TZ.md §15). Staff-only: unlike <see cref="Topic"/> it is
    /// never part of the schedule a student or parent reads.</summary>
    public string? Notes { get; private set; }
    /// <summary>The top of the lesson's score scale (1–100); null means this lesson is not scored and attendance takes no score.</summary>
    public int? MaxLessonScore { get; private set; }
    public LessonStatus Status { get; private set; } = LessonStatus.Scheduled;
    public string? CancelReason { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

    public bool IsCancelled => Status == LessonStatus.Cancelled;

    public bool OverlapsWith(DateTimeOffset otherStart, DateTimeOffset otherEnd) =>
        StartsAt < otherEnd && otherStart < EndsAt;

    public void SetTopic(string? topic)
    {
        EnsureScheduled();
        Topic = topic;
        MarkUpdated();
    }

    public void SetNotes(string? notes)
    {
        EnsureScheduled();
        Notes = notes;
        MarkUpdated();
    }

    public void SetMaxLessonScore(int? maxScore)
    {
        EnsureScheduled();
        if (maxScore is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(maxScore), "Dars ballining chegarasi 1 dan 100 gacha.");
        MaxLessonScore = maxScore;
        MarkUpdated();
    }

    /// <summary>Moves the lesson in time and/or to another room. A cancelled lesson does not move — it is scheduled again as a
    /// new lesson.</summary>
    public void Reschedule(DateTimeOffset startsAt, DateTimeOffset endsAt, string roomName, Guid? roomId = null)
    {
        EnsureScheduled();
        if (endsAt <= startsAt)
            throw new ArgumentException("Lesson end time must be after start time.");

        StartsAt = startsAt;
        EndsAt = endsAt;
        RoomName = roomName;
        RoomId = roomId;
        MarkUpdated();
    }

    /// <summary>Ties a lesson that only knew its room by name to the <see cref="Room"/> of that name. Any lesson may be linked, cancelled or
    /// past — it changes nothing about the lesson, only what the room is known by.</summary>
    public void LinkRoom(Guid roomId)
    {
        RoomId = roomId;
        MarkUpdated();
    }

    public void Cancel(string reason)
    {
        EnsureScheduled();
        Status = LessonStatus.Cancelled;
        CancelReason = reason;
        CancelledAt = DateTimeOffset.UtcNow;
        MarkUpdated();
    }

    private void EnsureScheduled()
    {
        if (IsCancelled)
            throw new InvalidOperationException("Bekor qilingan darsni o'zgartirib bo'lmaydi.");
    }

    /// <summary>docs/00_TZ.md §19 — the main teacher cannot make it and somebody else takes the lesson. The
    /// lesson keeps its identity: attendance, homework and the group's history all still hang off this row.</summary>
    public void ReplaceTeacher(Guid teacherId)
    {
        EnsureScheduled();
        TeacherId = teacherId;
        MarkUpdated();
    }
}
