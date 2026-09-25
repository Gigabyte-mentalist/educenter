using System.Globalization;
using EduCenter.Api.Notifications;
using EduCenter.Api.Students;
using EduCenter.Modules.Academic.Domain;
using EduCenter.Modules.Ai.Domain;
using EduCenter.Modules.Automation.Domain;
using EduCenter.Modules.Notifications.Domain;
using EduCenter.Modules.Students.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Automation;

/// <summary>
/// The Learning Signals of TZ §7 that were missing (docs/21.09-audit-jurnali S04.05): a skill that stays weak raises a support
/// recommendation and tells the group's main teacher — Writing grammar, Listening, Speaking fluency, Reading against the rest of the exam. The two
/// that already worked (attendance, homework) are in <c>FollowUpRules</c> proper; these run beside them, on the same footing:
/// thresholds and actions from the organization's settings, idempotent against the <em>trigger time</em> (when the newest result
/// behind the signal was recorded), and quiet while the student already has an open case in the group.
/// <para>What counts as "low" comes from <see cref="LearningProfileCalculator"/>, the arithmetic behind the Learning Profile. Every
/// reason carries the numbers, so the reviewer at <c>/support</c> sees why without opening the profile.</para>
/// <para><b>Speaking</b> reads only the sessions staff may see at all (<see cref="SpeakingSession.SharedWithStaff"/>, finished), and
/// its reason quotes band numbers only — never a word of what the student said. A teacher's corrected fluency band stands in for the
/// AI's, so a session the teacher has listened to and judged fine does not keep raising the alarm.</para>
/// </summary>
public partial class FollowUpRules
{
    /// <summary>Runs the three learning-signal rules for one organization; returns how many support recommendations were raised.</summary>
    public async Task<int> RunLearningSignalsAsync(Guid organizationId, AutomationTrigger trigger = AutomationTrigger.Sweep, CancellationToken ct = default) =>
        await RunWritingGrammarAsync(organizationId, trigger, ct)
        + await RunListeningLowAsync(organizationId, trigger, ct)
        + await RunReadingTimeAsync(organizationId, trigger, ct)
        + await RunSpeakingFluencyAsync(organizationId, trigger, ct);

    /// <summary>Writing → grammar weak: the grammar band of each of the student's last N finished essays is below the limit. A
    /// teacher's corrected grammar band stands in for the AI's, as everywhere else.</summary>
    private async Task<int> RunWritingGrammarAsync(Guid organizationId, AutomationTrigger trigger, CancellationToken ct)
    {
        if (await LoadLearningRuleAsync(organizationId, AutomationRuleCode.WritingGrammarSignal, ct) is not { } setup) return 0;
        var count = setup.Rule.Param(AutomationCatalog.Keys.Results);
        var limit = setup.Rule.Param(AutomationCatalog.Keys.MaxBand) / (decimal)AutomationCatalog.BandScale;
        var groups = await ActiveGroupsByStudentAsync(organizationId, ct);

        var essays = (await (
            from w in db.WritingEvaluations
            where w.OrganizationId == organizationId && w.Status == WritingEvaluationStatus.Completed && w.CompletedAt != null
                && db.Enrollments.Any(e => e.StudentId == w.StudentId && e.Status == EnrollmentStatus.Active)
            select new { w.StudentId, At = w.CompletedAt!.Value, Grammar = w.TeacherGrammarBand ?? w.GrammarBand }
        ).ToListAsync(ct)).Where(w => w.Grammar != null).ToList();

        int cases = 0, notified = 0;
        foreach (var student in essays.GroupBy(w => w.StudentId))
        {
            var newest = student.OrderByDescending(w => w.At).ToList();
            var low = LearningProfileCalculator.AllBelow(newest.Select(w => w.Grammar!.Value).ToList(), count, limit);
            if (low is null) continue;

            if (!groups.TryGetValue(student.Key, out var active)) continue;

            var signal = new Signal(organizationId, student.Key, active[0], newest.Take(count).Max(w => w.At),
                $"Oxirgi {count} ta insho: grammatika {Numbers(low)} (chegara {Band(limit)})");
            var (raised, told) = await RaiseLearningSignalAsync(signal, SupportSignalCode.WritingGrammarWeak, "Writing grammatikasi zaif", setup, ct);
            cases += raised;
            notified += told;
        }

        return await FinishAsync(organizationId, AutomationRuleCode.WritingGrammarSignal, trigger, cases, notified, ct);
    }

