using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduCenter.Infrastructure.Seed;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// Teacher Replacement (docs/00_TZ.md §19, audit §3.12/§5.10) and the Room datalist (audit §3.11/§5.9).
/// Substitutes are drawn from staff already teaching in the same branch who are free for the lesson's
/// window; replacing checks the stand-in's own calendar first, so a replacement can never double-book
/// the substitute the way an unchecked one would.
/// </summary>
[Collection("Api")]
public class TeacherReplacementTests(EduCenterWebApplicationFactory factory)
{
    [Fact]
    public async Task Substitutes_ExcludesBusyTeacher_IncludesFreeGroupStaff()
    {
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var branchId = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var course = await CreateCourseAsync(admin);

        var teacherA = await CreateTeacherAsync(superAdmin, branchId);
        var teacherB = await CreateTeacherAsync(superAdmin, branchId);
        var groupA = await CreateGroupAsync(admin, branchId, course);
        var groupB = await CreateGroupAsync(admin, branchId, course);
        await AssignMainTeacherAsync(admin, groupA, teacherA);
        await AssignMainTeacherAsync(admin, groupB, teacherB);

        var (startsAt, endsAt) = FarFutureSlot();
        var lesson = await ScheduleLessonAsync(admin, branchId, groupA, teacherA, startsAt, endsAt);

        var substitutes = await admin.GetFromJsonAsync<List<SubstituteDto>>($"/api/schedule/lessons/{lesson.Id}/substitutes");

        Assert.Contains(substitutes!, s => s.UserId == teacherB);
        Assert.DoesNotContain(substitutes!, s => s.UserId == teacherA);
    }

