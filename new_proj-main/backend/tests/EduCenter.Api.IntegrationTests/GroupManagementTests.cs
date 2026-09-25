using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduCenter.Infrastructure;
using EduCenter.Infrastructure.Seed;
using EduCenter.Modules.Students.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// What an office can do with a group after creating it (docs/21.09-audit-jurnali S02.01): rename and resize it, walk it
/// Forming → Active → Completed/Cancelled, take a teacher off it and delete an empty one. Closing a group ends its
/// students' enrollments with it; deleting one that has any history is refused with the list of what stands in the way.
/// </summary>
[Collection("Api")]
public class GroupManagementTests(EduCenterWebApplicationFactory factory)
{
    [Fact]
    public async Task Update_ChangesNameAndCapacity_AndRefusesCapacityBelowTheActiveStudents()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var group = await CreateGroupAsync(admin, capacity: 5);
        await RegisterAsync(admin, group);
        await RegisterAsync(admin, group);

        var tooSmall = await admin.PatchAsJsonAsync($"/api/groups/{group}", new { capacity = 1 });
        Assert.Equal(HttpStatusCode.Conflict, tooSmall.StatusCode);
        Assert.Contains("(2)", await tooSmall.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchAsJsonAsync($"/api/groups/{group}", new { name = "   " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchAsJsonAsync($"/api/groups/{group}", new { name = new string('x', 201) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchAsJsonAsync($"/api/groups/{group}", new { capacity = 0 })).StatusCode);

        var updated = await admin.PatchAsJsonAsync($"/api/groups/{group}", new { name = "  Renamed group  ", capacity = 2 });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var dto = (await updated.Content.ReadFromJsonAsync<GroupDto>())!;
        Assert.Equal("Renamed group", dto.Name);
        Assert.Equal(2, dto.Capacity);
        Assert.Equal(2, dto.StudentCount);

        await SetStatusAsync(admin, group, "Active");
        await SetStatusAsync(admin, group, "Completed");
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PatchAsJsonAsync($"/api/groups/{group}", new { name = "Too late" })).StatusCode);
    }

    [Fact]
    public async Task Status_MovesForwardOnly_AndNothingComesBackFromAClosedGroup()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var group = await CreateGroupAsync(admin);

        Assert.Equal(HttpStatusCode.Conflict, (await PostStatusAsync(admin, group, "Completed")).StatusCode); // not started yet
        Assert.Equal(HttpStatusCode.Conflict, (await PostStatusAsync(admin, group, "Forming")).StatusCode);
        Assert.Equal("Active", (await SetStatusAsync(admin, group, "Active")).Status);
        Assert.Equal(HttpStatusCode.Conflict, (await PostStatusAsync(admin, group, "Active")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PostStatusAsync(admin, group, "Forming")).StatusCode);
        Assert.Equal("Completed", (await SetStatusAsync(admin, group, "Completed")).Status);
        foreach (var back in new[] { "Forming", "Active", "Completed", "Cancelled" })
            Assert.Equal(HttpStatusCode.Conflict, (await PostStatusAsync(admin, group, back)).StatusCode);

