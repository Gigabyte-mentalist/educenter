using System.IO.Compression;
using System.Text.Unicode;

namespace EduCenter.Api.Files;

/// <summary>
/// What a file says it is, read from its own first bytes — not from the name the uploader gave it. Every upload path checks an extension
/// allow-list, and an extension is a label anyone can write: an executable renamed <c>report.pdf</c> passes that check and is then served
/// back to whoever opens it. A file is accepted only when its content is the kind its extension claims.
/// </summary>
public static class FileSignatures
{
    public const string ContentMismatch = "Fayl mazmuni kengaytmasiga mos emas.";

    /// <summary>The most of a file looked at to tell it: enough for every signature below and for a stretch of text.</summary>
    private const int HeaderBytes = 4096;

    /// <summary>Open-document formats are zip archives: their parts are unpacked, so they get the same limits as any archive.</summary>
    private static readonly HashSet<string> OfficeDocuments = new(StringComparer.Ordinal) { ".docx", ".xlsx", ".pptx" };

    private static readonly HashSet<string> TextFiles = new(StringComparer.Ordinal) { ".txt", ".md", ".csv" };

    /// <returns>A user-facing problem — the content is not what the extension says, or it is an archive built to blow up when unpacked —
    /// or null when the file may go on to the scanner. An extension with no known signature never passes: adding an upload type means
    /// adding its signature here, which is the point.</returns>
    public static async Task<string?> VerifyAsync(Stream content, string extension, CancellationToken ct)
    {
        extension = extension.ToLowerInvariant();

        content.Position = 0;
        var header = new byte[HeaderBytes];
        var read = 0;
        while (read < header.Length)
        {
            var n = await content.ReadAsync(header.AsMemory(read), ct);
            if (n == 0) break;
            read += n;
        }

        var truncated = read == HeaderBytes && content.Length > HeaderBytes;
        if (!HasSignature(header.AsSpan(0, read), extension, truncated)) return ContentMismatch;

        content.Position = 0;
        return OfficeDocuments.Contains(extension) || extension == ".zip" ? ZipLimits.Inspect(content, OfficeDocuments.Contains(extension)) : null;
    }

    /// <param name="truncated">The span is only the start of a longer file — a text sample may end in the middle of a character.</param>
    public static bool HasSignature(ReadOnlySpan<byte> header, string extension, bool truncated = false) => extension switch
    {
        ".pdf" => header.StartsWith("%PDF"u8),
        ".png" => header.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47 }),
        ".jpg" or ".jpeg" => header.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }),
        ".gif" => header.StartsWith("GIF87a"u8) || header.StartsWith("GIF89a"u8),
        ".webp" => IsRiff(header, "WEBP"u8),
        ".wav" => IsRiff(header, "WAVE"u8),
        ".mp3" => header.StartsWith("ID3"u8) || (header.Length >= 2 && header[0] == 0xFF && (header[1] & 0xE0) == 0xE0),
        ".m4a" or ".mp4" => header.Length >= 8 && header[4..8].SequenceEqual("ftyp"u8),
        ".webm" => header.StartsWith(new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }),
        ".ogg" => header.StartsWith("OggS"u8),

        // The old binary Office formats are all OLE2 compound files.
        ".doc" or ".xls" or ".ppt" => header.StartsWith(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }),

        // A zip starts with a local file header; an empty one with the end-of-directory record.
        ".zip" or ".docx" or ".xlsx" or ".pptx" =>
            header.StartsWith(new byte[] { 0x50, 0x4B, 0x03, 0x04 }) || (extension == ".zip" && header.StartsWith(new byte[] { 0x50, 0x4B, 0x05, 0x06 })),

        _ when TextFiles.Contains(extension) => IsUtf8Text(header, truncated),
        _ => false
    };

    private static bool IsRiff(ReadOnlySpan<byte> header, ReadOnlySpan<byte> form) =>
        header.Length >= 12 && header.StartsWith("RIFF"u8) && header[8..12].SequenceEqual(form);

    /// <summary>UTF-8 with nothing a person would not type: a NUL or another control character means a binary file wearing a .txt.</summary>
    private static bool IsUtf8Text(ReadOnlySpan<byte> sample, bool truncated)
    {
        foreach (var b in sample)
            if (b < 0x20 && b is not (0x09 or 0x0A or 0x0D or 0x0C))
                return false;

        var decoded = new char[sample.Length];
        var status = Utf8.ToUtf16(sample, decoded, out _, out _, replaceInvalidSequences: false, isFinalBlock: !truncated);
        return status is System.Buffers.OperationStatus.Done or System.Buffers.OperationStatus.NeedMoreData;
    }
}

/// <summary>
/// Limits on unpacking an archive (docs/21.09-audit-jurnali S08.03): a zip can be a few kilobytes that expands to gigabytes, or hold a
/// million tiny files. An archive — a .zip, or a .docx/.pptx/.xlsx, which are archives — is refused above these before anything opens it.
/// </summary>
public static class ZipLimits
{
    public const int MaxEntries = 1000;
    public const long MaxUncompressedBytes = 50L * 1024 * 1024;

    public const string TooLarge =
        "Arxiv juda ko'p yoki juda katta fayllardan iborat (ko'pi bilan 1000 ta fayl, ochilgandan keyin 50 MB).";

    /// <param name="officeDocument">A .docx/.pptx/.xlsx is also required to carry its <c>[Content_Types].xml</c>, the part every such file has.</param>
    /// <returns>A user-facing problem, or null when the archive is within the limits.</returns>
    public static string? Inspect(Stream archive, bool officeDocument)
    {
        try
        {
            archive.Position = 0;
            using var zip = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true);

            if (zip.Entries.Count > MaxEntries) return TooLarge;

            long total = 0;
            foreach (var entry in zip.Entries)
            {
                total += entry.Length;
                if (total > MaxUncompressedBytes) return TooLarge;
            }

            return officeDocument && zip.GetEntry("[Content_Types].xml") is null ? FileSignatures.ContentMismatch : null;
        }
        catch (InvalidDataException)
        {
            return FileSignatures.ContentMismatch;
        }
        finally
        {
            archive.Position = 0;
        }
    }
}
