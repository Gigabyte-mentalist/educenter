using System.Text.Json;
using EduCenter.Api.Authorization;
using EduCenter.Infrastructure;
using EduCenter.Modules.Ai.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Ai.Tutor;

public record TutorAskRequest(Guid? ConversationId, string? Question);

public record TutorConversationDto(Guid Id, string Title, int MessageCount, DateTimeOffset LastMessageAt);

public record TutorMessageDto(Guid Id, AiConversationRole Role, string Content, List<TutorCitation> Citations, DateTimeOffset CreatedAt);

public record TutorAnswerDto(Guid ConversationId, string Answer, List<TutorCitation> Citations, bool OutOfDomain);

/// <summary>AI Tutor for students (docs/00_TZ.md §32). A conversation belongs to the account that created it — nobody
/// else, staff included, reads it here.</summary>
[ApiController]
[Route("api/ai/tutor")]
[Authorize(Policy = "ai.tutor.use")]
public class TutorController(AppDbContext db, TutorService tutor) : ControllerBase
{
    [HttpGet("conversations")]
    public async Task<ActionResult<List<TutorConversationDto>>> Conversations(CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        var conversations = await db.AiConversations
            .Where(c => c.UserId == scope.UserId && c.Module == AiFeature.Tutor)
            .OrderByDescending(c => c.LastMessageAt)
            .Take(100)
            .Select(c => new TutorConversationDto(c.Id, c.Title, c.MessageCount, c.LastMessageAt))
            .AsNoTracking()
            .ToListAsync(ct);

        return Ok(conversations);
    }

    [HttpGet("conversations/{id:guid}/messages")]
    public async Task<ActionResult<List<TutorMessageDto>>> Messages(Guid id, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (!await db.AiConversations.AnyAsync(c => c.Id == id && c.UserId == scope.UserId, ct)) return NotFound();

        var messages = await db.AiConversationMessages
            .Where(m => m.ConversationId == id)
            .OrderBy(m => m.CreatedAt)
            .AsNoTracking()
            .ToListAsync(ct);

        return Ok(messages.Select(m => new TutorMessageDto(m.Id, m.Role, m.Content, Citations(m.CitationsJson), m.CreatedAt)).ToList());
    }

    [HttpPost("ask")]
    [EnableRateLimiting(AiGateway.RateLimitPolicy)]
    public async Task<ActionResult<TutorAnswerDto>> Ask(TutorAskRequest request, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (request.ConversationId is not null
            && !await db.AiConversations.AnyAsync(c => c.Id == request.ConversationId && c.UserId == scope.UserId, ct))
            return NotFound();

        var result = await tutor.AskAsync(scope.UserId, request.ConversationId, request.Question ?? "", ct);
        if (result.Turn is null)
        {
            return result.Status switch
            {
                AiResultStatus.LimitExceeded => StatusCode(StatusCodes.Status429TooManyRequests, new { error = result.Error }),
                AiResultStatus.Unavailable => StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = result.Error }),
                AiResultStatus.Rejected => BadRequest(new { error = result.Error }),
                _ => StatusCode(StatusCodes.Status502BadGateway, new { error = result.Error })
            };
        }

        return Ok(new TutorAnswerDto(result.Turn.ConversationId, result.Turn.Answer, result.Turn.Citations.ToList(), result.Turn.OutOfDomain));
    }

    [HttpDelete("conversations/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        var conversation = await db.AiConversations.FirstOrDefaultAsync(c => c.Id == id && c.UserId == scope.UserId, ct);
        if (conversation is null) return NotFound();

        await db.AiConversationMessages.Where(m => m.ConversationId == id).ExecuteDeleteAsync(ct);
        db.AiConversations.Remove(conversation);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static List<TutorCitation> Citations(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<TutorCitation>>(json, TutorService.WritingJsonOptions) ?? []; }
        catch (JsonException) { return []; }
    }
}
