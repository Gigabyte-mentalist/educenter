using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduCenter.Infrastructure.Seed;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// Teacher 360° Performance tab (docs/00_TZ.md §6, audit §3.6): attendance/homework/exam numbers built from
/// data the product already tracks. Visibility mirrors the roster endpoint it sits next to — self, or a
/// broad-access manager whose branch actually covers this teacher.
/// </summary>
[Collection("Api")]
public class TeacherPerformanceTests(EduCenterWebApplicationFactory factory)
{
    [Fact]
    public async Task Performance_WithoutToken_ReturnsUnauthorized()
    {
        var response = await factory.CreateClient().GetAsync($"/api/teachers/{Guid.NewGuid()}/performance");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Performance_ForNonTeacherUser_ReturnsNotFound()
    {
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var studentUserId = (await superAdmin.GetFromJsonAsync<PagedResponse<UserDto>>("/api/users?role=STUDENT&size=200"))!
            .Items.First(u => u.Email == "student@demo-center.test").Id;

        var response = await superAdmin.GetAsync($"/api/teachers/{studentUserId}/performance");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Performance_AsSelf_Succeeds()
    {
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var teacher = await AuthenticatedClientAsync("teacher@demo-center.test");
        var teacherId = (await superAdmin.GetFromJsonAsync<PagedResponse<UserDto>>("/api/users?role=TEACHER"))!
            .Items.First(u => u.Email == "teacher@demo-center.test").Id;

        var response = await teacher.GetAsync($"/api/teachers/{teacherId}/performance");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var performance = await response.Content.ReadFromJsonAsync<TeacherPerformanceDto>();
        Assert.InRange(performance!.AttendanceRate, 0, 100);
        Assert.InRange(performance.HomeworkCompletionRate, 0, 100);
        Assert.True(performance.GroupsCount > 0, "The demo teacher is staffed on groups.");
    }

    [Fact]
    public async Task Administrator_CannotViewTeacherPerformanceOutsideTheirBranch()
    {
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var adminBranch = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var otherBranch = (await superAdmin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))!
            .First(b => b.Id != adminBranch).Id;
        var otherTeacherId = await CreateTeacherAsync(superAdmin, otherBranch);

        var response = await admin.GetAsync($"/api/teachers/{otherTeacherId}/performance");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Performance_ComputesAttendanceRate_FromFreshData()
    {
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        var branchId = (await superAdmin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;

        var course = await (await superAdmin.PostAsJsonAsync("/api/courses",
            new { name = $"Perf Course {Guid.NewGuid():N}"[..24], levelCategory = "IELTS" })).Content.ReadFromJsonAsync<CourseDto>();
        var teacherId = await CreateTeacherAsync(superAdmin, branchId);
        var group = await (await superAdmin.PostAsJsonAsync("/api/groups",
            new { branchId, courseId = course!.Id, name = $"PERF-{Guid.NewGuid():N}", capacity = 10 }))
            .Content.ReadFromJsonAsync<GroupDto>();
        Assert.True((await superAdmin.PostAsJsonAsync($"/api/groups/{group!.Id}/staff",
            new { userId = teacherId, role = "MainTeacher" })).IsSuccessStatusCode);

        var student1 = await ConvertAsync(operatorClient, branchId, group.Id);
        var student2 = await ConvertAsync(operatorClient, branchId, group.Id);

        var startsAt = DateTimeOffset.UtcNow.Date.AddDays(Random.Shared.Next(400, 4000)).AddMinutes(Random.Shared.Next(0, 1440));
        var lesson = await (await superAdmin.PostAsJsonAsync("/api/schedule/lessons", new
        {
            branchId, groupId = group.Id, teacherId, roomName = $"R-{Guid.NewGuid():N}"[..8],
            startsAt, endsAt = startsAt.AddMinutes(60), topic = "Performance test",
        })).Content.ReadFromJsonAsync<LessonDto>();

        var mark = await superAdmin.PostAsJsonAsync($"/api/lessons/{lesson!.Id}/attendance", new
        {
            items = new object[]
            {
                new { studentId = student1, status = "Present" },
                new { studentId = student2, status = "Absent" },
            },
        });
        Assert.Equal(HttpStatusCode.NoContent, mark.StatusCode);

        var performance = await superAdmin.GetFromJsonAsync<TeacherPerformanceDto>($"/api/teachers/{teacherId}/performance");

        Assert.Equal(2, performance!.ActiveStudents);
        Assert.Equal(1, performance.GroupsCount);
        Assert.Equal(50.0, performance.AttendanceRate);
        Assert.Equal(0.0, performance.HomeworkCompletionRate);
        Assert.Null(performance.AverageExamScore);
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static async Task<Guid> CreateTeacherAsync(HttpClient superAdmin, Guid branchId)
    {
        var response = await superAdmin.PostAsJsonAsync("/api/users", new
        {
            fullName = $"Performance Teacher {Guid.NewGuid():N}",
            email = $"perf-{Guid.NewGuid():N}@demo-center.test",
            phone = "+998900000055",
            password = DbSeeder.DemoPassword,
            roleCode = "TEACHER",
            branchIds = new[] { branchId },
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<UserDto>())!.Id;
    }

    private static async Task<Guid> ConvertAsync(HttpClient operatorClient, Guid branchId, Guid groupId)
    {
        var lead = await (await operatorClient.PostAsJsonAsync("/api/leads", new
        {
            branchId, fullName = "Performance Student", phone = "+998900000077", source = "Website", interestedCourse = "IELTS",
        })).Content.ReadFromJsonAsync<LeadDto>();

        var email = $"perf-{Guid.NewGuid():N}@demo-center.test";
        var response = await operatorClient.PostAsJsonAsync($"/api/leads/{lead!.Id}/convert",
            new { groupId, email, password = DbSeeder.DemoPassword });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StudentDto>())!.Id;
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
    private record CourseDto(Guid Id);
    private record GroupDto(Guid Id);
    private record LeadDto(Guid Id);
    private record StudentDto(Guid Id);
    private record UserDto(Guid Id, string FullName, string Email);
    private record LessonDto(Guid Id);
    private record TeacherPerformanceDto(int ActiveStudents, int GroupsCount, double AttendanceRate, double HomeworkCompletionRate, decimal? AverageExamScore);
}
