using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduCenter.Api.Automation;
using EduCenter.Infrastructure;
using EduCenter.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// A lesson can be moved, given another room or teacher, cancelled — alone or many at once — and deleted while it is still
/// clean (docs/21.09-audit-jurnali S02.02). A cancelled lesson stays on the schedule, marked, with its attendance and
/// homework, but is not "held" anywhere: no clash, no reminder, no unmarked register, no share of an attendance rate.
/// </summary>
[Collection("Api")]
public class ScheduleEditTests(EduCenterWebApplicationFactory factory)
{
    [Fact]
    public async Task Update_MovesTheLessonAndItsRoom_AndDoesNotClashWithItself()
    {
        var admin = await ClientAsync("admin@demo-center.test");
        var arrange = await ArrangeAsync(admin);
        var (startsAt, endsAt) = FarFutureSlot();
        var lesson = await ScheduleAsync(admin, arrange, startsAt, endsAt);

        var shifted = await admin.PatchAsJsonAsync($"/api/schedule/lessons/{lesson.Id}",
            new { startsAt = startsAt.AddMinutes(10), endsAt = endsAt.AddMinutes(10) }); // overlaps its own old window
        Assert.Equal(HttpStatusCode.OK, shifted.StatusCode);
        Assert.Equal(startsAt.AddMinutes(10), (await shifted.Content.ReadFromJsonAsync<LessonDto>())!.StartsAt);

        var moved = await admin.PatchAsJsonAsync($"/api/schedule/lessons/{lesson.Id}", new { roomName = "New room", topic = "Renamed" });
        var dto = (await moved.Content.ReadFromJsonAsync<LessonDto>())!;
        Assert.Equal("New room", dto.RoomName);
        Assert.Equal("Renamed", dto.Topic);

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchAsJsonAsync($"/api/schedule/lessons/{lesson.Id}", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PatchAsJsonAsync($"/api/schedule/lessons/{lesson.Id}", new { endsAt = startsAt })).StatusCode);
    }

    [Fact]
    public async Task Update_RefusesARoomOrTeacherThatIsBookedThen()
    {
        var admin = await ClientAsync("admin@demo-center.test");
        var first = await ArrangeAsync(admin);
        var second = await ArrangeAsync(admin);
        var (startsAt, endsAt) = FarFutureSlot();
        var firstLesson = await ScheduleAsync(admin, first, startsAt, endsAt);
        var secondLesson = await ScheduleAsync(admin, second, startsAt, endsAt);

        var room = await admin.PatchAsJsonAsync($"/api/schedule/lessons/{secondLesson.Id}", new { roomName = firstLesson.RoomName });
        Assert.Equal(HttpStatusCode.Conflict, room.StatusCode);
        Assert.Contains("ROOM", await room.Content.ReadAsStringAsync());

        var teacher = await admin.PatchAsJsonAsync($"/api/schedule/lessons/{secondLesson.Id}", new { teacherId = first.TeacherId });
        Assert.Equal(HttpStatusCode.Conflict, teacher.StatusCode);
        Assert.Contains("TEACHER", await teacher.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Update_RefusesALessonThatIsOver_AndMovingOneIntoThePast()
    {
        var admin = await ClientAsync("admin@demo-center.test");
        var arrange = await ArrangeAsync(admin);
        var (pastStart, pastEnd) = PastSlot();
        var past = await ScheduleAsync(admin, arrange, pastStart, pastEnd);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchAsJsonAsync($"/api/schedule/lessons/{past.Id}", new { topic = "Late" })).StatusCode);

        var (startsAt, endsAt) = FarFutureSlot();
        var ahead = await ScheduleAsync(admin, arrange, startsAt, endsAt);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PatchAsJsonAsync($"/api/schedule/lessons/{ahead.Id}", new { startsAt = pastStart, endsAt = pastEnd })).StatusCode);
    }

    [Fact]
    public async Task Cancel_NeedsAReason_MarksTheLesson_AndFreezesIt()
    {
        var admin = await ClientAsync("admin@demo-center.test");
        var arrange = await ArrangeAsync(admin);
        var (startsAt, endsAt) = FarFutureSlot();
        var lesson = await ScheduleAsync(admin, arrange, startsAt, endsAt);

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/schedule/lessons/{lesson.Id}/cancel", new { reason = "  " })).StatusCode);

        var cancelled = await admin.PostAsJsonAsync($"/api/schedule/lessons/{lesson.Id}/cancel", new { reason = "Bayram" });
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        var dto = (await cancelled.Content.ReadFromJsonAsync<LessonDto>())!;
        Assert.Equal("Cancelled", dto.Status);
        Assert.Equal("Bayram", dto.CancelReason);

        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/schedule/lessons/{lesson.Id}/cancel", new { reason = "Yana" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PatchAsJsonAsync($"/api/schedule/lessons/{lesson.Id}", new { topic = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync($"/api/schedule/lessons/{lesson.Id}/replace-teacher", new { teacherId = arrange.TeacherId, reason = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/lessons/{lesson.Id}/attendance", new { items = Array.Empty<object>() })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/homework", new
        {
            lessonId = lesson.Id, title = "HW", type = "Grammar", deadline = startsAt.AddDays(1), maxScore = 10,
        })).StatusCode);

        // Still on the schedule, marked.
        var listed = (await admin.GetFromJsonAsync<List<LessonDto>>(
            $"/api/schedule?from={Uri.EscapeDataString(startsAt.AddHours(-1).ToString("O"))}&to={Uri.EscapeDataString(endsAt.AddHours(1).ToString("O"))}"))!;
        Assert.Contains(listed, l => l.Id == lesson.Id && l.Status == "Cancelled" && l.CancelReason == "Bayram");
    }

    [Fact]
    public async Task ACancelledLesson_TakesNoPartInConflicts_SoItsRoomAndTeacherAreFreeAgain()
    {
        var admin = await ClientAsync("admin@demo-center.test");
        var arrange = await ArrangeAsync(admin);
        var (startsAt, endsAt) = FarFutureSlot();
        var lesson = await ScheduleAsync(admin, arrange, startsAt, endsAt);
        var clash = await admin.PostAsJsonAsync("/api/schedule/lessons", NewLesson(arrange, startsAt, endsAt, lesson.RoomName));
        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/schedule/lessons/{lesson.Id}/cancel", new { reason = "Kasal" })).StatusCode);

        var again = await admin.PostAsJsonAsync("/api/schedule/lessons", NewLesson(arrange, startsAt, endsAt, lesson.RoomName));
        Assert.Equal(HttpStatusCode.Created, again.StatusCode); // same room, teacher and group, same hour
    }

    [Fact]
    public async Task Delete_ErasesACleanLesson_ButNotOneThatHasAttendance()
    {
        var admin = await ClientAsync("admin@demo-center.test");
        var arrange = await ArrangeAsync(admin);
        var (startsAt, endsAt) = FarFutureSlot();
        var clean = await ScheduleAsync(admin, arrange, startsAt, endsAt);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/schedule/lessons/{clean.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/schedule/lessons/{clean.Id}")).StatusCode);

        var (pastStart, pastEnd) = PastSlot();
        var held = await ScheduleAsync(admin, arrange, pastStart, pastEnd);
        await MarkPresentAsync(admin, held.Id, arrange.StudentId);
        var refused = await admin.DeleteAsync($"/api/schedule/lessons/{held.Id}");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var body = await refused.Content.ReadAsStringAsync();
        Assert.Contains("davomat", body);
        Assert.Contains("\"canCancel\":true", body);
    }

    [Fact]
    public async Task MovingAndCancelling_TellTheStudent_TheirParentAndTheTeacher()
    {
        var admin = await ClientAsync("admin@demo-center.test");
        var arrange = await ArrangeAsync(admin);
        var parentEmail = $"sched-parent-{Guid.NewGuid():N}@demo-center.test";
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync($"/api/students/{arrange.StudentId}/parents", new
        {
            fullName = "Schedule Parent", phone = "+998901112233", email = parentEmail, password = DbSeeder.DemoPassword, relationship = "Father",
        })).StatusCode);
        var (startsAt, endsAt) = FarFutureSlot();
        var lesson = await ScheduleAsync(admin, arrange, startsAt, endsAt);

        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/schedule/lessons/{lesson.Id}", new { roomName = "Moved room" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/schedule/lessons/{lesson.Id}/cancel", new { reason = "Filial yopiq" })).StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var studentUser = await db.Students.Where(s => s.Id == arrange.StudentId).Select(s => s.UserId).SingleAsync();
        var parentUser = await db.Users.Where(u => u.Email == parentEmail).Select(u => u.Id).SingleAsync();
        foreach (var user in new[] { studentUser, parentUser, arrange.TeacherId })
        {
            var inbox = await db.Notifications.Where(n => n.UserId == user && n.Type == "SCHEDULE_CHANGED").Select(n => n.Body).ToListAsync();
            Assert.Contains(inbox, b => b.Contains("Moved room"));
            Assert.Contains(inbox, b => b.Contains("bekor qilindi") && b.Contains("Filial yopiq"));
        }
    }

    [Fact]
    public async Task EveryEndpoint_IsForbidden_ToATeacherAStudentAndAnAdministratorOfAnotherBranch()
    {
        var superAdmin = await ClientAsync("superadmin@demo-center.test");
        var admin = await ClientAsync("admin@demo-center.test");
        var arrange = await ArrangeAsync(admin);
        var (startsAt, endsAt) = FarFutureSlot();
        var lesson = await ScheduleAsync(admin, arrange, startsAt, endsAt);
        var elsewhere = await LessonInOtherBranchAsync(superAdmin, admin);

        foreach (var email in new[] { "teacher@demo-center.test", "student@demo-center.test" })
            await AssertForbiddenAsync(await ClientAsync(email), lesson.Id);
        await AssertForbiddenAsync(admin, elsewhere);

        // ...and nothing was touched.
        var listed = (await superAdmin.GetFromJsonAsync<List<LessonDto>>(
            $"/api/schedule?from={Uri.EscapeDataString(startsAt.AddHours(-1).ToString("O"))}&to={Uri.EscapeDataString(endsAt.AddHours(1).ToString("O"))}"))!;
        Assert.All(listed.Where(l => l.Id == lesson.Id || l.Id == elsewhere), l => Assert.Equal("Scheduled", l.Status));
    }

    [Fact]
    public async Task BulkCancel_CancelsAllOrNothing_AndSkipsLessonsAlreadyCancelled()
    {
        var superAdmin = await ClientAsync("superadmin@demo-center.test");
        var admin = await ClientAsync("admin@demo-center.test");
        var arrange = await ArrangeAsync(admin);
        var (startsAt, endsAt) = FarFutureSlot();
        var one = await ScheduleAsync(admin, arrange, startsAt, endsAt);
        var two = await ScheduleAsync(admin, arrange, startsAt.AddDays(1), endsAt.AddDays(1));
        var elsewhere = await LessonInOtherBranchAsync(superAdmin, admin);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.PostAsJsonAsync("/api/schedule/lessons/bulk-cancel", new { lessonIds = new[] { one.Id, elsewhere }, reason = "Bayram" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync("/api/schedule/lessons/bulk-cancel", new { lessonIds = new[] { one.Id }, reason = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync("/api/schedule/lessons/bulk-cancel", new { lessonIds = Array.Empty<Guid>(), reason = "Bayram" })).StatusCode);
        Assert.Equal("Scheduled", (await StatusOfAsync(admin, one, startsAt)));

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/schedule/lessons/{one.Id}/cancel", new { reason = "Avval" })).StatusCode);
        var response = await admin.PostAsJsonAsync("/api/schedule/lessons/bulk-cancel", new { lessonIds = new[] { one.Id, two.Id }, reason = "Bayram" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"cancelled\":1", body);
        Assert.Contains("\"alreadyCancelled\":1", body);
        Assert.Equal("Cancelled", await StatusOfAsync(admin, two, startsAt.AddDays(1)));
    }

    [Fact]
    public async Task ACancelledLesson_IsNotHeld_NotUnmarked_NotAnAbsence_AndGetsNoReminder()
    {
        var admin = await ClientAsync("admin@demo-center.test");
        var arrange = await ArrangeAsync(admin, teacherRole: "TEACHER");
        var teacher = await ClientAsync(arrange.TeacherEmail);

        // A finished lesson, attendance marked: it is held, counted, and (with a second one unmarked) shows up as unmarked.
        var (pastStart, pastEnd) = PastSlot();
        var marked = await ScheduleAsync(admin, arrange, pastStart, pastEnd);
        await MarkPresentAsync(admin, marked.Id, arrange.StudentId);
        var (unmarkedStart, unmarkedEnd) = PastSlot(daysAgo: 3);
        var unmarked = await ScheduleAsync(admin, arrange, unmarkedStart, unmarkedEnd);

        var before = (await teacher.GetFromJsonAsync<TeacherDashboardDto>("/api/dashboard/teacher"))!;
        Assert.Equal(1, before.UnmarkedLessons);
        var overview = (await admin.GetFromJsonAsync<OverviewDto>($"/api/groups/{arrange.GroupId}/overview"))!;
        Assert.Equal(2, overview.Results!.LessonsHeld);
        Assert.Equal(100, overview.Results.AttendanceRate);

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/schedule/lessons/{unmarked.Id}/cancel", new { reason = "Kelmadi" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/schedule/lessons/{marked.Id}/cancel", new { reason = "Xato" })).StatusCode);

        var after = (await teacher.GetFromJsonAsync<TeacherDashboardDto>("/api/dashboard/teacher"))!;
        Assert.Equal(0, after.UnmarkedLessons);
        var afterOverview = (await admin.GetFromJsonAsync<OverviewDto>($"/api/groups/{arrange.GroupId}/overview"))!;
        Assert.Equal(0, afterOverview.Results!.LessonsHeld);
        Assert.Equal(0, afterOverview.Results.AttendanceRate); // the cancelled lesson's mark no longer counts
        Assert.Empty(afterOverview.UpcomingLessons);

        // A reminder goes out for a lesson that still stands, and not for the cancelled one of the same hour.
        var (startsAt, endsAt) = FarFutureSlot();
        var standing = await ScheduleAsync(admin, arrange, startsAt, endsAt);
        var other = await ArrangeAsync(admin);
        var doomed = await ScheduleAsync(admin, other, startsAt, endsAt);
        await admin.PostAsJsonAsync($"/api/schedule/lessons/{doomed.Id}/cancel", new { reason = "Bayram" });
        using var scope = factory.Services.CreateScope();
        // Lesson reminders sweep the whole organization; they are skipped when the run is narrowed to one student.
        await scope.ServiceProvider.GetRequiredService<NotificationRules>().RunAsync(startsAt.AddHours(-1));
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reminders = await db.Notifications.Where(n => n.Type == "LESSON_REMINDER" && n.Link == $"/schedule/{standing.Id}").CountAsync();
        var cancelledReminders = await db.Notifications.Where(n => n.Type == "LESSON_REMINDER" && n.Link == $"/schedule/{doomed.Id}").CountAsync();
        Assert.True(reminders > 0, "a lesson that still stands is reminded about");
        Assert.Equal(0, cancelledReminders);
    }

    [Fact]
    public async Task ClosingTheGroup_CancelsItsFutureLessons_AndLeavesThePastOnesAlone()
    {
        var admin = await ClientAsync("admin@demo-center.test");
        var arrange = await ArrangeAsync(admin);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/groups/{arrange.GroupId}/status", new { status = "Active" })).StatusCode);
        var (pastStart, pastEnd) = PastSlot();
        var past = await ScheduleAsync(admin, arrange, pastStart, pastEnd);
        var (startsAt, endsAt) = FarFutureSlot();
        var ahead = await ScheduleAsync(admin, arrange, startsAt, endsAt);

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/groups/{arrange.GroupId}/status", new { status = "Completed" })).StatusCode);

        Assert.Equal("Cancelled", await StatusOfAsync(admin, ahead, startsAt));
        Assert.Equal("Scheduled", await StatusOfAsync(admin, past, pastStart));
        using var scope = factory.Services.CreateScope();
        var reason = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Lessons
            .Where(l => l.Id == ahead.Id).Select(l => l.CancelReason).SingleAsync();
        Assert.Equal("Guruh yopildi", reason);
    }

    // ---- helpers -------------------------------------------------------------------------------

    private record Arrangement(Guid BranchId, Guid GroupId, Guid TeacherId, string TeacherEmail, Guid StudentId);

    /// <summary>A fresh group with its own teacher (main) and one enrolled student — so nothing here collides with another test.
    /// The teacher is a support teacher unless the test needs the teacher dashboard: other tests find the demo teacher on the first
    /// page of <c>/api/users?role=TEACHER</c>, and a dozen more accounts of that role would push it off.</summary>
    private async Task<Arrangement> ArrangeAsync(HttpClient admin, string teacherRole = "SUPPORT_TEACHER")
    {
        var superAdmin = await ClientAsync("superadmin@demo-center.test");
        var branchId = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var group = await CreateGroupAsync(admin);

        var teacherEmail = $"sched-teacher-{Guid.NewGuid():N}@demo-center.test";
        var teacher = await superAdmin.PostAsJsonAsync("/api/users", new
        {
            fullName = $"Schedule Teacher {Guid.NewGuid():N}", email = teacherEmail, phone = "+998900000095",
            password = DbSeeder.DemoPassword, roleCode = teacherRole, branchIds = new[] { branchId },
        });
        Assert.Equal(HttpStatusCode.Created, teacher.StatusCode);
        var teacherId = (await teacher.Content.ReadFromJsonAsync<IdDto>())!.Id;
        Assert.True((await admin.PostAsJsonAsync($"/api/groups/{group}/staff", new { userId = teacherId, role = "MainTeacher" })).IsSuccessStatusCode);

        var student = await admin.PostAsJsonAsync("/api/students", new
        {
            fullName = "Schedule Student", phone = "+998900000094", email = $"sched-student-{Guid.NewGuid():N}@demo-center.test",
            password = DbSeeder.DemoPassword, groupId = group,
        });
        Assert.Equal(HttpStatusCode.Created, student.StatusCode);
        return new Arrangement(branchId, group, teacherId, teacherEmail, (await student.Content.ReadFromJsonAsync<IdDto>())!.Id);
    }

    private static async Task<Guid> CreateGroupAsync(HttpClient client, Guid? branchId = null)
    {
        var course = await client.PostAsJsonAsync("/api/courses", new { name = $"Sched Course {Guid.NewGuid():N}"[..25], levelCategory = "IELTS" });
        Assert.Equal(HttpStatusCode.Created, course.StatusCode);
        var branch = branchId ?? (await client.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var response = await client.PostAsJsonAsync("/api/groups", new
        {
            branchId = branch, courseId = (await course.Content.ReadFromJsonAsync<IdDto>())!.Id, name = $"SE-{Guid.NewGuid():N}", capacity = 10,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    private static object NewLesson(Arrangement a, DateTimeOffset startsAt, DateTimeOffset endsAt, string? room = null) => new
    {
        branchId = a.BranchId, groupId = a.GroupId, teacherId = a.TeacherId, roomName = room ?? $"R-{Guid.NewGuid():N}"[..8],
        startsAt, endsAt, topic = "Test lesson",
    };

    private static async Task<LessonDto> ScheduleAsync(HttpClient admin, Arrangement a, DateTimeOffset startsAt, DateTimeOffset endsAt)
    {
        var response = await admin.PostAsJsonAsync("/api/schedule/lessons", NewLesson(a, startsAt, endsAt));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LessonDto>())!;
    }

    private static async Task MarkPresentAsync(HttpClient admin, Guid lessonId, Guid studentId) =>
        Assert.True((await admin.PostAsJsonAsync($"/api/lessons/{lessonId}/attendance",
            new { items = new[] { new { studentId, status = "Present" } } })).IsSuccessStatusCode);

    private static async Task<string> StatusOfAsync(HttpClient client, LessonDto lesson, DateTimeOffset startsAt) =>
        (await client.GetFromJsonAsync<List<LessonDto>>(
            $"/api/schedule?from={Uri.EscapeDataString(startsAt.AddHours(-1).ToString("O"))}&to={Uri.EscapeDataString(startsAt.AddHours(2).ToString("O"))}"))!
        .Single(l => l.Id == lesson.Id).Status;

    private async Task<Guid> LessonInOtherBranchAsync(HttpClient superAdmin, HttpClient admin)
    {
        var mine = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))!.Select(b => b.Id).ToHashSet();
        var other = (await superAdmin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))!.First(b => !mine.Contains(b.Id)).Id;
        var group = await CreateGroupAsync(superAdmin, other);
        var teacher = (await (await superAdmin.PostAsJsonAsync("/api/users", new
        {
            fullName = $"Other Teacher {Guid.NewGuid():N}", email = $"sched-other-{Guid.NewGuid():N}@demo-center.test", phone = "+998900000093",
            password = DbSeeder.DemoPassword, roleCode = "SUPPORT_TEACHER", branchIds = new[] { other },
        })).Content.ReadFromJsonAsync<IdDto>())!.Id;
        var (startsAt, endsAt) = FarFutureSlot();
        var response = await superAdmin.PostAsJsonAsync("/api/schedule/lessons", new
        {
            branchId = other, groupId = group, teacherId = teacher, roomName = $"R-{Guid.NewGuid():N}"[..8], startsAt, endsAt, topic = "Elsewhere",
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LessonDto>())!.Id;
    }

    private static async Task AssertForbiddenAsync(HttpClient client, Guid lessonId)
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PatchAsJsonAsync($"/api/schedule/lessons/{lessonId}", new { topic = "Nope" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/schedule/lessons/{lessonId}/cancel", new { reason = "Nope" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/schedule/lessons/{lessonId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/schedule/lessons/bulk-cancel", new { lessonIds = new[] { lessonId }, reason = "Nope" })).StatusCode);
    }

    /// <summary>A unique far-future slot — the suite shares one database across the whole run.</summary>
    private static (DateTimeOffset StartsAt, DateTimeOffset EndsAt) FarFutureSlot()
    {
        var startsAt = DateTimeOffset.UtcNow.Date.AddDays(Random.Shared.Next(400, 4000)).AddMinutes(Random.Shared.Next(0, 1440));
        return (startsAt, startsAt.AddMinutes(60));
    }

    /// <summary>A finished lesson a day or two back — inside the 14 days the teacher's "unmarked register" looks back over. Use a
    /// different <paramref name="daysAgo"/> for a second lesson of the same teacher or group, so the two never overlap.</summary>
    private static (DateTimeOffset StartsAt, DateTimeOffset EndsAt) PastSlot(int daysAgo = 1)
    {
        var startsAt = DateTimeOffset.UtcNow.AddDays(-daysAgo).AddMinutes(-Random.Shared.Next(0, 600));
        return (startsAt, startsAt.AddMinutes(60));
    }

    /// <summary>An account created through the API starts with the forced password change (S01.05).</summary>
    private async Task<HttpClient> ClientAsync(string email)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = DbSeeder.DemoPassword });
        var tokens = (await response.Content.ReadFromJsonAsync<TokenResponseDto>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        if (tokens.MustChangePassword)
        {
            var change = await client.PostAsJsonAsync("/api/auth/change-password",
                new { currentPassword = DbSeeder.DemoPassword, newPassword = "Chosen-Pass-42" });
            Assert.Equal(HttpStatusCode.OK, change.StatusCode);
            tokens = (await change.Content.ReadFromJsonAsync<TokenResponseDto>())!;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        }
        return client;
    }

    private record TokenResponseDto(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, bool MustChangePassword);
    private record IdDto(Guid Id);
    private record BranchDto(Guid Id, string Name);
    private record LessonDto(Guid Id, Guid GroupId, Guid TeacherId, string RoomName, DateTimeOffset StartsAt, DateTimeOffset EndsAt,
        string? Topic, string? TeacherName, string Status, string? CancelReason);
    private record TeacherDashboardDto(int UnmarkedLessons);
    private record UpcomingDto(Guid Id);
    private record ResultsDto(double AttendanceRate, int LessonsHeld);
    private record OverviewDto(ResultsDto? Results, List<UpcomingDto> UpcomingLessons);
}
