using System.Text.Json;
using System.Text.RegularExpressions;
using EduCenter.Modules.Ai.Domain;

namespace EduCenter.Api.Ai.Writing;

public sealed class WritingCriterionOutput
{
    public decimal? Band { get; set; }
    public string? Comment { get; set; }
}

public sealed class WritingIssueOutput
{
    public string? Excerpt { get; set; }
    public string? Correction { get; set; }
    public string? Explanation { get; set; }
}

/// <summary>The JSON object the model must return (schema checked by <see cref="WritingRubric.Validate"/>).</summary>
public sealed class WritingModelOutput
{
    public WritingCriterionOutput? TaskResponse { get; set; }
    public WritingCriterionOutput? CoherenceCohesion { get; set; }
    public WritingCriterionOutput? LexicalResource { get; set; }
    public WritingCriterionOutput? GrammaticalRange { get; set; }
    public List<string>? Strengths { get; set; }
    public List<string>? Weaknesses { get; set; }
    public List<WritingIssueOutput>? GrammarIssues { get; set; }
    public List<WritingIssueOutput>? VocabularyIssues { get; set; }
    public List<string>? Suggestions { get; set; }
}

public sealed record WritingIssue(string Excerpt, string Correction, string Explanation);

/// <summary>What is stored in <see cref="WritingEvaluation.FeedbackJson"/> — cleaned and capped model feedback.</summary>
public sealed record WritingFeedback(
    string? TaskResponseComment, string? CoherenceComment, string? LexicalComment, string? GrammarComment,
    List<string> Strengths, List<string> Weaknesses, List<WritingIssue> GrammarIssues, List<WritingIssue> VocabularyIssues,
    List<string> Suggestions);

public sealed record WritingScore(decimal TaskResponse, decimal Coherence, decimal Lexical, decimal Grammar, decimal Overall, WritingFeedback Feedback);

/// <summary>
/// Writing AI prompt, schema and scoring rules (docs/00_TZ.md §33, docs/06_phase4_technical_plan.md Sprint 2). The model
/// only rates the four criteria; the overall band is computed here with the IELTS rounding rule, so it can never
/// disagree with the criteria. Issues whose excerpt is not a verbatim quote of the essay are dropped.
/// </summary>
public static class WritingRubric
{
    public const int MinWords = 50;
    public const int MaxWords = 1200;

    public static readonly JsonSerializerOptions FeedbackJsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly HashSet<string> EnglishFunctionWords =
    [
        "the", "a", "an", "and", "or", "but", "is", "are", "was", "were", "be", "to", "of", "in", "on", "for", "with",
        "that", "this", "it", "as", "at", "by", "not", "have", "has", "can", "will", "i", "you", "they", "we", "their", "should"
    ];

    /// <summary>v2 writes its feedback in Uzbek; the version is part of every evaluation row, so benchmarks never compare
    /// results produced by different prompts.</summary>
    public static string PromptVersion(WritingTaskType type) => type switch
    {
        WritingTaskType.IeltsTask1 => "writing.ielts-task1.v2",
        WritingTaskType.IeltsTask2 => "writing.ielts-task2.v2",
        _ => "writing.general.v2"
    };

    public static int CountWords(string text) => Regex.Matches(text, @"[\p{L}\p{N}]+(?:['’-][\p{L}\p{N}]+)*").Count;

    /// <returns>A user-facing problem, or null when the task and essay can be assessed.</returns>
    public static string? ValidateSubmission(string? taskPrompt, string? essay)
    {
        if (string.IsNullOrWhiteSpace(taskPrompt)) return "Topshiriq matni kiritilishi shart.";
        if (taskPrompt.Length > WritingEvaluation.MaxTaskPromptLength)
            return $"Topshiriq matni juda uzun (ko'pi bilan {WritingEvaluation.MaxTaskPromptLength} belgi).";
        if (string.IsNullOrWhiteSpace(essay)) return "Insho matni kiritilishi shart.";
        if (essay.Length > WritingEvaluation.MaxEssayLength)
            return $"Insho juda uzun (ko'pi bilan {WritingEvaluation.MaxEssayLength} belgi).";

        var words = CountWords(essay);
        if (words < MinWords) return $"Insho kamida {MinWords} so'zdan iborat bo'lishi kerak (hozir {words}).";
        if (words > MaxWords) return $"Insho {MaxWords} so'zdan oshmasligi kerak (hozir {words}).";
        if (!LooksEnglish(essay)) return "Insho ingliz tilida yozilishi kerak.";
        return null;
    }

