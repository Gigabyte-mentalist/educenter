using EduCenter.Api.Authorization;
using EduCenter.Api.Lms;
using EduCenter.Api.Notifications;
using EduCenter.Api.Students;
using EduCenter.Modules.Notifications.Domain;
using EduCenter.Infrastructure;
using EduCenter.Modules.Academic.Domain;
using EduCenter.Modules.Identity.Domain;
using EduCenter.Modules.Lms.Domain;
using EduCenter.Modules.Students.Domain;
using EduCenter.SharedKernel.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Academic;

public record GroupStaffDto(Guid UserId, GroupStaffRole Role);
public record GroupDto(
    Guid Id, Guid BranchId, Guid CourseId, string Name, int Capacity, GroupStatus Status, List<GroupStaffDto> Staff, int StudentCount);
public record CreateGroupRequest(Guid BranchId, Guid CourseId, string Name, int Capacity);
public record AssignStaffRequest(Guid UserId, GroupStaffRole Role);
/// <summary>Both fields optional — only what is sent changes.</summary>
public record UpdateGroupRequest(string? Name, int? Capacity);
public record SetGroupStatusRequest(GroupStatus Status);
public record GroupStudentDto(Guid StudentId, string FullName, string StudentNumber);

public record GroupStaffMemberDto(Guid UserId, string FullName, GroupStaffRole Role);
public record GroupNextLessonDto(Guid Id, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string RoomName, string? Topic);

/// <param name="AttendanceRate">0-100; null while nothing has been marked yet.</param>
/// <param name="AverageExamScore">Mean exam overall; null while the student has no result.</param>
public record GroupStudentResultDto(
    Guid StudentId, string FullName, string StudentNumber, double? AttendanceRate, int Absences, int HomeworkSubmitted,
    decimal? AverageExamScore);

/// <param name="AttendanceRate">0-100 across every marked lesson of the group.</param>
/// <param name="HomeworkCompletionRate">0-100: submissions received out of (assignments × students).</param>
public record GroupResultsDto(
    double AttendanceRate, double HomeworkCompletionRate, decimal? AverageExamScore, int LessonsHeld, int HomeworkAssigned,
    int ExamsHeld, List<GroupStudentResultDto> Students);

/// <summary>Group detail. <c>Results</c> (marks and per-student figures) is null for a student or parent — they may open the
/// group but not read their classmates' results.</summary>
public record GroupOverviewDto(
    Guid Id, string Name, string CourseName, string BranchName, GroupStatus Status, int Capacity, int StudentCount,
    List<GroupStaffMemberDto> Staff, List<GroupNextLessonDto> UpcomingLessons, GroupResultsDto? Results, Guid BranchId = default);

[ApiController]
[Route("api/groups")]
public class GroupsController(AppDbContext db, NotificationDispatcher notifier, NotificationRecipients recipients) : ControllerBase
{
    private const int MaxNameLength = 200;

