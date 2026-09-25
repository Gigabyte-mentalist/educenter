using System.Security.Claims;
using System.Text.Json;
using EduCenter.Api.Authorization;
using EduCenter.Api.Notifications;
using EduCenter.Infrastructure;
using EduCenter.Modules.Crm.Domain;
using EduCenter.Modules.Identity.Domain;
using EduCenter.Modules.Notifications.Domain;
using EduCenter.Modules.Students.Domain;
using EduCenter.Modules.Timeline.Domain;
using EduCenter.SharedKernel;
using EduCenter.SharedKernel.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Complaints;

/// <param name="StudentId">The student it concerns, when there is one.</param>
/// <param name="IsMine">Whether the caller wrote it.</param>
/// <param name="CanManage">The caller runs this complaint's branch: they may hand it out and close it. The buttons are drawn from these flags.</param>
/// <param name="CanAct">The caller may move it along and write its outcome: whoever manages the branch, or the person it was handed to.</param>
/// <param name="CanRate">The caller wrote it and it is resolved, so their verdict is what is missing.</param>
public record ComplaintDto(
    Guid Id, Guid BranchId, string BranchName, Guid? StudentId, string? StudentName,
    Guid AuthorUserId, string AuthorName, string Subject, string Body, ComplaintStatus Status,
    Guid? AssignedToUserId, string? AssignedToName, string? Resolution, DateTimeOffset CreatedAt, DateTimeOffset? ResolvedAt,
    int? SatisfactionScore, string? FeedbackComment, bool IsMine, bool CanManage, bool CanAct, bool CanRate);

/// <param name="StudentId">Whom it is about. A student's own is about themselves and a parent's names their child; staff may name any student they can open.</param>
/// <param name="BranchId">Which branch, when it cannot be told from the student — a complaint about the centre in general, or a student in several.</param>
public record CreateComplaintRequest(string Subject, string Body, Guid? StudentId, Guid? BranchId);

public record AssignComplaintRequest(Guid UserId);
public record SetComplaintStatusRequest(ComplaintStatus Status);
public record ResolveComplaintRequest(string Resolution);
public record ComplaintFeedbackRequest(int Score, string? Comment);
public record ComplaintCandidateDto(Guid UserId, string FullName, string Email);

