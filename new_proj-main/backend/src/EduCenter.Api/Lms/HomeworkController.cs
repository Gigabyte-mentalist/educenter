using EduCenter.Api.Auth;
using EduCenter.Api.Authorization;
using EduCenter.Api.Automation;
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

/// <summary>What a stored file is called and how big it is — never the storage key, which is the server's business.</summary>
public record HomeworkFileDto(string FileName, string ContentType, long Size);

/// <param name="Attachment">The teacher's worksheet or audio for the task, when there is one.</param>
/// <param name="Targeted">Given to some students by name (an individual task) rather than to the whole group. Everyone who may see
/// the homework learns this much.</param>
/// <param name="StudentIds">Who an individual homework was given to — sent only to whoever may give homework (<c>homework.create</c>);
/// a student or a parent is told that it is individual, not who else it was given to. Null for a whole-group homework.</param>
public record HomeworkDto(Guid Id, Guid LessonId, string Title, string? Description, HomeworkType Type,
    DateTimeOffset Deadline, int MaxScore, HomeworkFileDto? Attachment = null, bool Targeted = false, List<Guid>? StudentIds = null);
/// <summary>Only what is sent changes; an empty description clears it. The lesson never does.</summary>
public record UpdateHomeworkRequest(string? Title, string? Description, HomeworkType? Type, DateTimeOffset? Deadline, int? MaxScore);
/// <param name="StudentIds">Absent or empty: the whole group. Otherwise the homework is individual and goes only to these students,
/// each of whom has to be an active student of the lesson's group.</param>
public record CreateHomeworkRequest(Guid LessonId, string Title, string? Description, HomeworkType Type,
    DateTimeOffset Deadline, int MaxScore, List<Guid>? StudentIds = null);

