using System.Net;
using System.Net.Http.Json;
using EduCenter.Infrastructure;
using EduCenter.Infrastructure.Seed;
using EduCenter.Modules.Crm.Domain;
using EduCenter.Modules.Finance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// The marketing funnel (docs/21.09-audit-jurnali S06.02): Lidlar → Bog'lanilgan → Trial band → Trial keldi → Student bo'ldi → To'lov qildi,
/// overall and per source. The figures are checked in a branch made for the purpose — nobody else's leads are in it — and its rows are put
/// in directly, so every count is exact.
/// </summary>
[Collection("Api")]
public class LeadFunnelTests(EduCenterWebApplicationFactory factory)
{
    private readonly LmsWorld lms = new(factory);

    [Fact]
    public async Task TheFunnel_CountsEachStageOfTheCohort_OverallAndPerSource()
    {
        var w = await BuildAsync();

        var funnel = await FunnelAsync(w.SuperAdmin, w, "");

        // 7 leads this week: I1 paid; I2 became a student but her payment was voided; I3 is booked; I4 was only rung without an answer;
        // T1 was reached; T2 enrolled straight away, without a trial; U1 has no source and is gone.
        Assert.Equal(
            new[] { ("leads", 7), ("contacted", 6), ("trialBooked", 3), ("trialAttended", 2), ("student", 3), ("paid", 1) },
            funnel.Stages.Select(s => (s.Key, s.Count)).ToArray());
        Assert.Equal(
            new[] { 1.0, 0.8571, 0.5, 0.6667, 1.5, 0.3333 },
            funnel.Stages.Select(s => s.ConversionFromPrevious).ToArray());
        Assert.Equal(
            new[] { 1.0, 0.8571, 0.4286, 0.2857, 0.4286, 0.1429 },
            funnel.Stages.Select(s => s.ShareOfLeads).ToArray());
        Assert.Equal(
            new[] { "Lidlar", "Bog'lanilgan", "Trial band", "Trial keldi", "Student bo'ldi", "To'lov qildi" },
            funnel.Stages.Select(s => s.Label).ToArray());

        // "Instagram" and "instagram " are one source (shown as the spelling most leads used); a blank one is "Ko'rsatilmagan".
        Assert.Equal(new[] { "Instagram", "Telegram", "Ko'rsatilmagan" }, funnel.BySource.Select(s => s.Source).ToArray());
        Assert.Equal(new[] { 4, 3, 3, 2, 2, 1 }, funnel.BySource[0].Stages.Select(s => s.Count).ToArray());
        Assert.Equal(new[] { 2, 2, 0, 0, 1, 0 }, funnel.BySource[1].Stages.Select(s => s.Count).ToArray());
        Assert.Equal(new[] { 1, 1, 0, 0, 0, 0 }, funnel.BySource[2].Stages.Select(s => s.Count).ToArray());
    }

    [Fact]
    public async Task ASource_NarrowsTheFunnelToItsLeads_WhateverTheCase()
    {
        var w = await BuildAsync();

        var instagram = await FunnelAsync(w.SuperAdmin, w, "&source=INSTAGRAM");

        Assert.Equal(new[] { 4, 3, 3, 2, 2, 1 }, instagram.Stages.Select(s => s.Count).ToArray());
        Assert.Equal("Instagram", Assert.Single(instagram.BySource).Source);

        var unknown = await FunnelAsync(w.SuperAdmin, w, "&source=Ko'rsatilmagan");
        Assert.Equal(1, unknown.Stages[0].Count);
    }

    [Fact]
    public async Task APaymentThatWasVoided_DoesNotMakeAStudentACustomerWhoPaid()
    {
        var w = await BuildAsync();

        var instagram = await FunnelAsync(w.SuperAdmin, w, "&source=Instagram");

        // I1 (paid) and I2 (voided) are both students; only I1 paid.
        Assert.Equal((2, 1), (instagram.Stages.Single(s => s.Key == "student").Count, instagram.Stages.Single(s => s.Key == "paid").Count));
    }

    [Fact]
    public async Task ThePeriod_IsTheDayTheLeadCameIn()
    {
        var w = await BuildAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var recent = await FunnelAsync(w.SuperAdmin, w, "", from: today.AddDays(-5), to: today.AddDays(1));
        var wide = await FunnelAsync(w.SuperAdmin, w, "", from: today.AddDays(-60), to: today.AddDays(1));

        Assert.Equal(7, recent.Stages[0].Count);
        Assert.Equal(8, wide.Stages[0].Count); // plus the one that came in a month ago
        Assert.Equal(6, wide.Stages[1].Count); // ...and never got further than "Yangi"
    }

