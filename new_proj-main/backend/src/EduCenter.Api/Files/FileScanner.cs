using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;

namespace EduCenter.Api.Files;

public enum ScanOutcome
{
    Clean,
    Infected,

    /// <summary>The scanner could not give an answer — not installed, unreachable, timed out, or refused the file. Not the same as clean.</summary>
    Unavailable
}

/// <param name="Signature">The name of the malware signature that matched; only set when <see cref="ScanOutcome.Infected"/>.</param>
public sealed record ScanResult(ScanOutcome Outcome, string? Signature = null)
{
    public static readonly ScanResult Clean = new(ScanOutcome.Clean);
    public static readonly ScanResult Unavailable = new(ScanOutcome.Unavailable);

    public static ScanResult Infected(string signature) => new(ScanOutcome.Infected, signature);
}

/// <summary>Malware scan of an uploaded file (docs/00_TZ.md §38 step 2). Only <see cref="IFileGuard"/> and the knowledge ingestor call it.</summary>
public interface IFileScanner
{
    /// <param name="content">Read from its current position to the end.</param>
    Task<ScanResult> ScanAsync(Stream content, string fileName, CancellationToken ct);
}

/// <summary>Section <c>Files:Scanner</c>.</summary>
public class FileScannerOptions
{
    public const string Section = "Files:Scanner";

    /// <summary><c>none</c> (no scanner at all) or <c>clamav</c> (a clamd daemon).</summary>
    public string Provider { get; set; } = "none";

    /// <summary>
    /// Whether an upload the scanner could not check is refused. <b>True unless a developer machine says otherwise</b>
    /// (<c>appsettings.Development.json</c>): with no scanner, or with clamd down, a production host takes no uploads at all rather
    /// than storing files nobody looked at. Fail-closed on purpose — a scanner outage is a reason to wait, not to let files in.
    /// </summary>
    public bool Required { get; set; } = true;

    public string Host { get; set; } = "";
    public int Port { get; set; } = 3310;
    public int TimeoutSeconds { get; set; } = 30;

    public bool UsesClamAv => string.Equals(Provider, "clamav", StringComparison.OrdinalIgnoreCase);
}

/// <summary>No scanner configured. Never says "clean": every file is <see cref="ScanOutcome.Unavailable"/>, and whether that lets it in is
/// <see cref="FileScannerOptions.Required"/> — true everywhere but a developer machine.</summary>
public sealed class NoopFileScanner : IFileScanner
{
    public Task<ScanResult> ScanAsync(Stream content, string fileName, CancellationToken ct) => Task.FromResult(ScanResult.Unavailable);
}

/// <summary>
/// ClamAV over clamd's TCP socket, the <c>INSTREAM</c> command: the file goes across in length-prefixed chunks and the daemon answers
/// <c>stream: OK</c> or <c>stream: &lt;signature&gt; FOUND</c>. The file is streamed, never held whole. Anything that goes wrong — the
/// daemon is down, slow, or refuses the stream (its size limit) — is <see cref="ScanOutcome.Unavailable"/>, never "clean".
/// </summary>
public sealed class ClamAvScanner(FileScannerOptions options, ILogger<ClamAvScanner> logger) : IFileScanner
{
    private const int ChunkSize = 64 * 1024;
    private const int MaxReplyBytes = 1024;

    public async Task<ScanResult> ScanAsync(Stream content, string fileName, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(options.Host, options.Port, timeout.Token);
            await using var network = client.GetStream();

            // "z" commands are NUL-terminated on both sides, which keeps a reply from being confused with a line of a longer one.
            await network.WriteAsync("zINSTREAM\0"u8.ToArray(), timeout.Token);

            var buffer = new byte[ChunkSize];
            var length = new byte[4];
            int read;
            while ((read = await content.ReadAsync(buffer, timeout.Token)) > 0)
            {
                BinaryPrimitives.WriteInt32BigEndian(length, read);
                await network.WriteAsync(length, timeout.Token);
                await network.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
            }

            BinaryPrimitives.WriteInt32BigEndian(length, 0); // a zero-length chunk ends the stream
            await network.WriteAsync(length, timeout.Token);
            await network.FlushAsync(timeout.Token);

            return Interpret(await ReadReplyAsync(network, timeout.Token));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // the caller gave up — not the scanner's failure
        }
        catch (Exception e) when (e is SocketException or IOException or OperationCanceledException or ObjectDisposedException)
        {
            logger.LogWarning("ClamAV at {Host}:{Port} did not answer: {Reason}", options.Host, options.Port, e.Message);
            return ScanResult.Unavailable;
        }
    }

    private static async Task<string> ReadReplyAsync(Stream network, CancellationToken ct)
    {
        var reply = new List<byte>();
        var single = new byte[1];
        while (reply.Count < MaxReplyBytes && await network.ReadAsync(single, ct) == 1 && single[0] != 0)
            reply.Add(single[0]);
        return Encoding.UTF8.GetString([.. reply]).Trim();
    }

    private ScanResult Interpret(string reply)
    {
        if (reply.EndsWith("OK", StringComparison.Ordinal)) return ScanResult.Clean;

        if (reply.EndsWith("FOUND", StringComparison.Ordinal))
        {
            var start = reply.IndexOf(':') + 1;
            var signature = reply[start..^"FOUND".Length].Trim();
            return ScanResult.Infected(signature.Length > 0 ? signature : "unknown");
        }

        // "INSTREAM size limit exceeded. ERROR", "Can't allocate memory. ERROR", or nothing at all.
        logger.LogWarning("ClamAV refused to scan a file: {Reply}", reply.Length > 0 ? reply : "(empty reply)");
        return ScanResult.Unavailable;
    }
}
