using EduCenter.Api.Authorization;
using EduCenter.Infrastructure;
using EduCenter.Modules.Ai.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Ai.Speaking;

public record StartSpeakingRequest(SpeakingMode Mode);

public record SpeakingAnswerRequest(IFormFile Audio, int DurationSeconds);

/// <param name="Part">1, 2 or 3 for an IELTS-shaped session — the part of the test this question is in. Null for the other modes.</param>
public record SpeakingTurnDto(
    Guid Id, int Ordinal, string Question, string? Transcript, int SpokenSeconds, int? WordsPerMinute, int? LongPauses, bool HasAudio,
    int? Part = null);

/// <summary>A teacher's bands beside the AI's, and what they said about the session. The AI's stay as they were.</summary>
public record SpeakingTeacherReviewDto(
    decimal? Band, decimal? FluencyBand, decimal? LexicalBand, decimal? GrammarBand, string? Comment, string? ByName, DateTimeOffset At);

/// <param name="TeacherReview">Set once a teacher has corrected the session; the student sees it too.</param>
/// <param name="CanOverride">Only ever true on the staff endpoint, for a caller who holds <c>ai.speaking.override</c> over this student.</param>
/// <param name="RelevanceBand">How well the answers answered the questions — beside the three criteria, not in <c>EstimatedBand</c>; null before it was asked for.</param>
/// <param name="CurrentPart">The part of the test the waiting question is in, while the session is active and IELTS-shaped.</param>
/// <param name="CurrentTurnId">The turn the waiting question belongs to — what its recorded voice is fetched by.</param>
public record SpeakingSessionDto(
    Guid Id, SpeakingMode Mode, string ModeLabel, string Topic, SpeakingSessionStatus Status, int TurnCount, int TotalTurns,
    int SpokenSeconds, decimal? EstimatedBand, decimal? FluencyBand, decimal? LexicalBand, decimal? GrammarBand,
    string? Error, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt, List<SpeakingTurnDto> Turns,
    SpeakingAssessment? Assessment, string? CurrentQuestion,
    SpeakingTeacherReviewDto? TeacherReview = null, bool CanOverride = false,
    decimal? RelevanceBand = null, int? CurrentPart = null, Guid? CurrentTurnId = null);

public record SpeakingSessionSummaryDto(
    Guid Id, SpeakingMode Mode, string ModeLabel, string Topic, SpeakingSessionStatus Status, int TurnCount, int SpokenSeconds,
    decimal? EstimatedBand, DateTimeOffset CreatedAt, decimal? TeacherBand = null);

/// <summary>Speaking practice for students (docs/00_TZ.md §34). Sessions, recordings and assessments belong to the
/// account that made them, and every endpoint here answers only for that account. Staff read a student's finished practice through
/// <see cref="StudentSpeakingController"/> (<c>ai.speaking.review</c>) — never through here.</summary>
[ApiController]
[Route("api/ai/speaking")]
[Authorize(Policy = "ai.speaking.use")]
public class SpeakingController(AppDbContext db, SpeakingService speaking, SpeakingAudioStorage storage) : ControllerBase
{
    [HttpGet("sessions")]
    public async Task<ActionResult<List<SpeakingSessionSummaryDto>>> Sessions(CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        var sessions = await db.SpeakingSessions
            .Where(s => s.UserId == scope.UserId)
            .OrderByDescending(s => s.CreatedAt)
            .Take(50)
            .AsNoTracking()
            .ToListAsync(ct);

        return Ok(sessions.Select(s => new SpeakingSessionSummaryDto(s.Id, s.Mode, SpeakingScript.ModeLabel(s.Mode), s.Topic,
            s.Status, s.TurnCount, s.SpokenSeconds, s.EstimatedBand, s.CreatedAt, s.TeacherBand)).ToList());
    }

    [HttpPost("sessions")]
    [EnableRateLimiting(AiGateway.RateLimitPolicy)]
    public async Task<ActionResult<SpeakingSessionDto>> Start(StartSpeakingRequest request, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        var result = await speaking.StartAsync(scope.UserId, request.Mode, ct);
        if (result.Outcome != SpeakingOutcome.Ok) return Problem(result);

        return CreatedAtAction(nameof(Get), new { id = result.SessionId!.Value },
            await SpeakingSessionView.BuildAsync(db, result.SessionId.Value, ct));
    }

