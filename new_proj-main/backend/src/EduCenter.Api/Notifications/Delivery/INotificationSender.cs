using EduCenter.Modules.Notifications.Domain;

namespace EduCenter.Api.Notifications.Delivery;

/// <summary>Who a message goes to, as the channel needs to know them.</summary>
public record NotificationTarget(Guid UserId, string FullName, string? Email, string? Phone);

/// <summary>What leaves the app: a subject (mail only) and a text. Everything in it is visible to whoever reads the mailbox or the phone.</summary>
public record NotificationEnvelope(string Subject, string Body);

/// <param name="Skipped">Not sent on purpose (channel off, no valid address) — neither a success nor a failure worth retrying.</param>
public record SendResult(bool Success, string? Error = null, bool Skipped = false)
{
    public static SendResult Ok() => new(true);
    public static SendResult Failed(string error) => new(false, error);
    public static SendResult Skip(string reason) => new(false, reason, true);
}

/// <summary>One outside channel (docs/00_TZ.md §39). Telegram will be another implementation once a bot token exists.</summary>
public interface INotificationSender
{
    NotificationChannel Channel { get; }

    /// <summary>False while the channel is switched off or not configured. Nothing is queued for a channel that is not enabled.</summary>
    bool IsEnabled { get; }

    Task<SendResult> SendAsync(NotificationTarget target, NotificationEnvelope message, CancellationToken ct);
}
