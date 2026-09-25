using EduCenter.Api.Authorization;
using EduCenter.Api.Notifications;
using EduCenter.Modules.Notifications.Domain;
using EduCenter.Infrastructure;
using EduCenter.Modules.Lms.Domain;
using EduCenter.Modules.Students.Domain;
using EduCenter.Modules.Timeline.Domain;
using EduCenter.SharedKernel;
using EduCenter.SharedKernel.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Lms;

public record ExamDto(Guid Id, Guid GroupId, string Name, ExamType Type, DateOnly Date);
public record CreateExamRequest(Guid GroupId, string Name, ExamType Type, DateOnly Date);
/// <summary>Only what is sent changes; the group never does.</summary>
public record UpdateExamRequest(string? Name, ExamType? Type, DateOnly? Date);

/// <param name="StudentName">Filled by the results list, so the screen names each row without loading the student list.</param>
/// <param name="Grammar">Percent 0–100 (the skills above are IELTS bands 0–9); null when the exam did not grade it.</param>
public record ExamResultDto(Guid Id, Guid ExamId, Guid StudentId,
    decimal? Listening, decimal? Reading, decimal? Writing, decimal? Speaking, decimal? Overall, string? StudentName = null,
    decimal? Grammar = null, decimal? Vocabulary = null);
public record RecordExamResultRequest(Guid StudentId,
    decimal? Listening, decimal? Reading, decimal? Writing, decimal? Speaking, decimal? Overall,
    decimal? Grammar = null, decimal? Vocabulary = null);

[ApiController]
[Route("api/exams")]
public class ExamsController(AppDbContext db, NotificationDispatcher notifier, NotificationRecipients recipients) : ControllerBase
{
    private const int MaxNameLength = 200;

    [HttpGet]
    [Authorize(Policy = "exams.view")]
    public async Task<ActionResult<Page<ExamDto>>> List(
        [FromQuery] Guid? groupId, [FromQuery] string? search, [FromQuery] PageRequest? paging, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();

        // Exams are visible through the groups the caller may see — for students/parents that means
        // the groups they're enrolled in, not every exam in the organization.
        // Filter and order on the entity, project last — EF can't translate an OrderBy over a
        // constructed DTO (same translation trap as the earlier Homework Self-scope bug).
        var visibleGroups = await scope.ScopedGroupsAsync(db);
        var query =
            from e in db.Exams
            join g in visibleGroups on e.GroupId equals g.Id
            where e.OrganizationId == scope.OrganizationId
            select e;

        if (groupId is not null) query = query.Where(e => e.GroupId == groupId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = PagingExtensions.ContainsPattern(search);
            query = query.Where(e => EF.Functions.ILike(e.Name, term));
        }

        return Ok(await query
            .OrderByDescending(e => e.Date).ThenBy(e => e.Id)
            .Select(e => new ExamDto(e.Id, e.GroupId, e.Name, e.Type, e.Date))
            .ToPageAsync(paging, ct));
    }

