using EduCenter.Modules.Ai.Domain;
using Microsoft.Extensions.Options;

namespace EduCenter.Api.Ai.Providers;

/// <summary>One provider/model the router may call for a task, with its price and timeout.</summary>
public sealed record AiRouteTarget(IChatModelProvider Provider, string Model, AiModelOptions? Pricing, int TimeoutSeconds)
{
    public decimal CostUsd(int inputTokens, int outputTokens) => Pricing is null
        ? 0
        : (inputTokens * Pricing.InputPricePerMillion + outputTokens * Pricing.OutputPricePerMillion) / 1_000_000m;
}

public sealed record AiProviderInfo(
    string Name, AiProviderType Type, bool Active, string? InactiveReason, string? BaseUrl, bool HasApiKey, string? ChatModel,
    string? EmbeddingModel);

/// <summary>An embedding model the RAG pipeline may call.</summary>
public sealed record AiEmbeddingTarget(IEmbeddingProvider Provider, string Model, int TimeoutSeconds);

/// <summary>A speech-to-text service the Speaking module may call.</summary>
public sealed record AiSpeechTarget(ISpeechToTextProvider Provider, int TimeoutSeconds);

/// <summary>A text-to-speech voice the Speaking module may call. The price, when the model has one, is per million characters.</summary>
public sealed record AiSynthesisTarget(ITextToSpeechProvider Provider, string Voice, string Model, AiModelOptions? Pricing, int TimeoutSeconds)
{
    public decimal CostUsd(int characters) => Pricing is null ? 0 : characters * Pricing.InputPricePerMillion / 1_000_000m;
}

/// <summary>
/// Builds the configured providers once and answers the AI Router's question "which models, in which order, for this
/// task" (docs/00_TZ.md §30). A provider without a base URL/model, switched off, or a fake one outside development never
/// becomes routable — so an unconfigured DeepSeek entry is skipped rather than failing every request.
/// </summary>
public class AiProviderRegistry
{
    public const string DefaultRoute = "Default";

    /// <summary>OpenAI's own default voice, which openedai-speech also maps to a Piper voice.</summary>
    public const string DefaultVoice = "alloy";

    private readonly AiOptions _options;
    private readonly Dictionary<string, (IChatModelProvider Provider, AiProviderOptions Options)> _active =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (IEmbeddingProvider Provider, AiProviderOptions Options)> _activeEmbedding =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (ISpeechToTextProvider Provider, AiProviderOptions Options)> _activeSpeech =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (ITextToSpeechProvider Provider, AiProviderOptions Options)> _activeSynthesis =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<AiProviderInfo> _described = [];

    public AiProviderRegistry(IOptions<AiOptions> options, IHttpClientFactory httpClientFactory)
    {
        _options = options.Value;

        foreach (var (name, provider) in _options.Providers)
        {
            var inactiveReason = InactiveReason(provider);
            if (inactiveReason is null)
            {
                // Speech-to-text services do not answer chat or embedding calls at all.
                if (provider.Type == AiProviderType.WhisperAsr)
                {
                    _activeSpeech[name] = (new WhisperAsrProvider(name, provider, httpClientFactory), provider);
                    _described.Add(new AiProviderInfo(name, provider.Type, true, null, provider.BaseUrl,
                        !string.IsNullOrWhiteSpace(provider.ApiKey), null, null));
                    continue;
                }

                if (provider.Type == AiProviderType.OpenAiCompatibleTts)
                {
                    _activeSynthesis[name] = (new OpenAiCompatibleTtsProvider(name, provider, httpClientFactory), provider);
                    _described.Add(new AiProviderInfo(name, provider.Type, true, null, provider.BaseUrl,
                        !string.IsNullOrWhiteSpace(provider.ApiKey), null, null));
                    continue;
                }

                IChatModelProvider instance = provider.Type switch
                {
                    AiProviderType.Fake => new FakeChatModelProvider(name, provider.FailureMode),
                    AiProviderType.Ollama => new OllamaChatProvider(name, provider, httpClientFactory),
                    _ => new OpenAiCompatibleChatProvider(name, provider, httpClientFactory)
                };
                _active[name] = (instance, provider);
                if (provider.Type == AiProviderType.Fake)
                {
                    _activeSpeech[name] = (new FakeSpeechToTextProvider(name), provider);
                    _activeSynthesis[name] = (new FakeTextToSpeechProvider(name, provider.FailureMode), provider);
                }

                // A provider embeds only when it was given an embedding model (DeepSeek has no embedding API at all).
                IEmbeddingProvider? embedding = provider.Type switch
                {
                    AiProviderType.Fake => new FakeEmbeddingProvider(name, KnowledgeDocument.EmbeddingDimensions),
                    AiProviderType.Ollama when !string.IsNullOrWhiteSpace(provider.EmbeddingModel) =>
                        new OllamaEmbeddingProvider(name, provider, httpClientFactory),
                    AiProviderType.OpenAiCompatible when !string.IsNullOrWhiteSpace(provider.EmbeddingModel) =>
                        new OpenAiCompatibleEmbeddingProvider(name, provider, httpClientFactory),
                    _ => null
                };
                if (embedding is not null) _activeEmbedding[name] = (embedding, provider);
            }

            _described.Add(new AiProviderInfo(name, provider.Type, inactiveReason is null, inactiveReason,
                provider.BaseUrl, !string.IsNullOrWhiteSpace(provider.ApiKey),
                provider.Type == AiProviderType.Fake ? FakeChatModelProvider.Model : provider.ChatModel,
                provider.Type == AiProviderType.Fake ? FakeEmbeddingProvider.Model : provider.EmbeddingModel));
        }
    }

    public IReadOnlyList<AiProviderInfo> Providers => _described;

