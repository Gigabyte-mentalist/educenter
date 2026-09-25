using EduCenter.SharedKernel;
using Pgvector;

namespace EduCenter.Modules.Ai.Domain;

/// <summary>Who a document belongs to — the retrieval filter starts from this (docs/00_TZ.md §38 "access classification").</summary>
public enum KnowledgeScope
{
    Organization,
    Branch,
    Course,
    Group
}

public enum KnowledgeVisibility
{
    /// <summary>Students may be answered from it (course materials, grammar references).</summary>
    Students,

    /// <summary>Staff only — teacher methodology, internal guides. Never reaches a student's AI Tutor.</summary>
    StaffOnly
}

public enum KnowledgeDocumentStatus
{
    Uploaded,
    Processing,
    Ready,

    /// <summary>The text contains instruction-shaped content (RAG poisoning attempt or a prompt-injection example);
    /// it is not searchable until a staff member approves it (docs/00_TZ.md §38).</summary>
    Quarantined,

    Failed
}

/// <summary>
/// One uploaded knowledge-base file (docs/00_TZ.md §31, §38). The original file is kept so the document can be
/// re-ingested — after quarantine approval, or when the embedding model changes.
/// </summary>
public class KnowledgeDocument : TenantEntity
{
    /// <summary>bge-m3's native width, and a portable one: Qwen3-Embedding-0.6B is 1024 too, and OpenAI/Cohere/Voyage
    /// models can be truncated to it — so a later model change means re-embedding, not a schema change.</summary>
    public const int EmbeddingDimensions = 1024;

    public const int MaxTitleLength = 200;
    public const int MaxErrorLength = 500;

    private KnowledgeDocument() { } // EF Core

    public KnowledgeDocument(Guid organizationId, Guid uploadedBy, string title, string fileName, string contentType,
        long sizeBytes, string storageKey, KnowledgeScope scope, KnowledgeVisibility visibility,
        Guid? branchId = null, Guid? courseId = null, Guid? groupId = null)
    {
        OrganizationId = organizationId;
        UploadedBy = uploadedBy;
        Title = title;
        FileName = fileName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        StorageKey = storageKey;
        Scope = scope;
        Visibility = visibility;
        BranchId = branchId;
        CourseId = courseId;
        GroupId = groupId;
        Status = KnowledgeDocumentStatus.Uploaded;
    }

    public Guid UploadedBy { get; private set; }
    public string Title { get; private set; } = default!;
    public string FileName { get; private set; } = default!;
    public string ContentType { get; private set; } = default!;
    public long SizeBytes { get; private set; }
    public string StorageKey { get; private set; } = default!;
    public KnowledgeScope Scope { get; private set; }
    public KnowledgeVisibility Visibility { get; private set; }
    public Guid? BranchId { get; private set; }
    public Guid? CourseId { get; private set; }
    public Guid? GroupId { get; private set; }
    public KnowledgeDocumentStatus Status { get; private set; }
    public string? Error { get; private set; }
    public int ChunkCount { get; private set; }
    public string? EmbeddingModel { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }

    /// <summary>Set when a staff member cleared a quarantined document — the next ingest skips the injection check.</summary>
    public bool InjectionCheckWaived { get; private set; }

    public void StartProcessing()
    {
        Status = KnowledgeDocumentStatus.Processing;
        Error = null;
        MarkUpdated();
    }

    public void MarkReady(int chunkCount, string? embeddingModel)
    {
        Status = KnowledgeDocumentStatus.Ready;
        Error = null;
        ChunkCount = chunkCount;
        EmbeddingModel = embeddingModel;
        ProcessedAt = DateTimeOffset.UtcNow;
        MarkUpdated();
    }

    public void Quarantine(string reason) => Stop(KnowledgeDocumentStatus.Quarantined, reason);

    public void Fail(string error) => Stop(KnowledgeDocumentStatus.Failed, error);

    public void WaiveInjectionCheck()
    {
        InjectionCheckWaived = true;
        MarkUpdated();
    }

    private void Stop(KnowledgeDocumentStatus status, string reason)
    {
        Status = status;
        Error = reason.Length <= MaxErrorLength ? reason : reason[..MaxErrorLength];
        ChunkCount = 0;
        MarkUpdated();
    }
}

/// <summary>
/// A searchable piece of a document. The document's scope and visibility are copied onto every chunk so retrieval can
/// filter in one SQL statement before ranking — the permission filter of docs/00_TZ.md §37, layer 5.
/// </summary>
public class KnowledgeChunk : TenantEntity
{
    public const int MaxTextLength = 4000;

    private KnowledgeChunk() { } // EF Core

    public KnowledgeChunk(Guid organizationId, Guid documentId, int ordinal, string text, KnowledgeScope scope,
        KnowledgeVisibility visibility, Guid? branchId, Guid? courseId, Guid? groupId, Vector? embedding, string? embeddingModel)
    {
        OrganizationId = organizationId;
        DocumentId = documentId;
        Ordinal = ordinal;
        Text = text;
        Scope = scope;
        Visibility = visibility;
        BranchId = branchId;
        CourseId = courseId;
        GroupId = groupId;
        Embedding = embedding;
        EmbeddingModel = embeddingModel;
    }

    public Guid DocumentId { get; private set; }
    public int Ordinal { get; private set; }
    public string Text { get; private set; } = default!;
    public KnowledgeScope Scope { get; private set; }
    public KnowledgeVisibility Visibility { get; private set; }
    public Guid? BranchId { get; private set; }
    public Guid? CourseId { get; private set; }
    public Guid? GroupId { get; private set; }

    /// <summary>Null when the embedding provider was unavailable — the chunk is still found by full-text search.</summary>
    public Vector? Embedding { get; private set; }

    public string? EmbeddingModel { get; private set; }
}
