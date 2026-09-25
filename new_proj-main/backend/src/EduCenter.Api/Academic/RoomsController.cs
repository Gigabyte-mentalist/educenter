using EduCenter.Api.Authorization;
using EduCenter.Api.Automation;
using EduCenter.Infrastructure;
using EduCenter.Modules.Academic.Domain;
using EduCenter.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Academic;

/// <param name="LessonCount">Lessons held or planned in it, cancelled ones included — a room with any cannot be erased, only switched off.</param>
public record RoomDto(Guid Id, Guid BranchId, string Name, int? Capacity, string? Equipment, bool IsActive, int LessonCount);
public record CreateRoomRequest(Guid BranchId, string Name, int? Capacity, string? Equipment);

/// <summary>Only what is sent changes. <paramref name="Capacity"/> 0 clears it; an empty <paramref name="Equipment"/> clears that.</summary>
public record UpdateRoomRequest(string? Name, int? Capacity, string? Equipment, bool? IsActive);

public record RoomBookingDto(Guid LessonId, DateTimeOffset StartsAt, DateTimeOffset EndsAt, Guid GroupId, string GroupName, Guid TeacherId, string? TeacherName, string? Topic);

/// <param name="Busy">The lessons that hold the room on that day of the branch's calendar, in time order; everything else is free.</param>
public record RoomAvailabilityDto(Guid RoomId, string Name, DateOnly Date, string Timezone, List<RoomBookingDto> Busy);

