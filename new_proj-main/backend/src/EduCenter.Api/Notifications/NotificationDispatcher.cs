using EduCenter.Api.Notifications.Delivery;
using EduCenter.Infrastructure;
using EduCenter.Modules.Identity.Domain;
using EduCenter.Modules.Notifications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EduCenter.Api.Notifications;

/// <param name="ExternalBody">Text for Email/SMS instead of the standard one. Leave it out unless the message is safe to read on a lock screen:
/// the standard text is the notification's generic title and a link, and nothing else (docs/00_TZ.md §23).</param>
public record NotificationMessage(string Type, string Title, string Body, string? Link = null, string? DedupeKey = null, string? ExternalBody = null);

/// <summary>
/// Puts a message into recipients' inboxes and, for each outside channel (Email, SMS) they have on, queues a delivery (docs/00_TZ.md §39).
/// Skips users outside the organization, users who muted every channel for the type, and — for messages with a dedupe key — whoever already got it
/// on that channel. It only adds rows to the context: the caller's SaveChanges commits them together with the change that caused them, so a failed
/// business write never leaves a notification about something that didn't happen; <see cref="NotificationDeliveryInterceptor"/> queues the
/// deliveries once that save has gone through. A failing mail server or SMS gateway therefore never touches the inbox.
/// Telegram plugs in the same way once a bot token exists.
/// </summary>
public class NotificationDispatcher(AppDbContext db, IEnumerable<INotificationSender> senders, IOptions<AppOptions> app)
{
    /// <returns>How many people the message reached on at least one channel.</returns>
    public async Task<int> NotifyAsync(Guid organizationId, IEnumerable<Guid> userIds, NotificationMessage message, CancellationToken ct = default)
    {
        var recipients = userIds.Distinct().ToList();
        if (recipients.Count == 0) return 0;

        // What each person chose for this type, per channel; no row means the default.
        var choices = (await db.NotificationPreferences
            .Where(p => recipients.Contains(p.UserId) && p.Type == message.Type)
            .Select(p => new { p.UserId, p.Channel, p.Enabled })
            .ToListAsync(ct))
            .ToDictionary(p => (p.UserId, p.Channel), p => p.Enabled);
        bool Wants(Guid userId, NotificationChannel channel) =>
            choices.TryGetValue((userId, channel), out var enabled) ? enabled : NotificationChannelDefaults.IsOnByDefault(message.Type, channel);

        var users = await db.Users
            .Where(u => recipients.Contains(u.Id) && u.OrganizationId == organizationId)
            .Select(u => new { u.Id, u.Email, u.Phone, u.Status })
            .ToListAsync(ct);
        if (users.Count == 0) return 0;

        // A channel nobody configured gets no rows at all: no queue traffic, no "skipped" noise, no SMS bill.
        var live = senders.Where(s => s.IsEnabled).Select(s => s.Channel).ToHashSet();

        var inboxDone = new HashSet<Guid>();
        var outsideDone = NotificationChannelDefaults.Outside.ToDictionary(c => c, _ => new HashSet<Guid>());
        if (message.DedupeKey is not null)
        {
            var ids = users.Select(u => u.Id).ToList();
            inboxDone.UnionWith(await db.Notifications
                .Where(n => ids.Contains(n.UserId) && n.DedupeKey == message.DedupeKey).Select(n => n.UserId).ToListAsync(ct));
            inboxDone.UnionWith(db.Notifications.Local.Where(n => n.DedupeKey == message.DedupeKey).Select(n => n.UserId));

            var deliveries = await db.NotificationDeliveries
                .Where(d => ids.Contains(d.UserId) && d.DedupeKey == message.DedupeKey).Select(d => new { d.UserId, d.Channel }).ToListAsync(ct);
            foreach (var d in deliveries.Concat(db.NotificationDeliveries.Local.Where(d => d.DedupeKey == message.DedupeKey).Select(d => new { d.UserId, d.Channel })))
                if (outsideDone.TryGetValue(d.Channel, out var done)) done.Add(d.UserId);
        }

        var (subject, externalBody) = ExternalText(message);
        var reached = 0;
        foreach (var user in users)
        {
            Notification? inbox = null;
            if (Wants(user.Id, NotificationChannel.InApp) && !inboxDone.Contains(user.Id))
            {
                inbox = new Notification(organizationId, user.Id, message.Type, Trim(message.Title, 200), Trim(message.Body, 1000),
                    message.Link, message.DedupeKey);
                db.Notifications.Add(inbox);
            }

            var outside = 0;
            if (user.Status == UserStatus.Active)
                foreach (var channel in NotificationChannelDefaults.Outside)
                {
                    if (!live.Contains(channel) || !Wants(user.Id, channel) || outsideDone[channel].Contains(user.Id)) continue;
                    if (channel == NotificationChannel.Email && string.IsNullOrWhiteSpace(user.Email)) continue;
                    if (channel == NotificationChannel.Sms && string.IsNullOrWhiteSpace(user.Phone)) continue;

                    db.NotificationDeliveries.Add(new NotificationDelivery(organizationId, user.Id, inbox?.Id, channel, message.Type,
                        subject, externalBody, message.DedupeKey));
                    outside++;
                }

            if (inbox is not null || outside > 0) reached++;
        }

        return reached;
    }

    /// <summary>What goes out on Email/SMS: the type's generic name and a link — never the inbox title or body, which carry names, amounts and marks.</summary>
    private (string Subject, string Body) ExternalText(NotificationMessage message)
    {
        var label = NotificationCatalog.Find(message.Type)?.Label ?? "Yangi bildirishnoma";
        if (!string.IsNullOrWhiteSpace(message.ExternalBody)) return (label, Trim(message.ExternalBody, 1000));

        var url = AbsoluteLink(message.Link);
        return (label, url is null ? $"{label}. Batafsil ilovada." : $"{label}. Batafsil: {url}");
    }

    private string? AbsoluteLink(string? link)
    {
        var baseUrl = app.Value.PublicUrl.Trim().TrimEnd('/');
        if (baseUrl.Length == 0) return null;
        return string.IsNullOrWhiteSpace(link) ? baseUrl : link.StartsWith('/') ? baseUrl + link : $"{baseUrl}/{link}";
    }

    private static string Trim(string value, int max) => value.Length <= max ? value : value[..(max - 1)] + "…";
}
