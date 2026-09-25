using System.Globalization;
using System.Text.Json;
using EduCenter.Api.Authorization;
using EduCenter.Api.Notifications;
using EduCenter.Infrastructure;
using EduCenter.Modules.Ai.Domain;
using EduCenter.Modules.Audit.Domain;
using EduCenter.Modules.Notifications.Domain;
using EduCenter.Modules.Timeline.Domain;
using EduCenter.SharedKernel.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Ai.Speaking;

/// <summary>One finished practice in the list: when, what, and the four bands — no transcript, no recording (those are the detail).</summary>
public record StudentSpeakingRowDto(
    Guid Id, SpeakingMode Mode, string ModeLabel, string Topic, int TurnCount, int SpokenSeconds,
    decimal? EstimatedBand, decimal? FluencyBand, decimal? LexicalBand, decimal? GrammarBand,
    DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt, decimal? TeacherBand = null);

/// <param name="Band">The teacher's overall band; the three criteria are optional, as in the Writing override.</param>
public record OverrideSpeakingRequest(decimal Band, decimal? Fluency, decimal? Lexical, decimal? Grammar, string? Comment);

/// <summary>
/// The staff side of a student's Speaking practice (docs/21.09-audit-jurnali S04.03, TZ §7 "Speaking" tab): the finished sessions
/// with their assessment, transcript and recordings, for the people who teach the student (<c>ai.speaking.review</c>, within their
/// own scope — the same reach as <c>CanAccessStudentAsync</c> gives every other tab of the profile).
/// <para>Practice used to be the student's private space, and a student is now told that it is not. What staff can read is therefore
/// limited to what the student knew about when they practised: <see cref="SpeakingSession.SharedWithStaff"/> sessions that are
/// finished. A session still running, one the student walked away from, and everything recorded before the notice stay out of reach —
/// they answer 404, exactly as if they did not exist. Opening a session or a recording leaves an audit entry.</para>
/// <para>A teacher who has listened may correct the AI's bands (<c>ai.speaking.override</c>): theirs are kept beside the AI's, the AI's
/// stay as they were, and the student is told.</para>
/// </summary>
[ApiController]
[Route("api/students/{id:guid}/speaking")]
public class StudentSpeakingController(
    AppDbContext db, SpeakingAudioStorage storage, PermissionResolver permissions, NotificationDispatcher notifier) : ControllerBase
{
    private const int MaxSessions = 50;
    private const string ReviewPermission = "ai.speaking.review";
    private const string OverridePermission = "ai.speaking.override";

    [HttpGet("sessions")]
    [Authorize(Policy = ReviewPermission)]
    public async Task<ActionResult<List<StudentSpeakingRowDto>>> Sessions(Guid id, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (!await CanReadAsync(scope, id, ct)) return Forbid();

        var sessions = await Shared(scope, id)
            .OrderByDescending(s => s.CreatedAt).ThenBy(s => s.Id)
            .Take(MaxSessions)
            .AsNoTracking()
            .ToListAsync(ct);

        return Ok(sessions.Select(s => new StudentSpeakingRowDto(s.Id, s.Mode, SpeakingScript.ModeLabel(s.Mode), s.Topic, s.TurnCount,
            s.SpokenSeconds, s.EstimatedBand, s.FluencyBand, s.LexicalBand, s.GrammarBand, s.CreatedAt, s.CompletedAt, s.TeacherBand)).ToList());
    }

    /// <summary>The whole session: every question, the student's transcribed answers with their pace and pauses, and the assessment.</summary>
    [HttpGet("sessions/{sessionId:guid}")]
    [Authorize(Policy = ReviewPermission)]
    public async Task<ActionResult<SpeakingSessionDto>> Get(Guid id, Guid sessionId, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (!await CanReadAsync(scope, id, ct)) return Forbid();
        if (!await Shared(scope, id).AnyAsync(s => s.Id == sessionId, ct)) return NotFound();

        await RecordViewAsync(scope, id, sessionId, "session", ct);
        return Ok((await SpeakingSessionView.BuildAsync(db, sessionId, ct)) with { CanOverride = await CanOverrideAsync(scope.UserId, id, ct) });
    }

    /// <summary>The teacher's own bands after listening — beside the AI's, which stay unchanged. The whole review is one act: the bands, the
    /// comment, who and when.</summary>
    [HttpPost("sessions/{sessionId:guid}/override")]
    [Authorize(Policy = OverridePermission)]
    public async Task<ActionResult<SpeakingSessionDto>> Override(Guid id, Guid sessionId, OverrideSpeakingRequest request, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (!await CanReadAsync(scope, id, ct)) return Forbid();
        var session = await Shared(scope, id).FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null) return NotFound();

        decimal?[] bands = [request.Band, request.Fluency, request.Lexical, request.Grammar];
        if (bands.Any(b => b is not null && !Writing.WritingRubric.IsHalfBand(b.Value)))
            return BadRequest(new { error = "Band 0 dan 9 gacha, 0.5 qadam bilan bo'lishi kerak." });
        var comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();
        if (comment?.Length > SpeakingSession.MaxCommentLength)
            return BadRequest(new { error = $"Izoh {SpeakingSession.MaxCommentLength} belgidan oshmasligi kerak." });

        var before = Snapshot(session);
        session.Override(request.Band, request.Fluency, request.Lexical, request.Grammar, comment, scope.UserId);

        // Audited by hand with the bands only, like the Writing override: the session's turns are the student's own words.
        db.AuditLogs.Add(new AuditLog(scope.OrganizationId, scope.UserId, "Modified", nameof(SpeakingSession), session.Id,
            before, Snapshot(session), "AI speaking override"));

        var band = request.Band.ToString("0.0", CultureInfo.InvariantCulture);
        db.StudentTimelineEvents.Add(new StudentTimelineEvent(scope.OrganizationId, id, TimelineEventType.SpeakingReviewed,
            JsonSerializer.Serialize(new { sessionId = session.Id, teacherBand = request.Band })));
        await notifier.NotifyAsync(scope.OrganizationId, [session.UserId],
            new NotificationMessage(NotificationType.SpeakingReviewed, "O'qituvchi speaking bahosini ko'rib chiqdi",
                $"O'qituvchi bahosi: {band}.", "/ai/speaking"), ct);

        await db.SaveChangesAsync(ct);
        return Ok((await SpeakingSessionView.BuildAsync(db, sessionId, ct)) with { CanOverride = true });
    }

    [HttpGet("sessions/{sessionId:guid}/turns/{turnId:guid}/audio")]
    [Authorize(Policy = ReviewPermission)]
    public async Task<IActionResult> Audio(Guid id, Guid sessionId, Guid turnId, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (!await CanReadAsync(scope, id, ct)) return Forbid();
        if (!await Shared(scope, id).AnyAsync(s => s.Id == sessionId, ct)) return NotFound();

        var turn = await db.SpeakingTurns.FirstOrDefaultAsync(t => t.Id == turnId && t.SessionId == sessionId, ct);
        if (turn?.AudioKey is null) return NotFound();
        var file = storage.Open(turn.AudioKey);
        if (file is null) return NotFound();

        await RecordViewAsync(scope, id, sessionId, "audio", ct, turnId);
        return File(file, SpeakingAudioStorage.ContentTypeFor(turn.AudioKey));
    }

    /// <summary>Whether the caller holds <c>ai.speaking.override</c> over this student — the support teacher may read a session but not
    /// correct it, so the form is offered on the permission, never on the role.</summary>
    private async Task<bool> CanOverrideAsync(Guid userId, Guid studentId, CancellationToken ct)
    {
        var overrideScope = await permissions.ResolveAsync(userId, OverridePermission, ct);
        return overrideScope is not null && await overrideScope.CanAccessStudentAsync(db, studentId);
    }

    private static string Snapshot(SpeakingSession s) => JsonSerializer.Serialize(new
    {
        s.EstimatedBand, s.TeacherBand, s.TeacherFluencyBand, s.TeacherLexicalBand, s.TeacherGrammarBand, s.TeacherComment, s.OverriddenBy
    });

    /// <summary>The finished sessions of this student that staff may read.</summary>
    private IQueryable<SpeakingSession> Shared(ResolvedScope scope, Guid studentId) =>
        db.SpeakingSessions.Where(s => s.StudentId == studentId && s.OrganizationId == scope.OrganizationId
            && s.SharedWithStaff && s.Status == SpeakingSessionStatus.Completed);

    // The scope covers the student, and the student is in the caller's organization — the same two checks as the other profile tabs.
    private async Task<bool> CanReadAsync(ResolvedScope scope, Guid studentId, CancellationToken ct) =>
        await scope.CanAccessStudentAsync(db, studentId)
        && await db.Students.AnyAsync(s => s.Id == studentId && s.OrganizationId == scope.OrganizationId, ct);

    /// <summary>Who read a student's session or recording, and when — a student's voice is not something to open without a trace.</summary>
    private async Task RecordViewAsync(ResolvedScope scope, Guid studentId, Guid sessionId, string what, CancellationToken ct, Guid? turnId = null)
    {
        db.AuditLogs.Add(new AuditLog(scope.OrganizationId, scope.UserId, "Viewed", nameof(SpeakingSession), sessionId, null,
            JsonSerializer.Serialize(new { studentId, what, turnId }), null));
        await db.SaveChangesAsync(ct);
    }
}
