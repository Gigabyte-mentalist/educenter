using System.Globalization;
using EduCenter.Api.Authorization;
using EduCenter.Api.Automation;
using EduCenter.Api.Notifications;
using EduCenter.Infrastructure;
using EduCenter.Modules.Academic.Domain;
using EduCenter.Modules.Identity.Domain;
using EduCenter.Modules.Notifications.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Academic;

/// <param name="StartTime">The branch's wall-clock time, "HH:mm".</param>
/// <param name="TeacherId">Null: the group's main teacher takes it.</param>
public record ScheduleSlotDto(DayOfWeek DayOfWeek, string StartTime, int DurationMinutes, Guid? RoomId, string? RoomName, Guid? TeacherId, string? TeacherName);
public record ScheduleTemplateDto(Guid GroupId, string Timezone, List<ScheduleSlotDto> Slots);
public record ScheduleSlotInput(DayOfWeek DayOfWeek, string? StartTime, int DurationMinutes, Guid? RoomId, Guid? TeacherId);
public record SaveScheduleTemplateRequest(List<ScheduleSlotInput>? Slots);

/// <param name="SkipDates">Days no lesson is made on — a holiday.</param>
public record GenerateLessonsRequest(DateOnly From, DateOnly To, List<DateOnly>? SkipDates);
public record GeneratedLessonDto(DateOnly Date, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string RoomName, Guid TeacherId, string? TeacherName);
public record GenerationConflictDto(DateOnly Date, string Type, Guid ConflictingLessonId, DateTimeOffset StartsAt, DateTimeOffset EndsAt);

/// <param name="Created">Lessons made; 0 on a preview, which only lists what <paramref name="Lessons"/> would be.</param>
/// <param name="Skipped">Lessons of the period the group already has at the same time (a cancelled one counts) — not made again.</param>
public record GenerateLessonsResult(bool Preview, int Created, int Skipped, List<GeneratedLessonDto> Lessons, List<GenerationConflictDto> Conflicts, List<string> Warnings);

/// <summary>
/// A group's weekly template and the lessons made from it (docs/21.09-audit-jurnali S07.04, TZ §18): "Mon/Wed/Fri 18:00, 90 minutes"
/// written once, then a period of lessons created from it in one go — every one of them checked for clashes first, and either all are
/// made or none.
/// </summary>
[ApiController]
[Route("api/groups/{id:guid}")]
public class GroupScheduleController(AppDbContext db, NotificationDispatcher notifier, NotificationRecipients recipients) : ControllerBase
{
    /// <summary>The longest stretch of lessons one generation makes.</summary>
    private const int MaxMonths = 6;

    private static readonly string[] DayNames = ["yakshanba", "dushanba", "seshanba", "chorshanba", "payshanba", "juma", "shanba"];

    [HttpGet("schedule-template")]
    [Authorize(Policy = "groups.view")]
    public async Task<ActionResult<ScheduleTemplateDto>> GetTemplate(Guid id, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        var group = await db.Groups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id && g.OrganizationId == scope.OrganizationId, ct);
        if (group is null) return NotFound();
        if (!await scope.CanAccessGroupAsync(db, group.Id, group.BranchId)) return Forbid();

