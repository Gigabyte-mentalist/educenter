using EduCenter.Infrastructure;
using EduCenter.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace EduCenter.Api.Auth;

/// <summary>The two things every authenticated request must know about the caller's account before anything
/// else runs — whether it still works at all, and whether it may only be used to change a temporary password.</summary>
public sealed record AccountState(UserStatus Status, bool MustChangePassword);

/// <summary>
/// A short-lived cache of <see cref="AccountState"/>, read on nearly every authenticated request
/// (<see cref="AccountStatusMiddleware"/>) — a database round trip per request just to check "still active" would
/// add real latency for no benefit. 30 seconds is how stale a block/unblock can be on this single API instance;
/// <see cref="AccountBlocker"/> evicts immediately on a status change, so in practice it takes effect at once. A
/// missing user (deleted) caches as null rather than re-querying every time.
/// </summary>
public sealed class UserStatusCache(IMemoryCache cache, IServiceScopeFactory scopes)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    public Task<AccountState?> GetAsync(Guid userId, CancellationToken ct = default) =>
        cache.GetOrCreateAsync(Key(userId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = Ttl;
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Users.Where(u => u.Id == userId)
                .Select(u => (AccountState?)new AccountState(u.Status, u.MustChangePassword))
                .FirstOrDefaultAsync(ct);
        });

    public void Evict(Guid userId) => cache.Remove(Key(userId));

    private static string Key(Guid userId) => $"user-status:{userId}";
}
