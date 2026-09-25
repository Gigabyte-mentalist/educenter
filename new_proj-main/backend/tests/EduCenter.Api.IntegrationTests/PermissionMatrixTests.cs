using System.Net;
using System.Net.Http.Json;
using EduCenter.Infrastructure;
using EduCenter.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// The role × permission matrix editor (docs/21.09-audit-jurnali S07.02): the Super Admin changes a role's cell on top of the grants that
/// ship in code, at once and for good, without being able to lock themselves out. The matrix belongs to the whole suite (every test logs
/// in as some role), so each test puts back what it changed.
/// </summary>
[Collection("Api")]
public class PermissionMatrixTests(EduCenterWebApplicationFactory factory)
{
    private readonly LmsWorld world = new(factory);

    [Fact]
    public async Task AnOverride_ChangesTheScope_AtOnce_AndDeletingItBringsTheDefaultBack()
    {
        var superAdmin = await world.ClientAsync("superadmin@demo-center.test");
        var operatorClient = await world.ClientAsync("operator@demo-center.test");
        Assert.Equal("MyBranches", await ScopeOfAsync(operatorClient, "finance.debt.view"));

        var changed = await SetAsync(superAdmin, "OPERATOR", "finance.debt.view", scope: "AllOrganization");
        try
        {
            Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
            var cell = (await changed.Content.ReadFromJsonAsync<CellDto>())!;
            Assert.Equal("MyBranches", cell.DefaultScope);
            Assert.Equal("AllOrganization", cell.EffectiveScope);
            Assert.NotNull(cell.OverrideId);

            // The same token, the very next request: no cache, no restart.
            Assert.Equal("AllOrganization", await ScopeOfAsync(operatorClient, "finance.debt.view"));

            // The matrix shows the cell as changed, next to what it was.
            var shown = (await CellAsync(superAdmin, "finance.debt.view", "OPERATOR"))!;
            Assert.Equal(cell.OverrideId, shown.OverrideId);
            Assert.Equal("MyBranches", shown.DefaultScope);

            Assert.Equal(HttpStatusCode.NoContent, (await superAdmin.DeleteAsync($"/api/permissions/overrides/{cell.OverrideId}")).StatusCode);
            Assert.Equal("MyBranches", await ScopeOfAsync(operatorClient, "finance.debt.view"));
            Assert.Null((await CellAsync(superAdmin, "finance.debt.view", "OPERATOR"))!.OverrideId);
            Assert.Equal(HttpStatusCode.NotFound, (await superAdmin.DeleteAsync($"/api/permissions/overrides/{cell.OverrideId}")).StatusCode);
        }
        finally
        {
            await SetAsync(superAdmin, "OPERATOR", "finance.debt.view", scope: null); // idempotent: nothing to undo when it already went
        }
    }

    [Fact]
    public async Task ARevokedCell_TakesThePermissionAway_AndAScopeOnAnEmptyCellGrantsIt()
    {
        var superAdmin = await world.ClientAsync("superadmin@demo-center.test");
        var operatorClient = await world.ClientAsync("operator@demo-center.test");
        Assert.Equal(HttpStatusCode.OK, (await operatorClient.GetAsync("/api/finance/invoices")).StatusCode);
        Assert.Null(await ScopeOfAsync(operatorClient, "payments.view")); // not shipped to the operator

        try
        {
            Assert.Equal(HttpStatusCode.OK, (await SetAsync(superAdmin, "OPERATOR", "finance.debt.view", revoked: true)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.GetAsync("/api/finance/invoices")).StatusCode);
            Assert.Null(await ScopeOfAsync(operatorClient, "finance.debt.view")); // and the UI stops offering it
            Assert.Equal("AllOrganization", await ScopeOfAsync(superAdmin, "finance.debt.view")); // other roles are untouched

            var granted = await SetAsync(superAdmin, "OPERATOR", "payments.view", scope: "MyBranches");
            Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
            Assert.Null((await granted.Content.ReadFromJsonAsync<CellDto>())!.DefaultScope);
            Assert.Equal("MyBranches", await ScopeOfAsync(operatorClient, "payments.view"));
        }
        finally
        {
            await SetAsync(superAdmin, "OPERATOR", "finance.debt.view", scope: null);
            await SetAsync(superAdmin, "OPERATOR", "payments.view", scope: null);
        }

        Assert.Equal(HttpStatusCode.OK, (await operatorClient.GetAsync("/api/finance/invoices")).StatusCode);
        Assert.Null(await ScopeOfAsync(operatorClient, "payments.view"));
    }

