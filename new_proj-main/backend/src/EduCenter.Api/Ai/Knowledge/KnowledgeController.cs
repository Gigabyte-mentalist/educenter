using EduCenter.Api.Authorization;
using EduCenter.Infrastructure;
using EduCenter.Modules.Ai.Domain;
using EduCenter.SharedKernel.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Ai.Knowledge;

public record KnowledgeDocumentDto(
    Guid Id, string Title, string FileName, string ContentType, long SizeBytes, KnowledgeScope Scope, string? ScopeName,
    KnowledgeVisibility Visibility, Guid? BranchId, Guid? CourseId, Guid? GroupId, KnowledgeDocumentStatus Status,
    string? Error, int ChunkCount, string? EmbeddingModel, string UploadedByName, DateTimeOffset CreatedAt,
    DateTimeOffset? ProcessedAt, bool FileAvailable, bool Malware = false);

public record KnowledgeUploadRequest(
    IFormFile File, string? Title, KnowledgeScope Scope, KnowledgeVisibility Visibility,
    Guid? BranchId, Guid? CourseId, Guid? GroupId);

/// <param name="Restored">True when the uploaded file is gone from storage and the text was rebuilt from the index.</param>
public record KnowledgePreviewDto(string Text, bool Truncated, bool Restored);

public record KnowledgeTargetDto(Guid Id, string Name);

/// <summary>What the caller may attach material to — the upload form offers exactly this and nothing else.</summary>
public record KnowledgeTargetsDto(
    List<KnowledgeScope> Scopes, List<KnowledgeTargetDto> Branches, List<KnowledgeTargetDto> Courses, List<KnowledgeTargetDto> Groups);

/// <summary>
/// Knowledge Base management (docs/00_TZ.md §31, §38). Every endpoint needs ai.knowledge.manage, and the caller's scope
/// decides what they may attach material to: organization-wide material is for an organization-wide manager, an
/// administrator covers their branches, a teacher their own groups and those groups' courses.
/// </summary>
[ApiController]
[Route("api/ai/knowledge")]
[Authorize(Policy = "ai.knowledge.manage")]
public class KnowledgeController(AppDbContext db, KnowledgeFileStorage storage, IAiJobQueue queue) : ControllerBase
{
    [HttpGet("documents")]
    public async Task<ActionResult<List<KnowledgeDocumentDto>>> List([FromQuery] KnowledgeDocumentStatus? status, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        var reach = await ReachAsync(scope, ct);

        var query = db.KnowledgeDocuments.Where(d => d.OrganizationId == scope.OrganizationId);
        if (status is not null) query = query.Where(d => d.Status == status);
        if (!reach.OrganizationWide)
        {
            query = query.Where(d =>
                d.Scope == KnowledgeScope.Organization
                || (d.BranchId != null && reach.BranchIds.Contains(d.BranchId.Value))
                || (d.CourseId != null && reach.CourseIds.Contains(d.CourseId.Value))
                || (d.GroupId != null && reach.GroupIds.Contains(d.GroupId.Value)));
        }

        var documents = await query.OrderByDescending(d => d.CreatedAt).Take(200).AsNoTracking().ToListAsync(ct);
        return Ok(await ToDtosAsync(documents, ct));
    }

    [HttpPost("documents")]
    [RequestSizeLimit(64 * 1024 * 1024)]
    public async Task<ActionResult<KnowledgeDocumentDto>> Upload([FromForm] KnowledgeUploadRequest request, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (request.File is null || request.File.Length == 0) return BadRequest(new { error = "Fayl tanlanmadi." });

        await using var content = request.File.OpenReadStream();
        var invalid = await storage.ValidateAsync(content, request.File.FileName, request.File.Length, ct);
        if (invalid is not null) return BadRequest(new { error = invalid });

        var target = await ValidateTargetAsync(scope, request, ct);
        if (target is not null) return BadRequest(new { error = target });

        var storageKey = await storage.SaveAsync(content, request.File.FileName, ct);

        var title = string.IsNullOrWhiteSpace(request.Title) ? Path.GetFileNameWithoutExtension(request.File.FileName) : request.Title.Trim();
        var document = new KnowledgeDocument(scope.OrganizationId, scope.UserId,
            title.Length <= KnowledgeDocument.MaxTitleLength ? title : title[..KnowledgeDocument.MaxTitleLength],
            Path.GetFileName(request.File.FileName), KnowledgeFileStorage.ContentTypeFor(request.File.FileName),
            request.File.Length, storageKey, request.Scope, request.Visibility, request.BranchId, request.CourseId, request.GroupId);

        db.KnowledgeDocuments.Add(document);
        await db.SaveChangesAsync(ct);
        await queue.EnqueueKnowledgeIngestionAsync(document.Id);

        var saved = await db.KnowledgeDocuments.AsNoTracking().FirstAsync(d => d.Id == document.Id, ct);
        return CreatedAtAction(nameof(Get), new { id = document.Id }, (await ToDtosAsync([saved], ct)).Single());
    }

