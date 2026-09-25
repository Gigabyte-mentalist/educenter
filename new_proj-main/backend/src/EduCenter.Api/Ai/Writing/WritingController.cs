using System.Security.Claims;
using System.Text.Json;
using EduCenter.Api.Authorization;
using EduCenter.Api.Notifications;
using EduCenter.Infrastructure;
using EduCenter.Modules.Ai.Domain;
using EduCenter.Modules.Audit.Domain;
using EduCenter.Modules.Notifications.Domain;
using EduCenter.Modules.Timeline.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Ai.Writing;

public record CreateWritingEvaluationRequest(WritingTaskType TaskType, string? TaskPrompt, string? Essay);

public record OverrideWritingEvaluationRequest(
    decimal Band, decimal? TaskResponse, decimal? CoherenceCohesion, decimal? LexicalResource, decimal? GrammaticalRange, string? Comment);

public record WritingEvaluationSummaryDto(
    Guid Id, Guid StudentId, string StudentName, WritingTaskType TaskType, string TaskTitle, int WordCount,
    WritingEvaluationStatus Status, decimal? EstimatedBand, decimal? TeacherBand, Guid? HomeworkId, Guid? HomeworkSubmissionId,
    DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt);

public record WritingCriterionDto(string Key, string Label, decimal? AiBand, decimal? TeacherBand, string? Comment);

public record WritingEvaluationDto(
    WritingEvaluationSummaryDto Summary, string TaskPrompt, string Essay, string? Error, string? Model,
    List<WritingCriterionDto> Criteria, List<string> Strengths, List<string> Weaknesses,
    List<WritingIssue> GrammarIssues, List<WritingIssue> VocabularyIssues, List<string> Suggestions,
    string? TeacherComment, string? OverriddenByName, DateTimeOffset? OverriddenAt, bool CanOverride, bool CanRetry);

