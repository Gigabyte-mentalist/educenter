using EduCenter.Modules.Academic.Domain;

namespace EduCenter.Api.Academic;

public static class LessonQueries
{
    /// <summary>Lessons that count as held or still to be held: everything except a cancelled one. Every "how many lessons",
    /// "was attendance marked", "who is busy" and "remind about this lesson" question goes through this — a cancelled lesson
    /// never took place, so it is not an absence, a missing register or a clash. Deliberately left off where the cancelled row
    /// itself is the point: the schedule list (shows it, marked), reading a lesson by id, and a delete's list of blockers.</summary>
    public static IQueryable<Lesson> Held(this IQueryable<Lesson> lessons) =>
        lessons.Where(l => l.Status != LessonStatus.Cancelled);
}
