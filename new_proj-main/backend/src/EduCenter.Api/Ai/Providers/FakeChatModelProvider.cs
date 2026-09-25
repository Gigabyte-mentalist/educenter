using System.Text.Json;
using System.Text.RegularExpressions;

namespace EduCenter.Api.Ai.Providers;

/// <summary>
/// Stands in for a model while no provider key exists (docs/06_phase4_technical_plan.md §5) and in tests: the same input
/// always gives the same answer, token counts are estimated from text length, and the cost is zero. Features add their
/// canned answers here by prompt version as they land.
/// </summary>
public sealed class FakeChatModelProvider(string name, FakeFailureMode failureMode) : IChatModelProvider
{
    public const string Model = "fake-deterministic";

    public string Name => name;
    public string? DefaultChatModel => Model;

    public Task<ChatCompletionResponse> CompleteAsync(ChatCompletionRequest request, CancellationToken ct)
    {
        if (failureMode == FakeFailureMode.AlwaysFail)
            throw new AiProviderException("FAKE_FAILURE", $"{name} is configured to fail.");

        var lastUserMessage = request.Messages.LastOrDefault(m => m.Role == AiChatRole.User)?.Content ?? "";
        var excerpt = lastUserMessage.Length <= 200 ? lastUserMessage : lastUserMessage[..200];

        var content = request switch
        {
            // The tutor's topic classifier: everything is in domain unless the question mentions a clearly unrelated field,
            // so tests can exercise both branches without a model.
            { JsonOutput: true } when request.PromptVersion.StartsWith("tutor.topic", StringComparison.Ordinal) =>
                JsonSerializer.Serialize(new
                {
                    inDomain = !Regex.IsMatch(lastUserMessage, @"\b(politics|medicine|crypto|football|recipe|siyosat|futbol)\b",
                        RegexOptions.IgnoreCase),
                    topic = "english"
                }),
            { JsonOutput: true } when request.PromptVersion.StartsWith("speaking.examiner", StringComparison.Ordinal) =>
                ExaminerQuestion(request),
            { JsonOutput: true } when request.PromptVersion.StartsWith("speaking.assessment", StringComparison.Ordinal) =>
                AssessSpeaking(request),
            { JsonOutput: true } when request.PromptVersion.StartsWith("writing.", StringComparison.Ordinal) =>
                WritingAssessment(UserInput(request, "essay") ?? lastUserMessage),
            { JsonOutput: true } => JsonSerializer.Serialize(new { echo = excerpt, promptVersion = request.PromptVersion }),
            _ => $"OK — fake answer for {request.PromptVersion}."
        };

        var inputChars = request.Messages.Sum(m => m.Content.Length);
        return Task.FromResult(new ChatCompletionResponse(content, EstimateTokens(inputChars), EstimateTokens(content.Length)));
    }

    /// <summary>The examiner's next question. A Full Mock names its part in the instructions ("You are in Part 2 of 3 of the test"), and
    /// the fake follows it: Part 2 is a cue card, and Part 3 talks about the topic of the card it was handed — so a test can see the parts
    /// run in order and Part 3 pick up Part 2's topic.</summary>
    private static string ExaminerQuestion(ChatCompletionRequest request)
    {
        var instructions = request.Messages.FirstOrDefault(m => m.Role == AiChatRole.System)?.Content ?? "";
        var part = Regex.Match(instructions, @"You are in Part (\d) of 3 of the test");

        if (part.Success && part.Groups[1].Value == "2")
            return JsonSerializer.Serialize(new
            {
                question = "Describe a place you would like to visit.\nYou should say:\n- where it is\n- why you want to go there\n"
                           + "- what you would do there\nand explain why this place appeals to you.\n"
                           + "You have one minute to prepare, then speak for one to two minutes.",
                topic = "a place you would like to visit"
            });

        if (part.Success && part.Groups[1].Value == "3")
        {
            var card = UserInput(request, "part2_cue_card") ?? "";
            var topic = Regex.Match(card, @"^Describe (.+?)\.").Groups[1].Value;
            return JsonSerializer.Serialize(new
            {
                question = $"Thinking about {(topic.Length > 0 ? topic : "that topic")}: why do some people prefer to do this abroad rather than at home?",
                topic
            });
        }

        return JsonSerializer.Serialize(new { question = "Let's talk about your studies. What do you enjoy most about learning English?", topic = "studies" });
    }

