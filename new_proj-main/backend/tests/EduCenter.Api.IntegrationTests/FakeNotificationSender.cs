using System.Collections.Concurrent;
using EduCenter.Api.Notifications.Delivery;
using EduCenter.Modules.Notifications.Domain;

namespace EduCenter.Api.IntegrationTests;

/// <summary>An outside channel that sends nothing: it remembers what it was asked to send, and answers the way a test tells it to.
/// Disabled until a test enables it (a disabled channel gets no delivery rows at all), and put back by <see cref="Reset"/>.</summary>
public sealed class FakeNotificationSender(NotificationChannel channel) : INotificationSender
{
    private int calls;

    public NotificationChannel Channel => channel;

    public bool IsEnabled { get; set; }

    /// <summary>What the channel answers for one recipient. It may throw, as a mail server that is down does.</summary>
    public Func<NotificationTarget, SendResult> Behaviour { get; set; } = _ => SendResult.Ok();

    public ConcurrentQueue<(NotificationTarget Target, NotificationEnvelope Message)> Sent { get; } = new();

    /// <summary>Every attempt, successful or not.</summary>
    public int Calls => calls;

    public Task<SendResult> SendAsync(NotificationTarget target, NotificationEnvelope message, CancellationToken ct)
    {
        Interlocked.Increment(ref calls);
        var result = Behaviour(target);
        if (result.Success) Sent.Enqueue((target, message));
        return Task.FromResult(result);
    }

    public void Reset()
    {
        IsEnabled = false;
        Behaviour = _ => SendResult.Ok();
        Sent.Clear();
        Interlocked.Exchange(ref calls, 0);
    }
}
