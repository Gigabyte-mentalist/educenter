using System.Net;
using System.Net.Http.Json;
using EduCenter.Infrastructure;
using EduCenter.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// Rooms (docs/21.09-audit-jurnali S07.03): a branch's classrooms with a seating capacity and equipment, a lesson put in one by id, the
/// clash check keyed on that id, and lessons that only ever named their room still colliding with it.
/// </summary>
[Collection("Api")]
public class RoomTests(EduCenterWebApplicationFactory factory)
{
    private readonly LmsWorld world = new(factory);

    [Fact]
    public async Task Administrator_ManagesTheRoomsOfTheirBranch_AndNoOneElse_Does()
    {
        var admin = await world.ClientAsync("admin@demo-center.test");
        var branchId = (await world.CreateAsync()).BranchId;
        var name = $"CRUD {Guid.NewGuid():N}"[..14];

        var created = await admin.PostAsJsonAsync("/api/rooms", new { branchId, name = $"  {name}  ", capacity = 12, equipment = "Proyektor" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var room = (await created.Content.ReadFromJsonAsync<RoomDto>())!;
        Assert.Equal(name, room.Name); // trimmed
        Assert.Equal(12, room.Capacity);
        Assert.True(room.IsActive);
        Assert.Equal(0, room.LessonCount);

        var listed = (await admin.GetFromJsonAsync<PagedResponse<RoomDto>>($"/api/rooms?branchId={branchId}&search={Uri.EscapeDataString(name)}"))!;
        Assert.Equal(room.Id, Assert.Single(listed.Items).Id);

        var changed = await admin.PatchAsJsonAsync($"/api/rooms/{room.Id}", new { name = name + "-B", capacity = 20, equipment = "Doska" });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var updated = (await changed.Content.ReadFromJsonAsync<RoomDto>())!;
        Assert.Equal((name + "-B", 20, "Doska"), (updated.Name, updated.Capacity, updated.Equipment));

        // 0 clears the capacity, an empty text clears the equipment; what is not sent stays.
        var cleared = (await (await admin.PatchAsJsonAsync($"/api/rooms/{room.Id}", new { capacity = 0, equipment = "" })).Content.ReadFromJsonAsync<RoomDto>())!;
        Assert.Null(cleared.Capacity);
        Assert.Null(cleared.Equipment);
        Assert.Equal(name + "-B", cleared.Name);

        // Others: a teacher, an operator, a student, a parent may not manage; the branch's own Administrator may not reach another branch.
        foreach (var email in new[] { "teacher@demo-center.test", "operator@demo-center.test", "student@demo-center.test", "parent@demo-center.test" })
        {
            var other = await world.ClientAsync(email);
            Assert.Equal(HttpStatusCode.Forbidden, (await other.PostAsJsonAsync("/api/rooms", new { branchId, name = "Nope" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await other.PatchAsJsonAsync($"/api/rooms/{room.Id}", new { name = "Nope" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await other.DeleteAsync($"/api/rooms/{room.Id}")).StatusCode);
        }
        var foreign = await OtherBranchIdAsync(branchId);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync("/api/rooms", new { branchId = foreign, name = "Foreign" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync($"/api/rooms?branchId={foreign}")).StatusCode);

        // ...whereas the Super Admin reaches both, and a room of the other branch is out of the Administrator's hands.
        var superAdmin = await world.ClientAsync("superadmin@demo-center.test");
        var abroad = (await (await superAdmin.PostAsJsonAsync("/api/rooms", new { branchId = foreign, name = $"Abroad {Guid.NewGuid():N}"[..14] })).Content.ReadFromJsonAsync<RoomDto>())!;
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PatchAsJsonAsync($"/api/rooms/{abroad.Id}", new { name = "Mine now" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.DeleteAsync($"/api/rooms/{abroad.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PatchAsJsonAsync($"/api/rooms/{Guid.NewGuid()}", new { name = "Ghost" })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/rooms/{room.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await superAdmin.DeleteAsync($"/api/rooms/{abroad.Id}")).StatusCode);
    }

    [Fact]
    public async Task ARoom_NeedsANameASaneCapacity_AndANameNoOtherRoomOfTheBranchHas()
    {
        var admin = await world.ClientAsync("admin@demo-center.test");
        var branchId = (await world.CreateAsync()).BranchId;
        var room = await CreateRoomAsync(admin, branchId, capacity: 10);

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/rooms", new { branchId, name = "   " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/rooms", new { branchId, name = new string('x', 51) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/rooms", new { branchId, name = "Zero", capacity = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/rooms", new { branchId, name = "Huge", capacity = 1001 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/rooms", new { branchId, name = "Notes", equipment = new string('e', 501) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchAsJsonAsync($"/api/rooms/{room.Id}", new { })).StatusCode);

        // "A-12" and "a-12" are one room.
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/rooms", new { branchId, name = room.Name.ToUpperInvariant() })).StatusCode);
        var second = await CreateRoomAsync(admin, branchId);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PatchAsJsonAsync($"/api/rooms/{second.Id}", new { name = room.Name.ToLowerInvariant() })).StatusCode);
        // Keeping its own name (in another case) is not a clash with itself.
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/rooms/{second.Id}", new { name = second.Name.ToUpperInvariant() })).StatusCode);
    }

    [Fact]
    public async Task TwoLessonsInTheSameRoomAtTheSameTime_ClashByTheRoom()
    {
        var (admin, first, second, room) = await TwoClassroomsAsync();
        var slot = Slot();

        Assert.Equal(HttpStatusCode.Created, (await ScheduleAsync(admin, first, room.Id, slot)).StatusCode);

        var clash = await ScheduleAsync(admin, second, room.Id, slot with { Start = slot.Start.AddMinutes(30), End = slot.End.AddMinutes(30) });
        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);
        Assert.Contains("ROOM", await clash.Content.ReadAsStringAsync());

        // The preview says the same, without creating anything.
        var preview = await admin.GetAsync($"/api/schedule/conflicts?branchId={second.BranchId}&groupId={second.GroupId}&teacherId={await TeacherIdAsync()}"
            + $"&roomId={room.Id}&startsAt={Uri.EscapeDataString(slot.Start.ToString("O"))}&endsAt={Uri.EscapeDataString(slot.End.ToString("O"))}");
        Assert.Contains("ROOM", await preview.Content.ReadAsStringAsync());

        // Another room, or the same room after the first lesson ends, is free.
        var other = await CreateRoomAsync(admin, first.BranchId);
        Assert.Equal(HttpStatusCode.Created, (await ScheduleAsync(admin, second, other.Id, slot with { Start = slot.Start.AddDays(1), End = slot.End.AddDays(1) })).StatusCode);
    }

    [Fact]
    public async Task ALesson_TakesTheRoomsNameAndId_AndARenamedRoomRenamesItsLessons()
    {
        var (admin, first, _, room) = await TwoClassroomsAsync();
        var slot = Slot();
        var lesson = (await (await ScheduleAsync(admin, first, room.Id, slot)).Content.ReadFromJsonAsync<LessonDto>())!;
        Assert.Equal(room.Id, lesson.RoomId);
        Assert.Equal(room.Name, lesson.RoomName); // even though the request sent no name

        var newName = room.Name + "-New";
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/rooms/{room.Id}", new { name = newName })).StatusCode);
        var shown = (await admin.GetFromJsonAsync<List<LessonDto>>($"/api/schedule?branchId={first.BranchId}&from={Uri.EscapeDataString(slot.Start.AddHours(-1).ToString("O"))}&to={Uri.EscapeDataString(slot.End.AddHours(1).ToString("O"))}"))!
            .Single(l => l.Id == lesson.Id);
        Assert.Equal(newName, shown.RoomName);
        Assert.Equal(room.Id, shown.RoomId);

        // A lesson can be moved to another room by id, and a name alone still works as it always did.
        var other = await CreateRoomAsync(admin, first.BranchId);
        var moved = (await (await admin.PatchAsJsonAsync($"/api/schedule/lessons/{lesson.Id}", new { roomId = other.Id })).Content.ReadFromJsonAsync<LessonDto>())!;
        Assert.Equal((other.Id, other.Name), (moved.RoomId, moved.RoomName));
        var named = (await (await admin.PatchAsJsonAsync($"/api/schedule/lessons/{lesson.Id}", new { roomName = "Somewhere else" })).Content.ReadFromJsonAsync<LessonDto>())!;
        Assert.Equal("Somewhere else", named.RoomName);
        Assert.Null(named.RoomId);
    }

    [Fact]
    public async Task ARoomThatIsSwitchedOff_TakesNoNewLesson_ButKeepsTheOnesItHas()
    {
        var (admin, first, second, room) = await TwoClassroomsAsync();
        var slot = Slot();
        var lesson = (await (await ScheduleAsync(admin, first, room.Id, slot)).Content.ReadFromJsonAsync<LessonDto>())!;

        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/rooms/{room.Id}", new { isActive = false })).StatusCode);

        var refused = await ScheduleAsync(admin, second, room.Id, slot with { Start = slot.Start.AddDays(2), End = slot.End.AddDays(2) });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("faol emas", await refused.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchAsJsonAsync($"/api/schedule/lessons/{(await LessonInAnotherRoomAsync(admin, second))}", new { roomId = room.Id })).StatusCode);

        // The lesson already in it can still be retitled; the room is not listed unless asked for.
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/schedule/lessons/{lesson.Id}", new { topic = "Still here" })).StatusCode);
        var active = (await admin.GetFromJsonAsync<PagedResponse<RoomDto>>($"/api/rooms?branchId={first.BranchId}&search={Uri.EscapeDataString(room.Name)}"))!;
        Assert.Empty(active.Items);
        var all = (await admin.GetFromJsonAsync<PagedResponse<RoomDto>>($"/api/rooms?branchId={first.BranchId}&includeInactive=true&search={Uri.EscapeDataString(room.Name)}"))!;
        Assert.False(Assert.Single(all.Items).IsActive);

        // Switched back on, it takes lessons again.
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/rooms/{room.Id}", new { isActive = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await ScheduleAsync(admin, second, room.Id, slot with { Start = slot.Start.AddDays(2), End = slot.End.AddDays(2) })).StatusCode);
    }

    [Fact]
    public async Task ARoomWithLessons_CannotBeErased_OnlySwitchedOff()
    {
        var (admin, first, _, room) = await TwoClassroomsAsync();
        var lesson = (await (await ScheduleAsync(admin, first, room.Id, Slot())).Content.ReadFromJsonAsync<LessonDto>())!;

        var refused = await admin.DeleteAsync($"/api/rooms/{room.Id}");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("nofaol", await refused.Content.ReadAsStringAsync());

        // Not even after the lesson is cancelled — it is still part of the room's history.
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/schedule/lessons/{lesson.Id}/cancel", new { reason = "Bayram" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/rooms/{room.Id}")).StatusCode);
        Assert.Equal(1, (await admin.GetFromJsonAsync<PagedResponse<RoomDto>>($"/api/rooms?branchId={first.BranchId}&search={Uri.EscapeDataString(room.Name)}"))!.Items.Single().LessonCount);

        // A cancelled lesson no longer books the room, though.
        var slot = Slot();
        var (_, _, second, free) = await TwoClassroomsAsync();
        Assert.Equal(HttpStatusCode.Created, (await ScheduleAsync(admin, first, free.Id, slot)).StatusCode);
        var cancelled = (await (await ScheduleAsync(admin, second, room.Id, slot with { Start = slot.Start.AddDays(3), End = slot.End.AddDays(3) })).Content.ReadFromJsonAsync<LessonDto>())!;
        await admin.PostAsJsonAsync($"/api/schedule/lessons/{cancelled.Id}/cancel", new { reason = "Bayram" });
        Assert.Equal(HttpStatusCode.Created, (await ScheduleAsync(admin, first, room.Id, slot with { Start = slot.Start.AddDays(3), End = slot.End.AddDays(3) })).StatusCode);
    }

    [Fact]
    public async Task ASmallRoom_IsAWarning_NotARefusal()
    {
        var admin = await world.ClientAsync("admin@demo-center.test");
        var classroom = await world.CreateAsync(); // its group seats 20
        var small = await CreateRoomAsync(admin, classroom.BranchId, capacity: 5);
        var big = await CreateRoomAsync(admin, classroom.BranchId, capacity: 40);
        var unknown = await CreateRoomAsync(admin, classroom.BranchId);
        var slot = Slot();

        var warned = await ScheduleAsync(admin, classroom, small.Id, slot);
        Assert.Equal(HttpStatusCode.Created, warned.StatusCode);
        var lesson = (await warned.Content.ReadFromJsonAsync<LessonDto>())!;
        var warning = Assert.Single(lesson.Warnings!);
        Assert.Contains("5", warning);
        Assert.Contains("20", warning);

        // Fits, or no capacity known: no warning. Moving the lesson into the small room warns as well.
        Assert.Null((await (await ScheduleAsync(admin, classroom, big.Id, slot with { Start = slot.Start.AddDays(1), End = slot.End.AddDays(1) })).Content.ReadFromJsonAsync<LessonDto>())!.Warnings);
        Assert.Null((await (await ScheduleAsync(admin, classroom, unknown.Id, slot with { Start = slot.Start.AddDays(2), End = slot.End.AddDays(2) })).Content.ReadFromJsonAsync<LessonDto>())!.Warnings);
        var moved = (await (await admin.PatchAsJsonAsync($"/api/schedule/lessons/{lesson.Id}", new { roomId = big.Id })).Content.ReadFromJsonAsync<LessonDto>())!;
        Assert.Null(moved.Warnings);
        var back = (await (await admin.PatchAsJsonAsync($"/api/schedule/lessons/{lesson.Id}", new { roomId = small.Id })).Content.ReadFromJsonAsync<LessonDto>())!;
        Assert.Single(back.Warnings!);
    }

    [Fact]
    public async Task Availability_ListsWhoHoldsTheRoom_OnTheBranchsCalendarDay()
    {
        var (admin, first, second, room) = await TwoClassroomsAsync();
        var day = Midnight(Random.Shared.Next(5000, 9000));
        // 08:00 and 14:00 in Tashkent (UTC+5) are 03:00 and 09:00 UTC.
        var morning = new Window(day.AddHours(3), day.AddHours(4).AddMinutes(30));
        var afternoon = new Window(day.AddHours(9), day.AddHours(10));
        var nextDay = new Window(day.AddDays(1).AddHours(3), day.AddDays(1).AddHours(4));
        await ScheduleAsync(admin, first, room.Id, morning);
        await ScheduleAsync(admin, second, room.Id, afternoon);
        await ScheduleAsync(admin, first, room.Id, nextDay);

        var date = DateOnly.FromDateTime(day.AddHours(5).DateTime).ToString("yyyy-MM-dd");
        var availability = (await admin.GetFromJsonAsync<AvailabilityDto>($"/api/rooms/{room.Id}/availability?date={date}"))!;
        Assert.Equal(room.Name, availability.Name);
        Assert.Equal(2, availability.Busy.Count);
        Assert.Equal(new[] { morning.Start, afternoon.Start }, availability.Busy.Select(b => b.StartsAt).ToArray()); // in time order
        Assert.Equal(new[] { first.GroupId, second.GroupId }, availability.Busy.Select(b => b.GroupId).ToArray());
        Assert.All(availability.Busy, b => Assert.False(string.IsNullOrEmpty(b.TeacherName)));

        Assert.Empty((await admin.GetFromJsonAsync<AvailabilityDto>($"/api/rooms/{room.Id}/availability?date=2001-01-01"))!.Busy);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/rooms/{Guid.NewGuid()}/availability")).StatusCode);

        // A cancelled lesson holds nothing.
        var morningId = availability.Busy[0].LessonId;
        await admin.PostAsJsonAsync($"/api/schedule/lessons/{morningId}/cancel", new { reason = "Bayram" });
        Assert.Single((await admin.GetFromJsonAsync<AvailabilityDto>($"/api/rooms/{room.Id}/availability?date={date}"))!.Busy);

        // Whoever may not read the branch's schedule cannot see its rooms' calendar.
        Assert.Equal(HttpStatusCode.Forbidden, (await (await world.ClientAsync("teacher@demo-center.test")).GetAsync($"/api/rooms/{room.Id}/availability?date={date}")).StatusCode);
    }

    [Fact]
    public async Task ALessonThatOnlyNamedItsRoom_StillClashesWithARoomOfThatName_AndBackfillGivesItARoom()
    {
        var (admin, first, second, room) = await TwoClassroomsAsync();
        var slot = Slot();

        // The way lessons were created before rooms existed: a name, no id.
        var legacy = await admin.PostAsJsonAsync("/api/schedule/lessons", new
        {
            branchId = first.BranchId, groupId = first.GroupId, teacherId = await TeacherIdAsync(), roomName = room.Name, startsAt = slot.Start, endsAt = slot.End,
        });
        Assert.Equal(HttpStatusCode.Created, legacy.StatusCode);
        var legacyLesson = (await legacy.Content.ReadFromJsonAsync<LessonDto>())!;
        Assert.Null(legacyLesson.RoomId);

        var clash = await ScheduleAsync(admin, second, room.Id, slot);
        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);
        Assert.Contains("ROOM", await clash.Content.ReadAsStringAsync());

        // Backfill (what the migration does for the rows that exist, and the seed for the ones it writes) ties it to the room of that name —
        // and makes a room of its own for a name nothing has yet.
        var orphanName = $"Old {Guid.NewGuid():N}"[..12];
        var orphan = await admin.PostAsJsonAsync("/api/schedule/lessons", new
        {
            branchId = first.BranchId, groupId = first.GroupId, teacherId = await TeacherIdAsync(), roomName = orphanName.ToUpperInvariant(),
            startsAt = slot.Start.AddDays(5), endsAt = slot.End.AddDays(5),
        });
        var orphanId = (await orphan.Content.ReadFromJsonAsync<LessonDto>())!.Id;
        using (var scope = factory.Services.CreateScope())
            await DbSeeder.BackfillRoomsAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>());

        using var check = factory.Services.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(room.Id, (await db.Lessons.SingleAsync(l => l.Id == legacyLesson.Id)).RoomId);
        var created = await db.Rooms.SingleAsync(r => r.BranchId == first.BranchId && r.Name == orphanName.ToUpperInvariant());
        Assert.Equal(created.Id, (await db.Lessons.SingleAsync(l => l.Id == orphanId)).RoomId);
        Assert.Equal(1, await db.Rooms.CountAsync(r => r.BranchId == first.BranchId && r.Name.ToLower() == room.Name.ToLower())); // no duplicate of the existing one
    }

    [Fact]
    public async Task ARoomOfAnotherBranch_CannotHoldALesson()
    {
        var (admin, first, _, _) = await TwoClassroomsAsync();
        var superAdmin = await world.ClientAsync("superadmin@demo-center.test");
        var foreign = (await (await superAdmin.PostAsJsonAsync("/api/rooms", new { branchId = await OtherBranchIdAsync(first.BranchId), name = $"Far {Guid.NewGuid():N}"[..12] })).Content.ReadFromJsonAsync<RoomDto>())!;

        Assert.Equal(HttpStatusCode.BadRequest, (await ScheduleAsync(admin, first, foreign.Id, Slot())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ScheduleAsync(admin, first, Guid.NewGuid(), Slot())).StatusCode);
        // A lesson needs a room one way or the other.
        Assert.Equal(HttpStatusCode.BadRequest, (await ScheduleAsync(admin, first, null, Slot())).StatusCode);

        await superAdmin.DeleteAsync($"/api/rooms/{foreign.Id}");
    }

    // ---- helpers ------------------------------------------------------------------------------------

    private record Window(DateTimeOffset Start, DateTimeOffset End);

    private static DateTimeOffset Midnight(int daysAhead) => new(DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(daysAhead), DateTimeKind.Utc));

    /// <summary>A window nobody else uses: the teacher is shared by the whole suite, so a fixed hour would now and then meet another test's lesson.</summary>
    private static Window Slot()
    {
        var start = Midnight(Random.Shared.Next(9500, 20000)).AddMinutes(Random.Shared.Next(0, 1200));
        return new Window(start, start.AddMinutes(60));
    }

    private async Task<Guid> TeacherIdAsync() => await world.UserIdAsync("teacher@demo-center.test", "TEACHER");

    private async Task<(HttpClient Admin, LmsWorld.Classroom First, LmsWorld.Classroom Second, RoomDto Room)> TwoClassroomsAsync()
    {
        var admin = await world.ClientAsync("admin@demo-center.test");
        var first = await world.CreateAsync();
        var second = await world.CreateAsync();
        Assert.Equal(first.BranchId, second.BranchId);
        return (admin, first, second, await CreateRoomAsync(admin, first.BranchId));
    }

    private async Task<RoomDto> CreateRoomAsync(HttpClient client, Guid branchId, int? capacity = null)
    {
        var response = await client.PostAsJsonAsync("/api/rooms", new { branchId, name = $"Room {Guid.NewGuid():N}"[..14], capacity });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RoomDto>())!;
    }

