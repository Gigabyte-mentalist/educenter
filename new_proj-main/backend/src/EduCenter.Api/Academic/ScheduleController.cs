using System.Text.Json;
using EduCenter.Api.Authorization;
using EduCenter.Api.Automation;
using EduCenter.Api.Notifications;
using EduCenter.Modules.Notifications.Domain;
using EduCenter.Infrastructure;
using EduCenter.Modules.Academic.Domain;
using EduCenter.Modules.Identity.Domain;
using EduCenter.Modules.Students.Domain;
using EduCenter.Modules.Timeline.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Academic;

/// <param name="TeacherName">Filled by the schedule list, where a day's lessons are shown side by side and "who teaches
/// this" is the first question; the single-lesson responses leave it out.</param>
/// <param name="CancelReason">Set while <paramref name="Status"/> is Cancelled — a cancelled lesson stays on the schedule, marked.</param>
/// <param name="MaxLessonScore">The top of the lesson's score scale, or null when the lesson is not scored; the screens show a
/// score column, and a student's "9 / 10", only when it is set. The teacher's notes are deliberately not part of this DTO.</param>
/// <param name="RoomName">The room's name — for a lesson held in a <c>Room</c> it is that room's name, so screens that only read the name still work.</param>
/// <param name="RoomId">Null for a lesson that only ever named its room as text.</param>
/// <param name="Warnings">Set on the response to creating or changing a lesson when something is worth a second look but not a refusal (the room seats
/// fewer than the group).</param>
public record LessonDto(
    Guid Id, Guid GroupId, Guid TeacherId, string RoomName, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? Topic,
    string? TeacherName = null, LessonStatus Status = LessonStatus.Scheduled, string? CancelReason = null, int? MaxLessonScore = null,
    Guid? RoomId = null, List<string>? Warnings = null);

/// <summary>Only what is sent changes. A <paramref name="RoomId"/> moves the lesson to that room; a <paramref name="RoomName"/> alone leaves
/// the lesson tied to no room but that name.</summary>
public record UpdateLessonRequest(DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, string? RoomName, Guid? TeacherId, string? Topic, Guid? RoomId = null);
public record CancelLessonRequest(string Reason);
public record BulkCancelLessonsRequest(List<Guid> LessonIds, string Reason);

/// <summary>A lesson to be placed. The room is given by <paramref name="RoomId"/> (a <c>Room</c> of the branch) or, as before, by
/// <paramref name="RoomName"/> alone; once validated the name is always the room's name.</summary>
public record LessonCandidate(Guid BranchId, Guid GroupId, Guid TeacherId, string? RoomName, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? Topic, Guid? RoomId = null);

public record ScheduleConflict(string Type, Guid ConflictingLessonId, DateTimeOffset StartsAt, DateTimeOffset EndsAt);

public record ReplaceTeacherRequest(Guid TeacherId, string Reason);

public record SubstituteDto(Guid UserId, string FullName, string Email, bool IsMainTeacher);

[ApiController]
[Route("api/schedule")]
public class ScheduleController(AppDbContext db, NotificationDispatcher notifier, NotificationRecipients recipients) : ControllerBase
{
    // The lengths the lessons table allows (see LessonConfiguration) and the reason column's; a cancel reason is prose.
    private const int MaxRoomNameLength = 50;
    private const int MaxTopicLength = 200;
    private const int MaxReasonLength = 500;
    private const int MaxBulkLessons = 200;