/// <summary>
/// Writing AI (docs/06_phase4_technical_plan.md Sprint 2). A student sends essays and reads their own assessments
/// (ai.writing.use); teaching staff read the assessments of students in their scope (ai.writing.review) and may override
/// the estimated band (ai.writing.override). Evaluations outside the caller's reach answer 404.
/// </summary>
[ApiController]
[Route("api/ai/writing")]
public class WritingController(AppDbContext db, WritingEvaluator evaluator, PermissionResolver permissions, NotificationDispatcher notifier)
    : ControllerBase
{
    private const string ReviewPermission = "ai.writing.review";
    private const string OverridePermission = "ai.writing.override";

    [HttpPost("evaluations")]
    [Authorize(Policy = "ai.writing.use")]
    [EnableRateLimiting(AiGateway.RateLimitPolicy)]
    public async Task<ActionResult<WritingEvaluationDto>> Create(CreateWritingEvaluationRequest request, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        var result = await evaluator.QueueAsync(scope.UserId, request.TaskType, request.TaskPrompt, request.Essay, ct: ct);
        if (result.Outcome != WritingQueueOutcome.Queued) return QueueError(result);

        return CreatedAtAction(nameof(Get), new { id = result.Evaluation!.Id }, await BuildDetailAsync(result.Evaluation.Id, scope.UserId, ct));
    }

    /// <summary>The caller's own assessments, newest first; <paramref name="homeworkId"/> narrows to one homework.</summary>
    [HttpGet("evaluations")]
    [Authorize(Policy = "ai.writing.use")]
    public async Task<ActionResult<List<WritingEvaluationSummaryDto>>> Mine([FromQuery] Guid? homeworkId, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        var studentId = await db.Students.Where(s => s.UserId == scope.UserId).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
        if (studentId is null) return Ok(new List<WritingEvaluationSummaryDto>());

        var query = db.WritingEvaluations.Where(e => e.StudentId == studentId);
        if (homeworkId is not null) query = query.Where(e => e.HomeworkId == homeworkId);
        return Ok(await SummariesAsync(query, ct));
    }

    /// <summary>Assessments a teacher may review: with no filter, everything from the students in their scope;
    /// with one, narrowed to that student or that homework.</summary>
    [HttpGet("reviews")]
    [Authorize(Policy = ReviewPermission)]
    public async Task<ActionResult<List<WritingEvaluationSummaryDto>>> Reviews(
        [FromQuery] Guid? studentId, [FromQuery] Guid? homeworkId,
        [FromQuery] WritingEvaluationStatus? status, [FromQuery] bool pendingOnly = false,
        CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        var query = db.WritingEvaluations.Where(e => e.OrganizationId == scope.OrganizationId);

        // With neither filter, this is the teacher's inbox: every assessment from the students their scope
        // covers. It used to be a 400, which is why the review screen was never built — there was no request
        // a teacher could make without already knowing which student to ask about.
        if (studentId is null && homeworkId is null)
        {
            var mine = scope.ScopedStudentIds(db);
            if (mine is null) return Forbid();
            query = query.Where(e => mine.Contains(e.StudentId));
            if (status is not null) query = query.Where(e => e.Status == status);
            if (pendingOnly) query = query.Where(e => e.TeacherBand == null);
            return Ok(await SummariesAsync(query, ct));
        }

        if (studentId is not null)
        {
            if (!await scope.CanAccessStudentAsync(db, studentId.Value)) return Forbid();
            query = query.Where(e => e.StudentId == studentId);
        }

        if (homeworkId is not null)
        {
            var lesson = await (
                from h in db.Homework
                join l in db.Lessons on h.LessonId equals l.Id
                where h.Id == homeworkId && h.OrganizationId == scope.OrganizationId
                select new { l.GroupId, l.BranchId }
            ).FirstOrDefaultAsync(ct);
            if (lesson is null) return NotFound();
            if (!await scope.CanAccessGroupAsync(db, lesson.GroupId, lesson.BranchId)) return Forbid();
            query = query.Where(e => e.HomeworkId == homeworkId);
        }

        return Ok(await SummariesAsync(query, ct));
    }

    [HttpGet("evaluations/{id:guid}")]
    [Authorize]
    public async Task<ActionResult<WritingEvaluationDto>> Get(Guid id, CancellationToken ct)
    {
        if (CurrentUserId is not { } userId) return Unauthorized();

        var evaluation = await db.WritingEvaluations.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        if (evaluation is null || !await CanReadAsync(evaluation, userId, ct)) return NotFound();

        return Ok(await BuildDetailAsync(id, userId, ct));
    }

    [HttpPost("evaluations/{id:guid}/retry")]
    [Authorize(Policy = "ai.writing.use")]
    [EnableRateLimiting(AiGateway.RateLimitPolicy)]
    public async Task<ActionResult<WritingEvaluationDto>> Retry(Guid id, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        var evaluation = await db.WritingEvaluations.FirstOrDefaultAsync(e => e.Id == id && e.RequestedBy == scope.UserId, ct);
        if (evaluation is null) return NotFound();
        if (!WritingEvaluator.CanRetry(evaluation, DateTimeOffset.UtcNow))
            return Conflict(new { error = "Faqat bajarilmay qolgan baholashni qayta yuborish mumkin." });

        var result = await evaluator.RetryAsync(evaluation, ct);
        if (result.Outcome != WritingQueueOutcome.Queued) return QueueError(result);

        return Ok(await BuildDetailAsync(id, scope.UserId, ct));
    }

    /// <summary>A teacher's own band (and optionally criterion bands) next to the AI result, which stays unchanged.</summary>
    [HttpPost("evaluations/{id:guid}/override")]
    [Authorize(Policy = OverridePermission)]
    public async Task<ActionResult<WritingEvaluationDto>> Override(Guid id, OverrideWritingEvaluationRequest request, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        var evaluation = await db.WritingEvaluations.FirstOrDefaultAsync(e => e.Id == id && e.OrganizationId == scope.OrganizationId, ct);
        if (evaluation is null) return NotFound();
        if (!await scope.CanAccessStudentAsync(db, evaluation.StudentId)) return Forbid();
        if (evaluation.Status != WritingEvaluationStatus.Completed)
            return Conflict(new { error = "Faqat yakunlangan baholashni tuzatish mumkin." });

        decimal?[] bands = [request.Band, request.TaskResponse, request.CoherenceCohesion, request.LexicalResource, request.GrammaticalRange];
        if (bands.Any(b => b is not null && !WritingRubric.IsHalfBand(b.Value)))
            return BadRequest(new { error = "Band 0 dan 9 gacha, 0.5 qadam bilan bo'lishi kerak." });
        var comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();
        if (comment?.Length > WritingEvaluation.MaxCommentLength)
            return BadRequest(new { error = $"Izoh {WritingEvaluation.MaxCommentLength} belgidan oshmasligi kerak." });

        var before = OverrideSnapshot(evaluation);
        evaluation.Override(request.Band, request.TaskResponse, request.CoherenceCohesion, request.LexicalResource, request.GrammaticalRange,
            comment, scope.UserId);

        // Audited by hand with the band fields only: the entity also holds the student's essay, which does not belong in the audit log.
        db.AuditLogs.Add(new AuditLog(scope.OrganizationId, scope.UserId, "Modified", nameof(WritingEvaluation), evaluation.Id,
            before, OverrideSnapshot(evaluation), "AI writing override"));

        var band = request.Band.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        db.StudentTimelineEvents.Add(new StudentTimelineEvent(scope.OrganizationId, evaluation.StudentId, TimelineEventType.WritingOverridden,
            $$"""{"evaluationId":"{{evaluation.Id}}","teacherBand":{{band}}}"""));
        await notifier.NotifyAsync(scope.OrganizationId, [evaluation.RequestedBy],
            new NotificationMessage(NotificationType.WritingEvaluated, "O'qituvchi writing bahosini ko'rib chiqdi",
                $"O'qituvchi bahosi: {band}.", $"/ai/writing?evaluation={evaluation.Id}"), ct);

        await db.SaveChangesAsync(ct);
        return Ok(await BuildDetailAsync(id, scope.UserId, ct));
    }

    private ActionResult QueueError(WritingQueueResult result) => result.Outcome switch
    {
        WritingQueueOutcome.LimitExceeded => StatusCode(StatusCodes.Status429TooManyRequests, new { error = result.Error }),
        WritingQueueOutcome.Unavailable => StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = result.Error }),
        _ => BadRequest(new { error = result.Error })
    };

    private async Task<bool> CanReadAsync(WritingEvaluation evaluation, Guid userId, CancellationToken ct)
    {
        if (evaluation.RequestedBy == userId || await db.Students.AnyAsync(s => s.Id == evaluation.StudentId && s.UserId == userId, ct))
            return true;

        var review = await permissions.ResolveAsync(userId, ReviewPermission, ct);
        return review is not null && review.OrganizationId == evaluation.OrganizationId
               && await review.CanAccessStudentAsync(db, evaluation.StudentId);
    }

    private async Task<List<WritingEvaluationSummaryDto>> SummariesAsync(IQueryable<WritingEvaluation> query, CancellationToken ct)
    {
        var rows = await (
            from e in query
            join s in db.Students on e.StudentId equals s.Id
            join u in db.Users on s.UserId equals u.Id
            orderby e.CreatedAt descending
            select new { Evaluation = e, u.FullName }
        ).AsNoTracking().Take(100).ToListAsync(ct);

        return rows.Select(r => Summary(r.Evaluation, r.FullName)).ToList();
    }

    private async Task<WritingEvaluationDto> BuildDetailAsync(Guid id, Guid viewerId, CancellationToken ct)
    {
        var row = await (
            from e in db.WritingEvaluations
            join s in db.Students on e.StudentId equals s.Id
            join u in db.Users on s.UserId equals u.Id
            where e.Id == id
            select new { Evaluation = e, u.FullName }
        ).AsNoTracking().FirstAsync(ct);
        var evaluation = row.Evaluation;

        var overriddenByName = evaluation.OverriddenBy is null
            ? null
            : await db.Users.Where(u => u.Id == evaluation.OverriddenBy).Select(u => u.FullName).FirstOrDefaultAsync(ct);

        var canOverride = false;
        if (evaluation.Status == WritingEvaluationStatus.Completed)
        {
            var overrideScope = await permissions.ResolveAsync(viewerId, OverridePermission, ct);
            canOverride = overrideScope is not null && overrideScope.OrganizationId == evaluation.OrganizationId
                          && await overrideScope.CanAccessStudentAsync(db, evaluation.StudentId);
        }

        var feedback = evaluation.FeedbackJson is null
            ? null
            : JsonSerializer.Deserialize<WritingFeedback>(evaluation.FeedbackJson, WritingRubric.FeedbackJsonOptions);

        var type = evaluation.TaskType;
        List<WritingCriterionDto> criteria =
        [
            new("taskResponse", WritingRubric.CriterionLabel("taskResponse", type), evaluation.TaskResponseBand, evaluation.TeacherTaskResponseBand, feedback?.TaskResponseComment),
            new("coherenceCohesion", WritingRubric.CriterionLabel("coherenceCohesion", type), evaluation.CoherenceBand, evaluation.TeacherCoherenceBand, feedback?.CoherenceComment),
            new("lexicalResource", WritingRubric.CriterionLabel("lexicalResource", type), evaluation.LexicalBand, evaluation.TeacherLexicalBand, feedback?.LexicalComment),
            new("grammaticalRange", WritingRubric.CriterionLabel("grammaticalRange", type), evaluation.GrammarBand, evaluation.TeacherGrammarBand, feedback?.GrammarComment)
        ];

        return new WritingEvaluationDto(
            Summary(evaluation, row.FullName), evaluation.TaskPrompt, evaluation.Essay, evaluation.Error, evaluation.Model, criteria,
            feedback?.Strengths ?? [], feedback?.Weaknesses ?? [], feedback?.GrammarIssues ?? [], feedback?.VocabularyIssues ?? [],
            feedback?.Suggestions ?? [], evaluation.TeacherComment, overriddenByName, evaluation.OverriddenAt, canOverride,
            evaluation.RequestedBy == viewerId && WritingEvaluator.CanRetry(evaluation, DateTimeOffset.UtcNow));
    }

    private static WritingEvaluationSummaryDto Summary(WritingEvaluation e, string studentName)
    {
        var firstLine = e.TaskPrompt.Split('\n', 2)[0].Trim();
        return new WritingEvaluationSummaryDto(e.Id, e.StudentId, studentName, e.TaskType,
            firstLine.Length <= 120 ? firstLine : firstLine[..117] + "...", e.WordCount, e.Status, e.EstimatedBand, e.TeacherBand,
            e.HomeworkId, e.HomeworkSubmissionId, e.CreatedAt, e.CompletedAt);
    }

    private static string OverrideSnapshot(WritingEvaluation e) => JsonSerializer.Serialize(new
    {
        e.EstimatedBand, e.TeacherBand, e.TeacherTaskResponseBand, e.TeacherCoherenceBand, e.TeacherLexicalBand, e.TeacherGrammarBand,
        e.TeacherComment, e.OverriddenBy
    });

    private Guid? CurrentUserId => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
