using EduCenter.Modules.Students.Domain;

namespace EduCenter.Api.Students;

/// <summary>
/// What a student's overall status should read given their enrollments — one rule instead of every lifecycle
/// action (transfer, freeze, resume, drop, graduate, enroll) each deciding it their own way. <see cref="BillingRules"/>
/// still owns the Active↔Debt flip on its own sweep; this only decides between "has an open enrollment" and the
/// three end states, and defers to the caller's current status rather than fighting billing over Debt.
/// </summary>
public static class StudentStatusRules
{
    public static StudentStatus Recompute(Student student, IReadOnlyCollection<Enrollment> enrollments)
    {
        if (enrollments.Any(e => e.Status == EnrollmentStatus.Active))
            return student.Status == StudentStatus.Debt ? StudentStatus.Debt : StudentStatus.Active;

        if (enrollments.Any(e => e.Status == EnrollmentStatus.Frozen))
            return StudentStatus.Frozen;

        var last = enrollments.Where(e => e.EndedAt is not null).OrderByDescending(e => e.EndedAt).FirstOrDefault();
        return last?.Status switch
        {
            EnrollmentStatus.Completed => StudentStatus.Graduated,
            EnrollmentStatus.Dropped => StudentStatus.Dropped,
            _ => student.Status,
        };
    }
}
