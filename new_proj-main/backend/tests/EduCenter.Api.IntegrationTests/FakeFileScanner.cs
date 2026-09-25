using System.Text;
using EduCenter.Api.Files;

namespace EduCenter.Api.IntegrationTests;

/// <summary>A test that only runs when a real ClamAV is named in <c>EDUCENTER_CLAMAV_HOST</c> — otherwise it is reported as skipped, with how to run it.</summary>
public sealed class LiveClamAvFactAttribute : FactAttribute
{
    public LiveClamAvFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("EDUCENTER_CLAMAV_HOST")))
            Skip = "Set EDUCENTER_CLAMAV_HOST (educenter-clamav, from `docker compose up -d clamav` in backend/) to run this against a real clamd.";
    }
}

/// <summary>A virus scanner without a daemon: the EICAR test string is "infected", everything else is clean. EICAR is the harmless
/// string every antivirus is built to flag — so an upload carrying it stands in for malware, without any real one in the repository.
/// The real ClamAV is never reachable from the suite; its protocol is tested against a stand-in daemon (<see cref="FileSecurityTests"/>).</summary>
public sealed class FakeFileScanner(bool available = true) : IFileScanner
{
    public const string Eicar = @"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*";
    public const string Signature = "Eicar-Test-Signature";

    private static readonly byte[] EicarBytes = Encoding.ASCII.GetBytes(Eicar);

    private int scanned;

    /// <summary>How many files were put in front of the scanner — a test can tell that an upload path really asks it.</summary>
    public int Scanned => Volatile.Read(ref scanned);

    public async Task<ScanResult> ScanAsync(Stream content, string fileName, CancellationToken ct)
    {
        Interlocked.Increment(ref scanned);
        if (!available) return ScanResult.Unavailable;

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        return buffer.GetBuffer().AsSpan(0, (int)buffer.Length).IndexOf(EicarBytes) >= 0 ? ScanResult.Infected(Signature) : ScanResult.Clean;
    }
}
