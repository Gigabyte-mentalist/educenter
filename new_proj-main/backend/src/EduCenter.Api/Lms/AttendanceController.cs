using EduCenter.Api.Authorization;
using EduCenter.Api.Automation;
using EduCenter.Modules.Automation.Domain;
using EduCenter.Api.Notifications;
using EduCenter.Modules.Notifications.Domain;
using EduCenter.Infrastructure;
using EduCenter.Modules.Lms.Domain;
using EduCenter.Modules.Students.Domain;
using EduCenter.Modules.Timeline.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Lms;

/// <param name="Note">Staff-only — filled in only for a caller who can also mark attendance, never for a student or a parent.</param>
/// <param name="Score">The student's score for the lesson; the student and the parent see their own.</param>
public record AttendanceDto(Guid Id, Guid LessonId, Guid StudentId, AttendanceStatus Status, string? Note = null, int? Score = null);

/// <summary>One item is the whole record of that student for the lesson: an omitted <paramref name="Note"/> or
/// <paramref name="Score"/> means "none", not "leave as it was" — the screen always sends the full roster back.</summary>
public record MarkAttendanceItem(Guid StudentId, AttendanceStatus Status, string? Note = null, int? Score = null);
public record MarkAttendanceRequest(List<MarkAttendanceItem> Items);