/// <summary>The upload variant of <see cref="CreateHomeworkRequest"/>: the same fields plus the teacher's file. A homework without
/// a file still goes through the JSON endpoint.</summary>
public class CreateHomeworkForm
{
    public Guid LessonId { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public HomeworkType Type { get; set; }
    public DateTimeOffset Deadline { get; set; }
    public int MaxScore { get; set; }
    public List<Guid>? StudentIds { get; set; }
    public IFormFile? File { get; set; }
}

/// <param name="File">The student's answer as a file, when they attached one.</param>
public record SubmissionDto(Guid Id, Guid HomeworkId, Guid StudentId, string? ContentUrl, string? ContentText,
    DateTimeOffset SubmittedAt, int? Score, string? Feedback, DateTimeOffset? ReviewedAt, HomeworkFileDto? File = null);
public record SubmitHomeworkRequest(string? ContentUrl, string? ContentText);
public record ReviewSubmissionRequest(int Score, string? Feedback);

/// <summary>The upload variant of <see cref="SubmitHomeworkRequest"/>: a file, with or without the student's own words.</summary>
public class SubmitHomeworkFileForm
{
    public IFormFile? File { get; set; }
    public string? ContentText { get; set; }
}

[ApiController]
[Route("api/homework")]
public class HomeworkController(AppDbContext db, NotificationDispatcher notifier, NotificationRecipients recipients,
    EduCenter.Api.Ai.Writing.WritingEvaluator writing, HomeworkFileStorage files, PermissionResolver permissions) : ControllerBase
{
    // What the homework tables hold (see HomeworkConfiguration, HomeworkSubmissionConfiguration).
    private const int MaxTitleLength = 200;
    private const int MaxDescriptionLength = 2000;
    private const int MaxSubmissionTextLength = 10000;
    // The upload ceiling (HomeworkFileOptions.MaxBytes, 20 MB) plus the multipart envelope.
    private const long MaxUploadRequestBytes = 22 * 1024 * 1024;

    [HttpGet]
    [Authorize(Policy = "homework.view")]
    public async Task<ActionResult<List<HomeworkDto>>> List([FromQuery] Guid? groupId, [FromQuery] Guid? lessonId)
    {
        var scope = HttpContext.RequireResolvedScope();

        // One SQL query: homework → lesson → (groups the caller may see). Replaces the earlier
        // load-everything-then-check-each-row loop, and covers students/parents via their enrollments.
        var visibleGroups = await scope.ScopedGroupsAsync(db);

        // A student sees the group's homework and the individual ones given to them; a parent, those of any of their children.
        // Staff see them all — an individual homework is the teacher's to manage.
        IQueryable<Homework> source = db.Homework;
        if (scope.IsPersonalScope())
            source = source.AssignedToAny(db, await scope.VisibleStudentIdsAsync(db));

        var query =
            from h in source
            join l in db.Lessons on h.LessonId equals l.Id
            join g in visibleGroups on l.GroupId equals g.Id
            where h.OrganizationId == scope.OrganizationId
            select new
            {
                h.Id, h.LessonId, h.Title, h.Description, h.Type, h.Deadline, h.MaxScore, l.GroupId,
                h.AttachmentKey, h.AttachmentName, h.AttachmentContentType, h.AttachmentSize,
            };

        if (groupId is not null) query = query.Where(x => x.GroupId == groupId);
        if (lessonId is not null) query = query.Where(x => x.LessonId == lessonId);

        var rows = await query.OrderByDescending(x => x.Deadline).ToListAsync();

        var homeworkIds = rows.Select(x => x.Id).ToList();
        var targets = (await db.HomeworkTargets.Where(t => homeworkIds.Contains(t.HomeworkId))
                .Select(t => new { t.HomeworkId, t.StudentId }).ToListAsync())
            .ToLookup(t => t.HomeworkId, t => t.StudentId);
        var canSeeWho = await permissions.ResolveAsync(scope.UserId, "homework.create") is not null;

        return Ok(rows.Select(x => new HomeworkDto(x.Id, x.LessonId, x.Title, x.Description, x.Type, x.Deadline, x.MaxScore,
            FileOf(x.AttachmentKey, x.AttachmentName, x.AttachmentContentType, x.AttachmentSize),
            targets.Contains(x.Id), canSeeWho && targets.Contains(x.Id) ? targets[x.Id].ToList() : null)).ToList());
    }

    [HttpPost]
    [Authorize(Policy = "homework.create")]
    public Task<ActionResult<HomeworkDto>> Create(CreateHomeworkRequest request, CancellationToken ct = default) =>
        CreateCoreAsync(request, null, ct);

    /// <summary>Creates a homework with the teacher's own file — a worksheet, an audio clip — attached. Everything else is the
    /// JSON <c>POST</c>: same checks, same notification to the group.</summary>
    [HttpPost("upload")]
    [Authorize(Policy = "homework.create")]
    [RequestSizeLimit(MaxUploadRequestBytes)]
    public Task<ActionResult<HomeworkDto>> CreateWithFile([FromForm] CreateHomeworkForm form, CancellationToken ct = default) =>
        CreateCoreAsync(new CreateHomeworkRequest(form.LessonId, form.Title, form.Description, form.Type, form.Deadline, form.MaxScore,
            form.StudentIds), form.File, ct);

    private async Task<ActionResult<HomeworkDto>> CreateCoreAsync(CreateHomeworkRequest request, IFormFile? file, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();

        var title = request.Title?.Trim();
        if (string.IsNullOrEmpty(title))
            return BadRequest(new { error = "Sarlavha ko'rsatilishi shart." });
        if (title.Length > MaxTitleLength)
            return BadRequest(new { error = $"Sarlavha {MaxTitleLength} belgidan oshmasin." });
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        if (description?.Length > MaxDescriptionLength)
            return BadRequest(new { error = $"Tavsif {MaxDescriptionLength} belgidan oshmasin." });
        if (!Enum.IsDefined(request.Type))
            return BadRequest(new { error = "Homework turini to'g'ri kiriting." });
        if (request.Deadline == default)
            return BadRequest(new { error = "Topshirish muddatini ko'rsating." });
        if (request.MaxScore < 1)
            return BadRequest(new { error = "Maksimal ball kamida 1 bo'lishi kerak." });
        if (file is not null && await files.ValidateAsync(file, ct) is { } problem)
            return BadRequest(new { error = problem });

        var lesson = await db.Lessons.FirstOrDefaultAsync(l => l.Id == request.LessonId && l.OrganizationId == scope.OrganizationId, ct);
        if (lesson is null) return NotFound(new { error = "Dars topilmadi." });

        if (!await scope.CanAccessGroupAsync(db, lesson.GroupId, lesson.BranchId))
            return Forbid();
        if (lesson.IsCancelled)
            return Conflict(new { error = "Bekor qilingan darsga uy vazifasi berib bo'lmaydi." });

        // An individual task goes to students named one by one — each has to be an active student of this very group, or the
        // "who is it for" list would point at people the teacher cannot see.
        var targetIds = request.StudentIds?.Distinct().ToList() ?? [];
        if (targetIds.Count > 0)
        {
            var inGroup = await db.Enrollments
                .Where(e => e.GroupId == lesson.GroupId && e.Status == EnrollmentStatus.Active && targetIds.Contains(e.StudentId))
                .Select(e => e.StudentId).ToListAsync(ct);
            var unknown = targetIds.Except(inGroup).ToList();
            if (unknown.Count > 0)
                return BadRequest(new { error = "Ba'zi studentlar bu guruhning faol o'quvchisi emas.", studentIds = unknown });
        }

        var homework = new Homework(scope.OrganizationId, request.LessonId, title, description,
            request.Type, request.Deadline, request.MaxScore);
        db.Homework.Add(homework);
        foreach (var studentId in targetIds)
            db.HomeworkTargets.Add(new HomeworkTarget(scope.OrganizationId, homework.Id, studentId));

        var context = await (
            from g in db.Groups
            join b in db.Branches on g.BranchId equals b.Id
            where g.Id == lesson.GroupId
            select new { g.Name, b.Timezone }
        ).FirstAsync(ct);
        var deadlineLocal = BranchClock.LocalDateTime(context.Timezone, homework.Deadline);
        await notifier.NotifyAsync(scope.OrganizationId, await AudienceAsync(lesson.GroupId, targetIds),
            new NotificationMessage(NotificationType.HomeworkCreated, $"Yangi homework: {homework.Title}",
                $"{context.Name} · topshirish muddati {deadlineLocal:dd.MM HH:mm}.", $"/schedule/{lesson.Id}"), ct);

        // The file is written last, once nothing else can refuse the request, and taken back if the save fails.
        string? storedKey = null;
        if (file is not null)
        {
            var saved = await files.SaveAsync(file, ct);
            storedKey = saved.Key;
            homework.AttachFile(saved.Key, saved.FileName, saved.ContentType, saved.Size);
        }
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            files.Delete(storedKey);
            throw;
        }

        return CreatedAtAction(nameof(List), ToDto(homework, targetIds));
    }

