using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace EduCenter.Api.Authorization;

/// <summary>
/// Lets controllers write [Authorize(Policy = "students.view")] directly, using the permission
/// code as the policy name, instead of pre-registering one AddPolicy(...) call per row of
/// docs/01_role_permission_matrix.md.
/// </summary>
public class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback = new(options);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(policyName))
            .Build();

        return Task.FromResult<AuthorizationPolicy?>(policy);
    }
}
