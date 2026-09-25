using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduCenter.Infrastructure.Seed;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// End-to-end proof that role_permission_matrix_UZ.md scopes actually filter data (not just that
/// [Authorize] rejects unauthenticated calls) — the property that matters most for this system.
/// </summary>
[Collection("Api")]
public class ScopeAuthorizationTests(EduCenterWebApplicationFactory factory)
{
    [Fact]
    public async Task GetBranches_WithoutToken_ReturnsUnauthorized()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/branches");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetBranches_AsSuperAdmin_SeesAllOrganizationBranches()
    {
        var client = await AuthenticatedClientAsync("superadmin@demo-center.test");

        var branches = await client.GetFromJsonAsync<List<BranchDto>>("/api/branches");

        Assert.NotNull(branches);
        Assert.True(branches!.Count >= 2, "Super Admin should see every seeded branch (AllOrganization scope).");
    }

    [Fact]
    public async Task GetBranches_AsAdministrator_SeesOnlyAssignedBranch()
    {
        var client = await AuthenticatedClientAsync("admin@demo-center.test");

        var branches = await client.GetFromJsonAsync<List<BranchDto>>("/api/branches");

        Assert.NotNull(branches);
        Assert.Single(branches!); // MyBranches scope: demo Administrator is assigned to exactly one
    }

    [Fact]
    public async Task CreateBranch_AsAdministrator_IsForbidden()
    {
        // Administrator has no branch.create grant in PermissionSeedData — only Super Admin does.
        var client = await AuthenticatedClientAsync("admin@demo-center.test");

        var response = await client.PostAsJsonAsync("/api/branches",
            new { name = "Should not be created", address = "n/a", timezone = "Asia/Tashkent" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<HttpClient> AuthenticatedClientAsync(string email)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = DbSeeder.DemoPassword });
        var tokens = await response.Content.ReadFromJsonAsync<TokenResponseDto>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        return client;
    }

    private record TokenResponseDto(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken);
    private record BranchDto(Guid Id, string Name, string Address, string Timezone);
}
