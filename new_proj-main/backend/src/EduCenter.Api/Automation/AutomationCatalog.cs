using System.Text.Json;
using EduCenter.Modules.Automation.Domain;

namespace EduCenter.Api.Automation;

/// <param name="Scale">Values are stored as whole numbers; a parameter that needs a fraction (an IELTS band such as 5.5) stores it multiplied by
/// this — 55 with a scale of 10 — and <see cref="Min"/>, <see cref="Max"/> and <see cref="Default"/> are in those stored units. The screen
/// divides for display; the rule reads it back with <see cref="AutomationCatalog.BandScale"/>.</param>
public record AutomationParameterDefinition(string Key, string Label, string Unit, int Min, int Max, int Default, int Scale = 1);
public record AutomationActionDefinition(string Key, string Label, bool Default);

/// <summary>A rule's settings for one organization after merging stored values over the catalog defaults.</summary>
public sealed record EffectiveRule(string Code, bool Enabled, IReadOnlyDictionary<string, int> Parameters, IReadOnlyDictionary<string, bool> Actions)
{
    public int Param(string key) => Parameters[key];
    public bool Action(string key) => Actions[key];
}

public record AutomationRuleDefinition(
    string Code, string Name, string Trigger, string Description,
    AutomationParameterDefinition[] Parameters, AutomationActionDefinition[] Actions)
{
    /// <summary>Stored values win over defaults, but are clamped to the catalog's range and unknown keys are ignored —
    /// a stale or hand-edited row can never push a rule outside what the catalog allows.</summary>
    public EffectiveRule Resolve(AutomationRule? row)
    {
        var storedParameters = Parse<int>(row?.ParametersJson);
        var storedActions = Parse<bool>(row?.ActionsJson);

        var parameters = Parameters.ToDictionary(
            p => p.Key,
            p => Math.Clamp(storedParameters.TryGetValue(p.Key, out var value) ? value : p.Default, p.Min, p.Max));
        var actions = Actions.ToDictionary(
            a => a.Key,
            a => storedActions.TryGetValue(a.Key, out var value) ? value : a.Default);

        return new EffectiveRule(Code, row?.IsEnabled ?? true, parameters, actions);
    }

    /// <summary>Rules whose parameters constrain each other; null when the combination is valid.</summary>
    public string? Validate(IReadOnlyDictionary<string, int> parameters) =>
        Code == AutomationRuleCode.HomeworkSupportSignal
        && parameters[AutomationCatalog.Keys.MaxSubmitted] >= parameters[AutomationCatalog.Keys.Window]
            ? "«Ko'pi bilan topshirilgan» soni tekshiriladigan homeworklar sonidan kichik bo'lishi kerak."
            : null;

    private static Dictionary<string, T> Parse<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<Dictionary<string, T>>(json) ?? []; }
        catch (JsonException) { return []; }
    }
}

/// <summary>
/// The Automation Engine's rules (docs/00_TZ.md §40: trigger → condition → action). Defaults reproduce the
/// behaviour that was hard-coded before Sprint 5, so an organization that never opens the settings sees no change.
/// </summary>
public static class AutomationCatalog
{
    /// <summary>IELTS bands are stored in tenths (55 = band 5.5): the settings hold whole numbers only.</summary>
    public const int BandScale = 10;

    public static class Keys
    {
        public const string Hours = "hours";
        public const string ConsecutiveAbsences = "consecutiveAbsences";
        public const string Window = "window";
        public const string MaxSubmitted = "maxSubmitted";
        public const string Days = "days";
        public const string DueLeadDays = "dueLeadDays";
        public const string OverdueAfterDays = "overdueAfterDays";
        public const string LeadHours = "leadHours";
        public const string Results = "results";
        public const string MaxBand = "maxBand";
        public const string Gap = "gap";

        public const string CreateTask = "createTask";
        public const string RaisePriority = "raisePriority";
        public const string RaiseSupport = "raiseSupport";
        public const string NotifyTeacher = "notifyTeacher";
        public const string NotifyDue = "notifyDue";
        public const string NotifyOverdue = "notifyOverdue";
    }

