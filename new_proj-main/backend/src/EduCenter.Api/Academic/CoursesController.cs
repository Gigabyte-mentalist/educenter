using EduCenter.Api.Authorization;
using EduCenter.Infrastructure;
using EduCenter.Modules.Academic.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Academic;

public record CourseDto(Guid Id, string Name, string LevelCategory, decimal MonthlyFee);
public record CreateCourseRequest(string Name, string LevelCategory, decimal? MonthlyFee);
public record UpdateCourseRequest(string Name, string LevelCategory, decimal MonthlyFee);

[ApiController]
[Route("api/courses")]
public class CoursesController(AppDbContext db) : ControllerBase
{
    private const decimal MaxMonthlyFee = 100_000_000m;

    [HttpGet]
    [Authorize(Policy = "groups.view")] // course catalog visibility follows group visibility in Phase 1
    public async Task<ActionResult<List<CourseDto>>> List()
    {
        var scope = HttpContext.RequireResolvedScope();

        var courses = await db.Courses
            .Where(c => c.OrganizationId == scope.OrganizationId)
            .OrderBy(c => c.Name)
            .Select(c => new CourseDto(c.Id, c.Name, c.LevelCategory, c.MonthlyFee))
            .ToListAsync();

        return Ok(courses);
    }

    [HttpPost]
    [Authorize(Policy = "courses.manage")]
    public async Task<ActionResult<CourseDto>> Create(CreateCourseRequest request)
    {
        var scope = HttpContext.RequireResolvedScope();

        var fee = request.MonthlyFee ?? 0;
        var error = Validate(request.Name, request.LevelCategory, fee);
        if (error is not null) return error;

        var course = new Course(scope.OrganizationId, request.Name.Trim(), request.LevelCategory.Trim(), fee);
        db.Courses.Add(course);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(List), new CourseDto(course.Id, course.Name, course.LevelCategory, course.MonthlyFee));
    }

    [HttpPatch("{id:guid}")]
    [Authorize(Policy = "courses.manage")]
    public async Task<ActionResult<CourseDto>> Update(Guid id, UpdateCourseRequest request)
    {
        var scope = HttpContext.RequireResolvedScope();

        var course = await db.Courses.FirstOrDefaultAsync(c => c.Id == id && c.OrganizationId == scope.OrganizationId);
        if (course is null) return NotFound();

        var error = Validate(request.Name, request.LevelCategory, request.MonthlyFee);
        if (error is not null) return error;

        course.Update(request.Name.Trim(), request.LevelCategory.Trim(), request.MonthlyFee);
        await db.SaveChangesAsync();

        return Ok(new CourseDto(course.Id, course.Name, course.LevelCategory, course.MonthlyFee));
    }

    /// <summary>Erases a course no group is on. A group keeps its course id whatever its status — closed groups included, they are
    /// history — so a course with any group stays and the refusal says how many. Knowledge-base documents scoped to the course
    /// hold its id too and would be left pointing at nothing, so they stop the delete as well.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "courses.manage")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        var course = await db.Courses.FirstOrDefaultAsync(c => c.Id == id && c.OrganizationId == scope.OrganizationId, ct);
        if (course is null) return NotFound();

        var groups = await db.Groups.CountAsync(g => g.CourseId == id, ct);
        var blockers = new List<string>();
        if (groups > 0) blockers.Add($"{groups} ta guruh");
        if (await db.KnowledgeDocuments.AnyAsync(d => d.CourseId == id, ct)) blockers.Add("bilim bazasi hujjatlari");
        if (blockers.Count > 0)
            return Conflict(new
            {
                error = $"Bu kursni o'chirib bo'lmaydi — unga bog'liq ma'lumotlar bor: {string.Join(", ", blockers)}.",
                blockers,
                groupCount = groups,
            });

        db.Courses.Remove(course);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private BadRequestObjectResult? Validate(string? name, string? levelCategory, decimal monthlyFee)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(levelCategory))
            return BadRequest(new { error = "Kurs nomi va darajasi ko'rsatilishi shart." });
        if (monthlyFee < 0 || monthlyFee > MaxMonthlyFee)
            return BadRequest(new { error = "Oylik to'lov 0 dan 100 000 000 so'mgacha bo'lishi kerak." });
        return null;
    }
}
