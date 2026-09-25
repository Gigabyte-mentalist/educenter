using Microsoft.Extensions.Caching.Memory;

namespace EduCenter.Api.Auth;

/// <summary>
/// Locks an <em>account</em> (not an address) after a run of wrong passwords. The per-IP limiter on <c>/api/auth/login</c>
/// stops one machine guessing quickly, but says nothing about a hundred machines each guessing once a minute at the same
/// administrator — on a host reachable from the internet that is the realistic attack. Keyed by the email as typed, whether
/// or not such an account exists, so a locked reply reveals nothing an unlocked one did not.
/// State is in memory: a restart clears every lock, which costs an attacker nothing they did not already have.
/// </summary>
public class LoginThrottle(IMemoryCache cache, IConfiguration configuration, TimeProvider clock)
{
    private readonly int _maxFailures = Math.Max(1, configuration.GetValue("RateLimiting:LoginMaxFailuresPerAccount", 5));
    private readonly TimeSpan _lockout = TimeSpan.FromMinutes(Math.Max(1, configuration.GetValue("RateLimiting:LoginLockoutMinutes", 15)));

    private sealed class Counter
    {
        public int Failures;
        public DateTimeOffset? LockedUntil;
    }

    /// <summary>True (with how long to wait) while the account is locked.</summary>
    public bool IsLocked(string email, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        if (!cache.TryGetValue(Key(email), out Counter? counter) || counter is null) return false;

        lock (counter)
        {
            if (counter.LockedUntil is not { } until || until <= clock.GetUtcNow()) return false;
            retryAfter = until - clock.GetUtcNow();
            return true;
        }
    }

    public void RecordFailure(string email)
    {
        var counter = cache.GetOrCreate(Key(email), entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _lockout; // failures older than the lockout period are forgiven
            return new Counter();
        })!;

        lock (counter)
        {
            // A lock that has run out starts a clean count — one more typo must not re-lock the account at once.
            if (counter.LockedUntil is { } expired && expired <= clock.GetUtcNow())
            {
                counter.Failures = 0;
                counter.LockedUntil = null;
            }

            counter.Failures++;
            if (counter.Failures < _maxFailures) return;

            counter.LockedUntil = clock.GetUtcNow().Add(_lockout);
            // Keep the entry alive for the whole lock, then let it go — the next attempt starts from zero.
            cache.Set(Key(email), counter, counter.LockedUntil.Value);
        }
    }

    public void RecordSuccess(string email) => cache.Remove(Key(email));

    private static string Key(string email) => $"login-failures:{email}";
}
