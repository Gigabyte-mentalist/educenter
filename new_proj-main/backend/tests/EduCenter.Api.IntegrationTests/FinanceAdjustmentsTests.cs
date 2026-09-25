using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduCenter.Infrastructure.Seed;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// Finance Sprint 2 (docs/05_phase3_technical_plan.md): discounts with the branch-level cap, promo codes,
/// refunds that settle instead of creating debt, pay-ahead invoices, and the finance summary report.
/// Caps come from FinanceOptions defaults: 20% discount, 1 000 000 so'm per refund for branch-scoped staff.
/// </summary>
[Collection("Api")]
public class FinanceAdjustmentsTests(EduCenterWebApplicationFactory factory)
{
    [Fact]
    public async Task Discount_IsCappedForBranchStaff_OneAtATime_AndRevokingRestoresTheBalance()
    {
        var b = await BilledStudentAsync(1_000_000);
        var url = $"/api/finance/invoices/{b.Invoice.Id}/discounts";

        Assert.Equal(HttpStatusCode.Forbidden, (await b.Admin.PostAsJsonAsync(url, new { type = "Percent", value = 30, reason = "Aka-uka" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await b.Admin.PostAsJsonAsync(url, new { type = "Percent", value = 10, reason = " " })).StatusCode);

        var applied = await b.Admin.PostAsJsonAsync(url, new { type = "Percent", value = 10, reason = "Aka-uka chegirmasi" });
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        var discounted = await applied.Content.ReadFromJsonAsync<InvoiceDto>();
        Assert.Equal(100000m, discounted!.DiscountAmount);
        Assert.Equal(900000m, discounted.Balance);
        Assert.NotNull(discounted.Discount);

        Assert.Equal(HttpStatusCode.Conflict, (await b.SuperAdmin.PostAsJsonAsync(url, new { type = "Fixed", value = 50000, reason = "Ikkinchi" })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await b.Admin.PostAsJsonAsync($"/api/finance/discounts/{discounted.Discount!.Id}/revoke",
            new { reason = "Xato berildi" })).StatusCode);
        Assert.Equal(1000000m, (await InvoiceAsync(b.Admin, b.StudentId)).Balance);

        // Organization-wide scope is not capped.
        Assert.Equal(HttpStatusCode.OK, (await b.SuperAdmin.PostAsJsonAsync(url, new { type = "Percent", value = 30, reason = "Direktor qarori" })).StatusCode);
        Assert.Equal(700000m, (await InvoiceAsync(b.Admin, b.StudentId)).Balance);

        Assert.Equal(20m, (await b.Admin.GetFromJsonAsync<LimitsDto>("/api/finance/limits"))!.MaxDiscountPercent);
        Assert.Null((await b.SuperAdmin.GetFromJsonAsync<LimitsDto>("/api/finance/limits"))!.MaxDiscountPercent);
        Assert.Equal(HttpStatusCode.Forbidden, (await b.Operator.PostAsJsonAsync(url, new { type = "Fixed", value = 1000, reason = "x" })).StatusCode);
    }

    [Fact]
    public async Task PromoCode_IsManagedBySuperAdmin_RespectsItsUsageLimit_AndRevokeReleasesTheUse()
    {
        var first = await BilledStudentAsync(500_000);
        var second = await BilledStudentAsync(500_000);
        var code = $"WELCOME{Guid.NewGuid():N}"[..14].ToUpperInvariant();

        Assert.Equal(HttpStatusCode.Forbidden, (await first.Admin.PostAsJsonAsync("/api/finance/promo-codes",
            new { code, type = "Fixed", value = 50000 })).StatusCode);

        var created = await first.SuperAdmin.PostAsJsonAsync("/api/finance/promo-codes",
            new { code = code.ToLowerInvariant(), type = "Fixed", value = 50000, maxUses = 1 });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(code, (await created.Content.ReadFromJsonAsync<PromoDto>())!.Code);
        Assert.Equal(HttpStatusCode.Conflict, (await first.SuperAdmin.PostAsJsonAsync("/api/finance/promo-codes",
            new { code, type = "Fixed", value = 1 })).StatusCode);

        var applied = await first.Admin.PostAsJsonAsync($"/api/finance/invoices/{first.Invoice.Id}/promo", new { code });
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        Assert.Equal(450000m, (await applied.Content.ReadFromJsonAsync<InvoiceDto>())!.Balance);

        Assert.Equal(HttpStatusCode.Conflict, (await first.Admin.PostAsJsonAsync($"/api/finance/invoices/{second.Invoice.Id}/promo", new { code })).StatusCode);

        var discountId = (await InvoiceAsync(first.Admin, first.StudentId)).Discount!.Id;
        Assert.Equal(HttpStatusCode.NoContent, (await first.Admin.PostAsJsonAsync($"/api/finance/discounts/{discountId}/revoke",
            new { reason = "Mijoz boshqa aksiyani tanladi" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await first.Admin.PostAsJsonAsync($"/api/finance/invoices/{second.Invoice.Id}/promo", new { code })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await first.Admin.PostAsJsonAsync($"/api/finance/invoices/{first.Invoice.Id}/promo",
            new { code = "NO-SUCH-CODE" })).StatusCode);
    }

    [Fact]
    public async Task Refund_SettlesTheInvoiceInsteadOfCreatingDebt_IsCappedForBranchStaff_AndBlocksVoid()
    {
        var b = await BilledStudentAsync(1_500_000);
        var payment = await (await b.Admin.PostAsJsonAsync($"/api/finance/invoices/{b.Invoice.Id}/payments",
            new { amount = 1500000, method = "Card" })).Content.ReadFromJsonAsync<PaymentDto>();
        var url = $"/api/finance/payments/{payment!.Id}/refunds";

        Assert.Equal(HttpStatusCode.Forbidden, (await b.Admin.PostAsJsonAsync(url, new { amount = 1100000, reason = "Kursni tark etdi" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await b.Admin.PostAsJsonAsync(url, new { amount = 2000000, reason = "Ko'p" })).StatusCode);

        Assert.Equal(HttpStatusCode.Created, (await b.Admin.PostAsJsonAsync(url, new { amount = 300000, reason = "Ikki hafta qatnashmadi" })).StatusCode);
        var settled = await InvoiceAsync(b.Admin, b.StudentId);
        Assert.Equal("Paid", settled.Status);
        Assert.Equal(0m, settled.Balance);
        Assert.Equal(300000m, settled.CreditedAmount);
        Assert.Equal(1200000m, settled.PaidAmount);

        Assert.Equal(HttpStatusCode.Created, (await b.SuperAdmin.PostAsJsonAsync(url, new { amount = 1100000, reason = "Direktor qarori" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await b.Admin.PostAsJsonAsync($"/api/finance/payments/{payment.Id}/void", new { reason = "Xato" })).StatusCode);

        var account = await b.Admin.GetFromJsonAsync<StudentFinanceDto>($"/api/students/{b.StudentId}/finance");
        Assert.Equal(2, account!.Refunds.Count);
        Assert.Equal(1400000m, account.Payments.Single().RefundedAmount);

        var timeline = (await b.Admin.GetFromJsonAsync<List<TimelineEventDto>>($"/api/students/{b.StudentId}/timeline"))!;
        Assert.Equal(2, timeline.Count(e => e.EventType == "REFUND_ISSUED"));
    }

    [Fact]
    public async Task AdvanceInvoices_AreIssuedAhead_AndCappedAtTwelvePeriods()
    {
        var b = await BilledStudentAsync(400_000);
        var url = $"/api/students/{b.StudentId}/advance-invoices";

        Assert.Equal(HttpStatusCode.BadRequest, (await b.Admin.PostAsJsonAsync(url, new { groupId = b.GroupId, months = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await b.Operator.PostAsJsonAsync(url, new { groupId = b.GroupId, months = 1 })).StatusCode);

        var two = await b.Admin.PostAsJsonAsync(url, new { groupId = b.GroupId, months = 2 });
        Assert.Equal(HttpStatusCode.OK, two.StatusCode);
        Assert.Equal(2, (await two.Content.ReadFromJsonAsync<AdvanceDto>())!.InvoicesIssued);

        var periods = (await b.Admin.GetFromJsonAsync<PagedResponse<InvoiceDto>>($"/api/finance/invoices?studentId={b.StudentId}"))!.Items
            .Select(i => i.PeriodStart).OrderBy(d => d).ToList();
        Assert.Equal(3, periods.Count);
        Assert.Equal(periods[0].AddMonths(1), periods[1]);
        Assert.Equal(periods[0].AddMonths(2), periods[2]);

        var capped = await (await b.Admin.PostAsJsonAsync(url, new { groupId = b.GroupId, months = 12 })).Content.ReadFromJsonAsync<AdvanceDto>();
        Assert.Equal(10, capped!.InvoicesIssued);
        Assert.Equal(13, (await b.Admin.GetFromJsonAsync<PagedResponse<InvoiceDto>>($"/api/finance/invoices?studentId={b.StudentId}"))!.Items.Count);
    }

    [Fact]
    public async Task Summary_ShowsCollectionsDiscountsAndRefunds_ForManagersOnly()
    {
        var b = await BilledStudentAsync(700_000);
        await b.Admin.PostAsJsonAsync($"/api/finance/invoices/{b.Invoice.Id}/discounts", new { type = "Fixed", value = 100000, reason = "Hisobot testi" });
        var payment = await (await b.Admin.PostAsJsonAsync($"/api/finance/invoices/{b.Invoice.Id}/payments",
            new { amount = 600000, method = "Cash" })).Content.ReadFromJsonAsync<PaymentDto>();
        await b.Admin.PostAsJsonAsync($"/api/finance/payments/{payment!.Id}/refunds", new { amount = 50000, reason = "Hisobot testi" });

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var range = $"from={today.AddDays(-1):yyyy-MM-dd}&to={today.AddDays(1):yyyy-MM-dd}";

        var summary = await b.Admin.GetFromJsonAsync<SummaryDto>($"/api/finance/reports/summary?{range}");
        Assert.True(summary!.Collected >= 600000m);
        Assert.True(summary.Refunded >= 50000m);
        Assert.True(summary.Discounts >= 100000m);
        Assert.Equal(summary.Collected - summary.Refunded, summary.NetCollected);
        Assert.Contains(summary.ByMethod, m => m.Method == "Cash" && m.Amount >= 600000m);
        Assert.Single(summary.ByBranch); // a branch administrator sees only their branch

        var teacher = await AuthenticatedClientAsync("teacher@demo-center.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await b.Operator.GetAsync($"/api/finance/reports/summary?{range}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync($"/api/finance/reports/summary?{range}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await b.Admin.GetAsync($"/api/finance/reports/summary?from={today:yyyy-MM-dd}&to={today.AddDays(-5):yyyy-MM-dd}")).StatusCode);
    }

    // ---- helpers -------------------------------------------------------------------------------

    /// <summary>A freshly converted student in the administrator's branch, billed for the current month.</summary>
    private async Task<Billed> BilledStudentAsync(decimal monthlyFee)
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        var branchId = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;

        var course = await (await admin.PostAsJsonAsync("/api/courses",
            new { name = $"Adj {Guid.NewGuid():N}"[..20], levelCategory = "IELTS", monthlyFee })).Content.ReadFromJsonAsync<IdDto>();
        var group = await (await admin.PostAsJsonAsync("/api/groups",
            new { branchId, courseId = course!.Id, name = $"ADJ-{Guid.NewGuid():N}", capacity = 10 })).Content.ReadFromJsonAsync<IdDto>();
        var lead = await (await operatorClient.PostAsJsonAsync("/api/leads", new
        {
            branchId, fullName = "Adjustment Student", phone = "+998900000033", source = "Website", interestedCourse = "IELTS"
        })).Content.ReadFromJsonAsync<IdDto>();

        var converted = await operatorClient.PostAsJsonAsync($"/api/leads/{lead!.Id}/convert",
            new { groupId = group!.Id, email = $"adj-{Guid.NewGuid():N}@demo-center.test", password = DbSeeder.DemoPassword });
        Assert.Equal(HttpStatusCode.Created, converted.StatusCode);
        var studentId = (await converted.Content.ReadFromJsonAsync<IdDto>())!.Id;

        return new Billed(admin, superAdmin, operatorClient, studentId, group.Id, await InvoiceAsync(admin, studentId));
    }

    private static async Task<InvoiceDto> InvoiceAsync(HttpClient client, Guid studentId) =>
        (await client.GetFromJsonAsync<PagedResponse<InvoiceDto>>($"/api/finance/invoices?studentId={studentId}"))!.Items
            .OrderBy(i => i.PeriodStart).First();

    private async Task<HttpClient> AuthenticatedClientAsync(string email)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = DbSeeder.DemoPassword });
        var tokens = await response.Content.ReadFromJsonAsync<TokenResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        return client;
    }

    private record Billed(HttpClient Admin, HttpClient SuperAdmin, HttpClient Operator, Guid StudentId, Guid GroupId, InvoiceDto Invoice);
    private record TokenResponseDto(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken);
    private record BranchDto(Guid Id, string Name);
    private record IdDto(Guid Id);
    private record DiscountDto(Guid Id, string Type, decimal Value, decimal Amount, string? PromoCode);
    private record InvoiceDto(Guid Id, decimal Amount, decimal DiscountAmount, decimal CreditedAmount, decimal PaidAmount,
        decimal Balance, string Status, DiscountDto? Discount, DateOnly PeriodStart);
    private record PaymentDto(Guid Id, decimal Amount, decimal RefundedAmount);
    private record RefundDto(Guid Id, decimal Amount);
    private record StudentFinanceDto(List<InvoiceDto> Invoices, List<PaymentDto> Payments, List<RefundDto> Refunds);
    private record LimitsDto(decimal? MaxDiscountPercent, decimal? MaxRefundAmount);
    private record PromoDto(Guid Id, string Code, int UsedCount);
    private record AdvanceDto(int InvoicesIssued);
    private record MethodDto(string Method, decimal Amount, int Count);
    private record BranchRowDto(Guid BranchId, string BranchName);
    private record SummaryDto(decimal Collected, decimal Refunded, decimal NetCollected, decimal Discounts, decimal OverdueNow,
        List<MethodDto> ByMethod, List<BranchRowDto> ByBranch);
    private record TimelineEventDto(Guid Id, string EventType);
}