    /// <summary>The ordered chain for a task: its own route if configured, otherwise the default route; inactive entries are dropped.</summary>
    public IReadOnlyList<AiRouteTarget> Route(AiTaskKind task) => Chain(task).Select(Target).OfType<AiRouteTarget>().ToList();

    /// <summary>The embedding chain (docs/00_TZ.md §31). Empty means retrieval falls back to full-text search only.</summary>
    public IReadOnlyList<AiEmbeddingTarget> EmbeddingRoute() =>
        Chain(AiTaskKind.Embedding).Select(EmbeddingTarget).OfType<AiEmbeddingTarget>().ToList();

    /// <summary>The speech-to-text chain (docs/00_TZ.md §34). Empty means Speaking is unavailable.</summary>
    public IReadOnlyList<AiSpeechTarget> SpeechRoute() =>
        Chain(AiTaskKind.Transcription).Select(SpeechTarget).OfType<AiSpeechTarget>().ToList();

    private AiSpeechTarget? SpeechTarget(string entry)
    {
        var (name, _) = SplitEntry(entry);
        return _activeSpeech.TryGetValue(name, out var registered)
            ? new AiSpeechTarget(registered.Provider, registered.Options.TimeoutSeconds ?? _options.RequestTimeoutSeconds)
            : null;
    }

    /// <summary>
    /// The text-to-speech chain (docs/00_TZ.md §34): the entries of the <c>SpeechSynthesis</c> route only. Unlike every other task
    /// it never falls back to the Default route — that one lists chat models — so a voice is something a centre switches on by
    /// naming a provider, and an empty chain means questions are shown as text, which is a working product, not an error.
    /// An entry is "provider" or "provider:voice".
    /// </summary>
    public IReadOnlyList<AiSynthesisTarget> SynthesisRoute() =>
        Chain(AiTaskKind.SpeechSynthesis, fallBackToDefault: false).Select(SynthesisTarget).OfType<AiSynthesisTarget>().ToList();

    private AiSynthesisTarget? SynthesisTarget(string entry)
    {
        var (name, entryVoice) = SplitEntry(entry);
        if (!_activeSynthesis.TryGetValue(name, out var registered)) return null;

        var voice = string.IsNullOrWhiteSpace(entryVoice) ? registered.Options.SpeechVoice : entryVoice;
        if (string.IsNullOrWhiteSpace(voice)) voice = DefaultVoice;

        var model = string.IsNullOrWhiteSpace(registered.Options.SpeechModel) ? OpenAiCompatibleTtsProvider.DefaultModel : registered.Options.SpeechModel;
        return new AiSynthesisTarget(registered.Provider, voice, model,
            registered.Options.Models.FirstOrDefault(m => string.Equals(m.Name, model, StringComparison.OrdinalIgnoreCase)),
            registered.Options.TimeoutSeconds ?? _options.RequestTimeoutSeconds);
    }

    private List<string> Chain(AiTaskKind task, bool fallBackToDefault = true)
    {
        var chain = _options.Routes.FirstOrDefault(r => string.Equals(r.Key, task.ToString(), StringComparison.OrdinalIgnoreCase)).Value;
        if (fallBackToDefault && (chain is null || chain.Count == 0))
            chain = _options.Routes.FirstOrDefault(r => string.Equals(r.Key, DefaultRoute, StringComparison.OrdinalIgnoreCase)).Value ?? [];
        return chain ?? [];
    }

    private AiEmbeddingTarget? EmbeddingTarget(string entry)
    {
        var (name, model) = SplitEntry(entry);
        if (!_activeEmbedding.TryGetValue(name, out var registered)) return null;

        model = string.IsNullOrWhiteSpace(model) ? registered.Provider.DefaultEmbeddingModel : model;
        return string.IsNullOrWhiteSpace(model)
            ? null
            : new AiEmbeddingTarget(registered.Provider, model, registered.Options.TimeoutSeconds ?? _options.RequestTimeoutSeconds);
    }

    /// <summary>"provider" or "provider:model" (model names may contain ':' themselves, e.g. Ollama's "qwen2.5:32b").</summary>
    public AiRouteTarget? Target(string entry)
    {
        var (name, model) = SplitEntry(entry);
        if (!_active.TryGetValue(name, out var registered)) return null;
        model = string.IsNullOrWhiteSpace(model) ? registered.Provider.DefaultChatModel : model;
        if (string.IsNullOrWhiteSpace(model)) return null;

        return new AiRouteTarget(registered.Provider, model,
            registered.Options.Models.FirstOrDefault(m => string.Equals(m.Name, model, StringComparison.OrdinalIgnoreCase)),
            registered.Options.TimeoutSeconds ?? _options.RequestTimeoutSeconds);
    }

    private static (string Name, string? Model) SplitEntry(string entry)
    {
        var separator = entry.IndexOf(':');
        return separator < 0
            ? (entry.Trim(), null)
            : (entry[..separator].Trim(), entry[(separator + 1)..].Trim());
    }

    private string? InactiveReason(AiProviderOptions provider)
    {
        if (!provider.Enabled) return "O'chirilgan (Enabled=false).";
        return provider.Type switch
        {
            AiProviderType.Fake when !_options.AllowFakeProvider => "Fake provider faqat development/test muhitida ishlaydi.",
            not AiProviderType.Fake when string.IsNullOrWhiteSpace(provider.BaseUrl) => "BaseUrl ko'rsatilmagan.",
            // A speech service has no chat model to configure.
            not AiProviderType.Fake and not AiProviderType.WhisperAsr and not AiProviderType.OpenAiCompatibleTts
                when string.IsNullOrWhiteSpace(provider.ChatModel) => "ChatModel ko'rsatilmagan.",
            _ => null
        };
    }
}
