namespace EduCenter.Modules.Identity.Domain;

/// <summary>Fixed set matching role_permission_matrix_UZ.md §2. Not user-extensible in Phase 1.</summary>
public static class RoleCode
{
    public const string SuperAdmin = "SUPER_ADMIN";
    public const string Administrator = "ADMINISTRATOR";
    public const string Operator = "OPERATOR";
    public const string Teacher = "TEACHER";
    public const string SupportTeacher = "SUPPORT_TEACHER";
    public const string Student = "STUDENT";
    public const string Parent = "PARENT";
    public const string Developer = "DEVELOPER";
}

public class Role
{
    private Role() { } // EF Core

    public Role(string code, string name)
    {
        Id = Guid.NewGuid();
        Code = code;
        Name = name;
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
}