    [HttpGet]
    [Authorize(Policy = "schedule.view")]
    public async Task<ActionResult<List<LessonDto>>> List(
        [FromQuery] Guid? branchId, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to)
    {
        var scope = HttpContext.RequireResolvedScope();

        // Org/branch scopes filter by branch; teachers, support teachers, students and parents see the
        // lessons of the groups they're attached to (staffing or enrollment), regardless of branch.
        var visibleGroupIds = await scope.VisibleGroupIdsAsync(db);
        var query = db.Lessons.Where(l => l.OrganizationId == scope.OrganizationId);

        if (visibleGroupIds is null)
        {
            if (scope.Scope == SharedKernel.Authorization.ScopeType.MyBranches)
                query = query.Where(l => scope.BranchIds.Contains(l.BranchId));
        }
        else
        {
            query = query.Where(l => visibleGroupIds.Contains(l.GroupId));
        }

        if (branchId is not null) query = query.Where(l => l.BranchId == branchId);
        if (from is not null) query = query.Where(l => l.EndsAt >= from);
        if (to is not null) query = query.Where(l => l.StartsAt <= to);

        var lessons = await query
            .OrderBy(l => l.StartsAt)
            .Select(l => new LessonDto(l.Id, l.GroupId, l.TeacherId, l.RoomName, l.StartsAt, l.EndsAt, l.Topic, null, l.Status, l.CancelReason, l.MaxLessonScore, l.RoomId))
            .ToListAsync();

        var teacherIds = lessons.Select(l => l.TeacherId).Distinct().ToList();
        var teacherNames = await db.Users.Where(u => teacherIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName);

        return Ok(lessons.Select(l => l with { TeacherName = teacherNames.GetValueOrDefault(l.TeacherId) }).ToList());
    }

    [HttpGet("conflicts")]
    [Authorize(Policy = "schedule.edit")]
    public async Task<ActionResult<List<ScheduleConflict>>> PreviewConflicts([FromQuery] LessonCandidate candidate)
    {
        var scope = HttpContext.RequireResolvedScope();
        var (error, resolved, _) = await ValidateCandidateAsync(scope, candidate);
        if (error is not null) return error;

        return Ok(await ScheduleConflicts.FindAsync(db, resolved!));
    }

