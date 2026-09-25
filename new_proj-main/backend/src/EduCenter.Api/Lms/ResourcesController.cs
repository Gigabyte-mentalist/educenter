using System.ComponentModel.DataAnnotations;
using EduCenter.Api.Authorization;
using EduCenter.Infrastructure;
using EduCenter.Modules.Academic.Domain;
using EduCenter.Modules.Lms.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Lms;

public record ResourceFileDto(string FileName, string ContentType, long Size);

public record ResourceDto(
    Guid Id, Guid LessonId, string Title, string? Url, string? Description,
    ResourceFileDto? File, Guid CreatedBy, DateTimeOffset CreatedAt);

public record CreateResourceRequest(Guid LessonId,
    [Required, MaxLength(200)] string Title,
    [Required, MaxLength(2000)] string Url,
    [MaxLength(2000)] string? Description);

/// <summary>Multipart form for the upload variant — a link material still goes through the JSON endpoint.</summary>
public class UploadResourceForm
{
    public Guid LessonId { get; set; }
    [Required, MaxLength(200)] public string Title { get; set; } = default!;
    [MaxLength(2000)] public string? Description { get; set; }
    public IFormFile? File { get; set; }
}

[ApiController]
[Route("api/resources")]
public class ResourcesController(AppDbContext db, ResourceFileStorage files) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "resources.view")]
    public async Task<ActionResult<List<ResourceDto>>> List([FromQuery] Guid? lessonId, [FromQuery] Guid? groupId)
    {
        var scope = HttpContext.RequireResolvedScope();
        var groups = await scope.ScopedGroupsAsync(db);
        var query = from r in db.LearningResources
                    join l in db.Lessons on r.LessonId equals l.Id
                    join g in groups on l.GroupId equals g.Id
                    where r.OrganizationId == scope.OrganizationId && l.OrganizationId == scope.OrganizationId
                        && (lessonId == null || l.Id == lessonId) && (groupId == null || g.Id == groupId)
                    orderby r.CreatedAt descending, r.Id
                    select new ResourceDto(r.Id, r.LessonId, r.Title, r.Url, r.Description,
                        r.StorageKey == null ? null : new ResourceFileDto(r.FileName!, r.FileContentType!, r.FileSize!.Value),
                        r.CreatedBy, r.CreatedAt);
        return Ok(await query.ToListAsync());
    }

    [HttpPost]
    [Authorize(Policy = "resources.create")]
    public async Task<ActionResult<ResourceDto>> Create(CreateResourceRequest request)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (string.IsNullOrWhiteSpace(request.Title)
            || !Uri.TryCreate(request.Url.Trim(), UriKind.Absolute, out var url)
            || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp)
            || string.IsNullOrEmpty(url.Host) || !string.IsNullOrEmpty(url.UserInfo))
            return BadRequest(new { error = "Sarlavha va yaroqli HTTP/HTTPS havola kiriting." });

        var lesson = await LoadLessonAsync(request.LessonId, scope.OrganizationId);
        if (lesson is null) return NotFound();
        if (!await scope.CanAccessGroupAsync(db, lesson.GroupId, lesson.BranchId)) return Forbid();

        var resource = LearningResource.ForLink(scope.OrganizationId, lesson.Id, scope.UserId,
            request.Title.Trim(), request.Url.Trim(), request.Description?.Trim());
        db.LearningResources.Add(resource);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(List), new { lessonId = lesson.Id }, ToDto(resource));
    }

    /// <summary>Uploads the file itself — the slide deck used in class, rather than a link to it.</summary>
    [HttpPost("upload")]
    [Authorize(Policy = "resources.create")]
    [RequestSizeLimit(12 * 1024 * 1024)] // the 10 MB payload plus multipart overhead
    public async Task<ActionResult<ResourceDto>> Upload([FromForm] UploadResourceForm form, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (string.IsNullOrWhiteSpace(form.Title)) return BadRequest(new { error = "Sarlavha kiriting." });
        if (form.File is null) return BadRequest(new { error = "Fayl tanlanmagan." });
        if (await files.ValidateAsync(form.File, ct) is { } problem) return BadRequest(new { error = problem });

        var lesson = await LoadLessonAsync(form.LessonId, scope.OrganizationId);
        if (lesson is null) return NotFound();
        if (!await scope.CanAccessGroupAsync(db, lesson.GroupId, lesson.BranchId)) return Forbid();

        var saved = await files.SaveAsync(form.File, ct);
        var resource = LearningResource.ForFile(scope.OrganizationId, lesson.Id, scope.UserId,
            form.Title.Trim(), form.Description?.Trim(), saved.Key, saved.FileName, saved.ContentType, saved.Size);

        db.LearningResources.Add(resource);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            // The file is already on disk at this point; without this it would stay there with no row
            // pointing at it, which is exactly the leak the chat attachments used to have.
            files.Delete(saved.Key);
            throw;
        }

        return CreatedAtAction(nameof(List), new { lessonId = lesson.Id }, ToDto(resource));
    }

    [HttpGet("{id:guid}/file")]
    [Authorize(Policy = "resources.view")]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        var resource = await db.LearningResources
            .FirstOrDefaultAsync(r => r.Id == id && r.OrganizationId == scope.OrganizationId, ct);
        if (resource?.StorageKey is null) return NotFound();

        var lesson = await LoadLessonAsync(resource.LessonId, scope.OrganizationId);
        if (lesson is null) return NotFound();
        if (!await scope.CanAccessGroupAsync(db, lesson.GroupId, lesson.BranchId)) return Forbid();

        var path = files.PathFor(resource.StorageKey);
        if (path is null) return NotFound(new { error = "Fayl saqlash joyida topilmadi." });

        return PhysicalFile(path, resource.FileContentType ?? "application/octet-stream", resource.FileName);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "resources.delete")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        var resource = await db.LearningResources
            .FirstOrDefaultAsync(r => r.Id == id && r.OrganizationId == scope.OrganizationId, ct);
        if (resource is null) return NotFound();

        var lesson = await LoadLessonAsync(resource.LessonId, scope.OrganizationId);
        if (lesson is null) return NotFound();
        if (!await scope.CanAccessGroupAsync(db, lesson.GroupId, lesson.BranchId)) return Forbid();

        var key = resource.StorageKey;
        db.LearningResources.Remove(resource);
        await db.SaveChangesAsync(ct);
        if (key is not null) files.Delete(key);

        return NoContent();
    }

    private Task<Lesson?> LoadLessonAsync(Guid lessonId, Guid organizationId) =>
        db.Lessons.FirstOrDefaultAsync(l => l.Id == lessonId && l.OrganizationId == organizationId);

    private static ResourceDto ToDto(LearningResource r) => new(
        r.Id, r.LessonId, r.Title, r.Url, r.Description,
        r.StorageKey is null ? null : new ResourceFileDto(r.FileName!, r.FileContentType!, r.FileSize!.Value),
        r.CreatedBy, r.CreatedAt);
}
