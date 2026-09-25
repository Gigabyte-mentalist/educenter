using EduCenter.SharedKernel;

namespace EduCenter.Modules.Ai.Domain;

/// <summary>The student-facing AI features (docs/00_TZ.md §28); daily limits are counted per feature (§54).</summary>
public enum AiFeature
{
    Tutor,
    Writing,
    Speaking,

    /// <summary>Staff/system work that no student limit applies to (provider checks, benchmarks).</summary>
    System
}

/// <summary>What a request asks a model to do — the AI Router picks the provider/model chain by this (docs/00_TZ.md §30).</summary>
public enum AiTaskKind
{
    TutorAnswer,
    WritingEvaluation,
    TopicClassification,
    InjectionCheck,
    Embedding,
    SpeakingReply,
    Transcription,
    SpeechSynthesis,
    ProviderCheck
}

public enum AiRequestStatus
{
    Succeeded,

    /// <summary>Every provider in the route failed, or none returned a valid answer.</summary>
    Failed,

    /// <summary>Stopped by the gateway before any provider was called (invalid input, prompt injection, limit).</summary>
    Rejected
}

/// <summary>Why a request was rejected or failed — stable codes for monitoring (docs/00_TZ.md §54 "failed requests").</summary>
public static class AiOutcomeReason
{
    public const string InvalidInput = "INVALID_INPUT";
    public const string PromptInjection = "PROMPT_INJECTION";
    public const string LimitExceeded = "LIMIT_EXCEEDED";
    public const string NoProvider = "NO_PROVIDER";
    public const string ProviderError = "PROVIDER_ERROR";
    public const string InvalidOutput = "INVALID_OUTPUT";
}

/// <summary>
/// One call into the AI Gateway on behalf of a user — the unit daily limits and cost reports count. Only metadata is
/// stored: prompts and answers are not logged here (they can carry student PII); features keep what they need in their
/// own tables. Each provider attempt behind the request is an <see cref="AiUsage"/> row.
/// </summary>
public class AiRequest : TenantEntity
{
    public const int MaxDetailLength = 500;

    private AiRequest() { } // EF Core

    public AiRequest(Guid organizationId, Guid? branchId, Guid userId, Guid? studentId, AiFeature feature, AiTaskKind taskKind,
        string promptVersion)
    {
        OrganizationId = organizationId;
        BranchId = branchId;
        UserId = userId;
        StudentId = studentId;
        Feature = feature;
        TaskKind = taskKind;
        PromptVersion = promptVersion;
        Status = AiRequestStatus.Failed;
    }

    public Guid? BranchId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid? StudentId { get; private set; }
    public AiFeature Feature { get; private set; }
    public AiTaskKind TaskKind { get; private set; }
    public string PromptVersion { get; private set; } = default!;
    public AiRequestStatus Status { get; private set; }
    public string? Reason { get; private set; }
    public string? Detail { get; private set; }

    /// <summary>Provider and model that produced the accepted answer.</summary>
    public string? Provider { get; private set; }
    public string? Model { get; private set; }

    public int InputTokens { get; private set; }
    public int OutputTokens { get; private set; }
    public decimal CostUsd { get; private set; }
    public int LatencyMs { get; private set; }

    /// <summary>Speaking minutes are limited and reported in seconds of student audio (docs/00_TZ.md §54).</summary>
    public int AudioSeconds { get; private set; }

    /// <summary>What a speech-synthesis request is counted in: characters read aloud, not the size of the audio that came back
    /// (providers price by the characters they are given). Not tokens — a model call never sets it, and it is kept out of
    /// <see cref="InputTokens"/> so the token totals stay about models.</summary>
    public int Characters { get; private set; }

    public void AddAttempt(AiUsage usage)
    {
        InputTokens += usage.InputTokens;
        OutputTokens += usage.OutputTokens;
        CostUsd += usage.CostUsd;
    }

    public void Succeed(string provider, string model, int latencyMs, int audioSeconds = 0, int characters = 0)
    {
        Status = AiRequestStatus.Succeeded;
        Reason = null;
        Detail = null;
        Provider = provider;
        Model = model;
        LatencyMs = latencyMs;
        AudioSeconds = audioSeconds;
        Characters = characters;
    }

    public void Fail(string reason, string? detail, int latencyMs)
    {
        Status = AiRequestStatus.Failed;
        Reason = reason;
        Detail = Truncate(detail);
        LatencyMs = latencyMs;
    }

    public void Reject(string reason, string? detail)
    {
        Status = AiRequestStatus.Rejected;
        Reason = reason;
        Detail = Truncate(detail);
    }

    private static string? Truncate(string? value) =>
        value is null || value.Length <= MaxDetailLength ? value : value[..MaxDetailLength];
}

/// <summary>One provider call behind an <see cref="AiRequest"/> — fallbacks and output-validation retries each add a row,
/// so provider reliability, latency and cost can be compared (docs/00_TZ.md §54).</summary>
public class AiUsage : TenantEntity
{
    private AiUsage() { } // EF Core

    public AiUsage(Guid organizationId, Guid requestId, int attempt, string provider, string model, bool succeeded,
        int inputTokens, int outputTokens, decimal costUsd, int latencyMs, string? errorCode)
    {
        OrganizationId = organizationId;
        RequestId = requestId;
        Attempt = attempt;
        Provider = provider;
        Model = model;
        Succeeded = succeeded;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        CostUsd = costUsd;
        LatencyMs = latencyMs;
        ErrorCode = errorCode;
    }

    public Guid RequestId { get; private set; }
    public int Attempt { get; private set; }
    public string Provider { get; private set; } = default!;
    public string Model { get; private set; } = default!;
    public bool Succeeded { get; private set; }
    public int InputTokens { get; private set; }
    public int OutputTokens { get; private set; }
    public decimal CostUsd { get; private set; }
    public int LatencyMs { get; private set; }
    public string? ErrorCode { get; private set; }
}
