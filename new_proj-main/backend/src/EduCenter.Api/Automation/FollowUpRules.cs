using System.Text.Json;
using EduCenter.Api.Academic;
using EduCenter.Api.Lms;
using EduCenter.Api.Notifications;
using EduCenter.Infrastructure;
using EduCenter.Modules.Academic.Domain;
using EduCenter.Modules.Automation.Domain;
using EduCenter.Modules.Crm.Domain;
using EduCenter.Modules.Lms.Domain;
using EduCenter.Modules.Notifications.Domain;
using EduCenter.Modules.Students.Domain;
using EduCenter.Modules.Timeline.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Automation;

public record SweepResult(int TrialFollowUps, int AbsenceSignals, int HomeworkSignals, int LearningSignals = 0);

/// <summary>Limits absence evaluation to the students just marked absent in one lesson's group.</summary>
public record AbsenceTarget(Guid GroupId, IReadOnlyCollection<Guid> StudentIds);

/// <summary>
/// CRM follow-ups (docs/00_TZ.md §10) and the Support Needed Engine signals (§8), driven by each organization's
/// Automation Engine settings (§40, <see cref="AutomationCatalog"/>): thresholds and actions come from
/// <see cref="AutomationSettings"/>, not constants.
///
/// Every rule is idempotent against a <em>trigger time</em> (when the last absence was marked, when the newest missed
/// deadline passed, when the trial took place): if a case or task was already created at or after that moment — even
/// one since dismissed or completed — the rule stays quiet. Only a newer trigger raises it again. That is what lets
/// the same rules run on the write path, on the schedule and on "run now".
/// </summary>
public partial class FollowUpRules(AppDbContext db, StaffResolver staff, NotificationDispatcher notifier, NotificationRecipients recipients,
    AutomationSettings settings, AutomationJournal journal, ILogger<FollowUpRules> logger)
{
    private record Signal(Guid OrganizationId, Guid StudentId, Guid GroupId, DateTimeOffset TriggeredAt, string Reason);

    public async Task<SweepResult> RunSweepAsync(DateTimeOffset now, Guid? organizationId = null,
        AutomationTrigger trigger = AutomationTrigger.Sweep, CancellationToken ct = default)
    {
        List<Guid> organizations = organizationId is { } only ? [only] : await db.Organizations.Select(o => o.Id).ToListAsync(ct);

        int trials = 0, absences = 0, homework = 0, learning = 0;
        foreach (var organization in organizations)
        {
            trials += await RunTrialFollowUpsAsync(now, organization, trigger, ct);
            absences += await EvaluateAbsencesAsync(organization, null, trigger, ct);
            homework += await RunHomeworkSignalsAsync(now, organization, trigger, ct);
            learning += await RunLearningSignalsAsync(organization, trigger, ct);
        }

        return new SweepResult(trials, absences, homework, learning);
    }

    /// <summary>§10 Trial follow-up: trial attended, no enrollment within the configured hours → operator task (+ message)
    /// and/or the lead's priority raised.</summary>
    public async Task<int> RunTrialFollowUpsAsync(DateTimeOffset now, Guid organizationId,
        AutomationTrigger trigger = AutomationTrigger.Sweep, CancellationToken ct = default)
    {
        var rule = await settings.GetAsync(organizationId, AutomationRuleCode.TrialFollowUp, ct);
        var createTask = rule.Enabled && rule.Action(AutomationCatalog.Keys.CreateTask);
        var raisePriority = rule.Enabled && rule.Action(AutomationCatalog.Keys.RaisePriority);
        if (!createTask && !raisePriority) return 0;

        var hours = rule.Param(AutomationCatalog.Keys.Hours);
        var cutoff = now - TimeSpan.FromHours(hours);

        var attended = await (
            from t in db.TrialLessons
            join l in db.Leads on t.LeadId equals l.Id
            where l.OrganizationId == organizationId
                && t.AttendanceStatus == TrialAttendanceStatus.Attended
                && t.ScheduledAt <= cutoff
                && l.ConvertedStudentId == null
                && l.Status != LeadStatus.Converted
                && l.Status != LeadStatus.Lost
                && l.Status != LeadStatus.NotInterested
            select new { LeadId = l.Id, t.ScheduledAt }
        ).ToListAsync(ct);
        if (attended.Count == 0) return 0;

        var latestTrial = attended.GroupBy(a => a.LeadId).ToDictionary(g => g.Key, g => g.Max(a => a.ScheduledAt));
        var leadIds = latestTrial.Keys.ToList();
        var leads = await db.Leads.Where(l => leadIds.Contains(l.Id)).ToListAsync(ct);

        int tasks = 0, raised = 0;
        foreach (var lead in leads)
        {
            if (raisePriority && lead.Priority != LeadPriority.High)
            {
                lead.RaisePriority();
                raised++;
            }

            if (!createTask) continue;

            var trialAt = latestTrial[lead.Id];
            var covered = await db.CrmTasks.AnyAsync(task => task.LeadId == lead.Id
                && task.ReasonCode == CrmTaskReason.TrialFollowUp && task.CreatedAt >= trialAt, ct);
            if (covered) continue;

            var note = $"Trial darsga keldi, lekin {hours} soat ichida guruhga yozilmadi.";
            db.CrmTasks.Add(new CrmTask(lead.OrganizationId, lead.Id, studentId: null, lead.OperatorId,
                CrmTaskType.FollowUp, DateTimeOffset.UtcNow, CrmTaskReason.TrialFollowUp, note));
            await notifier.NotifyAsync(lead.OrganizationId, [lead.OperatorId],
                new NotificationMessage(NotificationType.TaskAssigned, "Yangi vazifa: trial follow-up", $"{lead.FullName}: {note}", "/crm/tasks"), ct);
            tasks++;
        }

        journal.Record(organizationId, AutomationRuleCode.TrialFollowUp, trigger, tasks + raised,
            AutomationJournal.Describe((tasks, "ta vazifa"), (raised, "ta lid «Muhim» deb belgilandi")));
        if (tasks + raised > 0) await db.SaveChangesAsync(ct);
        return tasks;
    }

    /// <summary>§10 Absence follow-up: the student's last N marked lessons in a group are all Absent → support
    /// recommendation, operator task and a message to the main teacher, each switchable. <paramref name="target"/>
    /// narrows the check to one attendance save; <c>null</c> evaluates every active enrollment of the organization.</summary>
    public async Task<int> EvaluateAbsencesAsync(Guid organizationId, AbsenceTarget? target,
        AutomationTrigger trigger = AutomationTrigger.Event, CancellationToken ct = default)
    {
        var rule = await settings.GetAsync(organizationId, AutomationRuleCode.AbsenceFollowUp, ct);
        var createTask = rule.Enabled && rule.Action(AutomationCatalog.Keys.CreateTask);
        var raiseSupport = rule.Enabled && rule.Action(AutomationCatalog.Keys.RaiseSupport);
        var notifyTeacher = rule.Enabled && rule.Action(AutomationCatalog.Keys.NotifyTeacher);
        if (!createTask && !raiseSupport && !notifyTeacher) return 0;

        var threshold = rule.Param(AutomationCatalog.Keys.ConsecutiveAbsences);

        var enrollments = db.Enrollments.Where(e => e.OrganizationId == organizationId && e.Status == EnrollmentStatus.Active);
        if (target is not null)
            enrollments = enrollments.Where(e => e.GroupId == target.GroupId && target.StudentIds.Contains(e.StudentId));

        var rows = await (
            from e in enrollments
            join l in db.Lessons.Held() on e.GroupId equals l.GroupId
            join a in db.Attendance on new { LessonId = l.Id, e.StudentId } equals new { a.LessonId, a.StudentId }
            select new { e.OrganizationId, e.StudentId, e.GroupId, l.StartsAt, a.Status, MarkedAt = a.UpdatedAt ?? a.CreatedAt }
        ).ToListAsync(ct);

        var signals = rows
            .GroupBy(r => (r.OrganizationId, r.StudentId, r.GroupId))
            .Select(g => g.OrderByDescending(r => r.StartsAt).Take(threshold).ToList())
            .Where(last => last.Count == threshold && last.All(r => r.Status == AttendanceStatus.Absent))
            .Select(last => new Signal(last[0].OrganizationId, last[0].StudentId, last[0].GroupId,
                last.Max(r => r.MarkedAt), $"Ketma-ket {threshold} ta darsga kelmadi"))
            .ToList();

        int cases = 0, tasks = 0, notified = 0;
        foreach (var signal in signals)
        {
            if (raiseSupport && await RaiseSupportSignalAsync(signal, SupportSignalCode.ConsecutiveAbsences, ct)) cases++;
            if (notifyTeacher) notified += await NotifyTeacherAsync(signal, ct);
            if (createTask && await EnsureAbsenceTaskAsync(signal, ct)) tasks++;
        }

        journal.Record(organizationId, AutomationRuleCode.AbsenceFollowUp, trigger, cases + tasks + notified,
            AutomationJournal.Describe((cases, "ta support tavsiyasi"), (tasks, "ta qo'ng'iroq vazifasi"), (notified, "ta o'qituvchiga xabar")));
        await db.SaveChangesAsync(ct);
        return cases;
    }

    /// <summary>§8 "homework completion pasayishi": of the last N homework whose deadline has passed since the student
    /// joined the group, at most M were submitted → support recommendation.</summary>
    public async Task<int> RunHomeworkSignalsAsync(DateTimeOffset now, Guid organizationId,
        AutomationTrigger trigger = AutomationTrigger.Sweep, CancellationToken ct = default)
    {
        var rule = await settings.GetAsync(organizationId, AutomationRuleCode.HomeworkSupportSignal, ct);
        if (!rule.Enabled) return 0;

        var window = rule.Param(AutomationCatalog.Keys.Window);
        var maxSubmitted = rule.Param(AutomationCatalog.Keys.MaxSubmitted);

        var due = await (
            from h in db.Homework
            join l in db.Lessons on h.LessonId equals l.Id
            where l.OrganizationId == organizationId && h.Deadline <= now
            select new { h.Id, l.GroupId, h.Deadline }
        ).ToListAsync(ct);
        if (due.Count == 0) return 0;

        var dueIds = due.Select(d => d.Id).ToList();
        var submitted = (await db.HomeworkSubmissions
                .Where(s => dueIds.Contains(s.HomeworkId))
                .Select(s => new { s.HomeworkId, s.StudentId })
                .ToListAsync(ct))
            .Select(s => (s.HomeworkId, s.StudentId))
            .ToHashSet();

        var enrollments = await db.Enrollments
            .Where(e => e.OrganizationId == organizationId && e.Status == EnrollmentStatus.Active)
            .Select(e => new { e.OrganizationId, e.StudentId, e.GroupId, e.EnrolledAt })
            .ToListAsync(ct);

        // "The last N homework" are the ones the student was given: an individual task set for a classmate is not one they missed.
        var targeting = await db.LoadTargetingAsync(dueIds, ct);
        var dueByGroup = due.ToLookup(d => d.GroupId);
        var created = 0;

        foreach (var enrollment in enrollments)
        {
            var recent = dueByGroup[enrollment.GroupId]
                .Where(d => d.Deadline >= enrollment.EnrolledAt && targeting.IsAssigned(d.Id, enrollment.StudentId))
                .OrderByDescending(d => d.Deadline)
                .Take(window)
                .ToList();
            if (recent.Count < window) continue;

            var done = recent.Count(d => submitted.Contains((d.Id, enrollment.StudentId)));
            if (done > maxSubmitted) continue;

            var signal = new Signal(enrollment.OrganizationId, enrollment.StudentId, enrollment.GroupId, recent[0].Deadline,
                $"Oxirgi {window} ta homeworkdan faqat {done} tasi topshirilgan");
            if (await RaiseSupportSignalAsync(signal, SupportSignalCode.HomeworkMissed, ct)) created++;
        }

        journal.Record(organizationId, AutomationRuleCode.HomeworkSupportSignal, trigger, created,
            AutomationJournal.Describe((created, "ta support tavsiyasi")));
        if (created > 0) await db.SaveChangesAsync(ct);
        return created;
    }

    private async Task<bool> RaiseSupportSignalAsync(Signal signal, string code, CancellationToken ct)
    {
        // Covered if this trigger was already raised (in any status, including dismissed), or if the student
        // already has an open case in this group — a second concurrent recommendation adds noise, not help.
        var covered = await db.SupportCases.AnyAsync(c =>
            c.StudentId == signal.StudentId && c.GroupId == signal.GroupId
            && ((c.SignalCode == code && c.CreatedAt >= signal.TriggeredAt)
                || c.Status == SupportCaseStatus.Recommended
                || c.Status == SupportCaseStatus.Assigned), ct);
        if (covered) return false;

        db.SupportCases.Add(new SupportCase(signal.OrganizationId, signal.StudentId, signal.GroupId,
            SupportCaseSource.System, code, signal.Reason, proposedBy: null));
        db.StudentTimelineEvents.Add(new StudentTimelineEvent(signal.OrganizationId, signal.StudentId,
            TimelineEventType.SupportRecommended,
            JsonSerializer.Serialize(new { signal = code, groupId = signal.GroupId, reason = signal.Reason })));
        await NotifyAdministratorsAsync(signal, ct);

        // Saved per signal so the next signal's coverage check sees this case.
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>A new recommendation asks the branch administrators to review it.</summary>
    private async Task NotifyAdministratorsAsync(Signal signal, CancellationToken ct)
    {
        var group = await db.Groups.Where(g => g.Id == signal.GroupId).Select(g => new { g.Name, g.BranchId }).FirstAsync(ct);
        var studentName = await recipients.StudentNameAsync(signal.StudentId);

        await notifier.NotifyAsync(signal.OrganizationId, await recipients.BranchAdministratorsAsync(signal.OrganizationId, group.BranchId),
            new NotificationMessage(NotificationType.SupportRecommended, $"Support tavsiyasi: {studentName}",
                $"{group.Name} · {signal.Reason}.", "/support"), ct);
    }

    /// <summary>TZ §10 absence follow-up "teacher notification" — once per trigger, whether or not support was raised.</summary>
    private async Task<int> NotifyTeacherAsync(Signal signal, CancellationToken ct)
    {
        var groupName = await db.Groups.Where(g => g.Id == signal.GroupId).Select(g => g.Name).FirstAsync(ct);
        var studentName = await recipients.StudentNameAsync(signal.StudentId);

        return await notifier.NotifyAsync(signal.OrganizationId, await recipients.GroupTeachersAsync(signal.GroupId, GroupStaffRole.MainTeacher),
            new NotificationMessage(NotificationType.AbsenceStreak, $"{studentName} ketma-ket darsga kelmayapti",
                $"{groupName} · {signal.Reason}.", $"/students/{signal.StudentId}",
                $"absence-streak:{signal.StudentId}:{signal.GroupId}:{signal.TriggeredAt.UtcTicks}"), ct);
    }

    private async Task<bool> EnsureAbsenceTaskAsync(Signal signal, CancellationToken ct)
    {
        var covered = await db.CrmTasks.AnyAsync(t =>
            t.StudentId == signal.StudentId && t.ReasonCode == CrmTaskReason.ConsecutiveAbsences
            && (t.Status == CrmTaskStatus.Open || t.CreatedAt >= signal.TriggeredAt), ct);
        if (covered) return false;

        var branchId = await db.Groups.Where(g => g.Id == signal.GroupId).Select(g => (Guid?)g.BranchId).FirstOrDefaultAsync(ct);
        var assignee = await staff.ResolveOperatorForStudentAsync(signal.OrganizationId, signal.StudentId, branchId, ct);
        if (assignee is null)
        {
            logger.LogWarning("No operator or administrator found for student {StudentId} (group {GroupId}); absence task not created.",
                signal.StudentId, signal.GroupId);
            return false;
        }

        db.CrmTasks.Add(new CrmTask(signal.OrganizationId, leadId: null, signal.StudentId, assignee.Value,
            CrmTaskType.Absence, DateTimeOffset.UtcNow, CrmTaskReason.ConsecutiveAbsences,
            $"{signal.Reason}. Student yoki ota-onasi bilan bog'laning."));
        await notifier.NotifyAsync(signal.OrganizationId, [assignee.Value],
            new NotificationMessage(NotificationType.TaskAssigned, "Yangi vazifa: ketma-ket yo'qlik",
                $"{await recipients.StudentNameAsync(signal.StudentId)}: {signal.Reason}.", "/crm/tasks"), ct);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
