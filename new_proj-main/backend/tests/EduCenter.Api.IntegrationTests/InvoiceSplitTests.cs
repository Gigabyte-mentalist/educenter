using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduCenter.Api.Automation;
using EduCenter.Infrastructure.Seed;
using Microsoft.Extensions.DependencyInjection;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// Instalments (docs/21.09-audit-jurnali S07.05): one unpaid invoice cut into parts with their own due dates. The one thing that must
/// never happen is the same money counted twice — as the invoice and as its parts — so most of these tests read the figures every
/// screen shows before and after a split and demand that they do not move.
/// </summary>
[Collection("Api")]
public class InvoiceSplitTests(EduCenterWebApplicationFactory factory)
{
    [Fact]
    public async Task SixHundredThousand_InThreeParts_MakesThreeInvoices_ThatAddUpExactly_AndTheOriginalIsSplit()
    {
        var b = await BilledAsync(600_000);
        var start = b.Invoice.PeriodStart;

        var split = await SplitAsync(b.Admin, b.Invoice.Id, (200_000m, start), (200_000m, start.AddDays(10)), (200_000m, start.AddDays(20)));
        Assert.Equal(HttpStatusCode.OK, split.StatusCode);
        var result = (await split.Content.ReadFromJsonAsync<SplitResult>())!;

        Assert.Equal("Split", result.Invoice.Status);
        Assert.Equal(3, result.Parts.Count);
        Assert.Equal(600_000m, result.Parts.Sum(p => p.Amount)); // to the last tiyin
        Assert.Equal(new[] { 1, 2, 3 }, result.Parts.Select(p => p.InstallmentNumber!.Value).ToArray());
        Assert.All(result.Parts, p =>
        {
            Assert.Equal(b.Invoice.Id, p.ParentInvoiceId);
            Assert.Equal(3, p.InstallmentCount);
            Assert.Equal("Pending", p.Status);
            Assert.Equal(start, p.PeriodStart); // the parts belong to the period the original was billed for
            Assert.Equal(b.Invoice.Description + $" ({p.InstallmentNumber}/3)", p.Description);
        });
        Assert.Equal(new[] { start, start.AddDays(10), start.AddDays(20) }, result.Parts.Select(p => p.DueDate).ToArray());

        var listed = (await b.Admin.GetFromJsonAsync<PagedResponse<InvoiceRow>>($"/api/finance/invoices?studentId={b.StudentId}"))!.Items;
        Assert.Equal(4, listed.Count);

        // The student's own timeline says it happened.
        var events = await b.SuperAdmin.GetFromJsonAsync<List<TimelineRow>>($"/api/students/{b.StudentId}/timeline");
        Assert.Contains(events!, e => e.EventType == "INVOICE_SPLIT");
    }

    [Fact]
    public async Task PartsThatDoNotAddUp_OrAreMalformed_AreRefused_AndNothingIsCreated()
    {
        var b = await BilledAsync(600_000);
        var start = b.Invoice.PeriodStart;

        var bad = new (string Why, (decimal Amount, DateOnly Due)[] Parts)[]
        {
            ("short", [(200_000m, start), (300_000m, start.AddDays(10))]),
            ("over", [(400_000m, start), (300_000m, start.AddDays(10))]),
            ("one part", [(600_000m, start)]),
            ("no parts", []),
            ("zero", [(0m, start), (600_000m, start.AddDays(10))]),
            ("negative", [(-100_000m, start), (700_000m, start.AddDays(10))]),
            ("fractions of a tiyin", [(300_000.005m, start), (299_999.995m, start.AddDays(10))]),
            ("dates not rising", [(300_000m, start.AddDays(10)), (300_000m, start.AddDays(10))]),
            ("dates falling", [(300_000m, start.AddDays(10)), (300_000m, start)]),
            ("before the period", [(300_000m, start.AddDays(-1)), (300_000m, start.AddDays(10))]),
            ("thirteen parts", Enumerable.Range(0, 13).Select(i => (600_000m / 13, start.AddDays(i))).ToArray()),
        };
        foreach (var (why, parts) in bad)
            Assert.True(HttpStatusCode.BadRequest == (await SplitAsync(b.Admin, b.Invoice.Id, parts)).StatusCode, why);

        var listed = (await b.Admin.GetFromJsonAsync<PagedResponse<InvoiceRow>>($"/api/finance/invoices?studentId={b.StudentId}"))!.Items;
        var only = Assert.Single(listed);
        Assert.Equal("Pending", only.Status);

        // Two parts at exact tiyin amounts are fine; the last part need not equal the others.
        Assert.Equal(HttpStatusCode.OK, (await SplitAsync(b.Admin, b.Invoice.Id, (199_999.99m, start), (400_000.01m, start.AddDays(5)))).StatusCode);
    }

