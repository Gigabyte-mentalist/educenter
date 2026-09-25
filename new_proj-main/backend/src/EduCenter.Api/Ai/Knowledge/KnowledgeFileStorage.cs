using System.Security.Cryptography;
using EduCenter.Api.Files;
using Microsoft.Extensions.Options;

namespace EduCenter.Api.Ai.Knowledge;

/// <summary>
/// Uploaded knowledge-base files on local disk (docs/00_TZ.md §38 step 1). Files are stored under a random key, never
/// under the name the uploader chose, and only the extensions the text extractor understands are accepted. A file is also looked at
/// before it is stored (<see cref="IFileGuard"/>): what it contains must be what its extension says, and it must be clean.
/// </summary>
public class KnowledgeFileStorage(IOptions<AiOptions> options, IHostEnvironment environment, IFileGuard guard)
{
    public static readonly IReadOnlyDictionary<string, string> AllowedExtensions = new Dictionary<string, string>
    {
        [".txt"] = "text/plain",
        [".md"] = "text/markdown",
        [".csv"] = "text/csv",
        [".pdf"] = "application/pdf",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation"
    };

    private readonly string _root = Path.IsPathRooted(options.Value.StoragePath)
        ? options.Value.StoragePath
        : Path.Combine(environment.ContentRootPath, options.Value.StoragePath);

    public long MaxBytes => options.Value.MaxUploadBytes;

    /// <returns>A user-facing problem, or null when the file may be stored — its type and size, then what it really contains and whether
    /// it is clean. <paramref name="content"/> is left rewound to its start.</returns>
    public async Task<string?> ValidateAsync(Stream content, string fileName, long sizeBytes, CancellationToken ct)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!AllowedExtensions.ContainsKey(extension))
            return $"Bu fayl turi qo'llab-quvvatlanmaydi. Ruxsat etilganlar: {string.Join(", ", AllowedExtensions.Keys)}.";
        if (sizeBytes <= 0) return "Fayl bo'sh.";
        if (sizeBytes > MaxBytes)
            return $"Fayl {MaxBytes / (1024 * 1024)} MB dan katta. Iltimos, uni Google Drive'ga yuklab, havolasini joylashtiring.";
        return await guard.CheckAsync(content, fileName, ct);
    }

    public static string ContentTypeFor(string fileName) =>
        AllowedExtensions.GetValueOrDefault(Path.GetExtension(fileName).ToLowerInvariant(), "application/octet-stream");

    public async Task<string> SaveAsync(Stream content, string fileName, CancellationToken ct)
    {
        Directory.CreateDirectory(_root);
        var key = $"{Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()}{Path.GetExtension(fileName).ToLowerInvariant()}";

        await using var file = File.Create(Path.Combine(_root, key));
        await content.CopyToAsync(file, ct);
        return key;
    }

    public bool Exists(string storageKey) => File.Exists(Path.Combine(_root, Path.GetFileName(storageKey)));

    public Stream? Open(string storageKey)
    {
        var path = Path.Combine(_root, Path.GetFileName(storageKey));
        return File.Exists(path) ? File.OpenRead(path) : null;
    }

    public void Delete(string storageKey)
    {
        var path = Path.Combine(_root, Path.GetFileName(storageKey));
        if (File.Exists(path)) File.Delete(path);
    }
}