    public static readonly AutomationRuleDefinition[] All =
    [
        new(AutomationRuleCode.TrialFollowUp, "Trial follow-up", "Lid trial darsga keldi",
            "Belgilangan vaqt ichida guruhga yozilmasa, operator navbatiga vazifa tushadi va lid «Muhim» deb belgilanadi (TZ §10).",
            [new(Keys.Hours, "Kutish vaqti", "soat", 1, 168, 24)],
            [new(Keys.CreateTask, "Operatorga vazifa va bildirishnoma", true), new(Keys.RaisePriority, "Lidni «Muhim» deb belgilash", true)]),

        new(AutomationRuleCode.AbsenceFollowUp, "Ketma-ket yo'qlik", "Student darsga kelmadi deb belgilandi",
            "Student guruhdagi oxirgi N ta belgilangan darsga kelmasa: operatorga qo'ng'iroq vazifasi, support tavsiyasi va asosiy o'qituvchiga xabar (TZ §8, §10).",
            [new(Keys.ConsecutiveAbsences, "Ketma-ket yo'qliklar", "ta dars", 2, 10, 2)],
            [
                new(Keys.CreateTask, "Operatorga qo'ng'iroq vazifasi", true),
                new(Keys.RaiseSupport, "Support tavsiyasi (administratorga xabar bilan)", true),
                new(Keys.NotifyTeacher, "Asosiy o'qituvchiga bildirishnoma", true)
            ]),

        new(AutomationRuleCode.HomeworkSupportSignal, "Homework topshirilmayapti", "Homework muddati o'tdi",
            "Guruhga qo'shilganidan beri muddati o'tgan oxirgi N ta homeworkdan ko'pi bilan M tasi topshirilgan bo'lsa, support tavsiya qilinadi (TZ §8).",
            [new(Keys.Window, "Tekshiriladigan homeworklar", "ta", 2, 10, 3), new(Keys.MaxSubmitted, "Ko'pi bilan topshirilgan", "ta", 0, 9, 1)],
            []),

        new(AutomationRuleCode.DebtFollowUp, "Qarz follow-up", "To'lov muddati o'tdi",
            "To'lanmagan hisob-faktura belgilangan kundan ko'p kechiksa, operator navbatiga qarz vazifasi tushadi (TZ §10). Talabaning «Qarzdor» holati bu qoidadan qat'i nazar hisoblanadi.",
            [new(Keys.Days, "Kechikish", "kun", 1, 60, 3)],
            [new(Keys.CreateTask, "Operatorga vazifa va bildirishnoma", true)]),

        new(AutomationRuleCode.PaymentReminders, "To'lov eslatmalari", "To'lov muddati yaqinlashdi yoki o'tdi",
            "Student va ota-onaga to'lov kunidan oldin eslatma va kechikkandan keyin xabar yuboriladi (TZ §23).",
            [new(Keys.DueLeadDays, "Muddatdan oldin", "kun", 0, 14, 3), new(Keys.OverdueAfterDays, "Kechikkandan keyin", "kun", 1, 60, 3)],
            [new(Keys.NotifyDue, "Muddat eslatmasi", true), new(Keys.NotifyOverdue, "Kechikish xabari", true)]),

        new(AutomationRuleCode.HomeworkDeadlineReminder, "Homework muddati eslatmasi", "Homework muddati yaqinlashdi",
            "Hali topshirmagan studentlarga muddatdan oldin eslatma yuboriladi.",
            [new(Keys.LeadHours, "Muddatdan oldin", "soat", 1, 72, 24)],
            []),

        new(AutomationRuleCode.LessonReminder, "Dars eslatmasi", "Dars boshlanishi yaqinlashdi",
            "Guruh studentlari va o'qituvchiga dars boshlanishidan oldin eslatma yuboriladi.",
            [new(Keys.LeadHours, "Darsdan oldin", "soat", 1, 24, 2)],
            []),

        // Learning signals (TZ §7). The raised recommendation carries the numbers behind it, so the reviewer sees why without opening the profile.
        new(AutomationRuleCode.WritingGrammarSignal, "Writing: grammatika zaif", "Insho AI tomonidan baholandi",
            "Studentning oxirgi N ta yakunlangan inshosida grammatika bandi chegaradan past bo'lsa, support tavsiya qilinadi va o'qituvchiga xabar boradi (TZ §7). O'qituvchi tuzatgan band AI bahosidan ustun.",
            [
                new(Keys.Results, "Tekshiriladigan insholar", "ta", 2, 10, 3),
                new(Keys.MaxBand, "Grammatika bandi chegarasi (undan past)", "band", 30, 70, 55, AutomationCatalog.BandScale)
            ],
            [
                new(Keys.RaiseSupport, "Support tavsiyasi (administratorga xabar bilan)", true),
                new(Keys.NotifyTeacher, "Asosiy o'qituvchiga bildirishnoma", true)
            ]),

        new(AutomationRuleCode.ListeningLowSignal, "Listening: ketma-ket past natija", "Imtihon natijasi kiritildi",
            "Studentning oxirgi N ta imtihonida Listening bandi chegaradan past bo'lsa, support tavsiya qilinadi va o'qituvchiga xabar boradi (TZ §7).",
            [
                new(Keys.Results, "Tekshiriladigan imtihonlar", "ta", 2, 6, 2),
                new(Keys.MaxBand, "Listening bandi chegarasi (undan past)", "band", 30, 70, 50, AutomationCatalog.BandScale)
            ],
            [
                new(Keys.RaiseSupport, "Support tavsiyasi (administratorga xabar bilan)", true),
                new(Keys.NotifyTeacher, "Asosiy o'qituvchiga bildirishnoma", true)
            ]),

        new(AutomationRuleCode.ReadingTimeSignal, "Reading: vaqtni boshqarish muammosi", "Imtihon natijasi kiritildi",
            "Oxirgi imtihonda Reading bandi shu imtihondagi boshqa bo'limlar (Listening, Writing, Speaking) o'rtachasidan D banddan ko'p past bo'lsa, support tavsiya qilinadi (TZ §7). Kamida ikkita boshqa bo'lim baholangan bo'lishi kerak.",
            [new(Keys.Gap, "Boshqa bo'limlardan farq (shundan ko'p past)", "band", 5, 30, 10, AutomationCatalog.BandScale)],
            [
                new(Keys.RaiseSupport, "Support tavsiyasi (administratorga xabar bilan)", true),
                new(Keys.NotifyTeacher, "Asosiy o'qituvchiga bildirishnoma", true)
            ]),

        new(AutomationRuleCode.SpeakingFluencySignal, "Speaking: fluency past", "Speaking mashqi baholandi",
            "Studentning oxirgi N ta yakunlangan Speaking mashqida fluency bandi chegaradan past bo'lsa, support tavsiya qilinadi va o'qituvchiga xabar boradi (TZ §7). Faqat o'qituvchilarga ko'rinadigan (talaba bilgan) mashqlar hisobga olinadi; o'qituvchi tuzatgan band AI bahosidan ustun.",
            [
                new(Keys.Results, "Tekshiriladigan mashqlar", "ta", 2, 6, 3),
                new(Keys.MaxBand, "Fluency bandi chegarasi (undan past)", "band", 30, 70, 50, AutomationCatalog.BandScale)
            ],
            [
                new(Keys.RaiseSupport, "Support tavsiyasi (administratorga xabar bilan)", true),
                new(Keys.NotifyTeacher, "Asosiy o'qituvchiga bildirishnoma", true)
            ]),
    ];

    public static AutomationRuleDefinition? Find(string code) => All.FirstOrDefault(r => r.Code == code);

    public static AutomationRuleDefinition Get(string code) =>
        Find(code) ?? throw new ArgumentException($"Unknown automation rule '{code}'.", nameof(code));
}
