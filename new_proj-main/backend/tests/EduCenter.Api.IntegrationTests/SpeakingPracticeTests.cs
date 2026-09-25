using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using EduCenter.Api.Ai;
using EduCenter.Api.Ai.Providers;
using EduCenter.Api.Ai.Speaking;
using EduCenter.Api.Ai.Writing;
using EduCenter.Infrastructure;
using EduCenter.Infrastructure.Seed;
using EduCenter.Modules.Ai.Domain;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// Speaking practice (docs/06_phase4_technical_plan.md Sprint 4) with the fake chat and speech providers: a session runs
/// turn by turn, each recording is transcribed and measured, the closing assessment scores three criteria and the overall
/// band follows the IELTS mean rule. A session belongs to the student who made it; staff read it only through the review endpoints
/// (<see cref="SpeakingReviewTests"/>).
/// </summary>
[Collection("Api")]
public class SpeakingPracticeTests(EduCenterWebApplicationFactory factory)
{
    private readonly LmsWorld world = new(factory);

    [Fact]
    public async Task Session_RunsTurnByTurn_AndFinishesWithAnEstimatedBand()
    {
        var student = await AuthenticatedClientAsync("student@demo-center.test");

        var started = await student.PostAsJsonAsync("/api/ai/speaking/sessions", new { mode = "IeltsPart1" });
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        var session = (await started.Content.ReadFromJsonAsync<SessionDto>())!;
        Assert.Equal("Active", session.Status);
        Assert.Equal(5, session.TotalTurns);
        Assert.NotNull(session.CurrentQuestion);
        Assert.Single(session.Turns);

        var answered = (await (await AnswerAsync(student, session.Id, "answer.webm", 32)).Content.ReadFromJsonAsync<SessionDto>())!;
        Assert.Equal(1, answered.TurnCount);
        var firstTurn = answered.Turns[0];
        Assert.NotNull(firstTurn.Transcript);
        Assert.True(firstTurn.SpokenSeconds > 0);
        Assert.True(firstTurn.WordsPerMinute > 0);
        Assert.True(firstTurn.HasAudio);
        Assert.Equal(2, answered.Turns.Count); // the examiner already asked the next question
        Assert.True(answered.SpokenSeconds > 0);

        var audio = await student.GetAsync($"/api/ai/speaking/sessions/{session.Id}/turns/{firstTurn.Id}/audio");
        Assert.Equal(HttpStatusCode.OK, audio.StatusCode);

        var finished = (await (await student.PostAsync($"/api/ai/speaking/sessions/{session.Id}/finish", null))
            .Content.ReadFromJsonAsync<SessionDto>())!;
        Assert.Equal("Completed", finished.Status);
        Assert.Equal(6.0m, finished.FluencyBand);
        Assert.Equal(6.5m, finished.LexicalBand);
        Assert.Equal(6.0m, finished.GrammarBand);
        Assert.Equal(WritingRubric.OverallBand(6.0m, 6.5m, 6.0m), finished.EstimatedBand);
        Assert.NotNull(finished.Assessment);
        Assert.NotEmpty(finished.Assessment!.Strengths);
        Assert.NotEmpty(finished.Assessment.Suggestions);
        Assert.True(finished.Assessment.WordsPerMinute > 0);
        Assert.Null(finished.CurrentQuestion);

        Assert.Contains((await student.GetFromJsonAsync<List<SummaryDto>>("/api/ai/speaking/sessions"))!, s => s.Id == session.Id);
    }

