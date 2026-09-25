using System.Security.Claims;
using EduCenter.Api.Authorization;
using EduCenter.SharedKernel.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduCenter.Api.Auth;

public record MyPermissionDto(string Code, ScopeType Scope);

/// <summary>The caller's own effective permissions. Only the UI reads this — to hide what the user may not do — the
/// server still checks the permission and scope again on every request (docs/01_role_permission_matrix.md §1).</summary>
[ApiController]
[Route("api/me")]
[Authorize]
public class MeController(PermissionResolver permissions) : ControllerBase
{
    [HttpGet("permissions")]
    public async Task<ActionResult<List<MyPermissionDto>>> Permissions(CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId)) return Unauthorized();

        var held = await permissions.ListAsync(userId, ct);
        return Ok(held
            .Where(p => p.Value != ScopeType.None)
            .OrderBy(p => p.Key)
            .Select(p => new MyPermissionDto(p.Key, p.Value))
            .ToList());
    }
}
