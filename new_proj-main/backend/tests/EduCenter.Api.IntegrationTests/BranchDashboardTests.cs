using System.Net;
using System.Net.Http.Json;
using EduCenter.Api.Dashboard;
using EduCenter.Infrastructure;
using EduCenter.Infrastructure.Seed;
using EduCenter.Modules.Academic.Domain;
using EduCenter.Modules.Crm.Domain;
using EduCenter.Modules.Finance.Domain;
using EduCenter.Modules.Lms.Domain;
using EduCenter.Modules.Students.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// A branch's own dashboard (docs/21.09-audit-jurnali S05.03): the learning results, the teachers' league table and the lead funnel, with
/// exact numbers. The figures are checked in a branch made for the purpose — nobody else's lessons, leads or payments are in it — and
/// its rows are put in directly: the API refuses to schedule a lesson in the past, and stamps every answer with the clock.
/// </summary>
[Collection("Api")]
public class BranchDashboardTests(EduCenterWebApplicationFactory factory)
{
    private readonly LmsWorld lms = new(factory);

    // ---- the figures ---------------------------------------------------------------------------

    [Fact]
    public async Task Academic_AveragesTheGradedResults_AndCountsOnlyLessonsThatWereHeld()
    {
        var w = await BuildAsync();

        var academic = (await DashboardAsync(w.SuperAdmin, w)).Academic;

        // Results: s1 (6.0/7.0/5.5/6.5, overall 6.0, grammar 70, vocabulary 80), s2 (listening 7.0, overall 7.0, grammar 90), s3 (overall 8.0, vocabulary 60).
        Assert.Equal(new decimal?[] { 6.5m, 7.0m, 5.5m, 6.5m }, new[] { academic.Listening, academic.Reading, academic.Writing, academic.Speaking });
        Assert.Equal(new decimal?[] { 7.0m, 80.0m, 70.0m }, new[] { academic.Overall, academic.Grammar, academic.Vocabulary });
        Assert.Equal(3, academic.Results);
        // Two lessons held (10 marks: 2 of 3 counted marks came), one cancelled — its two absences are not counted.
        Assert.Equal((2, 1), (academic.LessonsHeld, academic.LessonsCancelled));
        Assert.Equal(0.6667, academic.AttendanceRate);
        // The trend is the overall band by month: both exams may fall in one month or two, whichever the calendar makes of "3 and 1 days ago".
        var expected = new[] { (w.ExamDay1, 6.5m, 2), (w.ExamDay2, 8.0m, 1) }.Select(e => (Month: $"{e.Item1:yyyy-MM}", e.Item2, e.Item3));
        Assert.Equal(
            expected.GroupBy(e => e.Month).OrderBy(g => g.Key).Select(g => (g.Key, Math.Round(g.Sum(e => e.Item2 * e.Item3) / g.Sum(e => e.Item3), 2), g.Sum(e => e.Item3))).ToArray(),
            academic.Trend.Select(t => (t.Month, t.Average, t.Results)).ToArray());
    }

    [Fact]
    public async Task Teachers_AreRankedByTheirGroupsAttendance_WithMarkingTimeRetentionAndCases()
    {
        var w = await BuildAsync();

        var teachers = (await DashboardAsync(w.SuperAdmin, w)).Teachers!.Teachers;

        Assert.Equal(new[] { w.T2, w.T1 }, teachers.Select(t => t.TeacherId).ToArray());
        var t2 = teachers[0];
        Assert.Equal((1, 1, 1, 1.0), (t2.Groups, t2.LessonsHeld, t2.Counted, t2.AttendanceRate));
        Assert.Equal((int?)1, t2.Rank);
        Assert.Null(t2.AverageReviewHours);
        Assert.Equal((0, 1, 1, 0), (t2.HomeworkPendingReview, t2.RetentionBase, t2.Retained, t2.OpenCases));
        Assert.Equal(1.0, t2.RetentionRate);

        var t1 = teachers[1];
        Assert.Equal((1, 1, 2, 0.5), (t1.Groups, t1.LessonsHeld, t1.Counted, t1.AttendanceRate));
        Assert.Equal((int?)2, t1.Rank);
        Assert.Equal(4.0, t1.AverageReviewHours);
        Assert.Equal(1, t1.HomeworkPendingReview);
        // s2 left after the period began, s1 stayed: one of two.
        Assert.Equal((2, 1), (t1.RetentionBase, t1.Retained));
        Assert.Equal(0.5, t1.RetentionRate);
        Assert.Equal((1, 0), (t1.OpenCases, t1.ClosedCases));
    }

