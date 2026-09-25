using System.Globalization;
using EduCenter.Api.Academic;
using EduCenter.Api.Lms;
using EduCenter.Api.Notifications;
using EduCenter.Infrastructure;
using EduCenter.Modules.Automation.Domain;
using EduCenter.Modules.Finance.Domain;
using EduCenter.Modules.Notifications.Domain;
using EduCenter.Modules.Students.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Automation;

public record NotificationSweepResult(int PaymentDue, int PaymentOverdue, int HomeworkDeadline, int LessonReminders)
{
    public int Total => PaymentDue + PaymentOverdue + HomeworkDeadline + LessonReminders;
}

/// <summary>
/// Time-based reminders (docs/00_TZ.md §23, §39) driven by each organization's Automation Engine rules:
/// PAYMENT_REMINDERS, HOMEWORK_DEADLINE_REMINDER, LESSON_REMINDER. Each message carries a dedupe key per subject, so
/// every run delivers each reminder once. Event-driven messages (absence, exam result, ...) are sent on the write path.
/// </summary>
public class NotificationRules(AppDbContext db, NotificationDispatcher notifier, NotificationRecipients recipients,
    AutomationSettings settings, AutomationJournal journal)
{
    // Look-ahead horizons match the largest lead time the catalog allows for each rule.
    private static readonly TimeSpan HomeworkHorizon = TimeSpan.FromHours(72);
    private static readonly TimeSpan LessonHorizon = TimeSpan.FromHours(24);

    /// <param name="studentId">Narrows payment reminders to one student and skips the group-wide reminders (tests).</param>
    public async Task<NotificationSweepResult> RunAsync(DateTimeOffset now, Guid? studentId = null, Guid? organizationId = null,
        AutomationTrigger trigger = AutomationTrigger.Sweep, CancellationToken ct = default)
    {
        var (due, overdue) = await PaymentRemindersAsync(now, studentId, organizationId, trigger, ct);
        var homework = studentId is null ? await HomeworkDeadlinesAsync(now, organizationId, trigger, ct) : 0;
        var lessons = studentId is null ? await LessonRemindersAsync(now, organizationId, trigger, ct) : 0;

        await db.SaveChangesAsync(ct);
        return new NotificationSweepResult(due, overdue, homework, lessons);
    }

    private async Task<(int Due, int Overdue)> PaymentRemindersAsync(DateTimeOffset now, Guid? studentId, Guid? organizationId,
        AutomationTrigger trigger, CancellationToken ct)
    {
        var invoices = await (
            from i in db.Invoices
            join b in db.Branches on i.BranchId equals b.Id
            join g in db.Groups on i.GroupId equals g.Id
            join s in db.Students on i.StudentId equals s.Id
            join u in db.Users on s.UserId equals u.Id
            where (i.Status == InvoiceStatus.Pending || i.Status == InvoiceStatus.PartiallyPaid)
                && i.Amount - i.DiscountAmount - i.CreditedAmount > i.PaidAmount
                && (studentId == null || i.StudentId == studentId)
                && (organizationId == null || i.OrganizationId == organizationId)
            select new
            {
                i.Id, i.OrganizationId, i.StudentId, i.DueDate, GroupName = g.Name, StudentName = u.FullName, b.Timezone,
                Balance = i.Amount - i.DiscountAmount - i.CreditedAmount - i.PaidAmount
            }
        ).ToListAsync(ct);

        int due = 0, overdue = 0;
        foreach (var organizationInvoices in invoices.GroupBy(i => i.OrganizationId))
        {
            var rule = await settings.GetAsync(organizationInvoices.Key, AutomationRuleCode.PaymentReminders, ct);
            if (!rule.Enabled) continue;

            var notifyDue = rule.Action(AutomationCatalog.Keys.NotifyDue);
            var notifyOverdue = rule.Action(AutomationCatalog.Keys.NotifyOverdue);
            var dueLeadDays = rule.Param(AutomationCatalog.Keys.DueLeadDays);
            var overdueAfterDays = rule.Param(AutomationCatalog.Keys.OverdueAfterDays);

            int organizationDue = 0, organizationOverdue = 0;
            foreach (var invoice in organizationInvoices)
            {
                var today = BranchClock.Today(invoice.Timezone, now);
                var daysToDue = invoice.DueDate.DayNumber - today.DayNumber;

                if (notifyDue && daysToDue >= 0 && daysToDue <= dueLeadDays)
                {
                    organizationDue += await notifier.NotifyAsync(invoice.OrganizationId, await recipients.StudentAndParentsAsync(invoice.StudentId),
                        new NotificationMessage(NotificationType.PaymentDue,
                            daysToDue == 0 ? "Bugun to'lov kuni" : "To'lov muddati yaqinlashmoqda",
                            $"{invoice.StudentName} · {invoice.GroupName}: {Money(invoice.Balance)} so'm, muddat {invoice.DueDate:dd.MM.yyyy}.",
                            $"/students/{invoice.StudentId}", $"payment-due:{invoice.Id}"), ct);
                }
                else if (notifyOverdue && -daysToDue >= overdueAfterDays)
                {
                    organizationOverdue += await notifier.NotifyAsync(invoice.OrganizationId, await recipients.StudentAndParentsAsync(invoice.StudentId),
                        new NotificationMessage(NotificationType.PaymentOverdue, "To'lov kechikdi",
                            $"{invoice.StudentName} · {invoice.GroupName}: {Money(invoice.Balance)} so'm to'lov {-daysToDue} kun kechikdi.",
                            $"/students/{invoice.StudentId}", $"payment-overdue:{invoice.Id}"), ct);
                }
            }

            journal.Record(organizationInvoices.Key, AutomationRuleCode.PaymentReminders, trigger, organizationDue + organizationOverdue,
                AutomationJournal.Describe((organizationDue, "ta to'lov eslatmasi"), (organizationOverdue, "ta kechikish xabari")));
            due += organizationDue;
            overdue += organizationOverdue;
        }

        return (due, overdue);
    }

    private async Task<int> HomeworkDeadlinesAsync(DateTimeOffset now, Guid? organizationId, AutomationTrigger trigger, CancellationToken ct)
    {
        var horizon = now + HomeworkHorizon;
        var homework = await (
            from h in db.Homework
            join l in db.Lessons on h.LessonId equals l.Id
            join b in db.Branches on l.BranchId equals b.Id
            where h.Deadline > now && h.Deadline <= horizon
                && (organizationId == null || l.OrganizationId == organizationId)
            select new { h.Id, h.Title, h.Deadline, LessonId = l.Id, l.GroupId, l.OrganizationId, b.Timezone }
        ).ToListAsync(ct);

        var sent = 0;
        foreach (var organizationHomework in homework.GroupBy(h => h.OrganizationId))
        {
            var rule = await settings.GetAsync(organizationHomework.Key, AutomationRuleCode.HomeworkDeadlineReminder, ct);
            if (!rule.Enabled) continue;
            var until = now + TimeSpan.FromHours(rule.Param(AutomationCatalog.Keys.LeadHours));

            var organizationSent = 0;
            foreach (var item in organizationHomework.Where(h => h.Deadline <= until))
            {
                // An individual homework reminds only the students it was given to.
                var pending = await (
                    from e in db.Enrollments
                    join s in db.Students.AssignedStudents(db, item.Id) on e.StudentId equals s.Id
                    where e.GroupId == item.GroupId && e.Status == EnrollmentStatus.Active
                        && !db.HomeworkSubmissions.Any(x => x.HomeworkId == item.Id && x.StudentId == s.Id)
                    select s.UserId
                ).ToListAsync(ct);

                var deadline = BranchClock.LocalDateTime(item.Timezone, item.Deadline);
                organizationSent += await notifier.NotifyAsync(item.OrganizationId, pending,
                    new NotificationMessage(NotificationType.HomeworkDeadline, $"Homework muddati yaqin: {item.Title}",
                        $"Topshirish muddati: {deadline:dd.MM HH:mm}.", $"/schedule/{item.LessonId}", $"homework-deadline:{item.Id}"), ct);
            }

            journal.Record(organizationHomework.Key, AutomationRuleCode.HomeworkDeadlineReminder, trigger, organizationSent,
                AutomationJournal.Describe((organizationSent, "ta homework eslatmasi")));
            sent += organizationSent;
        }

        return sent;
    }

    private async Task<int> LessonRemindersAsync(DateTimeOffset now, Guid? organizationId, AutomationTrigger trigger, CancellationToken ct)
    {
        var horizon = now + LessonHorizon;
        var lessons = await (
            from l in db.Lessons.Held() // no reminder for a lesson that was cancelled
            join g in db.Groups on l.GroupId equals g.Id
            join b in db.Branches on l.BranchId equals b.Id
            where l.StartsAt > now && l.StartsAt <= horizon
                && (organizationId == null || l.OrganizationId == organizationId)
            select new { l.Id, l.OrganizationId, l.GroupId, l.TeacherId, l.StartsAt, l.RoomName, l.Topic, GroupName = g.Name, b.Timezone }
        ).ToListAsync(ct);

        var sent = 0;
        foreach (var organizationLessons in lessons.GroupBy(l => l.OrganizationId))
        {
            var rule = await settings.GetAsync(organizationLessons.Key, AutomationRuleCode.LessonReminder, ct);
            if (!rule.Enabled) continue;
            var until = now + TimeSpan.FromHours(rule.Param(AutomationCatalog.Keys.LeadHours));

            var organizationSent = 0;
            foreach (var lesson in organizationLessons.Where(l => l.StartsAt <= until))
            {
                var audience = await recipients.GroupStudentsAsync(lesson.GroupId);
                audience.Add(lesson.TeacherId);

                var starts = BranchClock.LocalDateTime(lesson.Timezone, lesson.StartsAt);
                organizationSent += await notifier.NotifyAsync(lesson.OrganizationId, audience,
                    new NotificationMessage(NotificationType.LessonReminder, $"Dars tez orada: {lesson.GroupName}",
                        $"{starts:dd.MM HH:mm} da, xona {lesson.RoomName}" + (lesson.Topic is null ? "." : $" — {lesson.Topic}."),
                        $"/schedule/{lesson.Id}", $"lesson-reminder:{lesson.Id}"), ct);
            }

            journal.Record(organizationLessons.Key, AutomationRuleCode.LessonReminder, trigger, organizationSent,
                AutomationJournal.Describe((organizationSent, "ta dars eslatmasi")));
            sent += organizationSent;
        }

        return sent;
    }

    private static string Money(decimal value) => value.ToString("#,0", CultureInfo.InvariantCulture).Replace(",", " ");
}
