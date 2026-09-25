using System.Security.Cryptography;
using System.Text.RegularExpressions;
using EduCenter.Api.Files;
using Microsoft.Extensions.Options;

namespace EduCenter.Api.Lms;

public class HomeworkFileOptions
{
    public string StoragePath { get; set; } = "App_Data/homework";

    /// <summary>Twenty megabytes — twice the ceiling of the other uploads, because a spoken answer is an audio file. It is still a
    /// disk on the school's own machine: above it the answer is a Drive link, and the error message says exactly that.</summary>
    public long MaxBytes { get; set; } = 20 * 1024 * 1024;
}

/// <summary>
/// Homework files on local disk: the teacher's worksheet or audio, and the student's answer. Stored under a random key with the
/// original extension, never under the uploader's file name — a student's "../../uy vazifasi.docx" must not become a path on the server.
/// Nothing is stored before <see cref="IFileGuard"/> has looked at it — the content must be what the extension says, and the scanner must
/// not object.
/// </summary>
public partial class HomeworkFileStorage(IOptions<HomeworkFileOptions> options, IWebHostEnvironment environment, IFileGuard guard)
{
    /// <summary>What a homework actually comes as — documents, slides, sheets, photos of a notebook, a recording. No archives and no
    /// executables: nothing here needs to hand a teacher a .zip of unknown files to open.</summary>
    public static readonly IReadOnlyDictionary<string, string> AllowedTypes = new Dictionary<string, string>
    {
        [".pdf"] = "application/pdf",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".ppt"] = "application/vnd.ms-powerpoint",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".webp"] = "image/webp",
        [".mp3"] = "audio/mpeg",
        [".m4a"] = "audio/mp4",
        [".wav"] = "audio/wav",
        [".txt"] = "text/plain",
    };

    public long MaxBytes => options.Value.MaxBytes;
    public int MaxMegabytes => (int)(MaxBytes / (1024 * 1024));

    /// <returns>A user-facing problem, or null when the file may be stored — its type and size, then what it really contains and
    /// whether it is clean (<see cref="IFileGuard"/>).</returns>
    public async Task<string?> ValidateAsync(IFormFile file, CancellationToken ct)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedTypes.ContainsKey(extension))
            return $"Bu fayl turi qo'llab-quvvatlanmaydi. Ruxsat etilganlar: {string.Join(", ", AllowedTypes.Keys)}.";
        if (file.Length <= 0) return "Fayl bo'sh.";
        if (file.Length > MaxBytes)
            return $"Fayl {MaxMegabytes} MB dan katta. Iltimos, uni Google Drive'ga yuklab, havolasini shu yerga joylashtiring.";
        return await guard.CheckAsync(file, ct);
    }

    public async Task<(string Key, string FileName, string ContentType, long Size)> SaveAsync(IFormFile file, CancellationToken ct)
    {
        var root = Root;
        Directory.CreateDirectory(root);

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var key = $"{Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()}{extension}";

        await using (var target = File.Create(Path.Combine(root, key)))
            await file.CopyToAsync(target, ct);

        var name = Path.GetFileName(file.FileName);
        if (name.Length > 255) name = name[^255..];
        return (key, name, AllowedTypes[extension], file.Length);
    }

    /// <summary>Null when the key is malformed (never a path outside the storage root) or the file is gone.</summary>
    public string? PathFor(string key)
    {
        if (!KeyPattern().IsMatch(key)) return null;
        var path = Path.Combine(Root, key);
        return File.Exists(path) ? path : null;
    }

    public void Delete(string? key)
    {
        if (key is not null && PathFor(key) is { } path) File.Delete(path);
    }

    private string Root => Path.IsPathRooted(options.Value.StoragePath)
        ? options.Value.StoragePath
        : Path.Combine(environment.ContentRootPath, options.Value.StoragePath);

    [GeneratedRegex("^[0-9a-f]{32}\\.[a-z0-9]{1,5}$")]
    private static partial Regex KeyPattern();
}
