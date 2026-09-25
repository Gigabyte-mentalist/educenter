using System.Text.Json;
using EduCenter.Api.Academic;
using EduCenter.Api.Authorization;
using EduCenter.Api.Lms;
using EduCenter.Infrastructure;
using EduCenter.Modules.Academic.Domain;
using EduCenter.Modules.Lms.Domain;
using EduCenter.Modules.Students.Domain;
using EduCenter.Modules.Timeline.Domain;
using EduCenter.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Students;

public record StudentDto(
    Guid Id, Guid UserId, string FullName, string Email, string Phone, string StudentNumber,
    DateOnly JoinedDate, string? CurrentLevel, StudentStatus Status);

/// <param name="Header">What TZ §7 puts in the profile's header beyond the student's own fields — kept out of <see cref="StudentDto"/>,
/// which the student list uses too and would then pay for a group and teacher lookup on every row.</param>
public record StudentOverviewDto(
    StudentDto Student, List<EnrollmentSummaryDto> Enrollments,
    int LessonsAttended, int LessonsAbsent, double AttendanceRate,
    int HomeworkAssigned, int HomeworkSubmitted, double HomeworkCompletionRate,
    StudentHeaderDto? Header = null);

/// <param name="Groups">The groups the student is in now, the one they joined first at the front (the header shows it and "+N groups").</param>
/// <param name="PhotoVersion">Present when the student has a picture; the picture is fetched from <c>/api/users/{userId}/photo?v=…</c>.</param>
public record StudentHeaderDto(bool HasPhoto, string? PhotoVersion, List<StudentHeaderGroupDto> Groups, List<StudentHeaderParentDto> Parents);
public record StudentHeaderGroupDto(
    Guid GroupId, string GroupName, string BranchName, string CourseName,
    List<string> MainTeacherNames, List<string> SupportTeacherNames);
public record StudentHeaderParentDto(string FullName, string Phone, ParentRelationship Relationship);

public record EnrollmentSummaryDto(Guid Id, Guid GroupId, string GroupName, EnrollmentStatus Status, DateTimeOffset EnrolledAt);

public record TimelineEventDto(Guid Id, string EventType, string? PayloadJson, DateTimeOffset OccurredAt);

/// <summary>Student 360° (docs/00_TZ.md §7): profile + academic snapshot + timeline.</summary>
[ApiController]
[Route("api/students")]
public class StudentsController(AppDbContext db, PermissionResolver permissions) : ControllerBase
{
    /// <summary>One page of the students the caller may see. Search matches name, email, phone and student number; the group
    /// filter means "actively enrolled in that group".</summary>
    [HttpGet]
    [Authorize(Policy = "students.view")]
    public async Task<ActionResult<Page<StudentDto>>> List(
        [FromQuery] string? search, [FromQuery] StudentStatus? status, [FromQuery] Guid? groupId,
        [FromQuery] PageRequest? paging, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();

        var query =
            from s in db.Students
            join u in db.Users on s.UserId equals u.Id
            where s.OrganizationId == scope.OrganizationId
            select new { s, u };

        switch (scope.Scope)
        {
            case SharedKernel.Authorization.ScopeType.AllOrganization:
                break;

            case SharedKernel.Authorization.ScopeType.MyBranches:
                // Branch-scoped staff see students enrolled in groups of their branches — not the whole org.
                query = query.Where(x => db.Enrollments.Any(e =>
                    e.StudentId == x.s.Id && db.Groups.Any(g => g.Id == e.GroupId && scope.BranchIds.Contains(g.BranchId))));
                break;

            case SharedKernel.Authorization.ScopeType.MyGroups:
            case SharedKernel.Authorization.ScopeType.AssignedGroups:
            {
                var groupIds = await scope.VisibleGroupIdsAsync(db) ?? [];
                query = query.Where(x => db.Enrollments.Any(e =>
                    e.StudentId == x.s.Id && e.Status == EnrollmentStatus.Active && groupIds.Contains(e.GroupId)));
                break;
            }

            default:
            {
                // Self → own record; MyChildren → the children. Lets a student/parent land on /students.
                var visible = await scope.VisibleStudentIdsAsync(db);
                query = query.Where(x => visible.Contains(x.s.Id));
                break;
            }
        }

        if (status is not null) query = query.Where(x => x.s.Status == status);
        if (groupId is not null)
            query = query.Where(x => db.Enrollments.Any(e => e.StudentId == x.s.Id && e.GroupId == groupId && e.Status == EnrollmentStatus.Active));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = PagingExtensions.ContainsPattern(search);
            query = query.Where(x => EF.Functions.ILike(x.u.FullName, term) || EF.Functions.ILike(x.u.Email, term)
                || EF.Functions.ILike(x.u.Phone, term) || EF.Functions.ILike(x.s.StudentNumber, term));
        }

