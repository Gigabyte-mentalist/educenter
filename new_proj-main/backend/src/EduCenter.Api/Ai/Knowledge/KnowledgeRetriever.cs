using System.Data.Common;
using EduCenter.Api.Ai.Providers;
using EduCenter.Infrastructure;
using EduCenter.Modules.Ai.Domain;
using EduCenter.Modules.Students.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Ai.Knowledge;

/// <summary>What the caller is allowed to be answered from — the permission filter of docs/00_TZ.md §37 (layer 5),
/// resolved before any ranking happens.</summary>
public sealed record KnowledgeAudience(
    Guid OrganizationId, IReadOnlyCollection<Guid> BranchIds, IReadOnlyCollection<Guid> CourseIds,
    IReadOnlyCollection<Guid> GroupIds, bool IncludeStaffOnly);

public sealed record RetrievedChunk(Guid ChunkId, Guid DocumentId, string DocumentTitle, int Ordinal, string Text, double Score);

/// <summary>
/// Hybrid retrieval (docs/00_TZ.md §31): the scope filter runs in SQL first, then vector similarity and full-text search
/// each rank the surviving chunks and the two lists are merged by reciprocal rank fusion. Full-text alone still returns
/// sensible results when no embedding model is configured, so RAG degrades instead of breaking.
/// </summary>
public class KnowledgeRetriever(AppDbContext db, AiProviderRegistry providers, ILogger<KnowledgeRetriever> logger)
{
    private const int CandidatesPerMethod = 30;
    private const int RankConstant = 60; // standard RRF damping

    private const string SearchSql = """
        WITH scoped AS (
            SELECT c."Id", c."DocumentId", c."Ordinal", c."Text", c."Embedding", c."SearchVector", d."Title"
            FROM knowledge_chunks c
            JOIN knowledge_documents d ON d."Id" = c."DocumentId"
            WHERE c."OrganizationId" = @organization
              AND d."Status" = 'Ready'
              AND (@include_staff_only OR c."Visibility" = 'Students')
              AND (c."Scope" = 'Organization'
                   OR (c."Scope" = 'Branch' AND c."BranchId" = ANY(@branches))
                   OR (c."Scope" = 'Course' AND c."CourseId" = ANY(@courses))
                   OR (c."Scope" = 'Group'  AND c."GroupId"  = ANY(@groups)))
        ),
        vector_hits AS (
            SELECT "Id", row_number() OVER (ORDER BY "Embedding" <=> CAST(@embedding AS vector)) AS rank
            FROM scoped
            WHERE @has_embedding AND "Embedding" IS NOT NULL
            ORDER BY "Embedding" <=> CAST(@embedding AS vector)
            LIMIT @candidates
        ),
        text_hits AS (
            SELECT s."Id", row_number() OVER (ORDER BY ts_rank_cd(s."SearchVector", q) DESC) AS rank
            FROM scoped s, plainto_tsquery('simple', @query) q
            WHERE s."SearchVector" @@ q
            ORDER BY ts_rank_cd(s."SearchVector", q) DESC
            LIMIT @candidates
        )
        SELECT s."Id", s."DocumentId", s."Title", s."Ordinal", s."Text",
               COALESCE(1.0 / (@rank_constant + v.rank), 0) + COALESCE(1.0 / (@rank_constant + t.rank), 0) AS score
        FROM scoped s
        LEFT JOIN vector_hits v ON v."Id" = s."Id"
        LEFT JOIN text_hits t ON t."Id" = s."Id"
        WHERE v."Id" IS NOT NULL OR t."Id" IS NOT NULL
        ORDER BY score DESC, s."Ordinal"
        LIMIT @limit;
        """;

    /// <summary>A student may be answered from material attached to their organization, branches, courses and groups —
    /// staff-only documents never take part.</summary>
    public async Task<KnowledgeAudience> ForStudentAsync(Guid organizationId, Guid studentId, CancellationToken ct = default)
    {
        var rows = await (
            from e in db.Enrollments
            join g in db.Groups on e.GroupId equals g.Id
            where e.StudentId == studentId && e.Status == EnrollmentStatus.Active
            select new { g.Id, g.BranchId, g.CourseId }
        ).AsNoTracking().ToListAsync(ct);

        return new KnowledgeAudience(organizationId,
            rows.Select(r => r.BranchId).Distinct().ToList(),
            rows.Select(r => r.CourseId).Distinct().ToList(),
            rows.Select(r => r.Id).Distinct().ToList(),
            IncludeStaffOnly: false);
    }

    public async Task<IReadOnlyList<RetrievedChunk>> SearchAsync(KnowledgeAudience audience, string query, int limit,
        CancellationToken ct = default)
    {
        var embedding = await EmbedQueryAsync(query, ct);

        await db.Database.OpenConnectionAsync(ct);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = SearchSql;
        Add(command, "organization", audience.OrganizationId);
        Add(command, "branches", audience.BranchIds.ToArray());
        Add(command, "courses", audience.CourseIds.ToArray());
        Add(command, "groups", audience.GroupIds.ToArray());
        Add(command, "include_staff_only", audience.IncludeStaffOnly);
        Add(command, "query", query);
        Add(command, "embedding", embedding is null ? "[]" : Format(embedding));
        Add(command, "has_embedding", embedding is not null);
        Add(command, "candidates", CandidatesPerMethod);
        Add(command, "rank_constant", RankConstant);
        Add(command, "limit", Math.Clamp(limit, 1, 20));

        var results = new List<RetrievedChunk>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new RetrievedChunk(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetInt32(3),
                reader.GetString(4), (double)reader.GetDecimal(5)));
        }

        return results;
    }

    private async Task<float[]?> EmbedQueryAsync(string query, CancellationToken ct)
    {
        foreach (var target in providers.EmbeddingRoute())
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(target.TimeoutSeconds));
                var vectors = await target.Provider.EmbedAsync(target.Model, [query], timeout.Token);
                if (vectors.Count == 1 && vectors[0].Length == KnowledgeDocument.EmbeddingDimensions) return vectors[0];
            }
            catch (Exception e) when (e is AiProviderException || (e is OperationCanceledException && !ct.IsCancellationRequested))
            {
                logger.LogWarning(e, "Embedding provider {Provider} failed for a search query", target.Provider.Name);
            }
        }

        return null; // full-text only
    }

    private static string Format(float[] embedding) =>
        "[" + string.Join(",", embedding.Select(v => v.ToString("G9", System.Globalization.CultureInfo.InvariantCulture))) + "]";

    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