    /// <summary>Listening → several low results: each of the student's last N exams with a Listening band is below the limit.</summary>
    private async Task<int> RunListeningLowAsync(Guid organizationId, AutomationTrigger trigger, CancellationToken ct)
    {
        if (await LoadLearningRuleAsync(organizationId, AutomationRuleCode.ListeningLowSignal, ct) is not { } setup) return 0;
        var count = setup.Rule.Param(AutomationCatalog.Keys.Results);
        var limit = setup.Rule.Param(AutomationCatalog.Keys.MaxBand) / (decimal)AutomationCatalog.BandScale;
        var groups = await ActiveGroupsByStudentAsync(organizationId, ct);

        var results = await (
            from r in db.ExamResults
            join e in db.Exams on r.ExamId equals e.Id
            where r.OrganizationId == organizationId && r.Listening != null
                && db.Enrollments.Any(en => en.StudentId == r.StudentId && en.Status == EnrollmentStatus.Active)
            select new { r.StudentId, e.GroupId, e.Date, Band = r.Listening!.Value, r.CreatedAt }
        ).ToListAsync(ct);

        int cases = 0, notified = 0;
        foreach (var student in results.GroupBy(r => r.StudentId))
        {
            var newest = student.OrderByDescending(r => r.Date).ThenByDescending(r => r.CreatedAt).ToList();
            var low = LearningProfileCalculator.AllBelow(newest.Select(r => r.Band).ToList(), count, limit);
            if (low is null) continue;

            if (!groups.TryGetValue(student.Key, out var active)) continue;

            var signal = new Signal(organizationId, student.Key, GroupOf(active, newest[0].GroupId),
                newest.Take(count).Max(r => r.CreatedAt), $"Oxirgi {count} ta imtihon: Listening {Numbers(low)} (chegara {Band(limit)})");
            var (raised, told) = await RaiseLearningSignalAsync(signal, SupportSignalCode.ListeningLow, "Listening natijalari past", setup, ct);
            cases += raised;
            notified += told;
        }

        return await FinishAsync(organizationId, AutomationRuleCode.ListeningLowSignal, trigger, cases, notified, ct);
    }

