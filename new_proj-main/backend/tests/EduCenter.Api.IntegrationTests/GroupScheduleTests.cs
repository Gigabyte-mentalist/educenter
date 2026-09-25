using System.Net;
using System.Net.Http.Json;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// A group's weekly template and the lessons made from it (docs/21.09-audit-jurnali S07.04): "Mon/Wed/Fri 18:00, 90 minutes" written once,
/// a period of lessons made from it — every one checked first, all or nothing, on the branch's own clock.
/// </summary>
[Collection("Api")]
public class GroupScheduleTests(EduCenterWebApplicationFactory factory)
{
    private readonly LmsWorld world = new(factory);

    [Fact]
    public async Task ATemplateOfThreeSlots_MakesTwelveLessonsInFourWeeks_AtTheBranchsLocalTime()
    {
        var (admin, group, room, teacherId) = await ArrangeAsync();
        var monday = FarMonday();
        await SaveAsync(admin, group, Slot(DayOfWeek.Monday, "18:00", 90, room.Id), Slot(DayOfWeek.Wednesday, "18:00", 90, room.Id), Slot(DayOfWeek.Friday, "09:30", 60, room.Id, teacherId));

        var template = (await admin.GetFromJsonAsync<TemplateDto>($"/api/groups/{group.GroupId}/schedule-template"))!;
        Assert.Equal("Asia/Tashkent", template.Timezone);
        Assert.Equal(new[] { "Monday", "Wednesday", "Friday" }, template.Slots.Select(s => s.DayOfWeek).ToArray()); // in the order of a week
        Assert.Equal(room.Name, template.Slots[0].RoomName);
        Assert.Null(template.Slots[0].TeacherId); // the group's main teacher takes it
        Assert.Equal(teacherId, template.Slots[2].TeacherId);

        var before = (await LessonsOfAsync(admin, group, monday, monday.AddDays(28))).Count;
        var made = await GenerateAsync(admin, group, monday, monday.AddDays(27));
        Assert.Equal(HttpStatusCode.OK, made.StatusCode);
        var result = (await made.Content.ReadFromJsonAsync<GenerationDto>())!;
        Assert.Equal((false, 12, 0), (result.Preview, result.Created, result.Skipped));
        Assert.Equal(12, result.Lessons.Count);

        var lessons = (await LessonsOfAsync(admin, group, monday, monday.AddDays(28))).OrderBy(l => l.StartsAt).ToList();
        Assert.Equal(before + 12, lessons.Count);
        // 18:00 in Tashkent (UTC+5) is 13:00Z, 09:30 is 04:30Z: the template is the branch's wall clock, the lessons are instants.
        var first = lessons.First(l => l.StartsAt.Date == monday.Date);
        Assert.Equal(monday.AddHours(13), first.StartsAt);
        Assert.Equal(monday.AddHours(14).AddMinutes(30), first.EndsAt);
        var friday = lessons.First(l => l.StartsAt.Date == monday.AddDays(4).Date);
        Assert.Equal(monday.AddDays(4).AddHours(4).AddMinutes(30), friday.StartsAt);
        Assert.Equal(friday.StartsAt.AddMinutes(60), friday.EndsAt);
        Assert.All(lessons.Where(l => l.StartsAt.Hour == 13), l => Assert.Equal(room.Name, l.RoomName));
        Assert.Equal(12, lessons.Count(l => l.RoomId == room.Id));
    }