    [Fact]
    public async Task OnlyAnUnpaidInvoice_CanBeCut_AndAPartIsNotCutAgain()
    {
        var b = await BilledAsync(600_000);
        var start = b.Invoice.PeriodStart;
        var two = new[] { (300_000m, start), (300_000m, start.AddDays(10)) };

        // Part-paid: the money already taken belongs to the whole invoice.
        var partPaid = await BilledAsync(600_000);
        Assert.Equal(HttpStatusCode.Created, (await PayAsync(partPaid.Admin, partPaid.Invoice.Id, 100_000)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await SplitAsync(partPaid.Admin, partPaid.Invoice.Id, two)).StatusCode);

        var paid = await BilledAsync(300_000);
        Assert.Equal(HttpStatusCode.Created, (await PayAsync(paid.Admin, paid.Invoice.Id, 300_000)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await SplitAsync(paid.Admin, paid.Invoice.Id, (150_000m, paid.Invoice.PeriodStart), (150_000m, paid.Invoice.PeriodStart.AddDays(5)))).StatusCode);

        var cancelled = await BilledAsync(600_000);
        Assert.Equal(HttpStatusCode.NoContent, (await cancelled.Admin.PostAsJsonAsync($"/api/finance/invoices/{cancelled.Invoice.Id}/cancel", new { reason = "Xato yozilgan" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await SplitAsync(cancelled.Admin, cancelled.Invoice.Id, two)).StatusCode);

        // Split once, never twice — and a part is not cut further.
        var first = (await (await SplitAsync(b.Admin, b.Invoice.Id, two)).Content.ReadFromJsonAsync<SplitResult>())!;
        Assert.Equal(HttpStatusCode.Conflict, (await SplitAsync(b.Admin, b.Invoice.Id, two)).StatusCode);
        var part = first.Parts[1];
        Assert.Equal(HttpStatusCode.Conflict, (await SplitAsync(b.Admin, part.Id, (150_000m, part.DueDate), (150_000m, part.DueDate.AddDays(3)))).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await SplitAsync(b.Admin, Guid.NewGuid(), two)).StatusCode);
    }

    [Fact]
    public async Task ThePartsAreOrdinaryInvoices_PaidDiscountedAndCancelledOnTheirOwn_TheOriginalIsNone()
    {
        var b = await BilledAsync(600_000);
        var start = b.Invoice.PeriodStart;
        var parts = (await (await SplitAsync(b.Admin, b.Invoice.Id, (200_000m, start), (200_000m, start.AddDays(10)), (200_000m, start.AddDays(20)))).Content.ReadFromJsonAsync<SplitResult>())!.Parts;

        // The original takes no payment, no discount and no cancelling: its parts do.
        var paidOriginal = await PayAsync(b.Admin, b.Invoice.Id, 100_000);
        Assert.Equal(HttpStatusCode.Conflict, paidOriginal.StatusCode);
        Assert.Contains("qismlariga", await paidOriginal.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await b.SuperAdmin.PostAsJsonAsync($"/api/finance/invoices/{b.Invoice.Id}/discounts", new { type = "Fixed", value = 10_000, reason = "Sinov" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await b.Admin.PostAsJsonAsync($"/api/finance/invoices/{b.Invoice.Id}/cancel", new { reason = "Sinov" })).StatusCode);

        // A part is paid in full and in parts; overpaying it is refused.
        Assert.Equal(HttpStatusCode.BadRequest, (await PayAsync(b.Admin, parts[0].Id, 200_001)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PayAsync(b.Admin, parts[0].Id, 50_000)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PayAsync(b.Admin, parts[0].Id, 150_000)).StatusCode);
        var second = await b.Admin.PostAsJsonAsync($"/api/finance/invoices/{parts[1].Id}/discounts", new { type = "Fixed", value = 20_000, reason = "Aka-uka" });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await b.Admin.PostAsJsonAsync($"/api/finance/invoices/{parts[2].Id}/cancel", new { reason = "Ikki qismga o'zgartirildi" })).StatusCode);

        var finance = (await b.SuperAdmin.GetFromJsonAsync<StudentFinance>($"/api/students/{b.StudentId}/finance"))!;
        var byId = finance.Invoices.ToDictionary(i => i.Id);
        Assert.Equal("Paid", byId[parts[0].Id].Status);
        Assert.Equal(180_000m, byId[parts[1].Id].Balance);
        Assert.Equal("Cancelled", byId[parts[2].Id].Status);
        Assert.Equal("Split", byId[b.Invoice.Id].Status);
        // What the student owes is what is left on the parts — not that plus the original's 600 000.
        Assert.Equal(180_000m, finance.TotalBalance);
    }

