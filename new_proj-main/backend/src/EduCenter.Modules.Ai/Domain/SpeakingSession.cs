using EduCenter.SharedKernel;

namespace EduCenter.Modules.Ai.Domain;

/// <summary>Practice formats of docs/00_TZ.md §34. Interview, Business and Travel English come later. Stored as text, so a member
/// added in the middle changes nothing that is already saved.</summary>
public enum SpeakingMode
{
    IeltsPart1,
    IeltsPart2,
    IeltsPart3,

    /// <summary>The whole test in one sitting: Part 1 questions, a Part 2 cue card, then Part 3 discussion of the Part 2 topic.</summary>
    FullMock,
    GeneralEnglish,
    FreeConversation
}

public enum SpeakingSessionStatus
{
    Active,
    Completed,

    /// <summary>Assessment failed (provider outage or invalid output); the transcript is kept and it can be retried.</summary>
    Failed,

    Abandoned
}

/// <summary>
/// One speaking practice (docs/00_TZ.md §34, §35): turn-by-turn examiner questions and student answers, then an
/// estimated assessment over the whole transcript. Pronunciation scoring from the raw audio is deliberately out of
/// scope here (TZ §58 "advanced Speaking Assessment", §59) — this MVP scores what a transcript plus timings support.
/// </summary>
public class SpeakingSession : TenantEntity
{
    public const int MaxTurns = 12;
    public const int FullMockMaxTurns = 15;
    public const int MaxErrorLength = 500;

    /// <summary>The ceiling on exchanges in one session — a stop for a session that would otherwise keep asking. The planned length
    /// of each mode is below it (docs/21.09-audit-jurnali S08.02): the full test is 5 + 1 + 5 exchanges, and may grow to 15.</summary>
    public static int MaxTurnsFor(SpeakingMode mode) => mode == SpeakingMode.FullMock ? FullMockMaxTurns : MaxTurns;

    private SpeakingSession() { } // EF Core

    public SpeakingSession(Guid organizationId, Guid studentId, Guid userId, SpeakingMode mode, string topic)
    {
        OrganizationId = organizationId;
        StudentId = studentId;
        UserId = userId;
        Mode = mode;
        Topic = topic;
        Status = SpeakingSessionStatus.Active;
        SharedWithStaff = true;
    }

    public Guid StudentId { get; private set; }
    public Guid UserId { get; private set; }

    /// <summary>Whether staff with <c>ai.speaking.review</c> may read this session — its transcript, its recordings and its
    /// assessment. Speaking practice used to be private to the student; it is shared from the moment the student is told so
    /// (docs/21.09-audit-jurnali S04.03). Every session that existed before stays private: it was recorded under the old promise,
    /// and the migration leaves it <c>false</c>.</summary>
    public bool SharedWithStaff { get; private set; }
    public SpeakingMode Mode { get; private set; }
    public string Topic { get; private set; } = default!;
    public SpeakingSessionStatus Status { get; private set; }
    public int TurnCount { get; private set; }

    /// <summary>Seconds of student audio — what the daily speaking limit counts.</summary>
    public int SpokenSeconds { get; private set; }

    public decimal? EstimatedBand { get; private set; }
    public decimal? FluencyBand { get; private set; }
    public decimal? LexicalBand { get; private set; }
    public decimal? GrammarBand { get; private set; }

    /// <summary>Whether the answers actually answer the questions and stay on topic — read from the transcript, like the three above.
    /// It is shown next to them but is <b>not</b> part of <see cref="EstimatedBand"/>: IELTS has no such criterion of its own (relevance
    /// is judged inside Fluency and Coherence), so folding it into the mean would move the estimate away from the real scale. Null on
    /// every session assessed before it was asked for.</summary>
    public decimal? RelevanceBand { get; private set; }

    /// <summary>Feedback and the speech-rate/pause metrics, as JSON.</summary>
    public string? AssessmentJson { get; private set; }