    [HttpPost]
    [Authorize(Policy = "exams.create")]
    public async Task<ActionResult<ExamDto>> Create(CreateExamRequest request)
    {
        var scope = HttpContext.RequireResolvedScope();

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Imtihon nomi ko'rsatilishi shart." });

        if (!Enum.IsDefined(request.Type) || request.Date == default)
            return BadRequest(new { error = "Imtihon turi va sanasini to'g'ri kiriting." });

        var group = await db.Groups.FirstOrDefaultAsync(g => g.Id == request.GroupId && g.OrganizationId == scope.OrganizationId);
        if (group is null) return NotFound(new { error = "Guruh topilmadi." });

        if (!await scope.CanAccessGroupAsync(db, group.Id, group.BranchId))
            return Forbid();

        var exam = new Exam(scope.OrganizationId, request.GroupId, request.Name.Trim(), request.Type, request.Date);
        db.Exams.Add(exam);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(List), new ExamDto(exam.Id, exam.GroupId, exam.Name, exam.Type, exam.Date));
    }

    /// <summary>Fixes a wrong name, type or date. Allowed after results are in — the results stay where they are.</summary>
    [HttpPatch("{id:guid}")]
    [Authorize(Policy = "exams.create")]
    public async Task<ActionResult<ExamDto>> Update(Guid id, UpdateExamRequest request, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        var exam = await db.Exams.FirstOrDefaultAsync(e => e.Id == id && e.OrganizationId == scope.OrganizationId, ct);
        if (exam is null) return NotFound();
        if (!await CanAccessAsync(scope, exam, ct)) return Forbid();

        if (request.Name is null && request.Type is null && request.Date is null)
            return BadRequest(new { error = "O'zgartirish uchun nom, tur yoki sanani ko'rsating." });
        var name = request.Name?.Trim() ?? exam.Name;
        if (name.Length == 0 || name.Length > MaxNameLength)
            return BadRequest(new { error = $"Imtihon nomi 1 dan {MaxNameLength} tagacha belgi bo'lishi kerak." });
        var type = request.Type ?? exam.Type;
        var date = request.Date ?? exam.Date;
        if (!Enum.IsDefined(type) || date == default)
            return BadRequest(new { error = "Imtihon turi va sanasini to'g'ri kiriting." });

        exam.Update(name, type, date);
        await db.SaveChangesAsync(ct);
        return Ok(new ExamDto(exam.Id, exam.GroupId, exam.Name, exam.Type, exam.Date));
    }

    /// <summary>Erases an exam nobody has a result on. Results are what the exam is for; once any exists the exam stays, and the
    /// refusal says how many — remove the wrong ones first (or fix the exam instead of deleting it).</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "exams.create")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        var exam = await db.Exams.FirstOrDefaultAsync(e => e.Id == id && e.OrganizationId == scope.OrganizationId, ct);
        if (exam is null) return NotFound();
        if (!await CanAccessAsync(scope, exam, ct)) return Forbid();

        var results = await db.ExamResults.CountAsync(r => r.ExamId == id, ct);
        if (results > 0)
            return Conflict(new
            {
                error = $"Bu imtihonni o'chirib bo'lmaydi — {results} ta natija kiritilgan. Avval natijalarni o'chiring.",
                blockers = new[] { $"{results} ta natija" },
                resultCount = results,
            });

        db.Exams.Remove(exam);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Takes back a result that was entered wrongly. The student is <b>not</b> notified — a correction is not news — but
    /// their timeline records that it was removed, so the earlier "result recorded" entry does not stand alone.</summary>
    [HttpDelete("{id:guid}/results/{resultId:guid}")]
    [Authorize(Policy = "exams.score.edit")]
    public async Task<IActionResult> DeleteResult(Guid id, Guid resultId, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        var exam = await db.Exams.FirstOrDefaultAsync(e => e.Id == id && e.OrganizationId == scope.OrganizationId, ct);
        if (exam is null) return NotFound();
        if (!await CanAccessAsync(scope, exam, ct)) return Forbid();

        var result = await db.ExamResults.FirstOrDefaultAsync(r => r.Id == resultId && r.ExamId == id, ct);
        if (result is null) return NotFound();

        db.ExamResults.Remove(result);
        db.StudentTimelineEvents.Add(new StudentTimelineEvent(scope.OrganizationId, result.StudentId,
            TimelineEventType.ExamResultRemoved,
            System.Text.Json.JsonSerializer.Serialize(new { examId = id, examName = exam.Name, overall = result.Overall, byUserId = scope.UserId })));
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<bool> CanAccessAsync(ResolvedScope scope, Exam exam, CancellationToken ct)
    {
        var group = await db.Groups.FirstAsync(g => g.Id == exam.GroupId, ct);
        return await scope.CanAccessGroupAsync(db, group.Id, group.BranchId);
    }

    [HttpGet("{id:guid}/results")]
    [Authorize(Policy = "exams.view")]
    public async Task<ActionResult<List<ExamResultDto>>> ListResults(Guid id)
    {
        var scope = HttpContext.RequireResolvedScope();
        var exam = await db.Exams.FirstOrDefaultAsync(e => e.Id == id && e.OrganizationId == scope.OrganizationId);
        if (exam is null) return NotFound();

        var group = await db.Groups.FirstAsync(g => g.Id == exam.GroupId);
        if (!await scope.CanAccessGroupAsync(db, group.Id, group.BranchId))
            return Forbid();

        var query = db.ExamResults.Where(r => r.ExamId == id);

        if (scope.IsPersonalScope())
        {
            var visible = await scope.VisibleStudentIdsAsync(db);
            query = query.Where(r => visible.Contains(r.StudentId));
        }

        var results = await (
            from r in query
            join s in db.Students on r.StudentId equals s.Id
            join u in db.Users on s.UserId equals u.Id
            orderby u.FullName, r.Id
            select new ExamResultDto(r.Id, r.ExamId, r.StudentId, r.Listening, r.Reading, r.Writing, r.Speaking, r.Overall, u.FullName,
                r.Grammar, r.Vocabulary)
        ).ToListAsync();

        return Ok(results);
    }

    /// <summary>Records one student's result for an exam (docs/00_TZ.md §17). Recording a result also
    /// appends a Student Timeline entry (§9).</summary>
    [HttpPost("{id:guid}/results")]
    [Authorize(Policy = "exams.score.edit")]
    public async Task<ActionResult<ExamResultDto>> RecordResult(Guid id, RecordExamResultRequest request)
    {
        var scope = HttpContext.RequireResolvedScope();
        var exam = await db.Exams.FirstOrDefaultAsync(e => e.Id == id && e.OrganizationId == scope.OrganizationId);
        if (exam is null) return NotFound();

        var group = await db.Groups.FirstAsync(g => g.Id == exam.GroupId);
        if (!await scope.CanAccessGroupAsync(db, group.Id, group.BranchId))
            return Forbid();

        foreach (var (name, value) in new[] {
            ("listening", request.Listening), ("reading", request.Reading), ("writing", request.Writing),
            ("speaking", request.Speaking), ("overall", request.Overall) })
        {
            if (value is < 0 or > ExamResult.MaxBand)
                return BadRequest(new { error = $"'{name}' balli 0–9 oralig'ida bo'lishi kerak." });
        }

        // Grammar and Vocabulary are percentages, not bands — the TZ shows "Grammar: 71%".
        foreach (var (name, value) in new[] { ("grammar", request.Grammar), ("vocabulary", request.Vocabulary) })
        {
            if (value is < 0 or > ExamResult.MaxPercent)
                return BadRequest(new { error = $"'{name}' foizi 0–100 oralig'ida bo'lishi kerak." });
        }

        if (request.Listening is null && request.Reading is null && request.Writing is null
            && request.Speaking is null && request.Overall is null && request.Grammar is null && request.Vocabulary is null)
            return BadRequest(new { error = "Kamida bitta ball kiriting." });

        var isEnrolled = await db.Enrollments.AnyAsync(e =>
            e.GroupId == exam.GroupId && e.StudentId == request.StudentId && e.Status == EnrollmentStatus.Active);
        if (!isEnrolled)
            return BadRequest(new { error = "Student bu imtihon guruhiga yozilmagan." });

        var existing = await db.ExamResults.FirstOrDefaultAsync(r => r.ExamId == id && r.StudentId == request.StudentId);
        if (existing is not null)
            return Conflict(new { error = "Bu student uchun natija allaqachon kiritilgan." });

        var result = new ExamResult(scope.OrganizationId, id, request.StudentId,
            request.Listening, request.Reading, request.Writing, request.Speaking, request.Overall,
            request.Grammar, request.Vocabulary);
        db.ExamResults.Add(result);

        var studentName = await recipients.StudentNameAsync(request.StudentId);
        var score = request.Overall is { } overall
            ? $"Overall {overall.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}"
            : "natija kiritildi";
        await notifier.NotifyAsync(scope.OrganizationId, await recipients.StudentAndParentsAsync(request.StudentId),
            new NotificationMessage(NotificationType.ExamResult, $"Imtihon natijasi: {exam.Name}",
                $"{studentName} — {score}.", $"/students/{request.StudentId}"));

        db.StudentTimelineEvents.Add(new StudentTimelineEvent(scope.OrganizationId, request.StudentId,
            TimelineEventType.ExamResultRecorded,
            System.Text.Json.JsonSerializer.Serialize(new { examId = id, examName = exam.Name, overall = request.Overall })));

        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(ListResults), new { id },
            new ExamResultDto(result.Id, result.ExamId, result.StudentId, result.Listening, result.Reading, result.Writing, result.Speaking, result.Overall,
                Grammar: result.Grammar, Vocabulary: result.Vocabulary));
    }
}