    [Fact]
    public async Task OneClash_MakesNothing_AndSaysWhichLesson()
    {
        var (admin, group, room, _) = await ArrangeAsync();
        var other = await world.CreateAsync();
        var monday = FarMonday();
        await SaveAsync(admin, group, Slot(DayOfWeek.Monday, "18:00", 90, room.Id), Slot(DayOfWeek.Wednesday, "18:00", 90, room.Id));

        // Another group already holds the room on the second Wednesday.
        var wednesday = monday.AddDays(9);
        var taken = await admin.PostAsJsonAsync("/api/schedule/lessons", new
        {
            branchId = other.BranchId, groupId = other.GroupId, teacherId = await world.UserIdAsync("admin@demo-center.test", "ADMINISTRATOR"), roomId = room.Id,
            startsAt = wednesday.AddHours(13), endsAt = wednesday.AddHours(14),
        });
        Assert.Equal(HttpStatusCode.Created, taken.StatusCode);

        var before = (await LessonsOfAsync(admin, group, monday, monday.AddDays(28))).Count;
        var refused = await GenerateAsync(admin, group, monday, monday.AddDays(27));
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var conflicts = (await refused.Content.ReadFromJsonAsync<ConflictsDto>())!.Conflicts;
        var clash = Assert.Single(conflicts);
        Assert.Equal("ROOM", clash.Type);
        Assert.Equal(DateOnly.FromDateTime(wednesday.Date), clash.Date);

        Assert.Equal(before, (await LessonsOfAsync(admin, group, monday, monday.AddDays(28))).Count); // not one of the eight was made

        // A preview says the same without refusing — the screen shows it next to what would be made.
        var preview = (await (await GenerateAsync(admin, group, monday, monday.AddDays(27), preview: true)).Content.ReadFromJsonAsync<GenerationDto>())!;
        Assert.True(preview.Preview);
        Assert.Equal(0, preview.Created);
        Assert.Equal(8, preview.Lessons.Count);
        Assert.Single(preview.Conflicts);

        // Skipping that day (a holiday) clears the way.
        var skipped = await GenerateAsync(admin, group, monday, monday.AddDays(27), skipDates: [DateOnly.FromDateTime(wednesday.Date)]);
        Assert.Equal(HttpStatusCode.OK, skipped.StatusCode);
        Assert.Equal(7, (await skipped.Content.ReadFromJsonAsync<GenerationDto>())!.Created);
    }

    [Fact]
    public async Task RunningItAgain_MakesNothingTwice_AndAPreviewMakesNothingAtAll()
    {
        var (admin, group, room, _) = await ArrangeAsync();
        var monday = FarMonday();
        await SaveAsync(admin, group, Slot(DayOfWeek.Tuesday, "17:00", 60, room.Id));

        var preview = (await (await GenerateAsync(admin, group, monday, monday.AddDays(13), preview: true)).Content.ReadFromJsonAsync<GenerationDto>())!;
        Assert.Equal(2, preview.Lessons.Count);
        Assert.Equal(0, (await LessonsOfAsync(admin, group, monday, monday.AddDays(14))).Count(l => l.RoomId == room.Id));

        Assert.Equal(2, (await (await GenerateAsync(admin, group, monday, monday.AddDays(13))).Content.ReadFromJsonAsync<GenerationDto>())!.Created);
        var again = (await (await GenerateAsync(admin, group, monday, monday.AddDays(20))).Content.ReadFromJsonAsync<GenerationDto>())!;
        Assert.Equal((1, 2), (again.Created, again.Skipped)); // only the third week is new
        Assert.Equal(3, (await LessonsOfAsync(admin, group, monday, monday.AddDays(21))).Count(l => l.RoomId == room.Id));

        // A cancelled lesson is still "there": running it again does not bring it back.
        var cancelled = (await LessonsOfAsync(admin, group, monday, monday.AddDays(21))).First(l => l.RoomId == room.Id);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/schedule/lessons/{cancelled.Id}/cancel", new { reason = "Bayram" })).StatusCode);
        var afterCancel = (await (await GenerateAsync(admin, group, monday, monday.AddDays(20))).Content.ReadFromJsonAsync<GenerationDto>())!;
        Assert.Equal((0, 3), (afterCancel.Created, afterCancel.Skipped));
    }

