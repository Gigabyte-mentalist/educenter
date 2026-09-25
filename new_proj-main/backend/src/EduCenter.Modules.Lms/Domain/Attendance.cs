using EduCenter.SharedKernel;

namespace EduCenter.Modules.Lms.Domain;

/// <summary>docs/00_TZ.md §15.</summary>
public enum AttendanceStatus
{
    Present,
    Absent,
    Late,
    Excused,
    Online
}

public class Attendance : TenantEntity
{
    private Attendance() { } // EF Core

    public Attendance(Guid organizationId, Guid lessonId, Guid studentId, AttendanceStatus status,
        string? note = null, int? score = null)
    {
        OrganizationId = organizationId;
        LessonId = lessonId;
        StudentId = studentId;
        Status = status;
        Note = note;
        Score = score;
    }

    public Guid LessonId { get; private set; }
    public Guid StudentId { get; private set; }
    public AttendanceStatus Status { get; private set; }
    /// <summary>"Late — the bus broke down": staff-only, never shown to the student or the parent.</summary>
    public string? Note { get; private set; }
    /// <summary>The student's score for this lesson, 0..<c>Lesson.MaxLessonScore</c>; null while the lesson has no score or none was given.</summary>
    public int? Score { get; private set; }

    /// <summary>A record is one statement about a student's lesson — status, remark and score together.</summary>
    public void Update(AttendanceStatus status, string? note, int? score)
    {
        Status = status;
        Note = note;
        Score = score;
        MarkUpdated();
    }
}
