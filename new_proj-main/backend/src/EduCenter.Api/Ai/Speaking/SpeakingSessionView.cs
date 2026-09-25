using System.Text.Json;
using EduCenter.Infrastructure;
using EduCenter.Modules.Ai.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Ai.Speaking;

/// <summary>One session as its owner sees it, and as staff see it once the student has shared it: the same turns, transcripts and
/// assessment either way. The caller has already decided the reader may see this session — nothing here checks access.</summary>
public static class SpeakingSessionView
{
    public static async Task<SpeakingSessionDto> BuildAsync(AppDbContext db, Guid sessionId, CancellationToken ct)
    {
        var session = await db.SpeakingSessions.AsNoTracking().FirstAsync(s => s.Id == sessionId, ct);
        var turns = await db.SpeakingTurns.AsNoTracking()
            .Where(t => t.SessionId == sessionId)
            .OrderBy(t => t.Ordinal)
            .ToListAsync(ct);

        SpeakingAssessment? assessment = null;
        if (session.AssessmentJson is not null)
        {
            try { assessment = JsonSerializer.Deserialize<SpeakingAssessment>(session.AssessmentJson, Writing.WritingRubric.FeedbackJsonOptions); }
            catch (JsonException) { /* a stored assessment that cannot be read is simply not shown */ }
        }

        SpeakingTeacherReviewDto? review = null;
        if (session.OverriddenAt is { } overriddenAt)
        {
            var byName = session.OverriddenBy is null
                ? null
                : await db.Users.Where(u => u.Id == session.OverriddenBy).Select(u => u.FullName).FirstOrDefaultAsync(ct);
            review = new SpeakingTeacherReviewDto(session.TeacherBand, session.TeacherFluencyBand, session.TeacherLexicalBand,
                session.TeacherGrammarBand, session.TeacherComment, byName, overriddenAt);
        }

        var current = session.Status == SpeakingSessionStatus.Active ? turns.LastOrDefault(t => t.Transcript is null) : null;

        return new SpeakingSessionDto(session.Id, session.Mode, SpeakingScript.ModeLabel(session.Mode), session.Topic, session.Status,
            session.TurnCount, SpeakingScript.TurnsFor(session.Mode), session.SpokenSeconds, session.EstimatedBand,
            session.FluencyBand, session.LexicalBand, session.GrammarBand, session.Error, session.CreatedAt, session.CompletedAt,
            turns.Select(t => new SpeakingTurnDto(t.Id, t.Ordinal, t.Question, t.Transcript, t.SpokenSeconds, t.WordsPerMinute,
                t.LongPauses, t.AudioKey is not null, SpeakingScript.PartOf(session.Mode, t))).ToList(),
            assessment,
            current?.Question,
            review,
            RelevanceBand: session.RelevanceBand,
            CurrentPart: current is null ? null : SpeakingScript.PartOf(session.Mode, current),
            CurrentTurnId: current?.Id);
    }
}
