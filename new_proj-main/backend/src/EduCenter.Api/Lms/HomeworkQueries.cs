using EduCenter.Infrastructure;
using EduCenter.Modules.Lms.Domain;
using EduCenter.Modules.Students.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Lms;

/// <summary>A homework and the group its lesson belongs to — what counting assignments needs to know about it.</summary>
public record HomeworkRef(Guid Id, Guid GroupId);

/// <summary>
/// Who a homework is for (docs/21.09-audit-jurnali S03.04). A homework with no <see cref="HomeworkTarget"/> rows goes to the whole
/// group; one with rows goes only to those students. Every count of "assigned", "completed", "still to hand in" and every reminder
/// asks here instead of assuming the group's roster — otherwise a student who was never given an individual task would look
/// as if they had skipped it, and be reminded about it, and lose a percentage for it.
/// </summary>
public static class HomeworkQueries
{
    /// <summary>The homework that concerns this student: the whole group's, and the ones given to them by name.</summary>
    public static IQueryable<Homework> AssignedTo(this IQueryable<Homework> homework, AppDbContext db, Guid studentId) =>
        homework.Where(h => !db.HomeworkTargets.Any(t => t.HomeworkId == h.Id)
            || db.HomeworkTargets.Any(t => t.HomeworkId == h.Id && t.StudentId == studentId));

    /// <summary>The homework that concerns at least one of these students — a parent's view of their children.</summary>
    public static IQueryable<Homework> AssignedToAny(this IQueryable<Homework> homework, AppDbContext db, List<Guid> studentIds) =>
        homework.Where(h => !db.HomeworkTargets.Any(t => t.HomeworkId == h.Id)
            || db.HomeworkTargets.Any(t => t.HomeworkId == h.Id && studentIds.Contains(t.StudentId)));

    /// <summary>Of these students, the ones a homework is for — all of them, unless the homework was given to some by name.</summary>
    public static IQueryable<Student> AssignedStudents(this IQueryable<Student> students, AppDbContext db, Guid homeworkId) =>
        students.Where(s => !db.HomeworkTargets.Any(t => t.HomeworkId == homeworkId)
            || db.HomeworkTargets.Any(t => t.HomeworkId == homeworkId && t.StudentId == s.Id));

    /// <summary>How many (homework, student) pairs these homework put on the students of their groups: an ordinary homework is one
    /// pair per student in its group, an individual one only for the students it was given to (who are still in the group). This is
    /// the denominator of every "homework completion" figure.</summary>
    public static async Task<int> CountAssignmentsAsync(this AppDbContext db, IReadOnlyCollection<HomeworkRef> homework, CancellationToken ct = default)
    {
        if (homework.Count == 0) return 0;
        var groupIds = homework.Select(h => h.GroupId).Distinct().ToList();
        var homeworkIds = homework.Select(h => h.Id).ToList();

        var rosters = (await db.Enrollments
                .Where(e => groupIds.Contains(e.GroupId) && e.Status == EnrollmentStatus.Active)
                .Select(e => new { e.GroupId, e.StudentId })
                .ToListAsync(ct))
            .ToLookup(e => e.GroupId, e => e.StudentId);
        var targeting = await LoadTargetingAsync(db, homeworkIds, ct);

        return homework.Sum(h => rosters[h.GroupId].Count(studentId => targeting.IsAssigned(h.Id, studentId)));
    }

    /// <summary>Loads who the individual homework among these were given to, for code that walks (homework, student) pairs in memory.</summary>
    public static async Task<HomeworkTargeting> LoadTargetingAsync(this AppDbContext db, IReadOnlyCollection<Guid> homeworkIds, CancellationToken ct = default)
    {
        if (homeworkIds.Count == 0) return new HomeworkTargeting(new Dictionary<Guid, HashSet<Guid>>());
        var rows = await db.HomeworkTargets
            .Where(t => homeworkIds.Contains(t.HomeworkId))
            .Select(t => new { t.HomeworkId, t.StudentId })
            .ToListAsync(ct);
        return new HomeworkTargeting(rows.GroupBy(r => r.HomeworkId).ToDictionary(g => g.Key, g => g.Select(r => r.StudentId).ToHashSet()));
    }
}

/// <summary>The in-memory answer to "is this homework for this student", for the homework loaded with <see cref="HomeworkQueries.LoadTargetingAsync"/>.</summary>
public sealed class HomeworkTargeting(Dictionary<Guid, HashSet<Guid>> studentsByHomework)
{
    public bool IsAssigned(Guid homeworkId, Guid studentId) =>
        !studentsByHomework.TryGetValue(homeworkId, out var students) || students.Contains(studentId);
}
