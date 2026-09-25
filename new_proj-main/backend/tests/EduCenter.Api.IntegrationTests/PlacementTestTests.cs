using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduCenter.Infrastructure.Seed;
using EduCenter.Modules.Crm.Domain;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// Placement Test (docs/00_TZ.md §13, audit §3.10): a fixed, operator-administered quiz graded server-side
/// against <see cref="PlacementQuestionBank"/> — the client only ever sees questions and options, never the
/// answer key, so the test asserts against the real bank rather than guessing at scores.
/// </summary>
[Collection("Api")]
public class PlacementTestTests(EduCenterWebApplicationFactory factory)
{
    [Fact]
    public async Task Questions_WithoutToken_ReturnsUnauthorized()
    {
        var response = await factory.CreateClient().GetAsync("/api/placement-test/questions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Questions_NeverExposeTheAnswerKey()
    {
        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");

        var response = await operatorClient.GetAsync("/api/placement-test/questions");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("correctIndex", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("weight", body, StringComparison.OrdinalIgnoreCase);
        var questions = await response.Content.ReadFromJsonAsync<List<QuestionDto>>();
        Assert.Equal(PlacementQuestionBank.Questions.Count, questions!.Count);
    }

    [Fact]
    public async Task AllCorrectAnswers_RecommendsTheTopLevel()
    {
        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        var branchId = (await operatorClient.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var lead = await CreateLeadAsync(operatorClient, branchId);

        var answers = PlacementQuestionBank.Questions
            .Select(q => new { questionId = q.Id, selectedIndex = q.CorrectIndex });

        var response = await operatorClient.PostAsJsonAsync($"/api/leads/{lead}/placement-test", new { answers });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var attempt = await response.Content.ReadFromJsonAsync<AttemptDto>();
        Assert.Equal(PlacementQuestionBank.Questions.Count, attempt!.CorrectCount);
        Assert.Equal("IELTS Advanced", attempt.RecommendedLevel);
    }

    [Fact]
    public async Task AllWrongAnswers_RecommendsTheBottomLevel()
    {
        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        var branchId = (await operatorClient.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var lead = await CreateLeadAsync(operatorClient, branchId);

        // Picking the option right after the correct one (wrapping around) is always wrong for a 4-option MCQ.
        var answers = PlacementQuestionBank.Questions
            .Select(q => new { questionId = q.Id, selectedIndex = (q.CorrectIndex + 1) % q.Options.Length });

        var response = await operatorClient.PostAsJsonAsync($"/api/leads/{lead}/placement-test", new { answers });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var attempt = await response.Content.ReadFromJsonAsync<AttemptDto>();
        Assert.Equal(0, attempt!.CorrectCount);
        Assert.Equal("A1", attempt.RecommendedLevel);
    }

    [Fact]
    public async Task History_ReturnsSubmittedAttempts()
    {
        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        var branchId = (await operatorClient.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var lead = await CreateLeadAsync(operatorClient, branchId);

        var answers = PlacementQuestionBank.Questions.Take(3)
            .Select(q => new { questionId = q.Id, selectedIndex = q.CorrectIndex });
        await operatorClient.PostAsJsonAsync($"/api/leads/{lead}/placement-test", new { answers });

        var history = await operatorClient.GetFromJsonAsync<List<AttemptDto>>($"/api/leads/{lead}/placement-test");

        Assert.Single(history!);
        Assert.Equal(3, history![0].CorrectCount);
    }

    [Fact]
    public async Task Teacher_CannotSubmitOrViewPlacementTests()
    {
        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        var teacher = await AuthenticatedClientAsync("teacher@demo-center.test");
        var branchId = (await operatorClient.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var lead = await CreateLeadAsync(operatorClient, branchId);

        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PostAsJsonAsync($"/api/leads/{lead}/placement-test",
            new { answers = Array.Empty<object>() })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync($"/api/leads/{lead}/placement-test")).StatusCode);
    }

    [Fact]
    public async Task Administrator_CannotSubmitForALeadOutsideTheirBranch()
    {
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        var adminBranch = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var otherBranch = (await superAdmin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))!
            .First(b => b.Id != adminBranch).Id;
        var lead = await CreateLeadAsync(operatorClient, otherBranch);

        var response = await admin.PostAsJsonAsync($"/api/leads/{lead}/placement-test",
            new { answers = new[] { new { questionId = 1, selectedIndex = 0 } } });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task<Guid> CreateLeadAsync(HttpClient operatorClient, Guid branchId)
    {
        var lead = await (await operatorClient.PostAsJsonAsync("/api/leads", new
        {
            branchId, fullName = "Placement Test Lead", phone = "+998900000091", source = "Website", interestedCourse = "IELTS",
        })).Content.ReadFromJsonAsync<LeadDto>();
        return lead!.Id;
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
    private record LeadDto(Guid Id);
    private record QuestionDto(int Id, string Text, string[] Options);
    private record AttemptDto(Guid Id, Guid LeadId, int CorrectCount, int TotalQuestions, string RecommendedLevel, DateTimeOffset CompletedAt);
}