    [Fact]
    public async Task OneMessageGoesToTheGroup_NotOnePerLesson()
    {
        var (admin, group, room, _) = await ArrangeAsync();
        var student = await world.ClientAsync(group.First.Email);
        var monday = FarMonday();
        await SaveAsync(admin, group, Slot(DayOfWeek.Monday, "18:00", 60, room.Id), Slot(DayOfWeek.Thursday, "18:00", 60, room.Id));

        var before = await ScheduleMessagesAsync(student);
        Assert.Equal(HttpStatusCode.OK, (await GenerateAsync(admin, group, monday, monday.AddDays(41))).StatusCode); // twelve lessons
        Assert.Equal(before + 1, await ScheduleMessagesAsync(student));
    }

    [Fact]
    public async Task ThePeriod_MustBeSane_AndTheGroupReady()
    {
        var (admin, group, room, _) = await ArrangeAsync();
        var monday = FarMonday();

        // No template yet.
        Assert.Equal(HttpStatusCode.BadRequest, (await GenerateAsync(admin, group, monday, monday.AddDays(7))).StatusCode);

        await SaveAsync(admin, group, Slot(DayOfWeek.Monday, "18:00", 60, room.Id));
        Assert.Equal(HttpStatusCode.OK, (await GenerateAsync(admin, group, monday, monday.AddMonths(6), preview: true)).StatusCode); // six months exactly
        Assert.Equal(HttpStatusCode.BadRequest, (await GenerateAsync(admin, group, monday, monday.AddMonths(6).AddDays(1), preview: true)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await GenerateAsync(admin, group, monday.AddDays(7), monday, preview: true)).StatusCode); // ends before it starts
        Assert.Equal(HttpStatusCode.BadRequest, (await GenerateAsync(admin, group, DateTimeOffset.UtcNow.AddDays(-3), DateTimeOffset.UtcNow.AddDays(10))).StatusCode); // past

        // A slot with no room cannot become a lesson.
        await SaveAsync(admin, group, Slot(DayOfWeek.Monday, "18:00", 60, null));
        var noRoom = await GenerateAsync(admin, group, monday, monday.AddDays(7));
        Assert.Equal(HttpStatusCode.BadRequest, noRoom.StatusCode);
        Assert.Contains("xona", await noRoom.Content.ReadAsStringAsync());

        // A room switched off since the template was saved is refused as well.
        await SaveAsync(admin, group, Slot(DayOfWeek.Monday, "18:00", 60, room.Id));
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/rooms/{room.Id}", new { isActive = false })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await GenerateAsync(admin, group, monday, monday.AddDays(7))).StatusCode);
    }

