using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace EduCenter.Api.Ai.Providers;

/// <summary>
/// The OpenAI chat-completions protocol, which DeepSeek's API and local servers (vLLM, Ollama, LM Studio) all speak —
/// one client for the primary provider and the local Qwen fallback (docs/00_TZ.md §29, §36). The gateway owns the
/// timeout (the named HttpClient has none) and retries; this class only translates a single call.
/// </summary>
public sealed class OpenAiCompatibleChatProvider(string name, AiProviderOptions options, IHttpClientFactory httpClientFactory)
    : IChatModelProvider
{
    public const string HttpClientName = "ai-providers";

    public string Name => name;
    public string? DefaultChatModel => options.ChatModel;

    public async Task<ChatCompletionResponse> CompleteAsync(ChatCompletionRequest request, CancellationToken ct)
    {
        var messages = options.DisableReasoning ? WithoutReasoning(request.Messages) : request.Messages;
        var body = new Dictionary<string, object?>
        {
            ["model"] = request.Model,
            ["messages"] = messages
                .Select(m => new Dictionary<string, string> { ["role"] = RoleName(m.Role), ["content"] = m.Content })
                .ToList(),
            ["max_tokens"] = request.MaxOutputTokens,
            ["temperature"] = request.Temperature,
            ["stream"] = false
        };
        if (request.JsonOutput)
            body["response_format"] = new Dictionary<string, string> { ["type"] = "json_object" };
        if (options.DisableReasoning)
        {
            // Two switches because servers differ: vLLM/SGLang read the chat template argument, Ollama reads "think".
            body["chat_template_kwargs"] = new Dictionary<string, object?> { ["enable_thinking"] = false };
            body["think"] = false;
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, $"{options.BaseUrl!.TrimEnd('/')}/chat/completions")
        {
            Content = JsonContent.Create(body)
        };
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);

        HttpResponseMessage response;
        try
        {
            response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(message, ct);
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

                var content = StripThinking(root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString());
                if (string.IsNullOrWhiteSpace(content))
                    throw new AiProviderException("EMPTY_RESPONSE", $"{name} returned an empty answer.");

                var inputTokens = 0;
                var outputTokens = 0;
                if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
                {
                    inputTokens = ReadInt(usage, "prompt_tokens");
                    outputTokens = ReadInt(usage, "completion_tokens");
                }

                return new ChatCompletionResponse(content, inputTokens, outputTokens);
            }
            catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
            {
                throw new AiProviderException("MALFORMED_RESPONSE", $"{name} returned an unexpected response.", e);
            }
        }
    }

    /// <summary>Qwen3 turns thinking off when the prompt carries "/no_think" — the switch every server honours, since it
    /// lives in the prompt rather than the API.</summary>
    private static List<AiChatMessage> WithoutReasoning(IReadOnlyList<AiChatMessage> messages)
    {
        var updated = messages.ToList();
        var systemIndex = updated.FindIndex(m => m.Role == AiChatRole.System);
        if (systemIndex >= 0)
            updated[systemIndex] = updated[systemIndex] with { Content = updated[systemIndex].Content + "\n/no_think" };
        else
            updated.Insert(0, new AiChatMessage(AiChatRole.System, "/no_think"));
        return updated;
    }

    /// <summary>A model that still thinks leaves the block inline; the answer is what follows it.</summary>
    private static string? StripThinking(string? content) => content is null
        ? null
        : System.Text.RegularExpressions.Regex.Replace(content, @"<think>.*?</think>", "",
            System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

    private static int ReadInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt32(out var number) ? number : 0;

    private static string RoleName(AiChatRole role) => role switch
    {
        AiChatRole.System => "system",
        AiChatRole.Assistant => "assistant",
        _ => "user"
    };
}