    private async Task<HttpResponseMessage> ScheduleAsync(HttpClient admin, LmsWorld.Classroom classroom, Guid? roomId, Window window) =>
        await admin.PostAsJsonAsync("/api/schedule/lessons", new
        {
            branchId = classroom.BranchId, groupId = classroom.GroupId, teacherId = await TeacherIdAsync(), roomId, startsAt = window.Start, endsAt = window.End,
        });

    private async Task<Guid> LessonInAnotherRoomAsync(HttpClient admin, LmsWorld.Classroom classroom)
    {
        var other = await CreateRoomAsync(admin, classroom.BranchId);
        var window = Slot();
        return (await (await ScheduleAsync(admin, classroom, other.Id, window)).Content.ReadFromJsonAsync<LessonDto>())!.Id;
    }

    private async Task<Guid> OtherBranchIdAsync(Guid branchId)
    {
        var superAdmin = await world.ClientAsync("superadmin@demo-center.test");
        return (await superAdmin.GetFromJsonAsync<List<BranchRow>>("/api/branches"))!.First(b => b.Id != branchId).Id;
    }

    private record BranchRow(Guid Id);
    private record RoomDto(Guid Id, Guid BranchId, string Name, int? Capacity, string? Equipment, bool IsActive, int LessonCount);
    private record LessonDto(Guid Id, Guid GroupId, string RoomName, Guid? RoomId, List<string>? Warnings);
    private record BookingDto(Guid LessonId, DateTimeOffset StartsAt, Guid GroupId, string? TeacherName);
    private record AvailabilityDto(Guid RoomId, string Name, List<BookingDto> Busy);
}