    public string? Error { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public const int MaxCommentLength = 2000;

    /// <summary>A teacher's own bands, kept next to the AI's — never in place of them. The AI reads a transcript and cannot hear
    /// pronunciation; a teacher who listens to the recording can say it got a session wrong (docs/21.09-audit-jurnali S04.03).</summary>
    public decimal? TeacherBand { get; private set; }
    public decimal? TeacherFluencyBand { get; private set; }
    public decimal? TeacherLexicalBand { get; private set; }
    public decimal? TeacherGrammarBand { get; private set; }
    public string? TeacherComment { get; private set; }
    public Guid? OverriddenBy { get; private set; }
    public DateTimeOffset? OverriddenAt { get; private set; }

    public bool IsOverridden => OverriddenAt is not null;

    public void Override(decimal band, decimal? fluency, decimal? lexical, decimal? grammar, string? comment, Guid teacherId)
    {
        TeacherBand = band;
        TeacherFluencyBand = fluency;
        TeacherLexicalBand = lexical;
        TeacherGrammarBand = grammar;
        TeacherComment = comment;
        OverriddenBy = teacherId;
        OverriddenAt = DateTimeOffset.UtcNow;
        MarkUpdated();
    }

    public void RecordTurn(int spokenSeconds)
    {
        TurnCount++;
        SpokenSeconds += Math.Max(0, spokenSeconds);
        MarkUpdated();
    }

    public void Complete(decimal fluency, decimal lexical, decimal grammar, decimal overall, string assessmentJson, decimal? relevance = null)
    {
        Status = SpeakingSessionStatus.Completed;
        Error = null;
        FluencyBand = fluency;
        LexicalBand = lexical;
        GrammarBand = grammar;
        RelevanceBand = relevance;
        EstimatedBand = overall;
        AssessmentJson = assessmentJson;
        CompletedAt = DateTimeOffset.UtcNow;
        MarkUpdated();
    }

    public void Fail(string error)
    {
        Status = SpeakingSessionStatus.Failed;
        Error = error.Length <= MaxErrorLength ? error : error[..MaxErrorLength];
        MarkUpdated();
    }

    public void Abandon()
    {
        Status = SpeakingSessionStatus.Abandoned;
        MarkUpdated();
    }
}

/// <summary>One exchange: what the examiner asked, and what the student said back.</summary>
public class SpeakingTurn : TenantEntity
{
    public const int MaxQuestionLength = 1000;
    public const int MaxTranscriptLength = 4000;

    private SpeakingTurn() { } // EF Core

    public SpeakingTurn(Guid organizationId, Guid sessionId, int ordinal, string question, int? part = null)
    {
        OrganizationId = organizationId;
        SessionId = sessionId;
        Ordinal = ordinal;
        Question = question;
        Part = part;
    }

    public Guid SessionId { get; private set; }
    public int Ordinal { get; private set; }

    /// <summary>Which part of the IELTS test the question belongs to (1, 2 or 3). Null on a turn of a session that is not IELTS-shaped,
    /// and on every turn recorded before parts were kept — the script derives it from the mode and the ordinal then.</summary>
    public int? Part { get; private set; }
    public string Question { get; private set; } = default!;

    public string? AudioKey { get; private set; }
    public string? Transcript { get; private set; }
    public int SpokenSeconds { get; private set; }

    /// <summary>Words per minute over the answer — a fluency signal Whisper's timings give us for free.</summary>
    public int? WordsPerMinute { get; private set; }

    /// <summary>Silences longer than a second inside the answer.</summary>
    public int? LongPauses { get; private set; }

    public void Answer(string audioKey, string transcript, int spokenSeconds, int? wordsPerMinute, int? longPauses)
    {
        AudioKey = audioKey;
        Transcript = transcript.Length <= MaxTranscriptLength ? transcript : transcript[..MaxTranscriptLength];
        SpokenSeconds = spokenSeconds;
        WordsPerMinute = wordsPerMinute;
        LongPauses = longPauses;
        MarkUpdated();
    }
}