    [HttpGet("documents/{id:guid}")]
    public async Task<ActionResult<KnowledgeDocumentDto>> Get(Guid id, CancellationToken ct)
    {
        var document = await FindAsync(id, ct);
        return document is null ? NotFound() : Ok((await ToDtosAsync([document], ct)).Single());
    }

    /// <summary>The extracted text, so a staff member can see what a quarantined document actually contains before approving it.</summary>
    [HttpGet("documents/{id:guid}/preview")]
    public async Task<ActionResult<KnowledgePreviewDto>> Preview(Guid id, CancellationToken ct)
    {
        const int maxChars = 4000;
        var document = await FindAsync(id, ct);
        if (document is null) return NotFound();

        await using var file = storage.Open(document.StorageKey);
        if (file is null)
        {
            // The file was lost (uploads made before the API kept App_Data on a volume), but an indexed document still
            // holds its text in the chunks — show that instead of a dead end.
            var chunks = await db.KnowledgeChunks.AsNoTracking()
                .Where(c => c.DocumentId == id).OrderBy(c => c.Ordinal).Select(c => c.Text).ToListAsync(ct);
            if (chunks.Count == 0)
                return NotFound(new { error = "Asl fayl saqlash joyida topilmadi va hujjat indekslanmagan. Uni qayta yuklang." });

            var restored = KnowledgeChunker.Reassemble(chunks);
            return Ok(new KnowledgePreviewDto(restored.Length <= maxChars ? restored : restored[..maxChars], restored.Length > maxChars, true));
        }

        string text;
        try { text = DocumentTextExtractor.Extract(file, document.FileName); }
        catch (InvalidDataException e) { return BadRequest(new { error = e.Message }); }

        return Ok(new KnowledgePreviewDto(text.Length <= maxChars ? text : text[..maxChars], text.Length > maxChars, false));
    }

    /// <summary>The scopes, branches, courses and groups the caller may attach material to.</summary>
    [HttpGet("targets")]
    public async Task<ActionResult<KnowledgeTargetsDto>> Targets(CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        var reach = await ReachAsync(scope, ct);

        var branches = db.Branches.Where(b => b.OrganizationId == scope.OrganizationId);
        var courses = db.Courses.Where(c => c.OrganizationId == scope.OrganizationId);
        var groups = db.Groups.Where(g => g.OrganizationId == scope.OrganizationId);
        if (!reach.OrganizationWide)
        {
            branches = branches.Where(b => reach.AttachBranchIds.Contains(b.Id));
            courses = courses.Where(c => reach.CourseIds.Contains(c.Id));
            groups = groups.Where(g => reach.GroupIds.Contains(g.Id));
        }

        var branchList = await branches.OrderBy(b => b.Name).Select(b => new KnowledgeTargetDto(b.Id, b.Name)).ToListAsync(ct);
        var courseList = await courses.OrderBy(c => c.Name).Select(c => new KnowledgeTargetDto(c.Id, c.Name)).ToListAsync(ct);
        var groupList = await groups.OrderBy(g => g.Name).Select(g => new KnowledgeTargetDto(g.Id, g.Name)).ToListAsync(ct);

        var scopes = new List<KnowledgeScope>();
        if (reach.OrganizationWide) scopes.Add(KnowledgeScope.Organization);
        if (branchList.Count > 0) scopes.Add(KnowledgeScope.Branch);
        if (courseList.Count > 0) scopes.Add(KnowledgeScope.Course);
        if (groupList.Count > 0) scopes.Add(KnowledgeScope.Group);

        return Ok(new KnowledgeTargetsDto(scopes, branchList, courseList, groupList));
    }

