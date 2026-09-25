using System.Net;
using System.Net.Http.Json;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// The electronic journal (docs/21.09-audit-jurnali S03.01–S03.02, TZ §15): the teacher writes a lesson's topic and notes,
/// marks attendance with a remark and a lesson score. The topic is public to the group, the notes are staff-only.
/// </summary>
[Collection("Api")]
public class LessonJournalTests(EduCenterWebApplicationFactory factory)
{
    private readonly LmsWorld world = new(factory);

    [Fact]
    public async Task Teacher_WritesTopicAndNotes_TopicReachesTheSchedule()
    {
        var c = await world.CreateAsync();
        var teacher = await world.ClientAsync("teacher@demo-center.test");

        var saved = await teacher.PatchAsJsonAsync($"/api/lessons/{c.LessonId}/journal", new { topic = "  Present Perfect ", notes = "Weak on irregular verbs." });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(new JournalDto("Present Perfect", "Weak on irregular verbs.", null), await saved.Content.ReadFromJsonAsync<JournalDto>());

        Assert.Equal(new JournalDto("Present Perfect", "Weak on irregular verbs.", null),
            await teacher.GetFromJsonAsync<JournalDto>($"/api/lessons/{c.LessonId}/journal"));

        var admin = await world.ClientAsync("admin@demo-center.test");
        Assert.Equal("Present Perfect", (await ScheduleAsync(admin, c)).Single(l => l.Id == c.LessonId).Topic);
    }

    [Fact]
    public async Task EmptyTextClears_AndNullLeavesTheFieldAlone()
    {
        var c = await world.CreateAsync();
        var teacher = await world.ClientAsync("teacher@demo-center.test");
        await teacher.PatchAsJsonAsync($"/api/lessons/{c.LessonId}/journal", new { topic = "Topic", notes = "Notes" });

        var onlyNotes = await teacher.PatchAsJsonAsync($"/api/lessons/{c.LessonId}/journal", new { notes = "Changed notes" });
        Assert.Equal(new JournalDto("Topic", "Changed notes", null), await onlyNotes.Content.ReadFromJsonAsync<JournalDto>());

        var cleared = await teacher.PatchAsJsonAsync($"/api/lessons/{c.LessonId}/journal", new { topic = "   " });
        Assert.Equal(new JournalDto(null, "Changed notes", null), await cleared.Content.ReadFromJsonAsync<JournalDto>());

        // Nothing to change is a mistake, not a no-op.
        Assert.Equal(HttpStatusCode.BadRequest, (await teacher.PatchAsJsonAsync($"/api/lessons/{c.LessonId}/journal", new { })).StatusCode);
    }