        return Ok(await query
            .OrderBy(x => x.u.FullName).ThenBy(x => x.s.Id)
            .Select(x => new StudentDto(x.s.Id, x.s.UserId, x.u.FullName, x.u.Email, x.u.Phone, x.s.StudentNumber, x.s.JoinedDate, x.s.CurrentLevel, x.s.Status))
            .ToPageAsync(paging, ct));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "students.view")]
    public async Task<ActionResult<StudentDto>> GetById(Guid id)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (!await scope.CanAccessStudentAsync(db, id))
            return Forbid();

        var dto = await (
            from s in db.Students
            join u in db.Users on s.UserId equals u.Id
            where s.Id == id && s.OrganizationId == scope.OrganizationId
            select new StudentDto(s.Id, s.UserId, u.FullName, u.Email, u.Phone, s.StudentNumber, s.JoinedDate, s.CurrentLevel, s.Status)
        ).FirstOrDefaultAsync();

        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpGet("{id:guid}/overview")]
    [Authorize(Policy = "students.view")]
    public async Task<ActionResult<StudentOverviewDto>> GetOverview(Guid id)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (!await scope.CanAccessStudentAsync(db, id))
            return Forbid();

        var student = await (
            from s in db.Students
            join u in db.Users on s.UserId equals u.Id
            where s.Id == id && s.OrganizationId == scope.OrganizationId
            select new StudentDto(s.Id, s.UserId, u.FullName, u.Email, u.Phone, s.StudentNumber, s.JoinedDate, s.CurrentLevel, s.Status)
        ).FirstOrDefaultAsync();
        if (student is null) return NotFound();

        var enrollments = await (
            from e in db.Enrollments
            join g in db.Groups on e.GroupId equals g.Id
            where e.StudentId == id
            select new EnrollmentSummaryDto(e.Id, g.Id, g.Name, e.Status, e.EnrolledAt)
        ).ToListAsync();

        // The same rule as the Learning Profile, so the two tabs of one student never show two attendance figures: cancelled lessons
        // do not count, an excused absence is neither for nor against, and Late/Online mean the student took part.
        var attendanceCounts = await (
            from a in db.Attendance
            join l in db.Lessons.Held() on a.LessonId equals l.Id
            where a.StudentId == id && a.OrganizationId == scope.OrganizationId
            group a by a.Status into g
            select new { g.Key, Count = g.Count() }
        ).ToListAsync();
        var attended = attendanceCounts.Where(x => LearningProfileCalculator.Attended(x.Key)).Sum(x => x.Count);
        var absent = attendanceCounts.FirstOrDefault(x => x.Key == AttendanceStatus.Absent)?.Count ?? 0;
        var totalMarked = attendanceCounts.Where(x => LearningProfileCalculator.CountsForAttendance(x.Key)).Sum(x => x.Count);
        var attendanceRate = (double)(LearningProfileCalculator.Rate(attended, totalMarked) ?? 0);

        var groupIds = enrollments.Select(e => e.GroupId).ToList();
        // Only what was given to this student: an individual task set for a classmate is not one they skipped.
        var assignedHomeworkCount = await (
            from h in db.Homework.AssignedTo(db, id)
            join l in db.Lessons on h.LessonId equals l.Id
            where groupIds.Contains(l.GroupId)
            select h.Id
        ).CountAsync();
        var submittedCount = await db.HomeworkSubmissions.CountAsync(s => s.StudentId == id);
        var homeworkRate = assignedHomeworkCount == 0 ? 0 : Math.Round(submittedCount * 100.0 / assignedHomeworkCount, 1);

        return Ok(new StudentOverviewDto(student, enrollments, attended, absent, attendanceRate,
            assignedHomeworkCount, submittedCount, homeworkRate, await BuildHeaderAsync(student.UserId, id)));
    }

    private async Task<StudentHeaderDto> BuildHeaderAsync(Guid userId, Guid studentId)
    {
        var groups = await (
            from e in db.Enrollments
            join g in db.Groups on e.GroupId equals g.Id
            join c in db.Courses on g.CourseId equals c.Id
            join b in db.Branches on g.BranchId equals b.Id
            where e.StudentId == studentId && e.Status == EnrollmentStatus.Active
            orderby e.EnrolledAt, g.Name
            select new { GroupId = g.Id, GroupName = g.Name, BranchName = b.Name, CourseName = c.Name }
        ).ToListAsync();

        var groupIds = groups.Select(g => g.GroupId).ToList();
        var staff = await (
            from gs in db.GroupStaff
            join u in db.Users on gs.UserId equals u.Id
            where groupIds.Contains(gs.GroupId)
            orderby u.FullName
            select new { gs.GroupId, gs.Role, u.FullName }
        ).ToListAsync();

        var parents = await (
            from sp in db.StudentParents
            join p in db.Parents on sp.ParentId equals p.Id
            join u in db.Users on p.UserId equals u.Id
            where sp.StudentId == studentId
            orderby u.FullName
            select new StudentHeaderParentDto(u.FullName, u.Phone, sp.Relationship)
        ).ToListAsync();

        var photoKey = await db.Users.Where(u => u.Id == userId).Select(u => u.PhotoKey).FirstOrDefaultAsync();

        return new StudentHeaderDto(
            photoKey is not null, photoKey is null ? null : Users.UserPhotoStorage.VersionOf(photoKey),
            groups.Select(g => new StudentHeaderGroupDto(g.GroupId, g.GroupName, g.BranchName, g.CourseName,
                staff.Where(x => x.GroupId == g.GroupId && x.Role == GroupStaffRole.MainTeacher).Select(x => x.FullName).ToList(),
                staff.Where(x => x.GroupId == g.GroupId && x.Role == GroupStaffRole.SupportTeacher).Select(x => x.FullName).ToList())).ToList(),
            parents);
    }

    [HttpGet("{id:guid}/timeline")]
    [Authorize(Policy = "students.view")]
    public async Task<ActionResult<List<TimelineEventDto>>> GetTimeline(Guid id, [FromQuery] int limit = 200)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (!await scope.CanAccessStudentAsync(db, id))
            return Forbid();

        // Every lesson attended or missed and every homework adds an event, so a student who has been here a year has hundreds:
        // the profile shows the latest ones, not the whole history in one response.
        limit = Math.Clamp(limit, 1, 500);
        var visible = db.StudentTimelineEvents.Where(e => e.StudentId == id);

        // The timeline is read by the student's teachers, the student and the parents. That a complaint was filed — often about one of them — is for the
        // people who handle complaints only (S06.07).
        if (await permissions.ResolveAsync(scope.UserId, "complaints.view") is null)
            visible = visible.Where(e => !TimelineEventType.ComplaintEvents.Contains(e.EventType));

        var events = await visible
            .OrderByDescending(e => e.OccurredAt)
            .Take(limit)
            .Select(e => new TimelineEventDto(e.Id, e.EventType, e.PayloadJson, e.OccurredAt))
            .ToListAsync();

        // docs/00_TZ.md §9 — "CRM va academic tarixni bitta joyga birlashtiradi". Before conversion the
        // student was a Lead with no Student row, so that history lives on the lead: when they first came
        // in, every operator call, and every trial lesson. It is read from there rather than copied into
        // student_timeline_events at conversion, so the lead's own records stay the single source of truth.
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.ConvertedStudentId == id && l.OrganizationId == scope.OrganizationId);
        if (lead is not null)
        {
            events.Add(new TimelineEventDto(lead.Id, "LEAD_CREATED",
                JsonSerializer.Serialize(new { source = lead.Source, interestedCourse = lead.InterestedCourse }), lead.CreatedAt));

            var calls = await db.CrmCalls.Where(c => c.LeadId == lead.Id).ToListAsync();
            events.AddRange(calls.Select(c => new TimelineEventDto(c.Id, "CRM_CALL",
                JsonSerializer.Serialize(new { outcome = c.Outcome.ToString(), notes = c.Notes }), c.CalledAt)));

            var trials = await (
                from t in db.TrialLessons
                join g in db.Groups on t.GroupId equals g.Id
                where t.LeadId == lead.Id
                select new { t.Id, t.ScheduledAt, t.AttendanceStatus, GroupName = g.Name }
            ).ToListAsync();
            events.AddRange(trials.Select(t => new TimelineEventDto(t.Id, "TRIAL_LESSON",
                JsonSerializer.Serialize(new { groupName = t.GroupName, attendance = t.AttendanceStatus.ToString() }), t.ScheduledAt)));
        }

        return Ok(events.OrderByDescending(e => e.OccurredAt).Take(limit).ToList());
    }
}