/// <summary>
/// The classrooms of a branch (docs/21.09-audit-jurnali S07.03, TZ §18 "Room Management"). Anyone who reads the schedule of the branch
/// may list them and see when one is taken; managing them takes <c>rooms.manage</c> — the Super Admin anywhere, an Administrator in their
/// own branches.
/// </summary>
[ApiController]
[Route("api/rooms")]
public class RoomsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "schedule.view")]
    public async Task<ActionResult<Page<RoomDto>>> List(
        [FromQuery] Guid? branchId, [FromQuery] bool includeInactive, [FromQuery] string? search, [FromQuery] PageRequest? paging, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        var allowed = await AllowedBranchesAsync(scope, ct);
        if (allowed is null || (branchId is { } wanted && !allowed.Contains(wanted))) return Forbid();

        var rooms = db.Rooms.Where(r => r.OrganizationId == scope.OrganizationId);
        rooms = branchId is { } one ? rooms.Where(r => r.BranchId == one) : rooms.Where(r => allowed.Contains(r.BranchId));
        if (!includeInactive) rooms = rooms.Where(r => r.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = PagingExtensions.ContainsPattern(search);
            rooms = rooms.Where(r => EF.Functions.ILike(r.Name, term) || (r.Equipment != null && EF.Functions.ILike(r.Equipment, term)));
        }

        return Ok(await rooms
            .OrderBy(r => r.Name).ThenBy(r => r.Id)
            .Select(r => new RoomDto(r.Id, r.BranchId, r.Name, r.Capacity, r.Equipment, r.IsActive, db.Lessons.Count(l => l.RoomId == r.Id)))
            .ToPageAsync(paging, ct));
    }

    [HttpPost]
    [Authorize(Policy = "rooms.manage")]
    public async Task<ActionResult<RoomDto>> Create(CreateRoomRequest request, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        var allowed = await AllowedBranchesAsync(scope, ct);
        if (allowed is null || !allowed.Contains(request.BranchId)) return Forbid();

        var name = request.Name?.Trim() ?? "";
        if (Validate(name, request.Capacity, request.Equipment) is { } problem) return BadRequest(new { error = problem });
        if (await NameTakenAsync(request.BranchId, name, except: null, ct))
            return Conflict(new { error = $"Bu filialda \"{name}\" nomli xona allaqachon bor." });

        var room = new Room(scope.OrganizationId, request.BranchId, name, request.Capacity, request.Equipment);
        db.Rooms.Add(room);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(List), ToDto(room, 0));
    }

    [HttpPatch("{id:guid}")]
    [Authorize(Policy = "rooms.manage")]
    public async Task<ActionResult<RoomDto>> Update(Guid id, UpdateRoomRequest request, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == id && r.OrganizationId == scope.OrganizationId, ct);
        if (room is null) return NotFound();
        if (!await CanManageAsync(scope, room, ct)) return Forbid();

        if (request.Name is null && request.Capacity is null && request.Equipment is null && request.IsActive is null)
            return BadRequest(new { error = "O'zgartirish uchun nom, sig'im, jihoz yoki holatni ko'rsating." });

        var name = request.Name is null ? room.Name : request.Name.Trim();
        var capacity = request.Capacity is null ? room.Capacity : request.Capacity == 0 ? null : request.Capacity;
        var equipment = request.Equipment is null ? room.Equipment : request.Equipment;
        if (Validate(name, capacity, equipment) is { } problem) return BadRequest(new { error = problem });
        if (name != room.Name && await NameTakenAsync(room.BranchId, name, except: room.Id, ct))
            return Conflict(new { error = $"Bu filialda \"{name}\" nomli xona allaqachon bor." });

        var renamed = name != room.Name;
        room.Update(name, capacity, equipment);
        if (request.IsActive is { } active) room.SetActive(active);
        await db.SaveChangesAsync(ct);

        // Lessons keep the room's name beside its id, and every screen that shows a lesson reads the name: keep the two the same.
        if (renamed) await db.Lessons.Where(l => l.RoomId == room.Id).ExecuteUpdateAsync(s => s.SetProperty(l => l.RoomName, name), ct);

        return Ok(ToDto(room, await db.Lessons.CountAsync(l => l.RoomId == room.Id, ct)));
    }

    /// <summary>Only a room nothing was ever held in. One with lessons — planned, past or cancelled — is history: switch it off.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "rooms.manage")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == id && r.OrganizationId == scope.OrganizationId, ct);
        if (room is null) return NotFound();
        if (!await CanManageAsync(scope, room, ct)) return Forbid();

        // A lesson that only names the room (no id) belongs to it just the same.
        if (await db.Lessons.AnyAsync(l => l.RoomId == id || (l.RoomId == null && l.BranchId == room.BranchId && l.RoomName == room.Name), ct))
            return Conflict(new { error = $"\"{room.Name}\" xonasida darslar bor — o'chirib bo'lmaydi. Uning o'rniga xonani nofaol qiling.", canDeactivate = room.IsActive });

        if (await db.GroupScheduleSlots.AnyAsync(s => s.RoomId == id, ct))
            return Conflict(new { error = $"\"{room.Name}\" xonasi guruhlarning haftalik jadvalida ishlatilmoqda — o'chirib bo'lmaydi. Uning o'rniga xonani nofaol qiling.", canDeactivate = room.IsActive });

        db.Rooms.Remove(room);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>When the room is taken on one day (the branch's calendar day): the lessons that hold it, with their group and teacher.</summary>
    [HttpGet("{id:guid}/availability")]
    [Authorize(Policy = "schedule.view")]
    public async Task<ActionResult<RoomAvailabilityDto>> Availability(Guid id, [FromQuery] DateOnly? date, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == id && r.OrganizationId == scope.OrganizationId, ct);
        if (room is null) return NotFound();
        var allowed = await AllowedBranchesAsync(scope, ct);
        if (allowed is null || !allowed.Contains(room.BranchId)) return Forbid();

        var timezone = await db.Branches.Where(b => b.Id == room.BranchId).Select(b => b.Timezone).FirstAsync(ct);
        var day = date ?? BranchClock.Today(timezone, DateTimeOffset.UtcNow);
        var dayStart = BranchClock.StartOfLocalDay(timezone, day);
        var dayEnd = BranchClock.StartOfLocalDay(timezone, day.AddDays(1));

        var busy = await (
            from l in db.Lessons.Held()
            join g in db.Groups on l.GroupId equals g.Id
            join u in db.Users on l.TeacherId equals u.Id into teachers
            from teacher in teachers.DefaultIfEmpty()
            where l.BranchId == room.BranchId && l.StartsAt < dayEnd && dayStart < l.EndsAt
                  && (l.RoomId == room.Id || (l.RoomId == null && l.RoomName == room.Name))
            orderby l.StartsAt, l.Id
            select new RoomBookingDto(l.Id, l.StartsAt, l.EndsAt, g.Id, g.Name, l.TeacherId, teacher.FullName, l.Topic)).ToListAsync(ct);

        return Ok(new RoomAvailabilityDto(room.Id, room.Name, day, timezone, busy));
    }

    private async Task<IReadOnlyCollection<Guid>?> AllowedBranchesAsync(ResolvedScope scope, CancellationToken ct)
    {
        var branches = await db.Branches.Where(b => b.OrganizationId == scope.OrganizationId).Select(b => b.Id).ToListAsync(ct);
        return scope.AllowedBranchIds(branches);
    }

    private async Task<bool> CanManageAsync(ResolvedScope scope, Room room, CancellationToken ct) =>
        await AllowedBranchesAsync(scope, ct) is { } allowed && allowed.Contains(room.BranchId);

    private Task<bool> NameTakenAsync(Guid branchId, string name, Guid? except, CancellationToken ct)
    {
        // "A-12" and "a-12" are one room: the whole point of a room list is to stop the same room being written two ways.
        var lower = name.ToLower();
        return db.Rooms.AnyAsync(r => r.BranchId == branchId && r.Id != except && r.Name.ToLower() == lower, ct);
    }

    private static string? Validate(string name, int? capacity, string? equipment)
    {
        if (name.Length == 0) return "Xona nomini yozing.";
        if (name.Length > Room.MaxNameLength) return $"Xona nomi {Room.MaxNameLength} belgidan oshmasin.";
        if (capacity is < 1 or > Room.MaxCapacity) return $"Sig'im 1 dan {Room.MaxCapacity} gacha bo'lishi kerak.";
        if (equipment?.Trim().Length > Room.MaxEquipmentLength) return $"Jihoz tavsifi {Room.MaxEquipmentLength} belgidan oshmasin.";
        return null;
    }

    private static RoomDto ToDto(Room room, int lessonCount) =>
        new(room.Id, room.BranchId, room.Name, room.Capacity, room.Equipment, room.IsActive, lessonCount);
}
