using EduCenter.Api.Ai.Providers;
using EduCenter.Modules.Ai.Domain;

namespace EduCenter.Api.Ai.Speaking;

public sealed class SpeakingQuestionOutput
{
    public string? Question { get; set; }
    public string? Topic { get; set; }
}

public sealed class SpeakingCriterionOutput
{
    public decimal? Band { get; set; }
    public string? Comment { get; set; }
}

public sealed class SpeakingPartOutput
{
    public int? Part { get; set; }
    public string? Comment { get; set; }
}

public sealed class SpeakingAssessmentOutput
{
    public SpeakingCriterionOutput? FluencyCoherence { get; set; }
    public SpeakingCriterionOutput? LexicalResource { get; set; }
    public SpeakingCriterionOutput? GrammaticalRange { get; set; }

    /// <summary>Whether the answers answer the questions asked. Judged from the question/answer pairs; shown beside the three IELTS
    /// criteria, not averaged into the band.</summary>
    public SpeakingCriterionOutput? Relevance { get; set; }
    public List<string>? Strengths { get; set; }
    public List<string>? Weaknesses { get; set; }
    public List<string>? Suggestions { get; set; }

    /// <summary>A Full Mock only: one short comment for each of the three parts.</summary>
    public List<SpeakingPartOutput>? Parts { get; set; }
}

/// <summary>One question and what the student said back (null while it is unanswered), with the part of the test it belongs to.</summary>
public sealed record SpeakingExchange(string Question, string? Answer, int? Part);

/// <summary>Speech metrics Whisper's segment timings give us without a pronunciation model.</summary>
public sealed record SpeechMetrics(int SpokenSeconds, int WordsPerMinute, int LongPauses);

public sealed record SpeakingPartComment(int Part, string? Comment);

/// <summary>What is stored in <see cref="SpeakingSession.AssessmentJson"/>. The last two fields are absent from every assessment made
/// before they existed — they read back as null, which is how the page knows not to show them.</summary>
public sealed record SpeakingAssessment(
    string? FluencyComment, string? LexicalComment, string? GrammarComment,
    List<string> Strengths, List<string> Weaknesses, List<string> Suggestions,
    int SpokenSeconds, int WordsPerMinute, int LongPauses, int TurnCount,
    string? RelevanceComment = null, List<SpeakingPartComment>? Parts = null);

/// <summary>
/// The examiner's script and the assessment rubric (docs/00_TZ.md §34, §35). Three criteria are scored from the
/// transcript — fluency and coherence, lexical resource, grammatical range and accuracy — and the overall band is their mean.
/// Answer relevance is read from the same transcript and reported next to them, outside the mean. Pronunciation, which needs the
/// audio signal itself, is out of scope for this MVP (TZ §58, §59; docs/21.09-audit-jurnali S10.10), so the overall band is labelled as
/// an estimate everywhere it is shown.
/// </summary>
public static class SpeakingScript
{
    /// <summary>v2: Full Mock — the examiner follows the three parts and Part 3 continues the Part 2 topic. Every other mode asks what
    /// it asked under v1.</summary>
    public const string QuestionPromptVersion = "speaking.examiner.v2";

    /// <summary>v3 adds answer relevance and, for a Full Mock, a comment per part. (v2 wrote its feedback in Uzbek — the examiner's
    /// questions stay English, since that is what the student practises.) A session assessed under an older version is read as it was
    /// written: what those versions did not ask for is simply absent.</summary>
    public const string AssessmentPromptVersion = "speaking.assessment.v3";

    /// <summary>The Full Mock's shape (docs/00_TZ.md §34): Part 1 questions, one Part 2 cue card, Part 3 discussion.</summary>
    public const int FullMockPart1Turns = 5;
    public const int FullMockPart2Turns = 1;
    public const int FullMockPart3Turns = 5;

    /// <summary>The time the student gets with a Part 2 cue card before speaking.</summary>
    public const int Part2PreparationSeconds = 60;