    [HttpGet]
    [Authorize(Policy = "groups.view")]
    public async Task<ActionResult<List<GroupDto>>> List([FromQuery] Guid? branchId)
    {
        var scope = HttpContext.RequireResolvedScope();

        var query = await scope.ScopedGroupsAsync(db);
        if (branchId is not null)
            query = query.Where(g => g.BranchId == branchId);

        var groups = await query
            .OrderBy(g => g.Name)
            .Select(g => new GroupDto(
                g.Id, g.BranchId, g.CourseId, g.Name, g.Capacity, g.Status,
                g.Staff.Select(s => new GroupStaffDto(s.UserId, s.Role)).ToList(), 0))
            .ToListAsync();

        var groupIds = groups.Select(g => g.Id).ToList();
        var counts = await db.Enrollments
            .Where(e => groupIds.Contains(e.GroupId) && e.Status == EnrollmentStatus.Active)
            .GroupBy(e => e.GroupId)
            .Select(x => new { GroupId = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.GroupId, x => x.Count);

        return Ok(groups.Select(g => g with { StudentCount = counts.GetValueOrDefault(g.Id) }).ToList());
    }

    [HttpPost]
    [Authorize(Policy = "groups.create")]
    public async Task<ActionResult<GroupDto>> Create(CreateGroupRequest request)
    {
        var scope = HttpContext.RequireResolvedScope();

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Guruh nomi ko'rsatilishi shart." });
        if (request.Capacity < 1)
            return BadRequest(new { error = "Sig'im kamida 1 bo'lishi kerak." });

        var branch = await db.Branches.FirstOrDefaultAsync(b => b.Id == request.BranchId && b.OrganizationId == scope.OrganizationId);
        if (branch is null) return NotFound(new { error = "Filial topilmadi." });

        if (!await scope.CanAccessGroupAsync(db, Guid.Empty, branch.Id))
            return Forbid();

        var courseExists = await db.Courses.AnyAsync(c => c.Id == request.CourseId && c.OrganizationId == scope.OrganizationId);
        if (!courseExists) return NotFound(new { error = "Kurs topilmadi." });

        var group = new Group(scope.OrganizationId, request.BranchId, request.CourseId, request.Name, request.Capacity);
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(List),
            new GroupDto(group.Id, group.BranchId, group.CourseId, group.Name, group.Capacity, group.Status, [], 0));
    }

    [HttpPost("{id:guid}/staff")]
    [Authorize(Policy = "groups.edit")]
    public async Task<IActionResult> AssignStaff(Guid id, AssignStaffRequest request)
    {
        var scope = HttpContext.RequireResolvedScope();

        var group = await db.Groups.Include(g => g.Staff)
            .FirstOrDefaultAsync(g => g.Id == id && g.OrganizationId == scope.OrganizationId);
        if (group is null) return NotFound();

        if (!await scope.CanAccessGroupAsync(db, group.Id, group.BranchId))
            return Forbid();

        // The assignee must be a real user of this org holding a teaching role — a GroupStaff row
        // pointing at an arbitrary/foreign user id would silently break every MyGroups scope check.
        var expectedRole = request.Role == GroupStaffRole.MainTeacher ? RoleCode.Teacher : RoleCode.SupportTeacher;
        var isEligible = await (
            from u in db.Users
            join ur in db.UserRoles on u.Id equals ur.UserId
            join r in db.Roles on ur.RoleId equals r.Id
            where u.Id == request.UserId && u.OrganizationId == scope.OrganizationId
                  && (r.Code == RoleCode.Teacher || r.Code == RoleCode.SupportTeacher)
            select u.Id).AnyAsync();
        if (!isEligible)
            return BadRequest(new { error = $"Foydalanuvchi topilmadi yoki o'qituvchi roli yo'q (kutilgan: {expectedRole})." });

        try
        {
            group.AssignStaff(request.UserId, request.Role);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }

        var roleLabel = request.Role == GroupStaffRole.MainTeacher ? "asosiy o'qituvchi" : "yordamchi o'qituvchi";
        await notifier.NotifyAsync(scope.OrganizationId, [request.UserId],
            new NotificationMessage(NotificationType.TeacherAssigned, $"Siz {group.Name} guruhiga biriktirildingiz",
                $"Vazifangiz: {roleLabel}.", "/groups"));
        if (request.Role == GroupStaffRole.MainTeacher)
        {
            var teacherName = await recipients.UserNameAsync(request.UserId);
            await notifier.NotifyAsync(scope.OrganizationId, await recipients.GroupStudentsAsync(group.Id),
                new NotificationMessage(NotificationType.TeacherAssigned, $"{group.Name}: o'qituvchi biriktirildi",
                    $"Guruhingizga {teacherName} asosiy o'qituvchi sifatida biriktirildi.", "/groups"));
        }

        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPatch("{id:guid}")]
    [Authorize(Policy = "groups.edit")]
    public async Task<ActionResult<GroupDto>> Update(Guid id, UpdateGroupRequest request, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        var group = await db.Groups.Include(g => g.Staff)
            .FirstOrDefaultAsync(g => g.Id == id && g.OrganizationId == scope.OrganizationId, ct);
        if (group is null) return NotFound();
        if (!await scope.CanAccessGroupAsync(db, group.Id, group.BranchId)) return Forbid();

        if (request.Name is null && request.Capacity is null)
            return BadRequest(new { error = "O'zgartirish uchun nom yoki sig'imni ko'rsating." });
        var name = request.Name?.Trim() ?? group.Name;
        var capacity = request.Capacity ?? group.Capacity;
        if (name.Length == 0 || name.Length > MaxNameLength)
            return BadRequest(new { error = $"Guruh nomi 1 dan {MaxNameLength} tagacha belgi bo'lishi kerak." });
        if (capacity < 1)
            return BadRequest(new { error = "Sig'im kamida 1 bo'lishi kerak." });

        if (group.IsClosed)
            return Conflict(new { error = "Yopilgan guruhni tahrirlab bo'lmaydi." });
        var enrolled = await ActiveCountAsync(group.Id, ct);
        if (capacity < enrolled)
            return Conflict(new { error = $"Sig'im hozirgi faol talabalar sonidan ({enrolled}) past bo'lishi mumkin emas." });

        group.Update(name, capacity);
        await db.SaveChangesAsync(ct);
        return Ok(ToDto(group, enrolled));
    }

