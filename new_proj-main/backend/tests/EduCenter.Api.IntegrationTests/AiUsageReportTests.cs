using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduCenter.Api.Ai;
using EduCenter.Infrastructure.Seed;
using EduCenter.Modules.Ai.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// AI cost control and quality monitoring (docs/00_TZ.md §54, §55): the usage report is built from the gateway journal,
/// so every feature is counted without the report knowing about it, and the writing benchmark compares the AI's estimate
/// with the teacher's own band for essays that were re-marked.
/// </summary>
[Collection("Api")]
public class AiUsageReportTests(EduCenterWebApplicationFactory factory)
{
    private const string Essay =
        "Some people believe that university education should be free for everyone, while others think students should pay. " +
        "In my opinion, governments should cover most of the costs because education benefits the whole society. " +
        "Firstly, talented students from poor families can study and later contribute to the economy. " +
        "Secondly, an educated population is more likely to be healthy and to take part in public life. " +
        "However, free education is expensive, so taxes may need to rise. " +
        "In conclusion, university education should be funded mainly by the state.";

    [Fact]
    public async Task UsageSummary_CountsGatewayTraffic_AndOnlyManagersSeeIt()
    {
        // A request through the gateway is what the report reads — no feature-specific bookkeeping.
        using (var scope = factory.Services.CreateScope())
        {
            var gateway = scope.ServiceProvider.GetRequiredService<AiGateway>();
            var contexts = scope.ServiceProvider.GetRequiredService<AiCallContextFactory>();
            var db = scope.ServiceProvider.GetRequiredService<EduCenter.Infrastructure.AppDbContext>();
            var userId = db.Users.Single(u => u.Email == "student@demo-center.test").Id;
            var context = (await contexts.ForUserAsync(userId, AiFeature.Tutor))!;

            var prompt = new AiPrompt("test.usage.v1", "Answer briefly.", [new AiUntrustedInput("question", "What is a verb?", 200)]);
            Assert.True((await gateway.CompleteTextAsync(context, AiTaskKind.TutorAnswer, prompt)).IsSuccess);
        }

        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var summary = (await superAdmin.GetFromJsonAsync<SummaryDto>("/api/ai/usage/summary"))!;
        Assert.True(summary.Totals.Requests > 0);
        Assert.True(summary.Totals.Succeeded > 0);
        Assert.Contains(summary.ByFeature, f => f.Key == "Tutor");
        Assert.Contains(summary.ByProvider, p => p.Key.StartsWith("fake"));
        Assert.NotEmpty(summary.Daily);
        Assert.Contains(summary.TopStudents, s => s.Requests > 0);

        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/ai/usage/summary")).StatusCode);

        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.GetAsync("/api/ai/usage/summary")).StatusCode);
        var student = await AuthenticatedClientAsync("student@demo-center.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync("/api/ai/usage/summary")).StatusCode);
    }

    [Fact]
    public async Task UsageSummary_RejectsImpossiblePeriods()
    {
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await superAdmin.GetAsync($"/api/ai/usage/summary?from={today:yyyy-MM-dd}&to={today.AddDays(-1):yyyy-MM-dd}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await superAdmin.GetAsync($"/api/ai/usage/summary?from={today.AddDays(-200):yyyy-MM-dd}&to={today:yyyy-MM-dd}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await superAdmin.GetAsync($"/api/ai/usage/summary?from={today.AddDays(-7):yyyy-MM-dd}&to={today:yyyy-MM-dd}")).StatusCode);
    }

    [Fact]
    public async Task WritingBenchmark_ComparesTeacherBandsWithTheAiEstimate()
    {
        var student = await AuthenticatedClientAsync("nigora.sodiqova@demo-center.test");
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");

        var evaluation = (await (await student.PostAsJsonAsync("/api/ai/writing/evaluations", new
        {
            taskType = "IeltsTask2",
            taskPrompt = "Should university education be free? Discuss.",
            essay = Essay
        })).Content.ReadFromJsonAsync<EvaluationDto>())!;
        Assert.Equal("Completed", evaluation.Summary.Status);

        var beforeOverride = (await superAdmin.GetFromJsonAsync<BenchmarkDto>("/api/ai/benchmarks/writing"))!;
        var samplesBefore = beforeOverride.Rows.Sum(r => r.Samples);

        // Only a re-marked essay becomes a benchmark sample.
        Assert.Equal(HttpStatusCode.OK, (await superAdmin.PostAsJsonAsync(
            $"/api/ai/writing/evaluations/{evaluation.Summary.Id}/override",
            new { band = 6.5, taskResponse = 6.0, comment = "Good structure." })).StatusCode);

        var benchmark = (await superAdmin.GetFromJsonAsync<BenchmarkDto>("/api/ai/benchmarks/writing"))!;
        Assert.Equal(samplesBefore + 1, benchmark.Rows.Sum(r => r.Samples));
        Assert.True(benchmark.Overridden >= 1);

        var row = benchmark.Rows.Single(r => r.PromptVersion == "writing.ielts-task2.v2");
        Assert.True(row.MeanAbsoluteError >= 0);
        Assert.InRange(row.WithinHalfBand, 0, 100);
        Assert.NotNull(row.TaskResponseMae);

        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.GetAsync("/api/ai/benchmarks/writing")).StatusCode);
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
    private record TotalsDto(int Requests, int Succeeded, int Failed, int Rejected, decimal CostUsd, int SpeakingMinutes);
    private record GroupDto(string Key, int Requests, decimal CostUsd);
    private record StudentRowDto(Guid? StudentId, string Name, int Requests, decimal CostUsd);
    private record DayDto(DateOnly Date, int Requests);
    private record SummaryDto(TotalsDto Totals, List<DayDto> Daily, List<GroupDto> ByFeature, List<GroupDto> ByProvider,
        List<StudentRowDto> TopStudents);
    private record EvaluationSummaryDto(Guid Id, string Status, decimal? EstimatedBand);
    private record EvaluationDto(EvaluationSummaryDto Summary);
    private record BenchmarkRowDto(string PromptVersion, string? Model, int Samples, decimal MeanAbsoluteError,
        decimal WithinHalfBand, decimal? TaskResponseMae);
    private record BenchmarkDto(int Completed, int Overridden, List<BenchmarkRowDto> Rows);
}