    /// <summary>What a Full Mock costs the daily speaking allowance, in minutes: about eleven to fourteen minutes of speech.
    /// It does not start unless the student has that much left, rather than stopping half way through.</summary>
    public const int FullMockMinutes = 14;

    public static string ModeLabel(SpeakingMode mode) => mode switch
    {
        SpeakingMode.IeltsPart1 => "IELTS Speaking Part 1",
        SpeakingMode.IeltsPart2 => "IELTS Speaking Part 2",
        SpeakingMode.IeltsPart3 => "IELTS Speaking Part 3",
        SpeakingMode.FullMock => "IELTS Speaking Full Mock",
        SpeakingMode.GeneralEnglish => "General English",
        _ => "Free conversation"
    };

    /// <summary>How many exchanges a mode is worth: Part 2 is one long turn, Part 1 is a quick warm-up round.</summary>
    public static int TurnsFor(SpeakingMode mode) => mode switch
    {
        SpeakingMode.IeltsPart1 => 5,
        SpeakingMode.IeltsPart2 => 1,
        SpeakingMode.IeltsPart3 => 4,
        SpeakingMode.FullMock => FullMockPart1Turns + FullMockPart2Turns + FullMockPart3Turns,
        _ => 6
    };

    /// <summary>The part of the IELTS test a question is in — 1, 2 or 3 — or null for a mode that is not IELTS-shaped.</summary>
    public static int? PartFor(SpeakingMode mode, int ordinal) => mode switch
    {
        SpeakingMode.IeltsPart1 => 1,
        SpeakingMode.IeltsPart2 => 2,
        SpeakingMode.IeltsPart3 => 3,
        SpeakingMode.FullMock => ordinal <= FullMockPart1Turns ? 1 : ordinal <= FullMockPart1Turns + FullMockPart2Turns ? 2 : 3,
        _ => null
    };

    /// <summary>The part a stored turn belongs to: the one recorded with it, or — for a turn recorded before parts were kept — the one its
    /// mode and place give.</summary>
    public static int? PartOf(SpeakingMode mode, SpeakingTurn turn) => turn.Part ?? PartFor(mode, turn.Ordinal);

    /// <param name="nextOrdinal">The place of the question being asked: 1 to start, otherwise one more than the exchanges so far.</param>
    public static AiPrompt QuestionPrompt(SpeakingMode mode, string? topic, IReadOnlyList<SpeakingExchange> history, int nextOrdinal)
    {
        var part = PartFor(mode, nextOrdinal);
        var instructions = $$"""
            You are an IELTS speaking examiner running a {{ModeLabel(mode)}} practice with one student.
            {{ModeInstructions(mode, part)}}
            Ask exactly one question (or cue card) at a time. Keep it natural, under 60 words, and never comment on or
            correct the student's previous answer — that comes at the end of the session.
            {{TopicInstruction(mode, part, topic)}}
            Return only one JSON object: {"question": "...", "topic": "short topic label"}.
            """;

        var inputs = history
            .SelectMany((turn, i) => new[]
                {
                    new AiUntrustedInput($"turn_{i + 1}_examiner", turn.Question, SpeakingTurn.MaxQuestionLength),
                    new AiUntrustedInput($"turn_{i + 1}_student", string.IsNullOrWhiteSpace(turn.Answer) ? "(no answer)" : turn.Answer!,
                        SpeakingTurn.MaxTranscriptLength)
                })
            .ToList();

        // Part 3 grows out of the Part 2 card, so the card itself is handed over by name rather than left for the model to find.
        if (mode == SpeakingMode.FullMock && part == 3 && history.FirstOrDefault(turn => turn.Part == 2) is { } card)
            inputs.Add(new AiUntrustedInput("part2_cue_card", card.Question, SpeakingTurn.MaxQuestionLength));

        if (inputs.Count == 0)
            inputs.Add(new AiUntrustedInput("start", "Begin the practice.", 50));

        return new AiPrompt(QuestionPromptVersion, instructions, inputs, MaxOutputTokens: 200, Temperature: 0.7);
    }