    /// <summary>Reading → time management: in the student's latest exam with a Reading band, Reading trails the mean of the exam's
    /// other sections (Listening, Writing, Speaking — at least two) by at least the configured gap. A reader who understands the
    /// text but runs out of time shows exactly this shape; the recommendation says what was measured, not why.</summary>
    private async Task<int> RunReadingTimeAsync(Guid organizationId, AutomationTrigger trigger, CancellationToken ct)
    {
        if (await LoadLearningRuleAsync(organizationId, AutomationRuleCode.ReadingTimeSignal, ct) is not { } setup) return 0;
        var gapLimit = setup.Rule.Param(AutomationCatalog.Keys.Gap) / (decimal)AutomationCatalog.BandScale;
        var groups = await ActiveGroupsByStudentAsync(organizationId, ct);

        var results = await (
            from r in db.ExamResults
            join e in db.Exams on r.ExamId equals e.Id
            where r.OrganizationId == organizationId && r.Reading != null
                && db.Enrollments.Any(en => en.StudentId == r.StudentId && en.Status == EnrollmentStatus.Active)
            select new { r.StudentId, e.GroupId, e.Date, Reading = r.Reading!.Value, r.Listening, r.Writing, r.Speaking, r.CreatedAt }
        ).ToListAsync(ct);

        int cases = 0, notified = 0;
        foreach (var student in results.GroupBy(r => r.StudentId))
        {
            var latest = student.OrderByDescending(r => r.Date).ThenByDescending(r => r.CreatedAt).First();
            var others = new[] { latest.Listening, latest.Writing, latest.Speaking }.Where(b => b != null).Select(b => b!.Value).ToList();
            var comparison = LearningProfileCalculator.ReadingGap(latest.Reading, others);
            if (comparison is null || comparison.Value.Gap < gapLimit) continue;
            if (!groups.TryGetValue(student.Key, out var active)) continue;
            var (mean, gap) = comparison.Value;

            var signal = new Signal(organizationId, student.Key, GroupOf(active, latest.GroupId), latest.CreatedAt,
                $"Oxirgi imtihonda Reading {Band(latest.Reading)}, boshqa bo'limlar o'rtachasi {Band(mean)} (farq {Band(gap)}, chegara {Band(gapLimit)})");
            var (raised, told) = await RaiseLearningSignalAsync(signal, SupportSignalCode.ReadingTime, "Reading boshqa bo'limlardan past", setup, ct);
            cases += raised;
            notified += told;
        }

        return await FinishAsync(organizationId, AutomationRuleCode.ReadingTimeSignal, trigger, cases, notified, ct);
    }

    /// <summary>Speaking → low fluency: the fluency band of each of the student's last N finished, shared practice sessions is below the
    /// limit — the teacher's own band where they corrected it.</summary>
    private async Task<int> RunSpeakingFluencyAsync(Guid organizationId, AutomationTrigger trigger, CancellationToken ct)
    {
        if (await LoadLearningRuleAsync(organizationId, AutomationRuleCode.SpeakingFluencySignal, ct) is not { } setup) return 0;
        var count = setup.Rule.Param(AutomationCatalog.Keys.Results);
        var limit = setup.Rule.Param(AutomationCatalog.Keys.MaxBand) / (decimal)AutomationCatalog.BandScale;
        var groups = await ActiveGroupsByStudentAsync(organizationId, ct);

        var sessions = (await (
            from s in db.SpeakingSessions
            where s.OrganizationId == organizationId && s.SharedWithStaff && s.Status == SpeakingSessionStatus.Completed && s.CompletedAt != null
                && db.Enrollments.Any(e => e.StudentId == s.StudentId && e.Status == EnrollmentStatus.Active)
            select new { s.StudentId, At = s.CompletedAt!.Value, Fluency = s.TeacherFluencyBand ?? s.FluencyBand }
        ).ToListAsync(ct)).Where(s => s.Fluency != null).ToList();

        int cases = 0, notified = 0;
        foreach (var student in sessions.GroupBy(s => s.StudentId))
        {
            var newest = student.OrderByDescending(s => s.At).ToList();
            var low = LearningProfileCalculator.AllBelow(newest.Select(s => s.Fluency!.Value).ToList(), count, limit);
            if (low is null) continue;
            if (!groups.TryGetValue(student.Key, out var active)) continue;

            // Numbers only: what the student said is theirs, and the signal has no need of it.
            var signal = new Signal(organizationId, student.Key, active[0], newest.Take(count).Max(s => s.At),
                $"Oxirgi {count} ta Speaking mashqi: fluency {Numbers(low)} (chegara {Band(limit)})");
            var (raised, told) = await RaiseLearningSignalAsync(signal, SupportSignalCode.SpeakingFluencyLow, "Speaking: fluency past", setup, ct);
            cases += raised;
            notified += told;
        }

        return await FinishAsync(organizationId, AutomationRuleCode.SpeakingFluencySignal, trigger, cases, notified, ct);
    }

    private record LearningRuleSetup(EffectiveRule Rule, bool RaiseSupport, bool NotifyTeacher);