    [Fact]
    public async Task Recordings_AreValidated_AndSessionsStayPrivate()
    {
        var student = await AuthenticatedClientAsync("student@demo-center.test");
        var otherStudent = await AuthenticatedClientAsync("javlon.karimov@demo-center.test");
        var teacher = await AuthenticatedClientAsync("teacher@demo-center.test");

        var session = (await (await student.PostAsJsonAsync("/api/ai/speaking/sessions", new { mode = "GeneralEnglish" }))
            .Content.ReadFromJsonAsync<SessionDto>())!;

        Assert.Equal(HttpStatusCode.BadRequest, (await AnswerAsync(student, session.Id, "answer.exe", 10)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherStudent.GetAsync($"/api/ai/speaking/sessions/{session.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AnswerAsync(otherStudent, session.Id, "answer.webm", 10)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await teacher.PostAsJsonAsync("/api/ai/speaking/sessions", new { mode = "IeltsPart1" })).StatusCode);

        // Nothing to assess yet.
        Assert.Equal(HttpStatusCode.Conflict, (await student.PostAsync($"/api/ai/speaking/sessions/{session.Id}/finish", null)).StatusCode);

        var abandoned = (await (await student.PostAsync($"/api/ai/speaking/sessions/{session.Id}/abandon", null))
            .Content.ReadFromJsonAsync<SessionDto>())!;
        Assert.Equal("Abandoned", abandoned.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await AnswerAsync(student, session.Id, "answer.webm", 10)).StatusCode);
    }

    [Fact]
    public async Task StartingAPractice_ClosesAnOlderUnfinishedOne()
    {
        var student = await AuthenticatedClientAsync("feruza.tosheva@demo-center.test");

        var first = (await (await student.PostAsJsonAsync("/api/ai/speaking/sessions", new { mode = "IeltsPart3" }))
            .Content.ReadFromJsonAsync<SessionDto>())!;
        var second = (await (await student.PostAsJsonAsync("/api/ai/speaking/sessions", new { mode = "IeltsPart1" }))
            .Content.ReadFromJsonAsync<SessionDto>())!;

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal("Abandoned", (await student.GetFromJsonAsync<SessionDto>($"/api/ai/speaking/sessions/{first.Id}"))!.Status);
        Assert.Equal("Active", (await student.GetFromJsonAsync<SessionDto>($"/api/ai/speaking/sessions/{second.Id}"))!.Status);
    }

    // ---- Full Mock and answer relevance (S08.02) ------------------------------------------------------------------------------

    [Fact]
    public async Task FullMock_RunsThreePartsInOrder_AndPart3ContinuesTheTopicOfThePart2CueCard()
    {
        var c = await world.CreateAsync();
        var student = await world.ClientAsync(c.First.Email);

        var started = await student.PostAsJsonAsync("/api/ai/speaking/sessions", new { mode = "FullMock" });
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        var session = (await started.Content.ReadFromJsonAsync<SessionDto>())!;
        Assert.Equal(11, session.TotalTurns); // 5 questions, one cue card, 5 questions
        Assert.Equal(1, session.CurrentPart);

        SessionDto state = session;
        for (var answered = 1; answered <= 11; answered++)
        {
            var response = await AnswerAsync(student, session.Id, "answer.webm", 32);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            state = (await response.Content.ReadFromJsonAsync<SessionDto>())!;

            if (answered == 5) // Part 1 is over: the cue card, with its preparation minute
            {
                Assert.Equal(2, state.CurrentPart);
                Assert.Contains("You should say", state.CurrentQuestion);
                Assert.Contains("one minute to prepare", state.CurrentQuestion);
            }
            else if (answered == 6) // Part 3 takes its topic from the card the student has just spoken about
            {
                Assert.Equal(3, state.CurrentPart);
                Assert.Contains("a place you would like to visit", state.CurrentQuestion);
            }
            else if (answered < 5) Assert.Equal(1, state.CurrentPart);
            else if (answered < 11) Assert.Equal(3, state.CurrentPart);
        }

        Assert.Equal(11, state.TurnCount);
        Assert.Null(state.CurrentQuestion); // the test is over: nothing more is asked
        Assert.Equal([1, 1, 1, 1, 1, 2, 3, 3, 3, 3, 3], state.Turns.Select(t => t.Part));

        var finished = (await (await student.PostAsync($"/api/ai/speaking/sessions/{session.Id}/finish", null))
            .Content.ReadFromJsonAsync<SessionDto>())!;
        Assert.Equal("Completed", finished.Status);
        Assert.Equal([1, 2, 3], finished.Assessment!.Parts!.Select(p => p.Part));
        Assert.All(finished.Assessment.Parts!, p => Assert.False(string.IsNullOrWhiteSpace(p.Comment)));

        // Fifteen is the ceiling for a Full Mock, twelve for everything else; the planned 11 sits under it.
        Assert.Equal(15, SpeakingSession.MaxTurnsFor(SpeakingMode.FullMock));
        Assert.Equal(12, SpeakingSession.MaxTurnsFor(SpeakingMode.IeltsPart1));
        Assert.True(SpeakingScript.TurnsFor(SpeakingMode.FullMock) <= SpeakingSession.MaxTurnsFor(SpeakingMode.FullMock));
    }

    [Fact]
    public async Task FullMock_DoesNotStart_WhenTheDailyAllowanceCannotCoverIt()
    {
        var c = await world.CreateAsync();
        var student = await world.ClientAsync(c.First.Email);
        await SpendSpeakingMinutesAsync(c.First, 10); // 10 of the default 20 minutes are left; a Full Mock needs 14

        var refused = await student.PostAsJsonAsync("/api/ai/speaking/sessions", new { mode = "FullMock" });
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        var reason = (await refused.Content.ReadFromJsonAsync<ErrorDto>())!.Error;
        Assert.Contains("Full Mock", reason);
        Assert.Contains("10 daqiqa", reason);

        // Nothing was started, and the shorter modes are still there.
        Assert.Empty((await student.GetFromJsonAsync<List<SummaryDto>>("/api/ai/speaking/sessions"))!);
        Assert.Equal(HttpStatusCode.Created, (await student.PostAsJsonAsync("/api/ai/speaking/sessions", new { mode = "IeltsPart1" })).StatusCode);
    }

    [Fact]
    public async Task Relevance_IsShownBesideTheThreeCriteria_AndIsNotPartOfTheEstimate()
    {
        var student = await AuthenticatedClientAsync("student@demo-center.test");
        var session = (await (await student.PostAsJsonAsync("/api/ai/speaking/sessions", new { mode = "IeltsPart3" }))
            .Content.ReadFromJsonAsync<SessionDto>())!;
        Assert.Equal(3, session.CurrentPart);

        Assert.Equal(HttpStatusCode.OK, (await AnswerAsync(student, session.Id, "answer.webm", 30)).StatusCode);
        var finished = (await (await student.PostAsync($"/api/ai/speaking/sessions/{session.Id}/finish", null))
            .Content.ReadFromJsonAsync<SessionDto>())!;

        Assert.Equal(7.5m, finished.RelevanceBand);
        Assert.False(string.IsNullOrWhiteSpace(finished.Assessment!.RelevanceComment));
        Assert.Null(finished.Assessment.Parts); // only a Full Mock is commented part by part

        // The mean of the three IELTS criteria (6.0, 6.5, 6.0), not of four: with relevance in it the estimate would be 6.5.
        Assert.Equal(WritingRubric.OverallBand(6.0m, 6.5m, 6.0m), finished.EstimatedBand);
        Assert.NotEqual(WritingRubric.OverallBand(6.0m, 6.5m, 6.0m, 7.5m), finished.EstimatedBand);
    }

    [Fact]
    public async Task AnAssessmentWithoutRelevance_IsRefusedAndRetried_ThenTheSessionFailsWithItsTranscriptKept()
    {
        var c = await world.CreateAsync();

        // A model still answering in the older shape (no "relevance"): the same output validation that guards every other reply.
        await using var oldShape = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.PostConfigure<AiOptions>(o =>
            {
                o.Providers["incomplete"] = new AiProviderOptions { Type = AiProviderType.Fake, FailureMode = FakeFailureMode.IncompleteAssessment };
                o.Routes["SpeakingReply"] = ["incomplete"];
            })));
        var client = await world.ClientOnAsync(oldShape, c.First.Email);

        var session = (await (await client.PostAsJsonAsync("/api/ai/speaking/sessions", new { mode = "IeltsPart1" }))
            .Content.ReadFromJsonAsync<SessionDto>())!;
        Assert.Equal(HttpStatusCode.OK, (await AnswerAsync(client, session.Id, "answer.webm", 30)).StatusCode);

        var finish = await client.PostAsync($"/api/ai/speaking/sessions/{session.Id}/finish", null);
        Assert.Equal(HttpStatusCode.BadGateway, finish.StatusCode);

        var failed = (await client.GetFromJsonAsync<SessionDto>($"/api/ai/speaking/sessions/{session.Id}"))!;
        Assert.Equal("Failed", failed.Status);
        Assert.Null(failed.RelevanceBand);
        Assert.NotNull(failed.Turns[0].Transcript); // the answer is kept, so the assessment can be asked for again

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var request = await db.AiRequests.Where(r => r.UserId == c.First.UserId && r.PromptVersion == SpeakingScript.AssessmentPromptVersion)
            .SingleAsync();
        Assert.Equal((AiRequestStatus.Failed, AiOutcomeReason.InvalidOutput), (request.Status, request.Reason));
        Assert.Equal(2, await db.AiUsages.CountAsync(u => u.RequestId == request.Id)); // the answer, and its one retry
    }

