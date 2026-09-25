using EduCenter.Modules.Notifications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EduCenter.Api.Notifications.Delivery;

/// <summary>
/// Hands new <see cref="NotificationDelivery"/> rows to the queue <b>after</b> the save that wrote them has committed. The dispatcher only adds rows —
/// the caller's SaveChanges commits them together with the change that caused them — so this is the one place that knows the write went through.
/// Queueing before the commit would let a job look for a row that does not exist yet; queueing for a save that failed would announce nothing real.
/// If the queue itself is unavailable the rows stay Pending and the inbox message is unaffected.
/// </summary>
public sealed class NotificationDeliveryInterceptor(IServiceProvider services, ILogger<NotificationDeliveryInterceptor> logger) : SaveChangesInterceptor
{
    // Resolved only when there is something to queue: every DbContext gets this interceptor, and most never write a delivery — so opening
    // one (the seeder, a migration check) must not start the background-job client.
    private List<Guid> pending = [];

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Remember(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Remember(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        var ids = Take();
        if (ids.Count > 0) EnqueueAsync(ids).GetAwaiter().GetResult();
        return base.SavedChanges(eventData, result);
    }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        var ids = Take();
        if (ids.Count > 0) await EnqueueAsync(ids);
        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => pending = [];

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        pending = [];
        return Task.CompletedTask;
    }

    private void Remember(DbContext? context)
    {
        if (context is null) return;
        pending = context.ChangeTracker.Entries<NotificationDelivery>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity.Id)
            .ToList();
    }

    private List<Guid> Take()
    {
        var ids = pending;
        pending = [];
        return ids;
    }

    private async Task EnqueueAsync(List<Guid> ids)
    {
        try
        {
            await services.GetRequiredService<INotificationDeliveryQueue>().EnqueueAsync(ids);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{Count} notification deliveries were saved but could not be queued; they stay Pending.", ids.Count);
        }
    }
}
