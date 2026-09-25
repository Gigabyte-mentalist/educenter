using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EduCenter.Api.Ai.Providers;

/// <summary>Turns text into vectors for RAG (docs/00_TZ.md §31). Only the gateway's knowledge pipeline calls it.</summary>
public interface IEmbeddingProvider
{
    string Name { get; }
    string? DefaultEmbeddingModel { get; }
    Task<IReadOnlyList<float[]>> EmbedAsync(string model, IReadOnlyList<string> inputs, CancellationToken ct);
}

/// <summary>Ollama's native embedding endpoint — batches inputs in one call.</summary>
public sealed class OllamaEmbeddingProvider(string name, AiProviderOptions options, IHttpClientFactory httpClientFactory)
    : IEmbeddingProvider
{
    public string Name => name;
    public string? DefaultEmbeddingModel => options.EmbeddingModel;

    public async Task<IReadOnlyList<float[]>> EmbedAsync(string model, IReadOnlyList<string> inputs, CancellationToken ct)
    {
        var body = new Dictionary<string, object?> { ["model"] = model, ["input"] = inputs };
        using var document = await EmbeddingHttp.PostAsync(httpClientFactory, name, $"{options.BaseUrl!.TrimEnd('/')}/api/embed",
            body, apiKey: null, ct);

        var vectors = document.RootElement.GetProperty("embeddings").EnumerateArray().Select(ReadVector).ToList();
        return Validate(name, vectors, inputs.Count);
    }

    internal static float[] ReadVector(JsonElement element) =>
        element.EnumerateArray().Select(v => (float)v.GetDouble()).ToArray();

    internal static IReadOnlyList<float[]> Validate(string name, List<float[]> vectors, int expected) =>
        vectors.Count == expected
            ? vectors
            : throw new AiProviderException("EMBEDDING_COUNT_MISMATCH", $"{name} returned {vectors.Count} embeddings for {expected} inputs.");
}

/// <summary>The OpenAI /embeddings protocol — local vLLM/TEI servers and cloud providers that offer embeddings.</summary>
public sealed class OpenAiCompatibleEmbeddingProvider(string name, AiProviderOptions options, IHttpClientFactory httpClientFactory)
    : IEmbeddingProvider
{
    public string Name => name;
    public string? DefaultEmbeddingModel => options.EmbeddingModel;

    public async Task<IReadOnlyList<float[]>> EmbedAsync(string model, IReadOnlyList<string> inputs, CancellationToken ct)
    {
        var body = new Dictionary<string, object?> { ["model"] = model, ["input"] = inputs };
        using var document = await EmbeddingHttp.PostAsync(httpClientFactory, name, $"{options.BaseUrl!.TrimEnd('/')}/embeddings",
            body, options.ApiKey, ct);

        var vectors = document.RootElement.GetProperty("data").EnumerateArray()
            .Select(item => OllamaEmbeddingProvider.ReadVector(item.GetProperty("embedding")))
            .ToList();
        return OllamaEmbeddingProvider.Validate(name, vectors, inputs.Count);
    }
}

/// <summary>Deterministic pseudo-embeddings for development and tests: the same text always maps to the same vector, and
/// similar texts share leading words, so retrieval can be exercised without a model.</summary>
public sealed class FakeEmbeddingProvider(string name, int dimensions) : IEmbeddingProvider
{
    public const string Model = "fake-embedding";

    public string Name => name;
    public string? DefaultEmbeddingModel => Model;

    public Task<IReadOnlyList<float[]>> EmbedAsync(string model, IReadOnlyList<string> inputs, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<float[]>>(inputs.Select(Embed).ToList());

    private float[] Embed(string text)
    {
        // A bag-of-words hash: every word nudges one dimension, so texts sharing words end up close under cosine distance.
        var vector = new float[dimensions];
        foreach (var word in text.ToLowerInvariant().Split(' ', '\n', '\t', '.', ',', ';', ':', '!', '?', '"', '(', ')'))
        {
            if (word.Length == 0) continue;
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(word));
            var index = (BitConverter.ToUInt32(hash, 0) % (uint)dimensions);
            vector[index] += 1f;
        }

        var norm = MathF.Sqrt(vector.Sum(v => v * v));
        if (norm == 0) vector[0] = norm = 1f;
        for (var i = 0; i < vector.Length; i++) vector[i] /= norm;
        return vector;
    }
}

internal static class EmbeddingHttp
{
    public static async Task<JsonDocument> PostAsync(IHttpClientFactory httpClientFactory, string providerName, string url,
        Dictionary<string, object?> body, string? apiKey, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        if (!string.IsNullOrWhiteSpace(apiKey))
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        HttpResponseMessage response;
        try
        {
            response = await httpClientFactory.CreateClient(OpenAiCompatibleChatProvider.HttpClientName).SendAsync(message, ct);
        }
        catch (HttpRequestException e)
        {
            throw new AiProviderException("HTTP_UNREACHABLE", $"{providerName} is unreachable: {e.Message}", e);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new AiProviderException($"HTTP_{(int)response.StatusCode}", $"{providerName} answered HTTP {(int)response.StatusCode}.");

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException)
            {
                throw new AiProviderException("MALFORMED_RESPONSE", $"{providerName} returned an unexpected response.", e);
            }
        }
    }
}