    /// <summary>Forming → Active → Completed, or Forming/Active → Cancelled; nothing leaves Completed or Cancelled. Closing
    /// a group ends its open enrollments with it and cancels the lessons still ahead of it.</summary>
    [HttpPost("{id:guid}/status")]
    [Authorize(Policy = "groups.edit")]
    public async Task<ActionResult<GroupDto>> SetStatus(Guid id, SetGroupStatusRequest request, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        var group = await db.Groups.Include(g => g.Staff)
            .FirstOrDefaultAsync(g => g.Id == id && g.OrganizationId == scope.OrganizationId, ct);
        if (group is null) return NotFound();
        if (!await scope.CanAccessGroupAsync(db, group.Id, group.BranchId)) return Forbid();

        try
        {
            switch (request.Status)
            {
                case GroupStatus.Active: group.Activate(); break;
                case GroupStatus.Completed: group.Complete(); break;
                case GroupStatus.Cancelled: group.Cancel(); break;
                default: return Conflict(new { error = "Guruhni 'Shakllanmoqda' holatiga qaytarib bo'lmaydi." });
            }
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }

        if (group.IsClosed)
        {
            // The one message students and teachers get is the GroupClosed one below; the lessons need no message of their own.
            var ahead = await db.Lessons.Held().Where(l => l.GroupId == group.Id && l.StartsAt > DateTimeOffset.UtcNow).ToListAsync(ct);
            foreach (var lesson in ahead) lesson.Cancel("Guruh yopildi");
            await CloseEnrollmentsAsync(group, ct);
        }

        await db.SaveChangesAsync(ct);
        return Ok(ToDto(group, await ActiveCountAsync(group.Id, ct)));
    }

    /// <summary>Ends every open enrollment (Completed for a finished group, Dropped for a cancelled one), lets the student's
    /// own status follow, and tells the students and teachers. The timeline gets no GRADUATED: a group closing is not a person
    /// finishing — that stays the separate graduate action.</summary>
    private async Task CloseEnrollmentsAsync(Group group, CancellationToken ct)
    {
        var completed = group.Status == GroupStatus.Completed;
        var open = await db.Enrollments
            .Where(e => e.GroupId == group.Id && (e.Status == EnrollmentStatus.Active || e.Status == EnrollmentStatus.Frozen))
            .ToListAsync(ct);
        foreach (var enrollment in open)
            enrollment.End(completed ? EnrollmentStatus.Completed : EnrollmentStatus.Dropped, completed ? null : "Guruh bekor qilindi");

        var studentIds = open.Select(e => e.StudentId).ToList();
        var students = await db.Students.Where(s => studentIds.Contains(s.Id)).ToListAsync(ct);
        // The ones ended above are already tracked, so this read sees them ended.
        var byStudent = (await db.Enrollments.Where(e => studentIds.Contains(e.StudentId)).ToListAsync(ct)).ToLookup(e => e.StudentId);
        foreach (var student in students)
            student.SetStatus(StudentStatusRules.Recompute(student, byStudent[student.Id].ToList()));

        var audience = students.Select(s => s.UserId).Concat(group.Staff.Select(s => s.UserId)).Distinct().ToList();
        if (audience.Count == 0) return;
        var message = completed
            ? new NotificationMessage(NotificationType.GroupClosed, $"{group.Name}: guruh yakunlandi", "Guruh o'quv kursini yakunladi.", "/groups")
            : new NotificationMessage(NotificationType.GroupClosed, $"{group.Name}: guruh bekor qilindi",
                "Guruh bekor qilindi. Tafsilot uchun markaz bilan bog'laning.", "/groups");
        await notifier.NotifyAsync(group.OrganizationId, audience, message, ct);
    }

