using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using EduCenter.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// Staff reading a student's Speaking practice (docs/21.09-audit-jurnali S04.03): the assessment, the transcript and the recordings
/// of a finished session, for the people who teach the student. Practice used to be private; it is shared from the moment the
/// student is told so, so a session recorded before that stays out of reach, and so does one that is still running or abandoned.
/// </summary>
[Collection("Api")]
public class SpeakingReviewTests(EduCenterWebApplicationFactory factory)
{
    private readonly LmsWorld world = new(factory);

    [Fact]
    public async Task Teacher_ReadsAFinishedPractice_WithItsBandsTranscriptAndRecording_AndLeavesATrace()
    {
        var c = await world.CreateAsync();
        var practice = await PractiseAsync(await world.ClientAsync(c.First.Email));
        var teacher = await world.ClientAsync("teacher@demo-center.test");

        var row = Assert.Single((await teacher.GetFromJsonAsync<List<RowDto>>($"/api/students/{c.First.Id}/speaking/sessions"))!);
        Assert.Equal(practice.Id, row.Id);
        Assert.Equal((practice.EstimatedBand, practice.FluencyBand, practice.LexicalBand, practice.GrammarBand),
            (row.EstimatedBand, row.FluencyBand, row.LexicalBand, row.GrammarBand));

        var detail = (await teacher.GetFromJsonAsync<SessionDto>($"/api/students/{c.First.Id}/speaking/sessions/{practice.Id}"))!;
        var answered = detail.Turns.First(t => t.Transcript is not null);
        Assert.NotEmpty(answered.Transcript!);
        Assert.True(answered.HasAudio);
        Assert.NotNull(detail.Assessment);

        var audio = await teacher.GetAsync($"/api/students/{c.First.Id}/speaking/sessions/{practice.Id}/turns/{answered.Id}/audio");
        Assert.Equal(HttpStatusCode.OK, audio.StatusCode);
        Assert.Equal("audio/webm", audio.Content.Headers.ContentType?.MediaType);
        Assert.Equal(2048, (await audio.Content.ReadAsByteArrayAsync()).Length);

        // Opening the session and playing the recording are each written down: who listened to a student's voice, and when.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var teacherId = await db.Users.Where(u => u.Email == "teacher@demo-center.test").Select(u => u.Id).SingleAsync();
        var views = await db.AuditLogs.Where(l => l.ResourceId == practice.Id && l.Action == "Viewed").ToListAsync();
        Assert.Equal(2, views.Count);
        Assert.All(views, v => Assert.Equal(teacherId, v.UserId));
        Assert.Contains(views, v => v.NewValueJson!.Contains("\"what\":\"audio\""));
    }

    [Fact]
    public async Task OnlyFinishedSessionsTheStudentKnewWereShared_AreVisible()
    {
        var c = await world.CreateAsync();
        var student = await world.ClientAsync(c.First.Email);
        var teacher = await world.ClientAsync("teacher@demo-center.test");
        string Path(Guid session) => $"/api/students/{c.First.Id}/speaking/sessions/{session}";

        // Recorded before the notice existed — the migration leaves such sessions private.
        var legacy = await PractiseAsync(student);
        await MakePrivateAsync(legacy.Id);
        var shared = await PractiseAsync(student);

        // One the student walked away from (starting another closes it) and one still running.
        var abandoned = await StartAsync(student);
        var running = await StartAsync(student);
        Assert.Equal("Abandoned", (await student.GetFromJsonAsync<SessionDto>($"/api/ai/speaking/sessions/{abandoned}"))!.Status);

        var listed = (await teacher.GetFromJsonAsync<List<RowDto>>($"/api/students/{c.First.Id}/speaking/sessions"))!;
        Assert.Equal([shared.Id], listed.Select(r => r.Id));

        foreach (var hidden in new[] { legacy.Id, abandoned, running })
            Assert.Equal(HttpStatusCode.NotFound, (await teacher.GetAsync(Path(hidden))).StatusCode);
        var legacyTurn = legacy.Turns.First(t => t.HasAudio).Id;
        Assert.Equal(HttpStatusCode.NotFound, (await teacher.GetAsync($"{Path(legacy.Id)}/turns/{legacyTurn}/audio")).StatusCode);

        // The student's own view is untouched: they still see all of their practice.
        Assert.Equal(HttpStatusCode.OK, (await student.GetAsync($"/api/ai/speaking/sessions/{legacy.Id}")).StatusCode);
    }

