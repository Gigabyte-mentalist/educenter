using EduCenter.Api.Academic;
using EduCenter.Api.Authorization;
using EduCenter.Api.Lms;
using EduCenter.Infrastructure;
using EduCenter.Modules.Ai.Domain;
using EduCenter.Modules.Lms.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Students;

public record StudentAttendanceRow(Guid Id, Guid LessonId, string GroupName, string? Topic,
    DateTimeOffset StartsAt, AttendanceStatus Status);
/// <param name="AttachmentName">The teacher's worksheet for the task, downloaded through <c>/api/homework/{Id}/attachment</c>.</param>
/// <param name="SubmissionId">The student's own answer, when handed in; with <paramref name="SubmissionFileName"/> set the file is
/// downloaded through <c>/api/homework/submissions/{SubmissionId}/attachment</c>.</param>
public record StudentHomeworkRow(Guid Id, Guid LessonId, string GroupName, string Title,
    DateTimeOffset Deadline, int MaxScore, DateTimeOffset? SubmittedAt, int? Score, string? Feedback,
    string? AttachmentName = null, Guid? SubmissionId = null, string? SubmissionFileName = null);
/// <param name="Grammar">Percent 0–100 (the skills are IELTS bands); null on exams that did not grade it.</param>
public record StudentExamRow(Guid Id, string Name, string GroupName, DateOnly Date,
    decimal? Listening, decimal? Reading, decimal? Writing, decimal? Speaking, decimal? Overall,
    decimal? Grammar = null, decimal? Vocabulary = null);

/// <summary>The eight figures of TZ §7. Each is null while nothing is behind it; skills and percentages carry the number of
/// results they rest on and a trend (see <see cref="LearningProfileCalculator"/>).</summary>
public record LearningProfileDto(
    SkillDto Listening, SkillDto Reading, SkillDto Writing, SkillDto Speaking,
    PercentDto Grammar, PercentDto Vocabulary, PercentDto Attendance, PercentDto HomeworkCompletion);

[ApiController]
[Route("api/students/{id:guid}")]
public class StudentLearningController(AppDbContext db) : ControllerBase
{
    // Student ownership and group visibility are separate checks: access to a student in one group
    // must not expose that student's academic records from another teacher's group or branch.
    private async Task<bool> CanReadAsync(Guid id)
    {
        var scope = HttpContext.RequireResolvedScope();
        return await scope.CanAccessStudentAsync(db, id)
            && await db.Students.AnyAsync(s => s.Id == id && s.OrganizationId == scope.OrganizationId);
    }

    [HttpGet("attendance")]
    [Authorize(Policy = "attendance.view")]
    public async Task<ActionResult<List<StudentAttendanceRow>>> Attendance(Guid id)
    {
        if (!await CanReadAsync(id)) return Forbid();
        var scope = HttpContext.RequireResolvedScope();
        var groups = await scope.ScopedGroupsAsync(db);
        return Ok(await (
            from a in db.Attendance
            join l in db.Lessons.Held() on a.LessonId equals l.Id
            join g in groups on l.GroupId equals g.Id
            where a.StudentId == id && a.OrganizationId == scope.OrganizationId
                && l.OrganizationId == scope.OrganizationId
            orderby l.StartsAt descending, a.Id
            select new StudentAttendanceRow(a.Id, l.Id, g.Name, l.Topic, l.StartsAt, a.Status)
        ).ToListAsync());
    }

