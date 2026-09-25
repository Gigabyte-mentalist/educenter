using System.Security.Cryptography;
using System.Text.RegularExpressions;
using EduCenter.Api.Files;
using Microsoft.Extensions.Options;

namespace EduCenter.Api.Users;

public class TeacherCredentialFileOptions
{
    public string StoragePath { get; set; } = "App_Data/teacher-credentials";

    /// <summary>The same 10 MB ceiling as the other documents kept on the school's own disk.</summary>
    public long MaxBytes { get; set; } = 10 * 1024 * 1024;
}

/// <summary>
/// Scans of a teacher's certificates (docs/21.09-audit-jurnali S05.02), kept the way <c>StudentDocumentFileStorage</c> keeps a student's
/// paperwork: under a random key with the original extension, never under the uploader's name. A certificate scan can be a personal
/// document (a passport page in the same PDF), so it is only ever served to the teacher and the office — see the controller.
/// A PDF or a raster image only: no SVG (a document that can carry script, served from the application's own origin), no Office
/// files, no archives.
/// </summary>
public partial class TeacherCredentialFileStorage(IOptions<TeacherCredentialFileOptions> options, IWebHostEnvironment environment, IFileGuard guard)
{
    public static readonly IReadOnlyDictionary<string, string> AllowedTypes = new Dictionary<string, string>
    {
        [".pdf"] = "application/pdf",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".webp"] = "image/webp",
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
        if (file.Length > MaxBytes) return $"Fayl {MaxMegabytes} MB dan katta.";
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
