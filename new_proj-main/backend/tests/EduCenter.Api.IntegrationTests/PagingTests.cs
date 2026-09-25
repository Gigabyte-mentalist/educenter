using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduCenter.Infrastructure;
using EduCenter.Infrastructure.Seed;
using EduCenter.Modules.Finance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// The list screens load one page at a time and every filter runs in the database. These tests pin the shape (rows, total, page
/// count), the clamp on page size, and the two queries that used to be computed in memory: overdue invoices and debtors.
/// </summary>
[Collection("Api")]
public class PagingTests(EduCenterWebApplicationFactory factory)
{
    [Fact]
    public async Task Students_ComeOnePageAtATime_AndSearchFiltersInTheDatabase()
    {
        var admin = await AuthenticatedClientAsync("superadmin@demo-center.test");

        var first = (await admin.GetFromJsonAsync<PagedResponse<StudentRow>>("/api/students?page=1&size=2"))!;
        Assert.Equal(2, first.Items.Count);
        Assert.True(first.Total >= 4, "The demo seed alone has four students.");
        Assert.Equal(2, first.PageSize);
        Assert.Equal((int)Math.Ceiling(first.Total / 2.0), first.PageCount);

        var second = (await admin.GetFromJsonAsync<PagedResponse<StudentRow>>("/api/students?page=2&size=2"))!;
        Assert.Empty(first.Items.Select(s => s.Id).Intersect(second.Items.Select(s => s.Id)));

        var found = (await admin.GetFromJsonAsync<PagedResponse<StudentRow>>("/api/students?search=feruza"))!;
        Assert.NotEmpty(found.Items);
        Assert.All(found.Items, s => Assert.Contains("feruza", s.FullName + s.Email, StringComparison.OrdinalIgnoreCase));
        Assert.True(found.Total < first.Total);

        // A search for something that is nowhere returns an empty page, not an error.
        var none = (await admin.GetFromJsonAsync<PagedResponse<StudentRow>>("/api/students?search=zzzzzz-nobody"))!;
        Assert.Empty(none.Items);
        Assert.Equal(0, none.Total);
    }

    [Fact]
    public async Task PageSize_IsClamped_SoNobodyCanAskForAMillionRows()
    {
        var admin = await AuthenticatedClientAsync("superadmin@demo-center.test");

        var page = (await admin.GetFromJsonAsync<PagedResponse<StudentRow>>("/api/students?size=100000"))!;

        Assert.Equal(200, page.PageSize);
    }

    [Fact]
    public async Task Students_StayScopedWhenPaged()
    {
        var student = await AuthenticatedClientAsync("student@demo-center.test");

        var page = (await student.GetFromJsonAsync<PagedResponse<StudentRow>>("/api/students?size=5"))!;

        Assert.Single(page.Items);
        Assert.Equal(1, page.Total);
    }

    [Fact]
    public async Task Exams_AreAPagedList()
    {
        var teacher = await AuthenticatedClientAsync("teacher@demo-center.test");

        var page = (await teacher.GetFromJsonAsync<PagedResponse<ExamRow>>("/api/exams?size=1"))!;

        Assert.True(page.Items.Count <= 1);
        Assert.Equal(1, page.PageSize);
        Assert.Empty((await teacher.GetFromJsonAsync<PagedResponse<ExamRow>>("/api/exams?search=zzzzzz-no-such-exam"))!.Items);
    }

