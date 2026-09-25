using EduCenter.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Academic;

/// <summary>Conflict Detector (docs/00_TZ.md §18): a lesson clashes if the same room, the same teacher, or the same group is
/// already booked in an overlapping time window. A cancelled lesson books nothing, and a lesson being edited must not clash with
/// itself — pass its id as <c>excludeLessonId</c>.</summary>
public static class ScheduleConflicts
{
    public static async Task<List<ScheduleConflict>> FindAsync(
        AppDbContext db, LessonCandidate candidate, Guid? excludeLessonId = null, CancellationToken ct = default)
    {
        var lessons = db.Lessons.Held();
        if (excludeLessonId is { } own) lessons = lessons.Where(l => l.Id != own);

        // A room is identified by its id (S07.03). A lesson that only ever named its room (no id) is matched by that name — a lesson
        // put in a Room still collides with an old lesson in the room of the same name — and so is a candidate without an id.
        var roomName = candidate.RoomName ?? string.Empty;
        var inWindow = lessons.Where(l => l.BranchId == candidate.BranchId && l.StartsAt < candidate.EndsAt && candidate.StartsAt < l.EndsAt);
        var sameRoom = candidate.RoomId is { } roomId
            ? inWindow.Where(l => l.RoomId == roomId || (l.RoomId == null && l.RoomName == roomName))
            : inWindow.Where(l => l.RoomName == roomName);
        var roomConflicts = await sameRoom
            .Select(l => new ScheduleConflict("ROOM", l.Id, l.StartsAt, l.EndsAt))
            .ToListAsync(ct);

        // Teacher and group clashes are not branch-bound — a teacher can't be in two branches at once either.
        var teacherConflicts = await lessons
            .Where(l => l.TeacherId == candidate.TeacherId && l.StartsAt < candidate.EndsAt && candidate.StartsAt < l.EndsAt)
            .Select(l => new ScheduleConflict("TEACHER", l.Id, l.StartsAt, l.EndsAt))
            .ToListAsync(ct);

        var groupConflicts = await lessons
            .Where(l => l.GroupId == candidate.GroupId && l.StartsAt < candidate.EndsAt && candidate.StartsAt < l.EndsAt)
            .Select(l => new ScheduleConflict("GROUP", l.Id, l.StartsAt, l.EndsAt))
            .ToListAsync(ct);

        return roomConflicts.Concat(teacherConflicts).Concat(groupConflicts).ToList();
    }
}
