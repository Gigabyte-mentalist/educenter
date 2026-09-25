using Microsoft.Extensions.Caching.Memory;

namespace EduCenter.Api.Dashboard;

/// <summary>
/// Keeps a branch dashboard for a few minutes (<c>Dashboard:BranchCacheSeconds</c>, default 300; 0 turns it off). The screen is the
/// heaviest read in the product — exam results, every attendance mark, the whole lead funnel of a period — and it is looked at in
/// bursts: a manager opens a branch, flips through its tabs, changes the period back and forth. Nobody needs it to the second.
/// In memory, per instance: with more than one API instance each keeps its own copy (S09.07 moves this to Redis).
/// Only successful builds are kept; the caller's permissions are applied after the lookup, never baked into what is stored.
/// </summary>
public class BranchDashboardCache(IMemoryCache cache, IConfiguration configuration)
{
    /// <summary>How long a built dashboard is reused. Settable so a test can switch it on for the one test that is about caching.</summary>
    public TimeSpan Ttl { get; set; } = TimeSpan.FromSeconds(configuration.GetValue("Dashboard:BranchCacheSeconds", 300));

    public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> build) where T : class
    {
        if (Ttl <= TimeSpan.Zero) return await build();

        if (cache.TryGetValue(key, out T? kept) && kept is not null) return kept;
        var built = await build();
        cache.Set(key, built, Ttl);
        return built;
    }
}
