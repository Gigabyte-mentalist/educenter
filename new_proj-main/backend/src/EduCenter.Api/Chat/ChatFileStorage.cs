using System.Text.RegularExpressions;
using EduCenter.Api.Files;
using EduCenter.Modules.Chat.Domain;
using Microsoft.Extensions.Options;

namespace EduCenter.Api.Chat;

public class ChatFileOptions
{
    /// <summary>Relative paths resolve against the API's content root.</summary>
    public string StoragePath { get; set; } = "App_Data/chat-files";

    public long MaxBytes { get; set; } = 10 * 1024 * 1024;
}

/// <summary>
/// Chat attachments on local disk (docs/00_TZ.md §22 "file", "image"). Files are stored under a random key, never the
/// uploaded name, and served only through the chat's access check. The content type comes from the extension
/// allow-list, not from the client.
/// </summary>
public partial class ChatFileStorage(IOptions<ChatFileOptions> options, IWebHostEnvironment environment, IFileGuard guard)
{
    private static readonly Dictionary<string, string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png", [".gif"] = "image/gif", [".webp"] = "image/webp",
        [".pdf"] = "application/pdf", [".txt"] = "text/plain", [".zip"] = "application/zip",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".ppt"] = "application/vnd.ms-powerpoint",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".mp3"] = "audio/mpeg", [".m4a"] = "audio/mp4",
    };

    public long MaxBytes => options.Value.MaxBytes;

    /// <returns>A user-facing problem, or null when the file may be stored — its size and type, then what it really contains and whether
    /// it is clean (<see cref="IFileGuard"/>).</returns>
    public async Task<string?> ValidateAsync(IFormFile file, CancellationToken ct)
    {
        if (file.Length == 0) return "Fayl bo'sh.";
        if (file.Length > MaxBytes) return $"Fayl hajmi {MaxBytes / (1024 * 1024)} MB dan oshmasligi kerak.";
        if (!AllowedTypes.ContainsKey(Path.GetExtension(file.FileName)))
            return "Bu turdagi faylni yuborib bo'lmaydi. Rasm, PDF, Office hujjati, matn, ZIP yoki audio yuboring.";
        return await guard.CheckAsync(file, ct);
    }

    public async Task<ChatAttachment> SaveAsync(IFormFile file, CancellationToken ct)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var key = $"{Guid.NewGuid():N}{extension}";

        Directory.CreateDirectory(Root);
        await using (var stream = File.Create(Path.Combine(Root, key)))
            await file.CopyToAsync(stream, ct);

        var name = Path.GetFileName(file.FileName);
        if (name.Length > 255) name = name[^255..];
        return new ChatAttachment(name, AllowedTypes[extension], file.Length, key);
    }

    /// <summary>Null when the key is malformed (never a path outside the storage root) or the file is gone.</summary>
    public string? PathFor(string key)
    {
        if (!KeyPattern().IsMatch(key)) return null;
        var path = Path.Combine(Root, key);
        return File.Exists(path) ? path : null;
    }

    public static bool IsImage(string contentType) => contentType.StartsWith("image/", StringComparison.Ordinal);

    /// <summary>Removes the stored file for a key. Silent when the key is malformed or the file is already
    /// gone: this runs while a conversation is being deleted, and a missing file must not fail that.</summary>
    public void Delete(string key)
    {
        if (PathFor(key) is { } path) File.Delete(path);
    }

    private string Root => Path.IsPathRooted(options.Value.StoragePath)
        ? options.Value.StoragePath
        : Path.Combine(environment.ContentRootPath, options.Value.StoragePath);

    [GeneratedRegex("^[0-9a-f]{32}\\.[a-z0-9]{1,5}$")]
    private static partial Regex KeyPattern();
}
