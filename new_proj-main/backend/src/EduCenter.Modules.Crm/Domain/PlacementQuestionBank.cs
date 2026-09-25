namespace EduCenter.Modules.Crm.Domain;

/// <summary>One multiple-choice item. <see cref="Weight"/> is roughly the CEFR difficulty of the point
/// being tested (A1 grammar counts for less than a C1 vocabulary item), so the final score is a weighted
/// sum, not a flat correct-answer count.</summary>
public record PlacementQuestion(int Id, string Text, string[] Options, int CorrectIndex, int Weight);

/// <summary>
/// docs/00_TZ.md §13 Placement Test: a fixed, hand-written grammar/vocabulary quiz an operator administers
/// to a Lead before enrollment. This is content, not user data — like a printed test booklet — so it lives
/// in code rather than a seeded table; nothing about it needs an admin editor in this phase.
/// </summary>
public static class PlacementQuestionBank
{
    public static readonly IReadOnlyList<PlacementQuestion> Questions =
    [
        new(1, "She ___ to school every day.", ["go", "goes", "going", "gone"], 1, 1),
        new(2, "I ___ a book right now.", ["read", "reads", "am reading", "readed"], 2, 1),
        new(3, "They have ___ finished their homework.", ["already", "yet", "still", "ago"], 0, 2),
        new(4, "If I ___ rich, I would travel the world.", ["am", "was", "were", "will be"], 2, 2),
        new(5, "By next year, she ___ her degree.", ["will complete", "will have completed", "completes", "completed"], 1, 3),
        new(6, "The report, ___ was due yesterday, is still incomplete.", ["that", "which", "who", "what"], 1, 3),
        new(7, "He speaks English ___ than his brother.", ["fluent", "more fluent", "more fluently", "fluently"], 2, 3),
        new(8, "Choose the word closest in meaning to 'meticulous':", ["careless", "thorough", "hasty", "vague"], 1, 4),
        new(9, "Had I known about the traffic, I ___ earlier.", ["would leave", "would have left", "left", "will leave"], 1, 4),
        new(10, "Despite the heavy rain, the match ___ as scheduled.", ["was cancelled", "went ahead", "postponed", "called off"], 1, 4),
        new(11, "Choose the correctly punctuated sentence:",
            [
                "Although it was raining, we went for a walk.",
                "Although it was raining we, went for a walk.",
                "Although, it was raining we went for a walk.",
                "Although it was raining we went, for a walk.",
            ], 0, 5),
        new(12, "'Notwithstanding' most closely means:", ["because of", "despite", "in addition to", "instead of"], 1, 5),
    ];

    public static readonly int MaxWeight = Questions.Sum(q => q.Weight);

    /// <summary>Maps the weighted score to one of TZ §13's eight labels via equal eighths of the max
    /// score — a plain, explainable rule rather than a hidden model.</summary>
    public static string RecommendLevel(int earnedWeight)
    {
        var pct = MaxWeight == 0 ? 0 : (double)earnedWeight / MaxWeight;
        return pct switch
        {
            < 0.125 => "A1",
            < 0.25 => "A2",
            < 0.375 => "B1",
            < 0.50 => "B2",
            < 0.625 => "C1",
            < 0.75 => "IELTS Foundation",
            < 0.875 => "IELTS Intermediate",
            _ => "IELTS Advanced",
        };
    }
}
