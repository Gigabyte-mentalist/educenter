using EduCenter.Api.Authorization;
using EduCenter.Api.Notifications;
using EduCenter.Infrastructure;
using EduCenter.Modules.Academic.Domain;
using EduCenter.Modules.Chat.Domain;
using EduCenter.Modules.Identity.Domain;
using EduCenter.Modules.Students.Domain;
using EduCenter.SharedKernel.Authorization;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Chat;

public static class ChatPermissions
{
    public const string Direct = "chat.1to1.use";
    public const string Group = "chat.group.use";
    public const string Moderate = "chat.moderate";
    /// <summary>Removing a whole conversation, not one message — see <see cref="Moderate"/>.</summary>
    public const string Delete = "chat.delete";
    public const string StaffChannels = "staff.channels.access";
}

public record ChatCapabilities(bool CanRead, bool CanPost, bool CanModerate)
{
    public static readonly ChatCapabilities None = new(false, false, false);
}

public record ChatContact(Guid UserId, string FullName, string? RoleCode);

public record StaffChannelDefinition(StaffChannelKind Kind, string Title, string Description, string[]? Roles);

/// <summary>Branch staff channels (docs/00_TZ.md §22). Audience is the holders of staff.channels.access for the branch,
/// narrowed by role where the channel is for a profession (null = every staff member of the branch).</summary>
public static class StaffChannelCatalog
{
    private static readonly string[] TeachingStaff = [RoleCode.Teacher, RoleCode.SupportTeacher, RoleCode.Administrator, RoleCode.SuperAdmin];

    public static readonly StaffChannelDefinition[] All =
    [
        new(StaffChannelKind.General, "Umumiy", "Filialning barcha xodimlari", null),
        new(StaffChannelKind.Teachers, "O'qituvchilar", "O'qituvchilar va administratsiya", TeachingStaff),
        new(StaffChannelKind.IeltsTeachers, "IELTS o'qituvchilari", "IELTS guruhlaridagi o'qituvchilar va administratsiya", TeachingStaff),
        new(StaffChannelKind.Administrators, "Administratorlar", "Filial administratsiyasi", [RoleCode.Administrator, RoleCode.SuperAdmin]),
        new(StaffChannelKind.Announcements, "E'lonlar", "Faqat administratsiya yozadi, hamma o'qiydi", null),
    ];

    public static StaffChannelDefinition Get(StaffChannelKind kind) => All.Single(c => c.Kind == kind);
}

/// <summary>
/// Who may see, write to and moderate a chat — decided per request from the permission matrix §3.8 and the
/// caller's scope, never from stored membership (except the two participants of a direct chat).
/// </summary>
public class ChatAccess(AppDbContext db, PermissionResolver permissions, NotificationRecipients recipients)
{
    public async Task<ChatCapabilities> CapabilitiesAsync(Guid userId, ChatRoom chat, CancellationToken ct = default)
    {
        switch (chat.Type)
        {
            case ChatType.Direct:
            {
                var direct = await permissions.ResolveAsync(userId, ChatPermissions.Direct, ct);
                var member = await db.ChatMembers.AnyAsync(m => m.ChatId == chat.Id && m.UserId == userId, ct);
                var ok = direct is not null && direct.OrganizationId == chat.OrganizationId && member;
                return new ChatCapabilities(ok, ok, false);
            }

            case ChatType.Group:
            {
                var scope = await permissions.ResolveAsync(userId, ChatPermissions.Group, ct);
                if (scope is null || scope.OrganizationId != chat.OrganizationId) return ChatCapabilities.None;
                if (!await scope.CanAccessGroupAsync(db, chat.GroupId!.Value, chat.BranchId!.Value)) return ChatCapabilities.None;

                var moderate = await permissions.ResolveAsync(userId, ChatPermissions.Moderate, ct);
                var canModerate = moderate is not null && await moderate.CanAccessGroupAsync(db, chat.GroupId.Value, chat.BranchId.Value);
                return new ChatCapabilities(true, true, canModerate);
            }

            case ChatType.StaffChannel:
            {
                var scope = await permissions.ResolveAsync(userId, ChatPermissions.StaffChannels, ct);
                var branchId = chat.BranchId!.Value;
                if (scope is null || scope.OrganizationId != chat.OrganizationId || !CoversBranch(scope, branchId)) return ChatCapabilities.None;
                if (!await ChannelAllowsAsync(userId, branchId, chat.ChannelKind!.Value, ct)) return ChatCapabilities.None;

                var moderate = await permissions.ResolveAsync(userId, ChatPermissions.Moderate, ct);
                var canModerate = moderate is not null && CoversBranch(moderate, branchId);
                var canPost = chat.ChannelKind != StaffChannelKind.Announcements || canModerate;
                return new ChatCapabilities(true, canPost, canModerate);
            }

            default:
                return ChatCapabilities.None;
        }
    }

