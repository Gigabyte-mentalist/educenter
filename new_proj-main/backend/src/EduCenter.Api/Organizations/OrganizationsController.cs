using System.Security.Claims;
using EduCenter.Api.Authorization;
using EduCenter.Infrastructure;
using EduCenter.Modules.Organizations.Domain;
using EduCenter.SharedKernel.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Organizations;

public record OrganizationDto(Guid Id, string Name, string TenantSlug, int BranchCount);
public record CreateOrganizationRequest(string Name, string TenantSlug);

/// <summary>The organization as everyone signed in may see it — the name, logo and colour on every screen and the currency on every
/// price. No slug, branch count or limits: those belong to the office.</summary>
/// <param name="LogoVersion">Null while there is no logo; otherwise changes with every new one (the address carries it as <c>?v=</c>).</param>
public record OrganizationSettingsDto(Guid Id, string Name, string Currency, string WorkdayStart, string WorkdayEnd, string? BrandColor, string? LogoVersion);

/// <param name="BrandColor">Null keeps the colour; an empty string goes back to the product's own.</param>
public record UpdateOrganizationRequest(string? Name, string? Currency, string? WorkdayStart, string? WorkdayEnd, string? BrandColor);

/// <summary>Multipart form of the upload: one image under the field <c>file</c>.</summary>
public class OrganizationLogoForm
{
    public IFormFile? File { get; set; }
}

[ApiController]
[Route("api/organizations")]
public class OrganizationsController(AppDbContext db, OrganizationLogoStorage logos) : ControllerBase
{
    [HttpGet("{id:guid}")]
    [Authorize(Policy = "branch.view")]
    public async Task<ActionResult<OrganizationDto>> GetById(Guid id)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (scope.Scope != ScopeType.AllOrganization || scope.OrganizationId != id)
            return Forbid();

        var org = await db.Organizations
            .Where(o => o.Id == id)
            .Select(o => new OrganizationDto(o.Id, o.Name, o.TenantSlug, o.Branches.Count))
            .FirstOrDefaultAsync();

