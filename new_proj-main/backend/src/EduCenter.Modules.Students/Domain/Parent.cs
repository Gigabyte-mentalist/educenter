using EduCenter.SharedKernel;

namespace EduCenter.Modules.Students.Domain;

public class Parent : TenantEntity
{
    private Parent() { } // EF Core

    public Parent(Guid organizationId, Guid userId)
    {
        OrganizationId = organizationId;
        UserId = userId;
    }

    public Guid UserId { get; private set; }
}

public enum ParentRelationship
{
    Father,
    Mother,
    Guardian,
    Other
}

/// <summary>Join entity — a parent may guard multiple children; a child may (rarely) have more than one guardian.</summary>
public class StudentParent
{
    private StudentParent() { } // EF Core

    public StudentParent(Guid studentId, Guid parentId, ParentRelationship relationship)
    {
        StudentId = studentId;
        ParentId = parentId;
        Relationship = relationship;
    }

    public Guid StudentId { get; private set; }
    public Guid ParentId { get; private set; }
    public ParentRelationship Relationship { get; private set; }
}
