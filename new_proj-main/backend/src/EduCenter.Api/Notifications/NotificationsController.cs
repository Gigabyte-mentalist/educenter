using System.Security.Claims;
using EduCenter.Api.Notifications.Delivery;
using EduCenter.Infrastructure;
using EduCenter.Modules.Notifications.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Notifications;

public record NotificationDto(Guid Id, string Type, string Title, string Body, string? Link, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);
public record UnreadCountDto(int Count);
/// <summary>Which channels a person gets one notification type on. The inbox, and the two outside ones.</summary>
public record ChannelChoicesDto(bool InApp, bool Email, bool Sms);

/// <param name="InAppEnabled">The inbox switch, kept as it was before Email and SMS existed; the same value as <c>Channels.InApp</c>.</param>
public record NotificationPreferenceDto(string Type, string Label, string Description, bool InAppEnabled, ChannelChoicesDto Channels);

/// <summary>Send only the channels being changed. <c>InAppEnabled</c> is the older spelling of <c>InApp</c>.</summary>
public record UpdateNotificationPreferenceRequest(bool? InAppEnabled = null, bool? InApp = null, bool? Email = null, bool? Sms = null);

/// <param name="Reason">Why a channel cannot be used — shown next to its switch.</param>
public record ChannelStateDto(bool Available, string? Reason);
public record ChannelAvailabilityDto(ChannelStateDto Email, ChannelStateDto Sms);