    [Fact]
    public async Task NoFigureCountsTheMoneyTwice_NotTheDebtorsNotTheReportNotTheStudentsOwnScreen()
    {
        var b = await BilledAsync(600_000);
        var student = await ClientAsync(b.Email);
        var start = b.Invoice.PeriodStart;
        var day = start.ToString("yyyy-MM-dd");
        var reportUrl = $"/api/finance/reports/summary?from={day}&to={day}&branchId={b.BranchId}";

        var before = (await b.SuperAdmin.GetFromJsonAsync<Summary>(reportUrl))!;
        var overviewBefore = (await b.Admin.GetFromJsonAsync<Overview>("/api/finance/overview"))!;
        var dueBefore = (await student.GetFromJsonAsync<StudentDashboard>("/api/dashboard/student"))!.Money.Outstanding;
        Assert.Equal(600_000m, dueBefore);

        Assert.Equal(HttpStatusCode.OK, (await SplitAsync(b.Admin, b.Invoice.Id, (300_000m, start), (300_000m, start.AddDays(30)))).StatusCode);

        var after = (await b.SuperAdmin.GetFromJsonAsync<Summary>(reportUrl))!;
        Assert.Equal(before.Billed, after.Billed);
        Assert.Equal(before.Discounts, after.Discounts);
        Assert.Equal(before.ExpectedRevenue, after.ExpectedRevenue);
        Assert.Equal(before.OutstandingOnBilled, after.OutstandingOnBilled);

        // Two open invoices where there was one — the original no longer counts as open.
        var overviewAfter = (await b.Admin.GetFromJsonAsync<Overview>("/api/finance/overview"))!;
        Assert.Equal(overviewBefore.OpenInvoices + 1, overviewAfter.OpenInvoices);

        Assert.Equal(dueBefore, (await student.GetFromJsonAsync<StudentDashboard>("/api/dashboard/student"))!.Money.Outstanding);
        Assert.Equal(600_000m, (await b.SuperAdmin.GetFromJsonAsync<StudentFinance>($"/api/students/{b.StudentId}/finance"))!.TotalBalance);

        // Paying a part lowers the same figures by exactly that much, everywhere.
        var firstPart = (await b.Admin.GetFromJsonAsync<PagedResponse<InvoiceRow>>($"/api/finance/invoices?studentId={b.StudentId}"))!.Items.Single(i => i.InstallmentNumber == 1);
        Assert.Equal(HttpStatusCode.Created, (await PayAsync(b.Admin, firstPart.Id, 300_000)).StatusCode);
        Assert.Equal(300_000m, (await student.GetFromJsonAsync<StudentDashboard>("/api/dashboard/student"))!.Money.Outstanding);
        Assert.Equal(after.OutstandingOnBilled - 300_000m, (await b.SuperAdmin.GetFromJsonAsync<Summary>(reportUrl))!.OutstandingOnBilled);
    }

    [Fact]
    public async Task Billing_DoesNotIssueASecondInvoiceForASplitPeriod_AndADebtIsOnlyWhatIsPastDue()
    {
        var b = await BilledAsync(600_000);
        var start = b.Invoice.PeriodStart;
        Assert.Equal(HttpStatusCode.OK, (await SplitAsync(b.Admin, b.Invoice.Id, (200_000m, start), (400_000m, start.AddDays(30)))).StatusCode);

        // Five days on: the first part is overdue, the second is not; billing runs and must find the period already issued.
        var later = DateTimeOffset.UtcNow.AddDays(5);
        await RunBillingAsync(later, b.StudentId);
        await RunBillingAsync(later, b.StudentId);

        var listed = (await b.Admin.GetFromJsonAsync<PagedResponse<InvoiceRow>>($"/api/finance/invoices?studentId={b.StudentId}"))!.Items;
        Assert.Equal(3, listed.Count); // the original and its two parts — nothing new for this period
        Assert.Equal("Debt", (await b.Admin.GetFromJsonAsync<StudentRow>($"/api/students/{b.StudentId}"))!.Status);

        // The operator's debt task carries what is past due — the first part — and not the sum of everything on the student's account.
        var task = Assert.Single((await b.Operator.GetFromJsonAsync<PagedResponse<TaskRow>>("/api/crm/tasks?size=200"))!.Items, t => t.StudentId == b.StudentId);
        Assert.Contains("200 000", task.Note);
        Assert.DoesNotContain("600 000", task.Note);
        Assert.DoesNotContain("800 000", task.Note);

        // Paying that part clears the debt though the second is still open.
        var firstPart = listed.Single(i => i.InstallmentNumber == 1);
        Assert.Equal(HttpStatusCode.Created, (await PayAsync(b.Admin, firstPart.Id, 200_000)).StatusCode);
        Assert.Equal("Active", (await b.Admin.GetFromJsonAsync<StudentRow>($"/api/students/{b.StudentId}"))!.Status);
    }

