using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduCenter.Infrastructure.Seed;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// Proves the Lead → Student conversion (docs/04_phase2_technical_plan.md §1) actually produces a
/// working login — the whole point of the design decision to keep Lead and Student separate.
/// </summary>
[Collection("Api")]
public class LeadConversionTests(EduCenterWebApplicationFactory factory)
{
    [Fact]
    public async Task ConvertedLead_ProducesAWorkingStudentLogin()
    {
        var adminClient = await AuthenticatedClientAsync("admin@demo-center.test");
        var branches = await adminClient.GetFromJsonAsync<List<BranchDto>>("/api/branches");
        var branchId = branches![0].Id;

        var course = await (await adminClient.PostAsJsonAsync("/api/courses",
            new { name = "Conversion Test Course", levelCategory = "A1" })).Content.ReadFromJsonAsync<CourseDto>();
        var group = await (await adminClient.PostAsJsonAsync("/api/groups",
            new { branchId, courseId = course!.Id, name = "Conversion-Test-Group", capacity = 10 }))
            .Content.ReadFromJsonAsync<GroupDto>();

        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");

        var lead = await (await operatorClient.PostAsJsonAsync("/api/leads", new
        {
            branchId,
            fullName = "Conversion Test Lead",
            phone = "+998900000099",
            source = "Website",
            interestedCourse = "A1"
        })).Content.ReadFromJsonAsync<LeadDto>();

        var email = $"conversion-test-{Guid.NewGuid():N}@demo-center.test";
        var trialResponse = await operatorClient.PostAsJsonAsync($"/api/leads/{lead!.Id}/trial", new
        {
            groupId = group!.Id,
            scheduledAt = DateTimeOffset.UtcNow.AddDays(1)
        });
        Assert.Equal(HttpStatusCode.Created, trialResponse.StatusCode);
        var trial = await trialResponse.Content.ReadFromJsonAsync<TrialDto>();
        Assert.NotNull(trial);
        var trials = await operatorClient.GetFromJsonAsync<List<TrialDto>>($"/api/trial-lessons?leadId={lead.Id}");
        Assert.Contains(trials!, t => t.Id == trial.Id);
        var attendanceResponse = await operatorClient.PatchAsJsonAsync($"/api/trial-lessons/{trial.Id}/attendance", new { attended = true });
        Assert.Equal(HttpStatusCode.NoContent, attendanceResponse.StatusCode);
        var convertResponse = await operatorClient.PostAsJsonAsync($"/api/leads/{lead!.Id}/convert", new
        {
            groupId = group!.Id,
            email,
            password = DbSeeder.DemoPassword,
            currentLevel = "A1"
        });

        Assert.Equal(HttpStatusCode.Created, convertResponse.StatusCode);
        var student = await convertResponse.Content.ReadFromJsonAsync<StudentDto>();
        Assert.NotNull(student);

        // The point of the whole flow: the new account can actually log in.
        var loginClient = factory.CreateClient();
        var loginResponse = await loginClient.PostAsJsonAsync("/api/auth/login", new { email, password = DbSeeder.DemoPassword });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        // Converting the same lead twice must be rejected, not silently duplicate the student.
        var secondAttempt = await operatorClient.PostAsJsonAsync($"/api/leads/{lead.Id}/convert", new
        {
            groupId = group.Id,
            email = $"other-{Guid.NewGuid():N}@demo-center.test",
            password = DbSeeder.DemoPassword
        });
        Assert.Equal(HttpStatusCode.Conflict, secondAttempt.StatusCode);
        var lateAttendance = await operatorClient.PatchAsJsonAsync($"/api/trial-lessons/{trial.Id}/attendance", new { attended = false });
        Assert.Equal(HttpStatusCode.Conflict, lateAttendance.StatusCode);
        var refreshedLeads = (await operatorClient.GetFromJsonAsync<PagedResponse<LeadDto>>("/api/leads?size=200"))!.Items;
        Assert.Equal("Converted", refreshedLeads.Single(l => l.Id == lead.Id).Status);
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
    private record BranchDto(Guid Id, string Name, string Address, string Timezone);
    private record CourseDto(Guid Id, string Name, string LevelCategory);
    private record GroupDto(Guid Id, Guid BranchId, Guid CourseId, string Name, int Capacity, string Status, object[] Staff);
    private record LeadDto(Guid Id, Guid BranchId, string FullName, string Phone, string Source,
        string InterestedCourse, Guid OperatorId, string Status, string? Notes, string? LostReason,
        Guid? ConvertedStudentId, DateTimeOffset CreatedAt);
    private record StudentDto(Guid Id, Guid UserId, string StudentNumber, DateOnly JoinedDate, string? CurrentLevel, string Status);
    private record TrialDto(Guid Id);
}
