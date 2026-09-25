using System.Globalization;
using System.Text.Json;
using EduCenter.Api.Ai.Providers;
using EduCenter.Api.Authorization;
using EduCenter.Api.Automation;
using EduCenter.Api.Notifications;
using EduCenter.Infrastructure;
using EduCenter.Modules.Ai.Domain;
using EduCenter.Modules.Lms.Domain;
using EduCenter.Modules.Notifications.Domain;
using EduCenter.Modules.Timeline.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Ai.Writing;

public enum WritingQueueOutcome
{
    Queued,
    Invalid,
    NotAStudent,
    LimitExceeded,
    Unavailable
}

public sealed record WritingQueueResult(WritingQueueOutcome Outcome, WritingEvaluation? Evaluation, string? Error);

/// <summary>
/// Writing AI workflow: accept an essay (practice or a Writing homework submission), queue the assessment, and run it
/// through the AI Gateway in the background — completing the evaluation, or recording why it failed or was rejected.
/// </summary>
public class WritingEvaluator(
    AppDbContext db, AiGateway gateway, AiLimits limits, AiCallContextFactory contexts, AiProviderRegistry providers,
    PermissionResolver permissions, IAiJobQueue queue, NotificationDispatcher notifier, ILogger<WritingEvaluator> logger)
{
    /// <summary>An evaluation stuck this long in Queued/Processing (e.g. the worker restarted) may be retried.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

    public static bool CanRetry(WritingEvaluation evaluation, DateTimeOffset now) =>
        evaluation.Status == WritingEvaluationStatus.Failed
        || (evaluation.Status is WritingEvaluationStatus.Queued or WritingEvaluationStatus.Processing
            && (evaluation.UpdatedAt ?? evaluation.CreatedAt) < now - StaleAfter);

    public async Task<WritingQueueResult> QueueAsync(Guid userId, WritingTaskType taskType, string? taskPrompt, string? essay,
        Guid? homeworkId = null, Guid? homeworkSubmissionId = null, CancellationToken ct = default)
    {
        var context = await contexts.ForUserAsync(userId, AiFeature.Writing, ct);
        if (context?.StudentId is null)
            return new WritingQueueResult(WritingQueueOutcome.NotAStudent, null, "Faqat student akkaunti writing yubora oladi.");

        var problem = WritingRubric.ValidateSubmission(taskPrompt, essay);
        if (problem is not null) return new WritingQueueResult(WritingQueueOutcome.Invalid, null, problem);

        var blocked = await CheckCapacityAsync(context, excludeEvaluationId: null, ct);
        if (blocked is not null) return blocked;

        var evaluation = new WritingEvaluation(context.OrganizationId, context.StudentId.Value, userId, taskType, taskPrompt!.Trim(),
            essay!.Trim(), WritingRubric.CountWords(essay), WritingRubric.PromptVersion(taskType), homeworkId, homeworkSubmissionId);
        db.WritingEvaluations.Add(evaluation);
        await db.SaveChangesAsync(ct);

        await queue.EnqueueWritingEvaluationAsync(evaluation.Id);
        return new WritingQueueResult(WritingQueueOutcome.Queued, evaluation, null);
    }

    public async Task<WritingQueueResult> RetryAsync(WritingEvaluation evaluation, CancellationToken ct = default)
    {
        var context = await contexts.ForUserAsync(evaluation.RequestedBy, AiFeature.Writing, ct);
        if (context?.StudentId is null)
            return new WritingQueueResult(WritingQueueOutcome.NotAStudent, null, "Faqat student akkaunti writing yubora oladi.");

        var blocked = await CheckCapacityAsync(context, evaluation.Id, ct);
        if (blocked is not null) return blocked;

        evaluation.Requeue();
        await db.SaveChangesAsync(ct);

        await queue.EnqueueWritingEvaluationAsync(evaluation.Id);
        return new WritingQueueResult(WritingQueueOutcome.Queued, evaluation, null);
    }

    /// <summary>A submitted Writing homework is assessed automatically when the student may use Writing AI and has
    /// allowance left; otherwise nothing happens — the teacher reviews it as before.</summary>
    public async Task QueueForHomeworkAsync(Guid userId, Homework homework, HomeworkSubmission submission, CancellationToken ct = default)
    {
        if (homework.Type != HomeworkType.Writing || string.IsNullOrWhiteSpace(submission.ContentText)) return;
        if (await permissions.ResolveAsync(userId, "ai.writing.use", ct) is null) return;

        var levelCategory = await (
            from l in db.Lessons
            join g in db.Groups on l.GroupId equals g.Id
            join c in db.Courses on g.CourseId equals c.Id
            where l.Id == homework.LessonId
            select c.LevelCategory
        ).FirstOrDefaultAsync(ct);

        var taskPrompt = string.IsNullOrWhiteSpace(homework.Description) ? homework.Title : $"{homework.Title}\n\n{homework.Description}";
        if (taskPrompt.Length > WritingEvaluation.MaxTaskPromptLength) taskPrompt = taskPrompt[..WritingEvaluation.MaxTaskPromptLength];

        var result = await QueueAsync(userId, WritingRubric.InferTaskType(homework.Title, homework.Description, levelCategory),
            taskPrompt, submission.ContentText, homework.Id, submission.Id, ct);
        if (result.Outcome != WritingQueueOutcome.Queued)
            logger.LogInformation("Writing homework submission {SubmissionId} was not sent to AI: {Outcome}", submission.Id, result.Outcome);
    }

    /// <summary>The background job. Idempotent: only a Queued evaluation is picked up.</summary>
    public async Task RunAsync(Guid evaluationId, CancellationToken ct = default)
    {
        var evaluation = await db.WritingEvaluations.FirstOrDefaultAsync(e => e.Id == evaluationId, ct);
        if (evaluation is null || evaluation.Status != WritingEvaluationStatus.Queued) return;

        evaluation.StartProcessing();
        await db.SaveChangesAsync(ct);

        try
        {
            var context = await contexts.ForUserAsync(evaluation.RequestedBy, AiFeature.Writing, ct);
            if (context is null)
            {
                evaluation.Fail("Foydalanuvchi topilmadi.", null);
                await db.SaveChangesAsync(ct);
                return;
            }

            var result = await gateway.CompleteJsonAsync<WritingModelOutput>(context, AiTaskKind.WritingEvaluation,
                WritingRubric.BuildPrompt(evaluation), WritingRubric.Validate, ct);

            switch (result.Status)
            {
                case AiResultStatus.Succeeded:
                    await CompleteAsync(evaluation, result, ct);
                    break;
                case AiResultStatus.Rejected:
                    evaluation.Reject(result.Error ?? "So'rov xavfsizlik tekshiruvidan o'tmadi.", result.RequestId);
                    break;
                default:
                    evaluation.Fail(result.Error ?? "AI baholay olmadi.", result.RequestId);
                    break;
            }

            await db.SaveChangesAsync(ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Writing evaluation {EvaluationId} failed", evaluationId);
            db.ChangeTracker.Clear();
            var stored = await db.WritingEvaluations.FirstAsync(x => x.Id == evaluationId, CancellationToken.None);
            stored.Fail("Baholashda kutilmagan xatolik yuz berdi. Qayta urinib ko'ring.", null);
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    private async Task CompleteAsync(WritingEvaluation evaluation, AiResult<WritingModelOutput> result, CancellationToken ct)
    {
        var score = WritingRubric.Score(result.Value!, evaluation.Essay);
        evaluation.Complete(score.TaskResponse, score.Coherence, score.Lexical, score.Grammar, score.Overall,
            JsonSerializer.Serialize(score.Feedback, WritingRubric.FeedbackJsonOptions), result.RequestId, result.Model);

        var band = score.Overall.ToString("0.0", CultureInfo.InvariantCulture);
        db.StudentTimelineEvents.Add(new StudentTimelineEvent(evaluation.OrganizationId, evaluation.StudentId,
            TimelineEventType.WritingEvaluated, $$"""{"evaluationId":"{{evaluation.Id}}","estimatedBand":{{band}}}"""));

        await notifier.NotifyAsync(evaluation.OrganizationId, [evaluation.RequestedBy],
            new NotificationMessage(NotificationType.WritingEvaluated, "AI writing tahlili tayyor",
                $"Taxminiy band: {band}. Mezonlar bo'yicha izohlar va tavsiyalarni ko'ring.", $"/ai/writing?evaluation={evaluation.Id}"), ct);
    }

    /// <summary>Daily allowance counts finished assessments (the gateway's journal) plus ones still waiting in the queue,
    /// so a student cannot queue past the limit before any of them has run.</summary>
    private async Task<WritingQueueResult?> CheckCapacityAsync(AiCallContext context, Guid? excludeEvaluationId, CancellationToken ct)
    {
        if (providers.Route(AiTaskKind.WritingEvaluation).Count == 0)
            return new WritingQueueResult(WritingQueueOutcome.Unavailable, null, "AI xizmati hozircha sozlanmagan.");

        var limit = await limits.CheckAsync(context, ct);
        if (limit is null) return null;

        var since = BranchClock.StartOfLocalDay(context.BranchTimezone, DateTimeOffset.UtcNow);
        var pending = await db.WritingEvaluations.CountAsync(e =>
            e.RequestedBy == context.UserId && e.Id != excludeEvaluationId && e.CreatedAt >= since
            && (e.Status == WritingEvaluationStatus.Queued || e.Status == WritingEvaluationStatus.Processing), ct);

        return limit.Used + pending >= limit.Limit
            ? new WritingQueueResult(WritingQueueOutcome.LimitExceeded, null,
                $"Bugungi writing limiti tugadi ({limit.Limit} ta). Ertaga yana yuborishingiz mumkin.")
            : null;
    }
}
