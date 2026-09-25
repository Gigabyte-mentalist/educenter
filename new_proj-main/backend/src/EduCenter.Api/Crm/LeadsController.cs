using EduCenter.Api.Academic;
using EduCenter.Api.Auth;
using EduCenter.Api.Authorization;
using EduCenter.Api.Automation;
using EduCenter.Api.Students;
using EduCenter.Infrastructure;
using EduCenter.Modules.Academic.Domain;
using EduCenter.Modules.Crm.Domain;
using EduCenter.Modules.Identity.Domain;
using EduCenter.Modules.Students.Domain;
using EduCenter.Modules.Timeline.Domain;
using EduCenter.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Crm;

/// <param name="LatestTrial">The most recent trial lesson booked for this lead — the board shows it on the card so the
/// operator sees "who is coming where, when" and can mark attendance without opening the profile. Null when none.</param>
/// <param name="PreferredTime">Comma-separated <see cref="PreferredTimeCodes"/>; null = any time.</param>
/// <param name="CurrentLevel">The level the operator recorded ("B1"); the placement test only suggests one.</param>
public record LeadDto(
    Guid Id, Guid BranchId, string FullName, string Phone, string Source, string InterestedCourse,
    Guid OperatorId, LeadStatus Status, string? Notes, string? LostReason, Guid? ConvertedStudentId, DateTimeOffset CreatedAt, LeadPriority Priority,
    LeadTrialDto? LatestTrial = null, string? PreferredTime = null, string? CurrentLevel = null);

public record LeadTrialDto(Guid Id, Guid GroupId, string GroupName, DateTimeOffset ScheduledAt, TrialAttendanceStatus AttendanceStatus);

public record CreateLeadRequest(Guid BranchId, string FullName, string Phone, string Source, string InterestedCourse,
    string? PreferredTime = null, string? CurrentLevel = null);
/// <summary>A field left out (null) stays as it is; an empty string clears it.</summary>
public record UpdateLeadPreferencesRequest(string? PreferredTime, string? CurrentLevel);
public record UpdateLeadStatusRequest(LeadStatus Status, string? Reason);
public record AddLeadNoteRequest(string Note);

public record LogCallRequest(CallOutcome Outcome, string? Notes);
public record CrmCallDto(Guid Id, Guid LeadId, CallOutcome Outcome, string? Notes, DateTimeOffset CalledAt);

public record BookTrialRequest(Guid GroupId, DateTimeOffset ScheduledAt);
public record TrialLessonDto(Guid Id, Guid LeadId, Guid GroupId, DateTimeOffset ScheduledAt, TrialAttendanceStatus AttendanceStatus);

public record ConvertLeadRequest(Guid GroupId, string Email, string Password, string? CurrentLevel);
public record StudentDto(Guid Id, Guid UserId, string StudentNumber, DateOnly JoinedDate, string? CurrentLevel, StudentStatus Status);

public record RecommendedLessonDto(Guid Id, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string RoomName);

/// <param name="TeacherName">The group's main teacher.</param>
/// <param name="NextLessons">Its next lessons — a trial is a visit to one of these, so the operator picks a real slot.</param>
public record GroupRecommendationDto(Guid GroupId, string GroupName, Guid CourseId, string CourseName,
    int FreeSeats, int Capacity, int MatchPercent, List<string> Reasons,
    string? TeacherName = null, List<RecommendedLessonDto>? NextLessons = null);

public record PlacementQuestionDto(int Id, string Text, string[] Options);
public record SubmitPlacementAnswer(int QuestionId, int SelectedIndex);
public record SubmitPlacementTestRequest(List<SubmitPlacementAnswer> Answers);
public record PlacementAttemptDto(Guid Id, Guid LeadId, int CorrectCount, int TotalQuestions, string RecommendedLevel, DateTimeOffset CompletedAt);