    /// <summary>Latin script and a normal share of English function words — rejects Cyrillic and Uzbek/Russian essays
    /// before a paid call, while tolerating the odd foreign name.</summary>
    private static bool LooksEnglish(string text)
    {
        var letters = text.Where(char.IsLetter).ToList();
        if (letters.Count == 0) return false;
        if (letters.Count(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z') < letters.Count * 0.9) return false;

        var words = Regex.Matches(text.ToLowerInvariant(), @"[a-z]+").Select(m => m.Value).ToList();
        return words.Count > 0 && words.Count(EnglishFunctionWords.Contains) >= words.Count * 0.08;
    }

    /// <summary>Homework has no task-type field: "task 1"/"task 2" in its text decides, otherwise an IELTS course means Task 2.</summary>
    public static WritingTaskType InferTaskType(string title, string? description, string? courseLevelCategory)
    {
        var text = $"{title} {description}".ToLowerInvariant();
        if (Regex.IsMatch(text, @"task\s*1")) return WritingTaskType.IeltsTask1;
        if (Regex.IsMatch(text, @"task\s*2")) return WritingTaskType.IeltsTask2;
        return string.Equals(courseLevelCategory, "IELTS", StringComparison.OrdinalIgnoreCase)
            ? WritingTaskType.IeltsTask2
            : WritingTaskType.General;
    }

    public static AiPrompt BuildPrompt(WritingEvaluation evaluation) =>
        new(evaluation.PromptVersion, Instructions(evaluation.TaskType, evaluation.WordCount),
            [
                new AiUntrustedInput("task", evaluation.TaskPrompt, WritingEvaluation.MaxTaskPromptLength),
                new AiUntrustedInput("essay", evaluation.Essay, WritingEvaluation.MaxEssayLength)
            ],
            MaxOutputTokens: 2500, Temperature: 0.2);

    public static string CriterionLabel(string key, WritingTaskType type) => key switch
    {
        "taskResponse" => type == WritingTaskType.IeltsTask1 ? "Task Achievement" : "Task Response",
        "coherenceCohesion" => "Coherence & Cohesion",
        "lexicalResource" => "Lexical Resource",
        _ => "Grammatical Range & Accuracy"
    };

    private static string Instructions(WritingTaskType type, int wordCount)
    {
        var (firstCriterion, minimum, taskNote) = type switch
        {
            WritingTaskType.IeltsTask1 => ("Task Achievement: whether the report covers the requirements, gives a clear overview and accurately selects and compares key features",
                150, "This is IELTS Academic Writing Task 1. The chart, graph or diagram itself is not available to you: judge Task Achievement from the task description and the internal consistency of the report."),
            WritingTaskType.IeltsTask2 => ("Task Response: whether all parts of the question are addressed, the position is clear throughout, and ideas are extended and supported",
                250, "This is IELTS Writing Task 2."),
            _ => ("Task Response: whether the task is fully and relevantly addressed with developed, supported ideas",
                0, "This is a general English writing task, not necessarily an IELTS task: apply the same four criteria and give IELTS-equivalent bands.")
        };

        var lengthRule = minimum > 0
            ? $"The essay has {wordCount} words; the minimum for this task is {minimum}, and an essay below it must be penalised under the first criterion."
            : $"The essay has {wordCount} words.";

        return $$"""
            You are an experienced, strictly calibrated IELTS Writing examiner. Assess the essay supplied as <user_input name="essay"> written for the task supplied as <user_input name="task">.
            {{taskNote}} {{lengthRule}}

            Rate each criterion on the 0–9 band scale in half bands, using the public IELTS Writing band descriptors:
            - {{firstCriterion}};
            - Coherence and Cohesion: logical organisation, paragraphing, clear progression, range and accuracy of cohesive devices;
            - Lexical Resource: range, precision and appropriacy of vocabulary, collocation, spelling and word formation;
            - Grammatical Range and Accuracy: range of structures, proportion of error-free sentences, punctuation.
            Do not inflate bands. Base every judgement only on the essay text.

            {{AiLanguage.Feedback}}
            For every grammar or vocabulary issue, "excerpt" must be copied exactly from the essay (at most 12 words) and "correction" must be the improved version — both in English, untranslated.

            Return only one JSON object, with no text before or after it, in exactly this shape:
            {"taskResponse":{"band":6.5,"comment":"..."},"coherenceCohesion":{"band":6.0,"comment":"..."},"lexicalResource":{"band":6.0,"comment":"..."},"grammaticalRange":{"band":6.0,"comment":"..."},"strengths":["..."],"weaknesses":["..."],"grammarIssues":[{"excerpt":"...","correction":"...","explanation":"..."}],"vocabularyIssues":[{"excerpt":"...","correction":"...","explanation":"..."}],"suggestions":["..."]}
            Give 2–4 strengths, 2–4 weaknesses, at most 8 grammar issues, at most 8 vocabulary issues and 3–5 suggestions.
            """;
    }

    /// <summary>Schema rules beyond deserialization; the message goes back to the model on retry.</summary>
    public static string? Validate(WritingModelOutput output)
    {
        foreach (var (name, criterion) in Criteria(output))
        {
            if (criterion?.Band is null) return $"\"{name}.band\" is missing.";
            if (criterion.Band is < 0 or > 9) return $"\"{name}.band\" must be between 0 and 9.";
        }

        return output.Strengths is null || output.Weaknesses is null || output.Suggestions is null
            ? "\"strengths\", \"weaknesses\" and \"suggestions\" must be arrays."
            : null;
    }

    public static WritingScore Score(WritingModelOutput output, string essay)
    {
        var taskResponse = HalfBand(output.TaskResponse!.Band!.Value);
        var coherence = HalfBand(output.CoherenceCohesion!.Band!.Value);
        var lexical = HalfBand(output.LexicalResource!.Band!.Value);
        var grammar = HalfBand(output.GrammaticalRange!.Band!.Value);

        var feedback = new WritingFeedback(
            Clean(output.TaskResponse.Comment, 800), Clean(output.CoherenceCohesion.Comment, 800),
            Clean(output.LexicalResource.Comment, 800), Clean(output.GrammaticalRange.Comment, 800),
            Texts(output.Strengths, 6), Texts(output.Weaknesses, 6),
            Issues(output.GrammarIssues, essay), Issues(output.VocabularyIssues, essay),
            Texts(output.Suggestions, 8));

        return new WritingScore(taskResponse, coherence, lexical, grammar, OverallBand(taskResponse, coherence, lexical, grammar), feedback);
    }

    public static decimal HalfBand(decimal band) => Math.Round(Math.Clamp(band, 0, 9) * 2, MidpointRounding.AwayFromZero) / 2;

    /// <summary>IELTS rule: the mean of the four criteria, where .25 rounds up to the half band and .75 up to the next whole band.</summary>
    public static decimal OverallBand(params decimal[] criteria) => Math.Floor(criteria.Average() * 2 + 0.5m) / 2;

    public static bool IsHalfBand(decimal value) => value is >= 0 and <= 9 && value * 2 == Math.Floor(value * 2);

    private static IEnumerable<(string Name, WritingCriterionOutput? Criterion)> Criteria(WritingModelOutput output) =>
    [
        ("taskResponse", output.TaskResponse), ("coherenceCohesion", output.CoherenceCohesion),
        ("lexicalResource", output.LexicalResource), ("grammaticalRange", output.GrammaticalRange)
    ];

    private static List<string> Texts(IEnumerable<string>? items, int max) =>
        (items ?? []).Select(i => Clean(i, 400)).OfType<string>().Take(max).ToList();

    private static List<WritingIssue> Issues(IEnumerable<WritingIssueOutput>? issues, string essay)
    {
        var normalizedEssay = CollapseWhitespace(essay);
        return (issues ?? [])
            .Select(i => (Excerpt: Clean(i.Excerpt, 200), Correction: Clean(i.Correction, 200), Explanation: Clean(i.Explanation, 400)))
            .Where(i => i.Excerpt is not null && i.Correction is not null
                        && normalizedEssay.Contains(CollapseWhitespace(i.Excerpt), StringComparison.OrdinalIgnoreCase))
            .Take(8)
            .Select(i => new WritingIssue(i.Excerpt!, i.Correction!, i.Explanation ?? ""))
            .ToList();
    }

    private static string? Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }

    private static string CollapseWhitespace(string value) => Regex.Replace(value.Trim(), @"\s+", " ");
}