    [Fact]
    public async Task TooLongText_IsRefused_AndNothingIsSaved()
    {
        var c = await world.CreateAsync();
        var teacher = await world.ClientAsync("teacher@demo-center.test");

        Assert.Equal(HttpStatusCode.BadRequest, (await teacher.PatchAsJsonAsync($"/api/lessons/{c.LessonId}/journal", new { topic = new string('m', 201) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await teacher.PatchAsJsonAsync($"/api/lessons/{c.LessonId}/journal", new { notes = new string('n', 2001) })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await teacher.PatchAsJsonAsync($"/api/lessons/{c.LessonId}/journal",
            new { topic = new string('m', 200), notes = new string('n', 2000) })).StatusCode);
    }

    [Theory]
    [InlineData("student")]
    [InlineData("parent")]
    [InlineData("support")]
    [InlineData("operator")]
    public async Task OnlyThoseWhoMarkAttendance_ReadOrWriteTheJournal(string who)
    {
        var c = await world.CreateAsync();
        var client = await world.ClientAsync(who == "student" ? c.First.Email : $"{who}@demo-center.test");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/lessons/{c.LessonId}/journal")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PatchAsJsonAsync($"/api/lessons/{c.LessonId}/journal", new { topic = "Nope" })).StatusCode);
    }

    [Fact]
    public async Task ATeacherWhoDoesNotTeachTheGroup_CannotTouchItsJournal()
    {
        var c = await world.CreateAsync(teacherStaffsGroup: false);
        var teacher = await world.ClientAsync("teacher@demo-center.test");

        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync($"/api/lessons/{c.LessonId}/journal")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PatchAsJsonAsync($"/api/lessons/{c.LessonId}/journal", new { topic = "Not mine" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await teacher.GetAsync($"/api/lessons/{Guid.NewGuid()}/journal")).StatusCode);
    }

    [Fact]
    public async Task ACancelledLesson_TakesNoJournal_ButAPastOneStillDoes()
    {
        var c = await world.CreateAsync();
        var teacher = await world.ClientAsync("teacher@demo-center.test");

        // Most teachers write a lesson up once it is over.
        var pastLessonId = await world.AddPastLessonAsync(c);
        Assert.Equal(HttpStatusCode.OK, (await teacher.PatchAsJsonAsync($"/api/lessons/{pastLessonId}/journal", new { topic = "Written afterwards" })).StatusCode);

        var admin = await world.ClientAsync("admin@demo-center.test");
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/schedule/lessons/{c.LessonId}/cancel", new { reason = "Teacher is ill" })).StatusCode);

        var refused = await teacher.PatchAsJsonAsync($"/api/lessons/{c.LessonId}/journal", new { topic = "Too late" });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("Bekor qilingan", await refused.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TheNotes_NeverReachAStudentOrAParent_OnlyTheTopicDoes()
    {
        var c = await world.CreateAsync();
        var teacher = await world.ClientAsync("teacher@demo-center.test");
        await teacher.PatchAsJsonAsync($"/api/lessons/{c.LessonId}/journal", new { topic = "Public topic", notes = "PRIVATE-STAFF-NOTE" });

        var student = await world.ClientAsync(c.First.Email);
        var raw = await student.GetStringAsync($"/api/schedule?from={Uri.EscapeDataString(c.StartsAt.AddHours(-1).ToString("O"))}&to={Uri.EscapeDataString(c.StartsAt.AddHours(2).ToString("O"))}");
        Assert.Contains("Public topic", raw);
        Assert.DoesNotContain("PRIVATE-STAFF-NOTE", raw);

        var parent = await world.ClientAsync("parent@demo-center.test");
        Assert.DoesNotContain("PRIVATE-STAFF-NOTE", await parent.GetStringAsync("/api/schedule"));
    }

    // ---- attendance remark and lesson score (S03.02) -------------------------------------------

    [Fact]
    public async Task Attendance_TakesARemarkAndAScore_AndReturnsThemToStaff()
    {
        var c = await world.CreateAsync();
        var teacher = await world.ClientAsync("teacher@demo-center.test");
        await SetScaleAsync(teacher, c, 10);

        await MarkAsync(teacher, c, "Late", "  Bus broke down ", 7);

        var row = Assert.Single(await AttendanceAsync(teacher, c));
        Assert.Equal(("Late", "Bus broke down", 7), (row.Status, row.Note, row.Score));
    }

    /// <summary>Regression: the old upsert skipped a row whose status had not changed, so a corrected remark or score was lost.</summary>
    [Fact]
    public async Task TheSameStatus_WithAnotherRemarkOrScore_IsSaved()
    {
        var c = await world.CreateAsync();
        var teacher = await world.ClientAsync("teacher@demo-center.test");
        await SetScaleAsync(teacher, c, 10);

        await MarkAsync(teacher, c, "Present", "first", 5);
        await MarkAsync(teacher, c, "Present", "corrected", 8);
        var row = Assert.Single(await AttendanceAsync(teacher, c));
        Assert.Equal(("Present", "corrected", 8), (row.Status, row.Note, row.Score));

        // An item is the whole record: sending none takes them back.
        await MarkAsync(teacher, c, "Present", null, null);
        row = Assert.Single(await AttendanceAsync(teacher, c));
        Assert.Equal((null, null), (row.Note, row.Score));
    }

    [Fact]
    public async Task AScore_MustFitTheLessonsScale_AndALessonWithoutOneTakesNone()
    {
        var c = await world.CreateAsync();
        var teacher = await world.ClientAsync("teacher@demo-center.test");

        var unscored = await PostAttendanceAsync(teacher, c, "Present", null, 5);
        Assert.Equal(HttpStatusCode.BadRequest, unscored.StatusCode);
        Assert.Contains("chegarasini belgilang", await unscored.Content.ReadAsStringAsync());

        await SetScaleAsync(teacher, c, 10);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAttendanceAsync(teacher, c, "Present", null, 11)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAttendanceAsync(teacher, c, "Present", null, -1)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAttendanceAsync(teacher, c, "Present", new string('x', 501), null)).StatusCode);
        Assert.Empty(await AttendanceAsync(teacher, c)); // the refused calls wrote nothing

        await MarkAsync(teacher, c, "Present", new string('x', 500), 10);
        Assert.Equal(10, Assert.Single(await AttendanceAsync(teacher, c)).Score);
        await MarkAsync(teacher, c, "Present", null, 0);
        Assert.Equal(0, Assert.Single(await AttendanceAsync(teacher, c)).Score);
    }

    [Fact]
    public async Task CorrectingARemark_IsNotANewFact_ButChangingTheStatusIs()
    {
        var c = await world.CreateAsync();
        var teacher = await world.ClientAsync("teacher@demo-center.test");
        var admin = await world.ClientAsync("admin@demo-center.test");

        await MarkAsync(teacher, c, "Absent", "sick", null);
        await MarkAsync(teacher, c, "Absent", "sick, brought a doctor's note", null);
        Assert.Equal(1, await TimelineCountAsync(admin, c, "LESSON_ABSENT"));

        await MarkAsync(teacher, c, "Present", "came after all", null);
        Assert.Equal(1, await TimelineCountAsync(admin, c, "LESSON_ATTENDED"));
    }

    [Fact]
    public async Task TheRemark_IsForStaffWhoMarkAttendance_TheScoreForTheStudentToo()
    {
        var c = await world.CreateAsync();
        var teacher = await world.ClientAsync("teacher@demo-center.test");
        await SetScaleAsync(teacher, c, 10);
        await MarkAsync(teacher, c, "Late", "PRIVATE-REMARK", 9);

        var student = await world.ClientAsync(c.First.Email);
        var seenByStudent = await student.GetStringAsync($"/api/lessons/{c.LessonId}/attendance");
        Assert.DoesNotContain("PRIVATE-REMARK", seenByStudent);
        var own = Assert.Single(await AttendanceAsync(student, c));
        Assert.Equal((null, 9), (own.Note, own.Score));

        // Reading attendance is not enough: an operator can see who came, not why.
        var operatorClient = await world.ClientAsync("operator@demo-center.test");
        Assert.Null(Assert.Single(await AttendanceAsync(operatorClient, c)).Note);

        foreach (var who in new[] { "admin", "teacher" })
            Assert.Equal("PRIVATE-REMARK", Assert.Single(await AttendanceAsync(await world.ClientAsync($"{who}@demo-center.test"), c)).Note);
    }

    [Fact]
    public async Task TheScale_CannotDropBelowAScoreGiven_OrBeClearedWhileScoresExist()
    {
        var c = await world.CreateAsync();
        var teacher = await world.ClientAsync("teacher@demo-center.test");
        await SetScaleAsync(teacher, c, 10);
        await MarkAsync(teacher, c, "Present", null, 8);

        var tooLow = await teacher.PatchAsJsonAsync($"/api/lessons/{c.LessonId}/journal", new { maxLessonScore = 5 });
        Assert.Equal(HttpStatusCode.Conflict, tooLow.StatusCode);
        Assert.Contains("8 ball", await tooLow.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await teacher.PatchAsJsonAsync($"/api/lessons/{c.LessonId}/journal", new { maxLessonScore = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await teacher.PatchAsJsonAsync($"/api/lessons/{c.LessonId}/journal", new { maxLessonScore = 8 })).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await teacher.PatchAsJsonAsync($"/api/lessons/{c.LessonId}/journal", new { maxLessonScore = 101 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await teacher.PatchAsJsonAsync($"/api/lessons/{c.LessonId}/journal", new { maxLessonScore = -1 })).StatusCode);

        await MarkAsync(teacher, c, "Present", null, null);
        var cleared = await teacher.PatchAsJsonAsync($"/api/lessons/{c.LessonId}/journal", new { maxLessonScore = 0 });
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        Assert.Null((await cleared.Content.ReadFromJsonAsync<JournalDto>())!.MaxLessonScore);
    }

    [Fact]
    public async Task TheStudentsScale_ReachesTheScheduleSoTheScreenCanShowNineOutOfTen()
    {
        var c = await world.CreateAsync();
        var teacher = await world.ClientAsync("teacher@demo-center.test");
        await SetScaleAsync(teacher, c, 10);

        var student = await world.ClientAsync(c.First.Email);
        Assert.Equal(10, (await ScheduleAsync(student, c)).Single(l => l.Id == c.LessonId).MaxLessonScore);
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static async Task SetScaleAsync(HttpClient client, LmsWorld.Classroom c, int max) =>
        Assert.Equal(HttpStatusCode.OK, (await client.PatchAsJsonAsync($"/api/lessons/{c.LessonId}/journal", new { maxLessonScore = max })).StatusCode);

    private static Task<HttpResponseMessage> PostAttendanceAsync(HttpClient client, LmsWorld.Classroom c, string status, string? note, int? score) =>
        client.PostAsJsonAsync($"/api/lessons/{c.LessonId}/attendance", new { items = new[] { new { studentId = c.First.Id, status, note, score } } });

    private static async Task MarkAsync(HttpClient client, LmsWorld.Classroom c, string status, string? note, int? score) =>
        Assert.Equal(HttpStatusCode.NoContent, (await PostAttendanceAsync(client, c, status, note, score)).StatusCode);

    private static async Task<List<AttendanceRow>> AttendanceAsync(HttpClient client, LmsWorld.Classroom c) =>
        (await client.GetFromJsonAsync<List<AttendanceRow>>($"/api/lessons/{c.LessonId}/attendance"))!;

    private static async Task<int> TimelineCountAsync(HttpClient admin, LmsWorld.Classroom c, string eventType) =>
        (await admin.GetFromJsonAsync<List<TimelineDto>>($"/api/students/{c.First.Id}/timeline"))!.Count(e => e.EventType == eventType);

    private static async Task<List<LessonRow>> ScheduleAsync(HttpClient client, LmsWorld.Classroom c) =>
        (await client.GetFromJsonAsync<List<LessonRow>>(
            $"/api/schedule?from={Uri.EscapeDataString(c.StartsAt.AddHours(-1).ToString("O"))}&to={Uri.EscapeDataString(c.StartsAt.AddHours(2).ToString("O"))}"))!;

    private record JournalDto(string? Topic, string? Notes, int? MaxLessonScore);
    private record LessonRow(Guid Id, string? Topic, int? MaxLessonScore);
    private record AttendanceRow(Guid StudentId, string Status, string? Note, int? Score);
    private record TimelineDto(string EventType);
}
