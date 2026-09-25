using System.Diagnostics;

namespace EduCenter.Api.Observability;

public sealed record RequestSample(DateTimeOffset At, string Method, string Route, int Status, int ElapsedMs);

public sealed record ServerError(
    DateTimeOffset At, string Method, string Path, int Status, int ElapsedMs, string? TraceId, string? ExceptionType, string? Message);

/// <summary>
/// The last few thousand API requests and the last server errors, in memory (docs/00_TZ.md §53: requests/min, p50/p95/p99,
/// error rate, endpoint performance). Deliberately not a database table: a metrics row per request would double the write
/// load of the very thing being measured. The cost is that it is per API instance and empty after a restart — the screen
/// says "collecting since" rather than pretending to have history.
/// </summary>
public sealed class RequestMetrics
{
    private const int MaxSamples = 20_000;
    private const int MaxErrors = 200;

    private readonly Queue<RequestSample> _samples = new();
    private readonly Queue<ServerError> _errors = new();
    private readonly object _gate = new();

    public DateTimeOffset CollectingSince { get; } = DateTimeOffset.UtcNow;

    public void Record(RequestSample sample, ServerError? error)
    {
        lock (_gate)
        {
            _samples.Enqueue(sample);
            while (_samples.Count > MaxSamples) _samples.Dequeue();

            if (error is null) return;
            _errors.Enqueue(error);
            while (_errors.Count > MaxErrors) _errors.Dequeue();
        }
    }

    public List<RequestSample> Since(DateTimeOffset since)
    {
        lock (_gate) return _samples.Where(s => s.At >= since).ToList();
    }

    /// <summary>Newest first.</summary>
    public List<ServerError> RecentErrors()
    {
        lock (_gate) return _errors.Reverse().ToList();
    }
}

public static class RequestMetricsMiddleware
{
    /// <summary>Times every /api request except the monitoring endpoints themselves — a dashboard that refreshes every
    /// 30 seconds would otherwise become the busiest "endpoint" on its own screen.</summary>
    public static IApplicationBuilder UseRequestMetrics(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path;
            if (!path.StartsWithSegments("/api") || path.StartsWithSegments("/api/system"))
            {
                await next();
                return;
            }

            var metrics = context.RequestServices.GetRequiredService<RequestMetrics>();
            var watch = Stopwatch.StartNew();
            Exception? failure = null;
            try
            {
                await next();
            }
            catch (Exception e)
            {
                failure = e;
                throw;
            }
            finally
            {
                watch.Stop();
                var status = failure is not null ? StatusCodes.Status500InternalServerError : context.Response.StatusCode;
                // The route template, not the path: "/api/students/{id}" is one endpoint, a thousand ids are not a thousand.
                var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? path.Value ?? "?";
                var elapsed = (int)watch.ElapsedMilliseconds;
                var now = DateTimeOffset.UtcNow;

                ServerError? error = status >= 500
                    ? new ServerError(now, context.Request.Method, path.Value ?? "", status, elapsed, Activity.Current?.Id ?? context.TraceIdentifier,
                        failure?.GetType().Name, failure is null ? null : FirstLine(failure.Message))
                    : null;
                metrics.Record(new RequestSample(now, context.Request.Method, route, status, elapsed), error);
            }
        });

    private static string FirstLine(string message)
    {
        var line = message.Split('\n', 2)[0].Trim();
        return line.Length <= 300 ? line : line[..300];
    }
}
