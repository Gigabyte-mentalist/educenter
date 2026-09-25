using EduCenter.Api.Authorization;
using EduCenter.Api.Notifications;
using EduCenter.Infrastructure;
using EduCenter.Modules.Identity.Domain;
using EduCenter.Modules.Notifications.Domain;
using EduCenter.SharedKernel.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Users;

public record TeacherProfileDto(
    string? Bio, string? Specialization, int? YearsOfExperience, string? Education, string? Languages, DateOnly? TeachingSince);

public record CredentialFileDto(string FileName, string ContentType, long Size);

/// <param name="IsExpired">Past its <c>ExpiresOn</c> — still listed, marked, never in the headline.</param>
/// <param name="VerifiedByName">Who confirmed it; null when unverified or when that account has since been deleted.</param>
public record CredentialDto(
    Guid Id, CredentialType Type, string Title, string? Issuer, string? Score, DateOnly? IssuedOn, DateOnly? ExpiresOn,
    string? Reference, string? Note, CredentialFileDto? File,
    bool IsVerified, string? VerifiedByName, DateTimeOffset? VerifiedAt, bool IsExpired, DateTimeOffset CreatedAt);

/// <param name="VerificationReset">The edit took away a confirmation the credential had — the screen tells the teacher so.</param>
public record CredentialResultDto(CredentialDto Credential, bool VerificationReset);

public record ExperienceDto(Guid Id, string Employer, string Position, int FromYear, int? ToYear, string? Description);

/// <summary>A line of the header: "IELTS Academic 8.0".</summary>
public record HeadlineChipDto(string Title, string? Score);

public record TeacherPortfolioDto(
    TeacherProfileDto Profile, List<CredentialDto> Credentials, List<ExperienceDto> Experience, List<HeadlineChipDto> Headline);

/// <summary>A confirmed credential as a student or parent sees it: no reference, note, scan or verifier.</summary>
/// <param name="Id">Lets the reader ask for the scan; the file itself is only ever streamed to the viewer, and only for a confirmed credential.</param>
/// <param name="FileContentType">Null when there is no scan — no name, size or storage key is given.</param>
public record PublicCredentialDto(
    Guid Id, CredentialType Type, string Title, string? Issuer, string? Score, DateOnly? IssuedOn, DateOnly? ExpiresOn, bool IsExpired,
    string? FileContentType);

public record PublicPortfolioDto(
    Guid UserId, string FullName, string? PhotoVersion, TeacherProfileDto Profile,
    List<PublicCredentialDto> Credentials, List<ExperienceDto> Experience, List<HeadlineChipDto> Headline);

public record SaveProfileRequest(
    string? Bio, string? Specialization, int? YearsOfExperience, string? Education, string? Languages, DateOnly? TeachingSince);

/// <param name="Verified">Only for someone who may verify: an administrator entering a document they have already seen. Anyone else sending it gets a 403.</param>
public record SaveCredentialRequest(
    CredentialType? Type, string? Title, string? Issuer, string? Score, DateOnly? IssuedOn, DateOnly? ExpiresOn,
    string? Reference, string? Note, bool? Verified = null);

public record SaveExperienceRequest(string? Employer, string? Position, int? FromYear, int? ToYear, string? Description);

public class CredentialFileForm
{
    public IFormFile? File { get; set; }
}