    [Fact]
    public async Task Debtors_AreGroupedAndPagedInTheDatabase_AndTheOverviewAddsThemUp()
    {
        var studentId = await AddOverdueInvoiceAsync(amount: 1_250_000m, daysOverdue: 12);
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");

        var debtors = (await admin.GetFromJsonAsync<PagedResponse<DebtorRow>>("/api/finance/debtors?search=Student Demo&size=5"))!;
        var debtor = debtors.Items.Single(d => d.StudentId == studentId);
        Assert.True(debtor.TotalDebt >= 1_250_000m);
        Assert.True(debtor.OverdueInvoices >= 1);
        Assert.True(debtor.DaysOverdue >= 12);
        Assert.NotEmpty(debtor.Groups);

        // Biggest debt first, whatever the page size.
        var ordered = (await admin.GetFromJsonAsync<PagedResponse<DebtorRow>>("/api/finance/debtors?size=200"))!.Items;
        Assert.Equal(ordered.OrderByDescending(d => d.TotalDebt).Select(d => d.StudentId), ordered.Select(d => d.StudentId));

        var overview = (await admin.GetFromJsonAsync<OverviewRow>("/api/finance/overview"))!;
        Assert.True(overview.OverdueDebt >= 1_250_000m);
        Assert.True(overview.Debtors >= 1);
        Assert.True(overview.OpenInvoices >= 1);
        Assert.NotNull(overview.TodayIncome);

        // The operator follows up on debt but does not see what was paid.
        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        var operatorOverview = (await operatorClient.GetFromJsonAsync<OverviewRow>("/api/finance/overview"))!;
        Assert.Null(operatorOverview.TodayIncome);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.GetAsync("/api/finance/payments")).StatusCode);
    }

    [Fact]
    public async Task Invoices_FilterOverdueInSql()
    {
        var studentId = await AddOverdueInvoiceAsync(amount: 400_000m, daysOverdue: 5);
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");

        var overdue = (await admin.GetFromJsonAsync<PagedResponse<InvoiceRow>>($"/api/finance/invoices?studentId={studentId}&overdue=true&size=200"))!.Items;
        var notOverdue = (await admin.GetFromJsonAsync<PagedResponse<InvoiceRow>>($"/api/finance/invoices?studentId={studentId}&overdue=false&size=200"))!.Items;

        Assert.NotEmpty(overdue);
        Assert.All(overdue, i => Assert.True(i.IsOverdue));
        Assert.All(notOverdue, i => Assert.False(i.IsOverdue));
        Assert.Empty(overdue.Select(i => i.Id).Intersect(notOverdue.Select(i => i.Id)));

        var byName = (await admin.GetFromJsonAsync<PagedResponse<InvoiceRow>>("/api/finance/invoices?search=Student Demo&size=200"))!;
        Assert.NotEmpty(byName.Items);
        Assert.All(byName.Items, i => Assert.Equal("Student Demo", i.StudentName));
    }

    [Fact]
    public async Task Payments_AndRefunds_ArePaged()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");

        var payments = (await admin.GetFromJsonAsync<PagedResponse<object>>("/api/finance/payments?size=3"))!;
        var refunds = (await admin.GetFromJsonAsync<PagedResponse<object>>("/api/finance/refunds?size=3"))!;

        Assert.True(payments.Items.Count <= 3);
        Assert.Equal(3, payments.PageSize);
        Assert.Equal(3, refunds.PageSize);
    }

    [Fact]
    public async Task SupportCases_TakeSeveralStatusesAtOnce()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");

        var page = (await admin.GetFromJsonAsync<PagedResponse<StatusRow>>("/api/support-cases?statuses=Dismissed&statuses=Resolved&size=50"))!;

        Assert.All(page.Items, c => Assert.Contains(c.Status, new[] { "Dismissed", "Resolved" }));
    }

    [Fact]
    public async Task SupportCase_CanBeOpenedByIdWithinScope_AndAnUnknownIdIsNotFound()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var student = await AuthenticatedClientAsync("student@demo-center.test");

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/support-cases/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync($"/api/support-cases/{Guid.NewGuid()}")).StatusCode);

        var any = (await admin.GetFromJsonAsync<PagedResponse<IdRow>>("/api/support-cases?size=1"))!.Items.FirstOrDefault();
        if (any is not null)
            Assert.Equal(any.Id, (await admin.GetFromJsonAsync<IdRow>($"/api/support-cases/{any.Id}"))!.Id);
    }

    [Fact]
    public async Task StudentTimeline_ReturnsTheLatestEventsOnly()
    {
        var student = await AuthenticatedClientAsync("student@demo-center.test");
        var studentId = (await student.GetFromJsonAsync<PagedResponse<IdRow>>("/api/students"))!.Items.Single().Id;

        var all = (await student.GetFromJsonAsync<List<EventRow>>($"/api/students/{studentId}/timeline"))!;
        var one = (await student.GetFromJsonAsync<List<EventRow>>($"/api/students/{studentId}/timeline?limit=1"))!;

        Assert.NotEmpty(all);
        Assert.Single(one);
        Assert.Equal(all.Max(e => e.OccurredAt), one[0].OccurredAt);
    }

    [Fact]
    public async Task CrmTasks_SplitIntoOverdueAndUpcoming_EachWithItsOwnTotal()
    {
        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        var lead = await CreateLeadAsync(operatorClient, "Bucket Lead");
        Assert.Equal(HttpStatusCode.Created,
            (await operatorClient.PostAsJsonAsync($"/api/leads/{lead.Id}/calls", new { outcome = "CallLater", notes = "later" })).StatusCode);

        var upcoming = (await operatorClient.GetFromJsonAsync<PagedResponse<TaskRow>>("/api/crm/tasks?bucket=upcoming&size=200"))!;
        var overdue = (await operatorClient.GetFromJsonAsync<PagedResponse<TaskRow>>("/api/crm/tasks?bucket=overdue&size=200"))!;
        var everything = (await operatorClient.GetFromJsonAsync<PagedResponse<TaskRow>>("/api/crm/tasks?size=200"))!;

        Assert.Contains(upcoming.Items, t => t.LeadId == lead.Id);
        Assert.DoesNotContain(overdue.Items, t => t.LeadId == lead.Id);
        Assert.Equal(everything.Total, upcoming.Total + overdue.Total);
        Assert.All(upcoming.Items, t => Assert.True(t.DueAt >= DateTimeOffset.UtcNow.AddMinutes(-1)));
        Assert.All(overdue.Items, t => Assert.True(t.DueAt < DateTimeOffset.UtcNow.AddMinutes(1)));
    }

    [Fact]
    public async Task Leads_AreFilteredByStatusesAndSearch_OnePageAtATime()
    {
        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        var name = $"Paging Lead {Guid.NewGuid():N}"[..24];
        var lead = await CreateLeadAsync(operatorClient, name);

        var byName = (await operatorClient.GetFromJsonAsync<PagedResponse<LeadRow>>($"/api/leads?search={Uri.EscapeDataString(name)}"))!;
        Assert.Equal(lead.Id, Assert.Single(byName.Items).Id);

        var inColumn = (await operatorClient.GetFromJsonAsync<PagedResponse<LeadRow>>(
            $"/api/leads?statuses=Lead&statuses=Contacted&search={Uri.EscapeDataString(name)}"))!;
        Assert.Single(inColumn.Items);
        var elsewhere = (await operatorClient.GetFromJsonAsync<PagedResponse<LeadRow>>(
            $"/api/leads?statuses=Converted&search={Uri.EscapeDataString(name)}"))!;
        Assert.Empty(elsewhere.Items);

        var page = (await operatorClient.GetFromJsonAsync<PagedResponse<LeadRow>>("/api/leads?size=1"))!;
        Assert.Single(page.Items);
        Assert.True(page.Total >= 2);
    }

    /// <summary>Writes an unpaid invoice that fell due <paramref name="daysOverdue"/> days ago for the demo student — the only way to get
    /// an overdue one, because the billing job never issues an invoice that is already late.</summary>
    private async Task<Guid> AddOverdueInvoiceAsync(decimal amount, int daysOverdue)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await db.Users.FirstAsync(u => u.Email == "student@demo-center.test");
        var student = await db.Students.FirstAsync(s => s.UserId == user.Id);
        var enrollment = await db.Enrollments.FirstAsync(e => e.StudentId == student.Id);
        var group = await db.Groups.FirstAsync(g => g.Id == enrollment.GroupId);

        var due = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-daysOverdue));
        db.Invoices.Add(new Invoice(user.OrganizationId, group.BranchId, student.Id, enrollment.Id, group.Id,
            $"Paging test {Guid.NewGuid():N}", due.AddMonths(-1), due, due, amount, "UZS"));
        await db.SaveChangesAsync();
        return student.Id;
    }

    private static async Task<LeadRow> CreateLeadAsync(HttpClient client, string name)
    {
        var branchId = (await client.GetFromJsonAsync<List<BranchRow>>("/api/branches"))![0].Id;
        var response = await client.PostAsJsonAsync("/api/leads", new
        {
            branchId, fullName = name, phone = "+998900000066", source = "Instagram", interestedCourse = "IELTS",
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LeadRow>())!;
    }

    private async Task<HttpClient> AuthenticatedClientAsync(string email)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = DbSeeder.DemoPassword });
        var tokens = await response.Content.ReadFromJsonAsync<TokenResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        return client;
    }

    private record TokenResponseDto(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken);
    private record BranchRow(Guid Id, string Name);
    private record StudentRow(Guid Id, string FullName, string Email);
    private record ExamRow(Guid Id, Guid GroupId, string Name);
    private record DebtorRow(Guid StudentId, decimal TotalDebt, int OverdueInvoices, int DaysOverdue, List<string> Groups);
    private record OverviewRow(decimal OverdueDebt, int Debtors, int OpenInvoices, decimal? TodayIncome);
    private record InvoiceRow(Guid Id, string StudentName, bool IsOverdue);
    private record StatusRow(string Status);
    private record TaskRow(Guid Id, Guid? LeadId, DateTimeOffset DueAt);
    private record LeadRow(Guid Id);
    private record IdRow(Guid Id);
    private record EventRow(DateTimeOffset OccurredAt);
}