    [Fact]
    public async Task EachPart_IsRemindedAboutOnItsOwnDate_ForItsOwnAmount()
    {
        var b = await BilledAsync(600_000);
        var student = await ClientAsync(b.Email);
        var start = b.Invoice.PeriodStart;
        Assert.Equal(HttpStatusCode.OK, (await SplitAsync(b.Admin, b.Invoice.Id, (200_000m, start), (400_000m, start.AddDays(10)))).StatusCode);

        // The first part is due today: one reminder for it (however many sweeps), and none for the original's 600 000.
        await SweepAsync(DateTimeOffset.UtcNow, b.StudentId);
        await SweepAsync(DateTimeOffset.UtcNow, b.StudentId);
        var due = Assert.Single(await InboxAsync(student), n => n.Type == "PAYMENT_DUE");
        Assert.Contains("200 000", due.Body);

        // Five days on it is overdue — once. Ten days on the second part falls due, for its own amount.
        await SweepAsync(DateTimeOffset.UtcNow.AddDays(5), b.StudentId);
        await SweepAsync(DateTimeOffset.UtcNow.AddDays(5), b.StudentId);
        Assert.Single(await InboxAsync(student), n => n.Type == "PAYMENT_OVERDUE");

        await SweepAsync(DateTimeOffset.UtcNow.AddDays(10), b.StudentId);
        var dueReminders = (await InboxAsync(student)).Where(n => n.Type == "PAYMENT_DUE").ToList();
        Assert.Equal(2, dueReminders.Count);
        Assert.Contains(dueReminders, n => n.Body.Contains("400 000"));
        Assert.DoesNotContain(await InboxAsync(student), n => n.Body.Contains("600 000"));
    }

    [Fact]
    public async Task WhoMaySplit_FollowsWhoMayEditPayments_InTheirOwnBranch()
    {
        var b = await BilledAsync(600_000);
        var start = b.Invoice.PeriodStart;
        var two = new[] { (300_000m, start), (300_000m, start.AddDays(10)) };

        foreach (var email in new[] { "operator@demo-center.test", "teacher@demo-center.test", b.Email, "developer@demo-center.test" })
            Assert.Equal(HttpStatusCode.Forbidden, (await SplitAsync(await ClientAsync(email), b.Invoice.Id, two)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SplitAsync(factory.CreateClient(), b.Invoice.Id, two)).StatusCode);

        // The Administrator of the other branch cannot reach this invoice; the Super Admin can.
        var other = await BilledAsync(600_000, otherBranch: true);
        Assert.Equal(HttpStatusCode.Forbidden, (await SplitAsync(b.Admin, other.Invoice.Id, (300_000m, other.Invoice.PeriodStart), (300_000m, other.Invoice.PeriodStart.AddDays(10)))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SplitAsync(b.SuperAdmin, other.Invoice.Id, (300_000m, other.Invoice.PeriodStart), (300_000m, other.Invoice.PeriodStart.AddDays(10)))).StatusCode);

        // None of the refusals changed the invoice.
        Assert.Equal("Pending", (await b.Admin.GetFromJsonAsync<PagedResponse<InvoiceRow>>($"/api/finance/invoices?studentId={b.StudentId}"))!.Items.Single().Status);
    }

    // ---- helpers ------------------------------------------------------------------------------------

    private static Task<HttpResponseMessage> SplitAsync(HttpClient client, Guid invoiceId, params (decimal Amount, DateOnly Due)[] parts) =>
        client.PostAsJsonAsync($"/api/finance/invoices/{invoiceId}/split",
            new { parts = parts.Select(p => new { amount = p.Amount, dueDate = p.Due }) });

