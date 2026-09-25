using EduCenter.SharedKernel;

namespace EduCenter.Modules.Ai.Domain;

public enum WritingTaskType
{
    IeltsTask1,
    IeltsTask2,

    /// <summary>A writing task outside IELTS; scored on the same four criteria as IELTS-equivalent bands.</summary>
    General
}

public enum WritingEvaluationStatus
{
    Queued,
    Processing,
    Completed,

    /// <summary>No valid answer (provider outage, invalid output, limit reached at run time) — the student may retry.</summary>
    Failed,

    /// <summary>Stopped by the gateway's security layers (e.g. prompt injection) — not retried.</summary>
    Rejected
}

/// <summary>
/// One Writing AI assessment (docs/00_TZ.md §33): the task and essay, the model's criterion bands with feedback, and a
/// teacher's override kept alongside — never in place of — the AI result, so benchmarks can compare the two (§55).
/// The overall band is an <b>estimated</b> IELTS band, not an official score; nothing here changes homework or exam grades.
/// </summary>
public class WritingEvaluation : TenantEntity
{
    public const int MaxTaskPromptLength = 2000;
    public const int MaxEssayLength = 9000;
    public const int MaxErrorLength = 500;
    public const int MaxCommentLength = 2000;

    private WritingEvaluation() { } // EF Core

    public WritingEvaluation(Guid organizationId, Guid studentId, Guid requestedBy, WritingTaskType taskType, string taskPrompt,
        string essay, int wordCount, string promptVersion, Guid? homeworkId = null, Guid? homeworkSubmissionId = null)
    {
        OrganizationId = organizationId;
        StudentId = studentId;
        RequestedBy = requestedBy;
        TaskType = taskType;
        TaskPrompt = taskPrompt;
        Essay = essay;
        WordCount = wordCount;
        PromptVersion = promptVersion;
        HomeworkId = homeworkId;
        HomeworkSubmissionId = homeworkSubmissionId;
        Status = WritingEvaluationStatus.Queued;
    }

    public Guid StudentId { get; private set; }

    /// <summary>The user whose daily Writing allowance the assessment spends (the student's own account).</summary>
    public Guid RequestedBy { get; private set; }

    public Guid? HomeworkId { get; private set; }
    public Guid? HomeworkSubmissionId { get; private set; }
    public WritingTaskType TaskType { get; private set; }
    public string TaskPrompt { get; private set; } = default!;
    public string Essay { get; private set; } = default!;
    public int WordCount { get; private set; }
    public string PromptVersion { get; private set; } = default!;
    public WritingEvaluationStatus Status { get; private set; }
    public string? Error { get; private set; }
    public Guid? AiRequestId { get; private set; }
    public string? Model { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public decimal? EstimatedBand { get; private set; }
    public decimal? TaskResponseBand { get; private set; }
    public decimal? CoherenceBand { get; private set; }
    public decimal? LexicalBand { get; private set; }
    public decimal? GrammarBand { get; private set; }

    /// <summary>Comments, strengths, weaknesses, grammar/vocabulary issues and suggestions, as JSON.</summary>
    public string? FeedbackJson { get; private set; }

    public decimal? TeacherBand { get; private set; }
    public decimal? TeacherTaskResponseBand { get; private set; }
    public decimal? TeacherCoherenceBand { get; private set; }
    public decimal? TeacherLexicalBand { get; private set; }
    public decimal? TeacherGrammarBand { get; private set; }
    public string? TeacherComment { get; private set; }
    public Guid? OverriddenBy { get; private set; }
    public DateTimeOffset? OverriddenAt { get; private set; }

    public bool IsOverridden => OverriddenAt is not null;

    public void StartProcessing()
    {
        Status = WritingEvaluationStatus.Processing;
        Error = null;
        MarkUpdated();
    }

    public void Complete(decimal taskResponse, decimal coherence, decimal lexical, decimal grammar, decimal overall,
        string feedbackJson, Guid aiRequestId, string? model)
    {
        Status = WritingEvaluationStatus.Completed;
        Error = null;
        TaskResponseBand = taskResponse;
        CoherenceBand = coherence;
        LexicalBand = lexical;
        GrammarBand = grammar;
        EstimatedBand = overall;
        FeedbackJson = feedbackJson;
        AiRequestId = aiRequestId;
        Model = model;
        CompletedAt = DateTimeOffset.UtcNow;
        MarkUpdated();
    }

    public void Fail(string error, Guid? aiRequestId) => Stop(WritingEvaluationStatus.Failed, error, aiRequestId);

    public void Reject(string error, Guid? aiRequestId) => Stop(WritingEvaluationStatus.Rejected, error, aiRequestId);

    public void Requeue()
    {
        Status = WritingEvaluationStatus.Queued;
        Error = null;
        MarkUpdated();
    }

    public void Override(decimal band, decimal? taskResponse, decimal? coherence, decimal? lexical, decimal? grammar,
        string? comment, Guid teacherId)
    {
        TeacherBand = band;
        TeacherTaskResponseBand = taskResponse;
        TeacherCoherenceBand = coherence;
        TeacherLexicalBand = lexical;
        TeacherGrammarBand = grammar;
        TeacherComment = comment;
        OverriddenBy = teacherId;
        OverriddenAt = DateTimeOffset.UtcNow;
        MarkUpdated();
    }

    private void Stop(WritingEvaluationStatus status, string error, Guid? aiRequestId)
    {
        Status = status;
        Error = error.Length <= MaxErrorLength ? error : error[..MaxErrorLength];
        AiRequestId = aiRequestId;
        MarkUpdated();
    }
}
