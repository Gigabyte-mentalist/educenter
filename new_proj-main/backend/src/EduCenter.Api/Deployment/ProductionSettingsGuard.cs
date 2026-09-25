namespace EduCenter.Api.Deployment;

/// <summary>
/// Refuses to start a non-Development host on settings that are only acceptable on a developer machine.
/// <c>appsettings.json</c> ships development defaults (a committed JWT signing key, the dev database password,
/// <c>AllowedHosts: *</c>) so that <c>dotnet run</c> works out of the box; on a host reachable from the internet each of
/// them is a hole — anyone who can read the repository could forge a token. Failing loudly at startup is the only
/// way to be sure an override was not forgotten.
/// </summary>
public static class ProductionSettingsGuard
{
    public const int MinSigningKeyLength = 32;

    /// <summary>Every unsafe setting found, one sentence each; empty when the configuration is fit to face the internet.</summary>
    public static IReadOnlyList<string> Problems(IConfiguration configuration)
    {
        var problems = new List<string>();

        var signingKey = configuration["Jwt:SigningKey"];
        if (string.IsNullOrWhiteSpace(signingKey) || signingKey.Length < MinSigningKeyLength)
            problems.Add($"Jwt:SigningKey must be at least {MinSigningKeyLength} characters (set Jwt__SigningKey).");
        else if (signingKey.StartsWith("dev-only", StringComparison.OrdinalIgnoreCase))
            problems.Add("Jwt:SigningKey is the development key from the repository — anyone can forge a token with it (set Jwt__SigningKey).");

        var connection = configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(connection))
            problems.Add("ConnectionStrings:Postgres is not configured.");
        else if (connection.Contains("educenter_dev_only", StringComparison.Ordinal))
            problems.Add("ConnectionStrings:Postgres still uses the development database password.");

        var allowedHosts = configuration["AllowedHosts"];
        if (string.IsNullOrWhiteSpace(allowedHosts) || allowedHosts.Trim() == "*")
            problems.Add("AllowedHosts must name the public host(s) instead of '*' (set AllowedHosts).");

        if (configuration.GetValue<bool>("Ai:AllowFakeProvider"))
            problems.Add("Ai:AllowFakeProvider must be false — the fake provider returns canned answers.");

        // A demo deployment is every bit as reachable as a real one: its accounts must not use the published password.
        if (string.Equals(configuration["Bootstrap:Mode"], nameof(BootstrapMode.Demo), StringComparison.OrdinalIgnoreCase)
            && ProductionBootstrapper.ValidateDemoPassword(configuration["Bootstrap:DemoPassword"]) is { } demoProblem)
            problems.Add($"{demoProblem} (set Bootstrap__DemoPassword).");

        // Uploaded files are scanned before they are stored (S08.03). Switching that off, or naming a scanner that cannot be reached, would
        // let unchecked files in on a host anyone can reach.
        if (configuration.GetValue<bool?>("Files:Scanner:Required") == false)
            problems.Add("Files:Scanner:Required must be true — files would be stored without a virus scan (set Files__Scanner__Required=true).");

        var scannerProvider = configuration["Files:Scanner:Provider"];
        if (!string.IsNullOrWhiteSpace(scannerProvider) && !scannerProvider.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            if (!scannerProvider.Equals("clamav", StringComparison.OrdinalIgnoreCase))
                problems.Add($"Files:Scanner:Provider '{scannerProvider}' is not known — use 'clamav' or 'none'.");
            else if (string.IsNullOrWhiteSpace(configuration["Files:Scanner:Host"]))
                problems.Add("Files:Scanner:Host must name the ClamAV daemon (set Files__Scanner__Host).");
        }

        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        if (origins.Any(o => o.Trim() == "*"))
            problems.Add("Cors:AllowedOrigins must list explicit origins, not '*'.");

        return problems;
    }

    /// <exception cref="InvalidOperationException">Listing every problem at once, so one restart fixes them all.</exception>
    public static void EnsureSafe(IConfiguration configuration)
    {
        var problems = Problems(configuration);
        if (problems.Count > 0)
            throw new InvalidOperationException(
                "Refusing to start with unsafe settings outside Development:" +
                string.Concat(problems.Select(p => $"{Environment.NewLine} - {p}")));
    }
}