        return Ok(await BuildTemplateAsync(group, ct));
    }

    /// <summary>Replaces the whole template. The slots of one day must not overlap each other, a slot must end the same day, and a room or
    /// teacher named in one must be of this branch / organization and in use.</summary>
    [HttpPut("schedule-template")]
    [Authorize(Policy = "groups.edit")]
    public async Task<ActionResult<ScheduleTemplateDto>> SaveTemplate(Guid id, SaveScheduleTemplateRequest request, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        var group = await db.Groups.FirstOrDefaultAsync(g => g.Id == id && g.OrganizationId == scope.OrganizationId, ct);
        if (group is null) return NotFound();
        if (!await scope.CanAccessGroupAsync(db, group.Id, group.BranchId)) return Forbid();
        if (group.IsClosed) return Conflict(new { error = "Yopilgan guruhning jadvalini o'zgartirib bo'lmaydi." });

        var inputs = request.Slots ?? [];
        if (inputs.Count > GroupScheduleSlot.MaxSlotsPerGroup)
            return BadRequest(new { error = $"Haftalik jadvalda {GroupScheduleSlot.MaxSlotsPerGroup} tadan ortiq dars bo'lmaydi." });

        var parsed = new List<(ScheduleSlotInput Input, TimeOnly Start)>();
        foreach (var input in inputs)
        {
            if (!TimeOnly.TryParseExact(input.StartTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
                return BadRequest(new { error = "Dars boshlanishi SS:DD ko'rinishida bo'lishi kerak (masalan 18:00)." });
            if (input.DurationMinutes is < GroupScheduleSlot.MinDurationMinutes or > GroupScheduleSlot.MaxDurationMinutes)
                return BadRequest(new { error = $"Dars davomiyligi {GroupScheduleSlot.MinDurationMinutes} dan {GroupScheduleSlot.MaxDurationMinutes} daqiqagacha bo'lishi kerak." });
            if (start.ToTimeSpan() + TimeSpan.FromMinutes(input.DurationMinutes) > TimeSpan.FromDays(1))
                return BadRequest(new { error = $"{DayName(input.DayOfWeek)} kuni {input.StartTime} dagi dars yarim tundan keyin tugaydi — bir kunda tugashi kerak." });
            parsed.Add((input, start));
        }

        foreach (var day in parsed.GroupBy(p => p.Input.DayOfWeek))
        {
            var ordered = day.OrderBy(p => p.Start).ToList();
            for (var i = 1; i < ordered.Count; i++)
                if (ordered[i].Start < ordered[i - 1].Start.AddMinutes(ordered[i - 1].Input.DurationMinutes))
                    return BadRequest(new { error = $"{DayName(day.Key)} kuni darslar bir-biriga to'g'ri kelyapti ({ordered[i - 1].Start:HH:mm} va {ordered[i].Start:HH:mm})." });
        }

        var roomIds = parsed.Select(p => p.Input.RoomId).OfType<Guid>().Distinct().ToList();
        var rooms = await db.Rooms.Where(r => roomIds.Contains(r.Id) && r.OrganizationId == scope.OrganizationId).ToListAsync(ct);
        foreach (var roomId in roomIds)
        {
            var room = rooms.FirstOrDefault(r => r.Id == roomId);
            if (room is null) return NotFound(new { error = "Xona topilmadi." });
            if (room.BranchId != group.BranchId) return BadRequest(new { error = $"\"{room.Name}\" xonasi guruh filialiga tegishli emas." });
            if (!room.IsActive) return BadRequest(new { error = $"\"{room.Name}\" xonasi faol emas." });
        }

        var teacherIds = parsed.Select(p => p.Input.TeacherId).OfType<Guid>().Distinct().ToList();
        var teachers = await db.Users.Where(u => teacherIds.Contains(u.Id) && u.OrganizationId == scope.OrganizationId).ToListAsync(ct);
        foreach (var teacherId in teacherIds)
        {
            var teacher = teachers.FirstOrDefault(u => u.Id == teacherId);
            if (teacher is null) return NotFound(new { error = "O'qituvchi topilmadi." });
            if (teacher.Status != UserStatus.Active) return BadRequest(new { error = $"{teacher.FullName} bloklangan — uni jadvalga qo'yib bo'lmaydi." });
        }

        db.GroupScheduleSlots.RemoveRange(await db.GroupScheduleSlots.Where(s => s.GroupId == id).ToListAsync(ct));
        foreach (var (input, start) in parsed)
            db.GroupScheduleSlots.Add(new GroupScheduleSlot(scope.OrganizationId, id, input.DayOfWeek, start, input.DurationMinutes, input.RoomId, input.TeacherId));
        await db.SaveChangesAsync(ct);

        return Ok(await BuildTemplateAsync(group, ct));
    }

    /// <summary>Makes the lessons of a period from the template. Every lesson is checked for clashes first (a lesson the group already has at
    /// that time is skipped, not made twice); if any clashes, nothing is made and the clashes come back — as a 409, or in the body of a
    /// <c>?preview=true</c> answer, which makes nothing either way. One transaction, one message to the group.</summary>
    [HttpPost("generate-lessons")]
    [Authorize(Policy = "schedule.edit")]
    public async Task<ActionResult<GenerateLessonsResult>> Generate(Guid id, GenerateLessonsRequest request, [FromQuery] bool preview, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        var group = await db.Groups.FirstOrDefaultAsync(g => g.Id == id && g.OrganizationId == scope.OrganizationId, ct);
        if (group is null) return NotFound();
        if (!await scope.CanAccessGroupAsync(db, group.Id, group.BranchId)) return Forbid();
        if (group.IsClosed) return Conflict(new { error = "Yopilgan guruhga dars qo'shib bo'lmaydi." });

        var timezone = await db.Branches.Where(b => b.Id == group.BranchId).Select(b => b.Timezone).FirstAsync(ct);
        if (request.To < request.From) return BadRequest(new { error = "Davr oxiri boshlanishidan oldin bo'lmasligi kerak." });
        if (request.To > request.From.AddMonths(MaxMonths)) return BadRequest(new { error = $"Bir safar {MaxMonths} oydan uzun davrga dars yaratib bo'lmaydi." });
        if (request.From < BranchClock.Today(timezone, DateTimeOffset.UtcNow)) return BadRequest(new { error = "O'tib ketgan sanalarga dars yaratib bo'lmaydi." });

        var slots = await db.GroupScheduleSlots.Where(s => s.GroupId == id).OrderBy(s => s.StartTime).ToListAsync(ct);
        if (slots.Count == 0) return BadRequest(new { error = "Guruhda haftalik jadval yo'q — avval jadvalni saqlang." });

        var roomIds = slots.Select(s => s.RoomId).OfType<Guid>().Distinct().ToList();
        var rooms = await db.Rooms.Where(r => roomIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);
        foreach (var slot in slots)
        {
            if (slot.RoomId is not { } roomId || !rooms.TryGetValue(roomId, out var room))
                return BadRequest(new { error = $"{DayName(slot.DayOfWeek)} kuni {slot.StartTime:HH:mm} dagi darsga xona tanlanmagan — jadvalda xonani ko'rsating." });
            if (!room.IsActive) return BadRequest(new { error = $"\"{room.Name}\" xonasi faol emas — jadvalda boshqa xona tanlang." });
        }

        var mainTeacherId = await db.GroupStaff
            .Where(s => s.GroupId == id && s.Role == GroupStaffRole.MainTeacher).Select(s => (Guid?)s.UserId).FirstOrDefaultAsync(ct);
        if (mainTeacherId is null && slots.Any(s => s.TeacherId is null))
            return BadRequest(new { error = "Guruhda asosiy o'qituvchi yo'q — jadvalda har bir darsga o'qituvchi ko'rsating." });

        var skipDates = request.SkipDates?.ToHashSet() ?? [];
        var planned = new List<(DateOnly Date, DateTimeOffset Start, DateTimeOffset End, Room Room, Guid TeacherId)>();
        for (var date = request.From; date <= request.To; date = date.AddDays(1))
        {
            if (skipDates.Contains(date)) continue;
            foreach (var slot in slots.Where(s => s.DayOfWeek == date.DayOfWeek))
            {
                var start = BranchClock.LocalToInstant(timezone, date, slot.StartTime);
                planned.Add((date, start, start.AddMinutes(slot.DurationMinutes), rooms[slot.RoomId!.Value], slot.TeacherId ?? mainTeacherId!.Value));
            }
        }

        // A lesson the group already has at that time — held or cancelled — is what an earlier run (or a person) already made.
        var starts = planned.Select(p => p.Start).ToList();
        var existing = (await db.Lessons.Where(l => l.GroupId == id && starts.Contains(l.StartsAt)).Select(l => l.StartsAt).ToListAsync(ct)).ToHashSet();
        var fresh = planned.Where(p => !existing.Contains(p.Start)).ToList();
        var skipped = planned.Count - fresh.Count;

        var conflicts = new List<GenerationConflictDto>();
        foreach (var lesson in fresh)
        {
            var candidate = new LessonCandidate(group.BranchId, id, lesson.TeacherId, lesson.Room.Name, lesson.Start, lesson.End, null, lesson.Room.Id);
            foreach (var conflict in await ScheduleConflicts.FindAsync(db, candidate, ct: ct))
                conflicts.Add(new GenerationConflictDto(lesson.Date, conflict.Type, conflict.ConflictingLessonId, conflict.StartsAt, conflict.EndsAt));
        }

        var plannedTeachers = fresh.Select(f => f.TeacherId).Distinct().ToList();
        var teacherNames = await db.Users.Where(u => plannedTeachers.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var warnings = fresh.Select(f => f.Room).DistinctBy(r => r.Id)
            .Where(r => r.Capacity is { } seats && seats < group.Capacity)
            .Select(r => $"\"{r.Name}\" xonasi {r.Capacity} kishilik, guruh sig'imi esa {group.Capacity}.").ToList();
        var listed = fresh.Select(f => new GeneratedLessonDto(f.Date, f.Start, f.End, f.Room.Name, f.TeacherId, teacherNames.GetValueOrDefault(f.TeacherId))).ToList();

        if (conflicts.Count > 0 && !preview) return Conflict(new { error = "Ba'zi darslar band vaqtga to'g'ri keladi — hech qaysi dars yaratilmadi.", conflicts });
        if (preview || conflicts.Count > 0) return Ok(new GenerateLessonsResult(true, 0, skipped, listed, conflicts, warnings));

        foreach (var lesson in fresh)
            db.Lessons.Add(new Lesson(scope.OrganizationId, group.BranchId, id, lesson.TeacherId, lesson.Room.Name, lesson.Start, lesson.End, null, lesson.Room.Id));

        if (fresh.Count > 0)
        {
            // One message for the whole batch: a term of lessons must not arrive as sixty notifications.
            var audience = await recipients.GroupStudentsAndParentsAsync(id);
            audience.AddRange(fresh.Select(f => f.TeacherId));
            audience.Remove(scope.UserId);
            await notifier.NotifyAsync(scope.OrganizationId, audience.Distinct().ToList(),
                new NotificationMessage(NotificationType.ScheduleChanged, $"Jadvalga yangi darslar: {group.Name}",
                    $"{fresh.Count} ta dars qo'shildi ({fresh.Min(f => f.Date):dd.MM.yyyy} – {fresh.Max(f => f.Date):dd.MM.yyyy}).", "/schedule"), ct);
        }

        await db.SaveChangesAsync(ct);
        return Ok(new GenerateLessonsResult(false, fresh.Count, skipped, listed, [], warnings));
    }

    private async Task<ScheduleTemplateDto> BuildTemplateAsync(Group group, CancellationToken ct)
    {
        var timezone = await db.Branches.Where(b => b.Id == group.BranchId).Select(b => b.Timezone).FirstAsync(ct);
        var slots = await db.GroupScheduleSlots.AsNoTracking().Where(s => s.GroupId == group.Id).ToListAsync(ct);

        var usedRooms = slots.Select(s => s.RoomId).OfType<Guid>().Distinct().ToList();
        var usedTeachers = slots.Select(s => s.TeacherId).OfType<Guid>().Distinct().ToList();
        var roomNames = await db.Rooms.Where(r => usedRooms.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Name, ct);
        var teacherNames = await db.Users.Where(u => usedTeachers.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        // Monday first, then by time — the order of a week as it is read here.
        var ordered = slots.OrderBy(s => ((int)s.DayOfWeek + 6) % 7).ThenBy(s => s.StartTime)
            .Select(s => new ScheduleSlotDto(s.DayOfWeek, s.StartTime.ToString("HH:mm", CultureInfo.InvariantCulture), s.DurationMinutes,
                s.RoomId, s.RoomId is { } r ? roomNames.GetValueOrDefault(r) : null, s.TeacherId, s.TeacherId is { } t ? teacherNames.GetValueOrDefault(t) : null))
            .ToList();
        return new ScheduleTemplateDto(group.Id, timezone, ordered);
    }

    private static string DayName(DayOfWeek day) => DayNames[(int)day];
}
