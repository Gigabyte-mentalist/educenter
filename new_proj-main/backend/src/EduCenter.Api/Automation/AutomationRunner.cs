using EduCenter.Modules.Automation.Domain;
using Hangfire;
using Hangfire.Dashboard;

namespace EduCenter.Api.Automation;

public class AutomationOptions
{
    /// <summary>Registers Hangfire and the recurring job. Tests turn it off and call the rules directly.</summary>
    public bool Enabled { get; set; } = true;

    public int SweepIntervalSeconds { get; set; } = 300;
}

/// <param name="RuleActions">Everything the rules recorded in the journal (tasks, recommendations, messages, priorities).</param>
public record AutomationRunResult(BillingResult Billing, SweepResult FollowUps, NotificationSweepResult Notifications, int RuleActions);

/// <summary>One pass of the Automation Engine: billing first (an invoice that just became overdue marks the student
/// Debt before anything else reads it), then follow-ups and support signals, then reminders.</summary>
public class AutomationRunner(BillingRules billing, FollowUpRules followUps, NotificationRules notifications, AutomationJournal journal,
    ILogger<AutomationRunner> logger)
{
    public async Task<AutomationRunResult> RunAsync(DateTimeOffset now, Guid? organizationId, AutomationTrigger trigger, CancellationToken ct = default)
    {
        var billingResult = await billing.RunAsync(now, organizationId, studentId: null, trigger, ct);
        var followUpResult = await followUps.RunSweepAsync(now, organizationId, trigger, ct);
        var notificationResult = await notifications.RunAsync(now, studentId: null, organizationId, trigger, ct);

        var total = billingResult.InvoicesIssued + billingResult.StudentsMarkedDebt + billingResult.StudentsCleared + journal.RecordedItems;
        if (total > 0)
            logger.LogInformation(
                "Automation ({Trigger}): {Invoices} invoice(s), {Debt} marked Debt, {Cleared} cleared, {DebtTasks} debt task(s), " +
                "{Trials} trial follow-up(s), {Absences} absence signal(s), {Homework} homework signal(s), {Messages} reminder message(s).",
                trigger, billingResult.InvoicesIssued, billingResult.StudentsMarkedDebt, billingResult.StudentsCleared, billingResult.DebtTasks,
                followUpResult.TrialFollowUps, followUpResult.AbsenceSignals, followUpResult.HomeworkSignals, notificationResult.Total);

        return new AutomationRunResult(billingResult, followUpResult, notificationResult, journal.RecordedItems);
    }
}

/// <summary>
/// The Hangfire recurring job (docs/00_TZ.md §44 "Hangfire at initial stage"). Every rule is idempotent, so there is
/// nothing to retry — a failed run shows up in Hangfire and the next scheduled run starts from current data. Runs
/// never overlap, even with several API instances sharing the same storage.
/// </summary>
public class AutomationSweepJob(AutomationRunner runner)
{
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    [AutomaticRetry(Attempts = 0)]
    public Task RunAsync(CancellationToken cancellationToken) =>
        runner.RunAsync(DateTimeOffset.UtcNow, organizationId: null, AutomationTrigger.Sweep, cancellationToken);
}

/// <summary>The Hangfire dashboard has no bearer-token authentication of its own; it is only mapped in Development.</summary>
public class DevelopmentDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) => true;
}
