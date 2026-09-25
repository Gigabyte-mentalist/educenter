using EduCenter.SharedKernel;

namespace EduCenter.Modules.Automation.Domain;

/// <summary>The built-in rules of the Automation Engine (docs/00_TZ.md §40). Their trigger → condition → action shape,
/// parameters and defaults live in the API's rule catalog; an organization only stores what it changed.</summary>
public static class AutomationRuleCode
{
    public const string TrialFollowUp = "TRIAL_FOLLOW_UP";
    public const string AbsenceFollowUp = "ABSENCE_FOLLOW_UP";
    public const string HomeworkSupportSignal = "HOMEWORK_SUPPORT_SIGNAL";
    public const string DebtFollowUp = "DEBT_FOLLOW_UP";
    public const string PaymentReminders = "PAYMENT_REMINDERS";
    public const string HomeworkDeadlineReminder = "HOMEWORK_DEADLINE_REMINDER";
    public const string LessonReminder = "LESSON_REMINDER";

    // Learning signals (docs/00_TZ.md §7): a skill that stays weak raises a support recommendation.
    public const string WritingGrammarSignal = "WRITING_GRAMMAR_SIGNAL";
    public const string ListeningLowSignal = "LISTENING_LOW_SIGNAL";
    public const string ReadingTimeSignal = "READING_TIME_SIGNAL";
    public const string SpeakingFluencySignal = "SPEAKING_FLUENCY_SIGNAL";
}

public enum AutomationTrigger
{
    /// <summary>The recurring background job.</summary>
    Sweep,

    /// <summary>A write that evaluates its rule immediately (e.g. saving attendance).</summary>
    Event,

    /// <summary>"Run now" from the automation settings screen.</summary>
    Manual
}

/// <summary>
/// One organization's settings for one rule. No row means "catalog defaults". Parameters and action switches are
/// stored as small JSON objects keyed by the catalog's keys, so adding a parameter to a rule needs no migration;
/// values are always re-validated and clamped against the catalog when read.
/// </summary>
public class AutomationRule : TenantEntity
{
    private AutomationRule() { } // EF Core

    public AutomationRule(Guid organizationId, string code, bool isEnabled, string parametersJson, string actionsJson, Guid updatedBy)
    {
        OrganizationId = organizationId;
        Code = code;
        IsEnabled = isEnabled;
        ParametersJson = parametersJson;
        ActionsJson = actionsJson;
        UpdatedBy = updatedBy;
    }

    public string Code { get; private set; } = default!;
    public bool IsEnabled { get; private set; }
    public string ParametersJson { get; private set; } = "{}";
    public string ActionsJson { get; private set; } = "{}";
    public Guid UpdatedBy { get; private set; }

    public void Update(bool isEnabled, string parametersJson, string actionsJson, Guid updatedBy)
    {
        IsEnabled = isEnabled;
        ParametersJson = parametersJson;
        ActionsJson = actionsJson;
        UpdatedBy = updatedBy;
        MarkUpdated();
    }
}

/// <summary>What a rule did: written only when a run produced something (tasks, recommendations, messages), so the
/// log reads as a history of effects rather than a heartbeat every few minutes.</summary>
public class AutomationRunLog : TenantEntity
{
    private AutomationRunLog() { } // EF Core

    public AutomationRunLog(Guid organizationId, string ruleCode, AutomationTrigger trigger, int itemCount, string summary)
    {
        OrganizationId = organizationId;
        RuleCode = ruleCode;
        Trigger = trigger;
        ItemCount = itemCount;
        Summary = summary;
        RanAt = DateTimeOffset.UtcNow;
    }

    public string RuleCode { get; private set; } = default!;
    public AutomationTrigger Trigger { get; private set; }
    public int ItemCount { get; private set; }
    public string Summary { get; private set; } = default!;
    public DateTimeOffset RanAt { get; private set; }
}
