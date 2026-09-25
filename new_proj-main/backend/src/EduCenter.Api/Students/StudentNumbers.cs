using EduCenter.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Students;

public static class StudentNumbers
{
    /// <summary>STU-yyyyMM-NNNN with a uniqueness check — a plain random 4-digit suffix collides
    /// eventually and would surface as a 500 from the unique index instead of a clean retry.</summary>
    public static async Task<string> NextAsync(AppDbContext db, CancellationToken ct = default)
    {
        var prefix = $"STU-{DateTime.UtcNow:yyyyMM}-";
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var candidate = prefix + Random.Shared.Next(1000, 9999);
            if (!await db.Students.AnyAsync(s => s.StudentNumber == candidate, ct))
                return candidate;
        }
        return prefix + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    }
}