    [HttpGet("sessions/{id:guid}")]
    public async Task<ActionResult<SpeakingSessionDto>> Get(Guid id, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (!await db.SpeakingSessions.AnyAsync(s => s.Id == id && s.UserId == scope.UserId, ct)) return NotFound();
        return Ok(await SpeakingSessionView.BuildAsync(db, id, ct));
    }

    /// <summary>One recorded answer: stored, transcribed, measured, and followed by the next question.</summary>
    [HttpPost("sessions/{id:guid}/turns")]
    [EnableRateLimiting(AiGateway.RateLimitPolicy)]
    [RequestSizeLimit(32 * 1024 * 1024)]
    public async Task<ActionResult<SpeakingSessionDto>> Answer(Guid id, [FromForm] SpeakingAnswerRequest request, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (request.Audio is null || request.Audio.Length == 0) return BadRequest(new { error = "Ovoz yozuvi yuborilmadi." });

        await using var audio = request.Audio.OpenReadStream();
        var result = await speaking.AnswerAsync(scope.UserId, id, audio, request.Audio.FileName, request.Audio.Length,
            request.DurationSeconds, ct);
        if (result.Outcome != SpeakingOutcome.Ok) return Problem(result);

        return Ok(await SpeakingSessionView.BuildAsync(db, id, ct));
    }

    [HttpPost("sessions/{id:guid}/finish")]
    [EnableRateLimiting(AiGateway.RateLimitPolicy)]
    public async Task<ActionResult<SpeakingSessionDto>> Finish(Guid id, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        var result = await speaking.FinishAsync(scope.UserId, id, ct);
        if (result.Outcome != SpeakingOutcome.Ok) return Problem(result);

        return Ok(await SpeakingSessionView.BuildAsync(db, id, ct));
    }

    [HttpPost("sessions/{id:guid}/abandon")]
    public async Task<ActionResult<SpeakingSessionDto>> Abandon(Guid id, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        var result = await speaking.AbandonAsync(scope.UserId, id, ct);
        if (result.Outcome != SpeakingOutcome.Ok) return Problem(result);

        return Ok(await SpeakingSessionView.BuildAsync(db, id, ct));
    }

    [HttpGet("sessions/{id:guid}/turns/{turnId:guid}/audio")]
    public async Task<IActionResult> Audio(Guid id, Guid turnId, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (!await db.SpeakingSessions.AnyAsync(s => s.Id == id && s.UserId == scope.UserId, ct)) return NotFound();

        var turn = await db.SpeakingTurns.FirstOrDefaultAsync(t => t.Id == turnId && t.SessionId == id, ct);
        if (turn?.AudioKey is null) return NotFound();

        var file = storage.Open(turn.AudioKey);
        return file is null ? NotFound() : File(file, SpeakingAudioStorage.ContentTypeFor(turn.AudioKey));
    }

    /// <summary>The question of one turn, read aloud. 503 with <c>{ error }</c> — no voice configured, or the provider is down — is not
    /// a failure the student needs to see: the page keeps showing the question as text (and offers the browser's own voice).</summary>
    [HttpGet("sessions/{id:guid}/turns/{turnId:guid}/question-audio")]
    [EnableRateLimiting(AiGateway.RateLimitPolicy)]
    public async Task<IActionResult> QuestionAudio(Guid id, Guid turnId, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        var result = await speaking.QuestionAudioAsync(scope.UserId, id, turnId, ct);

        return result.Outcome switch
        {
            SpeakingOutcome.Ok => File(result.Speech!.Audio, result.Speech.ContentType),
            SpeakingOutcome.NotFound => NotFound(),
            SpeakingOutcome.NotAStudent => Forbid(),
            SpeakingOutcome.LimitExceeded => StatusCode(StatusCodes.Status429TooManyRequests, new { error = result.Error }),
            SpeakingOutcome.Invalid => BadRequest(new { error = result.Error }),
            _ => StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = result.Error })
        };
    }

    private ActionResult Problem(SpeakingResult result) => result.Outcome switch
    {
        SpeakingOutcome.NotFound => NotFound(),
        SpeakingOutcome.Conflict => Conflict(new { error = result.Error }),
        SpeakingOutcome.LimitExceeded => StatusCode(StatusCodes.Status429TooManyRequests, new { error = result.Error }),
        SpeakingOutcome.Unavailable => StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = result.Error }),
        SpeakingOutcome.Failed => StatusCode(StatusCodes.Status502BadGateway, new { error = result.Error }),
        _ => BadRequest(new { error = result.Error })
    };
}