    [Fact]
    public void TheAssessmentSchema_AsksForRelevance_AndForEveryPartOfAFullMock()
    {
        SpeakingCriterionOutput Criterion(decimal band) => new() { Band = band, Comment = "ok" };
        SpeakingAssessmentOutput Answer(decimal? relevance = 6, bool parts = false) => new()
        {
            FluencyCoherence = Criterion(6), LexicalResource = Criterion(6), GrammaticalRange = Criterion(6),
            Relevance = relevance is { } band ? Criterion(band) : null, Strengths = [], Weaknesses = [], Suggestions = [],
            Parts = parts ? new[] { 1, 2, 3 }.Select(p => new SpeakingPartOutput { Part = p, Comment = "ok" }).ToList() : null
        };

        Assert.Null(SpeakingScript.ValidateAssessment(Answer()));
        Assert.Contains("relevance", SpeakingScript.ValidateAssessment(Answer(relevance: null)));
        Assert.Contains("between 0 and 9", SpeakingScript.ValidateAssessment(Answer(relevance: 10)));

        // Parts are only asked of a Full Mock — and there, every one of them.
        Assert.Null(SpeakingScript.ValidateAssessment(Answer(), SpeakingMode.IeltsPart1));
        Assert.Contains("part 1", SpeakingScript.ValidateAssessment(Answer(), SpeakingMode.FullMock));
        Assert.Null(SpeakingScript.ValidateAssessment(Answer(parts: true), SpeakingMode.FullMock));
    }