    /// <summary>Every chat the user can open right now. Group chats and branch channels are created on first sight.</summary>
    public async Task<List<ChatRoom>> VisibleChatsAsync(Guid userId, CancellationToken ct = default)
    {
        var chats = new List<ChatRoom>();

        var direct = await permissions.ResolveAsync(userId, ChatPermissions.Direct, ct);
        if (direct is not null)
            chats.AddRange(await (
                from m in db.ChatMembers
                join c in db.ChatRooms on m.ChatId equals c.Id
                where m.UserId == userId && c.Type == ChatType.Direct && c.OrganizationId == direct.OrganizationId
                select c).ToListAsync(ct));

        var group = await permissions.ResolveAsync(userId, ChatPermissions.Group, ct);
        if (group is not null)
        {
            var groups = await (await group.ScopedGroupsAsync(db))
                .Select(g => new { g.Id, g.BranchId })
                .ToListAsync(ct);
            chats.AddRange(await EnsureGroupChatsAsync(group.OrganizationId, groups.Select(g => (g.Id, g.BranchId)).ToList(), ct));
        }

        var staff = await permissions.ResolveAsync(userId, ChatPermissions.StaffChannels, ct);
        if (staff is not null)
        {
            var branchIds = staff.Scope == ScopeType.AllOrganization
                ? await db.Branches.Where(b => b.OrganizationId == staff.OrganizationId).Select(b => b.Id).ToListAsync(ct)
                : staff.Scope == ScopeType.MyBranches ? staff.BranchIds.ToList() : [];

            var channels = new List<(Guid BranchId, StaffChannelKind Kind)>();
            foreach (var branchId in branchIds)
            foreach (var channel in StaffChannelCatalog.All)
                if (await ChannelAllowsAsync(userId, branchId, channel.Kind, ct))
                    channels.Add((branchId, channel.Kind));

            chats.AddRange(await EnsureStaffChannelsAsync(staff.OrganizationId, channels, ct));
        }

        return chats;
    }