    /// <summary>Clears a quarantine: the reviewed document is indexed, and its text is not scanned again.</summary>
    [HttpPost("documents/{id:guid}/approve")]
    public async Task<ActionResult<KnowledgeDocumentDto>> Approve(Guid id, CancellationToken ct)
    {
        var document = await FindAsync(id, tracked: true, ct);
        if (document is null) return NotFound();
        if (document.Status != KnowledgeDocumentStatus.Quarantined)
            return Conflict(new { error = "Faqat karantindagi hujjatni tasdiqlash mumkin." });

        // A hold for prompt injection is a judgement a person may overrule; a hold for malware is not one.
        if (KnowledgeIngestor.IsMalware(document.Error))
            return Conflict(new { error = "Zararli deb topilgan hujjatni tasdiqlab bo'lmaydi. Uni o'chiring." });

        document.WaiveInjectionCheck();
        await db.SaveChangesAsync(ct);
        await queue.EnqueueKnowledgeIngestionAsync(document.Id);

        return Ok((await ToDtosAsync([await db.KnowledgeDocuments.AsNoTracking().FirstAsync(d => d.Id == id, ct)], ct)).Single());
    }

    /// <summary>Re-runs the pipeline — after a failure, or to re-embed with a different model.</summary>
    [HttpPost("documents/{id:guid}/reingest")]
    public async Task<ActionResult<KnowledgeDocumentDto>> Reingest(Guid id, CancellationToken ct)
    {
        var document = await FindAsync(id, ct);
        if (document is null) return NotFound();

        // Re-running on a missing file would only flip a working, indexed document to "Failed".
        if (!storage.Exists(document.StorageKey))
            return Conflict(new { error = "Asl fayl saqlash joyida yo'q — qayta indekslab bo'lmaydi. Hujjatni qayta yuklang." });

        await queue.EnqueueKnowledgeIngestionAsync(document.Id);
        return Ok((await ToDtosAsync([await db.KnowledgeDocuments.AsNoTracking().FirstAsync(d => d.Id == id, ct)], ct)).Single());
    }

    [HttpDelete("documents/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var document = await FindAsync(id, tracked: true, ct);
        if (document is null) return NotFound();

        await db.KnowledgeChunks.Where(c => c.DocumentId == id).ExecuteDeleteAsync(ct);
        db.KnowledgeDocuments.Remove(document);
        await db.SaveChangesAsync(ct);
        storage.Delete(document.StorageKey);
        return NoContent();
    }

    [HttpGet("documents/{id:guid}/file")]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var document = await FindAsync(id, ct);
        if (document is null) return NotFound();

        var file = storage.Open(document.StorageKey);
        return file is null
            ? NotFound(new { error = "Fayl saqlash joyida topilmadi. Uni qayta yuklang." })
            : File(file, document.ContentType, document.FileName);
    }

    private Task<KnowledgeDocument?> FindAsync(Guid id, CancellationToken ct) => FindAsync(id, tracked: false, ct);

    private async Task<KnowledgeDocument?> FindAsync(Guid id, bool tracked, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        var query = tracked ? db.KnowledgeDocuments : db.KnowledgeDocuments.AsNoTracking();
        var document = await query.FirstOrDefaultAsync(d => d.Id == id && d.OrganizationId == scope.OrganizationId, ct);
        if (document is null) return null;

        var reach = await ReachAsync(scope, ct);
        if (reach.OrganizationWide) return document;

        var visible = document.Scope == KnowledgeScope.Organization
                      || (document.BranchId is { } branchId && reach.BranchIds.Contains(branchId))
                      || (document.CourseId is { } courseId && reach.CourseIds.Contains(courseId))
                      || (document.GroupId is { } groupId && reach.GroupIds.Contains(groupId));
        return visible ? document : null;
    }

