using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduCenter.Infrastructure.Seed;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// Proves the CallLater → automatic follow-up task behavior from docs/00_TZ.md §3.3
/// ("Call later" tanlansa yangi task avtomatik yaratiladi) actually happens end to end.
/// </summary>
[Collection("Api")]
public class CrmTests(EduCenterWebApplicationFactory factory)
{
    [Fact]
    public async Task LoggingCallLaterOutcome_AutomaticallyCreatesFollowUpTask()
    {
        var client = await AuthenticatedClientAsync("operator@demo-center.test");

        var branches = await client.GetFromJsonAsync<List<BranchDto>>("/api/branches");
        var branchId = branches![0].Id;

        var leadResponse = await client.PostAsJsonAsync("/api/leads", new
        {
            branchId,
            fullName = "Test Lead",
            phone = "+998900000001",
            source = "Website",
            interestedCourse = "IELTS"
        });
        Assert.Equal(HttpStatusCode.Created, leadResponse.StatusCode);
        var lead = await leadResponse.Content.ReadFromJsonAsync<LeadDto>();

        var callResponse = await client.PostAsJsonAsync($"/api/leads/{lead!.Id}/calls", new
        {
            outcome = "CallLater",
            notes = "Follow up tomorrow"
        });
        Assert.Equal(HttpStatusCode.Created, callResponse.StatusCode);

        var tasks = (await client.GetFromJsonAsync<PagedResponse<CrmTaskDto>>("/api/crm/tasks?size=200"))!.Items;

        Assert.Contains(tasks, t => t.LeadId == lead.Id && t.Type == "CallBack" && t.Status == "Open");
    }

    [Fact]
    public async Task Student_CannotViewLeads()
    {
        var client = await AuthenticatedClientAsync("student@demo-center.test");

        var response = await client.GetAsync("/api/leads");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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
    private record LeadDto(Guid Id, Guid BranchId, string FullName, string Phone, string Source,
        string InterestedCourse, Guid OperatorId, string Status, string? Notes, string? LostReason, DateTimeOffset CreatedAt);
    private record CrmTaskDto(Guid Id, Guid? LeadId, Guid? StudentId, Guid AssignedTo, string Type, string Status, DateTimeOffset DueAt);
}