        return org is null ? NotFound() : Ok(org);
    }

    [HttpPost]
    [Authorize(Policy = "branch.create")]
    public async Task<ActionResult<OrganizationDto>> Create(CreateOrganizationRequest request)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (scope.Scope != ScopeType.AllOrganization)
            return Forbid();

        var org = new Organization(request.Name, request.TenantSlug);
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = org.Id },
            new OrganizationDto(org.Id, org.Name, org.TenantSlug, 0));
    }

    /// <summary>The caller's own organization — the one to paint the shell with and to format money in (docs/21.09-audit-jurnali S07.01).
    /// Any signed-in person may read it; students and parents see the logo too.</summary>
    [HttpGet("current")]
    [Authorize]
    public async Task<ActionResult<OrganizationSettingsDto>> Current(CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue("org"), out var organizationId)) return Unauthorized();

        var org = await db.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == organizationId, ct);
        return org is null ? NotFound() : Ok(ToDto(org));
    }

    [HttpPatch("{id:guid}")]
    [Authorize(Policy = "org.settings.edit")]
    public async Task<ActionResult<OrganizationSettingsDto>> Update(Guid id, UpdateOrganizationRequest request, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (scope.Scope != ScopeType.AllOrganization || scope.OrganizationId != id) return Forbid();

        var org = await db.Organizations.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (org is null) return NotFound();

        var name = request.Name?.Trim();
        if (name is not null && (name.Length == 0 || name.Length > 200))
            return BadRequest(new { error = "Tashkilot nomi 1–200 belgidan iborat bo'lishi kerak." });

        var settings = OrganizationSettingsCatalog.Resolve(org.SettingsJson);
        var currency = request.Currency ?? settings.Currency;
        if (!OrganizationSettingsCatalog.IsCurrency(currency))
            return BadRequest(new { error = $"Valyuta shulardan biri bo'lishi kerak: {string.Join(", ", OrganizationSettingsCatalog.Currencies)}." });

        var start = settings.WorkdayStart;
        var end = settings.WorkdayEnd;
        if (request.WorkdayStart is not null && !OrganizationSettingsCatalog.TryParseTime(request.WorkdayStart, out start))
            return BadRequest(new { error = "Ish boshlanishi SS:DD ko'rinishida bo'lishi kerak (masalan 08:00)." });
        if (request.WorkdayEnd is not null && !OrganizationSettingsCatalog.TryParseTime(request.WorkdayEnd, out end))
            return BadRequest(new { error = "Ish tugashi SS:DD ko'rinishida bo'lishi kerak (masalan 21:00)." });
        if (end <= start) return BadRequest(new { error = "Ish tugashi boshlanishidan keyin bo'lishi kerak." });

        var branding = OrganizationSettingsCatalog.ResolveBranding(org.BrandingConfigJson);
        var color = branding.BrandColor;
        if (request.BrandColor is not null)
        {
            if (request.BrandColor.Length == 0)
            {
                color = null;
            }
            else if (!OrganizationSettingsCatalog.IsHexColor(request.BrandColor))
            {
                return BadRequest(new { error = "Brend rangi #RRGGBB ko'rinishida bo'lishi kerak (masalan #26645a)." });
            }
            else
            {
                var contrast = OrganizationSettingsCatalog.WhiteContrast(request.BrandColor);
                if (contrast < OrganizationSettingsCatalog.MinimumWhiteContrast)
                    return BadRequest(new
                    {
                        error = $"Bu rang juda och: oq matn uning ustida yomon o'qiladi (kontrast {contrast:0.0}:1, kamida "
                                + $"{OrganizationSettingsCatalog.MinimumWhiteContrast}:1 kerak). To'qroq rang tanlang."
                    });
                color = request.BrandColor.ToLowerInvariant();
            }
        }

        if (name is not null) org.Rename(name);
        org.SetSettings(OrganizationSettingsCatalog.Serialize(new ResolvedOrganizationSettings(currency, start, end)));
        org.SetBranding(OrganizationSettingsCatalog.SerializeBranding(branding with { BrandColor = color }));
        await db.SaveChangesAsync(ct);
        return Ok(ToDto(org));
    }

    [HttpPost("{id:guid}/logo")]
    [Authorize(Policy = "org.settings.edit")]
    [RequestSizeLimit(2 * 1024 * 1024)] // above the 1 MB rule, so an oversized logo gets the readable 400 rather than a bare 413
    public async Task<ActionResult<OrganizationSettingsDto>> UploadLogo(Guid id, [FromForm] OrganizationLogoForm form, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (scope.Scope != ScopeType.AllOrganization || scope.OrganizationId != id) return Forbid();

        var org = await db.Organizations.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (org is null) return NotFound();

        if (form.File is null) return BadRequest(new { error = "Logo tanlanmagan." });
        if (await logos.ValidateAsync(form.File, ct) is { } problem) return BadRequest(new { error = problem });

        var key = await logos.SaveAsync(form.File, ct);
        var branding = OrganizationSettingsCatalog.ResolveBranding(org.BrandingConfigJson);
        org.SetBranding(OrganizationSettingsCatalog.SerializeBranding(branding with { LogoKey = key }));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            logos.Delete(key);
            throw;
        }

        if (branding.LogoKey is { } previous) logos.Delete(previous);
        return Ok(ToDto(org));
    }

    [HttpDelete("{id:guid}/logo")]
    [Authorize(Policy = "org.settings.edit")]
    public async Task<IActionResult> RemoveLogo(Guid id, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (scope.Scope != ScopeType.AllOrganization || scope.OrganizationId != id) return Forbid();

        var org = await db.Organizations.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (org is null) return NotFound();

        var branding = OrganizationSettingsCatalog.ResolveBranding(org.BrandingConfigJson);
        if (branding.LogoKey is { } key)
        {
            org.SetBranding(OrganizationSettingsCatalog.SerializeBranding(branding with { LogoKey = null }));
            await db.SaveChangesAsync(ct);
            logos.Delete(key);
        }
        return NoContent();
    }

    /// <summary>The logo itself, fetched with the caller's token (an <c>&lt;img src&gt;</c> cannot send one). Served as an image and
    /// nothing else: the type comes from the stored extension, and the browser is told not to guess.</summary>
    [HttpGet("{id:guid}/logo")]
    [Authorize]
    public async Task<IActionResult> GetLogo(Guid id, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue("org"), out var organizationId) || organizationId != id) return NotFound();

        var json = await db.Organizations.Where(o => o.Id == id).Select(o => o.BrandingConfigJson).FirstOrDefaultAsync(ct);
        if (OrganizationSettingsCatalog.ResolveBranding(json).LogoKey is not { } key) return NotFound();

        var path = logos.PathFor(key);
        if (path is null) return NotFound();

        Response.Headers.CacheControl = "private, max-age=3600";
        Response.Headers.XContentTypeOptions = "nosniff";
        return PhysicalFile(path, OrganizationLogoStorage.ContentTypeFor(key));
    }

    private static OrganizationSettingsDto ToDto(Organization org)
    {
        var settings = OrganizationSettingsCatalog.Resolve(org.SettingsJson);
        var branding = OrganizationSettingsCatalog.ResolveBranding(org.BrandingConfigJson);
        return new OrganizationSettingsDto(
            org.Id, org.Name, settings.Currency,
            OrganizationSettingsCatalog.FormatTime(settings.WorkdayStart), OrganizationSettingsCatalog.FormatTime(settings.WorkdayEnd),
            branding.BrandColor, branding.LogoKey is { } key ? OrganizationLogoStorage.VersionOf(key) : null);
    }
}