/// <summary>
/// The signed-in user's own inbox and notification settings. No permission code: every account has an inbox,
/// and every query is pinned to the caller's own user id — nobody reads or marks anyone else's notifications.
/// </summary>
[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController(AppDbContext db, IEnumerable<INotificationSender> senders) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<NotificationDto>>> List([FromQuery] bool unreadOnly = false, [FromQuery] int limit = 30,
        [FromQuery] DateTimeOffset? before = null)
    {
        if (CurrentUserId is not { } userId) return Unauthorized();

        var query = db.Notifications.Where(n => n.UserId == userId);
        if (unreadOnly) query = query.Where(n => n.ReadAt == null);
        if (before is not null) query = query.Where(n => n.CreatedAt < before);

        var items = await query
            .OrderByDescending(n => n.CreatedAt)
            .Take(Math.Clamp(limit, 1, 100))
            .Select(n => new NotificationDto(n.Id, n.Type, n.Title, n.Body, n.Link, n.CreatedAt, n.ReadAt))
            .ToListAsync();

        return Ok(items);
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<UnreadCountDto>> UnreadCount()
    {
        if (CurrentUserId is not { } userId) return Unauthorized();
        return Ok(new UnreadCountDto(await db.Notifications.CountAsync(n => n.UserId == userId && n.ReadAt == null)));
    }

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id)
    {
        if (CurrentUserId is not { } userId) return Unauthorized();

        var notification = await db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId);
        if (notification is null) return NotFound();

        notification.MarkRead();
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<ActionResult<UnreadCountDto>> MarkAllRead()
    {
        if (CurrentUserId is not { } userId) return Unauthorized();

        var unread = await db.Notifications.Where(n => n.UserId == userId && n.ReadAt == null).ToListAsync();
        foreach (var notification in unread) notification.MarkRead();
        await db.SaveChangesAsync();

        return Ok(new UnreadCountDto(unread.Count));
    }

    [HttpGet("preferences")]
    public async Task<ActionResult<List<NotificationPreferenceDto>>> Preferences()
    {
        if (CurrentUserId is not { } userId) return Unauthorized();

        var roles = await RolesAsync(userId);
        // What the person set explicitly; everything else is the channel's default for that type.
        var chosen = (await db.NotificationPreferences.Where(p => p.UserId == userId)
                .Select(p => new { p.Type, p.Channel, p.Enabled }).ToListAsync())
            .ToDictionary(p => (p.Type, p.Channel), p => p.Enabled);
        bool Enabled(string type, NotificationChannel channel) =>
            chosen.TryGetValue((type, channel), out var value) ? value : NotificationChannelDefaults.IsOnByDefault(type, channel);

        return Ok(NotificationCatalog.ForRoles(roles)
            .Select(t => new NotificationPreferenceDto(t.Type, t.Label, t.Description, Enabled(t.Type, NotificationChannel.InApp),
                new ChannelChoicesDto(Enabled(t.Type, NotificationChannel.InApp), Enabled(t.Type, NotificationChannel.Email), Enabled(t.Type, NotificationChannel.Sms))))
            .ToList());
    }

    /// <summary>Whether Email and SMS can reach the caller right now — the switch is meaningless without a mail server or gateway
    /// behind it, and SMS without a phone number in the profile.</summary>
    [HttpGet("channels")]
    public async Task<ActionResult<ChannelAvailabilityDto>> Channels()
    {
        if (CurrentUserId is not { } userId) return Unauthorized();

        var contact = await db.Users.Where(u => u.Id == userId).Select(u => new { u.Email, u.Phone }).FirstOrDefaultAsync();
        if (contact is null) return Unauthorized();

        ChannelStateDto State(NotificationChannel channel, bool hasAddress, string missing)
        {
            if (!senders.Any(s => s.Channel == channel && s.IsEnabled)) return new ChannelStateDto(false, "Hozircha sozlanmagan.");
            return hasAddress ? new ChannelStateDto(true, null) : new ChannelStateDto(false, missing);
        }

        return Ok(new ChannelAvailabilityDto(
            State(NotificationChannel.Email, !string.IsNullOrWhiteSpace(contact.Email), "Profilingizda email yo'q."),
            State(NotificationChannel.Sms, !string.IsNullOrWhiteSpace(contact.Phone), "Profilingizda telefon raqami yo'q.")));
    }

    [HttpPut("preferences/{type}")]
    public async Task<IActionResult> UpdatePreference(string type, UpdateNotificationPreferenceRequest request)
    {
        if (CurrentUserId is not { } userId || CurrentOrganizationId is not { } organizationId) return Unauthorized();

        var info = NotificationCatalog.Find(type);
        var roles = await RolesAsync(userId);
        if (info is null || !info.Roles.Any(roles.Contains))
            return BadRequest(new { error = "Bu bildirishnoma turi sizning rolingiz uchun mavjud emas." });

        var changes = new List<(NotificationChannel Channel, bool Enabled)>();
        if ((request.InApp ?? request.InAppEnabled) is { } inApp) changes.Add((NotificationChannel.InApp, inApp));
        if (request.Email is { } email) changes.Add((NotificationChannel.Email, email));
        if (request.Sms is { } sms) changes.Add((NotificationChannel.Sms, sms));
        if (changes.Count == 0) return BadRequest(new { error = "Kamida bitta kanalni ko'rsating." });

        if (changes.Any(c => c.Channel == NotificationChannel.Sms && c.Enabled)
            && string.IsNullOrWhiteSpace(await db.Users.Where(u => u.Id == userId).Select(u => u.Phone).FirstOrDefaultAsync()))
            return BadRequest(new { error = "SMS olish uchun profilingizda telefon raqami bo'lishi kerak." });

        var existing = await db.NotificationPreferences.Where(p => p.UserId == userId && p.Type == type).ToListAsync();
        foreach (var (channel, enabled) in changes)
        {
            var preference = existing.FirstOrDefault(p => p.Channel == channel);
            if (preference is null)
                db.NotificationPreferences.Add(new NotificationPreference(organizationId, userId, type, channel, enabled));
            else
                preference.SetEnabled(enabled);
        }

        await db.SaveChangesAsync();
        return NoContent();
    }

    private Guid? CurrentUserId =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private Guid? CurrentOrganizationId =>
        Guid.TryParse(User.FindFirstValue("org"), out var id) ? id : null;

    private Task<List<string>> RolesAsync(Guid userId) =>
        (from ur in db.UserRoles
         join r in db.Roles on ur.RoleId equals r.Id
         where ur.UserId == userId
         select r.Code).ToListAsync();
}
