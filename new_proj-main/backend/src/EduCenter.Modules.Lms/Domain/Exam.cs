using EduCenter.SharedKernel;

namespace EduCenter.Modules.Lms.Domain;

/// <summary>docs/00_TZ.md §17.</summary>
public enum ExamType
{
    Placement,
    Unit,
    Monthly,
    MockIelts,
    Final,
    Custom
}

public class Exam : TenantEntity
{
    private Exam() { } // EF Core

    public Exam(Guid organizationId, Guid groupId, string name, ExamType type, DateOnly date)
    {
        OrganizationId = organizationId;
        GroupId = groupId;
        Name = name;
        Type = type;
        Date = date;
    }

    public Guid GroupId { get; private set; }
    public string Name { get; private set; } = default!;
    public ExamType Type { get; private set; }
    public DateOnly Date { get; private set; }

    /// <summary>The group is not among the things that change: moving an exam to another group would leave its results
    /// belonging to students who are not in it.</summary>
    public void Update(string name, ExamType type, DateOnly date)
    {
        Name = name;
        Type = type;
        Date = date;
        MarkUpdated();
    }
}

/// <summary>Section scores are nullable — a Unit exam might only grade Grammar/Vocabulary, while a
/// Mock IELTS grades all four (docs/00_TZ.md §17). The four skills and Overall are IELTS bands (0–9);
/// Grammar and Vocabulary are percentages (0–100), as the TZ shows them ("Grammar: 71%").</summary>
public class ExamResult : TenantEntity
{
    public const decimal MaxBand = 9;
    public const decimal MaxPercent = 100;

    private ExamResult() { } // EF Core

    public ExamResult(Guid organizationId, Guid examId, Guid studentId,
        decimal? listening, decimal? reading, decimal? writing, decimal? speaking, decimal? overall,
        decimal? grammar = null, decimal? vocabulary = null)
    {
        OrganizationId = organizationId;
        ExamId = examId;
        StudentId = studentId;
        Listening = listening;
        Reading = reading;
        Writing = writing;
        Speaking = speaking;
        Overall = overall;
        Grammar = grammar;
        Vocabulary = vocabulary;
    }

    public Guid ExamId { get; private set; }
    public Guid StudentId { get; private set; }
    public decimal? Listening { get; private set; }
    public decimal? Reading { get; private set; }
    public decimal? Writing { get; private set; }
    public decimal? Speaking { get; private set; }
    public decimal? Overall { get; private set; }
    /// <summary>Percent 0–100. Null on results recorded before the column existed — those exams did not measure it.</summary>
    public decimal? Grammar { get; private set; }
    /// <summary>Percent 0–100; null on older results, like <see cref="Grammar"/>.</summary>
    public decimal? Vocabulary { get; private set; }
}
