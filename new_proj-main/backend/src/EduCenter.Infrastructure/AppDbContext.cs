using EduCenter.Modules.Academic.Domain;
using EduCenter.Modules.Ai.Domain;
using EduCenter.Modules.Audit.Domain;
using EduCenter.Modules.Automation.Domain;
using EduCenter.Modules.Chat.Domain;
using EduCenter.Modules.Crm.Domain;
using EduCenter.Modules.Finance.Domain;
using EduCenter.Modules.Notifications.Domain;
using EduCenter.Modules.Identity.Domain;
using EduCenter.Modules.Lms.Domain;
using EduCenter.Modules.Organizations.Domain;
using EduCenter.Modules.Students.Domain;
using EduCenter.Modules.Timeline.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EduCenter.Infrastructure;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    // Organizations
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Branch> Branches => Set<Branch>();

    // Identity
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserPermission> UserPermissions => Set<UserPermission>();
    public DbSet<RolePermissionOverride> RolePermissionOverrides => Set<RolePermissionOverride>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<UserBranch> UserBranches => Set<UserBranch>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<DataAccessRequest> DataAccessRequests => Set<DataAccessRequest>();
    public DbSet<TeacherProfile> TeacherProfiles => Set<TeacherProfile>();
    public DbSet<TeacherCredential> TeacherCredentials => Set<TeacherCredential>();
    public DbSet<TeacherExperience> TeacherExperiences => Set<TeacherExperience>();

    // Academic
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<GroupStaff> GroupStaff => Set<GroupStaff>();
    public DbSet<Lesson> Lessons => Set<Lesson>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<GroupScheduleSlot> GroupScheduleSlots => Set<GroupScheduleSlot>();

    // Audit
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    // CRM
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<CrmCall> CrmCalls => Set<CrmCall>();
    public DbSet<CrmTask> CrmTasks => Set<CrmTask>();
    public DbSet<TrialLesson> TrialLessons => Set<TrialLesson>();
    public DbSet<PlacementAttempt> PlacementAttempts => Set<PlacementAttempt>();
    public DbSet<Complaint> Complaints => Set<Complaint>();

    // Students
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Parent> Parents => Set<Parent>();
    public DbSet<StudentParent> StudentParents => Set<StudentParent>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<SupportCase> SupportCases => Set<SupportCase>();
    public DbSet<StudentDocument> StudentDocuments => Set<StudentDocument>();

    // LMS
    public DbSet<Attendance> Attendance => Set<Attendance>();
    public DbSet<Homework> Homework => Set<Homework>();
    public DbSet<LearningResource> LearningResources => Set<LearningResource>();
    public DbSet<HomeworkSubmission> HomeworkSubmissions => Set<HomeworkSubmission>();
    public DbSet<HomeworkTarget> HomeworkTargets => Set<HomeworkTarget>();
    public DbSet<Exam> Exams => Set<Exam>();
    public DbSet<ExamResult> ExamResults => Set<ExamResult>();

    // Finance
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Discount> Discounts => Set<Discount>();
    public DbSet<Refund> Refunds => Set<Refund>();
    public DbSet<PromoCode> PromoCodes => Set<PromoCode>();

    // Notifications
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();
    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();
    public DbSet<Announcement> Announcements => Set<Announcement>();

    // Automation
    public DbSet<AutomationRule> AutomationRules => Set<AutomationRule>();
    public DbSet<AutomationRunLog> AutomationRunLogs => Set<AutomationRunLog>();

    // Chat
    public DbSet<ChatRoom> ChatRooms => Set<ChatRoom>();
    public DbSet<ChatMember> ChatMembers => Set<ChatMember>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<ChatReaction> ChatReactions => Set<ChatReaction>();

    // AI
    public DbSet<AiRequest> AiRequests => Set<AiRequest>();
    public DbSet<AiUsage> AiUsages => Set<AiUsage>();
    public DbSet<WritingEvaluation> WritingEvaluations => Set<WritingEvaluation>();
    public DbSet<KnowledgeDocument> KnowledgeDocuments => Set<KnowledgeDocument>();
    public DbSet<KnowledgeChunk> KnowledgeChunks => Set<KnowledgeChunk>();
    public DbSet<SpeakingSession> SpeakingSessions => Set<SpeakingSession>();
    public DbSet<SpeakingTurn> SpeakingTurns => Set<SpeakingTurn>();
    public DbSet<AiConversation> AiConversations => Set<AiConversation>();
    public DbSet<AiConversationMessage> AiConversationMessages => Set<AiConversationMessage>();

    // Timeline
    public DbSet<StudentTimelineEvent> StudentTimelineEvents => Set<StudentTimelineEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // RAG embeddings live in a pgvector column (docs/00_TZ.md §31); the dev/prod image is pgvector/pgvector:pg16.
        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Each module's Infrastructure assembly also carries its own IEntityTypeConfiguration<T> classes.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(Organization).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(User).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(Course).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AuditLog).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(Lead).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(Student).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(Attendance).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StudentTimelineEvent).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(Invoice).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(Notification).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AutomationRule).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ChatRoom).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AiRequest).Assembly);

        // Npgsql only accepts UTC for "timestamp with time zone" — normalize every DateTimeOffset
        // column on write so callers can pass any local offset (e.g. Asia/Tashkent) without erroring.
        var utcConverter = new ValueConverter<DateTimeOffset, DateTimeOffset>(
            v => v.ToUniversalTime(), v => v);
        var nullableUtcConverter = new ValueConverter<DateTimeOffset?, DateTimeOffset?>(
            v => v.HasValue ? v.Value.ToUniversalTime() : v, v => v);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset))
                    property.SetValueConverter(utcConverter);
                else if (property.ClrType == typeof(DateTimeOffset?))
                    property.SetValueConverter(nullableUtcConverter);

                // Every entity in this codebase self-assigns its Guid Id via Guid.NewGuid() in the
                // constructor — the database never generates it. Left at EF's default convention
                // (ValueGeneratedOnAdd for Guid keys), a child entity appended to an already-tracked
                // parent's collection navigation (e.g. Group.AssignStaff adding to Group.Staff) is
                // misread as "already exists in the DB" because its key is non-default, so EF marks it
                // Unchanged-then-Modified instead of Added and emits an UPDATE that matches zero rows —
                // a DbUpdateConcurrencyException on what was actually a brand-new row. Entities added
                // straight through a DbSet.Add() call aren't affected (that call sets EntityState.Added
                // explicitly, bypassing the heuristic), which is why this went unnoticed until a
                // graph-navigation write (GroupStaff via Group.Staff) exercised it.
                if (property.Name == "Id" && property.ClrType == typeof(Guid))
                    property.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
            }
        }

        base.OnModelCreating(modelBuilder);
    }
}