    [Fact]
    public async Task Access_NeedsThePermission_AndTheStudentInScope_AndTheSessionMustBelongToThatStudent()
    {
        var mine = await world.CreateAsync(students: 2);
        var notMine = await world.CreateAsync(teacherStaffsGroup: false);
        var practice = await PractiseAsync(await world.ClientAsync(mine.Students[0].Email));
        var url = $"/api/students/{mine.Students[0].Id}/speaking/sessions";

        // Everyone who teaches the student, within their own scope.
        foreach (var email in new[] { "teacher@demo-center.test", "admin@demo-center.test", "superadmin@demo-center.test" })
            Assert.Equal(HttpStatusCode.OK, (await (await world.ClientAsync(email)).GetAsync(url)).StatusCode);

        // A teacher who does not teach this student, and everyone without the permission: the student, their parent, the operator.
        var teacher = await world.ClientAsync("teacher@demo-center.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync($"/api/students/{notMine.First.Id}/speaking/sessions")).StatusCode);
        foreach (var email in new[] { mine.Students[0].Email, "parent@demo-center.test", "operator@demo-center.test" })
        {
            var client = await world.ClientAsync(email);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"{url}/{practice.Id}")).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync(url)).StatusCode);

        // A session id under someone else's name is not found, even by a teacher who teaches both students.
        Assert.Equal(HttpStatusCode.NotFound,
            (await teacher.GetAsync($"/api/students/{mine.Students[1].Id}/speaking/sessions/{practice.Id}")).StatusCode);
    }

    // ---- the teacher's own bands (ai.speaking.override) --------------------------------------------

    [Fact]
    public async Task ATeacherWhoListened_CorrectsTheBands_TheAiStaysAsItWas_AndTheStudentIsTold()
    {
        var c = await world.CreateAsync();
        var student = await world.ClientAsync(c.First.Email);
        var practice = await PractiseAsync(student);
        var teacher = await world.ClientAsync("teacher@demo-center.test");
        var url = $"/api/students/{c.First.Id}/speaking/sessions/{practice.Id}";

        var before = (await teacher.GetFromJsonAsync<StaffSessionDto>(url))!;
        Assert.True(before.CanOverride);
        Assert.Null(before.TeacherReview);

        var response = await teacher.PostAsJsonAsync($"{url}/override", new { band = 7.0, fluency = 7.5, lexical = 6.5, grammar = 7.0, comment = "  Talaffuzi juda yaxshi.  " });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var after = (await response.Content.ReadFromJsonAsync<StaffSessionDto>())!;

        // The teacher's bands sit beside the AI's, which did not move.
        Assert.Equal((7.0m, 7.5m, 6.5m, 7.0m, "Talaffuzi juda yaxshi."),
            (after.TeacherReview!.Band, after.TeacherReview.FluencyBand, after.TeacherReview.LexicalBand, after.TeacherReview.GrammarBand, after.TeacherReview.Comment));
        Assert.Equal((practice.EstimatedBand, practice.FluencyBand), (after.EstimatedBand, after.FluencyBand));
        Assert.NotNull(after.TeacherReview.ByName);

        // The student reads the same review on their own practice — and is told there is one.
        var own = (await student.GetFromJsonAsync<StaffSessionDto>($"/api/ai/speaking/sessions/{practice.Id}"))!;
        Assert.Equal(7.0m, own.TeacherReview!.Band);
        Assert.False(own.CanOverride);
        Assert.Equal(7.0m, (await student.GetFromJsonAsync<List<SummaryDto>>("/api/ai/speaking/sessions"))!.Single(s => s.Id == practice.Id).TeacherBand);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Notifications.CountAsync(n => n.UserId == c.First.UserId && n.Type == "SPEAKING_REVIEWED"));
        Assert.Contains(await db.StudentTimelineEvents.Where(e => e.StudentId == c.First.Id).Select(e => e.EventType).ToListAsync(), e => e == "SPEAKING_REVIEWED");
        var audit = await db.AuditLogs.SingleAsync(l => l.ResourceId == practice.Id && l.Action == "Modified");
        Assert.Equal("SpeakingSession", audit.Resource);
        Assert.DoesNotContain("Talaffuz", audit.OldValueJson ?? "");
    }

    [Fact]
    public async Task TheOverrideIsValidated_AndOnlyReachesSessionsStaffMayRead()
    {
        var c = await world.CreateAsync();
        var student = await world.ClientAsync(c.First.Email);
        var teacher = await world.ClientAsync("teacher@demo-center.test");
        var practice = await PractiseAsync(student);
        var running = await StartAsync(student);
        var url = $"/api/students/{c.First.Id}/speaking/sessions";

        foreach (var invalid in new object[]
                 {
                     new { band = 6.3 }, new { band = 9.5 }, new { band = -0.5 }, new { band = 6.0, fluency = 6.25 },
                     new { band = 6.0, comment = new string('x', 2001) },
                 })
            Assert.Equal(HttpStatusCode.BadRequest, (await teacher.PostAsJsonAsync($"{url}/{practice.Id}/override", invalid)).StatusCode);

        // A session still running, or one that never existed, is not something a teacher can grade.
        Assert.Equal(HttpStatusCode.NotFound, (await teacher.PostAsJsonAsync($"{url}/{running}/override", new { band = 6.0 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await teacher.PostAsJsonAsync($"{url}/{Guid.NewGuid()}/override", new { band = 6.0 })).StatusCode);

        // And one recorded before the notice stays private even to a teacher who knows its id.
        await MakePrivateAsync(practice.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await teacher.PostAsJsonAsync($"{url}/{practice.Id}/override", new { band = 6.0 })).StatusCode);

        // Nothing above was saved.
        Assert.Null((await student.GetFromJsonAsync<StaffSessionDto>($"/api/ai/speaking/sessions/{practice.Id}"))!.TeacherReview);
    }

    [Fact]
    public async Task OnlyThoseWhoHoldTheOverridePermissionCorrectABand_TheSupportTeacherReadsButDoesNot()
    {
        var mine = await world.CreateAsync();
        var notMine = await world.CreateAsync(teacherStaffsGroup: false);
        var practice = await PractiseAsync(await world.ClientAsync(mine.First.Email));
        var url = $"/api/students/{mine.First.Id}/speaking/sessions/{practice.Id}";
        var superAdmin = await world.ClientAsync("superadmin@demo-center.test");
        var supportId = await world.UserIdAsync("support@demo-center.test", "SUPPORT_TEACHER");
        Assert.Equal(HttpStatusCode.NoContent,
            (await superAdmin.PostAsJsonAsync($"/api/groups/{mine.GroupId}/staff", new { userId = supportId, role = "SupportTeacher" })).StatusCode);

        // The support teacher opens the session — with no form to correct it, and the server refuses if they try.
        var support = await world.ClientAsync("support@demo-center.test");
        Assert.False((await support.GetFromJsonAsync<StaffSessionDto>(url))!.CanOverride);
        Assert.Equal(HttpStatusCode.Forbidden, (await support.PostAsJsonAsync($"{url}/override", new { band = 6.0 })).StatusCode);

        // Everyone else without the permission or without the student in reach.
        foreach (var email in new[] { mine.First.Email, "operator@demo-center.test", "parent@demo-center.test" })
            Assert.Equal(HttpStatusCode.Forbidden, (await (await world.ClientAsync(email)).PostAsJsonAsync($"{url}/override", new { band = 6.0 })).StatusCode);
        var teacher = await world.ClientAsync("teacher@demo-center.test");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await teacher.PostAsJsonAsync($"/api/students/{notMine.First.Id}/speaking/sessions/{practice.Id}/override", new { band = 6.0 })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PostAsJsonAsync($"{url}/override", new { band = 6.0 })).StatusCode);

        // The branch Administrator and the org's Super Admin may.
        Assert.Equal(HttpStatusCode.OK, (await (await world.ClientAsync("admin@demo-center.test")).PostAsJsonAsync($"{url}/override", new { band = 6.5 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await superAdmin.PostAsJsonAsync($"{url}/override", new { band = 7.0 })).StatusCode);
        Assert.Equal(7.0m, (await superAdmin.GetFromJsonAsync<StaffSessionDto>(url))!.TeacherReview!.Band); // the latest review stands
    }

    [Fact]
    public async Task TheTeachersInbox_ListsTheirStudentsFinishedPractice_AndPendingOnlyLeavesOutWhatIsAlreadyReviewed()
    {
        var mine = await world.CreateAsync(students: 2);
        var notMine = await world.CreateAsync(teacherStaffsGroup: false);
        var first = await PractiseAsync(await world.ClientAsync(mine.Students[0].Email));
        var second = await PractiseAsync(await world.ClientAsync(mine.Students[1].Email));
        var stranger = await PractiseAsync(await world.ClientAsync(notMine.First.Email));
        var teacher = await world.ClientAsync("teacher@demo-center.test");

        var all = (await teacher.GetFromJsonAsync<List<InboxRowDto>>("/api/ai/speaking/reviews"))!;
        Assert.Contains(all, r => r.Id == first.Id && r.StudentId == mine.Students[0].Id && r.StudentName == "Lms Student");
        Assert.Contains(all, r => r.Id == second.Id);
        Assert.DoesNotContain(all, r => r.Id == stranger.Id); // a student of a group they do not teach

        Assert.Equal(HttpStatusCode.OK,
            (await teacher.PostAsJsonAsync($"/api/students/{mine.Students[0].Id}/speaking/sessions/{first.Id}/override", new { band = 6.5 })).StatusCode);
        var pending = (await teacher.GetFromJsonAsync<List<InboxRowDto>>("/api/ai/speaking/reviews?pendingOnly=true"))!;
        Assert.DoesNotContain(pending, r => r.Id == first.Id);
        Assert.Contains(pending, r => r.Id == second.Id);
        Assert.Equal(6.5m, (await teacher.GetFromJsonAsync<List<InboxRowDto>>("/api/ai/speaking/reviews"))!.Single(r => r.Id == first.Id).TeacherBand);

        // One student's practice, and no way in for those without the permission.
        var onlyOne = (await teacher.GetFromJsonAsync<List<InboxRowDto>>($"/api/ai/speaking/reviews?studentId={mine.Students[1].Id}"))!;
        Assert.Equal([second.Id], onlyOne.Select(r => r.Id));
        foreach (var email in new[] { mine.Students[0].Email, "operator@demo-center.test", "parent@demo-center.test" })
            Assert.Equal(HttpStatusCode.Forbidden, (await (await world.ClientAsync(email)).GetAsync("/api/ai/speaking/reviews")).StatusCode);
    }

    [Fact]
    public async Task TheTeachersDashboardCountsPracticeNobodyHasReviewed_UntilTheyDo()
    {
        var c = await world.CreateAsync();
        var teacher = await world.ClientAsync("teacher@demo-center.test");
        var before = (await teacher.GetFromJsonAsync<DashboardDto>("/api/dashboard/teacher"))!.PendingSpeakingReviews;

        var practice = await PractiseAsync(await world.ClientAsync(c.First.Email));
        Assert.Equal(before + 1, (await teacher.GetFromJsonAsync<DashboardDto>("/api/dashboard/teacher"))!.PendingSpeakingReviews);

        Assert.Equal(HttpStatusCode.OK,
            (await teacher.PostAsJsonAsync($"/api/students/{c.First.Id}/speaking/sessions/{practice.Id}/override", new { band = 6.0 })).StatusCode);
        Assert.Equal(before, (await teacher.GetFromJsonAsync<DashboardDto>("/api/dashboard/teacher"))!.PendingSpeakingReviews);
    }

    // ---- helpers ------------------------------------------------------------------------------------

    private static async Task<Guid> StartAsync(HttpClient student)
    {
        var started = await student.PostAsJsonAsync("/api/ai/speaking/sessions", new { mode = "IeltsPart1" });
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        return (await started.Content.ReadFromJsonAsync<SessionDto>())!.Id;
    }

    /// <summary>A whole practice as the student does it: start, answer once with a recording, finish.</summary>
    private static async Task<SessionDto> PractiseAsync(HttpClient student)
    {
        var id = await StartAsync(student);

        var form = new MultipartFormDataContent();
        var audio = new ByteArrayContent(TestFiles.Webm(2048));
        audio.Headers.ContentType = new MediaTypeHeaderValue("audio/webm");
        form.Add(audio, "audio", "answer.webm");
        form.Add(new StringContent("32"), "durationSeconds");
        Assert.Equal(HttpStatusCode.OK, (await student.PostAsync($"/api/ai/speaking/sessions/{id}/turns", form)).StatusCode);

        var finished = await student.PostAsync($"/api/ai/speaking/sessions/{id}/finish", null);
        Assert.Equal(HttpStatusCode.OK, finished.StatusCode);
        var session = (await finished.Content.ReadFromJsonAsync<SessionDto>())!;
        Assert.Equal("Completed", session.Status);
        return session;
    }

    /// <summary>What every session recorded before this feature looks like: there is no API for it, by design.</summary>
    private async Task MakePrivateAsync(Guid sessionId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE speaking_sessions SET \"SharedWithStaff\" = false WHERE \"Id\" = {sessionId}"));
    }

    private record TeacherReviewDto(decimal? Band, decimal? FluencyBand, decimal? LexicalBand, decimal? GrammarBand, string? Comment, string? ByName);
    private record StaffSessionDto(Guid Id, decimal? EstimatedBand, decimal? FluencyBand, TeacherReviewDto? TeacherReview, bool CanOverride);
    private record SummaryDto(Guid Id, decimal? TeacherBand);
    private record DashboardDto(int PendingSpeakingReviews);
    private record InboxRowDto(Guid Id, Guid StudentId, string StudentName, decimal? TeacherBand);
    private record TurnDto(Guid Id, string? Transcript, bool HasAudio);
    private record SessionDto(Guid Id, string Status, decimal? EstimatedBand, decimal? FluencyBand, decimal? LexicalBand, decimal? GrammarBand,
        List<TurnDto> Turns, object? Assessment);
    private record RowDto(Guid Id, decimal? EstimatedBand, decimal? FluencyBand, decimal? LexicalBand, decimal? GrammarBand);
}