    [Fact]
    public async Task ASessionAssessedBeforeRelevanceAndParts_StillReadsAndShows()
    {
        var c = await world.CreateAsync();
        var student = await world.ClientAsync(c.First.Email);

        // What speaking.assessment.v2 wrote: three criteria, no relevance, no parts, and turns that never recorded their part.
        const string legacy = """
            {"fluencyComment":"Uzoq gapirdi.","lexicalComment":"Lug'at yaxshi.","grammarComment":"Grammatika o'rtacha.",
             "strengths":["Aniq javob"],"weaknesses":["Pauzalar"],"suggestions":["Ko'proq mashq"],
             "spokenSeconds":42,"wordsPerMinute":118,"longPauses":2,"turnCount":1}
            """;
        Guid sessionId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var organizationId = await db.Students.Where(s => s.Id == c.First.Id).Select(s => s.OrganizationId).SingleAsync();
            var session = new SpeakingSession(organizationId, c.First.Id, c.First.UserId, SpeakingMode.IeltsPart2, "Old topic");
            var turn = new SpeakingTurn(organizationId, session.Id, 1, "Describe a person you admire.");
            turn.Answer("old-recording.webm", "I admire my grandmother.", 42, 118, 2);
            session.RecordTurn(42);
            session.Complete(6.0m, 6.0m, 6.0m, 6.0m, legacy);
            db.SpeakingSessions.Add(session);
            db.SpeakingTurns.Add(turn);
            await db.SaveChangesAsync();
            sessionId = session.Id;
        }

        var read = await student.GetAsync($"/api/ai/speaking/sessions/{sessionId}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var old = (await read.Content.ReadFromJsonAsync<SessionDto>())!;
        Assert.Equal(6.0m, old.EstimatedBand);
        Assert.Null(old.RelevanceBand);
        Assert.Equal(118, old.Assessment!.WordsPerMinute);
        Assert.Equal("Uzoq gapirdi.", old.Assessment.FluencyComment);
        Assert.Null(old.Assessment.RelevanceComment);
        Assert.Null(old.Assessment.Parts);
        Assert.Equal(2, old.Turns[0].Part); // derived from the mode: this turn never recorded one
    }