    [Fact]
    public async Task Crm_IsTheFunnelOfTheLeadsThatCameIn_ByStageAndBySource()
    {
        var w = await BuildAsync();

        var crm = (await DashboardAsync(w.SuperAdmin, w)).Crm;

        Assert.Equal((6, 5, 3, 2, 1, 2), (crm.Leads, crm.Contacted, crm.TrialBooked, crm.TrialAttended, crm.Converted, crm.Lost));
        Assert.Equal((1, 1), (crm.TrialToStudent, crm.TrialToPayment));
        Assert.Equal((0.1667, 0.5, 0.5), (crm.LeadConversionRate, crm.TrialToStudentRate, crm.TrialToPaymentRate));
        Assert.Equal(
            new[] { ("Instagram", 3, 3, 2, 1, 1), ("Website", 2, 1, 0, 0, 0), ("Referral", 1, 1, 0, 0, 1) },
            crm.Sources.Select(s => (s.Source, s.Leads, s.Contacted, s.TrialAttended, s.Converted, s.Lost)).ToArray());
    }

    [Fact]
    public async Task Overview_IsTheOrganizationDashboardOfThatBranch_AndCarriesItsMoney()
    {
        var w = await BuildAsync();

        var branch = await DashboardAsync(w.SuperAdmin, w);
        var organization = (await w.SuperAdmin.GetFromJsonAsync<Overview>($"/api/dashboard/organization?branchId={w.BranchId}&from={w.From:yyyy-MM-dd}&to={w.To:yyyy-MM-dd}"))!;

        Assert.True(branch.FinanceVisible);
        Assert.Equal(200000m, branch.Overview.Collected); // the one payment in this branch
        Assert.Equal(3, branch.Overview.TotalStudents);
        Assert.Equal(
            (organization.TotalStudents, organization.Collected, organization.ExpectedRevenue, organization.Outstanding, organization.Debtors, organization.NewLeads, organization.AttendanceRate),
            (branch.Overview.TotalStudents, branch.Overview.Collected, branch.Overview.ExpectedRevenue, branch.Overview.Outstanding, branch.Overview.Debtors, branch.Overview.NewLeads, branch.Overview.AttendanceRate));
        Assert.Equal((w.BranchName, "Asia/Tashkent"), (branch.BranchName, branch.Timezone));
    }

    // ---- who sees what -------------------------------------------------------------------------