    [Fact]
    public async Task ATemplateThatCannotHold_IsRefused_AndSavingReplacesTheWholeTemplate()
    {
        var (admin, group, room, _) = await ArrangeAsync();
        var url = $"/api/groups/{group.GroupId}/schedule-template";

        Assert.Equal(HttpStatusCode.BadRequest, (await SaveAsync(admin, group, Slot(DayOfWeek.Monday, "18:00", 90, room.Id), Slot(DayOfWeek.Monday, "19:00", 60, room.Id))).StatusCode); // overlap
        Assert.Equal(HttpStatusCode.OK, (await SaveAsync(admin, group, Slot(DayOfWeek.Monday, "18:00", 60, room.Id), Slot(DayOfWeek.Monday, "19:00", 60, room.Id))).StatusCode); // back to back is fine
        Assert.Equal(HttpStatusCode.BadRequest, (await SaveAsync(admin, group, Slot(DayOfWeek.Monday, "23:30", 60, room.Id))).StatusCode); // ends after midnight
        Assert.Equal(HttpStatusCode.BadRequest, (await SaveAsync(admin, group, Slot(DayOfWeek.Monday, "6pm", 60, room.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SaveAsync(admin, group, Slot(DayOfWeek.Monday, "18:00", 5, room.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SaveAsync(admin, group, Slot(DayOfWeek.Monday, "18:00", 361, room.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SaveAsync(admin, group, Enumerable.Range(0, 22).Select(i => Slot((DayOfWeek)(i % 7), $"{8 + i / 7 * 3:00}:00", 60, room.Id)).ToArray())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SaveAsync(admin, group, Slot(DayOfWeek.Monday, "18:00", 60, Guid.NewGuid()))).StatusCode);

        // A room of another branch does not belong here.
        var superAdmin = await world.ClientAsync("superadmin@demo-center.test");
        var otherBranch = (await superAdmin.GetFromJsonAsync<List<BranchRow>>("/api/branches"))!.First(b => b.Id != group.BranchId).Id;
        var foreign = (await (await superAdmin.PostAsJsonAsync("/api/rooms", new { branchId = otherBranch, name = $"Far {Guid.NewGuid():N}"[..12] })).Content.ReadFromJsonAsync<RoomDto>())!;
        Assert.Equal(HttpStatusCode.BadRequest, (await SaveAsync(admin, group, Slot(DayOfWeek.Monday, "18:00", 60, foreign.Id))).StatusCode);
        await superAdmin.DeleteAsync($"/api/rooms/{foreign.Id}");

        // None of the refusals touched what was saved; saving again replaces it — including with nothing.
        Assert.Equal(2, (await admin.GetFromJsonAsync<TemplateDto>(url))!.Slots.Count);
        Assert.Equal(HttpStatusCode.OK, (await SaveAsync(admin, group, Slot(DayOfWeek.Sunday, "10:00", 60, room.Id))).StatusCode);
        Assert.Equal("Sunday", Assert.Single((await admin.GetFromJsonAsync<TemplateDto>(url))!.Slots).DayOfWeek);
        Assert.Empty((await (await SaveAsync(admin, group)).Content.ReadFromJsonAsync<TemplateDto>())!.Slots);

        // A room the template still uses cannot be erased under it.
        await SaveAsync(admin, group, Slot(DayOfWeek.Monday, "18:00", 60, room.Id));
        var refused = await admin.DeleteAsync($"/api/rooms/{room.Id}");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("jadvalida", await refused.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AClosedGroup_TakesNoTemplateAndNoLessons_AndNotEveryoneMayChangeOne()
    {
        var (admin, group, room, _) = await ArrangeAsync();
        var monday = FarMonday();
        await SaveAsync(admin, group, Slot(DayOfWeek.Monday, "18:00", 60, room.Id));

        // Teachers, students and operators read schedules; they do not write them. A foreign branch's group is out of reach for the Administrator.
        foreach (var email in new[] { "teacher@demo-center.test", "operator@demo-center.test", group.First.Email })
        {
            var client = await world.ClientAsync(email);
            Assert.Equal(HttpStatusCode.Forbidden, (await SaveAsync(client, group, Slot(DayOfWeek.Friday, "10:00", 60, room.Id))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await GenerateAsync(client, group, monday, monday.AddDays(7))).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync($"/api/groups/{group.GroupId}/schedule-template")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/groups/{Guid.NewGuid()}/schedule-template")).StatusCode);

        // The group's own student may read the template (they see the group's overview too).
        Assert.Equal(HttpStatusCode.OK, (await (await world.ClientAsync(group.First.Email)).GetAsync($"/api/groups/{group.GroupId}/schedule-template")).StatusCode);

        var superAdmin = await world.ClientAsync("superadmin@demo-center.test");
        Assert.Equal(HttpStatusCode.OK, (await superAdmin.PostAsJsonAsync($"/api/groups/{group.GroupId}/status", new { status = "Cancelled" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await SaveAsync(admin, group, Slot(DayOfWeek.Friday, "10:00", 60, room.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await GenerateAsync(admin, group, monday, monday.AddDays(7))).StatusCode);
    }

    [Fact]
    public async Task AGroupWithoutAMainTeacher_NeedsATeacherOnEachSlot()
    {
        var admin = await world.ClientAsync("admin@demo-center.test");
        var group = await world.CreateAsync(teacherStaffsGroup: false);
        var room = await CreateRoomAsync(admin, group.BranchId);
        var teacherId = await world.UserIdAsync("teacher@demo-center.test", "TEACHER");
        var monday = FarMonday();

        await SaveAsync(admin, group, Slot(DayOfWeek.Monday, "18:00", 60, room.Id));
        var refused = await GenerateAsync(admin, group, monday, monday.AddDays(6));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("o'qituvchi", await refused.Content.ReadAsStringAsync());

        await SaveAsync(admin, group, Slot(DayOfWeek.Monday, "18:00", 60, room.Id, teacherId));
        var made = (await (await GenerateAsync(admin, group, monday, monday.AddDays(6))).Content.ReadFromJsonAsync<GenerationDto>())!;
        Assert.Equal(1, made.Created);
        Assert.Equal(teacherId, made.Lessons[0].TeacherId);
    }

    [Fact]
    public async Task TheRecommendation_JudgesAGroupOnItsTemplate_WhenItHasOne()
    {
        var admin = await world.ClientAsync("admin@demo-center.test");
        var operatorClient = await world.ClientAsync("operator@demo-center.test");
        var branchId = (await world.CreateAsync()).BranchId;
        // A course name only these two groups carry, so they are the ones the lead's interest matches — the suite has made dozens of IELTS groups by now.
        var token = $"Tpl{Guid.NewGuid():N}"[..11];
        var course = await (await admin.PostAsJsonAsync("/api/courses", new { name = token, levelCategory = "IELTS" })).Content.ReadFromJsonAsync<IdDto>();
        var evening = await NewGroupAsync(admin, branchId, course!.Id, "EVE");
        var morning = await NewGroupAsync(admin, branchId, course.Id, "MOR");
        var room = await CreateRoomAsync(admin, branchId);
        var otherRoom = await CreateRoomAsync(admin, branchId);
        await SaveAsync(admin, evening, Slot(DayOfWeek.Monday, "18:00", 90, room.Id), Slot(DayOfWeek.Wednesday, "18:00", 90, room.Id));
        await SaveAsync(admin, morning, Slot(DayOfWeek.Monday, "09:00", 90, otherRoom.Id), Slot(DayOfWeek.Wednesday, "09:00", 90, otherRoom.Id));

        // Neither group has a single lesson yet — before templates there was nothing to judge the time on.
        var lead = await (await operatorClient.PostAsJsonAsync("/api/leads", new
        {
            branchId, fullName = "Tpl Lead", phone = "+998900000077", source = "Website", interestedCourse = token, preferredTime = "Evening",
        })).Content.ReadFromJsonAsync<IdDto>();
        var ranked = (await operatorClient.GetFromJsonAsync<List<RecommendationDto>>($"/api/leads/{lead!.Id}/group-recommendations"))!;

        var eveningScore = ranked.Single(r => r.GroupId == evening.GroupId);
        var morningScore = ranked.Single(r => r.GroupId == morning.GroupId);
        Assert.True(eveningScore.MatchPercent > morningScore.MatchPercent, $"{eveningScore.MatchPercent} vs {morningScore.MatchPercent}");
        Assert.Contains(eveningScore.Reasons, reason => reason.Contains("kechqurun"));
    }

    // ---- helpers ------------------------------------------------------------------------------------

    private record SlotBody(DayOfWeek DayOfWeek, string StartTime, int DurationMinutes, Guid? RoomId, Guid? TeacherId);
    private static SlotBody Slot(DayOfWeek day, string start, int minutes, Guid? roomId, Guid? teacherId = null) => new(day, start, minutes, roomId, teacherId);

    /// <summary>The Monday of a week far ahead — the shared demo teacher is booked by other tests at random slots, so the template gets days of its own.</summary>
    private static DateTimeOffset FarMonday()
    {
        var day = new DateTimeOffset(DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(Random.Shared.Next(6000, 60000)), DateTimeKind.Utc));
        return day.AddDays(((int)DayOfWeek.Monday - (int)day.DayOfWeek + 7) % 7);
    }

    private async Task<(HttpClient Admin, LmsWorld.Classroom Group, RoomDto Room, Guid TeacherId)> ArrangeAsync()
    {
        var admin = await world.ClientAsync("admin@demo-center.test");
        var group = await world.CreateAsync();
        return (admin, group, await CreateRoomAsync(admin, group.BranchId), await world.UserIdAsync("teacher@demo-center.test", "TEACHER"));
    }

    private async Task<RoomDto> CreateRoomAsync(HttpClient admin, Guid branchId)
    {
        var response = await admin.PostAsJsonAsync("/api/rooms", new { branchId, name = $"Tpl {Guid.NewGuid():N}"[..12], capacity = 30 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RoomDto>())!;
    }

    private async Task<LmsWorld.Classroom> NewGroupAsync(HttpClient admin, Guid branchId, Guid courseId, string prefix)
    {
        var group = await admin.PostAsJsonAsync("/api/groups", new { branchId, courseId, name = $"{prefix}-{Guid.NewGuid():N}"[..14], capacity = 10 });
        Assert.Equal(HttpStatusCode.Created, group.StatusCode);
        return new LmsWorld.Classroom((await group.Content.ReadFromJsonAsync<IdDto>())!.Id, branchId, Guid.Empty, DateTimeOffset.UtcNow, []);
    }

    private static Task<HttpResponseMessage> SaveAsync(HttpClient client, LmsWorld.Classroom group, params SlotBody[] slots) =>
        client.PutAsJsonAsync($"/api/groups/{group.GroupId}/schedule-template", new { slots });

    private static Task<HttpResponseMessage> GenerateAsync(HttpClient client, LmsWorld.Classroom group, DateTimeOffset from, DateTimeOffset to, bool preview = false, DateOnly[]? skipDates = null) =>
        client.PostAsJsonAsync($"/api/groups/{group.GroupId}/generate-lessons{(preview ? "?preview=true" : "")}",
            new { from = DateOnly.FromDateTime(from.UtcDateTime), to = DateOnly.FromDateTime(to.UtcDateTime), skipDates });

    private static async Task<List<LessonRow>> LessonsOfAsync(HttpClient client, LmsWorld.Classroom group, DateTimeOffset from, DateTimeOffset to)
    {
        var all = await client.GetFromJsonAsync<List<LessonRow>>(
            $"/api/schedule?branchId={group.BranchId}&from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}");
        return all!.Where(l => l.GroupId == group.GroupId).ToList();
    }

    private static async Task<int> ScheduleMessagesAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<NotificationRow>>("/api/notifications?limit=100") ?? []).Count(n => n.Type == "SCHEDULE_CHANGED");

    private record IdDto(Guid Id);
    private record BranchRow(Guid Id);
    private record RoomDto(Guid Id, Guid BranchId, string Name);
    private record SlotDto(string DayOfWeek, string StartTime, int DurationMinutes, Guid? RoomId, string? RoomName, Guid? TeacherId, string? TeacherName);
    private record TemplateDto(Guid GroupId, string Timezone, List<SlotDto> Slots);
    private record LessonRow(Guid Id, Guid GroupId, string RoomName, Guid? RoomId, DateTimeOffset StartsAt, DateTimeOffset EndsAt);
    private record GeneratedDto(DateOnly Date, DateTimeOffset StartsAt, string RoomName, Guid TeacherId);
    private record ConflictRow(DateOnly Date, string Type, Guid ConflictingLessonId);
    private record GenerationDto(bool Preview, int Created, int Skipped, List<GeneratedDto> Lessons, List<ConflictRow> Conflicts);
    private record ConflictsDto(List<ConflictRow> Conflicts);
    private record NotificationRow(Guid Id, string Type);
    private record RecommendationDto(Guid GroupId, int MatchPercent, List<string> Reasons);
}