    [HttpPost("lessons")]
    [Authorize(Policy = "schedule.edit")]
    public async Task<ActionResult<LessonDto>> CreateLesson(LessonCandidate candidate)
    {
        var scope = HttpContext.RequireResolvedScope();
        var (error, resolved, warnings) = await ValidateCandidateAsync(scope, candidate);
        if (error is not null) return error;
        candidate = resolved!;

        var conflicts = await ScheduleConflicts.FindAsync(db, candidate);
        if (conflicts.Count > 0)
            return Conflict(new { conflicts });

        var lesson = new Lesson(
            scope.OrganizationId, candidate.BranchId, candidate.GroupId, candidate.TeacherId,
            candidate.RoomName!, candidate.StartsAt, candidate.EndsAt, candidate.Topic, candidate.RoomId);

        db.Lessons.Add(lesson);

        var context = await (
            from g in db.Groups
            join b in db.Branches on g.BranchId equals b.Id
            where g.Id == candidate.GroupId
            select new { g.Name, b.Timezone }
        ).FirstAsync();
        var startsLocal = BranchClock.LocalDateTime(context.Timezone, lesson.StartsAt);
        var audience = await recipients.GroupStudentsAsync(candidate.GroupId);
        if (candidate.TeacherId != scope.UserId) audience.Add(candidate.TeacherId);
        await notifier.NotifyAsync(scope.OrganizationId, audience,
            new NotificationMessage(NotificationType.ScheduleChanged, $"Jadvalga yangi dars: {context.Name}",
                $"{startsLocal:dd.MM.yyyy HH:mm}, xona {lesson.RoomName}" + (lesson.Topic is null ? "." : $" — {lesson.Topic}."),
                $"/schedule/{lesson.Id}"));
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(List), ToDto(lesson, warnings));
    }

    /// <summary>Who could take this lesson instead: staff already teaching in the same branch who are free for
    /// its window. Anyone already booked is excluded rather than offered and then refused.</summary>
    [HttpGet("lessons/{id:guid}/substitutes")]
    [Authorize(Policy = "teacher.replace")]
    public async Task<ActionResult<List<SubstituteDto>>> Substitutes(Guid id, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        var lesson = await db.Lessons.FirstOrDefaultAsync(l => l.Id == id && l.OrganizationId == scope.OrganizationId, ct);
        if (lesson is null) return NotFound();
        if (!await scope.CanAccessGroupAsync(db, lesson.GroupId, lesson.BranchId)) return Forbid();

        var busy = await db.Lessons.Held()
            .Where(l => l.StartsAt < lesson.EndsAt && lesson.StartsAt < l.EndsAt)
            .Select(l => l.TeacherId).Distinct().ToListAsync(ct);

        var candidates = await (
            from gs in db.GroupStaff
            join g in db.Groups on gs.GroupId equals g.Id
            join u in db.Users on gs.UserId equals u.Id
            where g.BranchId == lesson.BranchId && u.Status == UserStatus.Active && !busy.Contains(u.Id)
            orderby u.FullName
            select new SubstituteDto(u.Id, u.FullName, u.Email, gs.Role == GroupStaffRole.MainTeacher))
            .Distinct().ToListAsync(ct);

        return Ok(candidates);
    }

    /// <summary>docs/00_TZ.md §19. The teacher cannot come; somebody else takes the lesson and everyone who was
    /// going to sit in it is told. The substitute's own calendar is checked first — a replacement that
    /// double-books the stand-in has only moved the problem.</summary>
    [HttpPost("lessons/{id:guid}/replace-teacher")]
    [Authorize(Policy = "teacher.replace")]
    public async Task<ActionResult<LessonDto>> ReplaceTeacher(Guid id, ReplaceTeacherRequest request, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest(new { error = "Almashtirish sababini yozing." });

        var lesson = await db.Lessons.FirstOrDefaultAsync(l => l.Id == id && l.OrganizationId == scope.OrganizationId, ct);
        if (lesson is null) return NotFound();
        if (!await scope.CanAccessGroupAsync(db, lesson.GroupId, lesson.BranchId)) return Forbid();
        if (lesson.IsCancelled)
            return Conflict(new { error = "Bekor qilingan darsni o'zgartirib bo'lmaydi." });
        if (lesson.EndsAt < DateTimeOffset.UtcNow)
            return BadRequest(new { error = "O'tib ketgan darsni o'zgartirib bo'lmaydi." });
        if (lesson.TeacherId == request.TeacherId)
            return BadRequest(new { error = "Bu o'qituvchi allaqachon shu darsga biriktirilgan." });

        var substitute = await db.Users.FirstOrDefaultAsync(
            u => u.Id == request.TeacherId && u.OrganizationId == scope.OrganizationId, ct);
        if (substitute is null) return NotFound(new { error = "O'qituvchi topilmadi." });
        if (substitute.Status != UserStatus.Active)
            return BadRequest(new { error = "Bloklangan foydalanuvchini darsga biriktirib bo'lmaydi." });

        var clash = await db.Lessons.Held()
            .Where(l => l.TeacherId == request.TeacherId && l.Id != lesson.Id
                && l.StartsAt < lesson.EndsAt && lesson.StartsAt < l.EndsAt)
            .Select(l => new ScheduleConflict("TEACHER", l.Id, l.StartsAt, l.EndsAt))
            .ToListAsync(ct);
        if (clash.Count > 0) return Conflict(new { conflicts = clash });

        var previousId = lesson.TeacherId;
        var previousName = await db.Users.Where(u => u.Id == previousId).Select(u => u.FullName).FirstOrDefaultAsync(ct);
        lesson.ReplaceTeacher(request.TeacherId);

        var context = await (
            from g in db.Groups
            join b in db.Branches on g.BranchId equals b.Id
            where g.Id == lesson.GroupId
            select new { g.Name, b.Timezone }).FirstAsync(ct);
        var startsLocal = BranchClock.LocalDateTime(context.Timezone, lesson.StartsAt);

        // On the students' own timelines too: "who actually taught that day" is a question that gets asked
        // months later, and the schedule row by then only shows the substitute.
        var groupStudents = await db.Enrollments
            .Where(e => e.GroupId == lesson.GroupId && e.Status == EnrollmentStatus.Active)
            .Select(e => e.StudentId).ToListAsync(ct);
        foreach (var studentId in groupStudents)
            db.StudentTimelineEvents.Add(new StudentTimelineEvent(scope.OrganizationId, studentId,
                TimelineEventType.TeacherReplaced,
                JsonSerializer.Serialize(new
                {
                    lessonId = lesson.Id, group = context.Name, from = previousName, to = substitute.FullName,
                    reason = request.Reason, startsAt = lesson.StartsAt,
                })));

        var audience = await recipients.GroupStudentsAsync(lesson.GroupId);
        audience.Add(request.TeacherId);
        audience.Add(previousId);
        await notifier.NotifyAsync(scope.OrganizationId, audience,
            new NotificationMessage(NotificationType.TeacherReplaced, $"O'qituvchi almashdi: {context.Name}",
                $"{startsLocal:dd.MM.yyyy HH:mm} darsini {substitute.FullName} olib boradi. Sabab: {request.Reason}",
                $"/schedule/{lesson.Id}"), ct);

        await db.SaveChangesAsync(ct);
        return Ok(ToDto(lesson));
    }

    /// <summary>Rooms already in use in a branch. There is no Room table yet — a lesson names its room as text —
    /// so the schedule form offers what the branch actually uses instead of an empty box where "A-12", "A12"
    /// and "a-12" become three rooms and the conflict detector quietly stops catching clashes between them.</summary>
    [HttpGet("rooms")]
    [Authorize(Policy = "schedule.view")]
    public async Task<ActionResult<List<string>>> Rooms([FromQuery] Guid branchId, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        var branches = await db.Branches.Where(b => b.OrganizationId == scope.OrganizationId)
            .Select(b => b.Id).ToListAsync(ct);
        var allowed = scope.AllowedBranchIds(branches);
        if (allowed is null || !allowed.Contains(branchId)) return Forbid();

        var rooms = await db.Lessons
            .Where(l => l.BranchId == branchId)
            .Select(l => l.RoomName)
            .Distinct()
            .OrderBy(r => r)
            .ToListAsync(ct);

        return Ok(rooms);
    }

    /// <summary>The candidate must be inside the caller's scope, its group must exist in this org and
    /// live in the stated branch, its teacher must be a real user of this org, its room (when given by id) must be a room of that branch
    /// that is in use, and the window must be well-formed — none of which the raw request guarantees. Returns the candidate with the room
    /// resolved (its name is the room's), plus warnings that are not refusals. <paramref name="keepRoomId"/> is the room a lesson being
    /// edited is already in: staying in a room that has since been switched off is allowed, choosing one is not.</summary>
    private async Task<(ActionResult? Error, LessonCandidate? Resolved, List<string> Warnings)> ValidateCandidateAsync(
        ResolvedScope scope, LessonCandidate candidate, Guid? keepRoomId = null)
    {
        var warnings = new List<string>();

        if (candidate.EndsAt <= candidate.StartsAt)
            return (BadRequest(new { error = "Dars tugash vaqti boshlanish vaqtidan keyin bo'lishi kerak." }), null, warnings);
        if (candidate.RoomId is null && string.IsNullOrWhiteSpace(candidate.RoomName))
            return (BadRequest(new { error = "Xona ko'rsatilishi shart." }), null, warnings);
        if (candidate.RoomId is null && candidate.RoomName!.Trim().Length > MaxRoomNameLength)
            return (BadRequest(new { error = $"Xona nomi {MaxRoomNameLength} belgidan oshmasin." }), null, warnings);

        var group = await db.Groups.FirstOrDefaultAsync(g => g.Id == candidate.GroupId && g.OrganizationId == scope.OrganizationId);
        if (group is null) return (NotFound(new { error = "Guruh topilmadi." }), null, warnings);
        if (group.BranchId != candidate.BranchId)
            return (BadRequest(new { error = "Guruh ko'rsatilgan filialga tegishli emas." }), null, warnings);

        if (!await scope.CanAccessGroupAsync(db, group.Id, group.BranchId))
            return (Forbid(), null, warnings);

        var teacherExists = await db.Users.AnyAsync(u => u.Id == candidate.TeacherId && u.OrganizationId == scope.OrganizationId);
        if (!teacherExists) return (NotFound(new { error = "O'qituvchi topilmadi." }), null, warnings);

        var roomName = candidate.RoomName?.Trim();
        if (candidate.RoomId is { } roomId)
        {
            var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId && r.OrganizationId == scope.OrganizationId);
            if (room is null) return (NotFound(new { error = "Xona topilmadi." }), null, warnings);
            if (room.BranchId != candidate.BranchId)
                return (BadRequest(new { error = "Xona ko'rsatilgan filialga tegishli emas." }), null, warnings);
            if (!room.IsActive && roomId != keepRoomId)
                return (BadRequest(new { error = $"\"{room.Name}\" xonasi faol emas — darsni boshqa xonaga qo'ying yoki xonani qayta yoqing." }), null, warnings);

            roomName = room.Name;
            if (room.Capacity is { } seats && seats < group.Capacity)
                warnings.Add($"\"{room.Name}\" xonasi {seats} kishilik, guruh sig'imi esa {group.Capacity}.");
        }

        return (null, candidate with { RoomName = roomName }, warnings);
    }

    /// <summary>Moves a lesson, changes its room or teacher, or retitles it. Only what is sent changes; the lesson does not clash
    /// with itself. A lesson that is over, or cancelled, is not edited — a cancelled one is scheduled again as a new lesson.</summary>
    [HttpPatch("lessons/{id:guid}")]
    [Authorize(Policy = "schedule.edit")]
    public async Task<ActionResult<LessonDto>> UpdateLesson(Guid id, UpdateLessonRequest request, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        var lesson = await db.Lessons.FirstOrDefaultAsync(l => l.Id == id && l.OrganizationId == scope.OrganizationId, ct);
        if (lesson is null) return NotFound();
        if (!await scope.CanAccessGroupAsync(db, lesson.GroupId, lesson.BranchId)) return Forbid();
        if (lesson.IsCancelled)
            return Conflict(new { error = "Bekor qilingan darsni o'zgartirib bo'lmaydi." });
        var now = DateTimeOffset.UtcNow;
        if (lesson.EndsAt < now)
            return BadRequest(new { error = "O'tib ketgan darsni o'zgartirib bo'lmaydi." });
        if (request.StartsAt is null && request.EndsAt is null && request.RoomName is null && request.RoomId is null && request.TeacherId is null && request.Topic is null)
            return BadRequest(new { error = "O'zgartirish uchun vaqt, xona, o'qituvchi yoki mavzuni ko'rsating." });

        var startsAt = request.StartsAt ?? lesson.StartsAt;
        var endsAt = request.EndsAt ?? lesson.EndsAt;
        // A room by id wins; a name alone makes the lesson a name-only one; neither keeps the lesson where it is.
        var roomId = request.RoomId ?? (request.RoomName is null || request.RoomName.Trim() == lesson.RoomName ? lesson.RoomId : null);
        var room = request.RoomName?.Trim() ?? lesson.RoomName;
        var teacherId = request.TeacherId ?? lesson.TeacherId;
        var topic = request.Topic is null ? lesson.Topic : request.Topic.Trim().Length == 0 ? null : request.Topic.Trim();
        if (room.Length > MaxRoomNameLength) return BadRequest(new { error = $"Xona nomi {MaxRoomNameLength} belgidan oshmasin." });
        if (topic?.Length > MaxTopicLength) return BadRequest(new { error = $"Mavzu {MaxTopicLength} belgidan oshmasin." });

        var (error, resolved, warnings) = await ValidateCandidateAsync(
            scope, new LessonCandidate(lesson.BranchId, lesson.GroupId, teacherId, room, startsAt, endsAt, topic, roomId), lesson.RoomId);
        if (error is not null) return error;
        var candidate = resolved!;
        room = candidate.RoomName!;

        var moved = startsAt != lesson.StartsAt || endsAt != lesson.EndsAt || room != lesson.RoomName || roomId != lesson.RoomId;
        var teacherChanged = teacherId != lesson.TeacherId;
        if (moved && endsAt < now)
            return BadRequest(new { error = "Darsni o'tib ketgan vaqtga ko'chirib bo'lmaydi." });
        if (teacherChanged && !await db.Users.AnyAsync(u => u.Id == teacherId && u.Status == UserStatus.Active, ct))
            return BadRequest(new { error = "Bloklangan foydalanuvchini darsga biriktirib bo'lmaydi." });

        if (moved || teacherChanged)
        {
            var conflicts = await ScheduleConflicts.FindAsync(db, candidate, lesson.Id, ct);
            if (conflicts.Count > 0) return Conflict(new { conflicts });
        }

        var (oldStartsAt, oldRoom, oldTeacherId) = (lesson.StartsAt, lesson.RoomName, lesson.TeacherId);
        if (moved) lesson.Reschedule(startsAt, endsAt, room, roomId);
        if (teacherChanged) lesson.ReplaceTeacher(teacherId);
        if (request.Topic is not null) lesson.SetTopic(topic);

        if (moved || teacherChanged)
        {
            var context = await GroupContextAsync(lesson.GroupId, ct);
            var changes = new List<string>();
            if (startsAt != oldStartsAt)
                changes.Add($"vaqt {BranchClock.LocalDateTime(context.Timezone, oldStartsAt):dd.MM.yyyy HH:mm} → {BranchClock.LocalDateTime(context.Timezone, startsAt):dd.MM.yyyy HH:mm}");
            if (room != oldRoom) changes.Add($"xona {oldRoom} → {room}");
            if (teacherChanged)
                changes.Add($"o'qituvchi {await recipients.UserNameAsync(oldTeacherId)} → {await recipients.UserNameAsync(teacherId)}");
            if (changes.Count == 0) changes.Add("dars vaqti o'zgardi");

            var audience = await recipients.GroupStudentsAndParentsAsync(lesson.GroupId);
            audience.AddRange([teacherId, oldTeacherId]);
            await notifier.NotifyAsync(scope.OrganizationId, audience.Distinct().ToList(),
                new NotificationMessage(NotificationType.ScheduleChanged, $"Dars o'zgardi: {context.Name}",
                    string.Join("; ", changes) + ".", $"/schedule/{lesson.Id}"), ct);
        }

        await db.SaveChangesAsync(ct);
        return Ok(ToDto(lesson, warnings));
    }

    /// <summary>The lesson does not take place. It stays on the schedule, marked, with its attendance, homework and resources;
    /// it just stops counting as held, as busy or as something to remind about. Everyone the lesson concerned is told.</summary>
    [HttpPost("lessons/{id:guid}/cancel")]
    [Authorize(Policy = "schedule.edit")]
    public async Task<ActionResult<LessonDto>> CancelLesson(Guid id, CancelLessonRequest request, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason)) return BadRequest(new { error = "Bekor qilish sababini yozing." });
        if (reason.Length > MaxReasonLength) return BadRequest(new { error = $"Sabab {MaxReasonLength} belgidan oshmasin." });

        var lesson = await db.Lessons.FirstOrDefaultAsync(l => l.Id == id && l.OrganizationId == scope.OrganizationId, ct);
        if (lesson is null) return NotFound();
        if (!await scope.CanAccessGroupAsync(db, lesson.GroupId, lesson.BranchId)) return Forbid();
        if (lesson.IsCancelled) return Conflict(new { error = "Dars allaqachon bekor qilingan." });

        lesson.Cancel(reason);
        await NotifyCancelledAsync(scope.OrganizationId, [lesson], reason, ct);
        await db.SaveChangesAsync(ct);
        return Ok(ToDto(lesson));
    }

    /// <summary>A holiday, a closed branch: many lessons cancelled in one go. All or nothing — if any lesson is outside the
    /// caller's scope nothing is cancelled. Lessons already cancelled are skipped, not an error. Each group is told once.</summary>
    [HttpPost("lessons/bulk-cancel")]
    [Authorize(Policy = "schedule.edit")]
    public async Task<IActionResult> BulkCancel(BulkCancelLessonsRequest request, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason)) return BadRequest(new { error = "Bekor qilish sababini yozing." });
        if (reason.Length > MaxReasonLength) return BadRequest(new { error = $"Sabab {MaxReasonLength} belgidan oshmasin." });
        var ids = request.LessonIds?.Distinct().ToList() ?? [];
        if (ids.Count == 0) return BadRequest(new { error = "Bekor qilinadigan darslarni tanlang." });
        if (ids.Count > MaxBulkLessons) return BadRequest(new { error = $"Bir vaqtda {MaxBulkLessons} tagacha dars bekor qilinadi." });

        var lessons = await db.Lessons.Where(l => ids.Contains(l.Id) && l.OrganizationId == scope.OrganizationId).ToListAsync(ct);
        if (lessons.Count != ids.Count) return NotFound(new { error = "Ba'zi darslar topilmadi." });

        foreach (var pair in lessons.Select(l => (l.GroupId, l.BranchId)).Distinct())
            if (!await scope.CanAccessGroupAsync(db, pair.GroupId, pair.BranchId)) return Forbid();

        var open = lessons.Where(l => !l.IsCancelled).ToList();
        foreach (var lesson in open) lesson.Cancel(reason);
        foreach (var group in open.GroupBy(l => l.GroupId))
            await NotifyCancelledAsync(scope.OrganizationId, group.ToList(), reason, ct);

        await db.SaveChangesAsync(ct);
        return Ok(new { cancelled = open.Count, alreadyCancelled = lessons.Count - open.Count });
    }

    /// <summary>Erases a lesson nothing was ever recorded against. Attendance, homework and resources are history — a lesson that
    /// has any is cancelled, not deleted, and the refusal says so.</summary>
    [HttpDelete("lessons/{id:guid}")]
    [Authorize(Policy = "schedule.edit")]
    public async Task<IActionResult> DeleteLesson(Guid id, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        var lesson = await db.Lessons.FirstOrDefaultAsync(l => l.Id == id && l.OrganizationId == scope.OrganizationId, ct);
        if (lesson is null) return NotFound();
        if (!await scope.CanAccessGroupAsync(db, lesson.GroupId, lesson.BranchId)) return Forbid();

        var blockers = new List<string>();
        if (await db.Attendance.AnyAsync(a => a.LessonId == id, ct)) blockers.Add("davomat yozuvlari");
        if (await db.Homework.AnyAsync(h => h.LessonId == id, ct)) blockers.Add("uy vazifalari");
        if (await db.LearningResources.AnyAsync(r => r.LessonId == id, ct)) blockers.Add("dars materiallari");
        if (blockers.Count > 0)
            return Conflict(new
            {
                error = $"Bu darsni o'chirib bo'lmaydi — unga bog'liq ma'lumotlar bor: {string.Join(", ", blockers)}. "
                    + (lesson.IsCancelled ? "Dars allaqachon bekor qilingan." : "Buning o'rniga darsni bekor qiling."),
                blockers,
                canCancel = !lesson.IsCancelled,
            });

        db.Lessons.Remove(lesson);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>One message per group for lessons that were just cancelled together: students, their parents and the lessons'
    /// teachers. Adds to the context only — the caller's SaveChanges commits it with the cancellation.</summary>
    private async Task NotifyCancelledAsync(Guid organizationId, List<Lesson> lessons, string reason, CancellationToken ct)
    {
        var ordered = lessons.OrderBy(l => l.StartsAt).ToList();
        var context = await GroupContextAsync(ordered[0].GroupId, ct);
        string Local(DateTimeOffset at, string format) => BranchClock.LocalDateTime(context.Timezone, at).ToString(format);

        var what = ordered.Count == 1
            ? $"{Local(ordered[0].StartsAt, "dd.MM.yyyy HH:mm")} darsi"
            : $"{ordered.Count} ta dars ({Local(ordered[0].StartsAt, "dd.MM")} – {Local(ordered[^1].StartsAt, "dd.MM.yyyy")})";

        var audience = await recipients.GroupStudentsAndParentsAsync(ordered[0].GroupId);
        audience.AddRange(ordered.Select(l => l.TeacherId));
        await notifier.NotifyAsync(organizationId, audience.Distinct().ToList(),
            new NotificationMessage(NotificationType.ScheduleChanged, $"Dars bekor qilindi: {context.Name}",
                $"{what} bekor qilindi. Sabab: {reason}", ordered.Count == 1 ? $"/schedule/{ordered[0].Id}" : "/schedule"), ct);
    }

    private async Task<(string Name, string Timezone)> GroupContextAsync(Guid groupId, CancellationToken ct)
    {
        var context = await (
            from g in db.Groups
            join b in db.Branches on g.BranchId equals b.Id
            where g.Id == groupId
            select new { g.Name, b.Timezone }).FirstAsync(ct);
        return (context.Name, context.Timezone);
    }

    private static LessonDto ToDto(Lesson lesson, List<string>? warnings = null) =>
        new(lesson.Id, lesson.GroupId, lesson.TeacherId, lesson.RoomName, lesson.StartsAt, lesson.EndsAt, lesson.Topic,
            null, lesson.Status, lesson.CancelReason, lesson.MaxLessonScore, lesson.RoomId, warnings is { Count: > 0 } ? warnings : null);
}