    /// <summary>Who hears about a homework: a whole-group one goes to the group's students; an individual one only to the students it
    /// was given to — and to their parents, since a task set for one student is something the family will want to know.</summary>
    private async Task<List<Guid>> AudienceAsync(Guid groupId, IReadOnlyCollection<Guid> targetIds)
    {
        if (targetIds.Count == 0) return await recipients.GroupStudentsAsync(groupId);

        var audience = new List<Guid>();
        foreach (var studentId in targetIds) audience.AddRange(await recipients.StudentAndParentsAsync(studentId));
        return audience.Distinct().ToList();
    }

    /// <summary>The teacher's file for the task. Whoever may see the homework — its group's staff, the group's students and their
    /// parents — may take it; nobody else.</summary>
    [HttpGet("{id:guid}/attachment")]
    [Authorize(Policy = "homework.view")]
    public async Task<IActionResult> DownloadAttachment(Guid id, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        var homework = await db.Homework.FirstOrDefaultAsync(h => h.Id == id && h.OrganizationId == scope.OrganizationId, ct);
        if (homework?.AttachmentKey is null) return NotFound();
        var lesson = await db.Lessons.FirstAsync(l => l.Id == homework.LessonId, ct);
        if (!await scope.CanAccessGroupAsync(db, lesson.GroupId, lesson.BranchId)) return Forbid();
        // An individual task's file is for the students it was given to (and their parents), not the whole group.
        if (scope.IsPersonalScope()
            && !await db.Homework.Where(h => h.Id == id).AssignedToAny(db, await scope.VisibleStudentIdsAsync(db)).AnyAsync(ct))
            return Forbid();

        var path = files.PathFor(homework.AttachmentKey);
        if (path is null) return NotFound(new { error = "Fayl saqlash joyida topilmadi." });
        return PhysicalFile(path, homework.AttachmentContentType ?? "application/octet-stream", homework.AttachmentName);
    }

