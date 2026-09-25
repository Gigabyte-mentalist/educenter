using EduCenter.Infrastructure;
using EduCenter.Infrastructure.Seed;
using EduCenter.Modules.Identity.Domain;
using EduCenter.Modules.Organizations.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Deployment;

public enum BootstrapMode
{
    /// <summary>A real installation: one organization, one owner, nothing else.</summary>
    Clean,

    /// <summary>A showcase installation: the demo centre (staff, ~180 students, groups, lessons, invoices…) that
    /// development uses, but with a secret password instead of the published one.</summary>
    Demo,
}

/// <summary>The <c>Bootstrap</c> configuration section: what a freshly deployed, empty installation starts with.</summary>
public class BootstrapOptions
{
    public BootstrapMode Mode { get; set; } = BootstrapMode.Clean;

    /// <summary>Demo mode only: the one password shared by every demo account (<c>superadmin@demo-center.test</c> and
    /// the rest). Generated per deployment and never committed — the published <see cref="DbSeeder.DemoPassword"/> is refused.</summary>
    public string DemoPassword { get; set; } = "";

    public string OrganizationName { get; set; } = "EduCenter";
    public string TenantSlug { get; set; } = "educenter";
    public string BranchName { get; set; } = "Asosiy filial";
    public string BranchAddress { get; set; } = "";
    public string TimeZone { get; set; } = "Asia/Tashkent";

    public string SuperAdminEmail { get; set; } = "";
    public string SuperAdminFullName { get; set; } = "Super Admin";
    public string SuperAdminPhone { get; set; } = "";

    /// <summary>Temporary: the account is created with a forced password change (<see cref="User.RequirePasswordChange"/>),
    /// so this value stops being valid the first time the owner signs in. Only read while no organization exists.</summary>
    public string SuperAdminPassword { get; set; } = "";
}

/// <summary>
/// What a production start does instead of <see cref="DbSeeder.SeedAsync"/> (which also creates a demo centre whose
/// eight accounts share one password published in the repository — fine on a laptop, an open door on the internet).
/// It applies pending migrations, loads the roles/permissions matrix (idempotent, so an upgrade that revises a grant
/// takes effect on restart), and — only while the database has no organization at all — starts it in one of two modes:
/// <see cref="BootstrapMode.Clean"/> creates that organization, one branch and one SuperAdmin whose password must be
/// changed at first sign-in, and nothing else invented; <see cref="BootstrapMode.Demo"/> loads the demo centre with a
/// secret password of its own. Either way a database that already has an organization is never touched.
/// </summary>
public static class ProductionBootstrapper
{
    public static async Task RunAsync(AppDbContext db, BootstrapOptions options, ILogger logger, CancellationToken ct = default)
    {
        await db.Database.MigrateAsync(ct);

        var roles = await DbSeeder.SeedReferenceDataAsync(db);
        await db.SaveChangesAsync(ct);

        if (await db.Organizations.AnyAsync(ct))
        {
            logger.LogInformation("Bootstrap: an organization already exists — reference data refreshed, nothing else created.");
            return;
        }

        if (options.Mode == BootstrapMode.Demo)
        {
            var demoProblem = ValidateDemoPassword(options.DemoPassword);
            if (demoProblem is not null)
                throw new InvalidOperationException($"The database is empty and cannot be seeded with demo data: {demoProblem}.");

            // The same seeders a developer machine uses — one organization, so no tenant boundary is involved — only the
            // password differs. Both are idempotent and deterministic.
            await DbSeeder.SeedAsync(db, options.DemoPassword);
            await DemoDataSeeder.SeedAsync(db, options.DemoPassword, ct);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Bootstrap: seeded the demo centre (demo accounts use the configured demo password).");
            return;
        }

        var problem = Validate(options);
        if (problem is not null)
            throw new InvalidOperationException(
                $"The database is empty and cannot be bootstrapped: {problem} (see the Bootstrap__* settings).");

        var org = new Organization(options.OrganizationName.Trim(), options.TenantSlug.Trim().ToLowerInvariant());
        var branch = org.AddBranch(options.BranchName.Trim(), options.BranchAddress.Trim(), options.TimeZone.Trim());
        db.Organizations.Add(org);
        await db.SaveChangesAsync(ct); // the user needs the organization and branch ids

        var admin = new User(org.Id, options.SuperAdminFullName.Trim(), options.SuperAdminPhone.Trim(),
            options.SuperAdminEmail, passwordHash: string.Empty);
        admin.SetPasswordHash(new PasswordHasher<User>().HashPassword(admin, options.SuperAdminPassword));
        admin.RequirePasswordChange();
        admin.AssignRole(roles[RoleCode.SuperAdmin].Id);
        admin.AssignBranch(branch.Id);
        db.Users.Add(admin);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Bootstrap: created organization '{Organization}' with SuperAdmin {Email} (password change required at first sign-in).",
            org.Name, admin.Email);
    }

    /// <returns>What is wrong with the options, or null when an organization and its owner can be created from them.</returns>
    public static string? Validate(BootstrapOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.OrganizationName)) return "Bootstrap:OrganizationName is empty";
        if (string.IsNullOrWhiteSpace(options.TenantSlug)) return "Bootstrap:TenantSlug is empty";
        if (!EduCenter.Api.Auth.EmailPolicy.IsValid(options.SuperAdminEmail?.Trim().ToLowerInvariant() ?? ""))
            return "Bootstrap:SuperAdminEmail is not a valid email";
        if ((options.SuperAdminPassword ?? "").Length < MinBootstrapPasswordLength)
            return $"Bootstrap:SuperAdminPassword must be at least {MinBootstrapPasswordLength} characters";
        return null;
    }

    /// <returns>What is wrong with the shared demo password, or null when it is fit to protect a demo reachable from the
    /// internet: long, and not the one printed in the repository (anyone who read it would be a SuperAdmin).</returns>
    public static string? ValidateDemoPassword(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinBootstrapPasswordLength)
            return $"Bootstrap:DemoPassword must be at least {MinBootstrapPasswordLength} characters";
        if (password == DbSeeder.DemoPassword)
            return "Bootstrap:DemoPassword is the demo password published in the repository";
        return null;
    }

    /// <summary>Longer than <see cref="EduCenter.Api.Auth.PasswordPolicy.MinLength"/>: this one account owns everything.</summary>
    public const int MinBootstrapPasswordLength = 16;
}
