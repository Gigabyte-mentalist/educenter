using System.Security.Cryptography;
using System.Text;
using EduCenter.Api.Ai.Providers;
using EduCenter.Api.Files;
using Microsoft.Extensions.Options;

namespace EduCenter.Api.Ai.Speaking;

/// <summary>Speaking recordings on local disk, stored under a random key like chat attachments and knowledge files.
/// Object storage (S3/MinIO) is the production step, together with a retention rule for student audio.</summary>
public class SpeakingAudioStorage(IOptions<AiOptions> options, IHostEnvironment environment, IFileGuard guard)
{
    public const long MaxBytes = 10 * 1024 * 1024; // the shared upload ceiling; ~40 minutes of speech at 32 kbps

    /// <summary>What browsers actually produce from MediaRecorder, plus the common phone formats.</summary>
    public static readonly IReadOnlyDictionary<string, string> AllowedExtensions = new Dictionary<string, string>
    {
        [".webm"] = "audio/webm",
        [".ogg"] = "audio/ogg",
        [".m4a"] = "audio/mp4",
        [".mp4"] = "audio/mp4",
        [".mp3"] = "audio/mpeg",
        [".wav"] = "audio/wav"
    };

    private readonly string _root = Path.IsPathRooted(options.Value.SpeakingStoragePath)
        ? options.Value.SpeakingStoragePath
        : Path.Combine(environment.ContentRootPath, options.Value.SpeakingStoragePath);

    /// <returns>A user-facing problem, or null when the recording may be stored — its format and size, then that it really is that kind
    /// of audio and is clean. <paramref name="content"/> is left rewound to its start.</returns>
    public async Task<string?> ValidateAsync(Stream content, string fileName, long sizeBytes, CancellationToken ct)
    {
        if (!AllowedExtensions.ContainsKey(Path.GetExtension(fileName).ToLowerInvariant()))
            return $"Bu audio formati qo'llab-quvvatlanmaydi. Ruxsat etilganlar: {string.Join(", ", AllowedExtensions.Keys)}.";
        if (sizeBytes <= 0) return "Yozuv bo'sh.";
        if (sizeBytes > MaxBytes) return $"Yozuv juda katta (ko'pi bilan {MaxBytes / (1024 * 1024)} MB).";
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

    public Stream? Open(string audioKey)
    {
        var path = Path.Combine(_root, Path.GetFileName(audioKey));
        return File.Exists(path) ? File.OpenRead(path) : null;
    }

    /// <summary>The containers a synthesized question is kept in — by content type, because the cache is found by its key alone and
    /// the extension is what says how to play the file back. A provider answering with anything else is served, but not cached.</summary>
    private static readonly IReadOnlyDictionary<string, string> SynthesizedExtensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["audio/mpeg"] = ".mp3",
        ["audio/mp3"] = ".mp3",
        ["audio/wav"] = ".wav",
        ["audio/x-wav"] = ".wav",
        ["audio/wave"] = ".wav",
        ["audio/ogg"] = ".ogg"
    };

    /// <summary>What names a synthesized question: the question and the voice. The same words in the same voice are the same recording —
    /// for every student and every session — so an examiner's stock question is spoken by the provider once, not once per practice.</summary>
    public static string QuestionAudioKey(string question, string voice) =>
        "q-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{voice}\n{question}"))).ToLowerInvariant()[..32];

    /// <returns>The cached recording of this question in this voice, or null when it has not been synthesized yet.</returns>
    public async Task<SpeechResult?> FindQuestionAudioAsync(string question, string voice, CancellationToken ct)
    {
        if (!Directory.Exists(_root)) return null;

        var key = QuestionAudioKey(question, voice);
        var path = Directory.EnumerateFiles(_root, $"{key}.*").FirstOrDefault(p => !p.EndsWith(".tmp", StringComparison.Ordinal));
        return path is null ? null : new SpeechResult(await File.ReadAllBytesAsync(path, ct), ContentTypeFor(path), voice);
    }

    public async Task SaveQuestionAudioAsync(string question, SpeechResult speech, CancellationToken ct)
    {
        if (!SynthesizedExtensions.TryGetValue(speech.ContentType, out var extension)) return;

        Directory.CreateDirectory(_root);
        // Written under a temporary name and renamed, so a request that finds the key never reads half a recording.
        var path = Path.Combine(_root, QuestionAudioKey(question, speech.Voice) + extension);
        var temporary = $"{path}.{Convert.ToHexString(RandomNumberGenerator.GetBytes(4))}.tmp";
        await File.WriteAllBytesAsync(temporary, speech.Audio, ct);
        File.Move(temporary, path, overwrite: true);
    }

    public void Delete(string audioKey)
    {
        var path = Path.Combine(_root, Path.GetFileName(audioKey));
        if (File.Exists(path)) File.Delete(path);
    }
}