    /// <summary>Fixes a title, description, type, deadline or maximum score. The maximum cannot drop below a score already given —
    /// that would leave a mark higher than the total. A moved deadline is announced to the group; the other fixes are not news.</summary>
    [HttpPatch("{id:guid}")]
    [Authorize(Policy = "homework.create")]
    public async Task<ActionResult<HomeworkDto>> Update(Guid id, UpdateHomeworkRequest request, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        var homework = await db.Homework.FirstOrDefaultAsync(h => h.Id == id && h.OrganizationId == scope.OrganizationId, ct);
        if (homework is null) return NotFound();
        var lesson = await db.Lessons.FirstAsync(l => l.Id == homework.LessonId, ct);
        if (!await scope.CanAccessGroupAsync(db, lesson.GroupId, lesson.BranchId)) return Forbid();

        if (request.Title is null && request.Description is null && request.Type is null && request.Deadline is null && request.MaxScore is null)
            return BadRequest(new { error = "O'zgartirish uchun sarlavha, tavsif, tur, muddat yoki maksimal ballni ko'rsating." });
        var title = request.Title?.Trim() ?? homework.Title;
        if (title.Length == 0 || title.Length > MaxTitleLength)
            return BadRequest(new { error = $"Sarlavha 1 dan {MaxTitleLength} tagacha belgi bo'lishi kerak." });
        var description = request.Description is null ? homework.Description
            : request.Description.Trim().Length == 0 ? null : request.Description.Trim();
        if (description?.Length > MaxDescriptionLength)
            return BadRequest(new { error = $"Tavsif {MaxDescriptionLength} belgidan oshmasin." });
        var type = request.Type ?? homework.Type;
        if (!Enum.IsDefined(type)) return BadRequest(new { error = "Homework turini to'g'ri kiriting." });
        var maxScore = request.MaxScore ?? homework.MaxScore;
        if (maxScore < 1) return BadRequest(new { error = "Maksimal ball kamida 1 bo'lishi kerak." });

        var highest = await db.HomeworkSubmissions.Where(s => s.HomeworkId == id).MaxAsync(s => s.Score, ct);
        if (highest is { } given && maxScore < given)
            return Conflict(new { error = $"Maksimal ball berilgan baholardan ({given}) past bo'lishi mumkin emas." });

        var deadline = request.Deadline ?? homework.Deadline;
        var deadlineChanged = deadline != homework.Deadline;
        homework.Update(title, description, type, deadline, maxScore);

        var targetIds = await db.HomeworkTargets.Where(t => t.HomeworkId == id).Select(t => t.StudentId).ToListAsync(ct);
        if (deadlineChanged)
        {
            var context = await (
                from g in db.Groups
                join b in db.Branches on g.BranchId equals b.Id
                where g.Id == lesson.GroupId
                select new { g.Name, b.Timezone }).FirstAsync(ct);
            await notifier.NotifyAsync(scope.OrganizationId, await AudienceAsync(lesson.GroupId, targetIds),
                new NotificationMessage(NotificationType.HomeworkChanged, $"Homework muddati o'zgardi: {homework.Title}",
                    $"{context.Name} · yangi muddat {BranchClock.LocalDateTime(context.Timezone, homework.Deadline):dd.MM HH:mm}.",
                    $"/schedule/{lesson.Id}"), ct);
        }

        await db.SaveChangesAsync(ct);
        return Ok(ToDto(homework, targetIds));
    }

    /// <summary>Erases a homework nothing was handed in for. A submission — or an AI writing evaluation of one — is the student's
    /// work, so a homework that has any stays; the refusal counts them.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "homework.create")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        var homework = await db.Homework.FirstOrDefaultAsync(h => h.Id == id && h.OrganizationId == scope.OrganizationId, ct);
        if (homework is null) return NotFound();
        var lesson = await db.Lessons.FirstAsync(l => l.Id == homework.LessonId, ct);
        if (!await scope.CanAccessGroupAsync(db, lesson.GroupId, lesson.BranchId)) return Forbid();