    /// <summary>Takes a teacher off the group. Refused for the group's only main teacher, and while the teacher still has lessons
    /// ahead in it — the lesson row keeps its own teacher id, so the lessons would otherwise stay in their name.</summary>
    [HttpDelete("{id:guid}/staff/{userId:guid}")]
    [Authorize(Policy = "groups.edit")]
    public async Task<IActionResult> RemoveStaff(Guid id, Guid userId, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        var group = await db.Groups.Include(g => g.Staff)
            .FirstOrDefaultAsync(g => g.Id == id && g.OrganizationId == scope.OrganizationId, ct);
        if (group is null) return NotFound();
        if (!await scope.CanAccessGroupAsync(db, group.Id, group.BranchId)) return Forbid();

        if (group.Staff.All(s => s.UserId != userId))
            return NotFound(new { error = "Bu foydalanuvchi guruhga biriktirilmagan." });
        if (group.IsLastMainTeacher(userId))
            return Conflict(new { error = "Guruhning oxirgi asosiy o'qituvchisini olib tashlab bo'lmaydi." });

        var upcoming = await db.Lessons.Held().CountAsync(l => l.GroupId == id && l.TeacherId == userId && l.StartsAt > DateTimeOffset.UtcNow, ct);
        if (upcoming > 0)
            return Conflict(new
            {
                error = $"O'qituvchining bu guruhda kelajakda {upcoming} ta darsi bor. Avval darslarni boshqa o'qituvchiga o'tkazing.",
            });

        db.GroupStaff.Remove(group.RemoveStaff(userId));
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Erases an empty group. Everything that points at a group id — enrollments, lessons, exams, invoices, support
    /// cases, trial lessons, chat messages, knowledge documents — is counted first and any of it refuses the delete with the
    /// list, because those rows would otherwise be left pointing at a group that no longer resolves. Cancelling is what the
    /// caller wants then (<c>canCancel</c>); staff rows and a chat with no messages carry no history and go with the group.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "groups.delete")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        var group = await db.Groups.Include(g => g.Staff)
            .FirstOrDefaultAsync(g => g.Id == id && g.OrganizationId == scope.OrganizationId, ct);
        if (group is null) return NotFound();
        if (!await scope.CanAccessGroupAsync(db, group.Id, group.BranchId)) return Forbid();

        var chatIds = await db.ChatRooms.Where(c => c.GroupId == id).Select(c => c.Id).ToListAsync(ct);

        var blockers = new List<string>();
        if (await db.Enrollments.AnyAsync(e => e.GroupId == id, ct)) blockers.Add("o'quvchilar yozuvi (enrollment)");
        if (await db.Lessons.AnyAsync(l => l.GroupId == id, ct)) blockers.Add("darslar jadvali");
        if (await db.Exams.AnyAsync(e => e.GroupId == id, ct)) blockers.Add("imtihonlar");
        if (await db.Invoices.AnyAsync(i => i.GroupId == id, ct)) blockers.Add("hisob-fakturalar");
        if (await db.SupportCases.AnyAsync(c => c.GroupId == id, ct)) blockers.Add("support holatlari");
        if (await db.TrialLessons.AnyAsync(t => t.GroupId == id, ct)) blockers.Add("sinov darslari");
        if (await db.KnowledgeDocuments.AnyAsync(d => d.GroupId == id, ct)) blockers.Add("bilim bazasi hujjatlari");
        if (chatIds.Count > 0 && await db.ChatMessages.AnyAsync(m => chatIds.Contains(m.ChatId), ct)) blockers.Add("chat xabarlari");

        if (blockers.Count > 0)
        {
            var canCancel = !group.IsClosed;
            return Conflict(new
            {
                error = $"Bu guruhni o'chirib bo'lmaydi — unga bog'liq ma'lumotlar bor: {string.Join(", ", blockers)}. "
                    + (canCancel ? "Buning o'rniga guruhni bekor qiling — tarix saqlanadi." : "Guruh allaqachon yopilgan — tarix saqlanadi."),
                blockers,
                canCancel,
            });
        }

        await db.ChatMembers.Where(m => chatIds.Contains(m.ChatId)).ExecuteDeleteAsync(ct);
        await db.ChatRooms.Where(c => c.GroupId == id).ExecuteDeleteAsync(ct);
        db.GroupStaff.RemoveRange(group.Staff.ToList());
        db.Groups.Remove(group);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private Task<int> ActiveCountAsync(Guid groupId, CancellationToken ct) =>
        db.Enrollments.CountAsync(e => e.GroupId == groupId && e.Status == EnrollmentStatus.Active, ct);

    private static GroupDto ToDto(Group group, int studentCount) =>
        new(group.Id, group.BranchId, group.CourseId, group.Name, group.Capacity, group.Status,
            group.Staff.Select(s => new GroupStaffDto(s.UserId, s.Role)).ToList(), studentCount);

    /// <summary>Everything a teacher wants when they open a group: who teaches it, how full it is, what is coming up and how
    /// the students are doing. Built from attendance, homework and exam rows the product already keeps.</summary>
    [HttpGet("{id:guid}/overview")]
    [Authorize(Policy = "groups.view")]
    public async Task<ActionResult<GroupOverviewDto>> Overview(Guid id, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        var group = await db.Groups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id && g.OrganizationId == scope.OrganizationId, ct);
        if (group is null) return NotFound();
        if (!await scope.CanAccessGroupAsync(db, group.Id, group.BranchId)) return Forbid();

        var courseName = await db.Courses.Where(c => c.Id == group.CourseId).Select(c => c.Name).FirstOrDefaultAsync(ct) ?? "—";
        var branchName = await db.Branches.Where(b => b.Id == group.BranchId).Select(b => b.Name).FirstOrDefaultAsync(ct) ?? "—";

        var staff = await (
            from gs in db.GroupStaff
            join u in db.Users on gs.UserId equals u.Id
            where gs.GroupId == id
            orderby gs.Role, u.FullName
            select new GroupStaffMemberDto(u.Id, u.FullName, gs.Role)
        ).ToListAsync(ct);

        var roster = await (
            from e in db.Enrollments
            join s in db.Students on e.StudentId equals s.Id
            join u in db.Users on s.UserId equals u.Id
            where e.GroupId == id && e.Status == EnrollmentStatus.Active
            orderby u.FullName
            select new { s.Id, u.FullName, s.StudentNumber }
        ).ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var upcoming = await db.Lessons.AsNoTracking().Held()
            .Where(l => l.GroupId == id && l.StartsAt >= now)
            .OrderBy(l => l.StartsAt).Take(4)
            .Select(l => new GroupNextLessonDto(l.Id, l.StartsAt, l.EndsAt, l.RoomName, l.Topic))
            .ToListAsync(ct);

        GroupResultsDto? results = null;
        if (!scope.IsPersonalScope())
            results = await BuildResultsAsync(id, now, roster.Select(r => (r.Id, r.FullName, r.StudentNumber)).ToList(), ct);

        return Ok(new GroupOverviewDto(group.Id, group.Name, courseName, branchName, group.Status, group.Capacity,
            roster.Count, staff, upcoming, results, group.BranchId));
    }

