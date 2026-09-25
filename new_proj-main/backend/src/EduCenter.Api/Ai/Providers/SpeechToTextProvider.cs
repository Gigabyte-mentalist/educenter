using System.Net.Http.Headers;
using System.Text.Json;

namespace EduCenter.Api.Ai.Providers;

/// <param name="Text">The full transcript.</param>
/// <param name="Segments">Start/end seconds of each recognised segment — the basis for pace and pause metrics.</param>
public sealed record TranscriptionResult(string Text, IReadOnlyList<TranscriptSegment> Segments);

public sealed record TranscriptSegment(double Start, double End, string Text);

/// <summary>Speech to text (docs/00_TZ.md §34 ISTTProvider). Whisper runs locally; audio never leaves the network.</summary>
public interface ISpeechToTextProvider
{
    string Name { get; }
    Task<TranscriptionResult> TranscribeAsync(Stream audio, string fileName, string contentType, string language, CancellationToken ct);
}

/// <summary>
/// The whisper-asr-webservice API (the image this project runs locally): POST /asr with the audio as multipart, and a
/// JSON body of segments back. Word timestamps are off — segment timings are enough for pace and pauses, and asking for
/// word-level detail costs noticeably more time on a small GPU.
/// </summary>
public sealed class WhisperAsrProvider(string name, AiProviderOptions options, IHttpClientFactory httpClientFactory) : ISpeechToTextProvider
{
    public string Name => name;

    public async Task<TranscriptionResult> TranscribeAsync(Stream audio, string fileName, string contentType, string language,
        CancellationToken ct)
    {
        var url = $"{options.BaseUrl!.TrimEnd('/')}/asr?task=transcribe&language={Uri.EscapeDataString(language)}&output=json&encode=true";

        using var content = new MultipartFormDataContent();
        var file = new StreamContent(audio);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(file, "audio_file", fileName);

        HttpResponseMessage response;
        try
        {
            response = await httpClientFactory.CreateClient(OpenAiCompatibleChatProvider.HttpClientName).PostAsync(url, content, ct);
        }
        catch (HttpRequestException e)
        {
            throw new AiProviderException("HTTP_UNREACHABLE", $"{name} is unreachable: {e.Message}", e);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new AiProviderException($"HTTP_{(int)response.StatusCode}", $"{name} answered HTTP {(int)response.StatusCode}.");

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                var root = document.RootElement;

                var segments = new List<TranscriptSegment>();
                if (root.TryGetProperty("segments", out var list) && list.ValueKind == JsonValueKind.Array)
                    foreach (var segment in list.EnumerateArray())
                        if (ReadSegment(segment) is { } parsed)
                            segments.Add(parsed);

                var transcript = root.TryGetProperty("text", out var full)
                    ? full.GetString()?.Trim() ?? ""
                    : string.Join(" ", segments.Select(s => s.Text));

                return new TranscriptionResult(transcript, segments);
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException)
            {
                throw new AiProviderException("MALFORMED_RESPONSE", $"{name} returned an unexpected response.", e);
            }
        }
    }

    /// <summary>The two shapes this service emits: the openai-whisper engine writes segment objects, while
    /// faster-whisper writes tuples — [id, seek, start, end, text, tokens, …].</summary>
    private static TranscriptSegment? ReadSegment(JsonElement segment)
    {
        var (start, end, text) = segment.ValueKind switch
        {
            JsonValueKind.Object => (ReadDouble(segment, "start"), ReadDouble(segment, "end"),
                segment.TryGetProperty("text", out var value) ? value.GetString() : null),
            JsonValueKind.Array when segment.GetArrayLength() >= 5 => (ReadDouble(segment[2]), ReadDouble(segment[3]), segment[4].GetString()),
            _ => (0, 0, null)
        };

        return string.IsNullOrWhiteSpace(text) ? null : new TranscriptSegment(start, end, text.Trim());
    }

    private static double ReadDouble(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? ReadDouble(value) : 0;

    private static double ReadDouble(JsonElement element) => element.TryGetDouble(out var number) ? number : 0;
}

/// <summary>Deterministic transcription for development and tests: no audio decoding, just a fixed answer whose length
/// scales with the upload, so pace and pause metrics have something to work with.</summary>
public sealed class FakeSpeechToTextProvider(string name) : ISpeechToTextProvider
{
    public const string Transcript =
        "Well, I think this is quite an interesting question. In my hometown most people prefer to study in the evening, " +
        "because they work during the day. Personally I have been learning English for about two years and I try to " +
        "practise speaking every day with my friends.";

    public string Name => name;

    public async Task<TranscriptionResult> TranscribeAsync(Stream audio, string fileName, string contentType, string language,
        CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await audio.CopyToAsync(buffer, ct);

        var sentences = Transcript.Split(". ", StringSplitOptions.RemoveEmptyEntries);
        var segments = sentences
            .Select((text, i) => new TranscriptSegment(i * 5.0, i * 5.0 + 4.2, text.TrimEnd('.') + "."))
            .ToList();

        return new TranscriptionResult(Transcript, segments);
    }
}
