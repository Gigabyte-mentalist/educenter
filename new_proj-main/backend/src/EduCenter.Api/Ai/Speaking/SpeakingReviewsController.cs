using EduCenter.Api.Authorization;
using EduCenter.Infrastructure;
using EduCenter.Modules.Ai.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Ai.Speaking;

public record SpeakingReviewRowDto(
    Guid Id, Guid StudentId, string StudentName, SpeakingMode Mode, string ModeLabel, string Topic, int SpokenSeconds,
    decimal? EstimatedBand, decimal? TeacherBand, DateTimeOffset? CompletedAt);

/// <summary>
/// The teacher's inbox of Speaking practice (docs/21.09-audit-jurnali S04.03): every finished session, shared with staff, of the students
/// their scope covers — only the ones nobody has listened to yet, if asked. A row opens through <see cref="StudentSpeakingController"/>.
/// </summary>
[ApiController]
[Route("api/ai/speaking/reviews")]
public class SpeakingReviewsController(AppDbContext db) : ControllerBase
{
    private const int MaxRows = 100;

    /// <param name="pendingOnly">Only the sessions no teacher has corrected yet — an AI band nobody has checked against the recording.</param>
    [HttpGet]
    [Authorize(Policy = "ai.speaking.review")]
    public async Task<ActionResult<List<SpeakingReviewRowDto>>> Reviews(
        [FromQuery] bool pendingOnly = false, [FromQuery] Guid? studentId = null, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        var mine = scope.ScopedStudentIds(db);
        if (mine is null) return Forbid();

        var query = db.SpeakingSessions.Where(s => s.OrganizationId == scope.OrganizationId && s.SharedWithStaff
            && s.Status == SpeakingSessionStatus.Completed && mine.Contains(s.StudentId));
        if (studentId is not null) query = query.Where(s => s.StudentId == studentId);
        if (pendingOnly) query = query.Where(s => s.OverriddenAt == null);

        var rows = await (
            from s in query
            join st in db.Students on s.StudentId equals st.Id
            join u in db.Users on st.UserId equals u.Id
            orderby s.CompletedAt descending, s.Id
            select new { Session = s, u.FullName }
        ).AsNoTracking().Take(MaxRows).ToListAsync(ct);

        return Ok(rows.Select(r => new SpeakingReviewRowDto(r.Session.Id, r.Session.StudentId, r.FullName, r.Session.Mode,
            SpeakingScript.ModeLabel(r.Session.Mode), r.Session.Topic, r.Session.SpokenSeconds, r.Session.EstimatedBand,
            r.Session.TeacherBand, r.Session.CompletedAt)).ToList());
    }
}
