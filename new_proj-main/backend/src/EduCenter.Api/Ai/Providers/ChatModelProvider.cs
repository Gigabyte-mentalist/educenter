namespace EduCenter.Api.Ai.Providers;

public enum AiChatRole
{
    System,
    User,
    Assistant
}

public sealed record AiChatMessage(AiChatRole Role, string Content);

/// <param name="PromptVersion">Metadata only — real providers ignore it; the fake provider answers by it.</param>
public sealed record ChatCompletionRequest(
    string Model, IReadOnlyList<AiChatMessage> Messages, bool JsonOutput, int MaxOutputTokens, double Temperature, string PromptVersion);

public sealed record ChatCompletionResponse(string Content, int InputTokens, int OutputTokens);

/// <summary>A provider call that did not produce an answer. <see cref="Code"/> is stored on the usage row.</summary>
public class AiProviderException(string code, string message, Exception? inner = null) : Exception(message, inner)
{
    public string Code { get; } = code;
}

/// <summary>A text/JSON model behind the AI Gateway (docs/00_TZ.md §29 IAIProvider, §34 ILLMProvider). Only the gateway
/// calls it; features never talk to a provider directly.</summary>
public interface IChatModelProvider
{
    string Name { get; }
    string? DefaultChatModel { get; }
    Task<ChatCompletionResponse> CompleteAsync(ChatCompletionRequest request, CancellationToken ct);
}
