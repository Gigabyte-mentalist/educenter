using System.ComponentModel.DataAnnotations;
using EduCenter.Api.Authorization;
using EduCenter.Infrastructure;
using EduCenter.Modules.Students.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Students;

public record StudentDocumentFileDto(string FileName, string ContentType, long Size);

public record StudentDocumentDto(
    Guid Id, Guid StudentId, string Title, string? Url, string? Description,
    StudentDocumentFileDto? File, Guid UploadedBy, DateTimeOffset CreatedAt);

public record CreateStudentDocumentRequest(Guid StudentId,
    [Required, MaxLength(200)] string Title,
    [Required, MaxLength(2000)] string Url,
    [MaxLength(2000)] string? Description);

/// <summary>Multipart form for the upload variant — a link document still goes through the JSON endpoint.</summary>
public class UploadStudentDocumentForm
{
    public Guid StudentId { get; set; }
    [Required, MaxLength(200)] public string Title { get; set; } = default!;
    [MaxLength(2000)] public string? Description { get; set; }
    public IFormFile? File { get; set; }
}

/// <summary>Student 360° Documents tab (docs/00_TZ.md §7, audit §3.7): a passport scan, a signed contract, a
/// payment receipt attached to the student's own profile — same link-or-file shape as lesson resources.</summary>
[ApiController]
[Route("api/student-documents")]
public class StudentDocumentsController(AppDbContext db, StudentDocumentFileStorage files) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "student.documents.view")]
    public async Task<ActionResult<List<StudentDocumentDto>>> List([FromQuery] Guid studentId, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (!await scope.CanAccessStudentAsync(db, studentId)) return Forbid();

        var documents = await db.StudentDocuments
            .Where(d => d.OrganizationId == scope.OrganizationId && d.StudentId == studentId)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(ct);
        return Ok(documents.Select(ToDto).ToList());
    }

    [HttpPost]
    [Authorize(Policy = "student.documents.manage")]
    public async Task<ActionResult<StudentDocumentDto>> Create(CreateStudentDocumentRequest request, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (string.IsNullOrWhiteSpace(request.Title)
            || !Uri.TryCreate(request.Url.Trim(), UriKind.Absolute, out var url)
            || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp)
            || string.IsNullOrEmpty(url.Host) || !string.IsNullOrEmpty(url.UserInfo))
            return BadRequest(new { error = "Sarlavha va yaroqli HTTP/HTTPS havola kiriting." });

        if (!await scope.CanAccessStudentAsync(db, request.StudentId)) return Forbid();
        if (!await db.Students.AnyAsync(s => s.Id == request.StudentId && s.OrganizationId == scope.OrganizationId, ct))
            return NotFound();

        var document = StudentDocument.ForLink(scope.OrganizationId, request.StudentId, scope.UserId,
            request.Title.Trim(), request.Url.Trim(), request.Description?.Trim());
        db.StudentDocuments.Add(document);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(List), new { studentId = request.StudentId }, ToDto(document));
    }

    /// <summary>Uploads the file itself — the scanned passport or contract, rather than a link to it.</summary>
    [HttpPost("upload")]
    [Authorize(Policy = "student.documents.manage")]
    [RequestSizeLimit(12 * 1024 * 1024)]
    public async Task<ActionResult<StudentDocumentDto>> Upload([FromForm] UploadStudentDocumentForm form, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (string.IsNullOrWhiteSpace(form.Title)) return BadRequest(new { error = "Sarlavha kiriting." });
        if (form.File is null) return BadRequest(new { error = "Fayl tanlanmagan." });
        if (await files.ValidateAsync(form.File, ct) is { } problem) return BadRequest(new { error = problem });

        if (!await scope.CanAccessStudentAsync(db, form.StudentId)) return Forbid();
        if (!await db.Students.AnyAsync(s => s.Id == form.StudentId && s.OrganizationId == scope.OrganizationId, ct))
            return NotFound();

        var saved = await files.SaveAsync(form.File, ct);
        var document = StudentDocument.ForFile(scope.OrganizationId, form.StudentId, scope.UserId,
            form.Title.Trim(), form.Description?.Trim(), saved.Key, saved.FileName, saved.ContentType, saved.Size);

        db.StudentDocuments.Add(document);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            files.Delete(saved.Key);
            throw;
        }

        return CreatedAtAction(nameof(List), new { studentId = form.StudentId }, ToDto(document));
    }

    [HttpGet("{id:guid}/file")]
    [Authorize(Policy = "student.documents.view")]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        var document = await db.StudentDocuments
            .FirstOrDefaultAsync(d => d.Id == id && d.OrganizationId == scope.OrganizationId, ct);
        if (document?.StorageKey is null) return NotFound();
        if (!await scope.CanAccessStudentAsync(db, document.StudentId)) return Forbid();

        var path = files.PathFor(document.StorageKey);
        if (path is null) return NotFound(new { error = "Fayl saqlash joyida topilmadi." });

        return PhysicalFile(path, document.FileContentType ?? "application/octet-stream", document.FileName);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "student.documents.manage")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        var document = await db.StudentDocuments
            .FirstOrDefaultAsync(d => d.Id == id && d.OrganizationId == scope.OrganizationId, ct);
        if (document is null) return NotFound();
        if (!await scope.CanAccessStudentAsync(db, document.StudentId)) return Forbid();

        var key = document.StorageKey;
        db.StudentDocuments.Remove(document);
        await db.SaveChangesAsync(ct);
        if (key is not null) files.Delete(key);

        return NoContent();
    }

    private static StudentDocumentDto ToDto(StudentDocument d) => new(
        d.Id, d.StudentId, d.Title, d.Url, d.Description,
        d.StorageKey is null ? null : new StudentDocumentFileDto(d.FileName!, d.FileContentType!, d.FileSize!.Value),
        d.UploadedBy, d.CreatedAt);
}
