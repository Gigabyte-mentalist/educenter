using EduCenter.Infrastructure;
using EduCenter.Modules.Identity.Domain;
using EduCenter.Modules.Notifications.Domain;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EduCenter.Api.Notifications.Delivery;

/// <summary>Outside delivery happens away from the HTTP request: a slow mail server or SMS gateway must not make a teacher wait for the
/// attendance page to save.</summary>
public interface INotificationDeliveryQueue
{
    Task EnqueueAsync(IReadOnlyCollection<Guid> deliveryIds);
}

/// <summary>Production/development: one Hangfire background job per delivery (docs/00_TZ.md §44).</summary>
public class HangfireDeliveryQueue(IBackgroundJobClient jobs) : INotificationDeliveryQueue
{
    public Task EnqueueAsync(IReadOnlyCollection<Guid> deliveryIds)
    {
        foreach (var id in deliveryIds)
            jobs.Enqueue<NotificationDeliveryJob>(job => job.RunAsync(id, CancellationToken.None));
        return Task.CompletedTask;
    }
}

/// <summary>When Hangfire is off (Automation__Enabled=false, the test host) each delivery is attempted immediately in its own scope, up to
/// <see cref="NotificationDelivery.MaxAttempts"/> times — so a test sees the outcome as soon as the request returns. It never throws into the
/// request that saved the message: the inbox row is committed and stays whatever happens here.</summary>
public class InlineDeliveryQueue(IServiceScopeFactory scopes, ILogger<InlineDeliveryQueue> logger) : INotificationDeliveryQueue
{
    public async Task EnqueueAsync(IReadOnlyCollection<Guid> deliveryIds)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var job = scope.ServiceProvider.GetRequiredService<NotificationDeliveryJob>();
            foreach (var id in deliveryIds)
                for (var attempt = 0; attempt < NotificationDelivery.MaxAttempts; attempt++)
                    if (await job.AttemptAsync(id, CancellationToken.None)) break;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Inline delivery of {Count} notification(s) failed unexpectedly.", deliveryIds.Count);
        }
    }
}

/// <summary>
/// Sends one <see cref="NotificationDelivery"/>. The outcome is written to the row — Sent, Skipped, or after three failed attempts Failed —
/// and never touches the inbox message it belongs to. Under Hangfire a failed attempt throws so the job is retried after a growing delay;
/// the last attempt settles the row instead of throwing.
/// </summary>
public class NotificationDeliveryJob(
    AppDbContext db, IEnumerable<INotificationSender> senders, IOptionsMonitor<SmsOptions> sms, TimeProvider clock,
    ILogger<NotificationDeliveryJob> logger)
{
    [AutomaticRetry(Attempts = NotificationDelivery.MaxAttempts - 1, DelaysInSeconds = new[] { 60, 300 }, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task RunAsync(Guid deliveryId, CancellationToken ct)
    {
        if (!await AttemptAsync(deliveryId, ct))
            throw new InvalidOperationException($"Delivery {deliveryId} was not delivered; it will be retried.");
    }

    /// <summary>One attempt. True when the delivery is settled (sent, skipped, or failed for good); false when it should be tried again.</summary>
    public async Task<bool> AttemptAsync(Guid deliveryId, CancellationToken ct)
    {
        var delivery = await db.NotificationDeliveries.FirstOrDefaultAsync(d => d.Id == deliveryId, ct);
        if (delivery is null) return false;      // not visible yet (its save had not committed when the job started) — try again
        if (!delivery.IsPending) return true;    // settled by an earlier attempt

        var sender = senders.FirstOrDefault(s => s.Channel == delivery.Channel);
        var user = await db.Users.Where(u => u.Id == delivery.UserId)
            .Select(u => new { u.FullName, u.Email, u.Phone, u.Status }).FirstOrDefaultAsync(ct);

        string? skip = null;
        if (sender is null || !sender.IsEnabled) skip = "Kanal o'chirilgan.";
        else if (user is null || user.Status != UserStatus.Active) skip = "Foydalanuvchi faol emas.";
        else if (delivery.Channel == NotificationChannel.Email && string.IsNullOrWhiteSpace(user.Email)) skip = "Email manzili yo'q.";
        else if (delivery.Channel == NotificationChannel.Sms && string.IsNullOrWhiteSpace(user.Phone)) skip = "Telefon raqami yo'q.";
        else if (delivery.Channel == NotificationChannel.Sms && await SmsAllowanceSpentAsync(ct))
            skip = $"Kunlik SMS limiti ({sms.CurrentValue.DailyLimit}) tugadi.";

        if (skip is not null)
        {
            delivery.Skip(skip);
            await db.SaveChangesAsync(ct);
            return true;
        }

        delivery.RecordAttempt();
        SendResult result;
        try
        {
            result = await sender!.SendAsync(new NotificationTarget(delivery.UserId, user!.FullName, user.Email, user.Phone),
                new NotificationEnvelope(delivery.Subject, delivery.Body), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result = SendResult.Failed(ex.Message);
        }

        if (result.Success) delivery.MarkSent();
        else if (result.Skipped) delivery.Skip(result.Error ?? "Yuborilmadi.");
        else
        {
            delivery.RecordFailure(result.Error ?? "Noma'lum xato.");
            logger.LogWarning("{Channel} delivery {DeliveryId} failed (attempt {Attempt}): {Error}", delivery.Channel, delivery.Id, delivery.Attempts, delivery.Error);
        }

        await db.SaveChangesAsync(ct);
        return !delivery.IsPending;
    }

    /// <summary>SMS is paid for per message: once as many went out today (UTC day) as <c>Notifications:Sms:DailyLimit</c> allows, the rest are skipped.</summary>
    private async Task<bool> SmsAllowanceSpentAsync(CancellationToken ct)
    {
        var dayStart = new DateTimeOffset(clock.GetUtcNow().UtcDateTime.Date, TimeSpan.Zero);
        var sentToday = await db.NotificationDeliveries.CountAsync(
            d => d.Channel == NotificationChannel.Sms && d.Status == DeliveryStatus.Sent && d.SentAt >= dayStart, ct);
        return sentToday >= sms.CurrentValue.DailyLimit;
    }
}
