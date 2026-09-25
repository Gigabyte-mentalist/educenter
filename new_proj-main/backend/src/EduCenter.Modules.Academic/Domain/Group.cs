using EduCenter.SharedKernel;

namespace EduCenter.Modules.Academic.Domain;

public enum GroupStatus
{
    Forming,
    Active,
    Completed,
    Cancelled
}

public enum GroupStaffRole
{
    MainTeacher,
    SupportTeacher
}

public class Group : BranchScopedEntity
{
    private Group() { } // EF Core

    public Group(Guid organizationId, Guid branchId, Guid courseId, string name, int capacity)
    {
        OrganizationId = organizationId;
        BranchId = branchId;
        CourseId = courseId;
        Name = name;
        Capacity = capacity;
        Status = GroupStatus.Forming;
    }

    public Guid CourseId { get; private set; }
    public string Name { get; private set; } = default!;
    public int Capacity { get; private set; }
    public GroupStatus Status { get; private set; }

    private readonly List<GroupStaff> _staff = new();
    public IReadOnlyCollection<GroupStaff> Staff => _staff;

    public GroupStaff AssignStaff(Guid userId, GroupStaffRole role)
    {
        if (_staff.Any(s => s.UserId == userId))
            throw new InvalidOperationException("Bu foydalanuvchi guruhga allaqachon biriktirilgan.");
        if (role == GroupStaffRole.MainTeacher && _staff.Any(s => s.Role == GroupStaffRole.MainTeacher))
            throw new InvalidOperationException("Guruhda allaqachon asosiy o'qituvchi bor.");

        var staff = new GroupStaff(Id, userId, role);
        _staff.Add(staff);
        return staff;
    }

    /// <summary>Completed and Cancelled are final: nothing leaves them, and a closed group takes no more edits or students.</summary>
    public bool IsClosed => Status is GroupStatus.Completed or GroupStatus.Cancelled;

    /// <summary>The main teacher of a group is the one the schedule, chat and scope checks hang off, so the group is
    /// never left without one — <see cref="AssignStaff"/> allows only one, which makes removing them always refused.</summary>
    public bool IsLastMainTeacher(Guid userId) =>
        _staff.Count(s => s.Role == GroupStaffRole.MainTeacher) <= 1
        && _staff.Any(s => s.UserId == userId && s.Role == GroupStaffRole.MainTeacher);

    public void Update(string name, int capacity)
    {
        if (IsClosed)
            throw new InvalidOperationException("Yopilgan guruhni tahrirlab bo'lmaydi.");

        Name = name;
        Capacity = capacity;
        MarkUpdated();
    }

    /// <summary>Forming → Active only.</summary>
    public void Activate()
    {
        if (Status != GroupStatus.Forming)
            throw new InvalidOperationException("Faqat shakllanayotgan guruhni faollashtirish mumkin.");

        Status = GroupStatus.Active;
        MarkUpdated();
    }

    /// <summary>Active → Completed only: a group that never started has nothing to complete.</summary>
    public void Complete()
    {
        if (Status != GroupStatus.Active)
            throw new InvalidOperationException("Faqat faol guruhni yakunlash mumkin.");

        Status = GroupStatus.Completed;
        MarkUpdated();
    }

    /// <summary>Forming or Active → Cancelled.</summary>
    public void Cancel()
    {
        if (IsClosed)
            throw new InvalidOperationException("Guruh allaqachon yopilgan.");

        Status = GroupStatus.Cancelled;
        MarkUpdated();
    }

    /// <returns>The removed row, so the caller can delete it from the database too.</returns>
    public GroupStaff RemoveStaff(Guid userId)
    {
        var staff = _staff.FirstOrDefault(s => s.UserId == userId)
            ?? throw new InvalidOperationException("Bu foydalanuvchi guruhga biriktirilmagan.");
        _staff.Remove(staff);
        return staff;
    }
}

/// <summary>Maps to docs/00_TZ.md §3.5 — support teacher is an assignment, not a global role.</summary>
public class GroupStaff
{
    private GroupStaff() { } // EF Core

    public GroupStaff(Guid groupId, Guid userId, GroupStaffRole role)
    {
        Id = Guid.NewGuid();
        GroupId = groupId;
        UserId = userId;
        Role = role;
    }

    public Guid Id { get; private set; }
    public Guid GroupId { get; private set; }
    public Guid UserId { get; private set; }
    public GroupStaffRole Role { get; private set; }
}
