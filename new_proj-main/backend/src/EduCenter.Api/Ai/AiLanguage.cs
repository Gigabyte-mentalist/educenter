namespace EduCenter.Api.Ai;

/// <summary>
/// Which language the AI writes back in. The centre's students are Uzbek speakers, so feedback — comments, strengths,
/// weaknesses, advice — is written in Uzbek even though the work being assessed is in English. What must stay English
/// is the student's own text, its corrections, and the IELTS criterion names, otherwise the feedback stops being usable
/// for learning. The AI Tutor is different: it answers in whatever language the student asked in.
/// </summary>
public static class AiLanguage
{
    /// <summary>Feedback language for Writing and Speaking assessments (docs/06_phase4_technical_plan.md §1).</summary>
    public const string Feedback = """
        Write every comment, strength, weakness, explanation and suggestion in Uzbek (latin script), in simple wording a
        student understands. Do not translate the student's own words, the corrected versions, English examples or the
        IELTS criterion names — those stay in English exactly as they are.
        """;

    /// <summary>The AI Tutor mirrors the student: an Uzbek question gets an Uzbek answer, a Russian one a Russian answer.</summary>
    public static string Mirror(string? language) => $"""
        Answer in {Describe(language)}. Keep English examples, sentences, grammar terms and quotations in English inside
        that answer — the student is learning English, so the examples must stay in the target language. Use simple,
        clear wording and explain like a teacher, not like a dictionary.
        """;

    /// <summary>The out-of-domain reply, in the language the student wrote in.</summary>
    public static string OutOfDomainReply(string? language) => Normalize(language) switch
    {
        "ru" => "Я помогаю только с английским языком и подготовкой к экзаменам. Задайте вопрос по этой теме — например, "
                + "о грамматике, лексике или IELTS.",
        "en" => "I can only help with English and exam preparation. Please ask about grammar, vocabulary, IELTS or another "
                + "part of your English studies.",
        _ => "Men faqat ingliz tili va imtihonlarga tayyorgarlik bo'yicha yordam bera olaman. Savolingizni shu mavzuda "
             + "bering — masalan grammatika, lug'at yoki IELTS bo'yicha."
    };

    private static string Describe(string? language) => Normalize(language) switch
    {
        "ru" => "Russian",
        "en" => "English",
        "uz" => "Uzbek (latin script)",
        _ => "the same language the student used in the question"
    };

    /// <summary>The model returns a language name or code; only the three the centre actually sees are recognised.</summary>
    private static string? Normalize(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return null;
        var value = language.Trim().ToLowerInvariant();

        if (value.StartsWith("ru")) return "ru";
        if (value.StartsWith("en")) return "en";
        if (value.StartsWith("uz") || value.StartsWith("o'z") || value.StartsWith("oz")) return "uz";
        return null;
    }
}
