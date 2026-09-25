using System.Text.Json;
using EduCenter.Api.Ai.Providers;
using EduCenter.Api.Ai.Writing;
using EduCenter.Infrastructure;
using EduCenter.Modules.Ai.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EduCenter.Api.Ai.Speaking;

public enum SpeakingOutcome
{
    Ok,
    Invalid,
    NotAStudent,
    NotFound,
    Conflict,
    LimitExceeded,
    Unavailable,
    Failed
}

public sealed record SpeakingResult(SpeakingOutcome Outcome, Guid? SessionId, string? Error);

public sealed record QuestionAudioResult(SpeakingOutcome Outcome, SpeechResult? Speech, string? Error);

/// <summary>
/// Speaking practice (docs/00_TZ.md §34, §35): the examiner's questions come from the AI Gateway, the student's answers
/// are transcribed by the local Whisper service through the same gateway (so the daily minutes, fallback and usage
/// journal apply), and the closing assessment scores the transcript. Audio stays on this machine.
/// </summary>
public class SpeakingService(
    AppDbContext db, AiGateway gateway, AiCallContextFactory contexts, AiProviderRegistry providers, AiLimits limits,
    SpeakingAudioStorage storage, IOptions<AiOptions> options, ILogger<SpeakingService> logger)
{
    public async Task<SpeakingResult> StartAsync(Guid userId, SpeakingMode mode, CancellationToken ct = default)
    {
        var context = await contexts.ForUserAsync(userId, AiFeature.Speaking, ct);
        if (context?.StudentId is null)
            return new SpeakingResult(SpeakingOutcome.NotAStudent, null, "Speaking mashqi faqat student akkaunti uchun.");
        if (providers.SpeechRoute().Count == 0)
            return new SpeakingResult(SpeakingOutcome.Unavailable, null, "Nutqni matnga o'girish xizmati sozlanmagan.");

        // A Full Mock is fourteen minutes of speech. Started on less than that it would stop half way, on a limit — so it is refused
        // up front, with the reason, and the shorter modes stay available.
        if (mode == SpeakingMode.FullMock && await limits.CheckAsync(context, ct) is { Remaining: < SpeakingScript.FullMockMinutes } allowance)
            return new SpeakingResult(SpeakingOutcome.LimitExceeded, null,
                $"Full Mock taxminan {SpeakingScript.FullMockMinutes} daqiqa davom etadi, bugungi limitdan {allowance.Remaining} daqiqa qoldi. "
                + "Qisqaroq mashqni tanlang yoki ertaga urinib ko'ring.");

        // One practice at a time: an older unfinished session is closed rather than left hanging.
        var stale = await db.SpeakingSessions
            .Where(s => s.UserId == userId && s.Status == SpeakingSessionStatus.Active)
            .ToListAsync(ct);
        foreach (var session in stale) session.Abandon();

        var question = await gateway.CompleteJsonAsync<SpeakingQuestionOutput>(context, AiTaskKind.SpeakingReply,
            SpeakingScript.QuestionPrompt(mode, null, [], nextOrdinal: 1), SpeakingScript.ValidateQuestion, ct);
        if (!question.IsSuccess) return Map(question.Status, question.Error);

        var started = new SpeakingSession(context.OrganizationId, context.StudentId.Value, userId, mode,
            Trim(question.Value!.Topic ?? SpeakingScript.ModeLabel(mode), 200));
        db.SpeakingSessions.Add(started);
        db.SpeakingTurns.Add(new SpeakingTurn(context.OrganizationId, started.Id, 1, Trim(question.Value.Question!, SpeakingTurn.MaxQuestionLength),
            SpeakingScript.PartFor(mode, 1)));
        await db.SaveChangesAsync(ct);

        return new SpeakingResult(SpeakingOutcome.Ok, started.Id, null);
    }

    public async Task<SpeakingResult> AnswerAsync(Guid userId, Guid sessionId, Stream audio, string fileName, long sizeBytes,
        int clientSeconds, CancellationToken ct = default)
    {
        var context = await contexts.ForUserAsync(userId, AiFeature.Speaking, ct);
        if (context?.StudentId is null)
            return new SpeakingResult(SpeakingOutcome.NotAStudent, null, "Speaking mashqi faqat student akkaunti uchun.");

        var session = await db.SpeakingSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
        if (session is null) return new SpeakingResult(SpeakingOutcome.NotFound, null, null);
        if (session.Status != SpeakingSessionStatus.Active)
            return new SpeakingResult(SpeakingOutcome.Conflict, sessionId, "Bu mashq yakunlangan.");

        var invalid = await storage.ValidateAsync(audio, fileName, sizeBytes, ct);
        if (invalid is not null) return new SpeakingResult(SpeakingOutcome.Invalid, sessionId, invalid);

        var turns = await db.SpeakingTurns.Where(t => t.SessionId == sessionId).OrderBy(t => t.Ordinal).ToListAsync(ct);
        var current = turns.LastOrDefault(t => t.Transcript is null);
        if (current is null) return new SpeakingResult(SpeakingOutcome.Conflict, sessionId, "Javob kutilayotgan savol yo'q.");

        var audioKey = await storage.SaveAsync(audio, fileName, ct);
        await using var stored = storage.Open(audioKey)
                                 ?? throw new InvalidOperationException("The recording disappeared right after it was written.");

        var transcription = await gateway.TranscribeAsync(context, stored, fileName, SpeakingAudioStorage.ContentTypeFor(fileName),
            options.Value.SpeechLanguage, Math.Clamp(clientSeconds, 0, 600), ct);
        if (!transcription.IsSuccess)
        {
            storage.Delete(audioKey);
            return Map(transcription.Status, transcription.Error, sessionId);
        }

        var metrics = SpeakingScript.Measure(transcription.Value!, Math.Clamp(clientSeconds, 0, 600));
        if (string.IsNullOrWhiteSpace(transcription.Value!.Text))
        {
            storage.Delete(audioKey);
            return new SpeakingResult(SpeakingOutcome.Invalid, sessionId, "Ovoz eshitilmadi. Mikrofonni tekshirib, qaytadan yozing.");
        }

        current.Answer(audioKey, transcription.Value.Text, metrics.SpokenSeconds, metrics.WordsPerMinute, metrics.LongPauses);
        session.RecordTurn(metrics.SpokenSeconds);

        if (session.TurnCount < Math.Min(SpeakingScript.TurnsFor(session.Mode), SpeakingSession.MaxTurnsFor(session.Mode)))
        {
            var history = turns.Select(t => new SpeakingExchange(t.Question, t.Transcript, SpeakingScript.PartOf(session.Mode, t))).ToList();
            var nextOrdinal = current.Ordinal + 1;
            var next = await gateway.CompleteJsonAsync<SpeakingQuestionOutput>(context, AiTaskKind.SpeakingReply,
                SpeakingScript.QuestionPrompt(session.Mode, session.Topic, history, nextOrdinal), SpeakingScript.ValidateQuestion, ct);
            if (next.IsSuccess)
                db.SpeakingTurns.Add(new SpeakingTurn(session.OrganizationId, session.Id, nextOrdinal,
                    Trim(next.Value!.Question!, SpeakingTurn.MaxQuestionLength), SpeakingScript.PartFor(session.Mode, nextOrdinal)));
            else
                logger.LogWarning("Speaking session {SessionId}: next question failed ({Error})", sessionId, next.Error);
        }

        await db.SaveChangesAsync(ct);
        return new SpeakingResult(SpeakingOutcome.Ok, sessionId, null);
    }

    public async Task<SpeakingResult> FinishAsync(Guid userId, Guid sessionId, CancellationToken ct = default)
    {
        var context = await contexts.ForUserAsync(userId, AiFeature.Speaking, ct);
        if (context?.StudentId is null)
            return new SpeakingResult(SpeakingOutcome.NotAStudent, null, "Speaking mashqi faqat student akkaunti uchun.");

        var session = await db.SpeakingSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
        if (session is null) return new SpeakingResult(SpeakingOutcome.NotFound, null, null);
        if (session.Status == SpeakingSessionStatus.Completed)
            return new SpeakingResult(SpeakingOutcome.Ok, sessionId, null);

        var answered = await db.SpeakingTurns
            .Where(t => t.SessionId == sessionId && t.Transcript != null)
            .OrderBy(t => t.Ordinal)
            .ToListAsync(ct);
        if (answered.Count == 0)
            return new SpeakingResult(SpeakingOutcome.Conflict, sessionId, "Baholash uchun kamida bitta javob kerak.");

        var metrics = new SpeechMetrics(
            session.SpokenSeconds,
            answered.Where(t => t.WordsPerMinute is > 0).Select(t => t.WordsPerMinute!.Value).DefaultIfEmpty(0).Sum()
                / Math.Max(1, answered.Count(t => t.WordsPerMinute is > 0)),
            answered.Sum(t => t.LongPauses ?? 0));

        var history = answered.Select(t => new SpeakingExchange(t.Question, t.Transcript, SpeakingScript.PartOf(session.Mode, t))).ToList();
        var assessment = await gateway.CompleteJsonAsync<SpeakingAssessmentOutput>(context, AiTaskKind.SpeakingReply,
            SpeakingScript.AssessmentPrompt(session.Mode, history, metrics), output => SpeakingScript.ValidateAssessment(output, session.Mode), ct);
        if (!assessment.IsSuccess)
        {
            session.Fail(assessment.Error ?? "Baholab bo'lmadi.");
            await db.SaveChangesAsync(ct);
            return Map(assessment.Status, assessment.Error, sessionId);
        }

        var value = assessment.Value!;
        // Same half-band rounding and IELTS mean rule as Writing, over the three criteria a transcript supports. Relevance is rounded the
        // same way and kept beside them — it is not one of the three, so it is not in the mean (see SpeakingSession.RelevanceBand).
        var fluency = WritingRubric.HalfBand(value.FluencyCoherence!.Band!.Value);
        var lexical = WritingRubric.HalfBand(value.LexicalResource!.Band!.Value);
        var grammar = WritingRubric.HalfBand(value.GrammaticalRange!.Band!.Value);
        var relevance = WritingRubric.HalfBand(value.Relevance!.Band!.Value);

        var parts = session.Mode == SpeakingMode.FullMock
            ? value.Parts!.Where(p => p.Part is >= 1 and <= 3).GroupBy(p => p.Part!.Value).OrderBy(g => g.Key)
                .Select(g => new SpeakingPartComment(g.Key, Clean(g.First().Comment))).ToList()
            : null;

        var stored = new SpeakingAssessment(
            Clean(value.FluencyCoherence.Comment), Clean(value.LexicalResource.Comment), Clean(value.GrammaticalRange.Comment),
            Texts(value.Strengths), Texts(value.Weaknesses), Texts(value.Suggestions),
            metrics.SpokenSeconds, metrics.WordsPerMinute, metrics.LongPauses, answered.Count,
            Clean(value.Relevance.Comment), parts);

        session.Complete(fluency, lexical, grammar, WritingRubric.OverallBand(fluency, lexical, grammar),
            JsonSerializer.Serialize(stored, WritingRubric.FeedbackJsonOptions), relevance);
        await db.SaveChangesAsync(ct);

        return new SpeakingResult(SpeakingOutcome.Ok, sessionId, null);
    }

    /// <summary>
    /// The examiner's question of one turn, read aloud (docs/00_TZ.md §34 TTS). Only the owner of the session gets it. A recording is
    /// made once per question and voice and then served from the cache — no gateway call, no allowance spent — so a student who
    /// replays a question, or a second student who is asked the same one, costs the provider nothing.
    /// <see cref="SpeakingOutcome.Unavailable"/> means no voice is configured or the provider failed; the caller shows the text.
    /// </summary>
    public async Task<QuestionAudioResult> QuestionAudioAsync(Guid userId, Guid sessionId, Guid turnId, CancellationToken ct = default)
    {
        var question = await (
            from turn in db.SpeakingTurns
            join session in db.SpeakingSessions on turn.SessionId equals session.Id
            where turn.Id == turnId && session.Id == sessionId && session.UserId == userId
            select turn.Question
        ).AsNoTracking().FirstOrDefaultAsync(ct);
        if (question is null) return new QuestionAudioResult(SpeakingOutcome.NotFound, null, null);

        var targets = providers.SynthesisRoute();
        if (targets.Count == 0)
            return new QuestionAudioResult(SpeakingOutcome.Unavailable, null, "Savolni ovozda o'qish xizmati sozlanmagan.");

        // Any voice of the chain may already have it — the one a fallback made while the first provider was down included.
        foreach (var target in targets)
            if (await storage.FindQuestionAudioAsync(question, target.Voice, ct) is { } cached)
                return new QuestionAudioResult(SpeakingOutcome.Ok, cached, null);

        var context = await contexts.ForUserAsync(userId, AiFeature.Speaking, ct);
        if (context?.StudentId is null)
            return new QuestionAudioResult(SpeakingOutcome.NotAStudent, null, "Speaking mashqi faqat student akkaunti uchun.");

        var result = await gateway.SynthesizeAsync(context, question, options.Value.SpeechLanguage, ct);
        if (!result.IsSuccess)
        {
            var outcome = result.Status switch
            {
                AiResultStatus.LimitExceeded => SpeakingOutcome.LimitExceeded,
                AiResultStatus.Rejected => SpeakingOutcome.Invalid,
                _ => SpeakingOutcome.Unavailable
            };
            return new QuestionAudioResult(outcome, null, result.Error);
        }

        await storage.SaveQuestionAudioAsync(question, result.Value!, ct);
        return new QuestionAudioResult(SpeakingOutcome.Ok, result.Value, null);
    }

    public async Task<SpeakingResult> AbandonAsync(Guid userId, Guid sessionId, CancellationToken ct = default)
    {
        var session = await db.SpeakingSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
        if (session is null) return new SpeakingResult(SpeakingOutcome.NotFound, null, null);

        if (session.Status == SpeakingSessionStatus.Active)
        {
            session.Abandon();
            await db.SaveChangesAsync(ct);
        }

        return new SpeakingResult(SpeakingOutcome.Ok, sessionId, null);
    }

    private static SpeakingResult Map(AiResultStatus status, string? error, Guid? sessionId = null) => status switch
    {
        AiResultStatus.LimitExceeded => new SpeakingResult(SpeakingOutcome.LimitExceeded, sessionId, error),
        AiResultStatus.Unavailable => new SpeakingResult(SpeakingOutcome.Unavailable, sessionId, error),
        AiResultStatus.Rejected => new SpeakingResult(SpeakingOutcome.Invalid, sessionId, error),
        _ => new SpeakingResult(SpeakingOutcome.Failed, sessionId, error)
    };

    private static List<string> Texts(IEnumerable<string>? items) =>
        (items ?? []).Select(i => Clean(i)).OfType<string>().Take(6).ToList();

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : Trim(value.Trim(), 600);

    private static string Trim(string value, int max) => value.Length <= max ? value : value[..max];
}