    // ---- The examiner's question read aloud (S08.01) --------------------------------------------------------------------------

    [Fact]
    public async Task QuestionAudio_IsReadAloud_AndTheSecondRequestComesFromTheCache()
    {
        var c = await world.CreateAsync();
        var student = await world.ClientAsync(c.First.Email);
        var question = $"Describe a trip you remember well ({Guid.NewGuid():N}).";
        var (sessionId, turnId) = await SeedSessionAsync(c.First, question);
        var path = $"/api/ai/speaking/sessions/{sessionId}/turns/{turnId}/question-audio";

        var first = await student.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.StartsWith("audio/", first.Content.Headers.ContentType!.MediaType);
        var recording = await first.Content.ReadAsByteArrayAsync();
        Assert.True(recording.Length > 44);

        var second = await student.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(recording, await second.Content.ReadAsByteArrayAsync());

        // The provider was asked once: a replay is served from the cache without a gateway call.
        Assert.Equal(1, await SynthesisRequestCountAsync(c.First.UserId));

        // The cache is keyed by the words and the voice, not by the session — the same question in another student's practice costs nothing.
        var other = await world.CreateAsync();
        var otherClient = await world.ClientAsync(other.First.Email);
        var (otherSession, otherTurn) = await SeedSessionAsync(other.First, question);
        var served = await otherClient.GetAsync($"/api/ai/speaking/sessions/{otherSession}/turns/{otherTurn}/question-audio");
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal(recording, await served.Content.ReadAsByteArrayAsync());
        Assert.Equal(0, await SynthesisRequestCountAsync(other.First.UserId));
    }

    [Fact]
    public async Task QuestionAudio_BelongsToTheSessionOwner()
    {
        var c = await world.CreateAsync();
        var (sessionId, turnId) = await SeedSessionAsync(c.First, $"What do you do in your free time? ({Guid.NewGuid():N})");
        var path = $"/api/ai/speaking/sessions/{sessionId}/turns/{turnId}/question-audio";

        var otherStudent = await world.ClientAsync((await world.CreateAsync()).First.Email);
        Assert.Equal(HttpStatusCode.NotFound, (await otherStudent.GetAsync(path)).StatusCode);

        var teacher = await world.ClientAsync("teacher@demo-center.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync(path)).StatusCode);

        // A turn is reachable only through the session it belongs to, even for the person who owns both.
        var owner = await world.ClientAsync(c.First.Email);
        var (mySession, _) = await SeedSessionAsync(c.First, $"Another question ({Guid.NewGuid():N})");
        Assert.Equal(HttpStatusCode.NotFound,
            (await owner.GetAsync($"/api/ai/speaking/sessions/{mySession}/turns/{turnId}/question-audio")).StatusCode);
    }

    [Fact]
    public async Task QuestionAudio_WithoutAVoiceProvider_Answers503_AndCostsNothing()
    {
        var c = await world.CreateAsync();
        var (sessionId, turnId) = await SeedSessionAsync(c.First, $"Tell me about your hometown ({Guid.NewGuid():N}).");

        // The suite names the fake voice; this host does not, which is how a centre that never set one up looks.
        await using var withoutVoice = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.PostConfigure<AiOptions>(o =>
            {
                foreach (var route in o.Routes.Keys.Where(k => k.Equals("SpeechSynthesis", StringComparison.OrdinalIgnoreCase)).ToList())
                    o.Routes.Remove(route);
            })));
        var client = await world.ClientOnAsync(withoutVoice, c.First.Email);

