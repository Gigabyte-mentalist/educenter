using EduCenter.SharedKernel;

namespace EduCenter.Modules.Academic.Domain;

/// <summary>
/// A classroom of a branch (docs/00_TZ.md §18 "Room Management"): its name, how many it seats and what it holds. A lesson names the room
/// by id, and keeps the name too — see <see cref="Lesson.RoomName"/> — so older lessons and existing screens keep reading. A room that
/// has been used is switched off (<see cref="IsActive"/>) rather than erased; that keeps its lessons' history whole and only stops new
/// lessons being put in it.
/// </summary>
public class Room : BranchScopedEntity
{
    /// <summary>The same limit the lessons table puts on the room name it mirrors.</summary>
    public const int MaxNameLength = 50;
    public const int MaxEquipmentLength = 500;
    public const int MaxCapacity = 1000;

    private Room() { } // EF Core

    public Room(Guid organizationId, Guid branchId, string name, int? capacity, string? equipment)
    {
        OrganizationId = organizationId;
        BranchId = branchId;
        Update(name, capacity, equipment);
    }

    public string Name { get; private set; } = default!;
    /// <summary>How many people it seats; null when nobody has said. Only ever a warning against a group's size, never a refusal.</summary>
    public int? Capacity { get; private set; }
    /// <summary>Free text: projector, whiteboard, speakers…</summary>
    public string? Equipment { get; private set; }
    public bool IsActive { get; private set; } = true;

    public void Update(string name, int? capacity, string? equipment)
    {
        Name = name.Trim();
        Capacity = capacity;
        Equipment = string.IsNullOrWhiteSpace(equipment) ? null : equipment.Trim();
        MarkUpdated();
    }

    public void SetActive(bool active)
    {
        IsActive = active;
        MarkUpdated();
    }
}