    [Fact]
    public async Task TheBranchDashboard_CountsTheSameStages()
    {
        var w = await BuildAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var funnel = await FunnelAsync(w.SuperAdmin, w, "");
        var dashboard = (await w.SuperAdmin.GetFromJsonAsync<Dashboard>($"/api/dashboard/branch/{w.BranchId}?from={today.AddDays(-5):yyyy-MM-dd}&to={today.AddDays(1):yyyy-MM-dd}"))!;

        Assert.Equal(funnel.Stages.Select(s => (s.Key, s.Count)), dashboard.Crm.Stages.Select(s => (s.Key, s.Count)));
        Assert.Equal(
            (7, 6, 3, 2, 3),
            (dashboard.Crm.Leads, dashboard.Crm.Contacted, dashboard.Crm.TrialBooked, dashboard.Crm.TrialAttended, dashboard.Crm.Converted));
    }

    [Fact]
    public async Task AnAdministrator_OnlySeesTheirOwnBranch_AndTheOperatorMayLookToo()
    {
        var w = await BuildAsync();
        var admin = await lms.ClientAsync("admin@demo-center.test");
        var ownBranch = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;

        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync($"/api/leads/funnel?branchId={w.BranchId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/leads/funnel?branchId={ownBranch}")).StatusCode);

        // Without a branch the administrator gets their own branches only: the seven leads of the other branch are not among them.
        var period = $"from={DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-5):yyyy-MM-dd}&to={DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1):yyyy-MM-dd}";
        var everything = (await admin.GetFromJsonAsync<Funnel>($"/api/leads/funnel?{period}"))!;
        var ownOnly = (await admin.GetFromJsonAsync<Funnel>($"/api/leads/funnel?branchId={ownBranch}&{period}"))!;
        Assert.Equal(ownOnly.Stages.Select(s => s.Count), everything.Stages.Select(s => s.Count));
        var superAdminSees = (await w.SuperAdmin.GetFromJsonAsync<Funnel>($"/api/leads/funnel?{period}"))!;
        Assert.True(superAdminSees.Stages[0].Count >= everything.Stages[0].Count + 7);

        var operatorClient = await lms.ClientAsync("operator@demo-center.test");
        Assert.Equal(HttpStatusCode.OK, (await operatorClient.GetAsync($"/api/leads/funnel?branchId={ownBranch}")).StatusCode);
    }

    [Fact]
    public async Task TeachersAndStudents_AreRefused_AndSoIsAnAnonymousCaller()
    {
        foreach (var email in new[] { "teacher@demo-center.test", "student@demo-center.test", "parent@demo-center.test" })
            Assert.Equal(HttpStatusCode.Forbidden, (await (await lms.ClientAsync(email)).GetAsync("/api/leads/funnel")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/leads/funnel")).StatusCode);
    }

    [Fact]
    public async Task APeriodLongerThanAYear_OrBackwards_IsRefused()
    {
        var superAdmin = await lms.ClientAsync("superadmin@demo-center.test");

        Assert.Equal(HttpStatusCode.BadRequest, (await superAdmin.GetAsync("/api/leads/funnel?from=2024-01-01&to=2025-06-01")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await superAdmin.GetAsync("/api/leads/funnel?from=2025-06-01&to=2025-05-01")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await superAdmin.GetAsync("/api/leads/funnel?from=2025-01-01&to=2026-01-02")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await superAdmin.GetAsync("/api/leads/funnel")).StatusCode); // the last 90 days
    }

    // ---- the world -----------------------------------------------------------------------------

    private sealed record World(HttpClient SuperAdmin, Guid BranchId);

    private async Task<Funnel> FunnelAsync(HttpClient client, World w, string extra, DateOnly? from = null, DateOnly? to = null)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var url = $"/api/leads/funnel?branchId={w.BranchId}&from={(from ?? today.AddDays(-5)):yyyy-MM-dd}&to={(to ?? today.AddDays(1)):yyyy-MM-dd}{extra}";
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Funnel>())!;
    }

    /// <summary>A branch of its own, with three students (one paid, one whose payment was voided, one who has not paid) and the leads
    /// of the funnel test above.</summary>
    private async Task<World> BuildAsync()
    {
        var superAdmin = await lms.ClientAsync("superadmin@demo-center.test");
        // Named to sort after the demo branches: other tests take the first branch of the name-ordered list as "the" branch.
        var branch = await superAdmin.PostAsJsonAsync("/api/branches",
            new { name = $"ZZ Funnel {Guid.NewGuid():N}"[..30], address = "Test street 1", timezone = "Asia/Tashkent" });
        Assert.Equal(HttpStatusCode.Created, branch.StatusCode);
        var branchId = (await branch.Content.ReadFromJsonAsync<IdDto>())!.Id;

        var course = await superAdmin.PostAsJsonAsync("/api/courses", new { name = $"Funnel Course {Guid.NewGuid():N}"[..25], levelCategory = "IELTS" });
        Assert.Equal(HttpStatusCode.Created, course.StatusCode);
        var courseId = (await course.Content.ReadFromJsonAsync<IdDto>())!.Id;
        var group = await superAdmin.PostAsJsonAsync("/api/groups", new { branchId, courseId, name = $"FN-{Guid.NewGuid():N}", capacity = 20 });
        Assert.Equal(HttpStatusCode.Created, group.StatusCode);
        var groupId = (await group.Content.ReadFromJsonAsync<IdDto>())!.Id;

        var s1 = await NewStudentAsync(superAdmin, groupId);
        var s2 = await NewStudentAsync(superAdmin, groupId);
        var s3 = await NewStudentAsync(superAdmin, groupId);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var organizationId = await db.Groups.Where(g => g.Id == groupId).Select(g => g.OrganizationId).SingleAsync();
        var operatorId = await db.Users.Where(u => u.Email == "operator@demo-center.test").Select(u => u.Id).SingleAsync();
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        Lead NewLead(string name, string source, Action<Lead>? set = null)
        {
            var lead = new Lead(organizationId, branchId, name, "+998900000077", source, "IELTS", operatorId);
            set?.Invoke(lead);
            db.Leads.Add(lead);
            return lead;
        }

        var i1 = NewLead("I1", "Instagram", l => l.MarkConverted(s1));
        var i2 = NewLead("I2", "instagram ", l => l.MarkConverted(s2));
        var i3 = NewLead("I3", "Instagram", l => l.SetStatus(LeadStatus.TrialBooked));
        var i4 = NewLead("I4", "Instagram");
        NewLead("T1", "Telegram", l => l.SetStatus(LeadStatus.Contacted));
        NewLead("T2", "Telegram", l => l.MarkConverted(s3));
        NewLead("U1", "", l => l.MarkLost("Too expensive"));
        var old = NewLead("Old", "Instagram");
        db.Entry(old).Property(l => l.CreatedAt).CurrentValue = now.AddDays(-30);

        // I1 and I2 both came to their trial; I3 has one booked; I4 only got a call nobody answered.
        var t1 = new TrialLesson(organizationId, i1.Id, groupId, now.AddDays(-3));
        t1.MarkAttended();
        var t2 = new TrialLesson(organizationId, i2.Id, groupId, now.AddDays(-3));
        t2.MarkAttended();
        db.TrialLessons.AddRange(t1, t2, new TrialLesson(organizationId, i3.Id, groupId, now.AddDays(2)));
        db.CrmCalls.Add(new CrmCall(organizationId, i4.Id, operatorId, CallOutcome.NoAnswer, null));

        // s1 paid; s2's payment was voided afterwards; s3 has paid nothing.
        var paid = await PayAsync(db, organizationId, branchId, groupId, s1, operatorId, today, now, voided: false);
        var voided = await PayAsync(db, organizationId, branchId, groupId, s2, operatorId, today, now, voided: true);
        Assert.NotEqual(paid, voided);

        await db.SaveChangesAsync();
        return new World(superAdmin, branchId);
    }

    private static async Task<Guid> PayAsync(
        AppDbContext db, Guid organizationId, Guid branchId, Guid groupId, Guid studentId, Guid operatorId, DateOnly today, DateTimeOffset now, bool voided)
    {
        var enrollmentId = await db.Enrollments.Where(e => e.StudentId == studentId && e.GroupId == groupId).Select(e => e.Id).SingleAsync();
        var invoice = new Invoice(organizationId, branchId, studentId, enrollmentId, groupId, "Funnel invoice",
            today.AddDays(-2), today.AddDays(28), today.AddDays(-1), 500000m, "UZS");
        db.Invoices.Add(invoice);
        var payment = new Payment(organizationId, branchId, invoice.Id, studentId, 200000m, PaymentMethod.Cash, now.AddHours(-1), operatorId, null);
        if (voided) payment.Void(operatorId, "Entered by mistake");
        db.Payments.Add(payment);
        return payment.Id;
    }

    private static async Task<Guid> NewStudentAsync(HttpClient superAdmin, Guid groupId)
    {
        var response = await superAdmin.PostAsJsonAsync("/api/students", new
        {
            fullName = "Zz Funnel Student", phone = "+998900000076", email = $"funnel-{Guid.NewGuid():N}@demo-center.test",
            password = DbSeeder.DemoPassword, groupId,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    private record IdDto(Guid Id);
    private record BranchDto(Guid Id, string Name);
    private record StageRow(string Key, string Label, int Count, double ConversionFromPrevious, double ShareOfLeads);
    private record SourceStages(string Source, List<StageRow> Stages);
    private record Funnel(List<StageRow> Stages, List<SourceStages> BySource);
    private record CrmBlock(int Leads, int Contacted, int TrialBooked, int TrialAttended, int Converted, List<StageRow> Stages);
    private record Dashboard(CrmBlock Crm);
}