    private async Task<GroupResultsDto> BuildResultsAsync(
        Guid groupId, DateTimeOffset now, List<(Guid Id, string FullName, string StudentNumber)> roster, CancellationToken ct)
    {
        var studentIds = roster.Select(r => r.Id).ToList();

        var lessonsHeld = await db.Lessons.Held().CountAsync(l => l.GroupId == groupId && l.EndsAt <= now, ct);

        var attendance = await (
            from a in db.Attendance
            join l in db.Lessons.Held() on a.LessonId equals l.Id
            where l.GroupId == groupId && studentIds.Contains(a.StudentId)
            select new { a.StudentId, a.Status }
        ).ToListAsync(ct);
        var counted = attendance.Where(a => a.Status != AttendanceStatus.Excused).ToList();
        var attendanceRate = counted.Count == 0 ? 0
            : Math.Round(counted.Count(a => a.Status != AttendanceStatus.Absent) * 100.0 / counted.Count, 1);

        var homeworkIds = await (
            from h in db.Homework
            join l in db.Lessons on h.LessonId equals l.Id
            where l.GroupId == groupId
            select h.Id
        ).ToListAsync(ct);
        var submissions = homeworkIds.Count == 0
            ? []
            : await db.HomeworkSubmissions.AsNoTracking()
                .Where(s => homeworkIds.Contains(s.HomeworkId) && studentIds.Contains(s.StudentId))
                .Select(s => s.StudentId).ToListAsync(ct);
        // The rate's denominator is the pairs actually given out: an individual homework is one pair per student it was set for,
        // not one per student in the group.
        var possible = await db.CountAssignmentsAsync(homeworkIds.Select(id => new HomeworkRef(id, groupId)).ToList(), ct);
        var homeworkRate = possible == 0 ? 0 : Math.Round(submissions.Count * 100.0 / possible, 1);

        var examRows = await (
            from r in db.ExamResults
            join e in db.Exams on r.ExamId equals e.Id
            where e.GroupId == groupId && r.Overall != null && studentIds.Contains(r.StudentId)
            select new { r.StudentId, Overall = r.Overall!.Value }
        ).ToListAsync(ct);
        var examsHeld = await db.Exams.CountAsync(e => e.GroupId == groupId, ct);

        var students = roster.Select(r =>
        {
            var mine = counted.Where(a => a.StudentId == r.Id).ToList();
            var exams = examRows.Where(x => x.StudentId == r.Id).Select(x => x.Overall).ToList();
            return new GroupStudentResultDto(r.Id, r.FullName, r.StudentNumber,
                mine.Count == 0 ? null : Math.Round(mine.Count(a => a.Status != AttendanceStatus.Absent) * 100.0 / mine.Count, 1),
                mine.Count(a => a.Status == AttendanceStatus.Absent),
                submissions.Count(s => s == r.Id),
                exams.Count == 0 ? null : Math.Round(exams.Average(), 2));
        }).ToList();

        return new GroupResultsDto(attendanceRate, homeworkRate,
            examRows.Count == 0 ? null : Math.Round(examRows.Average(x => x.Overall), 2),
            lessonsHeld, homeworkIds.Count, examsHeld, students);
    }

    /// <summary>Roster for a group — backs the Teacher's attendance/homework screens, which need the
    /// enrolled-student list to build a checklist against.</summary>
    [HttpGet("{id:guid}/students")]
    [Authorize(Policy = "attendance.view")]
    public async Task<ActionResult<List<GroupStudentDto>>> ListStudents(Guid id)
    {
        var scope = HttpContext.RequireResolvedScope();
        var group = await db.Groups.FirstOrDefaultAsync(g => g.Id == id && g.OrganizationId == scope.OrganizationId);
        if (group is null) return NotFound();

        if (!await scope.CanAccessGroupAsync(db, group.Id, group.BranchId))
            return Forbid();

        var students = await (
            from e in db.Enrollments
            join s in db.Students on e.StudentId equals s.Id
            join u in db.Users on s.UserId equals u.Id
            where e.GroupId == id && e.Status == EnrollmentStatus.Active
            orderby u.FullName
            select new GroupStudentDto(s.Id, u.FullName, s.StudentNumber)
        ).ToListAsync();

        return Ok(students);
    }
}