    /// <summary>People the caller may start a direct chat with (docs/00_TZ.md §22 "1-to-1 Chat"), by the chat.1to1.use scope.</summary>
    public async Task<HashSet<Guid>> ContactIdsAsync(ResolvedScope scope, CancellationToken ct = default)
    {
        var ids = new HashSet<Guid>();

        switch (scope.Scope)
        {
            case ScopeType.AllOrganization:
                ids.UnionWith(await db.Users.Where(u => u.OrganizationId == scope.OrganizationId).Select(u => u.Id).ToListAsync(ct));
                break;

            case ScopeType.MyBranches:
            {
                ids.UnionWith(await db.UserBranches.Where(ub => scope.BranchIds.Contains(ub.BranchId)).Select(ub => ub.UserId).ToListAsync(ct));
                var students = await (
                    from e in db.Enrollments
                    join g in db.Groups on e.GroupId equals g.Id
                    join s in db.Students on e.StudentId equals s.Id
                    where scope.BranchIds.Contains(g.BranchId) && e.Status == EnrollmentStatus.Active
                    select new { s.Id, s.UserId }).ToListAsync(ct);
                ids.UnionWith(students.Select(s => s.UserId));
                ids.UnionWith(await ParentUsersAsync(students.Select(s => s.Id).ToList(), ct));
                break;
            }

            case ScopeType.MyGroups:
            case ScopeType.AssignedGroups:
            {
                var role = scope.Scope == ScopeType.MyGroups ? GroupStaffRole.MainTeacher : GroupStaffRole.SupportTeacher;
                var students = await (
                    from gs in db.GroupStaff
                    join e in db.Enrollments on gs.GroupId equals e.GroupId
                    join s in db.Students on e.StudentId equals s.Id
                    where gs.UserId == scope.UserId && gs.Role == role && e.Status == EnrollmentStatus.Active
                    select new { s.Id, s.UserId }).ToListAsync(ct);
                ids.UnionWith(students.Select(s => s.UserId));
                // Main teachers also talk to the parents of their students; support teachers work with the student.
                if (role == GroupStaffRole.MainTeacher)
                    ids.UnionWith(await ParentUsersAsync(students.Select(s => s.Id).ToList(), ct));
                ids.UnionWith(await ColleaguesAsync(scope, ct));
                break;
            }

            case ScopeType.Self:
                ids.UnionWith(await (
                    from s in db.Students
                    join e in db.Enrollments on s.Id equals e.StudentId
                    join gs in db.GroupStaff on e.GroupId equals gs.GroupId
                    where s.UserId == scope.UserId && e.Status == EnrollmentStatus.Active
                    select gs.UserId).ToListAsync(ct));
                break;

            case ScopeType.MyChildren:
            {
                var groups = await (
                    from p in db.Parents
                    join sp in db.StudentParents on p.Id equals sp.ParentId
                    join e in db.Enrollments on sp.StudentId equals e.StudentId
                    join g in db.Groups on e.GroupId equals g.Id
                    where p.UserId == scope.UserId && e.Status == EnrollmentStatus.Active
                    select new { g.Id, g.BranchId }).Distinct().ToListAsync(ct);
                var groupIds = groups.Select(g => g.Id).ToList();
                ids.UnionWith(await db.GroupStaff
                    .Where(gs => groupIds.Contains(gs.GroupId) && gs.Role == GroupStaffRole.MainTeacher)
                    .Select(gs => gs.UserId).ToListAsync(ct));
                foreach (var branchId in groups.Select(g => g.BranchId).Distinct())
                    ids.UnionWith(await recipients.BranchAdministratorsAsync(scope.OrganizationId, branchId));
                break;
            }
        }

        ids.Remove(scope.UserId);
        var active = await db.Users
            .Where(u => ids.Contains(u.Id) && u.OrganizationId == scope.OrganizationId && u.Status == UserStatus.Active)
            .Select(u => u.Id).ToListAsync(ct);
        return active.ToHashSet();
    }

    public async Task<List<ChatContact>> ContactsAsync(ResolvedScope scope, string? search, CancellationToken ct = default)
    {
        var ids = await ContactIdsAsync(scope, ct);
        var query = db.Users.Where(u => ids.Contains(u.Id));
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(u => EF.Functions.ILike(u.FullName, $"%{search.Trim()}%"));

        var users = await query.OrderBy(u => u.FullName).Take(200).Select(u => new { u.Id, u.FullName }).ToListAsync(ct);
        var roles = await RoleCodesAsync(users.Select(u => u.Id).ToList(), ct);
        return users.Select(u => new ChatContact(u.Id, u.FullName, roles.GetValueOrDefault(u.Id))).ToList();
    }

