using EduCenter.Api.Crm;
using EduCenter.Modules.Students.Domain;

namespace EduCenter.Api.Dashboard;

/// <summary>One scheduled occurrence as a dashboard shows it — the group and teacher resolved to names, because
/// nobody reading "bugungi darslar" wants a Guid.</summary>
public record DashboardLessonDto(
    Guid Id, Guid GroupId, string GroupName, string BranchName, string RoomName,
    DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? Topic,
    Guid TeacherId, string TeacherName);

public record PendingHomeworkDto(
    Guid Id, Guid LessonId, string Title, string GroupName, DateTimeOffset Deadline, bool Overdue);

/// <summary>An AI result recent enough to be worth surfacing. <paramref name="Band"/> is null while the job is
/// still running or when it failed — <paramref name="Status"/> says which.</summary>
public record AiResultDto(string Kind, string Title, decimal? Band, string Status, DateTimeOffset At, string Link);

public record MoneyDueDto(decimal Outstanding, DateOnly? NextDueDate, bool Overdue);

// ── Organization (Super Admin / Administrator) ───────────────────────────────────────────────────────

public record BranchKpiDto(
    Guid BranchId, string BranchName,
    int Students, int ActiveGroups, int LessonsToday,
    decimal Collected, decimal Outstanding, int Debtors,
    double AttendanceRate,
    int NewLeads, int ConvertedLeads, double ConversionRate);

public record OrganizationDashboardDto(
    DateOnly From, DateOnly To,
    int TotalStudents, int ActiveStudents, int FrozenStudents, int DebtStudents, int NewStudents,
    int ActiveGroups, int LessonsToday, int TeacherCount,
    decimal Collected, decimal ExpectedRevenue, decimal Outstanding, int Debtors,
    int NewLeads, int TrialsBooked, int TrialsAttended, int ConvertedLeads,
    double TrialConversionRate, double LeadConversionRate,
    double AttendanceRate, double RetentionRate,
    int OpenSupportCases, int PendingHomeworkReviews,
    List<BranchKpiDto> Branches,
    decimal RefundedAmount = 0, decimal DiscountAmount = 0,
    int? AiRequests = null, decimal? AiCostUsd = null, int? AiActiveStudents = null);

/// <summary>One branch's own dashboard (docs/21.09-audit-jurnali S05.03). <see cref="Overview"/> is the organization dashboard cut to this
/// branch — with its money left at zero, and <see cref="FinanceVisible"/> false, for a caller who may not read payments. The other blocks are
/// null for a caller without the permission behind them (Teachers: <c>dashboard.teachers.view</c>); CRM and Academic need nothing beyond the
/// dashboard itself.</summary>
public record BranchDashboardDto(
    Guid BranchId, string BranchName, string Address, string Timezone,
    DateOnly From, DateOnly To,
    OrganizationDashboardDto Overview, bool FinanceVisible,
    AcademicBlockDto Academic, TeachersBlockDto? Teachers, LeadFunnelDto Crm);

// ── Teacher / Support teacher ────────────────────────────────────────────────────────────────────────

public record RiskStudentDto(Guid StudentId, string FullName, string GroupName, string Reason, int Severity);

public record TeacherDashboardDto(
    List<DashboardLessonDto> Today, List<DashboardLessonDto> Tomorrow,
    int MyGroups, int MyStudents,
    int PendingHomeworkReviews, int PendingWritingReviews, int UnmarkedLessons,
    double AttendanceRate,
    List<RiskStudentDto> Risks,
    int UnreadNotifications, int PendingSpeakingReviews = 0);

// ── Student ──────────────────────────────────────────────────────────────────────────────────────────

public record StudentDashboardDto(
    Guid StudentId, string FullName, string? CurrentLevel, StudentStatus Status,
    List<DashboardLessonDto> Today, List<DashboardLessonDto> Upcoming,
    List<PendingHomeworkDto> PendingHomework,
    double AttendanceRate, int LessonsAttended, int LessonsHeld,
    MoneyDueDto Money,
    List<AiResultDto> RecentAi,
    decimal? LastExamOverall, string? LastExamName, DateOnly? LastExamDate);

// ── Parent ───────────────────────────────────────────────────────────────────────────────────────────

public record ParentChildDto(
    Guid StudentId, string FullName, string? CurrentLevel, StudentStatus Status,
    List<DashboardLessonDto> Today, DashboardLessonDto? NextLesson,
    double AttendanceRate, int LessonsAttended, int LessonsHeld,
    int PendingHomework,
    MoneyDueDto Money,
    decimal? LastExamOverall, string? LastExamName, DateOnly? LastExamDate);

public record ParentDashboardDto(List<ParentChildDto> Children);

// ── Developer ────────────────────────────────────────────────────────────────────────────────────────

public record TableCountDto(string Name, long Rows);

public record AiProviderHealthDto(string Provider, int Requests, int Failures, double ErrorRate, int P95LatencyMs, decimal CostUsd);

public record AutomationRunDto(string RuleCode, string Trigger, int ItemCount, string Summary, DateTimeOffset RanAt);

public record SystemDashboardDto(
    bool DatabaseReachable, int DatabaseLatencyMs, string? DatabaseVersion, long? DatabaseSizeBytes, string? DatabaseError,
    string ApiVersion, string Environment, TimeSpan Uptime,
    int AiRequests24h, int AiFailures24h, double AiErrorRate24h, decimal AiCost24h, int AiP95LatencyMs,
    List<AiProviderHealthDto> Providers,
    int QueuedWritingJobs, int ProcessingWritingJobs, int FailedWritingJobs24h,
    List<AutomationRunDto> RecentAutomationRuns,
    int AuditEvents24h, int ActiveSessions,
    List<TableCountDto> Tables);