/// <summary>Complaint workflow (docs/00_TZ.md §41, docs/21.09-audit-jurnali S06.07): filed by the people the centre serves, taken and settled by the branch.</summary>
[ApiController]
[Route("api/complaints")]
public class ComplaintsController(
    AppDbContext db, PermissionResolver permissions, NotificationDispatcher notifier, NotificationRecipients recipients) : ControllerBase
{
    private sealed record Caller(Guid UserId, Guid OrganizationId, ResolvedScope? View);

    /// <summary>The complaints the caller may see, newest first. Whoever only files them sees only their own.</summary>
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<Page<ComplaintDto>>> List(
        [FromQuery] ComplaintStatus[]? statuses, [FromQuery] string? search, [FromQuery] PageRequest? paging, CancellationToken ct = default)
    {
        if (await CallerAsync(ct) is not { } caller) return Forbid();

        var query = ComplaintQueries.Visible(db, caller.OrganizationId, caller.UserId, caller.View);
        if (statuses is { Length: > 0 }) query = query.WhereIn(c => c.Status, statuses);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = PagingExtensions.ContainsPattern(search);
            query = query.Where(c => EF.Functions.ILike(c.Subject, term) || EF.Functions.ILike(c.Body, term)
                || db.Users.Any(u => u.Id == c.AuthorUserId && EF.Functions.ILike(u.FullName, term))
                || db.Students.Any(s => s.Id == c.StudentId && db.Users.Any(u => u.Id == s.UserId && EF.Functions.ILike(u.FullName, term))));
        }

        var page = await query.OrderByDescending(c => c.CreatedAt).ThenBy(c => c.Id).ToPageAsync(paging, ct);
        var dtos = await ToDtosAsync(page.Items, caller, ct);
        return Ok(new Page<ComplaintDto>(dtos, page.Total, page.PageNumber, page.PageSize));
    }

    /// <summary>One complaint, with the same visibility as the list — a notification links to it by id, and it may not be on the first page.</summary>
    [HttpGet("{id:guid}")]
    [Authorize]
    public async Task<ActionResult<ComplaintDto>> GetById(Guid id, CancellationToken ct)
    {
        if (await CallerAsync(ct) is not { } caller) return Forbid();

        var complaint = await ComplaintQueries.Visible(db, caller.OrganizationId, caller.UserId, caller.View).FirstOrDefaultAsync(c => c.Id == id, ct);
        return complaint is null ? NotFound() : Ok((await ToDtosAsync([complaint], caller, ct))[0]);
    }

    [HttpPost]
    [Authorize(Policy = "complaints.create")]
    public async Task<ActionResult<ComplaintDto>> Create(CreateComplaintRequest request, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();

        var subject = request.Subject?.Trim() ?? "";
        var body = request.Body?.Trim() ?? "";
        if (subject.Length == 0) return BadRequest(new { error = "Mavzuni yozing." });
        if (subject.Length > Complaint.MaxSubjectLength) return BadRequest(new { error = $"Mavzu {Complaint.MaxSubjectLength} belgidan oshmasin." });
        if (body.Length == 0) return BadRequest(new { error = "Shikoyat matnini yozing." });
        if (body.Length > Complaint.MaxBodyLength) return BadRequest(new { error = $"Matn {Complaint.MaxBodyLength} belgidan oshmasin." });

        var (target, problem) = await ResolveTargetAsync(scope, request, ct);
        if (problem is not null) return problem;

        var complaint = new Complaint(scope.OrganizationId, target!.BranchId, target.StudentId, scope.UserId, subject, body);
        db.Complaints.Add(complaint);

        if (target.StudentId is { } studentId)
            db.StudentTimelineEvents.Add(new StudentTimelineEvent(scope.OrganizationId, studentId, TimelineEventType.ComplaintCreated,
                JsonSerializer.Serialize(new { complaintId = complaint.Id })));

        // The branch's administrators are told; the text stays in the app — it is what the sender wrote about a person, and does not belong in a mail.
        var administrators = (await recipients.BranchAdministratorsAsync(scope.OrganizationId, target.BranchId)).Where(id => id != scope.UserId);
        await notifier.NotifyAsync(scope.OrganizationId, administrators,
            new NotificationMessage(NotificationType.ComplaintCreated, "Yangi shikoyat", subject, $"/complaints?highlight={complaint.Id}"), ct);

        await db.SaveChangesAsync(ct);

        var caller = new Caller(scope.UserId, scope.OrganizationId, await permissions.ResolveAsync(scope.UserId, "complaints.view", ct));
        return CreatedAtAction(nameof(GetById), new { id = complaint.Id }, (await ToDtosAsync([complaint], caller, ct))[0]);
    }

    /// <summary>The people this complaint can be handed to: active staff who read complaints and reach its branch.</summary>
    [HttpGet("{id:guid}/candidates")]
    [Authorize(Policy = "complaints.manage")]
    public async Task<ActionResult<List<ComplaintCandidateDto>>> Candidates(Guid id, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        var complaint = await db.Complaints.FirstOrDefaultAsync(c => c.Id == id && c.OrganizationId == scope.OrganizationId, ct);
        if (complaint is null) return NotFound();
        if (!ComplaintQueries.Covers(scope, complaint.BranchId)) return Forbid();

        return Ok(await CandidatesAsync(complaint, ct));
    }

    [HttpPost("{id:guid}/assign")]
    [Authorize(Policy = "complaints.manage")]
    public async Task<IActionResult> Assign(Guid id, AssignComplaintRequest request, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        var complaint = await db.Complaints.FirstOrDefaultAsync(c => c.Id == id && c.OrganizationId == scope.OrganizationId, ct);
        if (complaint is null) return NotFound();
        if (!ComplaintQueries.Covers(scope, complaint.BranchId)) return Forbid();

        if (complaint.Status is ComplaintStatus.Resolved or ComplaintStatus.Closed)
            return Conflict(new { error = "Yechilgan shikoyatga xodim biriktirilmaydi." });
        if (!(await CandidatesAsync(complaint, ct)).Any(c => c.UserId == request.UserId))
            return BadRequest(new { error = "Bu xodim shikoyatni ko'ra olmaydi — shu filial xodimlaridan birini tanlang." });

        complaint.Assign(request.UserId);

        if (request.UserId != scope.UserId)
            await notifier.NotifyAsync(scope.OrganizationId, [request.UserId],
                new NotificationMessage(NotificationType.ComplaintAssigned, "Sizga shikoyat biriktirildi", complaint.Subject,
                    $"/complaints?highlight={complaint.Id}"), ct);

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Moves a complaint along. Only forward, and only one step at a time: a person is handed it with <c>assign</c>, it is settled with
    /// <c>resolve</c> (which needs the outcome in words) and rated by its author with <c>feedback</c>; what is left here is starting it, and closing a
    /// resolved one whose author never answered.</summary>
    [HttpPost("{id:guid}/status")]
    [Authorize(Policy = "complaints.view")]
    public async Task<IActionResult> SetStatus(Guid id, SetComplaintStatusRequest request, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        var (complaint, problem) = await LoadForActionAsync(scope, id, ct);
        if (problem is not null) return problem;

        var target = request.Status;
        if (!Enum.IsDefined(target)) return BadRequest(new { error = "Holat noto'g'ri: New, Assigned, InProgress, Resolved yoki Closed." });
        if (target < complaint!.Status) return Conflict(new { error = "Holat orqaga qaytmaydi." });
        if (target == complaint.Status) return Conflict(new { error = "Shikoyat allaqachon shu holatda." });
        if (target == ComplaintStatus.Assigned) return BadRequest(new { error = "Biriktirish uchun xodimni tanlang." });
        if (target == ComplaintStatus.Resolved) return BadRequest(new { error = "Shikoyatni yechim yozib yakunlang." });

        if (target == ComplaintStatus.InProgress)
        {
            if (complaint.Status != ComplaintStatus.Assigned) return Conflict(new { error = "Avval shikoyatni xodimga biriktiring." });
            complaint.Start();
        }
        else
        {
            // Closed: only a resolved complaint, and only by whoever runs the branch — the assignee settles it, they do not shelve it unrated.
            if (complaint.Status != ComplaintStatus.Resolved) return Conflict(new { error = "Faqat yechilgan shikoyat yopiladi." });
            if (!ComplaintQueries.Covers(await permissions.ResolveAsync(scope.UserId, "complaints.manage", ct), complaint.BranchId)) return Forbid();
            complaint.Close();
        }

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/resolve")]
    [Authorize(Policy = "complaints.view")]
    public async Task<IActionResult> Resolve(Guid id, ResolveComplaintRequest request, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        var (complaint, problem) = await LoadForActionAsync(scope, id, ct);
        if (problem is not null) return problem;

        var resolution = request.Resolution?.Trim() ?? "";
        if (resolution.Length == 0) return BadRequest(new { error = "Yechimni yozing: nima qilindi?" });
        if (resolution.Length > Complaint.MaxResolutionLength) return BadRequest(new { error = $"Yechim {Complaint.MaxResolutionLength} belgidan oshmasin." });
        if (complaint!.Status is not (ComplaintStatus.Assigned or ComplaintStatus.InProgress))
            return Conflict(new { error = complaint.Status == ComplaintStatus.New ? "Avval shikoyatni xodimga biriktiring." : "Shikoyat allaqachon yechilgan." });

        complaint.Resolve(resolution);

        if (complaint.StudentId is { } studentId)
            db.StudentTimelineEvents.Add(new StudentTimelineEvent(scope.OrganizationId, studentId, TimelineEventType.ComplaintResolved,
                JsonSerializer.Serialize(new { complaintId = complaint.Id })));

        if (complaint.AuthorUserId != scope.UserId)
            await notifier.NotifyAsync(scope.OrganizationId, [complaint.AuthorUserId],
                new NotificationMessage(NotificationType.ComplaintResolved, "Shikoyatingiz ko'rib chiqildi",
                    "Yechim bilan tanishing va bahoingizni qoldiring.", $"/complaints?highlight={complaint.Id}"), ct);

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>The author's verdict on the outcome, 0–10; it closes the complaint. Nobody else's — a manager does not speak for the person who was wronged.</summary>
    [HttpPost("{id:guid}/feedback")]
    [Authorize]
    public async Task<IActionResult> Feedback(Guid id, ComplaintFeedbackRequest request, CancellationToken ct)
    {
        if (await CallerAsync(ct) is not { } caller) return Forbid();

        var complaint = await ComplaintQueries.Visible(db, caller.OrganizationId, caller.UserId, caller.View).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (complaint is null) return NotFound();
        if (complaint.AuthorUserId != caller.UserId) return Forbid();

        if (request.Score is < 0 or > Complaint.MaxScore) return BadRequest(new { error = $"Baho 0 dan {Complaint.MaxScore} gacha bo'lsin." });
        var comment = request.Comment?.Trim();
        if (comment?.Length > Complaint.MaxFeedbackLength) return BadRequest(new { error = $"Izoh {Complaint.MaxFeedbackLength} belgidan oshmasin." });
        if (complaint.Status != ComplaintStatus.Resolved)
            return Conflict(new { error = complaint.Status == ComplaintStatus.Closed ? "Bu shikoyatga baho allaqachon qo'yilgan." : "Baho shikoyat yechilgandan keyin qo'yiladi." });

        complaint.GiveFeedback(request.Score, comment);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---- who is asking, and where a new complaint goes ------------------------------------------

    /// <summary>Who is calling and what they hold: at least one of "may file" or "may view", or nothing to do here. The list is not one permission's
    /// because a student files complaints and reads their own without ever holding <c>complaints.view</c>.</summary>
    private async Task<Caller?> CallerAsync(CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return null;

        var view = await permissions.ResolveAsync(userId, "complaints.view", ct);
        var known = view ?? await permissions.ResolveAsync(userId, "complaints.create", ct);
        return known is null ? null : new Caller(userId, known.OrganizationId, view);
    }

    /// <summary>The complaint the caller may act on: it has to be one they can see, and they must be its assignee or run its branch.</summary>
    private async Task<(Complaint? Complaint, IActionResult? Problem)> LoadForActionAsync(ResolvedScope scope, Guid id, CancellationToken ct)
    {
        var complaint = await ComplaintQueries.Visible(db, scope.OrganizationId, scope.UserId, scope).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (complaint is null) return (null, NotFound());

        if (complaint.AssignedToUserId == scope.UserId) return (complaint, null);
        var manage = await permissions.ResolveAsync(scope.UserId, "complaints.manage", ct);
        return ComplaintQueries.Covers(manage, complaint.BranchId) ? (complaint, null) : (null, Forbid());
    }

    private sealed record Target(Guid BranchId, Guid? StudentId);

    /// <summary>
    /// Which branch (and student) a new complaint is about. A student's is about themselves and a parent's about a child; with a student, the branch is
    /// where they are enrolled (a choice among them when they are in several); without one it is a branch the caller belongs to.
    /// </summary>
    private async Task<(Target? Target, ActionResult? Problem)> ResolveTargetAsync(ResolvedScope scope, CreateComplaintRequest request, CancellationToken ct)
    {
        var studentId = request.StudentId;

        if (studentId is null && scope.Scope == ScopeType.Self)
            studentId = await db.Students.Where(s => s.UserId == scope.UserId && s.OrganizationId == scope.OrganizationId)
                .Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);

        if (studentId is null && scope.Scope == ScopeType.MyChildren)
        {
            var children = await scope.ScopedStudentIds(db)!.Distinct().Take(2).ToListAsync(ct);
            if (children.Count == 0) return (null, BadRequest(new { error = "Sizga bog'langan farzand topilmadi." }));
            if (children.Count > 1) return (null, BadRequest(new { error = "Qaysi farzand haqida ekanini tanlang." }));
            studentId = children[0];
        }

        var orgBranchIds = await db.Branches.Where(b => b.OrganizationId == scope.OrganizationId).Select(b => b.Id).ToListAsync(ct);
        // Where the caller may put a complaint that is not tied to a student: the whole organization for an organization-wide scope, else their own branches.
        var reachable = scope.Scope == ScopeType.AllOrganization ? orgBranchIds : scope.BranchIds.Where(orgBranchIds.Contains).ToList();

        if (studentId is { } sid)
        {
            if (!await db.Students.AnyAsync(s => s.Id == sid && s.OrganizationId == scope.OrganizationId, ct))
                return (null, NotFound(new { error = "Student topilmadi." }));
            if (!await scope.CanAccessStudentAsync(db, sid)) return (null, Forbid());

            // The student's branches: where they study now, or where they last did.
            var enrolled = await (
                from e in db.Enrollments
                join g in db.Groups on e.GroupId equals g.Id
                where e.StudentId == sid
                select new { g.BranchId, e.Status }).ToListAsync(ct);
            var active = enrolled.Where(x => x.Status == EnrollmentStatus.Active).Select(x => x.BranchId).Distinct().ToList();
            var branches = active.Count > 0 ? active : enrolled.Select(x => x.BranchId).Distinct().ToList();
            // An office worker files into the branches they run; the student's other branches are not theirs to put a complaint in.
            if (scope.Scope == ScopeType.MyBranches) branches = branches.Where(reachable.Contains).ToList();

            if (request.BranchId is { } named)
            {
                if (branches.Count > 0 ? !branches.Contains(named) : !reachable.Contains(named))
                    return (null, BadRequest(new { error = "Student bu filialda o'qimaydi." }));
                return (new Target(named, sid), null);
            }
            if (branches.Count == 1) return (new Target(branches[0], sid), null);
            return (null, BadRequest(new
            {
                error = branches.Count == 0 ? "Student hech qaysi guruhga yozilmagan — filialni ko'rsating." : "Student bir necha filialda o'qiydi — filialni tanlang.",
            }));
        }

        if (request.BranchId is { } wanted)
            return reachable.Contains(wanted) ? (new Target(wanted, null), null) : (null, Forbid());
        if (reachable.Count == 1) return (new Target(reachable[0], null), null);
        return (null, BadRequest(new { error = "Filialni tanlang." }));
    }

    // ---- handing it out -------------------------------------------------------------------------

    /// <summary>Active staff who read complaints and reach the complaint's branch, judged on the permission each of them actually holds (an override
    /// counts) — a role's name is never asked.</summary>
    private async Task<List<ComplaintCandidateDto>> CandidatesAsync(Complaint complaint, CancellationToken ct)
    {
        // Who could hold it at all: anyone whose role grants complaints.view. Each is then resolved for their real scope.
        var roles = PermissionResolver.RolesHolding(db, complaint.OrganizationId, "complaints.view");
        var holderIds = await db.UserRoles.Where(ur => roles.Contains(ur.RoleId)).Select(ur => ur.UserId).Distinct().ToListAsync(ct);

        var users = await db.Users
            .Where(u => holderIds.Contains(u.Id) && u.OrganizationId == complaint.OrganizationId && u.Status == UserStatus.Active)
            .OrderBy(u => u.FullName).ThenBy(u => u.Id)
            .Select(u => new ComplaintCandidateDto(u.Id, u.FullName, u.Email)).ToListAsync(ct);

        var result = new List<ComplaintCandidateDto>();
        foreach (var user in users)
            if (ComplaintQueries.Covers(await permissions.ResolveAsync(user.UserId, "complaints.view", ct), complaint.BranchId))
                result.Add(user);
        return result;
    }

    // ---- what the screens get -------------------------------------------------------------------

    private async Task<List<ComplaintDto>> ToDtosAsync(IReadOnlyCollection<Complaint> items, Caller caller, CancellationToken ct)
    {
        var manage = await permissions.ResolveAsync(caller.UserId, "complaints.manage", ct);

        var branchIds = items.Select(c => c.BranchId).Distinct().ToList();
        var studentIds = items.Where(c => c.StudentId is not null).Select(c => c.StudentId!.Value).Distinct().ToList();
        var userIds = items.Select(c => c.AuthorUserId)
            .Concat(items.Where(c => c.AssignedToUserId is not null).Select(c => c.AssignedToUserId!.Value)).Distinct().ToList();

        var branches = await db.Branches.Where(b => branchIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Name, ct);
        var users = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var students = await (
            from s in db.Students
            join u in db.Users on s.UserId equals u.Id
            where studentIds.Contains(s.Id)
            select new { s.Id, u.FullName }).ToDictionaryAsync(x => x.Id, x => x.FullName, ct);

        return items.Select(c =>
        {
            var manages = ComplaintQueries.Covers(manage, c.BranchId);
            var open = c.Status is ComplaintStatus.Assigned or ComplaintStatus.InProgress;
            return new ComplaintDto(
                c.Id, c.BranchId, branches.GetValueOrDefault(c.BranchId, "Filial"),
                c.StudentId, c.StudentId is { } sid ? students.GetValueOrDefault(sid) : null,
                c.AuthorUserId, users.GetValueOrDefault(c.AuthorUserId, "Foydalanuvchi"), c.Subject, c.Body, c.Status,
                c.AssignedToUserId, c.AssignedToUserId is { } assignee ? users.GetValueOrDefault(assignee) : null,
                c.Resolution, c.CreatedAt, c.ResolvedAt, c.SatisfactionScore, c.FeedbackComment,
                IsMine: c.AuthorUserId == caller.UserId,
                CanManage: manages,
                CanAct: open && (manages || c.AssignedToUserId == caller.UserId),
                CanRate: c.AuthorUserId == caller.UserId && c.Status == ComplaintStatus.Resolved);
        }).ToList();
    }
}