    /// <summary>A fixed assessment in the shape the current prompt asks for: relevance always, a comment per part when the prompt asks for
    /// them (a Full Mock). In <see cref="FakeFailureMode.IncompleteAssessment"/> it is the older shape, which the gateway must refuse.</summary>
    private string AssessSpeaking(ChatCompletionRequest request)
    {
        var instructions = request.Messages.FirstOrDefault(m => m.Role == AiChatRole.System)?.Content ?? "";
        var answer = new Dictionary<string, object?>
        {
            ["fluencyCoherence"] = new { band = 6.0m, comment = "Fake provider: you keep going, with some hesitation before longer ideas." },
            ["lexicalResource"] = new { band = 6.5m, comment = "Fake provider: everyday vocabulary is used accurately." },
            ["grammaticalRange"] = new { band = 6.0m, comment = "Fake provider: simple sentences are accurate, complex ones less so." },
            ["strengths"] = new[] { "You answer the question directly.", "Your ideas are easy to follow." },
            ["weaknesses"] = new[] { "Few linking words.", "Some answers are short." },
            ["suggestions"] = new[] { "Add one example to every answer.", "Practise past tenses.", "Slow down on long sentences." }
        };

        if (failureMode == FakeFailureMode.IncompleteAssessment) return JsonSerializer.Serialize(answer);

        answer["relevance"] = new { band = 7.5m, comment = "Fake provider: the answers stay on the questions that were asked." };
        if (instructions.Contains("\"parts\":", StringComparison.Ordinal))
            answer["parts"] = new[]
            {
                new { part = 1, comment = "Fake provider: Part 1 answers are short but direct." },
                new { part = 2, comment = "Fake provider: the cue card talk covers most of the points." },
                new { part = 3, comment = "Fake provider: Part 3 opinions need reasons and examples." }
            };
        return JsonSerializer.Serialize(answer);
    }

    /// <summary>The text the gateway placed inside &lt;user_input name="…"&gt; delimiters.</summary>
    private static string? UserInput(ChatCompletionRequest request, string label)
    {
        var opening = $"<user_input name=\"{label}\">\n";
        foreach (var message in request.Messages.Where(m => m.Role == AiChatRole.User))
        {
            var start = message.Content.IndexOf(opening, StringComparison.Ordinal);
            if (start < 0) continue;
            start += opening.Length;
            var end = message.Content.IndexOf("\n</user_input>", start, StringComparison.Ordinal);
            if (end > start) return message.Content[start..end];
        }

        return null;
    }

    /// <summary>A plausible, reproducible IELTS assessment derived from simple text statistics — not a real judgement.
    /// Its grammar issue quotes the essay verbatim; its vocabulary issue deliberately does not, and must be filtered out.</summary>
    private static string WritingAssessment(string essay)
    {
        var words = Regex.Matches(essay, @"[\p{L}\p{N}]+").Select(m => m.Value.ToLowerInvariant()).ToList();
        var uniqueRatio = words.Count == 0 ? 0 : (double)words.Distinct().Count() / words.Count;
        var paragraphs = essay.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
        var opening = Regex.Match(essay, @"^\s*(\S+(?: \S+){0,4})").Groups[1].Value;

        return JsonSerializer.Serialize(new
        {
            taskResponse = new { band = words.Count >= 250 ? 7.0m : words.Count >= 150 ? 6.5m : 5.5m, comment = "Fake provider: the position is clear but ideas need more development." },
            coherenceCohesion = new { band = paragraphs >= 3 ? 6.5m : 6.0m, comment = "Fake provider: use more paragraphs and linking words." },
            lexicalResource = new { band = uniqueRatio >= 0.6 ? 6.5m : 6.0m, comment = "Fake provider: vocabulary is adequate for the task." },
            grammaticalRange = new { band = 6.0m, comment = "Fake provider: a mix of simple and complex sentences with some errors." },
            strengths = new[] { "The main idea is easy to follow.", "Examples support the argument." },
            weaknesses = new[] { "Some ideas are not fully developed.", "Paragraphing could be clearer." },
            grammarIssues = new[] { new { excerpt = opening, correction = opening, explanation = "Fake provider: example issue quoted from the essay." } },
            vocabularyIssues = new[] { new { excerpt = "phrase that is not in the essay", correction = "-", explanation = "Fake provider: must be filtered out." } },
            suggestions = new[] { "Add a clear conclusion.", "Use a wider range of linking words.", "Check subject-verb agreement." }
        });
    }

    private static int EstimateTokens(int characters) => Math.Max(1, (characters + 3) / 4);
}