    [HttpGet("homework")]
    [Authorize(Policy = "homework.view")]
    public async Task<ActionResult<List<StudentHomeworkRow>>> Homework(Guid id)
    {
        if (!await CanReadAsync(id)) return Forbid();
        var scope = HttpContext.RequireResolvedScope();
        var groups = await scope.ScopedGroupsAsync(db);
        var assignments = await (
            from h in db.Homework.AssignedTo(db, id)
            join l in db.Lessons on h.LessonId equals l.Id
            join g in groups on l.GroupId equals g.Id
            where h.OrganizationId == scope.OrganizationId && l.OrganizationId == scope.OrganizationId
                && db.Enrollments.Any(e => e.StudentId == id && e.GroupId == g.Id
                    && e.OrganizationId == scope.OrganizationId)
            orderby h.Deadline descending, h.Id
            select new { h.Id, h.LessonId, GroupName = g.Name, h.Title, h.Deadline, h.MaxScore, h.AttachmentName }
        ).ToListAsync();
        var ids = assignments.Select(h => h.Id).ToList();
        var submissions = await db.HomeworkSubmissions
            .Where(s => s.StudentId == id && s.OrganizationId == scope.OrganizationId && ids.Contains(s.HomeworkId))
            .OrderByDescending(s => s.SubmittedAt).ThenBy(s => s.Id).ToListAsync();
        var latest = submissions.DistinctBy(s => s.HomeworkId).ToDictionary(s => s.HomeworkId);
        return Ok(assignments.Select(h =>
        {
            latest.TryGetValue(h.Id, out var submission);
            return new StudentHomeworkRow(h.Id, h.LessonId, h.GroupName, h.Title, h.Deadline,
                h.MaxScore, submission?.SubmittedAt, submission?.Score, submission?.Feedback,
                h.AttachmentName, submission?.Id, submission?.AttachmentName);
        }).ToList());
    }

    [HttpGet("exams")]
    [Authorize(Policy = "exams.view")]
    public async Task<ActionResult<List<StudentExamRow>>> Exams(Guid id)
    {
        if (!await CanReadAsync(id)) return Forbid();
        var scope = HttpContext.RequireResolvedScope();
        var groups = await scope.ScopedGroupsAsync(db);
        return Ok(await (
            from r in db.ExamResults
            join e in db.Exams on r.ExamId equals e.Id
            join g in groups on e.GroupId equals g.Id
            where r.StudentId == id && r.OrganizationId == scope.OrganizationId
                && e.OrganizationId == scope.OrganizationId
            orderby e.Date descending, r.Id
            select new StudentExamRow(r.Id, e.Name, g.Name, e.Date,
                r.Listening, r.Reading, r.Writing, r.Speaking, r.Overall, r.Grammar, r.Vocabulary)
        ).ToListAsync());
    }

