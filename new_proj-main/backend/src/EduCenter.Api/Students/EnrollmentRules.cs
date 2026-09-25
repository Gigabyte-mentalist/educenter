using EduCenter.Infrastructure;
using EduCenter.Modules.Academic.Domain;
using EduCenter.Modules.Students.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Students;

public enum AdmissionProblem
{
    NotFound,

    /// <summary>The group exists but sits in a branch the caller does not hold.</summary>
    OutOfScope,

    /// <summary>Completed or cancelled — it has no lessons left to join.</summary>
    Closed,

    Full,
    AlreadyEnrolled
}

/// <summary>The group a student is about to join, as the admission check saw it.</summary>
public sealed record AdmissionGroup(Guid Id, string Name, Guid BranchId, string BranchName, int Capacity, GroupStatus Status);

/// <param name="Group">Set whenever the group was found, including when it was then refused — a caller that wants to
/// name the group in its own message has it.</param>
public sealed record Admission(AdmissionGroup? Group, AdmissionProblem? Problem, string? Message)
{
    public bool Allowed => Problem is null;
}

/// <summary>
/// Whether a group may take one more student — the questions a person at the desk asks before writing a name on the
/// list. Shared by everything that puts a student into a group (transfer, registration), so "is there a seat" and "is
/// this my branch" have one answer. Callers translate the problem into a status code: a transfer has always answered
/// 400 for these, a fresh registration answers 409, because the request is well-formed and it is the group's state
/// that refuses it.
/// </summary>
public static class EnrollmentRules
{
    /// <param name="allowedBranchIds">The branches the caller may act inside.</param>
    /// <param name="studentId">Pass it when the student already exists, so joining a group they are in is refused.</param>
    public static async Task<Admission> CheckAsync(
        AppDbContext db, Guid organizationId, Guid groupId, IReadOnlyCollection<Guid> allowedBranchIds,
        Guid? studentId = null, CancellationToken ct = default)
    {
        var group = await (
            from g in db.Groups
            join b in db.Branches on g.BranchId equals b.Id
            where g.Id == groupId && g.OrganizationId == organizationId
            select new AdmissionGroup(g.Id, g.Name, g.BranchId, b.Name, g.Capacity, g.Status)).FirstOrDefaultAsync(ct);

        if (group is null)
            return new Admission(null, AdmissionProblem.NotFound, "Guruh topilmadi.");

        if (!allowedBranchIds.Contains(group.BranchId))
            return new Admission(group, AdmissionProblem.OutOfScope, "Bu guruh sizning filialingizga tegishli emas.");

        if (group.Status is GroupStatus.Completed or GroupStatus.Cancelled)
            return new Admission(group, AdmissionProblem.Closed, "Yopilgan guruhga o'quvchi qo'shib bo'lmaydi.");

        var enrolled = await db.Enrollments.CountAsync(e => e.GroupId == group.Id && e.Status == EnrollmentStatus.Active, ct);
        if (enrolled >= group.Capacity)
            return new Admission(group, AdmissionProblem.Full, $"{group.Name} guruhida bo'sh joy yo'q ({enrolled}/{group.Capacity}).");

        if (studentId is { } id
            && await db.Enrollments.AnyAsync(e => e.StudentId == id && e.GroupId == group.Id && e.Status == EnrollmentStatus.Active, ct))
            return new Admission(group, AdmissionProblem.AlreadyEnrolled, "O'quvchi allaqachon bu guruhda o'qiyapti.");

        return new Admission(group, null, null);
    }
}