        var cancelled = await CreateGroupAsync(admin);
        Assert.Equal("Cancelled", (await SetStatusAsync(admin, cancelled, "Cancelled")).Status); // straight from Forming
        Assert.Equal(HttpStatusCode.Conflict, (await PostStatusAsync(admin, cancelled, "Active")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PostStatusAsync(admin, cancelled, "Cancelled")).StatusCode);
    }

    [Fact]
    public async Task Complete_EndsTheEnrollments_LeavesTheGraduatedEventAlone_AndTakesTheChatAway()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var group = await CreateGroupAsync(admin);
        await SetStatusAsync(admin, group, "Active");
        var email = NewEmail();
        var studentId = await RegisterAsync(admin, group, email);
        var student = await AuthenticatedClientAsync(email);

        Assert.Contains((await student.GetFromJsonAsync<List<ChatItemDto>>("/api/chats"))!, c => c.GroupId == group);

        var closed = await SetStatusAsync(admin, group, "Completed");
        Assert.Equal(0, closed.StudentCount);

        var overview = (await admin.GetFromJsonAsync<OverviewDto>($"/api/students/{studentId}/overview"))!;
        Assert.Equal("Completed", overview.Enrollments.Single(e => e.GroupId == group).Status);
        Assert.Equal(StudentStatus.Graduated, await StudentStatusAsync(studentId));

        var timeline = (await admin.GetFromJsonAsync<List<TimelineDto>>($"/api/students/{studentId}/timeline"))!;
        Assert.DoesNotContain(timeline, e => e.EventType == "GRADUATED");
        Assert.DoesNotContain((await student.GetFromJsonAsync<List<ChatItemDto>>("/api/chats"))!, c => c.GroupId == group);
    }

    [Fact]
    public async Task Cancel_DropsTheEnrollments_AndAStudentInAnotherGroupStaysActive()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var doomed = await CreateGroupAsync(admin);
        var other = await CreateGroupAsync(admin);
        await SetStatusAsync(admin, doomed, "Active");
        var alone = await RegisterAsync(admin, doomed);
        var both = await RegisterAsync(admin, doomed);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/students/{both}/enrollments", new { groupId = other })).StatusCode);

        var closed = await SetStatusAsync(admin, doomed, "Cancelled");
        Assert.Equal(0, closed.StudentCount);

        var aloneOverview = (await admin.GetFromJsonAsync<OverviewDto>($"/api/students/{alone}/overview"))!;
        Assert.Equal("Dropped", aloneOverview.Enrollments.Single().Status);
        Assert.Equal(StudentStatus.Dropped, await StudentStatusAsync(alone));

        var bothOverview = (await admin.GetFromJsonAsync<OverviewDto>($"/api/students/{both}/overview"))!;
        Assert.Equal("Dropped", bothOverview.Enrollments.Single(e => e.GroupId == doomed).Status);
        Assert.Equal("Active", bothOverview.Enrollments.Single(e => e.GroupId == other).Status);
        Assert.Equal(StudentStatus.Active, await StudentStatusAsync(both));
    }

    [Fact]
    public async Task RemoveStaff_RefusesTheMainTeacherAndAnyoneWithLessonsAhead_ButLetsASupportTeacherGo()
    {
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var branchId = await BranchIdAsync(admin);
        var group = await CreateGroupAsync(admin);
        var main = await CreateTeacherAsync(superAdmin, branchId, "TEACHER");
        var free = await CreateTeacherAsync(superAdmin, branchId, "SUPPORT_TEACHER");
        var busy = await CreateTeacherAsync(superAdmin, branchId, "SUPPORT_TEACHER");
        await AssignStaffAsync(admin, group, main, "MainTeacher");
        await AssignStaffAsync(admin, group, free, "SupportTeacher");
        await AssignStaffAsync(admin, group, busy, "SupportTeacher");

        var (startsAt, endsAt) = FarFutureSlot();
        var lesson = await admin.PostAsJsonAsync("/api/schedule/lessons",
            new { branchId, groupId = group, teacherId = busy, roomName = $"R-{Guid.NewGuid():N}"[..8], startsAt, endsAt, topic = "Ahead" });
        Assert.Equal(HttpStatusCode.Created, lesson.StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/groups/{group}/staff/{main}")).StatusCode);
        var withLessons = await admin.DeleteAsync($"/api/groups/{group}/staff/{busy}");
        Assert.Equal(HttpStatusCode.Conflict, withLessons.StatusCode);
        Assert.Contains("1 ta", await withLessons.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/groups/{group}/staff/{Guid.NewGuid()}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/groups/{group}/staff/{free}")).StatusCode);
        var staff = (await admin.GetFromJsonAsync<List<GroupDto>>("/api/groups"))!.Single(g => g.Id == group).Staff;
        Assert.DoesNotContain(staff, s => s.UserId == free);
        Assert.Contains(staff, s => s.UserId == busy);
        Assert.Contains(staff, s => s.UserId == main);
    }

    [Fact]
    public async Task Delete_ErasesAnEmptyGroup_WithItsStaffAndItsUnusedChat()
    {
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var group = await CreateGroupAsync(admin);
        var teacherEmail = $"gm-teacher-{Guid.NewGuid():N}@demo-center.test";
        var teacherId = await CreateTeacherAsync(superAdmin, await BranchIdAsync(admin), "TEACHER", teacherEmail);
        await AssignStaffAsync(admin, group, teacherId, "MainTeacher");

        // Opening the chat list is what creates a group's chat room.
        var teacher = await AuthenticatedClientAsync(teacherEmail);
        Assert.Contains((await teacher.GetFromJsonAsync<List<ChatItemDto>>("/api/chats"))!, c => c.GroupId == group);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/groups/{group}")).StatusCode);
        Assert.DoesNotContain((await admin.GetFromJsonAsync<List<GroupDto>>("/api/groups"))!, g => g.Id == group);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/groups/{group}")).StatusCode);
    }

    [Fact]
    public async Task Delete_IsRefusedWithTheBlockers_WhenTheGroupHasEnrollmentsOrLessons()
    {
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var branchId = await BranchIdAsync(admin);

        var withStudent = await CreateGroupAsync(admin);
        await RegisterAsync(admin, withStudent);
        var refused = await admin.DeleteAsync($"/api/groups/{withStudent}");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var body = (await refused.Content.ReadFromJsonAsync<DeleteRefusalDto>())!;
        Assert.Contains(body.Blockers, b => b.Contains("enrollment"));
        Assert.True(body.CanCancel);

        // Once cancelled the enrollment rows still stand, and there is nothing left to cancel.
        await SetStatusAsync(admin, withStudent, "Cancelled");
        var afterCancel = (await (await admin.DeleteAsync($"/api/groups/{withStudent}")).Content.ReadFromJsonAsync<DeleteRefusalDto>())!;
        Assert.False(afterCancel.CanCancel);

        var withLesson = await CreateGroupAsync(admin);
        var teacher = await CreateTeacherAsync(superAdmin, branchId, "TEACHER");
        await AssignStaffAsync(admin, withLesson, teacher, "MainTeacher");
        var (startsAt, endsAt) = FarFutureSlot();
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/schedule/lessons",
            new { branchId, groupId = withLesson, teacherId = teacher, roomName = $"R-{Guid.NewGuid():N}"[..8], startsAt, endsAt, topic = "Held" })).StatusCode);
        var lessonRefusal = await admin.DeleteAsync($"/api/groups/{withLesson}");
        Assert.Equal(HttpStatusCode.Conflict, lessonRefusal.StatusCode);
        Assert.Contains("darslar jadvali", (await lessonRefusal.Content.ReadFromJsonAsync<DeleteRefusalDto>())!.Blockers);
    }

    [Fact]
    public async Task EveryEndpoint_IsForbidden_ToATeacherAnOperatorAndAnAdministratorOfAnotherBranch()
    {
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var group = await CreateGroupAsync(admin);
        var teacherId = await CreateTeacherAsync(superAdmin, await BranchIdAsync(admin), "SUPPORT_TEACHER");
        await AssignStaffAsync(admin, group, teacherId, "SupportTeacher");
        var elsewhere = await GroupInOtherBranchAsync(superAdmin, admin);

        foreach (var email in new[] { "teacher@demo-center.test", "operator@demo-center.test" })
        {
            var client = await AuthenticatedClientAsync(email);
            await AssertForbiddenAsync(client, group, teacherId);
        }

        await AssertForbiddenAsync(admin, elsewhere, teacherId);
        // ...and none of it touched the groups.
        var groups = (await superAdmin.GetFromJsonAsync<List<GroupDto>>("/api/groups"))!;
        Assert.Equal("Forming", groups.Single(g => g.Id == group).Status);
        Assert.Equal("Forming", groups.Single(g => g.Id == elsewhere).Status);
        Assert.Contains(groups.Single(g => g.Id == group).Staff, s => s.UserId == teacherId);
    }

    [Fact]
    public async Task ClosingAndDeletingAGroup_LeaveAnAuditTrail()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var group = await CreateGroupAsync(admin);
        await SetStatusAsync(admin, group, "Active");
        await SetStatusAsync(admin, group, "Cancelled");
        var deleted = await CreateGroupAsync(admin);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/groups/{deleted}")).StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var closing = await db.AuditLogs.Where(a => a.Resource == "Group" && a.ResourceId == group).ToListAsync();
        // The interceptor writes the enum as its number: Cancelled is the fourth member of GroupStatus.
        Assert.Contains(closing, a => a.Action == "Modified" && a.NewValueJson!.Contains("\"Status\":3"));
        Assert.Contains(await db.AuditLogs.Where(a => a.Resource == "Group" && a.ResourceId == deleted).ToListAsync(), a => a.Action == "Deleted");
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static async Task AssertForbiddenAsync(HttpClient client, Guid group, Guid staffUserId)
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PatchAsJsonAsync($"/api/groups/{group}", new { name = "Nope" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostStatusAsync(client, group, "Active")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/groups/{group}/staff/{staffUserId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/groups/{group}")).StatusCode);
    }

    private static Task<HttpResponseMessage> PostStatusAsync(HttpClient client, Guid group, string status) =>
        client.PostAsJsonAsync($"/api/groups/{group}/status", new { status });

    private static async Task<GroupDto> SetStatusAsync(HttpClient client, Guid group, string status)
    {
        var response = await PostStatusAsync(client, group, status);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<GroupDto>())!;
    }

    private static string NewEmail() => $"gm-student-{Guid.NewGuid():N}@demo-center.test";

    private static async Task<Guid> BranchIdAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;

    private static async Task<Guid> CreateGroupAsync(HttpClient client, int capacity = 10)
    {
        var course = await client.PostAsJsonAsync("/api/courses",
            new { name = $"Group Mgmt Course {Guid.NewGuid():N}"[..25], levelCategory = "IELTS" });
        Assert.Equal(HttpStatusCode.Created, course.StatusCode);
        var courseId = (await course.Content.ReadFromJsonAsync<IdDto>())!.Id;

        var response = await client.PostAsJsonAsync("/api/groups",
            new { branchId = await BranchIdAsync(client), courseId, name = $"GM-{Guid.NewGuid():N}", capacity });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<GroupDto>())!.Id;
    }

    private static async Task<Guid> GroupInOtherBranchAsync(HttpClient superAdmin, HttpClient admin)
    {
        var mine = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))!.Select(b => b.Id).ToHashSet();
        var other = (await superAdmin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))!.First(b => !mine.Contains(b.Id)).Id;
        var course = await superAdmin.PostAsJsonAsync("/api/courses",
            new { name = $"Group Mgmt Course {Guid.NewGuid():N}"[..25], levelCategory = "IELTS" });
        var response = await superAdmin.PostAsJsonAsync("/api/groups", new
        {
            branchId = other, courseId = (await course.Content.ReadFromJsonAsync<IdDto>())!.Id, name = $"GM-{Guid.NewGuid():N}", capacity = 5,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<GroupDto>())!.Id;
    }

    private static async Task<Guid> RegisterAsync(HttpClient admin, Guid groupId, string? email = null)
    {
        var response = await admin.PostAsJsonAsync("/api/students", new
        {
            fullName = "Group Management Student", phone = "+998900000097", email = email ?? NewEmail(),
            password = DbSeeder.DemoPassword, groupId,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    private static async Task<Guid> CreateTeacherAsync(HttpClient superAdmin, Guid branchId, string roleCode, string? email = null)
    {
        var response = await superAdmin.PostAsJsonAsync("/api/users", new
        {
            fullName = $"Group Mgmt Teacher {Guid.NewGuid():N}", email = email ?? $"gm-staff-{Guid.NewGuid():N}@demo-center.test",
            phone = "+998900000096", password = DbSeeder.DemoPassword, roleCode, branchIds = new[] { branchId },
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    private static async Task AssignStaffAsync(HttpClient admin, Guid groupId, Guid userId, string role)
    {
        var response = await admin.PostAsJsonAsync($"/api/groups/{groupId}/staff", new { userId, role });
        Assert.True(response.IsSuccessStatusCode, $"Staff assignment failed: {response.StatusCode}");
    }

    /// <summary>A unique far-future slot — the suite shares one database across the whole run.</summary>
    private static (DateTimeOffset StartsAt, DateTimeOffset EndsAt) FarFutureSlot()
    {
        var startsAt = DateTimeOffset.UtcNow.Date.AddDays(Random.Shared.Next(400, 4000)).AddMinutes(Random.Shared.Next(0, 1440));
        return (startsAt, startsAt.AddMinutes(60));
    }

    private async Task<StudentStatus> StudentStatusAsync(Guid studentId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Students.Where(s => s.Id == studentId).Select(s => s.Status).SingleAsync();
    }

    /// <summary>An account created through the API holds a temporary password, so its first real session starts with the
    /// forced change (S01.05) — done here, the way a person signing in for the first time would.</summary>
    private async Task<HttpClient> AuthenticatedClientAsync(string email)
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
    private record StaffDto(Guid UserId, string Role);
    private record GroupDto(Guid Id, string Name, int Capacity, string Status, List<StaffDto> Staff, int StudentCount);
    private record ChatItemDto(Guid Id, string Type, Guid? GroupId);
    private record EnrollmentSummaryDto(Guid Id, Guid GroupId, string GroupName, string Status, DateTimeOffset EnrolledAt);
    private record OverviewDto(List<EnrollmentSummaryDto> Enrollments);
    private record TimelineDto(string EventType);
    private record DeleteRefusalDto(string Error, List<string> Blockers, bool CanCancel);
}