    /// <summary>The rule's settings, or null when it is off — or both of its actions are — so a disabled rule costs no queries.</summary>
    private async Task<LearningRuleSetup?> LoadLearningRuleAsync(Guid organizationId, string code, CancellationToken ct)
    {
        var rule = await settings.GetAsync(organizationId, code, ct);
        var support = rule.Enabled && rule.Action(AutomationCatalog.Keys.RaiseSupport);
        var teacher = rule.Enabled && rule.Action(AutomationCatalog.Keys.NotifyTeacher);
        return support || teacher ? new LearningRuleSetup(rule, support, teacher) : null;
    }

    /// <summary>Each student's active groups, the one they joined first at the front. A learning signal is about the student, not
    /// a group, but a support case belongs to one — and only a student who is still in a group can be helped.</summary>
    private async Task<Dictionary<Guid, List<Guid>>> ActiveGroupsByStudentAsync(Guid organizationId, CancellationToken ct) =>
        (await db.Enrollments
            .Where(e => e.OrganizationId == organizationId && e.Status == EnrollmentStatus.Active)
            .OrderBy(e => e.EnrolledAt)
            .Select(e => new { e.StudentId, e.GroupId })
            .ToListAsync(ct))
        .GroupBy(e => e.StudentId)
        .ToDictionary(g => g.Key, g => g.Select(e => e.GroupId).ToList());

    /// <summary>The group an exam was set in, when the student is still in it; otherwise the group they joined first.</summary>
    private static Guid GroupOf(List<Guid> activeGroups, Guid examGroupId) => activeGroups.Contains(examGroupId) ? examGroupId : activeGroups[0];

    private async Task<(int Raised, int Notified)> RaiseLearningSignalAsync(
        Signal signal, string code, string title, LearningRuleSetup setup, CancellationToken ct)
    {
        var raised = setup.RaiseSupport && await RaiseSupportSignalAsync(signal, code, ct) ? 1 : 0;
        return (raised, setup.NotifyTeacher ? await NotifyTeacherOfLearningSignalAsync(signal, code, title, ct) : 0);
    }

    /// <summary>Once per trigger — the key carries the moment of the newest result, so a sweep that sees the same weakness again
    /// says nothing, and a further weak result later is news again.</summary>
    private async Task<int> NotifyTeacherOfLearningSignalAsync(Signal signal, string code, string title, CancellationToken ct)
    {
        var groupName = await db.Groups.Where(g => g.Id == signal.GroupId).Select(g => g.Name).FirstAsync(ct);
        var studentName = await recipients.StudentNameAsync(signal.StudentId);

        return await notifier.NotifyAsync(signal.OrganizationId, await recipients.GroupTeachersAsync(signal.GroupId, GroupStaffRole.MainTeacher),
            new NotificationMessage(NotificationType.LearningSignal, $"{studentName}: {title}", $"{groupName} · {signal.Reason}.",
                $"/students/{signal.StudentId}", $"learning-signal:{code}:{signal.StudentId}:{signal.GroupId}:{signal.TriggeredAt.UtcTicks}"), ct);
    }

    /// <summary>Writes the journal line and commits the notifications the rule queued (recommendations are saved as they are raised).</summary>
    private async Task<int> FinishAsync(Guid organizationId, string ruleCode, AutomationTrigger trigger, int cases, int notified, CancellationToken ct)
    {
        journal.Record(organizationId, ruleCode, trigger, cases + notified,
            AutomationJournal.Describe((cases, "ta support tavsiyasi"), (notified, "ta o'qituvchiga xabar")));
        if (cases + notified > 0) await db.SaveChangesAsync(ct);
        return cases;
    }

    private static string Band(decimal value) => value.ToString("0.0", CultureInfo.InvariantCulture);
    private static string Numbers(IEnumerable<decimal> values) => string.Join(", ", values.Select(Band));
}
