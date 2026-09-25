using EduCenter.Api.Ai.Providers;
using EduCenter.Api.Ai.Security;
using EduCenter.Api.Files;
using EduCenter.Infrastructure;
using EduCenter.Modules.Ai.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pgvector;

namespace EduCenter.Api.Ai.Knowledge;

/// <summary>
/// The document ingestion pipeline of docs/00_TZ.md §38: validation (at upload) → malware scan → parsing → prompt-injection scan →
/// access classification (carried on every chunk) → chunking → embedding → storage. A document whose text tries to give
/// the model instructions is quarantined instead of indexed, so uploaded material cannot poison the knowledge base.
/// The malware scan is repeated here, ahead of parsing, even though the upload was scanned: signatures improve, and a file that
/// went in while the scanner was off (or before it existed) is looked at again the next time it is indexed. It cannot be waived.
/// </summary>
public class KnowledgeIngestor(
    AppDbContext db, KnowledgeFileStorage storage, AiProviderRegistry providers, IFileScanner scanner,
    IOptions<FileScannerOptions> scannerOptions, ILogger<KnowledgeIngestor> logger)
{
    private const int EmbeddingBatchSize = 16;

    /// <summary>What a quarantined-for-malware document's error starts with — how the approval endpoint tells it from an injection hold.</summary>
    public const string MalwareReason = "Fayl zararli dastur (malware) deb topildi";

    public static bool IsMalware(string? error) => error?.StartsWith(MalwareReason, StringComparison.Ordinal) == true;

    public async Task IngestAsync(Guid documentId, CancellationToken ct = default)
    {
        var document = await db.KnowledgeDocuments.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (document is null || document.Status == KnowledgeDocumentStatus.Processing) return;

        document.StartProcessing();
        await db.SaveChangesAsync(ct);

        try
        {
            await using var file = storage.Open(document.StorageKey);
            if (file is null)
            {
                await StopAsync(document, d => d.Fail("Fayl saqlash joyida topilmadi."), ct);
                return;
            }

            var scan = await scanner.ScanAsync(file, document.FileName, ct);
            file.Position = 0;
            if (scan.Outcome == ScanOutcome.Infected)
            {
                await StopAsync(document, d => d.Quarantine($"{MalwareReason}: {scan.Signature}. Hujjat indekslanmadi va tasdiqlanmaydi."), ct);
                logger.LogWarning("Knowledge document {DocumentId} quarantined: malware {Signature}", documentId, scan.Signature);
                return;
            }

            if (scan.Outcome == ScanOutcome.Unavailable && scannerOptions.Value.Required)
            {
                await StopAsync(document, d => d.Fail("Faylni xavfsizlik tekshiruvidan o'tkazib bo'lmadi. Birozdan keyin qayta urinib ko'ring."), ct);
                return;
            }

            var text = DocumentTextExtractor.Extract(file, document.FileName);
            var chunks = KnowledgeChunker.Split(text);
            if (chunks.Count == 0)
            {
                await StopAsync(document, d => d.Fail("Faylda matn topilmadi (skan qilingan rasm bo'lishi mumkin)."), ct);
                return;
            }

            if (!document.InjectionCheckWaived)
            {
                var suspicious = chunks
                    .Select(chunk => (Chunk: chunk, Verdict: PromptInjectionDetector.Inspect(chunk)))
                    .FirstOrDefault(x => x.Verdict.IsSuspicious);
                if (suspicious.Verdict is { IsSuspicious: true })
                {
                    await StopAsync(document, d => d.Quarantine(
                        $"Matnda AI ko'rsatmalarini o'zgartirishga urinishga o'xshash qism bor ({suspicious.Verdict.Rule}). " +
                        "Hujjatni ko'rib chiqing va kerak bo'lsa tasdiqlang."), ct);
                    logger.LogWarning("Knowledge document {DocumentId} quarantined: {Rule}", documentId, suspicious.Verdict.Rule);
                    return;
                }
            }

            var (embeddings, embeddingModel) = await EmbedAsync(chunks, ct);

            await db.KnowledgeChunks.Where(c => c.DocumentId == documentId).ExecuteDeleteAsync(ct);
            for (var i = 0; i < chunks.Count; i++)
            {
                db.KnowledgeChunks.Add(new KnowledgeChunk(document.OrganizationId, document.Id, i, chunks[i],
                    document.Scope, document.Visibility, document.BranchId, document.CourseId, document.GroupId,
                    embeddings is null ? null : new Vector(embeddings[i]), embeddingModel));
            }

            document.MarkReady(chunks.Count, embeddingModel);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Knowledge document {DocumentId} ingestion failed", documentId);
            db.ChangeTracker.Clear();
            var stored = await db.KnowledgeDocuments.FirstAsync(d => d.Id == documentId, CancellationToken.None);
            stored.Fail(e is InvalidDataException ? e.Message : "Hujjatni qayta ishlashda kutilmagan xatolik yuz berdi.");
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    /// <summary>Embeddings are optional: with no embedding model configured (or a provider that is down) the document is
    /// still indexed and stays findable through full-text search.</summary>
    private async Task<(IReadOnlyList<float[]>? Vectors, string? Model)> EmbedAsync(List<string> chunks, CancellationToken ct)
    {
        foreach (var target in providers.EmbeddingRoute())
        {
            try
            {
                var vectors = new List<float[]>(chunks.Count);
                for (var offset = 0; offset < chunks.Count; offset += EmbeddingBatchSize)
                {
                    var batch = chunks.Skip(offset).Take(EmbeddingBatchSize).ToList();
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(TimeSpan.FromSeconds(target.TimeoutSeconds));
                    vectors.AddRange(await target.Provider.EmbedAsync(target.Model, batch, timeout.Token));
                }

                var wrongWidth = vectors.FirstOrDefault(v => v.Length != KnowledgeDocument.EmbeddingDimensions);
                if (wrongWidth is not null)
                {
                    logger.LogWarning("Embedding provider {Provider} returned {Width} dimensions, expected {Expected}",
                        target.Provider.Name, wrongWidth.Length, KnowledgeDocument.EmbeddingDimensions);
                    continue;
                }

                return (vectors, $"{target.Provider.Name}:{target.Model}");
            }
            catch (Exception e) when (e is AiProviderException || (e is OperationCanceledException && !ct.IsCancellationRequested))
            {
                logger.LogWarning(e, "Embedding provider {Provider} failed", target.Provider.Name);
            }
        }

        return (null, null);
    }

    private async Task StopAsync(KnowledgeDocument document, Action<KnowledgeDocument> stop, CancellationToken ct)
    {
        await db.KnowledgeChunks.Where(c => c.DocumentId == document.Id).ExecuteDeleteAsync(ct);
        stop(document);
        await db.SaveChangesAsync(ct);
    }
}