    [Fact]
    public async Task AnAdministrator_SeesTheirOwnBranch_NotAnotherOne_AndTeachersAndStudentsSeeNoBranchDashboard()
    {
        var w = await BuildAsync();
        var admin = await lms.ClientAsync("admin@demo-center.test");
        var ownBranch = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/dashboard/branch/{ownBranch}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync($"/api/dashboard/branch/{w.BranchId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/dashboard/branch/{Guid.NewGuid()}")).StatusCode);
        foreach (var email in new[] { "teacher@demo-center.test", "student@demo-center.test", "parent@demo-center.test" })
            Assert.Equal(HttpStatusCode.Forbidden, (await (await lms.ClientAsync(email)).GetAsync($"/api/dashboard/branch/{ownBranch}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync($"/api/dashboard/branch/{ownBranch}")).StatusCode);
    }

    [Fact]
    public async Task TheOperator_GetsTheBranchWithoutItsMoneyOrItsTeachers_TheAdministratorGetsBoth()
    {
        var admin = await lms.ClientAsync("admin@demo-center.test");
        var operatorClient = await lms.ClientAsync("operator@demo-center.test");
        var branch = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;

        var byAdmin = (await admin.GetFromJsonAsync<Dashboard>($"/api/dashboard/branch/{branch}"))!;
        var byOperator = (await operatorClient.GetFromJsonAsync<Dashboard>($"/api/dashboard/branch/{branch}"))!;

        Assert.True(byAdmin.FinanceVisible);
        Assert.NotNull(byAdmin.Teachers);
        Assert.False(byOperator.FinanceVisible);
        Assert.Null(byOperator.Teachers);
        Assert.Equal((0m, 0m, 0m, 0), (byOperator.Overview.Collected, byOperator.Overview.ExpectedRevenue, byOperator.Overview.Outstanding, byOperator.Overview.Debtors));
        Assert.All(byOperator.Overview.Branches, b => Assert.Equal((0m, 0m, 0), (b.Collected, b.Outstanding, b.Debtors)));
        Assert.NotNull(byOperator.Crm);      // what the operator is there for
        Assert.NotNull(byOperator.Academic);
    }

    [Fact]
    public async Task APeriodLongerThanAYear_OrBackwards_IsRefused()
    {
        var superAdmin = await lms.ClientAsync("superadmin@demo-center.test");
        var branch = (await superAdmin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;

        Assert.Equal(HttpStatusCode.BadRequest, (await superAdmin.GetAsync($"/api/dashboard/branch/{branch}?from=2024-01-01&to=2025-06-01")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await superAdmin.GetAsync($"/api/dashboard/branch/{branch}?from=2025-06-01&to=2025-05-01")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await superAdmin.GetAsync($"/api/dashboard/branch/{branch}?from=2025-01-01&to=2026-01-02")).StatusCode);
    }

    // ---- the cache -----------------------------------------------------------------------------

    [Fact]
    public async Task ADashboard_IsKeptForTheTtl_ThenBuiltAgain()
    {
        var superAdmin = await lms.ClientAsync("superadmin@demo-center.test");
        var branchId = await NewBranchAsync(superAdmin);
        var cache = factory.Services.GetRequiredService<BranchDashboardCache>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var url = $"/api/dashboard/branch/{branchId}?from={today.AddDays(-1):yyyy-MM-dd}&to={today.AddDays(1):yyyy-MM-dd}";

        cache.Ttl = TimeSpan.FromMinutes(5);
        try
        {
            await AddLeadsAsync(branchId, 1);
            Assert.Equal(1, (await superAdmin.GetFromJsonAsync<Dashboard>(url))!.Crm.Leads);

            await AddLeadsAsync(branchId, 1);
            Assert.Equal(1, (await superAdmin.GetFromJsonAsync<Dashboard>(url))!.Crm.Leads); // still the copy built a moment ago

            cache.Ttl = TimeSpan.Zero; // off: built afresh — and so is what a later cached read would keep
            Assert.Equal(2, (await superAdmin.GetFromJsonAsync<Dashboard>(url))!.Crm.Leads);
        }
        finally
        {
            cache.Ttl = TimeSpan.Zero;
        }
    }

    // ---- the world -----------------------------------------------------------------------------

    /// <summary>A branch of its own with two groups (G1: s1 and s2, teacher T1; G2: s3, teacher T2), and the events of the last days.</summary>
    private sealed record World(
        HttpClient SuperAdmin, Guid BranchId, string BranchName, Guid G1, Guid G2, Guid T1, Guid T2,
        DateOnly From, DateOnly To, DateOnly ExamDay1, DateOnly ExamDay2);

    private async Task<World> BuildAsync()
    {
        var superAdmin = await lms.ClientAsync("superadmin@demo-center.test");
        var branchName = $"ZZ Dashboard {Guid.NewGuid():N}"[..30];
        var branchId = await NewBranchAsync(superAdmin, branchName);

        var course = await superAdmin.PostAsJsonAsync("/api/courses", new { name = $"Branch Course {Guid.NewGuid():N}"[..25], levelCategory = "IELTS" });
        Assert.Equal(HttpStatusCode.Created, course.StatusCode);
        var courseId = (await course.Content.ReadFromJsonAsync<IdDto>())!.Id;

        var t1 = await NewTeacherAsync(superAdmin, branchId, "A");
        var t2 = await NewTeacherAsync(superAdmin, branchId, "B");
        var g1 = await NewGroupAsync(superAdmin, branchId, courseId, t1);
        var g2 = await NewGroupAsync(superAdmin, branchId, courseId, t2);
        var s1 = await NewStudentAsync(superAdmin, g1);
        var s2 = await NewStudentAsync(superAdmin, g1);
        var s3 = await NewStudentAsync(superAdmin, g2);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var world = new World(superAdmin, branchId, branchName, g1, g2, t1, t2, today.AddDays(-5), today.AddDays(1), today.AddDays(-3), today.AddDays(-1));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var organizationId = await db.Groups.Where(g => g.Id == g1).Select(g => g.OrganizationId).SingleAsync();
        var operatorId = await db.Users.Where(u => u.Email == "operator@demo-center.test").Select(u => u.Id).SingleAsync();
        var now = DateTimeOffset.UtcNow;

        // Two lessons held and one cancelled (with marks that must not count).
        var l1 = new Lesson(organizationId, branchId, g1, t1, "Room 1", now.AddDays(-2).AddHours(-2), now.AddDays(-2).AddHours(-1));
        var l1Cancelled = new Lesson(organizationId, branchId, g1, t1, "Room 1", now.AddDays(-1).AddHours(-2), now.AddDays(-1).AddHours(-1));
        l1Cancelled.Cancel("Teacher was ill");
        var l2 = new Lesson(organizationId, branchId, g2, t2, "Room 2", now.AddDays(-2).AddHours(-4), now.AddDays(-2).AddHours(-3));
        db.Lessons.AddRange(l1, l1Cancelled, l2);
        db.Attendance.AddRange(
            new Attendance(organizationId, l1.Id, s1, AttendanceStatus.Present), new Attendance(organizationId, l1.Id, s2, AttendanceStatus.Absent),
            new Attendance(organizationId, l1Cancelled.Id, s1, AttendanceStatus.Absent), new Attendance(organizationId, l1Cancelled.Id, s2, AttendanceStatus.Absent),
            new Attendance(organizationId, l2.Id, s3, AttendanceStatus.Present));

        var e1 = new Exam(organizationId, g1, "Branch exam 1", ExamType.Unit, world.ExamDay1);
        var e2 = new Exam(organizationId, g2, "Branch exam 2", ExamType.Unit, world.ExamDay2);
        db.Exams.AddRange(e1, e2);
        db.ExamResults.AddRange(
            new ExamResult(organizationId, e1.Id, s1, 6.0m, 7.0m, 5.5m, 6.5m, 6.0m, 70m, 80m),
            new ExamResult(organizationId, e1.Id, s2, 7.0m, null, null, null, 7.0m, 90m, null),
            new ExamResult(organizationId, e2.Id, s3, null, null, null, null, 8.0m, null, 60m));

        // Homework at L1: s1's answer waited 4 hours for its mark, s2's has not been marked.
        var homework = new Homework(organizationId, l1.Id, "Branch homework", null, HomeworkType.Custom, now.AddDays(3), 10);
        db.Homework.Add(homework);
        var marked = new HomeworkSubmission(organizationId, homework.Id, s1, null, "answer");
        marked.Review(9, null, operatorId);
        db.HomeworkSubmissions.AddRange(marked, new HomeworkSubmission(organizationId, homework.Id, s2, null, "answer"));
        db.Entry(marked).Property(s => s.SubmittedAt).CurrentValue = now.AddDays(-2);
        db.Entry(marked).Property(s => s.ReviewedAt).CurrentValue = now.AddDays(-2).AddHours(4);

        // Everyone was already in the groups when the period began; s2 has since left.
        var enrollments = await db.Enrollments.Where(e => e.GroupId == g1 || e.GroupId == g2).ToListAsync();
        foreach (var enrollment in enrollments) db.Entry(enrollment).Property(e => e.EnrolledAt).CurrentValue = now.AddDays(-60);
        var leaver = enrollments.Single(e => e.StudentId == s2);
        leaver.End(EnrollmentStatus.Dropped, "Moved away");
        db.Entry(leaver).Property(e => e.EndedAt).CurrentValue = now.AddDays(-1);

        db.SupportCases.Add(new SupportCase(organizationId, s2, g1, SupportCaseSource.Staff, SupportSignalCode.ConsecutiveAbsences, "Missed lessons", operatorId));

        // The funnel: six leads; A1 went all the way (trial, student s1, paid), A5 came to the trial, A3 is booked, A2 and A6 are gone.
        Lead NewLead(string name, string source, Action<Lead>? set = null)
        {
            var lead = new Lead(organizationId, branchId, name, "+998900000099", source, "IELTS", operatorId);
            set?.Invoke(lead);
            db.Leads.Add(lead);
            return lead;
        }
        var a1 = NewLead("A1", "Instagram", l => l.MarkConverted(s1));
        NewLead("A2", "Instagram", l => l.MarkLost("Too expensive"));
        var a3 = NewLead("A3", "Website", l => l.SetStatus(LeadStatus.TrialBooked));
        NewLead("A4", "Website");
        var a5 = NewLead("A5", "Instagram", l => l.SetStatus(LeadStatus.TrialAttended));
        NewLead("A6", "Referral", l => l.SetStatus(LeadStatus.NotInterested));
        var trial1 = new TrialLesson(organizationId, a1.Id, g1, now.AddDays(-3));
        trial1.MarkAttended();
        var trial5 = new TrialLesson(organizationId, a5.Id, g1, now.AddDays(-3));
        trial5.MarkAttended();
        db.TrialLessons.AddRange(trial1, trial5, new TrialLesson(organizationId, a3.Id, g1, now.AddDays(2)));

        // s1's invoice and the one payment in the branch.
        var enrollmentOfS1 = enrollments.Single(e => e.StudentId == s1);
        var invoice = new Invoice(organizationId, branchId, s1, enrollmentOfS1.Id, g1, "Branch invoice",
            today.AddDays(-2), today.AddDays(28), today.AddDays(-1), 500000m, "UZS");
        db.Invoices.Add(invoice);
        db.Payments.Add(new Payment(organizationId, branchId, invoice.Id, s1, 200000m, PaymentMethod.Cash, now.AddHours(-1), operatorId, null));

        await db.SaveChangesAsync();
        return world;
    }

    private async Task<Dashboard> DashboardAsync(HttpClient client, World w)
    {
        var response = await client.GetAsync($"/api/dashboard/branch/{w.BranchId}?from={w.From:yyyy-MM-dd}&to={w.To:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Dashboard>())!;
    }

    private static async Task<Guid> NewBranchAsync(HttpClient superAdmin, string? name = null)
    {
        // Named to sort after the demo branches: other tests take the first branch of the name-ordered list as "the" branch.
        var response = await superAdmin.PostAsJsonAsync("/api/branches",
            new { name = name ?? $"ZZ Dashboard {Guid.NewGuid():N}"[..30], address = "Test street 1", timezone = "Asia/Tashkent" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    private static async Task<Guid> NewTeacherAsync(HttpClient superAdmin, Guid branchId, string tag)
    {
        var response = await superAdmin.PostAsJsonAsync("/api/users", new
        {
            // Sorts after "Teacher Demo": other tests look the demo teacher up on the first page of the name-ordered user list.
            fullName = $"Teacher Zz Branch {tag} {Guid.NewGuid():N}"[..34], email = $"branch-{Guid.NewGuid():N}@demo-center.test", phone = "+998900000058",
            password = DbSeeder.DemoPassword, roleCode = "TEACHER", branchIds = new[] { branchId },
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    private static async Task<Guid> NewGroupAsync(HttpClient superAdmin, Guid branchId, Guid courseId, Guid teacherId)
    {
        var group = await superAdmin.PostAsJsonAsync("/api/groups", new { branchId, courseId, name = $"BR-{Guid.NewGuid():N}", capacity = 20 });
        Assert.Equal(HttpStatusCode.Created, group.StatusCode);
        var id = (await group.Content.ReadFromJsonAsync<IdDto>())!.Id;
        Assert.Equal(HttpStatusCode.NoContent,
            (await superAdmin.PostAsJsonAsync($"/api/groups/{id}/staff", new { userId = teacherId, role = "MainTeacher" })).StatusCode);
        return id;
    }

    /// <summary>Named to sort last in the student list, which other tests read the first page of.</summary>
    private static async Task<Guid> NewStudentAsync(HttpClient superAdmin, Guid groupId)
    {
        var response = await superAdmin.PostAsJsonAsync("/api/students", new
        {
            fullName = "Zz Branch Student", phone = "+998900000094", email = $"branch-{Guid.NewGuid():N}@demo-center.test",
            password = DbSeeder.DemoPassword, groupId,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    private async Task AddLeadsAsync(Guid branchId, int count)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var organizationId = await db.Branches.Where(b => b.Id == branchId).Select(b => b.OrganizationId).SingleAsync();
        var operatorId = await db.Users.Where(u => u.Email == "operator@demo-center.test").Select(u => u.Id).SingleAsync();
        for (var i = 0; i < count; i++) db.Leads.Add(new Lead(organizationId, branchId, $"Cache lead {i}", "+998900000098", "Website", "IELTS", operatorId));
        await db.SaveChangesAsync();
    }

    private record IdDto(Guid Id);
    private record BranchDto(Guid Id);
    private record Trend(string Month, decimal Average, int Results);
    private record Academic(decimal? Listening, decimal? Reading, decimal? Writing, decimal? Speaking, decimal? Overall, decimal? Grammar,
        decimal? Vocabulary, int Results, List<Trend> Trend, int LessonsHeld, int LessonsCancelled, double AttendanceRate);
    private record TeacherRow(Guid TeacherId, int Groups, int LessonsHeld, int Attended, int Counted, double AttendanceRate, double? AverageReviewHours,
        int HomeworkPendingReview, int RetentionBase, int Retained, double? RetentionRate, int OpenCases, int ClosedCases, int? Rank);
    private record TeachersBlock(List<TeacherRow> Teachers);
    private record SourceRow(string Source, int Leads, int Contacted, int TrialAttended, int Converted, int Lost);
    private record Funnel(int Leads, int Contacted, int TrialBooked, int TrialAttended, int Converted, int Lost, int TrialToStudent, int TrialToPayment,
        double LeadConversionRate, double TrialToStudentRate, double TrialToPaymentRate, List<SourceRow> Sources);
    private record KpiRow(decimal Collected, decimal Outstanding, int Debtors);
    private record Overview(int TotalStudents, decimal Collected, decimal ExpectedRevenue, decimal Outstanding, int Debtors, int NewLeads, double AttendanceRate,
        List<KpiRow> Branches);
    private record Dashboard(string BranchName, string Timezone, Overview Overview, bool FinanceVisible, Academic Academic, TeachersBlock? Teachers, Funnel Crm);
}
