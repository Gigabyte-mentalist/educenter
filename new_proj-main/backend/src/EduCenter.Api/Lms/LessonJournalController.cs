using EduCenter.Api.Authorization;
using EduCenter.Infrastructure;
using EduCenter.Modules.Academic.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Lms;

/// <param name="MaxLessonScore">The top of this lesson's score scale; null means the lesson is not scored.</param>
public record LessonJournalDto(string? Topic, string? Notes, int? MaxLessonScore);

/// <summary>Only what is sent changes: <c>null</c> leaves the field alone, an empty text clears it, and for
/// <paramref name="MaxLessonScore"/> — a number — <c>0</c> is the way to say "this lesson is not scored".</summary>
public record UpdateLessonJournalRequest(string? Topic, string? Notes, int? MaxLessonScore = null);

/// <summary>The teacher's side of a lesson (docs/00_TZ.md §15): what it was about, how it went, whether it is scored. Kept apart
/// from the schedule endpoints on purpose — the topic is pedagogy, not timetabling, so it needs <c>attendance.mark</c> (which a
/// teacher holds) and not <c>schedule.edit</c> (which they do not). The notes are staff-only, and the schedule a student or
/// parent reads never carries them; only the topic does.</summary>
[ApiController]
[Route("api/lessons/{lessonId:guid}/journal")]
public class LessonJournalController(AppDbContext db) : ControllerBase
{
    private const int MaxTopicLength = 200;
    private const int MaxNotesLength = 2000;
    private const int HighestLessonScore = 100;

    [HttpGet]
    [Authorize(Policy = "attendance.mark")]
    public async Task<ActionResult<LessonJournalDto>> Get(Guid lessonId, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        var lesson = await db.Lessons.FirstOrDefaultAsync(l => l.Id == lessonId && l.OrganizationId == scope.OrganizationId, ct);
        if (lesson is null) return NotFound();
        if (!await scope.CanAccessGroupAsync(db, lesson.GroupId, lesson.BranchId)) return Forbid();

        return Ok(ToDto(lesson));
    }

    /// <summary>A lesson that is over is still written up — that is when most teachers do it. A cancelled one never took place,
    /// so there is nothing to record.</summary>
    [HttpPatch]
    [Authorize(Policy = "attendance.mark")]
    public async Task<ActionResult<LessonJournalDto>> Update(Guid lessonId, UpdateLessonJournalRequest request, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        var lesson = await db.Lessons.FirstOrDefaultAsync(l => l.Id == lessonId && l.OrganizationId == scope.OrganizationId, ct);
        if (lesson is null) return NotFound();
        if (!await scope.CanAccessGroupAsync(db, lesson.GroupId, lesson.BranchId)) return Forbid();
        if (lesson.IsCancelled)
            return Conflict(new { error = "Bekor qilingan darsga jurnal yozib bo'lmaydi." });
        if (request.Topic is null && request.Notes is null && request.MaxLessonScore is null)
            return BadRequest(new { error = "O'zgartirish uchun mavzu, izoh yoki dars ballining chegarasini ko'rsating." });

        var topic = Normalize(request.Topic);
        var notes = Normalize(request.Notes);
        if (topic?.Length > MaxTopicLength) return BadRequest(new { error = $"Mavzu {MaxTopicLength} belgidan oshmasin." });
        if (notes?.Length > MaxNotesLength) return BadRequest(new { error = $"Dars izohi {MaxNotesLength} belgidan oshmasin." });

        // 0 clears the scale; anything else has to be a real one.
        int? maxScore = request.MaxLessonScore is 0 ? null : request.MaxLessonScore;
        if (request.MaxLessonScore is < 0 or > HighestLessonScore)
            return BadRequest(new { error = $"Dars ballining chegarasi 1 dan {HighestLessonScore} gacha bo'lsin (0 — ball qo'yilmaydi)." });
        if (request.MaxLessonScore is not null && maxScore != lesson.MaxLessonScore)
        {
            // A score already given never silently falls off the scale.
            var highestGiven = await db.Attendance.Where(a => a.LessonId == lessonId).MaxAsync(a => a.Score, ct);
            if (highestGiven is not null && (maxScore is null || highestGiven > maxScore))
                return Conflict(new { error = $"Bu darsda {highestGiven} ball qo'yilgan — chegarani undan pastga tushirib bo'lmaydi." });
        }

        // Nobody is told: a topic filled in before class and reworded after would be noise on every student's phone.
        if (request.Topic is not null) lesson.SetTopic(topic);
        if (request.Notes is not null) lesson.SetNotes(notes);
        if (request.MaxLessonScore is not null) lesson.SetMaxLessonScore(maxScore);

        await db.SaveChangesAsync(ct);
        return Ok(ToDto(lesson));
    }

    private static LessonJournalDto ToDto(Lesson lesson) => new(lesson.Topic, lesson.Notes, lesson.MaxLessonScore);

    private static string? Normalize(string? text)
    {
        var trimmed = text?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
