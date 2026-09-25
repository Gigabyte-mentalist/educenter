using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduCenter.Infrastructure.Seed;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// The operator's path from a call to a student: pick a group and one of its real lessons for the trial, mark whether the person
/// came, enrol them. The lead list and the recommendations carry what the board needs to show each of those steps on the card.
/// </summary>
[Collection("Api")]
public class LeadTrialFlowTests(EduCenterWebApplicationFactory factory)
{
    [Fact]
    public async Task Recommendations_NameTheTeacher_AndOfferTheGroupsRealNextLessons()
    {
        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        var lead = await CreateLeadAsync(operatorClient);

        var recommendations = (await operatorClient.GetFromJsonAsync<List<RecommendationDto>>($"/api/leads/{lead.Id}/group-recommendations"))!;

        // Other tests fill some demo groups, so take whichever open group has a teacher and lessons still to come.
        var group = recommendations.First(r => r.TeacherName is not null && r.NextLessons.Count > 0);
        Assert.True(group.NextLessons.All(l => l.StartsAt > DateTimeOffset.UtcNow.AddMinutes(-1)), "Only lessons still to come are offered.");
        Assert.Equal(group.NextLessons.OrderBy(l => l.StartsAt).Select(l => l.Id), group.NextLessons.Select(l => l.Id));
        Assert.All(recommendations, r => Assert.NotNull(r.NextLessons));
    }

    [Fact]
    public async Task LeadList_ShowsTheLatestTrial_AndItFollowsAttendance_ThroughToEnrolment()
    {
        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        var lead = await CreateLeadAsync(operatorClient);
        Assert.Null((await GetLeadAsync(operatorClient, lead.Id)).LatestTrial);

        var recommendations = (await operatorClient.GetFromJsonAsync<List<RecommendationDto>>($"/api/leads/{lead.Id}/group-recommendations"))!;
        var group = recommendations.First(r => r.NextLessons.Count > 0);
        var lesson = group.NextLessons[0];
        var booked = await operatorClient.PostAsJsonAsync($"/api/leads/{lead.Id}/trial", new { groupId = group.GroupId, scheduledAt = lesson.StartsAt });
        Assert.Equal(HttpStatusCode.Created, booked.StatusCode);

        var scheduled = await GetLeadAsync(operatorClient, lead.Id);
        Assert.Equal("TrialBooked", scheduled.Status);
        Assert.Equal(group.GroupName, scheduled.LatestTrial!.GroupName);
        Assert.Equal("Scheduled", scheduled.LatestTrial.AttendanceStatus);
        // The board reads the same thing from the list — no second request per card.
        var onBoard = (await operatorClient.GetFromJsonAsync<PagedResponse<LeadRow>>("/api/leads?size=200"))!.Items.Single(l => l.Id == lead.Id);
        Assert.Equal(scheduled.LatestTrial.Id, onBoard.LatestTrial!.Id);

        Assert.Equal(HttpStatusCode.NoContent,
            (await operatorClient.PatchAsJsonAsync($"/api/trial-lessons/{scheduled.LatestTrial.Id}/attendance", new { attended = true })).StatusCode);
        var attended = await GetLeadAsync(operatorClient, lead.Id);
        Assert.Equal("TrialAttended", attended.Status);
        Assert.Equal("Attended", attended.LatestTrial!.AttendanceStatus);

        // Attended → enrol into the trial's group: the lead becomes a student and remembers who.
        var converted = await operatorClient.PostAsJsonAsync($"/api/leads/{lead.Id}/convert", new
        {
            groupId = attended.LatestTrial.GroupId, email = $"trial-flow-{Guid.NewGuid():N}@demo-center.test", password = DbSeeder.DemoPassword,
        });
        Assert.Equal(HttpStatusCode.Created, converted.StatusCode);
        var student = (await converted.Content.ReadFromJsonAsync<StudentRow>())!;

        var done = await GetLeadAsync(operatorClient, lead.Id);
        Assert.Equal("Converted", done.Status);
        Assert.Equal(student.Id, done.ConvertedStudentId);
    }

    [Fact]
    public async Task Administrator_CanBookTheTrial_AndEnrolTheLead_Too()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var lead = await CreateLeadAsync(admin);
        var recommendations = (await admin.GetFromJsonAsync<List<RecommendationDto>>($"/api/leads/{lead.Id}/group-recommendations"))!;
        var group = recommendations.First(r => r.NextLessons.Count > 0);

        Assert.Equal(HttpStatusCode.Created,
            (await admin.PostAsJsonAsync($"/api/leads/{lead.Id}/trial", new { groupId = group.GroupId, scheduledAt = group.NextLessons[0].StartsAt })).StatusCode);
        var trialId = (await GetLeadAsync(admin, lead.Id)).LatestTrial!.Id;
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PatchAsJsonAsync($"/api/trial-lessons/{trialId}/attendance", new { attended = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync($"/api/leads/{lead.Id}/convert", new
        {
            groupId = group.GroupId, email = $"admin-flow-{Guid.NewGuid():N}@demo-center.test", password = DbSeeder.DemoPassword,
        })).StatusCode);
    }

    private static async Task<LeadRow> CreateLeadAsync(HttpClient client)
    {
        var branchId = (await client.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var response = await client.PostAsJsonAsync("/api/leads", new
        {
            branchId, fullName = $"Trial Flow {Guid.NewGuid():N}"[..20], phone = "+998900000077", source = "Instagram", interestedCourse = "IELTS",
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LeadRow>())!;
    }

    private static async Task<LeadRow> GetLeadAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<LeadRow>($"/api/leads/{id}"))!;

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
    private record TrialRow(Guid Id, Guid GroupId, string GroupName, DateTimeOffset ScheduledAt, string AttendanceStatus);
    private record LeadRow(Guid Id, string Status, Guid? ConvertedStudentId, TrialRow? LatestTrial);
    private record StudentRow(Guid Id);
    private record LessonRow(Guid Id, DateTimeOffset StartsAt);
    private record RecommendationDto(Guid GroupId, string GroupName, string? TeacherName, List<LessonRow> NextLessons);
}
