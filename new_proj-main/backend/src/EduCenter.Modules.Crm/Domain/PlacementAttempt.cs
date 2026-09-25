using EduCenter.SharedKernel;

namespace EduCenter.Modules.Crm.Domain;

/// <summary>docs/00_TZ.md §13 — a completed placement test for a Lead, before enrollment. One Lead can
/// have several attempts (the test is optional and repeatable); the latest one is what the Convert flow
/// suggests as the new student's starting level.</summary>
public class PlacementAttempt : TenantEntity
{
    private PlacementAttempt() { } // EF Core

    public PlacementAttempt(Guid organizationId, Guid leadId, Guid administeredBy,
        int correctCount, int totalQuestions, string recommendedLevel)
    {
        OrganizationId = organizationId;
        LeadId = leadId;
        AdministeredBy = administeredBy;
        CorrectCount = correctCount;
        TotalQuestions = totalQuestions;
        RecommendedLevel = recommendedLevel;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public Guid LeadId { get; private set; }
    public Guid AdministeredBy { get; private set; }
    public int CorrectCount { get; private set; }
    public int TotalQuestions { get; private set; }
    public string RecommendedLevel { get; private set; } = default!;
    public DateTimeOffset CompletedAt { get; private set; }
}
