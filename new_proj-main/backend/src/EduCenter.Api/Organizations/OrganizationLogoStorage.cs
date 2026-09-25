using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using EduCenter.Api.Files;
using Microsoft.Extensions.Options;

namespace EduCenter.Api.Organizations;

public class OrganizationLogoOptions
{
    public string StoragePath { get; set; } = "App_Data/org-logo";

    /// <summary>A logo is small; 1 MB is generous for a PNG at any size the sidebar can show.</summary>
    public long MaxBytes { get; set; } = 1024 * 1024;
}

/// <summary>
/// An organization's logo on local disk, kept the way profile pictures are (<c>UserPhotoStorage</c>): under a random key with the
/// original extension, never the uploader's file name. Only raster images — <b>not SVG</b>: an SVG is a document that can carry
/// script, and this one is served from the application's own origin.
/// </summary>
public partial class OrganizationLogoStorage(IOptions<OrganizationLogoOptions> options, IWebHostEnvironment environment, IFileGuard guard)
{
    public static readonly IReadOnlyDictionary<string, string> AllowedTypes = new Dictionary<string, string>
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".webp"] = "image/webp",
    };

    public long MaxBytes => options.Value.MaxBytes;
    public int MaxMegabytes => (int)(MaxBytes / (1024 * 1024));

    /// <returns>A user-facing problem, or null when the logo may be stored — its type and size, then that it really is that kind of
    /// image and is clean (<see cref="IFileGuard"/>).</returns>
    public async Task<string?> ValidateAsync(IFormFile file, CancellationToken ct)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedTypes.ContainsKey(extension))
            return $"Logo faqat rasm bo'lishi kerak: {string.Join(", ", AllowedTypes.Keys)} (SVG qabul qilinmaydi).";
        if (file.Length <= 0) return "Fayl bo'sh.";
        if (file.Length > MaxBytes) return $"Logo {MaxMegabytes} MB dan katta.";
        return await guard.CheckAsync(file, ct);
    }

    public async Task<string> SaveAsync(IFormFile file, CancellationToken ct)
    {
        var root = Root;
        Directory.CreateDirectory(root);

        var key = $"{Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()}{Path.GetExtension(file.FileName).ToLowerInvariant()}";
        await using var target = File.Create(Path.Combine(root, key));
        await file.CopyToAsync(target, ct);
        return key;
    }

    /// <summary>Null when the key is malformed (never a path outside the storage root) or the file is gone.</summary>
    public string? PathFor(string key)
    {
        if (!KeyPattern().IsMatch(key)) return null;
        var path = Path.Combine(Root, key);
        return File.Exists(path) ? path : null;
    }

    public static string ContentTypeFor(string key) => AllowedTypes.GetValueOrDefault(Path.GetExtension(key).ToLowerInvariant(), "application/octet-stream");

    public void Delete(string key)
    {
        if (PathFor(key) is { } path) File.Delete(path);
    }

    /// <summary>A short token that changes whenever the logo does, so a client that caches it asks again after an upload.</summary>
    public static string VersionOf(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..8].ToLowerInvariant();

    private string Root => Path.IsPathRooted(options.Value.StoragePath)
        ? options.Value.StoragePath
        : Path.Combine(environment.ContentRootPath, options.Value.StoragePath);

    [GeneratedRegex("^[0-9a-f]{32}\\.[a-z0-9]{1,5}$")]
    private static partial Regex KeyPattern();
}