    private static Task<HttpResponseMessage> PayAsync(HttpClient client, Guid invoiceId, decimal amount) =>
        client.PostAsJsonAsync($"/api/finance/invoices/{invoiceId}/payments", new { amount, method = "Cash" });

    private async Task RunBillingAsync(DateTimeOffset now, Guid studentId)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<BillingRules>().RunAsync(now, studentId: studentId);
    }

    private async Task SweepAsync(DateTimeOffset now, Guid studentId)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<NotificationRules>().RunAsync(now, studentId);
    }

    private static async Task<List<NotificationRow>> InboxAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<NotificationRow>>("/api/notifications?limit=100"))!;

    private readonly Dictionary<string, HttpClient> clients = [];

    private async Task<HttpClient> ClientAsync(string email)
    {
        if (clients.TryGetValue(email, out var known)) return known;
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = DbSeeder.DemoPassword });
        var tokens = (await response.Content.ReadFromJsonAsync<Tokens>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return clients[email] = client;
    }

    /// <summary>A student billed a month at <paramref name="fee"/>, through the same door as the office: a lead converted into a group.</summary>
    private async Task<Billed> BilledAsync(decimal fee, bool otherBranch = false)
    {
        var admin = await ClientAsync("admin@demo-center.test");
        var superAdmin = await ClientAsync("superadmin@demo-center.test");
        var operatorClient = await ClientAsync("operator@demo-center.test");
        var branches = (await superAdmin.GetFromJsonAsync<List<BranchRow>>("/api/branches"))!;
        var adminBranch = (await admin.GetFromJsonAsync<List<BranchRow>>("/api/branches"))![0].Id;
        var branchId = otherBranch ? branches.First(x => x.Id != adminBranch).Id : adminBranch;

        var course = await (await superAdmin.PostAsJsonAsync("/api/courses", new { name = $"Split {Guid.NewGuid():N}"[..20], levelCategory = "IELTS", monthlyFee = fee })).Content.ReadFromJsonAsync<IdRow>();
        var group = await (await superAdmin.PostAsJsonAsync("/api/groups", new { branchId, courseId = course!.Id, name = $"SPL-{Guid.NewGuid():N}", capacity = 10 })).Content.ReadFromJsonAsync<IdRow>();
        var lead = await (await operatorClient.PostAsJsonAsync("/api/leads", new { branchId, fullName = "Split Student", phone = "+998900000055", source = "Website", interestedCourse = "IELTS" })).Content.ReadFromJsonAsync<IdRow>();
        var email = $"split-{Guid.NewGuid():N}@demo-center.test";
        var converted = await operatorClient.PostAsJsonAsync($"/api/leads/{lead!.Id}/convert", new { groupId = group!.Id, email, password = DbSeeder.DemoPassword });
        Assert.Equal(HttpStatusCode.Created, converted.StatusCode);
        var studentId = (await converted.Content.ReadFromJsonAsync<IdRow>())!.Id;

        var invoice = (await superAdmin.GetFromJsonAsync<PagedResponse<InvoiceRow>>($"/api/finance/invoices?studentId={studentId}"))!.Items.Single();
        return new Billed(admin, superAdmin, operatorClient, studentId, email, branchId, invoice);
    }

    private record Billed(HttpClient Admin, HttpClient SuperAdmin, HttpClient Operator, Guid StudentId, string Email, Guid BranchId, InvoiceRow Invoice);
    private record Tokens(string AccessToken);
    private record IdRow(Guid Id);
    private record BranchRow(Guid Id);
    private record InvoiceRow(Guid Id, string Description, DateOnly PeriodStart, DateOnly DueDate, decimal Amount, decimal PaidAmount, decimal Balance, string Status,
        bool IsOverdue, Guid? ParentInvoiceId, int? InstallmentNumber, int? InstallmentCount);
    private record SplitResult(InvoiceRow Invoice, List<InvoiceRow> Parts);
    private record StudentFinance(decimal TotalBalance, List<InvoiceRow> Invoices);
    private record Summary(decimal Billed, decimal Discounts, decimal ExpectedRevenue, decimal OutstandingOnBilled);
    private record Overview(decimal OverdueDebt, int Debtors, int OpenInvoices);
    private record MoneyRow(decimal Outstanding);
    private record StudentDashboard(MoneyRow Money);
    private record StudentRow(string Status);
    private record TaskRow(Guid? StudentId, string? Note);
    private record TimelineRow(string EventType);
    private record NotificationRow(string Type, string Body);
}
