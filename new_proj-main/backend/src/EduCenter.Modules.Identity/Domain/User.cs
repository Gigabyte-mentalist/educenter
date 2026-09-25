using EduCenter.SharedKernel;

namespace EduCenter.Modules.Identity.Domain;

public enum UserStatus
{
    Active,
    Inactive,
    Suspended
}

public class User : TenantEntity
{
    private User() { } // EF Core

    public User(Guid organizationId, string fullName, string phone, string email, string passwordHash)
    {
        OrganizationId = organizationId;
        FullName = fullName;
        Phone = phone;
        Email = email.Trim().ToLowerInvariant();
        PasswordHash = passwordHash;
        Status = UserStatus.Active;
    }

    public string FullName { get; private set; } = default!;
    public string Phone { get; private set; } = default!;
    public string Email { get; private set; } = default!;
    public string PasswordHash { get; private set; } = default!;
    public UserStatus Status { get; private set; }
    public DateTimeOffset? PasswordChangedAt { get; private set; }
    public bool MustChangePassword { get; private set; }

    /// <summary>Random key of the profile picture in <c>UserPhotoStorage</c>; null while the person has none. Never a file name.</summary>
    public string? PhotoKey { get; private set; }

    private readonly List<UserRole> _roles = new();
    public IReadOnlyCollection<UserRole> Roles => _roles;

    private readonly List<UserBranch> _branches = new();
    public IReadOnlyCollection<UserBranch> Branches => _branches;

    public void AssignRole(Guid roleId) => _roles.Add(new UserRole(Id, roleId));
    public void AssignBranch(Guid branchId) => _branches.Add(new UserBranch(Id, branchId));

    public void SetPasswordHash(string passwordHash)
    {
        PasswordHash = passwordHash;
        MarkUpdated();
    }

    /// <summary>A password the account holder chose themselves — clears the flag a temporary one set.</summary>
    public void ChangePassword(string passwordHash)
    {
        PasswordHash = passwordHash;
        PasswordChangedAt = DateTimeOffset.UtcNow;
        MustChangePassword = false;
        MarkUpdated();
    }

    /// <summary>Marks a temporary password (freshly registered, or just reset by an administrator) as one the
    /// holder must replace before doing anything else — enforced server-side by <c>AccountStatusMiddleware</c>.</summary>
    public void RequirePasswordChange()
    {
        MustChangePassword = true;
        MarkUpdated();
    }

    /// <summary>Sets or, with null, removes the profile picture. The caller deletes the file the old key pointed at.</summary>
    public void SetPhoto(string? key)
    {
        PhotoKey = key;
        MarkUpdated();
    }

    public void UpdateProfile(string fullName, string phone)
    {
        FullName = fullName;
        Phone = phone;
        MarkUpdated();
    }

    public void Deactivate()
    {
        Status = UserStatus.Inactive;
        MarkUpdated();
    }

    public void Activate()
    {
        Status = UserStatus.Active;
        MarkUpdated();
    }
}

/// <summary>Join entity — a user may hold multiple roles (rare but supported, e.g. Admin who also teaches).</summary>
public class UserRole
{
    private UserRole() { } // EF Core

    public UserRole(Guid userId, Guid roleId)
    {
        UserId = userId;
        RoleId = roleId;
    }

    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }
}

/// <summary>Join entity — a user (typically Administrator or Support Teacher) may be assigned to multiple branches.</summary>
public class UserBranch
{
    private UserBranch() { } // EF Core

    public UserBranch(Guid userId, Guid branchId)
    {
        UserId = userId;
        BranchId = branchId;
    }

    public Guid UserId { get; private set; }
    public Guid BranchId { get; private set; }
}
