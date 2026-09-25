using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduCenter.Infrastructure;
using EduCenter.Infrastructure.Seed;
using EduCenter.Modules.Academic.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// A small classroom for the LMS tests (journal, homework files, targeted homework): a fresh group in the administrator's branch, the
/// demo teacher on it — or not, for the "another teacher's group" case — enrolled students and one lesson far ahead. A fresh group per
/// test keeps the tests from counting each other's rows, and the demo teacher is reused because they only need to be named on the
/// lesson: it keeps the suite from adding accounts other tests page through.
/// </summary>
internal sealed class LmsWorld(EduCenterWebApplicationFactory factory)
{
    public record Student(Guid Id, Guid UserId, string Email);

    public record Classroom(Guid GroupId, Guid BranchId, Guid LessonId, DateTimeOffset StartsAt, List<Student> Students)
    {
        public Student First => Students[0];
    }

    /// <param name="teacherStaffsGroup">Off, the teacher is only named on the lesson — they do not staff the group, so it is not theirs.</param>
    public async Task<Classroom> CreateAsync(int students = 1, bool teacherStaffsGroup = true)
    {
        var admin = await ClientAsync("admin@demo-center.test");
        var branchId = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;

        var course = await admin.PostAsJsonAsync("/api/courses", new { name = $"Lms Course {Guid.NewGuid():N}"[..25], levelCategory = "IELTS" });
        Assert.Equal(HttpStatusCode.Created, course.StatusCode);
        var courseId = (await course.Content.ReadFromJsonAsync<IdDto>())!.Id;
        var group = await admin.PostAsJsonAsync("/api/groups", new { branchId, courseId, name = $"LW-{Guid.NewGuid():N}", capacity = 20 });
        Assert.Equal(HttpStatusCode.Created, group.StatusCode);
        var groupId = (await group.Content.ReadFromJsonAsync<IdDto>())!.Id;

        var teacherId = await UserIdAsync("teacher@demo-center.test", "TEACHER");
        if (teacherStaffsGroup)
        {
            var superAdmin = await ClientAsync("superadmin@demo-center.test");
            Assert.Equal(HttpStatusCode.NoContent,
                (await superAdmin.PostAsJsonAsync($"/api/groups/{groupId}/staff", new { userId = teacherId, role = "MainTeacher" })).StatusCode);
        }

        var enrolled = new List<Student>();
        for (var i = 0; i < students; i++) enrolled.Add(await AddStudentAsync(groupId));

        // The slot is random and the demo teacher is shared by every test, so two lessons now and then overlap and the schedule
        // refuses the second with a 409: draw another slot instead of failing a test that is not about scheduling.
        DateTimeOffset startsAt;
        HttpResponseMessage lesson;
        var attempts = 0;
        do
        {
            startsAt = DateTimeOffset.UtcNow.Date.AddDays(Random.Shared.Next(400, 4000)).AddMinutes(Random.Shared.Next(0, 1440));
            lesson = await admin.PostAsJsonAsync("/api/schedule/lessons", new
            {
                branchId, groupId, teacherId, roomName = $"R-{Guid.NewGuid():N}"[..8], startsAt, endsAt = startsAt.AddMinutes(60),
            });
        } while (lesson.StatusCode == HttpStatusCode.Conflict && ++attempts < 5);
        Assert.Equal(HttpStatusCode.Created, lesson.StatusCode);
        return new Classroom(groupId, branchId, (await lesson.Content.ReadFromJsonAsync<IdDto>())!.Id, startsAt, enrolled);
    }

    /// <summary>A student registered into the group the way the office does it.</summary>
    public async Task<Student> AddStudentAsync(Guid groupId)
    {
        var admin = await ClientAsync("admin@demo-center.test");
        var email = $"lms-{Guid.NewGuid():N}@demo-center.test";
        var response = await admin.PostAsJsonAsync("/api/students", new
        {
            fullName = "Lms Student", phone = "+998900000093", email, password = DbSeeder.DemoPassword, groupId,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var studentId = (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;

        using var scope = factory.Services.CreateScope();
        var userId = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Students
            .Where(s => s.Id == studentId).Select(s => s.UserId).SingleAsync();
        return new Student(studentId, userId, email);
    }

    /// <summary>A lesson that already took place, put in directly: the schedule endpoint refuses to create one.</summary>
    public async Task<Guid> AddPastLessonAsync(Classroom classroom)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var organizationId = await db.Groups.Where(g => g.Id == classroom.GroupId).Select(g => g.OrganizationId).SingleAsync();
        var teacherId = await db.Users.Where(u => u.Email == "teacher@demo-center.test").Select(u => u.Id).SingleAsync();
        var start = DateTimeOffset.UtcNow.AddDays(-Random.Shared.Next(2, 300)).AddMinutes(Random.Shared.Next(0, 1440));
        var lesson = new Lesson(organizationId, classroom.BranchId, classroom.GroupId, teacherId, "Past room", start, start.AddHours(1));
        db.Lessons.Add(lesson);
        await db.SaveChangesAsync();
        return lesson.Id;
    }

    public async Task<Guid> UserIdAsync(string email, string role)
    {
        var superAdmin = await ClientAsync("superadmin@demo-center.test");
        return (await superAdmin.GetFromJsonAsync<PagedResponse<UserDto>>($"/api/users?role={role}"))!.Items.First(u => u.Email == email).Id;
    }

    private readonly Dictionary<string, HttpClient> clients = [];

    /// <summary>A signed-in client, kept for the rest of the test: an account created through the API starts with the forced password
    /// change (S01.05), so the second sign-in with the demo password would be refused.</summary>
    public async Task<HttpClient> ClientAsync(string email)
    {
        if (clients.TryGetValue(email, out var known)) return known;

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
        clients[email] = client;
        return client;
    }

    /// <summary>A signed-in client of <em>another</em> host over the same database (<c>factory.WithWebHostBuilder</c> — a host with one setting
    /// changed). An account made through the API has to change its first password before anything else works, which <see cref="ClientAsync"/>
    /// does on the main host; here the changed password is what signs in.</summary>
    public async Task<HttpClient> ClientOnAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> host, string email)
    {
        await ClientAsync(email);

        var client = host.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = DbSeeder.DemoPassword });
        if (response.StatusCode != HttpStatusCode.OK)
            response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Chosen-Pass-42" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", (await response.Content.ReadFromJsonAsync<TokenResponseDto>())!.AccessToken);
        return client;
    }

    private record TokenResponseDto(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, bool MustChangePassword);
    private record IdDto(Guid Id);
    private record BranchDto(Guid Id, string Name);
    private record UserDto(Guid Id, string Email);
}