[ApiController]
[Route("api/lessons/{lessonId:guid}/attendance")]
public class AttendanceController(AppDbContext db, FollowUpRules followUps, NotificationDispatcher notifier,
    NotificationRecipients recipients, PermissionResolver permissions) : ControllerBase
{
    private const int MaxNoteLength = 500;

    [HttpGet]
    [Authorize(Policy = "attendance.view")]
    public async Task<ActionResult<List<AttendanceDto>>> List(Guid lessonId)
    {
        var scope = HttpContext.RequireResolvedScope();
        var lesson = await db.Lessons.FirstOrDefaultAsync(l => l.Id == lessonId && l.OrganizationId == scope.OrganizationId);
        if (lesson is null) return NotFound();

        if (!await scope.CanAccessGroupAsync(db, lesson.GroupId, lesson.BranchId))
            return Forbid();

        var query = db.Attendance.Where(a => a.LessonId == lessonId);

        // A student/parent sees only their own rows for this lesson — never the classmates'.
        if (scope.IsPersonalScope())
        {
            var visible = await scope.VisibleStudentIdsAsync(db);
            query = query.Where(a => visible.Contains(a.StudentId));
        }

        var records = await query
            .Select(a => new AttendanceDto(a.Id, a.LessonId, a.StudentId, a.Status, a.Note, a.Score))
            .ToListAsync();

        // "Late — the bus broke down" is written for the staff. Reading attendance is not enough to see it: only whoever
        // could also have written it (attendance.mark) gets the remarks back.
        var canSeeNotes = await permissions.ResolveAsync(scope.UserId, "attendance.mark") is not null;
        return Ok(canSeeNotes ? records : records.Select(r => r with { Note = null }).ToList());
    }

    /// <summary>Bulk-marks attendance for a lesson — one call covers the whole roster (TZ §15).
    /// Upserts: re-marking a student updates their existing record instead of duplicating it — status, remark and score alike,
    /// so a remark can be corrected without the status changing. Only a change of status is a new fact about the student's day
    /// (timeline, parent notification, follow-up); a corrected remark or score is saved quietly.</summary>
    [HttpPost]
    [Authorize(Policy = "attendance.mark")]
    public async Task<IActionResult> Mark(Guid lessonId, MarkAttendanceRequest request)
    {
        var scope = HttpContext.RequireResolvedScope();
        var lesson = await db.Lessons.FirstOrDefaultAsync(l => l.Id == lessonId && l.OrganizationId == scope.OrganizationId);
        if (lesson is null) return NotFound();

        if (!await scope.CanAccessGroupAsync(db, lesson.GroupId, lesson.BranchId))
            return Forbid();
        if (lesson.IsCancelled)
            return Conflict(new { error = "Bekor qilingan darsga davomat belgilab bo'lmaydi." });

        // Only students actually enrolled in this lesson's group may be marked — otherwise a teacher
        // could write attendance rows for any student id in the system.
        var enrolled = await db.Enrollments
            .Where(e => e.GroupId == lesson.GroupId && e.Status == EnrollmentStatus.Active)
            .Select(e => e.StudentId)
            .ToListAsync();
        var items = request.Items.DistinctBy(i => i.StudentId).ToList();
        var unknown = items.Select(i => i.StudentId).Where(id => !enrolled.Contains(id)).ToList();
        if (unknown.Count > 0)
            return BadRequest(new { error = "Ba'zi studentlar bu guruhga yozilmagan.", studentIds = unknown });

        var notes = items.ToDictionary(i => i.StudentId, i => string.IsNullOrWhiteSpace(i.Note) ? null : i.Note.Trim());
        if (notes.Values.Any(n => n?.Length > MaxNoteLength))
            return BadRequest(new { error = $"Izoh {MaxNoteLength} belgidan oshmasin." });

        // A lesson takes scores only when its scale was set (LessonJournalController); refusing beats silently dropping a
        // score the teacher believes was saved.
        var scores = items.Where(i => i.Score is not null).ToList();
        if (scores.Count > 0)
        {
            if (lesson.MaxLessonScore is not { } max)
                return BadRequest(new { error = "Bu darsda ball qo'yilmaydi — avval dars ballining chegarasini belgilang." });
            if (scores.Any(i => i.Score < 0 || i.Score > max))
                return BadRequest(new { error = $"Ball 0 dan {max} gacha bo'lsin." });
        }

        var existing = await db.Attendance.Where(a => a.LessonId == lessonId).ToListAsync();
        var existingByStudent = existing.ToDictionary(a => a.StudentId);
        var newlyAbsent = new List<Guid>();

        foreach (var item in items)
        {
            var note = notes[item.StudentId];
            if (existingByStudent.TryGetValue(item.StudentId, out var record))
            {
                var statusChanged = record.Status != item.Status;
                if (!statusChanged && record.Note == note && record.Score == item.Score) continue; // nothing changed
                record.Update(item.Status, note, item.Score);
                if (!statusChanged) continue; // a corrected remark or score is not a new fact: no duplicate timeline entry
            }
            else
            {
                db.Attendance.Add(new Attendance(scope.OrganizationId, lessonId, item.StudentId, item.Status, note, item.Score));
            }

            if (item.Status == AttendanceStatus.Absent) newlyAbsent.Add(item.StudentId);

            // Only Present/Absent are timeline-worthy (docs/00_TZ.md §9 example list) — Late/Excused/
            // Online don't drive the "2 consecutive absences" support signal the way Absent does.
            var eventType = item.Status switch
            {
                AttendanceStatus.Present => TimelineEventType.LessonAttended,
                AttendanceStatus.Absent => TimelineEventType.LessonAbsent,
                _ => null
            };
            if (eventType is not null)
                db.StudentTimelineEvents.Add(new StudentTimelineEvent(scope.OrganizationId, item.StudentId,
                    eventType, $$"""{"lessonId":"{{lessonId}}"}"""));
        }

        if (newlyAbsent.Count > 0)
        {
            // TZ §23 parent notification "student absent".
            var context = await (
                from g in db.Groups
                join b in db.Branches on g.BranchId equals b.Id
                where g.Id == lesson.GroupId
                select new { g.Name, b.Timezone }
            ).FirstAsync();
            var startsLocal = BranchClock.LocalDateTime(context.Timezone, lesson.StartsAt);

            foreach (var studentId in newlyAbsent.Distinct())
            {
                var studentName = await recipients.StudentNameAsync(studentId);
                await notifier.NotifyAsync(scope.OrganizationId, await recipients.ParentsAsync(studentId),
                    new NotificationMessage(NotificationType.Absent, "Farzandingiz darsga kelmadi",
                        $"{studentName} {startsLocal:dd.MM HH:mm} dagi darsga kelmadi ({context.Name}).", $"/students/{studentId}"));
            }
        }

        await db.SaveChangesAsync();

        // §10 Absence follow-up runs right away so the operator's queue and the support recommendation
        // appear the moment the second absence is saved, not on the next sweep.
        if (newlyAbsent.Count > 0)
            await followUps.EvaluateAbsencesAsync(scope.OrganizationId, new AbsenceTarget(lesson.GroupId, newlyAbsent), AutomationTrigger.Event);

        return NoContent();
    }
}