        var submissions = await db.HomeworkSubmissions.CountAsync(s => s.HomeworkId == id, ct);
        var evaluations = await db.WritingEvaluations.CountAsync(e => e.HomeworkId == id, ct);
        var blockers = new List<string>();
        if (submissions > 0) blockers.Add($"{submissions} ta topshiriq");
        if (evaluations > 0) blockers.Add($"{evaluations} ta AI writing tahlili");
        if (blockers.Count > 0)
            return Conflict(new
            {
                error = $"Bu homeworkni o'chirib bo'lmaydi — unga bog'liq ma'lumotlar bor: {string.Join(", ", blockers)}.",
                blockers,
                submissionCount = submissions,
            });

        var attachmentKey = homework.AttachmentKey;
        db.Homework.Remove(homework);
        db.HomeworkTargets.RemoveRange(await db.HomeworkTargets.Where(t => t.HomeworkId == id).ToListAsync(ct));
        await db.SaveChangesAsync(ct);
        files.Delete(attachmentKey);
        return NoContent();
    }

    [HttpPost("{id:guid}/submissions")]
    [Authorize(Policy = "homework.submit")]
    public Task<ActionResult<SubmissionDto>> Submit(Guid id, SubmitHomeworkRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.ContentText) && string.IsNullOrWhiteSpace(request.ContentUrl))
            return Task.FromResult<ActionResult<SubmissionDto>>(BadRequest(new { error = "Matn yoki fayl havolasi kiritilishi shart." }));
        return SubmitCoreAsync(id, request.ContentText, request.ContentUrl, null, ct);
    }

    /// <summary>Hands in a file — with the student's own words or without. The rules are the JSON endpoint's: an enrolled student, on an
    /// existing homework, once (a second answer is a 409 and the file that came with it is not kept), late allowed.</summary>
    [HttpPost("{id:guid}/submissions/upload")]
    [Authorize(Policy = "homework.submit")]
    [RequestSizeLimit(MaxUploadRequestBytes)]
    public Task<ActionResult<SubmissionDto>> SubmitFile(Guid id, [FromForm] SubmitHomeworkFileForm form, CancellationToken ct = default)
    {
        if (form.File is null)
            return Task.FromResult<ActionResult<SubmissionDto>>(BadRequest(new { error = "Fayl tanlanmagan." }));
        return SubmitCoreAsync(id, form.ContentText, null, form.File, ct);
    }

    private async Task<ActionResult<SubmissionDto>> SubmitCoreAsync(
        Guid id, string? contentText, string? rawContentUrl, IFormFile? file, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();

        // The teacher opens this link from their own signed-in session; a javascript: or data: URL would run there.
        var contentUrl = string.IsNullOrWhiteSpace(rawContentUrl) ? null : rawContentUrl.Trim();
        if (contentUrl is not null && !HttpUrlPolicy.IsValid(contentUrl))
            return BadRequest(new { error = "Yaroqli HTTP/HTTPS havola kiriting." });
        if (contentText?.Length > MaxSubmissionTextLength)
            return BadRequest(new { error = $"Javob matni {MaxSubmissionTextLength} belgidan oshmasin." });
        if (file is not null && await files.ValidateAsync(file, ct) is { } problem)
            return BadRequest(new { error = problem });

        var homework = await db.Homework.FirstOrDefaultAsync(h => h.Id == id && h.OrganizationId == scope.OrganizationId, ct);
        if (homework is null) return NotFound();

        var student = await db.Students.FirstOrDefaultAsync(s => s.UserId == scope.UserId, ct);
        if (student is null) return BadRequest(new { error = "Faqat student akkaunti homework topshira oladi." });

        // The student must be enrolled in the group this homework's lesson belongs to — otherwise any
        // student account could submit to any homework in the organization.
        var lesson = await db.Lessons.FirstAsync(l => l.Id == homework.LessonId, ct);
        var isEnrolled = await db.Enrollments.AnyAsync(e =>
            e.GroupId == lesson.GroupId && e.StudentId == student.Id && e.Status == EnrollmentStatus.Active, ct);
        if (!isEnrolled) return Forbid();

        // An individual task is only for the students it was given to: a classmate cannot hand it in.
        if (!await db.Homework.Where(h => h.Id == id).AssignedTo(db, student.Id).AnyAsync(ct)) return Forbid();

        if (await db.HomeworkSubmissions.AnyAsync(s => s.HomeworkId == id && s.StudentId == student.Id, ct))
            return Conflict(new { error = "Bu homework uchun allaqachon topshiriq yuborilgan." });

        var submission = new HomeworkSubmission(scope.OrganizationId, id, student.Id, contentUrl,
            string.IsNullOrWhiteSpace(contentText) ? null : contentText);
        db.HomeworkSubmissions.Add(submission);

        db.StudentTimelineEvents.Add(new StudentTimelineEvent(scope.OrganizationId, student.Id,
            TimelineEventType.HomeworkSubmitted,
            $$"""{"homeworkId":"{{id}}","title":"{{homework.Title.Replace("\"", "'")}}"}"""));

        // Only now, after every refusal above, is the file written — and it is taken back if the save fails, so a refused or failed
        // submission leaves nothing on the disk.
        string? storedKey = null;
        if (file is not null)
        {
            var saved = await files.SaveAsync(file, ct);
            storedKey = saved.Key;
            submission.AttachFile(saved.Key, saved.FileName, saved.ContentType, saved.Size);
        }
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            files.Delete(storedKey);
            throw;
        }

        // Writing homework also gets an AI assessment (docs/06_phase4_technical_plan.md Sprint 2) when the student may use
        // Writing AI and has allowance left; the submission itself never depends on it. An answer that is only a file has no text
        // to assess, and the evaluator leaves it alone.
        await writing.QueueForHomeworkAsync(scope.UserId, homework, submission, ct);

        return CreatedAtAction(nameof(List), ToDto(submission));
    }

    /// <summary>A student's answer file. A student takes their own, a parent their child's, and staff those of the groups in their
    /// scope — never another student's.</summary>
    [HttpGet("submissions/{submissionId:guid}/attachment")]
    [Authorize(Policy = "homework.view")]
    public async Task<IActionResult> DownloadSubmissionAttachment(Guid submissionId, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        var submission = await db.HomeworkSubmissions
            .FirstOrDefaultAsync(s => s.Id == submissionId && s.OrganizationId == scope.OrganizationId, ct);
        if (submission?.AttachmentKey is null) return NotFound();

        if (scope.IsPersonalScope())
        {
            if (!(await scope.VisibleStudentIdsAsync(db)).Contains(submission.StudentId)) return Forbid();
        }
        else
        {
            var homework = await db.Homework.FirstAsync(h => h.Id == submission.HomeworkId, ct);
            var lesson = await db.Lessons.FirstAsync(l => l.Id == homework.LessonId, ct);
            if (!await scope.CanAccessGroupAsync(db, lesson.GroupId, lesson.BranchId)) return Forbid();
        }

        var path = files.PathFor(submission.AttachmentKey);
        if (path is null) return NotFound(new { error = "Fayl saqlash joyida topilmadi." });
        return PhysicalFile(path, submission.AttachmentContentType ?? "application/octet-stream", submission.AttachmentName);
    }

    [HttpGet("{id:guid}/submissions")]
    [Authorize(Policy = "homework.review")]
    public async Task<ActionResult<List<SubmissionDto>>> ListSubmissions(Guid id)
    {
        var scope = HttpContext.RequireResolvedScope();
        var homework = await db.Homework.FirstOrDefaultAsync(h => h.Id == id && h.OrganizationId == scope.OrganizationId);
        if (homework is null) return NotFound();

        var lesson = await db.Lessons.FirstAsync(l => l.Id == homework.LessonId);
        if (!await scope.CanAccessGroupAsync(db, lesson.GroupId, lesson.BranchId))
            return Forbid();

        var submissions = await db.HomeworkSubmissions
            .Where(s => s.HomeworkId == id)
            .OrderBy(s => s.SubmittedAt)
            .ToListAsync();

        return Ok(submissions.Select(s => ToDto(s)).ToList());
    }

    /// <summary>Student/parent side of the homework loop: the submissions belonging to the caller's own
    /// student record (Self) or their children (MyChildren). Staff use ListSubmissions instead — this one
    /// never widens beyond the personal scope, so a teacher calling it gets 403 rather than an empty list.</summary>
    [HttpGet("{id:guid}/my-submissions")]
    [Authorize(Policy = "homework.view")]
    public async Task<ActionResult<List<SubmissionDto>>> MySubmissions(Guid id)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (!scope.IsPersonalScope()) return Forbid();

        if (!await db.Homework.AnyAsync(h => h.Id == id && h.OrganizationId == scope.OrganizationId))
            return NotFound();

        var visibleStudents = await scope.VisibleStudentIdsAsync(db);
        var submissions = await db.HomeworkSubmissions
            .Where(s => s.HomeworkId == id && visibleStudents.Contains(s.StudentId))
            .OrderBy(s => s.SubmittedAt)
            .ToListAsync();

        return Ok(submissions.Select(s => ToDto(s)).ToList());
    }

    [HttpPatch("submissions/{submissionId:guid}/review")]
    [Authorize(Policy = "homework.review")]
    public async Task<IActionResult> Review(Guid submissionId, ReviewSubmissionRequest request)
    {
        var scope = HttpContext.RequireResolvedScope();
        var submission = await db.HomeworkSubmissions
            .FirstOrDefaultAsync(s => s.Id == submissionId && s.OrganizationId == scope.OrganizationId);
        if (submission is null) return NotFound();

        var homework = await db.Homework.FirstAsync(h => h.Id == submission.HomeworkId);
        if (request.Score < 0 || request.Score > homework.MaxScore)
            return BadRequest(new { error = $"Ball 0–{homework.MaxScore} oralig'ida bo'lishi kerak." });

        var lesson = await db.Lessons.FirstAsync(l => l.Id == homework.LessonId);
        if (!await scope.CanAccessGroupAsync(db, lesson.GroupId, lesson.BranchId))
            return Forbid();

        submission.Review(request.Score, request.Feedback, scope.UserId);

        var reviewedName = await recipients.StudentNameAsync(submission.StudentId);
        await notifier.NotifyAsync(scope.OrganizationId, await recipients.StudentAndParentsAsync(submission.StudentId),
            new NotificationMessage(NotificationType.HomeworkReviewed, $"Homework baholandi: {homework.Title}",
                $"{reviewedName} — {request.Score}/{homework.MaxScore} ball" + (string.IsNullOrWhiteSpace(request.Feedback) ? "." : $". Izoh: {request.Feedback}"),
                $"/schedule/{lesson.Id}"));

        db.StudentTimelineEvents.Add(new StudentTimelineEvent(scope.OrganizationId, submission.StudentId,
            TimelineEventType.HomeworkReviewed, $$"""{"homeworkId":"{{homework.Id}}","score":{{request.Score}}}"""));

        await db.SaveChangesAsync();
        return NoContent();
    }

    private static HomeworkFileDto? FileOf(string? key, string? name, string? contentType, long? size) =>
        key is null ? null : new HomeworkFileDto(name!, contentType!, size!.Value);

    /// <summary>For the responses of create and edit, whose caller holds <c>homework.create</c> and so may be told who an individual task is for.</summary>
    private static HomeworkDto ToDto(Homework h, List<Guid> targetIds) => new(h.Id, h.LessonId, h.Title, h.Description, h.Type, h.Deadline, h.MaxScore,
        FileOf(h.AttachmentKey, h.AttachmentName, h.AttachmentContentType, h.AttachmentSize),
        targetIds.Count > 0, targetIds.Count > 0 ? targetIds : null);

    private static SubmissionDto ToDto(HomeworkSubmission s) => new(s.Id, s.HomeworkId, s.StudentId, s.ContentUrl, s.ContentText,
        s.SubmittedAt, s.Score, s.Feedback, s.ReviewedAt, FileOf(s.AttachmentKey, s.AttachmentName, s.AttachmentContentType, s.AttachmentSize));
}
