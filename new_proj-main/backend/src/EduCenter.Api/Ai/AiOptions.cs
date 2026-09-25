namespace EduCenter.Api.Ai;

public enum AiProviderType
{
    /// <summary>Deterministic answers without a model — development and tests only (<see cref="AiOptions.AllowFakeProvider"/>).</summary>
    Fake,

    /// <summary>Any endpoint speaking the OpenAI chat-completions protocol: DeepSeek API, local vLLM / LM Studio.</summary>
    OpenAiCompatible,

    /// <summary>Ollama's own /api/chat — the only way to switch a hybrid-reasoning model's thinking off there.</summary>
    Ollama,

    /// <summary>whisper-asr-webservice: speech to text only, no chat or embeddings (docs/00_TZ.md §34).</summary>
    WhisperAsr,

    /// <summary>The OpenAI speech protocol (<c>POST /audio/speech</c>): text to speech only — a local Piper voice behind
    /// openedai-speech, or OpenAI itself (docs/00_TZ.md §34).</summary>
    OpenAiCompatibleTts
}

public enum FakeFailureMode
{
    None,

    /// <summary>Every call fails — lets tests exercise router fallback.</summary>
    AlwaysFail,

    /// <summary>A Speaking assessment comes back in the shape an older prompt asked for — no relevance, no parts — so tests can exercise
    /// output validation, its retry and the Failed session. Everything else is answered as usual.</summary>
    IncompleteAssessment
}

/// <summary>Price of one model in USD per million tokens (docs/00_TZ.md §54). Local models are free (omit them).</summary>
public class AiModelOptions
{
    public string Name { get; set; } = "";
    public decimal InputPricePerMillion { get; set; }
    public decimal OutputPricePerMillion { get; set; }
}

public class AiProviderOptions
{
    public AiProviderType Type { get; set; }
    public bool Enabled { get; set; } = true;
    public string? BaseUrl { get; set; }

    /// <summary>Never committed: set through environment variables or user-secrets (Ai__Providers__deepseek__ApiKey).</summary>
    public string? ApiKey { get; set; }

    public string? ChatModel { get; set; }

    /// <summary>Model used for RAG embeddings (docs/00_TZ.md §31). DeepSeek has no embedding API — this is a local model.</summary>
    public string? EmbeddingModel { get; set; }

    /// <summary>Text-to-speech providers only: the voice the examiner's questions are read in ("alloy", "nova", …) and the model
    /// behind it (OpenAI's "tts-1" when omitted). Prices in <see cref="Models"/> for a TTS model are per million characters.</summary>
    public string? SpeechVoice { get; set; }
    public string? SpeechModel { get; set; }

    public int? TimeoutSeconds { get; set; }

    /// <summary>Ask a hybrid-reasoning model (Qwen3, DeepSeek-R1 style) to answer directly. Thinking tokens are slow,
    /// cost output tokens and — on Ollama — land in a separate field, leaving the answer itself empty.</summary>
    public bool DisableReasoning { get; set; }
    public List<AiModelOptions> Models { get; set; } = [];
    public FakeFailureMode FailureMode { get; set; }
}

/// <summary>AI Gateway settings (docs/06_phase4_technical_plan.md §1). Section "Ai".</summary>
public class AiOptions
{
    public bool AllowFakeProvider { get; set; }
    public int RequestTimeoutSeconds { get; set; } = 60;

    /// <summary>How many times a provider is asked again after an answer fails JSON/schema validation.</summary>
    public int OutputValidationRetries { get; set; } = 1;

    /// <summary>Where uploaded knowledge-base files are kept (docs/00_TZ.md §38 keeps the original for re-ingestion).
    /// Local disk for now, like chat attachments; object storage (S3/MinIO) is a later step.</summary>
    public string StoragePath { get; set; } = "App_Data/knowledge";

    /// <summary>Ten megabytes, the ceiling every upload in the product shares. Anything larger belongs in
    /// Drive with a link pasted in, which is what the rejection message tells the uploader.</summary>
    public long MaxUploadBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>Where speaking-practice recordings are kept (docs/00_TZ.md §34).</summary>
    public string SpeakingStoragePath { get; set; } = "App_Data/speaking";

    /// <summary>Language hint for speech recognition — the centre teaches English.</summary>
    public string SpeechLanguage { get; set; } = "en";

    public Dictionary<string, AiProviderOptions> Providers { get; set; } = [];

    /// <summary>Task kind (or "Default") → ordered provider chain; an entry is "provider" or "provider:model".</summary>
    public Dictionary<string, List<string>> Routes { get; set; } = [];
}
