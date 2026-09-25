using System.Security.Claims;
using Microsoft.Extensions.Options;

namespace EduCenter.Api.Files;

/// <summary>
/// The gate every uploaded file passes before it is written anywhere (docs/00_TZ.md §38 steps 1–2). Each <c>*FileStorage</c> checks what
/// only it knows — its own size ceiling and extension list — and then asks the guard about what is common to all of them: is the content
/// what the extension says, and is it clean.
/// </summary>
public interface IFileGuard
{
    /// <returns>A user-facing problem, or null when the file may be stored. Leaves <paramref name="content"/> rewound to its start.</returns>
    Task<string?> CheckAsync(Stream content, string fileName, CancellationToken ct);
}

public static class FileGuardExtensions
{
    public static async Task<string?> CheckAsync(this IFileGuard guard, IFormFile file, CancellationToken ct)
    {
        await using var content = file.OpenReadStream();
        return await guard.CheckAsync(content, file.FileName, ct);
    }
}

/// <summary>
/// (1) The content matches the extension (<see cref="FileSignatures"/>, with the archive limits of <see cref="ZipLimits"/>), then (2) the
/// scanner looks at it. An infected file is refused, and so is one the scanner could not check when <see cref="FileScannerOptions.Required"/>
/// — the answer to "the scanner is down" is "not now", never "come in". A refused upload leaves a warning with the name, the person and
/// the signature; never the file's content.
/// </summary>
public sealed class FileGuard(
    IFileScanner scanner, IOptions<FileScannerOptions> options, IHttpContextAccessor http, ILogger<FileGuard> logger) : IFileGuard
{
    public const string Infected = "Fayl xavfli deb topildi va qabul qilinmadi.";
    public const string ScannerUnavailable = "Fayllarni xavfsizlik tekshiruvidan o'tkazib bo'lmadi. Birozdan keyin qayta urinib ko'ring.";

    public async Task<string?> CheckAsync(Stream content, string fileName, CancellationToken ct)
    {
        if (!content.CanSeek) return "Faylni o'qib bo'lmadi.";

        try
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            if (await FileSignatures.VerifyAsync(content, extension, ct) is { } mismatch)
            {
                logger.LogWarning("Upload {FileName} by {UserId} refused: {Reason}", Shown(fileName), UserId(), mismatch);
                return mismatch;
            }

            content.Position = 0;
            var scan = await scanner.ScanAsync(content, fileName, ct);
            switch (scan.Outcome)
            {
                case ScanOutcome.Infected:
                    logger.LogWarning("Upload {FileName} by {UserId} refused: malware {Signature}", Shown(fileName), UserId(), scan.Signature);
                    return Infected;

                case ScanOutcome.Unavailable when options.Value.Required:
                    logger.LogWarning("Upload {FileName} by {UserId} refused: no virus scan was possible and Files:Scanner:Required is on",
                        Shown(fileName), UserId());
                    return ScannerUnavailable;

                default:
                    return null;
            }
        }
        finally
        {
            content.Position = 0;
        }
    }

    private string? UserId() => http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);

    /// <summary>The name as the uploader typed it — kept short, and on one line, so it cannot bend a log entry.</summary>
    private static string Shown(string fileName)
    {
        var name = Path.GetFileName(fileName);
        name = string.Concat(name.Where(c => !char.IsControl(c)));
        return name.Length <= 100 ? name : name[..100];
    }
}