    /// <summary>
    /// The eight numbers of TZ §7 — Listening, Reading, Writing, Speaking, Grammar, Vocabulary, Attendance, Homework — worked out
    /// from what the caller can already open in the Attendance, Homework and Exams tabs (the same group visibility), nothing new.
    /// A number with nothing behind it is null, never zero.
    /// <para>Speaking is the exam's Speaking band only. An AI practice band is an estimate read off a transcript — it does not judge
    /// pronunciation — so it is shown in the Speaking tab (<c>StudentSpeakingController</c>) but never averaged into this figure.</para>
    /// </summary>
    [HttpGet("learning-profile")]
    [Authorize(Policy = "students.view")]
    public async Task<ActionResult<LearningProfileDto>> LearningProfile(Guid id, CancellationToken ct = default)
    {
        if (!await CanReadAsync(id)) return Forbid();
        var scope = HttpContext.RequireResolvedScope();
        var org = scope.OrganizationId;
        var groups = await scope.ScopedGroupsAsync(db);
        var now = DateTimeOffset.UtcNow;

        var exams = await (
            from r in db.ExamResults
            join e in db.Exams on r.ExamId equals e.Id
            join g in groups on e.GroupId equals g.Id
            where r.StudentId == id && r.OrganizationId == org && e.OrganizationId == org
            select new { e.Date, r.Listening, r.Reading, r.Writing, r.Speaking, r.Grammar, r.Vocabulary }
        ).ToListAsync(ct);

        // Writing is judged by the exam and by the essays the AI graded; where a teacher corrected the AI, the teacher's band stands.
        var essays = await db.WritingEvaluations
            .Where(w => w.StudentId == id && w.OrganizationId == org && w.Status == WritingEvaluationStatus.Completed
                && w.CompletedAt != null)
            .OrderByDescending(w => w.CompletedAt).Take(LearningProfileCalculator.Window + 1)
            .Select(w => new { At = w.CompletedAt!.Value, Band = w.TeacherBand ?? w.EstimatedBand })
            .ToListAsync(ct);

        // A Grammar or Vocabulary task the teacher marked is a measurement too: score out of the task's maximum, as a percentage.
        var practice = await (
            from s in db.HomeworkSubmissions
            join h in db.Homework on s.HomeworkId equals h.Id
            join l in db.Lessons on h.LessonId equals l.Id
            join g in groups on l.GroupId equals g.Id
            where s.StudentId == id && s.OrganizationId == org && h.OrganizationId == org && s.Score != null && h.MaxScore > 0
                && (h.Type == HomeworkType.Grammar || h.Type == HomeworkType.Vocabulary)
            select new { h.Type, Score = s.Score!.Value, h.MaxScore, At = s.ReviewedAt ?? s.SubmittedAt }
        ).ToListAsync(ct);

        var marks = await (
            from a in db.Attendance
            join l in db.Lessons.Held() on a.LessonId equals l.Id
            join g in groups on l.GroupId equals g.Id
            where a.StudentId == id && a.OrganizationId == org && l.OrganizationId == org
            select new { l.StartsAt, a.Status }
        ).ToListAsync(ct);

        var assigned = await (
            from h in db.Homework.AssignedTo(db, id)
            join l in db.Lessons on h.LessonId equals l.Id
            join g in groups on l.GroupId equals g.Id
            where h.OrganizationId == org && l.OrganizationId == org
                && db.Enrollments.Any(e => e.StudentId == id && e.GroupId == g.Id && e.OrganizationId == org)
            select new { h.Id, h.Deadline }
        ).ToListAsync(ct);
        var submittedIds = (await db.HomeworkSubmissions
            .Where(s => s.StudentId == id && s.OrganizationId == org)
            .Select(s => s.HomeworkId).ToListAsync(ct)).ToHashSet();

        static ProfileSample Sample(DateOnly date, decimal value) => new(ExamMoment(date), value);

        var listening = exams.Where(x => x.Listening != null).Select(x => Sample(x.Date, x.Listening!.Value));
        var reading = exams.Where(x => x.Reading != null).Select(x => Sample(x.Date, x.Reading!.Value));
        var speaking = exams.Where(x => x.Speaking != null).Select(x => Sample(x.Date, x.Speaking!.Value));
        var writing = exams.Where(x => x.Writing != null).Select(x => Sample(x.Date, x.Writing!.Value))
            .Concat(essays.Where(w => w.Band != null).Select(w => new ProfileSample(w.At, w.Band!.Value)));

        IEnumerable<ProfileSample> Practised(HomeworkType type) => practice.Where(p => p.Type == type)
            .Select(p => new ProfileSample(p.At, Math.Clamp(p.Score * 100m / p.MaxScore, 0m, 100m)));
        var grammar = exams.Where(x => x.Grammar != null).Select(x => Sample(x.Date, x.Grammar!.Value))
            .Concat(Practised(HomeworkType.Grammar));
        var vocabulary = exams.Where(x => x.Vocabulary != null).Select(x => Sample(x.Date, x.Vocabulary!.Value))
            .Concat(Practised(HomeworkType.Vocabulary));

        return Ok(new LearningProfileDto(
            LearningProfileCalculator.Band(listening), LearningProfileCalculator.Band(reading),
            LearningProfileCalculator.Band(writing), LearningProfileCalculator.Band(speaking),
            LearningProfileCalculator.Percent(grammar), LearningProfileCalculator.Percent(vocabulary),
            LearningProfileCalculator.Attendance(marks.Select(m => (m.StartsAt, m.Status))),
            LearningProfileCalculator.HomeworkCompletion(assigned.Select(h => (h.Deadline, submittedIds.Contains(h.Id))), now)));
    }

    /// <summary>An exam has a day, not a moment; noon UTC puts it between the essays and homework marked that day, whichever
    /// side of midnight in Tashkent they fall on.</summary>
    private static DateTimeOffset ExamMoment(DateOnly date) => new(date.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
}