    /// <returns>A problem describing why the caller may not attach material there, or null.</returns>
    private async Task<string?> ValidateTargetAsync(ResolvedScope scope, KnowledgeUploadRequest request, CancellationToken ct)
    {
        var reach = await ReachAsync(scope, ct);

        switch (request.Scope)
        {
            case KnowledgeScope.Organization:
                return reach.OrganizationWide ? null : "Butun tashkilot uchun material qo'shishga ruxsatingiz yo'q.";

            case KnowledgeScope.Branch:
                if (request.BranchId is null) return "Filial tanlanmadi.";
                return reach.OrganizationWide || reach.AttachBranchIds.Contains(request.BranchId.Value)
                    ? null
                    : "Bu filial uchun material qo'shishga ruxsatingiz yo'q.";

            case KnowledgeScope.Course:
                if (request.CourseId is null) return "Kurs tanlanmadi.";
                return reach.OrganizationWide || reach.CourseIds.Contains(request.CourseId.Value)
                    ? null
                    : "Bu kurs uchun material qo'shishga ruxsatingiz yo'q.";

            default:
                if (request.GroupId is null) return "Guruh tanlanmadi.";
                return reach.OrganizationWide || reach.GroupIds.Contains(request.GroupId.Value)
                    ? null
                    : "Bu guruh uchun material qo'shishga ruxsatingiz yo'q.";
        }
    }

    /// <param name="BranchIds">Branches whose material the caller may see.</param>
    /// <param name="AttachBranchIds">Branches the caller may attach material to — narrower than <paramref name="BranchIds"/>.</param>
    private sealed record Reach(
        bool OrganizationWide, HashSet<Guid> BranchIds, HashSet<Guid> AttachBranchIds, HashSet<Guid> CourseIds, HashSet<Guid> GroupIds);

    private async Task<Reach> ReachAsync(ResolvedScope scope, CancellationToken ct)
    {
        if (scope.Scope == ScopeType.AllOrganization) return new Reach(true, [], [], [], []);

        var groups = await (await scope.ScopedGroupsAsync(db))
            .Select(g => new { g.Id, g.BranchId, g.CourseId })
            .AsNoTracking()
            .ToListAsync(ct);

        var branches = scope.Scope == ScopeType.MyBranches
            ? scope.BranchIds.ToHashSet()
            : groups.Select(g => g.BranchId).ToHashSet();

        // Material for a whole branch is a branch-level decision. A teacher's group sits in a branch and they may read
        // what is attached to it, but the branch is not theirs to attach to — only their groups and those groups' courses.
        var attachBranches = scope.Scope == ScopeType.MyBranches ? branches : [];

        return new Reach(false, branches, attachBranches, groups.Select(g => g.CourseId).ToHashSet(), groups.Select(g => g.Id).ToHashSet());
    }

    private async Task<List<KnowledgeDocumentDto>> ToDtosAsync(IReadOnlyList<KnowledgeDocument> documents, CancellationToken ct)
    {
        if (documents.Count == 0) return [];

        var uploaderIds = documents.Select(d => d.UploadedBy).Distinct().ToList();
        var uploaders = await db.Users.Where(u => uploaderIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        var branchIds = documents.Where(d => d.BranchId != null).Select(d => d.BranchId!.Value).Distinct().ToList();
        var courseIds = documents.Where(d => d.CourseId != null).Select(d => d.CourseId!.Value).Distinct().ToList();
        var groupIds = documents.Where(d => d.GroupId != null).Select(d => d.GroupId!.Value).Distinct().ToList();
        var branches = await db.Branches.Where(b => branchIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Name, ct);
        var courses = await db.Courses.Where(c => courseIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var groups = await db.Groups.Where(g => groupIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, g => g.Name, ct);

        return documents.Select(d => new KnowledgeDocumentDto(d.Id, d.Title, d.FileName, d.ContentType, d.SizeBytes, d.Scope,
            d.Scope switch
            {
                KnowledgeScope.Branch => d.BranchId is { } b ? branches.GetValueOrDefault(b) : null,
                KnowledgeScope.Course => d.CourseId is { } c ? courses.GetValueOrDefault(c) : null,
                KnowledgeScope.Group => d.GroupId is { } g ? groups.GetValueOrDefault(g) : null,
                _ => "Butun tashkilot"
            },
            d.Visibility, d.BranchId, d.CourseId, d.GroupId, d.Status, d.Error, d.ChunkCount, d.EmbeddingModel,
            uploaders.GetValueOrDefault(d.UploadedBy, "—"), d.CreatedAt, d.ProcessedAt, storage.Exists(d.StorageKey),
            d.Status == KnowledgeDocumentStatus.Quarantined && KnowledgeIngestor.IsMalware(d.Error))).ToList();
    }
}