[ApiController]
[Route("api/leads")]
public class LeadsController(AppDbContext db, BillingRules billing, ILogger<LeadsController> logger) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "leads.view")]
    public async Task<ActionResult<Page<LeadDto>>> List(
        [FromQuery] LeadStatus? status, [FromQuery] LeadStatus[]? statuses, [FromQuery] string? source, [FromQuery] string? search,
        [FromQuery] PageRequest? paging, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();

        var allowed = await AllowedBranchIdsAsync(scope);
        if (allowed is null) return Forbid();

        var query = db.Leads.Where(l => l.OrganizationId == scope.OrganizationId && allowed.Contains(l.BranchId));

        if (status is not null) query = query.Where(l => l.Status == status);
        // The board asks for one column at a time; a column can hold several statuses (Yopilgan = trial missed, not interested, lost).
        if (statuses is { Length: > 0 }) query = query.WhereIn(l => l.Status, statuses);
        if (source is not null) query = query.Where(l => l.Source == source);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = PagingExtensions.ContainsPattern(search);
            query = query.Where(l => EF.Functions.ILike(l.FullName, term) || EF.Functions.ILike(l.Phone, term));
        }

        var page = await query
            .OrderByDescending(l => l.CreatedAt).ThenBy(l => l.Id)
            .Select(l => new LeadDto(l.Id, l.BranchId, l.FullName, l.Phone, l.Source, l.InterestedCourse,
                l.OperatorId, l.Status, l.Notes, l.LostReason, l.ConvertedStudentId, l.CreatedAt, l.Priority, null,
                l.PreferredTime, l.CurrentLevel))
            .ToPageAsync(paging, ct);

        var trials = await LatestTrialsAsync(page.Items.Select(l => l.Id).ToList());
        return Ok(new Page<LeadDto>(
            page.Items.Select(l => l with { LatestTrial = trials.GetValueOrDefault(l.Id) }).ToList(), page.Total, page.PageNumber, page.PageSize));
    }

    /// <summary>
    /// docs/00_TZ.md §11 — "Super Admin sees marketing conversion by source": the leads that came in during the period, followed
    /// Lidlar → Bog'lanilgan → Trial band → Trial keldi → Student bo'ldi → To'lov qildi, overall and per source. The branch dashboard's CRM
    /// block counts with the same <see cref="LeadFunnelQueries"/>, so the two screens can't disagree. The period is the leads' creation
    /// date (a cohort), at most a year; without one it is the last 90 days.
    /// </summary>
    [HttpGet("funnel")]
    [Authorize(Policy = "leads.view")]
    public async Task<ActionResult<FunnelDto>> Funnel(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? branchId, [FromQuery] string? source, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();

        var end = to ?? DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        var start = from ?? end.AddDays(-89);
        if (end < start) return BadRequest(new { error = "Tugash sanasi boshlanish sanasidan oldin bo'lmasin." });
        if (end.DayNumber - start.DayNumber > 366) return BadRequest(new { error = "Hisobot davri bir yildan oshmasligi kerak." });

        var orgBranches = await db.Branches.Where(b => b.OrganizationId == scope.OrganizationId)
            .Select(b => new { b.Id, b.Timezone }).ToListAsync(ct);
        var allowed = scope.AllowedBranchIds(orgBranches.Select(b => b.Id).ToList());
        if (allowed is null) return Forbid();
        if (branchId is not null && !allowed.Contains(branchId.Value)) return Forbid();

        var branches = orgBranches
            .Where(b => allowed.Contains(b.Id) && (branchId is null || b.Id == branchId))
            .Select(b => (b.Id, (string?)b.Timezone))
            .ToList();

        return Ok(await LeadFunnelQueries.FunnelAsync(db, branches, start, end, source, ct));
    }

    private async Task<Dictionary<Guid, LeadTrialDto>> LatestTrialsAsync(IReadOnlyCollection<Guid> leadIds)
    {
        if (leadIds.Count == 0) return [];

        var rows = await (
            from t in db.TrialLessons
            join g in db.Groups on t.GroupId equals g.Id
            where leadIds.Contains(t.LeadId)
            select new { t.LeadId, Trial = new LeadTrialDto(t.Id, t.GroupId, g.Name, t.ScheduledAt, t.AttendanceStatus) }
        ).ToListAsync();

        return rows.GroupBy(r => r.LeadId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Trial.ScheduledAt).First().Trial);
    }

    /// <summary>Lead 360° profile (docs/kamchiliklar.docx §2): the single-lead counterpart to <see cref="List"/>,
    /// so the detail page doesn't have to keep the whole list cached to find one row.</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = "leads.view")]
    public async Task<ActionResult<LeadDto>> GetById(Guid id)
    {
        var scope = HttpContext.RequireResolvedScope();
        var (lead, error) = await LoadLeadInScopeAsync(scope, id);
        if (error is not null) return error;

        var trials = await LatestTrialsAsync([id]);
        return Ok(ToDto(lead!) with { LatestTrial = trials.GetValueOrDefault(id) });
    }

    /// <summary>Call history for the Lead 360° activity timeline — the outcomes themselves were always
    /// recorded (<see cref="LogCall"/>), there was just no endpoint to read them back per lead.</summary>
    [HttpGet("{id:guid}/calls")]
    [Authorize(Policy = "leads.view")]
    public async Task<ActionResult<List<CrmCallDto>>> ListCalls(Guid id)
    {
        var scope = HttpContext.RequireResolvedScope();
        var (lead, error) = await LoadLeadInScopeAsync(scope, id);
        if (error is not null) return error;

        var calls = await db.CrmCalls
            .Where(c => c.LeadId == lead!.Id)
            .OrderByDescending(c => c.CalledAt)
            .Select(c => new CrmCallDto(c.Id, c.LeadId, c.Outcome, c.Notes, c.CalledAt))
            .ToListAsync();

        return Ok(calls);
    }

    /// <summary>Appends a free-text note (<see cref="Lead.AppendNote"/> existed but nothing ever called it —
    /// the operator had no way to actually write one down).</summary>
    [HttpPost("{id:guid}/notes")]
    [Authorize(Policy = "leads.edit")]
    public async Task<IActionResult> AddNote(Guid id, AddLeadNoteRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Note))
            return BadRequest(new { error = "Izoh bo'sh bo'lmasin." });

        var scope = HttpContext.RequireResolvedScope();
        var (lead, error) = await LoadLeadInScopeAsync(scope, id);
        if (error is not null) return error;

        lead!.AppendNote(request.Note.Trim());
        await db.SaveChangesAsync();

        return Ok(ToDto(lead));
    }

    [HttpPost]
    [Authorize(Policy = "leads.create")]
    public async Task<ActionResult<LeadDto>> Create(CreateLeadRequest request)
    {
        var scope = HttpContext.RequireResolvedScope();

        if (string.IsNullOrWhiteSpace(request.FullName) || string.IsNullOrWhiteSpace(request.Phone))
            return BadRequest(new { error = "Ism va telefon raqami majburiy." });

        var allowed = await AllowedBranchIdsAsync(scope);
        if (allowed is null || !allowed.Contains(request.BranchId))
            return Forbid();

        var preferences = ParsePreferences(request.PreferredTime, request.CurrentLevel);
        if (preferences.Error is not null) return BadRequest(new { error = preferences.Error });

        var lead = new Lead(scope.OrganizationId, request.BranchId, request.FullName.Trim(), request.Phone.Trim(),
            request.Source, request.InterestedCourse, operatorId: scope.UserId);
        if (preferences.PreferredTime is not null || preferences.CurrentLevel is not null)
            lead.UpdatePreferences(preferences.PreferredTime, preferences.CurrentLevel);

        db.Leads.Add(lead);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(List), ToDto(lead));
    }

    /// <summary>Records when the person can attend and what level they are at — the two facts the group recommendation
    /// (<see cref="GroupRecommendations"/>) weighs besides course and branch. Leaves <c>PATCH {id}/status</c> alone.</summary>
    [HttpPatch("{id:guid}")]
    [Authorize(Policy = "leads.edit")]
    public async Task<ActionResult<LeadDto>> UpdatePreferences(Guid id, UpdateLeadPreferencesRequest request)
    {
        var scope = HttpContext.RequireResolvedScope();
        var (lead, error) = await LoadLeadInScopeAsync(scope, id);
        if (error is not null) return error;

        if (lead!.Status == LeadStatus.Converted)
            return Conflict(new { error = "Konvertatsiya qilingan lidni o'zgartirib bo'lmaydi." });

        var preferences = ParsePreferences(request.PreferredTime, request.CurrentLevel);
        if (preferences.Error is not null) return BadRequest(new { error = preferences.Error });

        lead.UpdatePreferences(
            request.PreferredTime is null ? lead.PreferredTime : preferences.PreferredTime,
            request.CurrentLevel is null ? lead.CurrentLevel : preferences.CurrentLevel);
        await db.SaveChangesAsync();

        return Ok(ToDto(lead));
    }

    private static (string? PreferredTime, string? CurrentLevel, string? Error) ParsePreferences(string? preferredTime, string? currentLevel)
    {
        if (!PreferredTimeCodes.TryNormalize(preferredTime, out var time))
            return (null, null, "Qulay vaqt noto'g'ri: Morning, Afternoon, Evening yoki Weekend bo'lishi kerak.");

        var level = string.IsNullOrWhiteSpace(currentLevel) ? null : currentLevel.Trim();
        if (level is { Length: > 50 })
            return (null, null, "Daraja 50 belgidan oshmasin.");

        return (time, level, null);
    }

    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = "leads.edit")]
    public async Task<IActionResult> UpdateStatus(Guid id, UpdateLeadStatusRequest request)
    {
        var scope = HttpContext.RequireResolvedScope();
        var (lead, error) = await LoadLeadInScopeAsync(scope, id);
        if (error is not null) return error;

        if (lead!.Status == LeadStatus.Converted)
            return Conflict(new { error = "Konvertatsiya qilingan lidning holatini o'zgartirib bo'lmaydi." });
        if (request.Status == LeadStatus.Converted)
            return BadRequest(new { error = "Converted holati faqat /convert orqali o'rnatiladi." });

        if (request.Status == LeadStatus.Lost)
            lead.MarkLost(request.Reason ?? "Sabab ko'rsatilmagan");
        else
            lead.SetStatus(request.Status);

        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Logs a call outcome (docs/00_TZ.md §3.3). A CallLater outcome automatically opens a
    /// follow-up CrmTask due the next day — the operator's queue fills itself.</summary>
    [HttpPost("{id:guid}/calls")]
    [Authorize(Policy = "crm.call.log")]
    public async Task<ActionResult<CrmCallDto>> LogCall(Guid id, LogCallRequest request)
    {
        var scope = HttpContext.RequireResolvedScope();
        var (lead, error) = await LoadLeadInScopeAsync(scope, id);
        if (error is not null) return error;

        if (lead!.Status == LeadStatus.Converted)
            return Conflict(new { error = "Bu lid allaqachon studentga aylantirilgan." });

        var call = new CrmCall(scope.OrganizationId, id, scope.UserId, request.Outcome, request.Notes);
        db.CrmCalls.Add(call);

        switch (request.Outcome)
        {
            case CallOutcome.CallLater:
                db.CrmTasks.Add(new CrmTask(scope.OrganizationId, leadId: id, studentId: null,
                    assignedTo: scope.UserId, CrmTaskType.CallBack, DateTimeOffset.UtcNow.AddDays(1)));
                lead.SetStatus(LeadStatus.Contacted);
                break;
            case CallOutcome.TrialBooked:
                lead.SetStatus(LeadStatus.TrialBooked);
                break;
            case CallOutcome.NotInterested or CallOutcome.WrongNumber or CallOutcome.Lost:
                lead.MarkLost(request.Outcome.ToString());
                break;
            case CallOutcome.Answered or CallOutcome.Interested or CallOutcome.PaymentPromised or CallOutcome.Transferred:
                lead.SetStatus(LeadStatus.Contacted);
                break;
            case CallOutcome.NoAnswer:
                // status unchanged — a missed call isn't a contact
                break;
        }

        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(List), new CrmCallDto(call.Id, call.LeadId, call.Outcome, call.Notes, call.CalledAt));
    }

    /// <summary>
    /// docs/00_TZ.md §12 Group Recommendation Engine: ranks this lead's branch-visible open groups so the
    /// operator doesn't pick one by eye. Six factors (<see cref="GroupRecommendationScorer"/>): course match against
    /// <see cref="Lead.InterestedCourse"/> 35%, level fit 20%, same branch 15%, preferred time 15%, free seats 10%, group already
    /// running 5%. A factor with nothing to compare (no level or time given, no lessons yet) is left out and the rest rescaled, so an
    /// unknown never becomes a fake score. There is no weekly pattern on a group yet, so "preferred time" is judged on its real
    /// upcoming lessons. Groups with no free seat are dropped entirely; they aren't a choice.
    /// </summary>
    [HttpGet("{id:guid}/group-recommendations")]
    [Authorize(Policy = "leads.view")]
    public async Task<ActionResult<List<GroupRecommendationDto>>> GroupRecommendations(Guid id)
    {
        var scope = HttpContext.RequireResolvedScope();
        var (lead, error) = await LoadLeadInScopeAsync(scope, id);
        if (error is not null) return error;

        var allowed = await AllowedBranchIdsAsync(scope);
        if (allowed is null) return Forbid();

        var candidates = await (
            from g in db.Groups
            join c in db.Courses on g.CourseId equals c.Id
            where g.OrganizationId == scope.OrganizationId && allowed.Contains(g.BranchId)
                && g.Status != GroupStatus.Completed && g.Status != GroupStatus.Cancelled
            select new
            {
                g.Id,
                g.Name,
                g.BranchId,
                g.Capacity,
                g.Status,
                CourseId = c.Id,
                CourseName = c.Name,
                CourseLevel = c.LevelCategory,
                ActiveEnrollments = db.Enrollments.Count(e => e.GroupId == g.Id && e.Status == EnrollmentStatus.Active),
            }).ToListAsync();

        var open = candidates
            .Select(g => new { g, FreeSeats = g.Capacity - g.ActiveEnrollments })
            .Where(x => x.FreeSeats > 0)
            .ToList();

        // When each open group meets next: shown to the operator as real slots to book a trial into, and the "preferred time" factor is
        // judged on them too. Only lessons still to come, on each branch's own clock.
        var openIds = open.Select(x => x.g.Id).ToList();
        var now = DateTimeOffset.UtcNow;
        var upcoming = (await db.Lessons.AsNoTracking().Held()
                .Where(l => openIds.Contains(l.GroupId) && l.StartsAt >= now)
                .OrderBy(l => l.StartsAt)
                .Select(l => new { l.GroupId, Lesson = new RecommendedLessonDto(l.Id, l.StartsAt, l.EndsAt, l.RoomName) })
                .ToListAsync())
            .GroupBy(x => x.GroupId)
            .ToDictionary(g => g.Key, g => g.Take(4).Select(x => x.Lesson).ToList());

        var timezones = await db.Branches
            .Where(b => b.OrganizationId == scope.OrganizationId)
            .ToDictionaryAsync(b => b.Id, b => b.Timezone);

        // A group that has a weekly template is judged on it — "Mon/Wed/Fri 18:00" says when it meets better than whichever four lessons
        // happen to come next (a holiday week, a moved lesson). Without one, the upcoming lessons stand in for it, as before.
        var templates = (await db.GroupScheduleSlots.AsNoTracking().Where(t => openIds.Contains(t.GroupId)).ToListAsync())
            .GroupBy(t => t.GroupId).ToDictionary(g => g.Key, g => g.ToList());

        var profile = new LeadProfile(lead!.BranchId, lead.InterestedCourse, lead.CurrentLevel, PreferredTimeCodes.Split(lead.PreferredTime));

        var recommendations = open
            .Select(x =>
            {
                var group = new GroupCandidate(x.g.Id, x.g.Name, x.g.BranchId, x.g.Capacity, x.FreeSeats, x.g.Status, x.g.CourseName, x.g.CourseLevel);
                var lessons = upcoming.GetValueOrDefault(x.g.Id, []);
                var localStarts = templates.TryGetValue(x.g.Id, out var template)
                    ? template.Select(TemplateStart).ToList()
                    : lessons.Select(l => BranchClock.LocalDateTime(timezones.GetValueOrDefault(x.g.BranchId), l.StartsAt)).ToList();
                var (score, reasons) = GroupRecommendationScorer.Score(group, profile, localStarts);
                return new { x.g, x.FreeSeats, score, reasons, lessons };
            })
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.g.Name)
            .Take(10)
            .ToList();

        // Who teaches it — "which group suits this person" is decided on the teacher and the slot, not on a score.
        var groupIds = recommendations.Select(x => x.g.Id).ToList();
        var teachers = await (
            from gs in db.GroupStaff
            join u in db.Users on gs.UserId equals u.Id
            where groupIds.Contains(gs.GroupId) && gs.Role == GroupStaffRole.MainTeacher
            select new { gs.GroupId, u.FullName }
        ).ToDictionaryAsync(x => x.GroupId, x => x.FullName);

        return Ok(recommendations
            .Select(x => new GroupRecommendationDto(x.g.Id, x.g.Name, x.g.CourseId, x.g.CourseName,
                x.FreeSeats, x.g.Capacity, (int)Math.Round(x.score), x.reasons, teachers.GetValueOrDefault(x.g.Id), x.lessons))
            .ToList());
    }

    /// <summary>A template slot as a local date-time. Only its weekday and hour are read (by the "preferred time" factor), so any date of that
    /// weekday will do — 2001-01-01 was a Monday.</summary>
    private static DateTime TemplateStart(GroupScheduleSlot slot) =>
        new DateTime(2001, 1, 1).AddDays(((int)slot.DayOfWeek + 6) % 7).Add(slot.StartTime.ToTimeSpan());

    /// <summary>docs/00_TZ.md §13 — books an optional trial class before enrollment.</summary>
    [HttpPost("{id:guid}/trial")]
    [Authorize(Policy = "trial.book")]
    public async Task<ActionResult<TrialLessonDto>> BookTrial(Guid id, BookTrialRequest request)
    {
        var scope = HttpContext.RequireResolvedScope();
        var (lead, error) = await LoadLeadInScopeAsync(scope, id);
        if (error is not null) return error;

        if (lead!.Status == LeadStatus.Converted)
            return Conflict(new { error = "Bu lid allaqachon studentga aylantirilgan." });

        var groupError = await EnsureGroupAdmissibleAsync(scope, request.GroupId);
        if (groupError is not null) return groupError;

        var trial = new TrialLesson(scope.OrganizationId, id, request.GroupId, request.ScheduledAt);
        db.TrialLessons.Add(trial);
        lead.SetStatus(LeadStatus.TrialBooked);

        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(List),
            new TrialLessonDto(trial.Id, trial.LeadId, trial.GroupId, trial.ScheduledAt, trial.AttendanceStatus));
    }

    /// <summary>
    /// Lead → Student conversion (docs/04_phase2_technical_plan.md §1): creates the login User (STUDENT
    /// role), the Student profile, and an Enrollment into the chosen group — all in one SaveChangesAsync
    /// call, so either the whole conversion lands or none of it does. The Lead itself is kept, marked
    /// Converted, per docs/00_TZ.md §20 ("oldingi tarix o'chirilmaydi").
    /// </summary>
    [HttpPost("{id:guid}/convert")]
    [Authorize(Policy = "students.create")]
    public async Task<ActionResult<StudentDto>> Convert(Guid id, ConvertLeadRequest request)
    {
        var scope = HttpContext.RequireResolvedScope();
        var (lead, error) = await LoadLeadInScopeAsync(scope, id);
        if (error is not null) return error;

        if (lead!.Status == LeadStatus.Converted)
            return Conflict(new { error = "Lead allaqachon konvertatsiya qilingan." });

        if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@'))
            return BadRequest(new { error = "Yaroqli email kiriting." });
        if (PasswordPolicy.Validate(request.Password) is { } passwordProblem)
            return BadRequest(new { error = passwordProblem });

        var groupError = await EnsureGroupAdmissibleAsync(scope, request.GroupId);
        if (groupError is not null) return groupError;

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == normalizedEmail))
            return Conflict(new { error = "Bu email bilan foydalanuvchi allaqachon mavjud." });

        var studentRole = await db.Roles.FirstOrDefaultAsync(r => r.Code == RoleCode.Student);
        if (studentRole is null) return Problem("STUDENT roli topilmadi — seed ishga tushirilmagan bo'lishi mumkin.");

        var user = new User(scope.OrganizationId, lead.FullName, lead.Phone, normalizedEmail, passwordHash: string.Empty);
        user.SetPasswordHash(new PasswordHasher<User>().HashPassword(user, request.Password));
        user.AssignRole(studentRole.Id);
        user.AssignBranch(lead.BranchId);
        db.Users.Add(user);

        var student = new Student(scope.OrganizationId, user.Id, await StudentNumbers.NextAsync(db),
            DateOnly.FromDateTime(DateTime.UtcNow), request.CurrentLevel);
        db.Students.Add(student);

        var enrollment = new Enrollment(scope.OrganizationId, student.Id, request.GroupId);
        db.Enrollments.Add(enrollment);

        db.StudentTimelineEvents.Add(new StudentTimelineEvent(scope.OrganizationId, student.Id,
            TimelineEventType.Enrolled, $$"""{"groupId":"{{request.GroupId}}","leadId":"{{lead.Id}}"}"""));

        lead.MarkConverted(student.Id);

        await db.SaveChangesAsync();

        // The first month is invoiced immediately (docs/05_phase3_technical_plan.md §1) rather than on the next
        // sweep. The conversion itself is already committed, so a billing hiccup must not turn it into an error —
        // the sweep issues the invoice later in that case.
        try
        {
            await billing.IssueDueInvoicesAsync(DateTimeOffset.UtcNow, enrollmentId: enrollment.Id);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "First invoice for enrollment {EnrollmentId} not issued at conversion; the sweep will retry.", enrollment.Id);
        }

        return CreatedAtAction(nameof(List),
            new StudentDto(student.Id, student.UserId, student.StudentNumber, student.JoinedDate, student.CurrentLevel, student.Status));
    }

    /// <summary>docs/00_TZ.md §13 Placement Test — the fixed question bank, without the answer key.</summary>
    [HttpGet("~/api/placement-test/questions")]
    [Authorize(Policy = "leads.view")]
    public ActionResult<List<PlacementQuestionDto>> PlacementQuestions()
    {
        HttpContext.RequireResolvedScope();
        return Ok(PlacementQuestionBank.Questions
            .Select(q => new PlacementQuestionDto(q.Id, q.Text, q.Options))
            .ToList());
    }

    /// <summary>Grades an attempt server-side against <see cref="PlacementQuestionBank"/> — the client never
    /// sees the answer key, only the resulting score and recommended level.</summary>
    [HttpPost("{id:guid}/placement-test")]
    [Authorize(Policy = "placement.test.log")]
    public async Task<ActionResult<PlacementAttemptDto>> SubmitPlacementTest(Guid id, SubmitPlacementTestRequest request)
    {
        var scope = HttpContext.RequireResolvedScope();
        var (lead, error) = await LoadLeadInScopeAsync(scope, id);
        if (error is not null) return error;

        if (request.Answers is null || request.Answers.Count == 0)
            return BadRequest(new { error = "Kamida bitta javob kerak." });

        var byId = PlacementQuestionBank.Questions.ToDictionary(q => q.Id);
        var earnedWeight = 0;
        var correctCount = 0;
        foreach (var answer in request.Answers)
        {
            if (!byId.TryGetValue(answer.QuestionId, out var question)) continue;
            if (answer.SelectedIndex == question.CorrectIndex)
            {
                earnedWeight += question.Weight;
                correctCount++;
            }
        }

        var level = PlacementQuestionBank.RecommendLevel(earnedWeight);
        var attempt = new PlacementAttempt(scope.OrganizationId, id, scope.UserId,
            correctCount, PlacementQuestionBank.Questions.Count, level);
        db.PlacementAttempts.Add(attempt);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(PlacementHistory), new { id },
            new PlacementAttemptDto(attempt.Id, attempt.LeadId, attempt.CorrectCount, attempt.TotalQuestions, attempt.RecommendedLevel, attempt.CompletedAt));
    }

    /// <summary>History for the Convert drawer's level suggestion and for the operator to see prior attempts.</summary>
    [HttpGet("{id:guid}/placement-test")]
    [Authorize(Policy = "leads.view")]
    public async Task<ActionResult<List<PlacementAttemptDto>>> PlacementHistory(Guid id)
    {
        var scope = HttpContext.RequireResolvedScope();
        var (lead, error) = await LoadLeadInScopeAsync(scope, id);
        if (error is not null) return error;

        var attempts = await db.PlacementAttempts
            .Where(a => a.LeadId == lead!.Id)
            .OrderByDescending(a => a.CompletedAt)
            .Select(a => new PlacementAttemptDto(a.Id, a.LeadId, a.CorrectCount, a.TotalQuestions, a.RecommendedLevel, a.CompletedAt))
            .ToListAsync();

        return Ok(attempts);
    }

    // ---- helpers -------------------------------------------------------------------------------

    private async Task<IReadOnlyCollection<Guid>?> AllowedBranchIdsAsync(ResolvedScope scope)
    {
        var orgBranchIds = await db.Branches
            .Where(b => b.OrganizationId == scope.OrganizationId)
            .Select(b => b.Id)
            .ToListAsync();
        return scope.AllowedBranchIds(orgBranchIds);
    }

    /// <summary>Every per-lead action must confirm the lead sits in a branch the caller may act on —
    /// an org check alone would let a branch-scoped operator edit another branch's leads by id.</summary>
    private async Task<(Lead? Lead, ActionResult? Error)> LoadLeadInScopeAsync(ResolvedScope scope, Guid leadId)
    {
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == leadId && l.OrganizationId == scope.OrganizationId);
        if (lead is null) return (null, NotFound());

        var allowed = await AllowedBranchIdsAsync(scope);
        if (allowed is null || !allowed.Contains(lead.BranchId)) return (null, Forbid());

        return (lead, null);
    }

    /// <summary>Same check <see cref="EnrollmentRules"/> uses for registering or transferring a student, so a trial
    /// booking or a conversion can no longer target a group that is already full or closed — before this, only
    /// scope was checked here, and <c>EnrollmentRules.CheckAsync</c> existed but nothing in this controller called it.</summary>
    private async Task<ActionResult?> EnsureGroupAdmissibleAsync(ResolvedScope scope, Guid groupId)
    {
        var allowed = await AllowedBranchIdsAsync(scope);
        if (allowed is null) return Forbid();

        var admission = await EnrollmentRules.CheckAsync(db, scope.OrganizationId, groupId, allowed);
        return admission.Problem switch
        {
            null => null,
            AdmissionProblem.NotFound => NotFound(new { error = admission.Message }),
            AdmissionProblem.OutOfScope => StatusCode(StatusCodes.Status403Forbidden, new { error = admission.Message }),
            _ => Conflict(new { error = admission.Message }),
        };
    }

    private static LeadDto ToDto(Lead lead) => new(lead.Id, lead.BranchId, lead.FullName, lead.Phone,
        lead.Source, lead.InterestedCourse, lead.OperatorId, lead.Status, lead.Notes, lead.LostReason,
        lead.ConvertedStudentId, lead.CreatedAt, lead.Priority, PreferredTime: lead.PreferredTime, CurrentLevel: lead.CurrentLevel);
}