        var response = await client.GetAsync($"/api/ai/speaking/sessions/{sessionId}/turns/{turnId}/question-audio");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace((await response.Content.ReadFromJsonAsync<ErrorDto>())!.Error));

        // Practice itself is untouched: the question is still there as text.
        var session = (await client.GetFromJsonAsync<SessionDto>($"/api/ai/speaking/sessions/{sessionId}"))!;
        Assert.NotNull(session.CurrentQuestion);
    }

    [Fact]
    public async Task QuestionAudio_IsJournaledInCharacters_AndStopsWhenTheDailyAllowanceIsSpent()
    {
        var c = await world.CreateAsync();
        var student = await world.ClientAsync(c.First.Email);
        var question = $"Do you prefer studying alone or with friends? ({Guid.NewGuid():N})";
        var (sessionId, turnId) = await SeedSessionAsync(c.First, question);
        var path = $"/api/ai/speaking/sessions/{sessionId}/turns/{turnId}/question-audio";

        Assert.Equal(HttpStatusCode.OK, (await student.GetAsync(path)).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var journaled = await db.AiRequests.SingleAsync(r => r.UserId == c.First.UserId && r.TaskKind == AiTaskKind.SpeechSynthesis);
            Assert.Equal(AiFeature.Speaking, journaled.Feature);
            Assert.Equal(AiRequestStatus.Succeeded, journaled.Status);
            Assert.Equal("fake", journaled.Provider);
            Assert.Equal(question.Length, journaled.Characters);
            // Characters are not tokens and not seconds of audio: neither of those totals moves.
            Assert.Equal((0, 0), (journaled.InputTokens, journaled.AudioSeconds));
        }

        // Spend the whole allowance on recordings (20 minutes by default): a question that is not cached yet is no longer read.
        await SpendSpeakingMinutesAsync(c.First, 21);
        var (nextSession, nextTurn) = await SeedSessionAsync(c.First, $"What is your favourite season? ({Guid.NewGuid():N})");
        var refused = await student.GetAsync($"/api/ai/speaking/sessions/{nextSession}/turns/{nextTurn}/question-audio");
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);

        // …but one that already has its recording keeps playing: replays are free.
        Assert.Equal(HttpStatusCode.OK, (await student.GetAsync(path)).StatusCode);
    }

    [Fact]
    public void VoiceIsOptIn_TheSynthesisRouteNeverFallsBackToTheChatRoute()
    {
        var options = new AiOptions
        {
            AllowFakeProvider = true,
            Providers = { ["fake"] = new AiProviderOptions { Type = AiProviderType.Fake } },
            Routes = { ["Default"] = ["fake"] }
        };
        var registry = new AiProviderRegistry(Options.Create(options), httpClientFactory: null!);

        Assert.Empty(registry.SynthesisRoute());
        Assert.NotEmpty(registry.SpeechRoute()); // every other task does use the default chain

        options.Routes["SpeechSynthesis"] = ["fake:nova"];
        var target = Assert.Single(new AiProviderRegistry(Options.Create(options), httpClientFactory: null!).SynthesisRoute());
        Assert.Equal("nova", target.Voice);
    }

    [Fact]
    public async Task OpenAiCompatibleTts_SendsTheSpeechRequest_AndRefusesAnAnswerThatIsNotAudio()
    {
        var options = new AiProviderOptions
        {
            Type = AiProviderType.OpenAiCompatibleTts, BaseUrl = "https://voice.test/v1/", ApiKey = "secret", SpeechModel = "tts-1-hd"
        };

        var handler = new StubHandler(_ => Reply("audio/mpeg", [1, 2, 3]));
        var provider = new OpenAiCompatibleTtsProvider("tts", options, new StubHttpClientFactory(handler));
        var speech = await provider.SynthesizeAsync("Hello there.", "nova", "en", CancellationToken.None);

        Assert.Equal(new byte[] { 1, 2, 3 }, speech.Audio);
        Assert.Equal("audio/mpeg", speech.ContentType);
        var sent = Assert.Single(handler.Requests);
        Assert.Equal("https://voice.test/v1/audio/speech", sent.Uri);
        Assert.Equal("Bearer secret", sent.Authorization);
        Assert.Contains("\"voice\":\"nova\"", sent.Body);
        Assert.Contains("\"model\":\"tts-1-hd\"", sent.Body);
        Assert.Contains("\"input\":\"Hello there.\"", sent.Body);

        async Task<string> CodeOfAsync(HttpResponseMessage response) =>
            (await Assert.ThrowsAsync<AiProviderException>(() =>
                new OpenAiCompatibleTtsProvider("tts", options, new StubHttpClientFactory(new StubHandler(_ => response)))
                    .SynthesizeAsync("Hi", "nova", "en", CancellationToken.None))).Code;

        Assert.Equal("MALFORMED_RESPONSE", await CodeOfAsync(Reply("application/json", "{\"error\":\"boom\"}"u8.ToArray())));
        Assert.Equal("EMPTY_RESPONSE", await CodeOfAsync(Reply("audio/mpeg", [])));
        Assert.Equal("HTTP_500", await CodeOfAsync(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
    }

    private static HttpResponseMessage Reply(string contentType, byte[] body)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return response;
    }

    private sealed record SentRequest(string Uri, string? Authorization, string Body);

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<SentRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new SentRequest(request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(),
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            return respond(request);
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private async Task<(Guid SessionId, Guid TurnId)> SeedSessionAsync(LmsWorld.Student student, string question)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var organizationId = await db.Students.Where(s => s.Id == student.Id).Select(s => s.OrganizationId).SingleAsync();

        var session = new SpeakingSession(organizationId, student.Id, student.UserId, SpeakingMode.IeltsPart1, "Practice");
        var turn = new SpeakingTurn(organizationId, session.Id, 1, question);
        db.SpeakingSessions.Add(session);
        db.SpeakingTurns.Add(turn);
        await db.SaveChangesAsync();
        return (session.Id, turn.Id);
    }

    private async Task<int> SynthesisRequestCountAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AiRequests.CountAsync(r => r.UserId == userId && r.TaskKind == AiTaskKind.SpeechSynthesis);
    }

    /// <summary>Minutes of transcribed answers already spent today, against the default allowance of twenty.</summary>
    private async Task SpendSpeakingMinutesAsync(LmsWorld.Student student, int minutes)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var organizationId = await db.Students.Where(s => s.Id == student.Id).Select(s => s.OrganizationId).SingleAsync();

        var spent = new AiRequest(organizationId, null, student.UserId, student.Id, AiFeature.Speaking, AiTaskKind.Transcription, "speaking.transcribe.v1");
        spent.Succeed("fake", "asr", 10, audioSeconds: minutes * 60);
        db.AiRequests.Add(spent);
        await db.SaveChangesAsync();
    }

    private static Task<HttpResponseMessage> AnswerAsync(HttpClient client, Guid sessionId, string fileName, int seconds)
    {
        var form = new MultipartFormDataContent();
        var audio = new ByteArrayContent(TestFiles.Webm(2048));
        audio.Headers.ContentType = new MediaTypeHeaderValue("audio/webm");
        form.Add(audio, "audio", fileName);
        form.Add(new StringContent(seconds.ToString()), "durationSeconds");
        return client.PostAsync($"/api/ai/speaking/sessions/{sessionId}/turns", form);
    }

    private async Task<HttpClient> AuthenticatedClientAsync(string email)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = DbSeeder.DemoPassword });
        var tokens = await response.Content.ReadFromJsonAsync<TokenResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        return client;
    }

    private record ErrorDto(string Error);
    private record TokenResponseDto(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken);
    private record TurnDto(Guid Id, int Ordinal, string Question, string? Transcript, int SpokenSeconds, int? WordsPerMinute, bool HasAudio,
        int? Part = null);
    private record PartCommentDto(int Part, string? Comment);
    private record AssessmentDto(List<string> Strengths, List<string> Weaknesses, List<string> Suggestions, int SpokenSeconds, int WordsPerMinute,
        string? FluencyComment = null, string? RelevanceComment = null, List<PartCommentDto>? Parts = null);
    private record SessionDto(Guid Id, string Status, int TurnCount, int TotalTurns, int SpokenSeconds, decimal? EstimatedBand,
        decimal? FluencyBand, decimal? LexicalBand, decimal? GrammarBand, List<TurnDto> Turns, AssessmentDto? Assessment, string? CurrentQuestion,
        decimal? RelevanceBand = null, int? CurrentPart = null);
    private record SummaryDto(Guid Id, string Status, decimal? EstimatedBand);
}