/// <summary>
/// A teacher's portfolio (docs/21.09-audit-jurnali S05.02): who they are, what they are certified in, where they have worked.
/// Reading takes <c>teacher.portfolio.view</c>, writing <c>.edit</c>, confirming a certificate <c>.verify</c>. The reach is the one
/// that opens the teacher's page (<see cref="TeacherAccess"/>): themselves, or an administrator of a branch they work in — and a branch
/// administrator may not edit a Super Admin's or Developer's, even if that person also teaches (<see cref="UserAccess.CanTouchAsync"/>).
/// Certificate scans can be personal documents, so they are served to those people only, never cached.
/// </summary>
[ApiController]
[Route("api/teachers/{id:guid}")]
public class TeacherPortfolioController(
    AppDbContext db, TeacherCredentialFileStorage files, NotificationDispatcher notifier, NotificationRecipients recipients,
    PermissionResolver permissions) : ControllerBase
{
    private const string VerifyPermission = "teacher.portfolio.verify";

    // ── Reading ─────────────────────────────────────────────────────────────────────────────────────

    [HttpGet("portfolio")]
    [Authorize(Policy = "teacher.portfolio.view")]
    public async Task<ActionResult<TeacherPortfolioDto>> Get(Guid id, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (await DeniedAsync(scope, id, write: false, ct) is { } denied) return denied;

        var profile = await db.TeacherProfiles.Where(p => p.UserId == id).FirstOrDefaultAsync(ct);
        var credentials = await db.TeacherCredentials.Where(c => c.UserId == id)
            .OrderByDescending(c => c.IssuedOn).ThenByDescending(c => c.CreatedAt).ToListAsync(ct);
        var experience = await db.TeacherExperiences.Where(e => e.UserId == id)
            .OrderByDescending(e => e.ToYear == null).ThenByDescending(e => e.ToYear).ThenByDescending(e => e.FromYear).ToListAsync(ct);

        var today = Today();
        var verifierIds = credentials.Where(c => c.VerifiedBy != null).Select(c => c.VerifiedBy!.Value).Distinct().ToList();
        var verifierNames = await db.Users.Where(u => verifierIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        return Ok(new TeacherPortfolioDto(
            ToDto(profile),
            credentials.Select(c => ToDto(c, c.VerifiedBy is { } by ? verifierNames.GetValueOrDefault(by) : null, today)).ToList(),
            experience.Select(e => ToDto(e)).ToList(),
            HeadlineOf(credentials, today)));
    }

    /// <summary>"Mening o'qituvchim": what a student (or a parent) may read about the teacher of their own group — the <b>confirmed</b> facts
    /// only, without the reference, the note, the scan or who confirmed it. Reach: the teacher staffs a group one of the caller's students
    /// (themselves, or their children) is actively enrolled in — a teacher of another group stays closed.</summary>
    [HttpGet("portfolio/public")]
    [Authorize(Policy = "teacher.portfolio.view.public")]
    public async Task<ActionResult<PublicPortfolioDto>> GetPublic(Guid id, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        var teacher = await db.TeacherUsers()
            .Where(u => u.Id == id && u.OrganizationId == scope.OrganizationId)
            .Select(u => new { u.FullName, u.PhotoKey }).FirstOrDefaultAsync(ct);
        if (teacher is null) return NotFound();

        if (!await TeachesCallersStudentAsync(scope, id, ct)) return Forbid();

        var today = Today();
        var profile = await db.TeacherProfiles.Where(p => p.UserId == id).FirstOrDefaultAsync(ct);
        var confirmed = await db.TeacherCredentials.Where(c => c.UserId == id && c.IsVerified)
            .OrderByDescending(c => c.IssuedOn).ThenByDescending(c => c.CreatedAt).ToListAsync(ct);
        var experience = await db.TeacherExperiences.Where(e => e.UserId == id)
            .OrderByDescending(e => e.ToYear == null).ThenByDescending(e => e.ToYear).ThenByDescending(e => e.FromYear).ToListAsync(ct);

        return Ok(new PublicPortfolioDto(
            id, teacher.FullName, teacher.PhotoKey is { } key ? UserPhotoStorage.VersionOf(key) : null, ToDto(profile),
            confirmed.Select(c => new PublicCredentialDto(c.Id, c.Type, c.Title, c.Issuer, c.Score, c.IssuedOn, c.ExpiresOn, c.IsExpired(today), c.FileContentType)).ToList(),
            experience.Select(e => ToDto(e)).ToList(),
            HeadlineOf(confirmed, today)));
    }

    /// <summary>The scan of a <b>confirmed</b> credential, for the student or parent whose reach covers this teacher — shown in the app's
    /// viewer. Same reach as <see cref="GetPublic"/>; an unconfirmed credential's scan stays with the teacher and the office.</summary>
    [HttpGet("portfolio/public/credentials/{cid:guid}/file")]
    [Authorize(Policy = "teacher.portfolio.view.public")]
    public async Task<IActionResult> DownloadPublicFile(Guid id, Guid cid, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (!await TeachesCallersStudentAsync(scope, id, ct)) return Forbid();
        var credential = await FindCredentialAsync(scope, id, cid, ct);
        if (credential is not { IsVerified: true, StorageKey: not null }) return NotFound();

        var path = files.PathFor(credential.StorageKey);
        if (path is null) return NotFound(new { error = "Fayl saqlash joyida topilmadi." });

        Response.Headers.CacheControl = "no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        return PhysicalFile(path, credential.FileContentType ?? "application/octet-stream");
    }

    /// <summary>The teacher staffs a group one of the caller's students (themselves, or their children) is actively enrolled in.</summary>
    private async Task<bool> TeachesCallersStudentAsync(ResolvedScope scope, Guid teacherId, CancellationToken ct)
    {
        var students = scope.ScopedStudentIds(db);
        if (students is null) return false;
        return await (
            from e in db.Enrollments
            join gs in db.GroupStaff on e.GroupId equals gs.GroupId
            where gs.UserId == teacherId && e.Status == Modules.Students.Domain.EnrollmentStatus.Active && students.Contains(e.StudentId)
            select e.Id).AnyAsync(ct);
    }

    // ── About ───────────────────────────────────────────────────────────────────────────────────────

    [HttpPut("profile")]
    [Authorize(Policy = "teacher.portfolio.edit")]
    public async Task<ActionResult<TeacherProfileDto>> SaveProfile(Guid id, SaveProfileRequest request, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (await DeniedAsync(scope, id, write: true, ct) is { } denied) return denied;

        var bio = Clean(request.Bio);
        var specialization = Clean(request.Specialization);
        var education = Clean(request.Education);
        var languages = Clean(request.Languages);
        if (TooLong(bio, TeacherProfile.MaxBio, "Tanishtiruv") is { } e1) return BadRequest(new { error = e1 });
        if (TooLong(specialization, TeacherProfile.MaxSpecialization, "Ixtisoslik") is { } e2) return BadRequest(new { error = e2 });
        if (TooLong(education, TeacherProfile.MaxEducation, "Ta'lim") is { } e3) return BadRequest(new { error = e3 });
        if (TooLong(languages, TeacherProfile.MaxLanguages, "Tillar") is { } e4) return BadRequest(new { error = e4 });
        if (request.YearsOfExperience is < 0 or > TeacherProfile.MaxYearsOfExperience)
            return BadRequest(new { error = $"Tajriba yili 0 dan {TeacherProfile.MaxYearsOfExperience} gacha bo'lishi kerak." });
        if (request.TeachingSince is { } since && since > Today())
            return BadRequest(new { error = "O'qita boshlagan sana kelajakda bo'lishi mumkin emas." });

        var profile = await db.TeacherProfiles.FirstOrDefaultAsync(p => p.UserId == id, ct);
        if (profile is null)
        {
            profile = new TeacherProfile(scope.OrganizationId, id);
            db.TeacherProfiles.Add(profile);
        }
        profile.Update(bio, specialization, request.YearsOfExperience, education, languages, request.TeachingSince);
        await db.SaveChangesAsync(ct);
        return Ok(ToDto(profile));
    }

    // ── Credentials ─────────────────────────────────────────────────────────────────────────────────

    [HttpPost("credentials")]
    [Authorize(Policy = "teacher.portfolio.edit")]
    public async Task<ActionResult<CredentialResultDto>> AddCredential(Guid id, SaveCredentialRequest request, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (await DeniedAsync(scope, id, write: true, ct) is { } denied) return denied;

        var (fields, problem) = ValidateCredential(request);
        if (fields is null) return BadRequest(new { error = problem });

        if (await db.TeacherCredentials.CountAsync(c => c.UserId == id, ct) >= TeacherCredential.MaxPerTeacher)
            return Conflict(new { error = $"Bitta o'qituvchiga {TeacherCredential.MaxPerTeacher} tadan ortiq yozuv qo'shib bo'lmaydi." });

        var verifyNow = request.Verified == true;
        if (verifyNow && !await CanVerifyAsync(scope.UserId, id, ct)) return Forbid();

        var credential = new TeacherCredential(scope.OrganizationId, id, fields.Type, fields.Title, fields.Issuer, fields.Score,
            fields.IssuedOn, fields.ExpiresOn, fields.Reference, fields.Note);
        if (verifyNow) credential.Verify(scope.UserId);
        db.TeacherCredentials.Add(credential);

        if (!verifyNow) await NotifyOfficeAsync(scope, id, credential, "yangi ma'lumot qo'shdi", $"credential-submitted:{credential.Id}", ct);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id }, new CredentialResultDto(await ToDtoAsync(credential), false));
    }

    /// <summary>Saves the whole form (the drawer sends every field). Changing anything an administrator confirmed — the title, issuer,
    /// score, dates, reference — takes the confirmation away; changing only the note keeps it.</summary>
    [HttpPatch("credentials/{cid:guid}")]
    [Authorize(Policy = "teacher.portfolio.edit")]
    public async Task<ActionResult<CredentialResultDto>> UpdateCredential(Guid id, Guid cid, SaveCredentialRequest request, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (await DeniedAsync(scope, id, write: true, ct) is { } denied) return denied;
        var credential = await FindCredentialAsync(scope, id, cid, ct);
        if (credential is null) return NotFound();

        var (fields, problem) = ValidateCredential(request);
        if (fields is null) return BadRequest(new { error = problem });
        if (request.Verified is not null) return BadRequest(new { error = "Tasdiqlash uchun alohida amal bor: /verify." });

        var reset = credential.Update(fields.Type, fields.Title, fields.Issuer, fields.Score,
            fields.IssuedOn, fields.ExpiresOn, fields.Reference, fields.Note);
        if (reset)
            await NotifyOfficeAsync(scope, id, credential, "tasdiqlangan ma'lumotni o'zgartirdi — qayta tasdiqlash kerak", null, ct);
        await db.SaveChangesAsync(ct);

        return Ok(new CredentialResultDto(await ToDtoAsync(credential), reset));
    }

    [HttpDelete("credentials/{cid:guid}")]
    [Authorize(Policy = "teacher.portfolio.edit")]
    public async Task<IActionResult> DeleteCredential(Guid id, Guid cid, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (await DeniedAsync(scope, id, write: true, ct) is { } denied) return denied;
        var credential = await FindCredentialAsync(scope, id, cid, ct);
        if (credential is null) return NotFound();

        var key = credential.StorageKey;
        db.TeacherCredentials.Remove(credential);
        await db.SaveChangesAsync(ct);
        files.Delete(key);
        return NoContent();
    }

    // ── The scan of a credential ────────────────────────────────────────────────────────────────────

    /// <summary>Puts the scan in — or replaces it. The file is part of what an administrator confirmed, so a new one withdraws the confirmation.</summary>
    [HttpPost("credentials/{cid:guid}/file")]
    [Authorize(Policy = "teacher.portfolio.edit")]
    [RequestSizeLimit(12 * 1024 * 1024)]
    public async Task<ActionResult<CredentialResultDto>> UploadFile(Guid id, Guid cid, [FromForm] CredentialFileForm form, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (await DeniedAsync(scope, id, write: true, ct) is { } denied) return denied;
        var credential = await FindCredentialAsync(scope, id, cid, ct);
        if (credential is null) return NotFound();

        if (form.File is null) return BadRequest(new { error = "Fayl tanlanmagan." });
        if (await files.ValidateAsync(form.File, ct) is { } problem) return BadRequest(new { error = problem });

        var saved = await files.SaveAsync(form.File, ct);
        var previous = credential.StorageKey;
        var reset = credential.AttachFile(saved.Key, saved.FileName, saved.ContentType, saved.Size);
        if (reset)
            await NotifyOfficeAsync(scope, id, credential, "tasdiqlangan hujjatni almashtirdi — qayta tasdiqlash kerak", null, ct);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            files.Delete(saved.Key);
            throw;
        }

        files.Delete(previous);
        return Ok(new CredentialResultDto(await ToDtoAsync(credential), reset));
    }

    /// <summary>The scan itself, fetched with the caller's token. Only the teacher and the office reach this policy; the response is never
    /// cached, and served as the stored type with sniffing off.</summary>
    [HttpGet("credentials/{cid:guid}/file")]
    [Authorize(Policy = "teacher.portfolio.view")]
    public async Task<IActionResult> DownloadFile(Guid id, Guid cid, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (await DeniedAsync(scope, id, write: false, ct) is { } denied) return denied;
        var credential = await FindCredentialAsync(scope, id, cid, ct);
        if (credential?.StorageKey is null) return NotFound();

        var path = files.PathFor(credential.StorageKey);
        if (path is null) return NotFound(new { error = "Fayl saqlash joyida topilmadi." });

        Response.Headers.CacheControl = "no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        return PhysicalFile(path, credential.FileContentType ?? "application/octet-stream", credential.FileName);
    }

    [HttpDelete("credentials/{cid:guid}/file")]
    [Authorize(Policy = "teacher.portfolio.edit")]
    public async Task<ActionResult<CredentialResultDto>> DeleteFile(Guid id, Guid cid, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (await DeniedAsync(scope, id, write: true, ct) is { } denied) return denied;
        var credential = await FindCredentialAsync(scope, id, cid, ct);
        if (credential is null) return NotFound();

        var key = credential.DetachFile(out var reset);
        if (reset)
            await NotifyOfficeAsync(scope, id, credential, "tasdiqlangan hujjatni olib tashladi — qayta tasdiqlash kerak", null, ct);
        await db.SaveChangesAsync(ct);
        files.Delete(key);
        return Ok(new CredentialResultDto(await ToDtoAsync(credential), reset));
    }

    // ── Confirming ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>The administrator has looked at the scan or the reference and vouches for it. Nobody confirms their own credential —
    /// except the organization's top scope, which has nobody above it to ask.</summary>
    [HttpPost("credentials/{cid:guid}/verify")]
    [Authorize(Policy = VerifyPermission)]
    public async Task<ActionResult<CredentialDto>> Verify(Guid id, Guid cid, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (id == scope.UserId && scope.Scope != ScopeType.AllOrganization) return Forbid();
        if (await DeniedAsync(scope, id, write: true, ct) is { } denied) return denied;
        var credential = await FindCredentialAsync(scope, id, cid, ct);
        if (credential is null) return NotFound();

        if (!credential.IsVerified)
        {
            credential.Verify(scope.UserId);
            await notifier.NotifyAsync(scope.OrganizationId, [id],
                new NotificationMessage(NotificationType.CredentialVerified, "Portfolio tasdiqlandi",
                    $"«{credential.Title}» ma'lumotingiz tasdiqlandi.", $"/teachers/{id}"), ct);
            await db.SaveChangesAsync(ct);
        }
        return Ok((await ToDtoAsync(credential)));
    }

    [HttpPost("credentials/{cid:guid}/unverify")]
    [Authorize(Policy = VerifyPermission)]
    public async Task<ActionResult<CredentialDto>> Unverify(Guid id, Guid cid, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (id == scope.UserId && scope.Scope != ScopeType.AllOrganization) return Forbid();
        if (await DeniedAsync(scope, id, write: true, ct) is { } denied) return denied;
        var credential = await FindCredentialAsync(scope, id, cid, ct);
        if (credential is null) return NotFound();

        if (credential.IsVerified)
        {
            credential.Unverify();
            await db.SaveChangesAsync(ct);
        }
        return Ok(await ToDtoAsync(credential));
    }

    // ── Experience ──────────────────────────────────────────────────────────────────────────────────

    [HttpPost("experience")]
    [Authorize(Policy = "teacher.portfolio.edit")]
    public async Task<ActionResult<ExperienceDto>> AddExperience(Guid id, SaveExperienceRequest request, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (await DeniedAsync(scope, id, write: true, ct) is { } denied) return denied;

        var (fields, problem) = ValidateExperience(request);
        if (fields is null) return BadRequest(new { error = problem });
        if (await db.TeacherExperiences.CountAsync(e => e.UserId == id, ct) >= TeacherExperience.MaxPerTeacher)
            return Conflict(new { error = $"Bitta o'qituvchiga {TeacherExperience.MaxPerTeacher} tadan ortiq tajriba yozuvi qo'shib bo'lmaydi." });

        var experience = new TeacherExperience(scope.OrganizationId, id, fields.Employer, fields.Position, fields.FromYear, fields.ToYear, fields.Description);
        db.TeacherExperiences.Add(experience);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id }, ToDto(experience));
    }

    [HttpPatch("experience/{eid:guid}")]
    [Authorize(Policy = "teacher.portfolio.edit")]
    public async Task<ActionResult<ExperienceDto>> UpdateExperience(Guid id, Guid eid, SaveExperienceRequest request, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (await DeniedAsync(scope, id, write: true, ct) is { } denied) return denied;
        var experience = await db.TeacherExperiences.FirstOrDefaultAsync(
            e => e.Id == eid && e.UserId == id && e.OrganizationId == scope.OrganizationId, ct);
        if (experience is null) return NotFound();

        var (fields, problem) = ValidateExperience(request);
        if (fields is null) return BadRequest(new { error = problem });

        experience.Update(fields.Employer, fields.Position, fields.FromYear, fields.ToYear, fields.Description);
        await db.SaveChangesAsync(ct);
        return Ok(ToDto(experience));
    }

    [HttpDelete("experience/{eid:guid}")]
    [Authorize(Policy = "teacher.portfolio.edit")]
    public async Task<IActionResult> DeleteExperience(Guid id, Guid eid, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (await DeniedAsync(scope, id, write: true, ct) is { } denied) return denied;
        var experience = await db.TeacherExperiences.FirstOrDefaultAsync(
            e => e.Id == eid && e.UserId == id && e.OrganizationId == scope.OrganizationId, ct);
        if (experience is null) return NotFound();

        db.TeacherExperiences.Remove(experience);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Access ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Null when the caller may read (or, with <paramref name="write"/>, change) this teacher's portfolio; otherwise the response.</summary>
    private async Task<ActionResult?> DeniedAsync(ResolvedScope scope, Guid teacherId, bool write, CancellationToken ct)
    {
        var access = await db.CheckTeacherAccessAsync(scope, teacherId, ct);
        if (access == TeacherAccessResult.NotFound) return NotFound();
        if (access == TeacherAccessResult.Forbidden) return Forbid();
        if (!write) return null;

        var teacher = await db.Users.Include(u => u.Roles).Include(u => u.Branches).FirstAsync(u => u.Id == teacherId, ct);
        return await scope.CanTouchAsync(db, teacher, ct) ? null : Forbid();
    }

    /// <summary>Whether this caller holds the right to confirm certificates for this teacher — used when an administrator enters a
    /// credential already confirmed. Same rule as the verify endpoint: not for oneself (short of the top scope), and within their branches.</summary>
    private async Task<bool> CanVerifyAsync(Guid callerId, Guid teacherId, CancellationToken ct)
    {
        var verify = await permissions.ResolveAsync(callerId, VerifyPermission, ct);
        if (verify is null) return false;
        if (verify.Scope == ScopeType.AllOrganization) return true;
        return verify.Scope == ScopeType.MyBranches && teacherId != callerId
            && await db.UserBranches.AnyAsync(b => b.UserId == teacherId && verify.BranchIds.Contains(b.BranchId), ct);
    }

    private Task<TeacherCredential?> FindCredentialAsync(ResolvedScope scope, Guid teacherId, Guid credentialId, CancellationToken ct) =>
        db.TeacherCredentials.FirstOrDefaultAsync(
            c => c.Id == credentialId && c.UserId == teacherId && c.OrganizationId == scope.OrganizationId, ct);

    // ── Notifications ───────────────────────────────────────────────────────────────────────────────

    /// <summary>Tells the administrators of the teacher's branches that something waits for their confirmation — only when the teacher did
    /// it themselves; an administrator editing a portfolio needs no telling.</summary>
    private async Task NotifyOfficeAsync(ResolvedScope scope, Guid teacherId, TeacherCredential credential, string what, string? dedupeKey, CancellationToken ct)
    {
        if (scope.UserId != teacherId) return;

        var branchIds = await db.UserBranches.Where(b => b.UserId == teacherId).Select(b => b.BranchId).ToListAsync(ct);
        var administrators = new HashSet<Guid>();
        foreach (var branchId in branchIds)
            administrators.UnionWith(await recipients.BranchAdministratorsAsync(scope.OrganizationId, branchId));
        administrators.Remove(scope.UserId);
        if (administrators.Count == 0) return;

        var name = await recipients.UserNameAsync(teacherId);
        await notifier.NotifyAsync(scope.OrganizationId, administrators.ToList(),
            new NotificationMessage(NotificationType.CredentialSubmitted, "Portfolio: tasdiqlash kutilmoqda",
                $"{name} «{credential.Title}» — {what}.", $"/teachers/{teacherId}", dedupeKey), ct);
    }

    // ── Validation and mapping ──────────────────────────────────────────────────────────────────────

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? TooLong(string? value, int max, string label) =>
        value is not null && value.Length > max ? $"{label} {max} belgidan oshmasin." : null;

    private sealed record CredentialFields(
        CredentialType Type, string Title, string? Issuer, string? Score, DateOnly? IssuedOn, DateOnly? ExpiresOn, string? Reference, string? Note);

    private static (CredentialFields? Fields, string? Problem) ValidateCredential(SaveCredentialRequest request)
    {
        if (request.Type is not { } type || !Enum.IsDefined(type)) return (null, "Turini tanlang.");
        var title = Clean(request.Title);
        if (title is null) return (null, "Nomini kiriting (masalan, «IELTS Academic»).");
        var issuer = Clean(request.Issuer);
        var score = Clean(request.Score);
        var reference = Clean(request.Reference);
        var note = Clean(request.Note);

        if (TooLong(title, TeacherCredential.MaxTitle, "Nom") is { } e1) return (null, e1);
        if (TooLong(issuer, TeacherCredential.MaxIssuer, "Beruvchi") is { } e2) return (null, e2);
        if (TooLong(score, TeacherCredential.MaxScore, "Ball") is { } e3) return (null, e3);
        if (TooLong(reference, TeacherCredential.MaxReference, "Tekshirish havolasi/raqami") is { } e4) return (null, e4);
        if (TooLong(note, TeacherCredential.MaxNote, "Izoh") is { } e5) return (null, e5);
        if (type == CredentialType.ExamScore && score is null) return (null, "Imtihon uchun ballni kiriting (masalan, «8.0»).");
        if (request.IssuedOn is { } issued && request.ExpiresOn is { } expires && expires < issued)
            return (null, "Amal qilish muddati berilgan sanadan oldin bo'lishi mumkin emas.");

        return (new CredentialFields(type, title, issuer, score, request.IssuedOn, request.ExpiresOn, reference, note), null);
    }

    private sealed record ExperienceFields(string Employer, string Position, int FromYear, int? ToYear, string? Description);

    private static (ExperienceFields? Fields, string? Problem) ValidateExperience(SaveExperienceRequest request)
    {
        var employer = Clean(request.Employer);
        var position = Clean(request.Position);
        var description = Clean(request.Description);
        if (employer is null || position is null) return (null, "Ish joyi va lavozimni kiriting.");
        if (TooLong(employer, TeacherExperience.MaxEmployer, "Ish joyi") is { } e1) return (null, e1);
        if (TooLong(position, TeacherExperience.MaxPosition, "Lavozim") is { } e2) return (null, e2);
        if (TooLong(description, TeacherExperience.MaxDescription, "Izoh") is { } e3) return (null, e3);

        var thisYear = Today().Year;
        if (request.FromYear is not { } from || from < 1950 || from > thisYear) return (null, $"Boshlangan yil 1950 dan {thisYear} gacha bo'lishi kerak.");
        if (request.ToYear is { } to && (to < from || to > thisYear)) return (null, "Tugagan yil boshlangan yildan oldin bo'lmasin va kelajakda bo'lmasin.");

        return (new ExperienceFields(employer, position, from, request.ToYear, description), null);
    }

    /// <summary>The newest three <b>confirmed</b> exam scores that have not lapsed — what the header of the teacher's page boasts about.</summary>
    public static List<HeadlineChipDto> HeadlineOf(IEnumerable<TeacherCredential> credentials, DateOnly today) =>
        credentials
            .Where(c => c is { Type: CredentialType.ExamScore, IsVerified: true } && !c.IsExpired(today))
            .OrderByDescending(c => c.IssuedOn).ThenByDescending(c => c.CreatedAt)
            .Take(3)
            .Select(c => new HeadlineChipDto(c.Title, c.Score))
            .ToList();

    private async Task<CredentialDto> ToDtoAsync(TeacherCredential c)
    {
        var verifier = c.VerifiedBy is { } by ? await db.Users.Where(u => u.Id == by).Select(u => u.FullName).FirstOrDefaultAsync() : null;
        return ToDto(c, verifier, Today());
    }

    private static CredentialDto ToDto(TeacherCredential c, string? verifiedByName, DateOnly today) => new(
        c.Id, c.Type, c.Title, c.Issuer, c.Score, c.IssuedOn, c.ExpiresOn, c.Reference, c.Note,
        c.StorageKey is null ? null : new CredentialFileDto(c.FileName!, c.FileContentType!, c.FileSize!.Value),
        c.IsVerified, verifiedByName, c.VerifiedAt, c.IsExpired(today), c.CreatedAt);

    private static TeacherProfileDto ToDto(TeacherProfile? p) => p is null
        ? new TeacherProfileDto(null, null, null, null, null, null)
        : new TeacherProfileDto(p.Bio, p.Specialization, p.YearsOfExperience, p.Education, p.Languages, p.TeachingSince);

    private static ExperienceDto ToDto(TeacherExperience e) => new(e.Id, e.Employer, e.Position, e.FromYear, e.ToYear, e.Description);
}
