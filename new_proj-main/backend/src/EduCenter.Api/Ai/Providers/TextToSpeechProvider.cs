using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace EduCenter.Api.Ai.Providers;

/// <param name="Audio">The finished recording, in whatever container <paramref name="ContentType"/> says.</param>
/// <param name="Voice">The voice it was made with. The gateway fills it in; it is part of the cache key, so switching a voice in
/// settings never replays a recording made with the old one.</param>
public sealed record SpeechResult(byte[] Audio, string ContentType, string Voice = "");

/// <summary>Text to speech (docs/00_TZ.md §34 ITTSProvider): the examiner's question read aloud. Only the gateway calls it.</summary>
public interface ITextToSpeechProvider
{
    string Name { get; }
    Task<SpeechResult> SynthesizeAsync(string text, string voice, string language, CancellationToken ct);
}

/// <summary>
/// The OpenAI speech protocol — <c>POST {BaseUrl}/audio/speech</c> with a model, a voice and the text, an audio file back. A local
/// service that speaks it (openedai-speech in front of Piper, Kokoro-FastAPI) and OpenAI's own API answer the same call, so moving
/// from one to the other is a change of <c>BaseUrl</c> and key, not of code. Like the chat client, it has no timeout of its own:
/// the gateway applies the provider's.
/// </summary>
public sealed class OpenAiCompatibleTtsProvider(string name, AiProviderOptions options, IHttpClientFactory httpClientFactory)
    : ITextToSpeechProvider
{
    public const string DefaultModel = "tts-1";

    /// <summary>mp3 is the smallest thing every browser plays and every server of this kind can produce.</summary>
    private const string Format = "mp3";

    public string Name => name;

    public async Task<SpeechResult> SynthesizeAsync(string text, string voice, string language, CancellationToken ct)
    {
        var body = new Dictionary<string, object?>
        {
            ["model"] = string.IsNullOrWhiteSpace(options.SpeechModel) ? DefaultModel : options.SpeechModel,
            ["input"] = text,
            ["voice"] = voice,
            ["response_format"] = Format
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, $"{options.BaseUrl!.TrimEnd('/')}/audio/speech")
        {
            Content = JsonContent.Create(body)
        };
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);

        HttpResponseMessage response;
        try
        {
            response = await httpClientFactory.CreateClient(OpenAiCompatibleChatProvider.HttpClientName).SendAsync(message, ct);
        }
        catch (HttpRequestException e)
        {
            throw new AiProviderException("HTTP_UNREACHABLE", $"{name} is unreachable: {e.Message}", e);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new AiProviderException($"HTTP_{(int)response.StatusCode}", $"{name} answered HTTP {(int)response.StatusCode}.");

            var audio = await response.Content.ReadAsByteArrayAsync(ct);
            if (audio.Length == 0)
                throw new AiProviderException("EMPTY_RESPONSE", $"{name} returned no audio.");

            // A server that fails after the status line was written would hand back its error text as the "audio".
            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (contentType is not null && !contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
                throw new AiProviderException("MALFORMED_RESPONSE", $"{name} returned {contentType} instead of audio.");

            return new SpeechResult(audio, contentType ?? "audio/mpeg");
        }
    }
}

/// <summary>Deterministic speech for tests: a short quiet tone whose length follows the text, in a real (tiny) WAV file, so
/// the endpoint, the cache and the player all have a valid recording to work with — no voice model involved.</summary>
public sealed class FakeTextToSpeechProvider(string name, FakeFailureMode failureMode) : ITextToSpeechProvider
{
    private const int SampleRate = 8000;

    public string Name => name;

    public Task<SpeechResult> SynthesizeAsync(string text, string voice, string language, CancellationToken ct)
    {
        if (failureMode == FakeFailureMode.AlwaysFail)
            throw new AiProviderException("FAKE_FAILURE", $"{name} is configured to fail.");

        var seconds = Math.Clamp(text.Length * 0.03, 0.3, 3.0);
        var samples = (int)(seconds * SampleRate);

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write("RIFF"u8);
            writer.Write(36 + samples * 2);
            writer.Write("WAVEfmt "u8);
            writer.Write(16);
            writer.Write((short)1); // PCM
            writer.Write((short)1); // mono
            writer.Write(SampleRate);
            writer.Write(SampleRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write("data"u8);
            writer.Write(samples * 2);
            for (var i = 0; i < samples; i++)
                writer.Write((short)(Math.Sin(2 * Math.PI * 440 * i / SampleRate) * 2000));
        }

        return Task.FromResult(new SpeechResult(stream.ToArray(), "audio/wav"));
    }
}