    public static AiPrompt AssessmentPrompt(SpeakingMode mode, IReadOnlyList<SpeakingExchange> history, SpeechMetrics metrics)
    {
        var fullMock = mode == SpeakingMode.FullMock;
        const string criteriaShape =
            "\"fluencyCoherence\":{\"band\":6.0,\"comment\":\"...\"},\"lexicalResource\":{\"band\":6.0,\"comment\":\"...\"},"
            + "\"grammaticalRange\":{\"band\":6.0,\"comment\":\"...\"},\"relevance\":{\"band\":6.0,\"comment\":\"...\"},"
            + "\"strengths\":[\"...\"],\"weaknesses\":[\"...\"],\"suggestions\":[\"...\"]";
        const string partsShape = ",\"parts\":[{\"part\":1,\"comment\":\"...\"},{\"part\":2,\"comment\":\"...\"},{\"part\":3,\"comment\":\"...\"}]";
        var shape = "{" + criteriaShape + (fullMock ? partsShape : "") + "}";
        var partsHint = fullMock
            ? "The inputs are the whole test in order and their names say the part: assess each criterion over the whole test, "
              + "and also give one short comment for each of the three parts."
            : "";

        var instructions = $$"""
            You are an experienced, strictly calibrated IELTS speaking examiner. Assess the student's answers in this
            {{ModeLabel(mode)}} practice using the public band descriptors, on the 0–9 scale in half bands:
            - Fluency and Coherence: speech rate and continuity, hesitation, linking of ideas, development of answers;
            - Lexical Resource: range and precision of vocabulary, paraphrase, collocation;
            - Grammatical Range and Accuracy: variety of structures and how many are error-free;
            - Answer Relevance (scored separately from the three IELTS criteria, on the same scale): whether each answer
              really answers the question that was asked and stays on the topic, or drifts away, repeats itself or recites a
              memorised answer. Read every question together with the answer that follows it.
            Do not judge pronunciation — you only have a transcript, not the audio.
            Measured from the recording: the student spoke {{metrics.SpokenSeconds}} seconds at about {{metrics.WordsPerMinute}}
            words per minute, with {{metrics.LongPauses}} pauses longer than a second. A natural pace is roughly 120–160 wpm;
            treat this as evidence for fluency, not as a score by itself.
            The transcript comes from automatic speech recognition, so ignore punctuation and spelling oddities.
            {{partsHint}}
            {{AiLanguage.Feedback}}
            Return only one JSON object:
            {{shape}}
            Give 2–4 strengths, 2–4 weaknesses and 3–5 suggestions.
            """;

        var inputs = history
            .SelectMany((turn, i) =>
            {
                var prefix = fullMock && turn.Part is { } part ? $"part{part}_" : "";
                return new[]
                {
                    new AiUntrustedInput($"{prefix}question_{i + 1}", turn.Question, SpeakingTurn.MaxQuestionLength),
                    new AiUntrustedInput($"{prefix}answer_{i + 1}", string.IsNullOrWhiteSpace(turn.Answer) ? "(no answer)" : turn.Answer!,
                        SpeakingTurn.MaxTranscriptLength)
                };
            })
            .ToList();

        return new AiPrompt(AssessmentPromptVersion, instructions, inputs, MaxOutputTokens: fullMock ? 1600 : 1200, Temperature: 0.2);
    }

    public static string? ValidateQuestion(SpeakingQuestionOutput output) =>
        string.IsNullOrWhiteSpace(output.Question) ? "\"question\" is missing." : null;

