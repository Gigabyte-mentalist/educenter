namespace EduCenter.Api.Deployment;

/// <summary>
/// <c>EduCenter.Api --healthcheck</c>: asks the running server in this container for <c>/healthz</c> and exits 0 or 1.
/// The runtime image ships neither curl nor wget (every extra binary is more to patch and more for an intruder to
/// use), so Docker's HEALTHCHECK runs the app itself instead.
/// </summary>
public static class HealthProbe
{
    public static async Task<int> RunAsync()
    {
        var port = Environment.GetEnvironmentVariable("HEALTHCHECK_PORT") ?? "8080";
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            // AllowedHosts lists the public name plus "localhost" — the literal IP would be refused by host filtering.
            client.DefaultRequestHeaders.Host = "localhost";
            using var response = await client.GetAsync($"http://127.0.0.1:{port}/healthz");
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch
        {
            return 1;
        }
    }
}
