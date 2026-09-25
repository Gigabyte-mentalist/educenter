using System.Net.Http.Json;
using System.Text.Json;

namespace EduCenter.Api.Ai.Providers;

/// <summary>
/// Ollama's native chat API (docs/06_phase4_technical_plan.md §1). Its OpenAI-compatible endpoint cannot turn a
/// hybrid-reasoning model's thinking off — it appends "/think" to the prompt itself and returns the thoughts in a
/// separate "reasoning" field, leaving "content" empty — so local models are called through /api/chat instead, where
/// "think": false and "format": "json" are honoured.
/// </summary>
public sealed class OllamaChatProvider(string name, AiProviderOptions options, IHttpClientFactory httpClientFactory) : IChatModelProvider
{
    public string Name => name;
    public string? DefaultChatModel => options.ChatModel;

    public async Task<ChatCompletionResponse> CompleteAsync(ChatCompletionRequest request, CancellationToken ct)
    {
        var body = new Dictionary<string, object?>
        {
            ["model"] = request.Model,
            ["messages"] = request.Messages
                .Select(m => new Dictionary<string, string> { ["role"] = RoleName(m.Role), ["content"] = m.Content })
                .ToList(),
            ["stream"] = false,
            ["options"] = new Dictionary<string, object?>
            {
                ["temperature"] = request.Temperature,
                ["num_predict"] = request.MaxOutputTokens
            }
        };
        if (options.DisableReasoning) body["think"] = false;
        if (request.JsonOutput) body["format"] = "json";

        HttpResponseMessage response;
        try
        {
            response = await httpClientFactory.CreateClient(OpenAiCompatibleChatProvider.HttpClientName)
                .PostAsJsonAsync($"{options.BaseUrl!.TrimEnd('/')}/api/chat", body, ct);
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

                var content = root.GetProperty("message").GetProperty("content").GetString();
                if (string.IsNullOrWhiteSpace(content))
                    throw new AiProviderException("EMPTY_RESPONSE", $"{name} returned an empty answer.");

                return new ChatCompletionResponse(content.Trim(), ReadInt(root, "prompt_eval_count"), ReadInt(root, "eval_count"));
            }
            catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                throw new AiProviderException("MALFORMED_RESPONSE", $"{name} returned an unexpected response.", e);
            }
        }
    }

    private static int ReadInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt32(out var number) ? number : 0;

    private static string RoleName(AiChatRole role) => role switch
    {
        AiChatRole.System => "system",
        AiChatRole.Assistant => "assistant",
        _ => "user"
    };
}