    public static string? ValidateAssessment(SpeakingAssessmentOutput output, SpeakingMode mode = SpeakingMode.GeneralEnglish)
    {
        foreach (var (name, criterion) in Criteria(output))
        {
            if (criterion?.Band is null) return $"\"{name}.band\" is missing.";
            if (criterion.Band is < 0 or > 9) return $"\"{name}.band\" must be between 0 and 9.";
        }

        if (output.Strengths is null || output.Weaknesses is null || output.Suggestions is null)
            return "\"strengths\", \"weaknesses\" and \"suggestions\" must be arrays.";

        // A Full Mock answers for every part; the other modes have no "parts" to ask for.
        if (mode == SpeakingMode.FullMock)
            foreach (var part in new[] { 1, 2, 3 })
                if (output.Parts?.Any(p => p.Part == part && !string.IsNullOrWhiteSpace(p.Comment)) != true)
                    return $"\"parts\" must hold a comment for part {part}.";

        return null;
    }

    /// <summary>Words per minute and long pauses from the recogniser's segment timings.</summary>
    public static SpeechMetrics Measure(TranscriptionResult transcription, int fallbackSeconds)
    {
        var spoken = transcription.Segments.Count > 0
            ? (int)Math.Round(transcription.Segments.Max(s => s.End))
            : fallbackSeconds;
        spoken = Math.Clamp(spoken <= 0 ? fallbackSeconds : spoken, 0, 3600);

        var words = transcription.Text.Split([' ', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries).Length;
        var wordsPerMinute = spoken > 0 ? (int)Math.Round(words / (spoken / 60.0)) : 0;

        var pauses = 0;
        for (var i = 1; i < transcription.Segments.Count; i++)
            if (transcription.Segments[i].Start - transcription.Segments[i - 1].End > 1.0)
                pauses++;

        return new SpeechMetrics(spoken, wordsPerMinute, pauses);
    }

    private const string Part1Instructions = "Part 1: short, familiar questions about the student's life, work, studies, hobbies and home.";

    private const string Part2Instructions = "Part 2: give one cue card — a topic plus three or four bullet points to cover — and say the "
                                             + "student has one minute to prepare and should speak for one to two minutes.";

    private const string Part3Instructions =
        "Part 3: abstract, opinion-based questions that follow from a Part 2 topic; ask the student to explain and justify.";

    private static string ModeInstructions(SpeakingMode mode, int? part) => mode switch
    {
        SpeakingMode.IeltsPart1 => Part1Instructions,
        SpeakingMode.IeltsPart2 => Part2Instructions,
        SpeakingMode.IeltsPart3 => Part3Instructions,
        SpeakingMode.FullMock => part switch
        {
            1 => $"This is a full mock test. You are in Part 1 of 3 of the test. {Part1Instructions}",
            2 => $"This is a full mock test. You are in Part 2 of 3 of the test. {Part2Instructions} Choose a topic unrelated to the Part 1 questions.",
            _ => "This is a full mock test. You are in Part 3 of 3 of the test. Part 3: abstract, opinion-based questions that develop "
                 + "the topic of the Part 2 cue card, which is the input named part2_cue_card; ask the student to explain, compare and justify."
        },
        SpeakingMode.GeneralEnglish => "Everyday English conversation practice at the student's level.",
        _ => "A free, friendly conversation; follow the student's interests while keeping the English level appropriate."
    };

    /// <summary>Part 1 stays on one everyday topic; the cue card starts a new one, and Part 3 keeps to the card's.</summary>
    private static string TopicInstruction(SpeakingMode mode, int? part, string? topic) => (mode, part) switch
    {
        (SpeakingMode.FullMock, 2) => $"The Part 1 topic was: {topic}. Choose a different, everyday topic for the cue card.",
        (SpeakingMode.FullMock, 3) => "Stay on the topic of the Part 2 cue card.",
        _ => topic is null ? "Choose a common, everyday topic suitable for practice." : $"Stay on the topic: {topic}."
    };

    private static IEnumerable<(string Name, SpeakingCriterionOutput? Criterion)> Criteria(SpeakingAssessmentOutput output) =>
    [
        ("fluencyCoherence", output.FluencyCoherence),
        ("lexicalResource", output.LexicalResource),
        ("grammaticalRange", output.GrammaticalRange),
        ("relevance", output.Relevance)
    ];
}
