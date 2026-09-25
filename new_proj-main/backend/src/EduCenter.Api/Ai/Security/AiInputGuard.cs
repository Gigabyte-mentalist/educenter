using System.Text;
using System.Text.RegularExpressions;

namespace EduCenter.Api.Ai.Security;

public sealed record InjectionVerdict(bool IsSuspicious, string? Rule);

/// <summary>Security layer 1 (docs/00_TZ.md §37): shape checks on text a user sends to a model.</summary>
public static class AiInputValidator
{
    /// <returns>A user-facing problem, or null when the text is acceptable.</returns>
    public static string? Validate(string? text, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text)) return "Matn bo'sh.";
        if (text.Length > maxChars) return $"Matn juda uzun (ko'pi bilan {maxChars} belgi).";
        if (text.Any(c => char.IsControl(c) && c is not '\n' and not '\r' and not '\t'))
            return "Matnda ruxsat etilmagan boshqaruv belgilari bor.";
        return null;
    }
}

/// <summary>
/// Security layer 3 (docs/00_TZ.md §37): rule-based detection of attempts to steer the model — overriding its
/// instructions, extracting the system prompt, switching it into an "unrestricted" persona, or smuggling chat-template
/// tokens and fake role headers. Rules are deliberately narrow (instruction-shaped phrases, not topics), because students
/// write essays about rules, AI and technology; a hit rejects the request before any provider sees it.
/// Invisible characters are stripped before matching so they cannot split a phrase; bidirectional overrides are
/// themselves suspicious (they make text read differently to a human and a model).
/// </summary>
public static class PromptInjectionDetector
{
    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.Compiled;
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(200);

    private static readonly (string Name, Regex Pattern)[] NormalizedRules =
    [
        ("override-instructions", new Regex(
            @"\b(ignore|disregard|forget|override|bypass|skip)\b(\s+\w+){0,2}\s+(all|any|the|your|my|previous|prior|above|earlier|preceding|system|these|those)\s+(\w+\s+){0,2}(instructions?|prompts?|system messages?|guidelines|directives)\b",
            Options, Timeout)),
        ("reveal-system-prompt", new Regex(
            @"\b(reveal|show|print|repeat|output|display|leak|tell me|give me|what (is|are|were))\b.{0,40}\b(system prompt|system message|hidden prompt|initial prompt|your (instructions|rules|prompt)|developer message)\b",
            Options, Timeout)),
        ("unrestricted-persona", new Regex(
            @"\b(developer mode|jailbreak|jailbroken|do anything now)\b|\b(you are now|from now on you are|pretend (to be|you are)|act as)\b.{0,30}\b(dan|unrestricted|unfiltered|uncensored|without (any )?(restrictions|rules|limits|filters))\b",
            Options, Timeout)),
        ("chat-template-token", new Regex(
            @"<\|(im_start|im_end|system|assistant|user|endoftext)\|>|\[/?inst\]|<<sys>>|</s>",
            Options, Timeout)),
        ("override-instructions-ru", new Regex(
            @"(игнорируй|игнорировать|забудь|забыть|отмени|не обращай внимания на)\s+(все\s+)?(предыдущие\s+|прошлые\s+|свои\s+|эти\s+)?(инструкции|указания|правила|промпт)",
            Options, Timeout)),
        ("reveal-system-prompt-ru", new Regex(
            @"(покажи|выведи|раскрой|повтори)\s+.{0,20}(системный промпт|системные инструкции|свои инструкции)",
            Options, Timeout)),
        ("override-instructions-uz", new Regex(
            @"(oldingi|avvalgi|yuqoridagi|barcha|hamma)\s+(ko'rsatma|buyruq|instruksiya|qoida)\w*\s+(unut|e'tiborga olma|bekor qil|inobatga olma)",
            Options, Timeout)),
        ("reveal-system-prompt-uz", new Regex(@"\btizim\s+(prompt|ko'rsatma)\w*", Options, Timeout))
    ];

    /// <summary>Checked on the lower-cased text with line breaks intact: a line that pretends to open a new chat turn.</summary>
    private static readonly Regex FakeRoleHeader = new(
        @"^\s*(#{1,3}\s*)?(system|developer)\s*(message|prompt)?\s*:", Options | RegexOptions.Multiline, Timeout);

    public static InjectionVerdict Inspect(string text)
    {
        if (text.Any(IsBidiControl)) return new InjectionVerdict(true, "bidi-control");

        var stripped = StripInvisible(text).ToLowerInvariant();
        try
        {
            if (FakeRoleHeader.IsMatch(stripped)) return new InjectionVerdict(true, "fake-role-header");

            var normalized = Normalize(stripped);
            foreach (var (name, pattern) in NormalizedRules)
                if (pattern.IsMatch(normalized))
                    return new InjectionVerdict(true, name);
        }
        catch (RegexMatchTimeoutException)
        {
            return new InjectionVerdict(true, "inspection-timeout");
        }

        return new InjectionVerdict(false, null);
    }

    private static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormKC))
            builder.Append(c is '‘' or '’' or 'ʻ' or 'ʼ' or '`' ? '\'' : c);
        return Regex.Replace(builder.ToString(), @"\s+", " ");
    }

    private static string StripInvisible(string text) =>
        string.Concat(text.Where(c => !(c is >= '​' and <= '‏' or >= '⁠' and <= '⁤' or '﻿' or '­')));

    private static bool IsBidiControl(char c) => c is >= '‪' and <= '‮' or >= '⁦' and <= '⁩';
}
