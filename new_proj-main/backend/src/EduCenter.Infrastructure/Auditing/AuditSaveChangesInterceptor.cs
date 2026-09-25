using System.Security.Claims;
using System.Text.Json;
using EduCenter.Modules.Academic.Domain;
using EduCenter.Modules.Audit.Domain;
using EduCenter.Modules.Automation.Domain;
using EduCenter.Modules.Crm.Domain;
using EduCenter.Modules.Finance.Domain;
using EduCenter.Modules.Identity.Domain;
using EduCenter.Modules.Lms.Domain;
using EduCenter.Modules.Organizations.Domain;
using EduCenter.Modules.Students.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EduCenter.Infrastructure.Auditing;

/// <summary>
/// Writes an AuditLog row (WHO/WHAT/WHEN/OLD/NEW per docs/00_TZ.md §52) for every Added/Modified/Deleted
/// entry of an audited entity type, right before the same SaveChanges commits it. Only scalar properties
/// are captured — no navigation graphs — to keep the diff readable and cheap.
/// </summary>
public class AuditSaveChangesInterceptor(IHttpContextAccessor httpContextAccessor) : SaveChangesInterceptor
{
    // Finance rows and course prices are audited per docs/00_TZ.md §52 ("payment edited").
    private static readonly Type[] AuditedTypes =
        [typeof(User), typeof(Branch), typeof(Organization), typeof(Course), typeof(Invoice), typeof(Payment),
         typeof(Discount), typeof(Refund), typeof(PromoCode),
         // A transfer or a freeze moves a student between groups and stops their billing, so it belongs in the
         // log for the same reason an edited payment does — somebody will ask who did it and when.
         typeof(Student), typeof(Enrollment), typeof(Lesson),
         // Closing or deleting a group ends every student's enrollment in it, so who did it and when has to be on record.
         typeof(Group),
         // A room switched off, renamed or resized changes where lessons can be held (S07.03).
         typeof(Room), typeof(GroupScheduleSlot),
         // An exam, a result or a homework that was edited or erased: who did it, and what it said before.
         typeof(Exam), typeof(ExamResult), typeof(Homework),
         typeof(AutomationRule),
         // A teacher's qualifications, and above all their confirmation: an administrator vouching for a certificate (or the
         // confirmation lapsing because it was edited) is a matter of trust somebody may later ask about.
         typeof(TeacherProfile), typeof(TeacherCredential),
         // Temporary access to production data (TZ §3.8): who asked, why, who approved and until when.
         typeof(DataAccessRequest),
         // A Super Admin widening or taking away a role's permission (S07.02): the most consequential setting the product has.
         typeof(RolePermissionOverride),
         // A complaint changes hands and is closed by somebody's word (S06.07): who took it, what they wrote as the outcome, and what the author rated it.
         typeof(Complaint)];

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        AddAuditEntries(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        AddAuditEntries(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void AddAuditEntries(DbContext? context)
    {
        if (context is null) return;

        var (userId, organizationId) = ResolveActor();
        if (userId is null) return; // no authenticated actor (e.g. dev seed) — nothing to attribute the change to

        var entries = context.ChangeTracker.Entries()
            .Where(e => AuditedTypes.Contains(e.Entity.GetType())
                        && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        foreach (var entry in entries)
        {
            var resourceId = (Guid)entry.Property("Id").CurrentValue!;
            var action = entry.State.ToString()!; // Added | Modified | Deleted

            var oldValue = entry.State == EntityState.Added ? null : SerializeOriginal(entry);
            var newValue = entry.State == EntityState.Deleted ? null : SerializeCurrent(entry);

            context.Set<AuditLog>().Add(new AuditLog(
                organizationId ?? Guid.Empty,
                userId.Value,
                action,
                entry.Entity.GetType().Name,
                resourceId,
                oldValue,
                newValue,
                reason: null));
        }
    }

    private (Guid? UserId, Guid? OrganizationId) ResolveActor()
    {
        var user = httpContextAccessor.HttpContext?.User;
        var userIdClaim = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var orgClaim = user?.FindFirst("org")?.Value;

        return (
            Guid.TryParse(userIdClaim, out var uid) ? uid : null,
            Guid.TryParse(orgClaim, out var oid) ? oid : null);
    }

    /// <summary>Never copied into the audit log — a password hash (or any secret) in a log row is a
    /// leak waiting to happen, and the audit trail doesn't need it to say "password changed".</summary>
    private static readonly HashSet<string> RedactedProperties = ["PasswordHash"];

    private static string SerializeCurrent(EntityEntry entry) =>
        JsonSerializer.Serialize(entry.Properties
            .Where(p => !RedactedProperties.Contains(p.Metadata.Name))
            .ToDictionary(p => p.Metadata.Name, p => p.CurrentValue));

    private static string SerializeOriginal(EntityEntry entry) =>
        JsonSerializer.Serialize(entry.Properties
            .Where(p => !RedactedProperties.Contains(p.Metadata.Name))
            .ToDictionary(p => p.Metadata.Name, p => p.OriginalValue));
}