    public async Task<Dictionary<Guid, string>> RoleCodesAsync(List<Guid> userIds, CancellationToken ct = default) =>
        (await (from ur in db.UserRoles
                join r in db.Roles on ur.RoleId equals r.Id
                where userIds.Contains(ur.UserId)
                select new { ur.UserId, r.Code }).ToListAsync(ct))
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.First().Code);

    private static bool CoversBranch(ResolvedScope scope, Guid branchId) =>
        scope.Scope == ScopeType.AllOrganization || (scope.Scope == ScopeType.MyBranches && scope.BranchIds.Contains(branchId));

    private async Task<bool> ChannelAllowsAsync(Guid userId, Guid branchId, StaffChannelKind kind, CancellationToken ct)
    {
        var definition = StaffChannelCatalog.Get(kind);
        if (definition.Roles is null) return true;

        var roles = await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id where ur.UserId == userId select r.Code).ToListAsync(ct);
        if (!roles.Any(definition.Roles.Contains)) return false;
        if (kind != StaffChannelKind.IeltsTeachers || roles.Contains(RoleCode.Administrator) || roles.Contains(RoleCode.SuperAdmin)) return true;

        return await (
            from gs in db.GroupStaff
            join g in db.Groups on gs.GroupId equals g.Id
            join c in db.Courses on g.CourseId equals c.Id
            where gs.UserId == userId && g.BranchId == branchId && EF.Functions.ILike(c.LevelCategory, "%IELTS%")
            select gs.Id).AnyAsync(ct);
    }

    /// <summary>Staff (holders of staff.channels.access) who share a branch with the caller.</summary>
    private Task<List<Guid>> ColleaguesAsync(ResolvedScope scope, CancellationToken ct)
    {
        // The roles that hold it according to the matrix — a Super Admin's change to who has staff channels counts here too.
        var holders = PermissionResolver.RolesHolding(db, scope.OrganizationId, ChatPermissions.StaffChannels);
        return (from ub in db.UserBranches
                join ur in db.UserRoles on ub.UserId equals ur.UserId
                where scope.BranchIds.Contains(ub.BranchId) && holders.Contains(ur.RoleId)
                select ub.UserId).Distinct().ToListAsync(ct);
    }

    private Task<List<Guid>> ParentUsersAsync(List<Guid> studentIds, CancellationToken ct) =>
        (from sp in db.StudentParents
         join p in db.Parents on sp.ParentId equals p.Id
         where studentIds.Contains(sp.StudentId)
         select p.UserId).Distinct().ToListAsync(ct);

    private async Task<List<ChatRoom>> EnsureGroupChatsAsync(Guid organizationId, List<(Guid GroupId, Guid BranchId)> groups, CancellationToken ct)
    {
        if (groups.Count == 0) return [];
        var groupIds = groups.Select(g => (Guid?)g.GroupId).ToList();
        IQueryable<ChatRoom> Existing() => db.ChatRooms.Where(c => groupIds.Contains(c.GroupId));

        var existing = await Existing().ToListAsync(ct);
        var created = groups
            .Where(g => existing.All(c => c.GroupId != g.GroupId))
            .Select(g => ChatRoom.ForGroup(organizationId, g.GroupId, g.BranchId))
            .ToList();
        return created.Count == 0 ? existing : await SaveCreatedAsync(existing, created, Existing, ct);
    }

    private async Task<List<ChatRoom>> EnsureStaffChannelsAsync(Guid organizationId, List<(Guid BranchId, StaffChannelKind Kind)> channels, CancellationToken ct)
    {
        if (channels.Count == 0) return [];
        var branchIds = channels.Select(c => (Guid?)c.BranchId).Distinct().ToList();
        IQueryable<ChatRoom> Existing() => db.ChatRooms.Where(c => c.ChannelKind != null && branchIds.Contains(c.BranchId));

        var existing = await Existing().ToListAsync(ct);
        var created = channels
            .Where(ch => existing.All(c => c.BranchId != ch.BranchId || c.ChannelKind != ch.Kind))
            .Select(ch => ChatRoom.ForStaffChannel(organizationId, ch.BranchId, ch.Kind))
            .ToList();
        var all = created.Count == 0 ? existing : await SaveCreatedAsync(existing, created, Existing, ct);
        return all.Where(c => channels.Contains((c.BranchId!.Value, c.ChannelKind!.Value))).ToList();
    }

    /// <summary>Two first visits can race to create the same chat; the unique index lets one win and the other re-reads.</summary>
    private async Task<List<ChatRoom>> SaveCreatedAsync(List<ChatRoom> existing, List<ChatRoom> created,
        Func<IQueryable<ChatRoom>> reload, CancellationToken ct)
    {
        db.ChatRooms.AddRange(created);
        try
        {
            await db.SaveChangesAsync(ct);
            return [.. existing, .. created];
        }
        catch (DbUpdateException)
        {
            foreach (var chat in created) db.Entry(chat).State = EntityState.Detached;
            return await reload().ToListAsync(ct);
        }
    }
}
