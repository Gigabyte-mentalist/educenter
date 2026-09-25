using EduCenter.Modules.Notifications.Domain;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace EduCenter.Api.Notifications.Delivery;

/// <summary>Email over SMTP (MailKit). Configure <c>Notifications:Email</c>; until then <see cref="IsEnabled"/> is false and nothing is queued.</summary>
public sealed class SmtpEmailSender(IOptionsMonitor<EmailOptions> options) : INotificationSender
{
    public NotificationChannel Channel => NotificationChannel.Email;

    public bool IsEnabled
    {
        get
        {
            var o = options.CurrentValue;
            return o.Enabled && !string.IsNullOrWhiteSpace(o.Host) && !string.IsNullOrWhiteSpace(o.From);
        }
    }

    public async Task<SendResult> SendAsync(NotificationTarget target, NotificationEnvelope message, CancellationToken ct)
    {
        var o = options.CurrentValue;
        if (!IsEnabled) return SendResult.Skip("Email o'chirilgan.");
        if (!MailboxAddress.TryParse(target.Email ?? "", out var to)) return SendResult.Skip("Email manzili yo'q yoki noto'g'ri.");
        to.Name = target.FullName;

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(o.FromName, o.From));
        mime.To.Add(to);
        mime.Subject = message.Subject;
        mime.Body = new TextPart("plain") { Text = message.Body };

        using var client = new SmtpClient { Timeout = Math.Max(1, o.TimeoutSeconds) * 1000 };
        await client.ConnectAsync(o.Host, o.Port, o.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto, ct);
        if (!string.IsNullOrEmpty(o.User)) await client.AuthenticateAsync(o.User, o.Password, ct);
        await client.SendAsync(mime, ct);
        await client.DisconnectAsync(true, ct);
        return SendResult.Ok();
    }
}