    [Fact]
    public async Task AnOverride_SurvivesTheSeederRewritingTheDefaults()
    {
        var superAdmin = await world.ClientAsync("superadmin@demo-center.test");
        var operatorClient = await world.ClientAsync("operator@demo-center.test");
        try
        {
            Assert.Equal(HttpStatusCode.OK, (await SetAsync(superAdmin, "OPERATOR", "finance.debt.view", scope: "AllOrganization")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await SetAsync(superAdmin, "OPERATOR", "leads.view", revoked: true)).StatusCode);

            // What every start does in Development: bring role_permissions back in step with PermissionSeedData.
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await DbSeeder.SeedReferenceDataAsync(db);
                await db.SaveChangesAsync();
            }

            Assert.Equal("AllOrganization", await ScopeOfAsync(operatorClient, "finance.debt.view"));
            Assert.Null(await ScopeOfAsync(operatorClient, "leads.view"));
            Assert.Equal("MyBranches", (await CellAsync(superAdmin, "finance.debt.view", "OPERATOR"))!.DefaultScope); // the default itself is untouched
        }
        finally
        {
            await SetAsync(superAdmin, "OPERATOR", "finance.debt.view", scope: null);
            await SetAsync(superAdmin, "OPERATOR", "leads.view", scope: null);
        }
    }

    [Fact]
    public async Task TheSuperAdmin_CannotLockThemselvesOut()
    {
        var superAdmin = await world.ClientAsync("superadmin@demo-center.test");

        foreach (var code in new[] { "permission.policy.edit", "users.view", "users.deactivate", "role.assign" })
        {
            var revoke = await SetAsync(superAdmin, "SUPER_ADMIN", code, revoked: true);
            Assert.Equal(HttpStatusCode.Conflict, revoke.StatusCode);
            Assert.Contains(code, await revoke.Content.ReadAsStringAsync());
        }
        // Narrowing one of them is just as much a lock-out as removing it.
        Assert.Equal(HttpStatusCode.Conflict, (await SetAsync(superAdmin, "SUPER_ADMIN", "users.view", scope: "MyBranches")).StatusCode);

        // Nothing changed, and the screen still opens.
        var cell = (await CellAsync(superAdmin, "permission.policy.edit", "SUPER_ADMIN"))!;
        Assert.Null(cell.OverrideId);
        Assert.Equal("AllOrganization", cell.EffectiveScope);
        Assert.Equal("AllOrganization", await ScopeOfAsync(superAdmin, "users.deactivate"));

        // Other cells of the Super Admin's own role may change.
        try
        {
            Assert.Equal(HttpStatusCode.OK, (await SetAsync(superAdmin, "SUPER_ADMIN", "leads.view", scope: "MyBranches")).StatusCode);
        }
        finally
        {
            await SetAsync(superAdmin, "SUPER_ADMIN", "leads.view", scope: null);
        }
    }

    [Fact]
    public async Task OnlyTheSuperAdmin_OpensAndChangesTheMatrix()
    {
        var superAdmin = await world.ClientAsync("superadmin@demo-center.test");
        var cell = (await SetAsync(superAdmin, "OPERATOR", "finance.debt.view", scope: "AllOrganization")).Content;
        var overrideId = (await cell.ReadFromJsonAsync<CellDto>())!.OverrideId;
        try
        {
            foreach (var email in new[] { "admin@demo-center.test", "operator@demo-center.test", "teacher@demo-center.test", "student@demo-center.test", "developer@demo-center.test" })
            {
                var client = await world.ClientAsync(email);
                Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/permissions/matrix")).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await SetAsync(client, "OPERATOR", "finance.debt.view", scope: "MyBranches")).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/permissions/overrides/{overrideId}")).StatusCode);
            }
            Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/permissions/matrix")).StatusCode);
            Assert.Equal("AllOrganization", (await CellAsync(superAdmin, "finance.debt.view", "OPERATOR"))!.EffectiveScope); // none of it stuck
        }
        finally
        {
            await SetAsync(superAdmin, "OPERATOR", "finance.debt.view", scope: null);
        }
    }

    [Fact]
    public async Task InvalidEdits_AreRefused()
    {
        var superAdmin = await world.ClientAsync("superadmin@demo-center.test");

        // A Developer is never given money, accounts or the permission system.
        foreach (var code in new[] { "payments.view", "finance.debt.view", "users.create", "role.assign", "refund.approve" })
            Assert.Equal(HttpStatusCode.BadRequest, (await SetAsync(superAdmin, "DEVELOPER", code, scope: "AllOrganization")).StatusCode);
        Assert.Null((await CellAsync(superAdmin, "payments.view", "DEVELOPER"))!.OverrideId);

        // The right to edit the matrix stays with the Super Admin.
        Assert.Equal(HttpStatusCode.BadRequest, (await SetAsync(superAdmin, "ADMINISTRATOR", "permission.policy.edit", scope: "AllOrganization")).StatusCode);

        // A scope the permission has no use for: an operator cannot take payments "for themselves".
        Assert.Equal(HttpStatusCode.BadRequest, (await SetAsync(superAdmin, "ADMINISTRATOR", "payments.create", scope: "Self")).StatusCode);
        // "None" would be read as a grant; taking away is what Revoked is for.
        Assert.Equal(HttpStatusCode.BadRequest, (await SetAsync(superAdmin, "OPERATOR", "finance.debt.view", scope: "None")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SetAsync(superAdmin, "OPERATOR", "finance.debt.view", scope: "MyBranches", revoked: true)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SetAsync(superAdmin, "OPERATOR", "payments.view", revoked: true)).StatusCode); // nothing to take away
        Assert.Equal(HttpStatusCode.BadRequest, (await SetAsync(superAdmin, "OPERATOR", "finance.debt.view", scope: "Everything")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await SetAsync(superAdmin, "NO_SUCH_ROLE", "finance.debt.view", scope: "MyBranches")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SetAsync(superAdmin, "OPERATOR", "no.such.permission", scope: "MyBranches")).StatusCode);

        // Asking for what the role already ships with is asking to undo — no override is left behind.
        var same = await SetAsync(superAdmin, "OPERATOR", "finance.debt.view", scope: "MyBranches");
        Assert.Equal(HttpStatusCode.OK, same.StatusCode);
        Assert.Null((await same.Content.ReadFromJsonAsync<CellDto>())!.OverrideId);
    }

    [Fact]
    public async Task TheMatrix_ListsEveryPermissionForEveryRole_WithItsAllowedScopes()
    {
        var superAdmin = await world.ClientAsync("superadmin@demo-center.test");
        var matrix = (await superAdmin.GetFromJsonAsync<MatrixDto>("/api/permissions/matrix"))!;

        Assert.Equal(new[] { "SUPER_ADMIN", "ADMINISTRATOR", "OPERATOR", "TEACHER", "SUPPORT_TEACHER", "STUDENT", "PARENT", "DEVELOPER" }, matrix.Roles.Select(r => r.Code).ToArray());
        Assert.True(matrix.Permissions.Count > 50);
        Assert.All(matrix.Permissions, p => Assert.Equal(8, p.Cells.Count));

        var debt = matrix.Permissions.Single(p => p.Code == "finance.debt.view");
        Assert.Equal("finance", debt.Resource);
        Assert.Equal(new[] { "AllOrganization", "MyBranches" }, debt.AllowedScopes);
        Assert.DoesNotContain(matrix.Permissions.SelectMany(p => p.AllowedScopes), s => s == "None");
        Assert.Equal("AllOrganization", debt.Cells.Single(c => c.RoleCode == "SUPER_ADMIN").EffectiveScope);
        Assert.Null(debt.Cells.Single(c => c.RoleCode == "TEACHER").EffectiveScope); // "—" in the document's table
    }

    [Fact]
    public async Task EveryChange_IsInTheAuditLog_WithTheCodesNotJustIds()
    {
        var superAdmin = await world.ClientAsync("superadmin@demo-center.test");
        try
        {
            var set = (await (await SetAsync(superAdmin, "OPERATOR", "finance.debt.view", scope: "AllOrganization")).Content.ReadFromJsonAsync<CellDto>())!;
            await SetAsync(superAdmin, "OPERATOR", "finance.debt.view", revoked: true);
            await SetAsync(superAdmin, "OPERATOR", "finance.debt.view", scope: null);

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rows = await db.AuditLogs.Where(a => a.Resource == "RolePermissionOverride" && a.ResourceId == set.OverrideId!.Value).OrderBy(a => a.CreatedAt).ToListAsync();

            Assert.Equal(new[] { "Added", "Modified", "Deleted" }, rows.Select(r => r.Action).ToArray());
            Assert.Contains("OPERATOR", rows[0].NewValueJson);
            Assert.Contains("finance.debt.view", rows[0].NewValueJson);
            Assert.Contains("\"ScopeOverride\":0", rows[0].NewValueJson); // the audit log writes enums as numbers; 0 is AllOrganization
            Assert.Contains("\"Revoked\":true", rows[1].NewValueJson);
            Assert.Contains("\"ScopeOverride\":0", rows[1].OldValueJson); // what it was before the second edit
            Assert.Null(rows[2].NewValueJson);
        }
        finally
        {
            await SetAsync(superAdmin, "OPERATOR", "finance.debt.view", scope: null);
        }
    }

    // ---- helpers ------------------------------------------------------------------------------------

    private static Task<HttpResponseMessage> SetAsync(HttpClient client, string role, string permission, string? scope = null, bool revoked = false) =>
        client.PutAsJsonAsync("/api/permissions/overrides", new { roleCode = role, permissionCode = permission, scope, revoked });

    private static async Task<string?> ScopeOfAsync(HttpClient client, string code) =>
        (await client.GetFromJsonAsync<List<MyPermissionDto>>("/api/me/permissions"))!.FirstOrDefault(p => p.Code == code)?.Scope;

    private static async Task<CellDto?> CellAsync(HttpClient superAdmin, string permission, string role) =>
        (await superAdmin.GetFromJsonAsync<MatrixDto>("/api/permissions/matrix"))!
            .Permissions.Single(p => p.Code == permission).Cells.SingleOrDefault(c => c.RoleCode == role);

    private record MyPermissionDto(string Code, string Scope);
    private record CellDto(string RoleCode, string? DefaultScope, Guid? OverrideId, string? OverrideScope, bool Revoked, string? EffectiveScope);
    private record PermissionDto(string Code, string Resource, string Action, string[] AllowedScopes, List<CellDto> Cells);
    private record RoleDto(string Code, string Name);
    private record MatrixDto(List<RoleDto> Roles, List<PermissionDto> Permissions);
}