    [Fact]
    public async Task ReplaceTeacher_HappyPath_UpdatesLesson()
    {
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var branchId = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var course = await CreateCourseAsync(admin);

        var teacherA = await CreateTeacherAsync(superAdmin, branchId);
        var teacherB = await CreateTeacherAsync(superAdmin, branchId);
        var group = await CreateGroupAsync(admin, branchId, course);
        await AssignMainTeacherAsync(admin, group, teacherA);

        var (startsAt, endsAt) = FarFutureSlot();
        var lesson = await ScheduleLessonAsync(admin, branchId, group, teacherA, startsAt, endsAt);

        var response = await admin.PostAsJsonAsync($"/api/schedule/lessons/{lesson.Id}/replace-teacher",
            new { teacherId = teacherB, reason = "Kasal bo'lib qoldi" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await response.Content.ReadFromJsonAsync<LessonDto>();
        Assert.Equal(teacherB, updated!.TeacherId);
    }

    [Fact]
    public async Task ReplaceTeacher_WhenSubstituteAlreadyBusy_ReturnsConflict()
    {
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var branchId = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var course = await CreateCourseAsync(admin);

        var teacherA = await CreateTeacherAsync(superAdmin, branchId);
        var teacherB = await CreateTeacherAsync(superAdmin, branchId);
        var groupA = await CreateGroupAsync(admin, branchId, course);
        var groupB = await CreateGroupAsync(admin, branchId, course);
        await AssignMainTeacherAsync(admin, groupA, teacherA);
        await AssignMainTeacherAsync(admin, groupB, teacherB);

        var (startsAt, endsAt) = FarFutureSlot();
        var lessonA = await ScheduleLessonAsync(admin, branchId, groupA, teacherA, startsAt, endsAt);
        await ScheduleLessonAsync(admin, branchId, groupB, teacherB, startsAt, endsAt); // teacherB already booked then

        var response = await admin.PostAsJsonAsync($"/api/schedule/lessons/{lessonA.Id}/replace-teacher",
            new { teacherId = teacherB, reason = "Kasal bo'lib qoldi" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ReplaceTeacher_PastLesson_ReturnsBadRequest()
    {
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var branchId = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var course = await CreateCourseAsync(admin);

        var teacherA = await CreateTeacherAsync(superAdmin, branchId);
        var teacherB = await CreateTeacherAsync(superAdmin, branchId);
        var group = await CreateGroupAsync(admin, branchId, course);
        await AssignMainTeacherAsync(admin, group, teacherA);

        var startsAt = DateTimeOffset.UtcNow.Date.AddDays(-Random.Shared.Next(400, 4000));
        var lesson = await ScheduleLessonAsync(admin, branchId, group, teacherA, startsAt, startsAt.AddMinutes(60));

        var response = await admin.PostAsJsonAsync($"/api/schedule/lessons/{lesson.Id}/replace-teacher",
            new { teacherId = teacherB, reason = "Kasal bo'lib qoldi" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReplaceTeacher_WithoutReason_ReturnsBadRequest()
    {
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var branchId = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var course = await CreateCourseAsync(admin);

        var teacherA = await CreateTeacherAsync(superAdmin, branchId);
        var teacherB = await CreateTeacherAsync(superAdmin, branchId);
        var group = await CreateGroupAsync(admin, branchId, course);
        await AssignMainTeacherAsync(admin, group, teacherA);

        var (startsAt, endsAt) = FarFutureSlot();
        var lesson = await ScheduleLessonAsync(admin, branchId, group, teacherA, startsAt, endsAt);

        var response = await admin.PostAsJsonAsync($"/api/schedule/lessons/{lesson.Id}/replace-teacher",
            new { teacherId = teacherB, reason = " " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReplaceTeacher_AsTeacher_Forbidden()
    {
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var teacherClient = await AuthenticatedClientAsync("teacher@demo-center.test");
        var branchId = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var course = await CreateCourseAsync(admin);

        var teacherA = await CreateTeacherAsync(superAdmin, branchId);
        var teacherB = await CreateTeacherAsync(superAdmin, branchId);
        var group = await CreateGroupAsync(admin, branchId, course);
        await AssignMainTeacherAsync(admin, group, teacherA);

        var (startsAt, endsAt) = FarFutureSlot();
        var lesson = await ScheduleLessonAsync(admin, branchId, group, teacherA, startsAt, endsAt);

        var substitutes = await teacherClient.GetAsync($"/api/schedule/lessons/{lesson.Id}/substitutes");
        var replace = await teacherClient.PostAsJsonAsync($"/api/schedule/lessons/{lesson.Id}/replace-teacher",
            new { teacherId = teacherB, reason = "Urinish" });

        Assert.Equal(HttpStatusCode.Forbidden, substitutes.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, replace.StatusCode);
    }

    [Fact]
    public async Task Rooms_ReturnsDistinctRoomNamesForBranch()
    {
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var branchId = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var course = await CreateCourseAsync(admin);
        var teacher = await CreateTeacherAsync(superAdmin, branchId);
        var group = await CreateGroupAsync(admin, branchId, course);
        await AssignMainTeacherAsync(admin, group, teacher);

        var roomName = $"R-{Guid.NewGuid():N}"[..8];
        var (startsAt, endsAt) = FarFutureSlot();
        await ScheduleLessonAsync(admin, branchId, group, teacher, startsAt, endsAt, roomName);
        await ScheduleLessonAsync(admin, branchId, group, teacher, startsAt.AddDays(1), endsAt.AddDays(1), roomName); // same room again

        var rooms = await admin.GetFromJsonAsync<List<string>>($"/api/schedule/rooms?branchId={branchId}");

        Assert.Single(rooms!, r => r == roomName);
    }

    [Fact]
    public async Task Rooms_Administrator_CannotQueryAnotherBranch()
    {
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var adminBranch = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var otherBranch = (await superAdmin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))!
            .First(b => b.Id != adminBranch).Id;

        var response = await admin.GetAsync($"/api/schedule/rooms?branchId={otherBranch}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static (DateTimeOffset StartsAt, DateTimeOffset EndsAt) FarFutureSlot()
    {
        // Unique far-future slot — the suite shares a persistent dev DB with earlier runs.
        var startsAt = DateTimeOffset.UtcNow.Date.AddDays(Random.Shared.Next(400, 4000)).AddMinutes(Random.Shared.Next(0, 1440));
        return (startsAt, startsAt.AddMinutes(60));
    }

    private static async Task<Guid> CreateCourseAsync(HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync("/api/courses",
            new { name = $"Replacement Course {Guid.NewGuid():N}"[..24], levelCategory = "IELTS" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CourseDto>())!.Id;
    }

    private static async Task<Guid> CreateGroupAsync(HttpClient admin, Guid branchId, Guid courseId)
    {
        var response = await admin.PostAsJsonAsync("/api/groups",
            new { branchId, courseId, name = $"TR-{Guid.NewGuid():N}", capacity = 10 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<GroupDto>())!.Id;
    }

    private static async Task AssignMainTeacherAsync(HttpClient admin, Guid groupId, Guid teacherId)
    {
        var response = await admin.PostAsJsonAsync($"/api/groups/{groupId}/staff", new { userId = teacherId, role = "MainTeacher" });
        Assert.True(response.IsSuccessStatusCode, $"Staff assignment failed: {response.StatusCode}");
    }

    private static async Task<Guid> CreateTeacherAsync(HttpClient superAdmin, Guid branchId)
    {
        var response = await superAdmin.PostAsJsonAsync("/api/users", new
        {
            fullName = $"Substitute Teacher {Guid.NewGuid():N}",
            email = $"substitute-{Guid.NewGuid():N}@demo-center.test",
            phone = "+998900000077",
            password = DbSeeder.DemoPassword,
            roleCode = "TEACHER",
            branchIds = new[] { branchId }
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<UserDto>())!.Id;
    }

    private static async Task<LessonDto> ScheduleLessonAsync(
        HttpClient admin, Guid branchId, Guid groupId, Guid teacherId,
        DateTimeOffset startsAt, DateTimeOffset endsAt, string? roomName = null)
    {
        var response = await admin.PostAsJsonAsync("/api/schedule/lessons", new
        {
            branchId, groupId, teacherId, roomName = roomName ?? $"R-{Guid.NewGuid():N}"[..8],
            startsAt, endsAt, topic = "Test lesson"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LessonDto>())!;
    }

    private async Task<HttpClient> AuthenticatedClientAsync(string email)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = DbSeeder.DemoPassword });
        var tokens = await response.Content.ReadFromJsonAsync<TokenResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        return client;
    }

    private record TokenResponseDto(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken);
    private record BranchDto(Guid Id, string Name);
    private record CourseDto(Guid Id, string Name, string LevelCategory);
    private record GroupDto(Guid Id);
    private record UserDto(Guid Id);
    private record LessonDto(Guid Id, Guid GroupId, Guid TeacherId, string RoomName, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? Topic);
    private record SubstituteDto(Guid UserId, string FullName, string Email, bool IsMainTeacher);
}
