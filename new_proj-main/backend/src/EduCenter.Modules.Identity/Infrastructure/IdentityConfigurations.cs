using EduCenter.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduCenter.Modules.Identity.Infrastructure;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.FullName).HasMaxLength(200).IsRequired();
        builder.Property(u => u.Phone).HasMaxLength(30).IsRequired();
        builder.Property(u => u.Email).HasMaxLength(200).IsRequired();
        builder.Property(u => u.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(u => u.PhotoKey).HasMaxLength(64);
        builder.HasIndex(u => u.Email).IsUnique();
        builder.HasIndex(u => new { u.OrganizationId, u.Phone });

        builder.HasMany(u => u.Roles).WithOne().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(u => u.Branches).WithOne().HasForeignKey(b => b.UserId).OnDelete(DeleteBehavior.Cascade);

        // Must come AFTER HasMany — see EduCenter.Modules.Academic's GroupConfiguration for why the
        // reverse order silently breaks change detection on appends to the private backing list.
        builder.Navigation(u => u.Roles).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(u => u.Branches).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Code).HasMaxLength(50).IsRequired();
        builder.Property(r => r.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(r => r.Code).IsUnique();
    }
}

public class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("user_roles");
        builder.HasKey(ur => new { ur.UserId, ur.RoleId });
    }
}

public class UserBranchConfiguration : IEntityTypeConfiguration<UserBranch>
{
    public void Configure(EntityTypeBuilder<UserBranch> builder)
    {
        builder.ToTable("user_branches");
        builder.HasKey(ub => new { ub.UserId, ub.BranchId });
    }
}

public class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("permissions");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Code).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Resource).HasMaxLength(50).IsRequired();
        builder.Property(p => p.Action).HasMaxLength(30).IsRequired();
        builder.HasIndex(p => p.Code).IsUnique();
    }
}

public class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("role_permissions");
        builder.HasKey(rp => new { rp.RoleId, rp.PermissionId });
        builder.Property(rp => rp.DefaultScope).HasConversion<string>().HasMaxLength(30);
    }
}

public class UserPermissionConfiguration : IEntityTypeConfiguration<UserPermission>
{
    public void Configure(EntityTypeBuilder<UserPermission> builder)
    {
        builder.ToTable("user_permissions");
        builder.HasKey(up => new { up.UserId, up.PermissionId });
        builder.Property(up => up.ScopeOverride).HasConversion<string>().HasMaxLength(30);
    }
}

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.HasKey(rt => rt.Id);
        builder.Property(rt => rt.TokenHash).HasMaxLength(200).IsRequired();
        builder.HasIndex(rt => rt.TokenHash).IsUnique();
        builder.HasIndex(rt => rt.UserId);
    }
}

public class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
{
    public void Configure(EntityTypeBuilder<PasswordResetToken> builder)
    {
        builder.ToTable("password_reset_tokens");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.CodeHash).HasMaxLength(200).IsRequired();
        // "How many codes did this person ask for in the last hour" and "which code is live" — both by user, newest first.
        builder.HasIndex(t => new { t.UserId, t.CreatedAt });
    }
}

public class DataAccessRequestConfiguration : IEntityTypeConfiguration<DataAccessRequest>
{
    public void Configure(EntityTypeBuilder<DataAccessRequest> builder)
    {
        builder.ToTable("data_access_requests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.PermissionCode).HasMaxLength(100).IsRequired();
        builder.Property(r => r.Reason).HasMaxLength(DataAccessRequest.MaxReasonLength).IsRequired();
        builder.Property(r => r.DecisionNote).HasMaxLength(500);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);

        // "Does this user hold an active grant for this code?" runs whenever a permission check comes up empty.
        builder.HasIndex(r => new { r.RequesterId, r.PermissionCode, r.Status });
        builder.HasIndex(r => new { r.OrganizationId, r.Status });
    }
}

public class RolePermissionOverrideConfiguration : IEntityTypeConfiguration<RolePermissionOverride>
{
    public void Configure(EntityTypeBuilder<RolePermissionOverride> builder)
    {
        builder.ToTable("role_permission_overrides");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.RoleCode).HasMaxLength(30).IsRequired();
        builder.Property(o => o.PermissionCode).HasMaxLength(100).IsRequired();
        builder.Property(o => o.ScopeOverride).HasConversion<string>().HasMaxLength(30);

        builder.HasOne<Role>().WithMany().HasForeignKey(o => o.RoleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Permission>().WithMany().HasForeignKey(o => o.PermissionId).OnDelete(DeleteBehavior.Cascade);
        // One cell of the matrix holds one override — and the resolver looks a cell up by exactly this key.
        builder.HasIndex(o => new { o.OrganizationId, o.RoleId, o.PermissionId }).IsUnique();
    }
}
