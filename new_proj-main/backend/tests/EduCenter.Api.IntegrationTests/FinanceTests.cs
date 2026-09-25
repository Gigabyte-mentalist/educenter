using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduCenter.Api.Automation;
using EduCenter.Infrastructure.Seed;
using Microsoft.Extensions.DependencyInjection;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// Finance core (docs/05_phase3_technical_plan.md Sprint 1): course fee → invoice at conversion → partial and
/// full payment → void with reason; overdue invoice → Debt status + one operator task → payment clears both;
/// and the permission split between finance.debt.view (operator) and payments.* (managers, student/parent).
/// </summary>
[Collection("Api")]
public class FinanceTests(EduCenterWebApplicationFactory factory)
{
    [Fact]
    public async Task ConvertedStudent_IsInvoiced_PaysInParts_AndAWrongPaymentIsVoided()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        var teacher = await AuthenticatedClientAsync("teacher@demo-center.test");
        var branchId = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;

        // Course fee: administrators set it, teachers cannot, negative is rejected.
        var course = await (await admin.PostAsJsonAsync("/api/courses",
            new { name = $"Finance Course {Guid.NewGuid():N}"[..24], levelCategory = "IELTS", monthlyFee = 500000 })).Content.ReadFromJsonAsync<CourseDto>();
        Assert.Equal(500000m, course!.MonthlyFee);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PatchAsJsonAsync($"/api/courses/{course.Id}",
            new { name = course.Name, levelCategory = "IELTS", monthlyFee = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchAsJsonAsync($"/api/courses/{course.Id}",
            new { name = course.Name, levelCategory = "IELTS", monthlyFee = -1 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/courses/{course.Id}",
            new { name = course.Name, levelCategory = "IELTS", monthlyFee = 600000 })).StatusCode);

        var groupId = await CreateGroupAsync(admin, branchId, course.Id);
        var (studentId, email) = await ConvertAsync(operatorClient, branchId, groupId);

        // The first month is invoiced at conversion, due today — not overdue yet.
        var invoice = Assert.Single((await admin.GetFromJsonAsync<PagedResponse<InvoiceDto>>($"/api/finance/invoices?studentId={studentId}"))!.Items);
        Assert.Equal(600000m, invoice.Amount);
        Assert.Equal("Pending", invoice.Status);
        Assert.False(invoice.IsOverdue);

        // Operator: sees the debt side, never takes or lists payments. Teacher: no finance access.
        Assert.Equal(HttpStatusCode.OK, (await operatorClient.GetAsync($"/api/finance/invoices?studentId={studentId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.PostAsJsonAsync($"/api/finance/invoices/{invoice.Id}/payments",
            new { amount = 1000, method = "Cash" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.GetAsync("/api/finance/payments")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync("/api/finance/invoices")).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/finance/invoices/{invoice.Id}/payments",
            new { amount = 700000, method = "Cash" })).StatusCode);

        var first = await admin.PostAsJsonAsync($"/api/finance/invoices/{invoice.Id}/payments", new { amount = 200000, method = "Cash" });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal("PartiallyPaid", (await InvoiceAsync(admin, studentId)).Status);

        var second = await (await admin.PostAsJsonAsync($"/api/finance/invoices/{invoice.Id}/payments",
            new { amount = 400000, method = "Card", note = "Terminal #2" })).Content.ReadFromJsonAsync<PaymentDto>();
        var paid = await InvoiceAsync(admin, studentId);
        Assert.Equal("Paid", paid.Status);
        Assert.Equal(0m, paid.Balance);

        var timeline = (await admin.GetFromJsonAsync<List<TimelineEventDto>>($"/api/students/{studentId}/timeline"))!;
        Assert.Equal(2, timeline.Count(e => e.EventType == "PAYMENT_RECEIVED"));

        // Void: a reason is mandatory, the balance re-opens, and a payment can be voided only once.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/finance/payments/{second!.Id}/void", new { reason = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/finance/payments/{second.Id}/void", new { reason = "Karta to'lovi qaytdi" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/finance/payments/{second.Id}/void", new { reason = "Yana" })).StatusCode);
        var reopened = await InvoiceAsync(admin, studentId);
        Assert.Equal("PartiallyPaid", reopened.Status);
        Assert.Equal(400000m, reopened.Balance);

        // The student sees their own account (payments.view Self) and nobody else's.
        var student = await AuthenticatedClientAsync(email);
        var own = await student.GetFromJsonAsync<StudentFinanceDto>($"/api/students/{studentId}/finance");
        Assert.Equal(400000m, own!.TotalBalance);
        Assert.Single(own.Invoices);
        Assert.Equal(2, own.Payments.Count);
        Assert.Single(own.Payments, p => p.IsVoided);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync($"/api/students/{Guid.NewGuid()}/finance")).StatusCode);
    }

    [Fact]
    public async Task OverdueInvoice_MarksStudentDebt_OpensOneDebtTask_AndFullPaymentClearsBoth()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        var branchId = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;

        var course = await (await admin.PostAsJsonAsync("/api/courses",
            new { name = $"Debt Course {Guid.NewGuid():N}"[..24], levelCategory = "A2", monthlyFee = 300000 })).Content.ReadFromJsonAsync<CourseDto>();
        var groupId = await CreateGroupAsync(admin, branchId, course!.Id);
        var (studentId, _) = await ConvertAsync(operatorClient, branchId, groupId);

        // Five days later the invoice due at conversion is overdue by more than the 3-day follow-up threshold.
        await RunBillingAsync(DateTimeOffset.UtcNow.AddDays(5), studentId);
        await RunBillingAsync(DateTimeOffset.UtcNow.AddDays(5), studentId); // idempotent

        Assert.Equal("Debt", (await admin.GetFromJsonAsync<StudentDto>($"/api/students/{studentId}"))!.Status);
        Assert.Single((await admin.GetFromJsonAsync<PagedResponse<InvoiceDto>>($"/api/finance/invoices?studentId={studentId}"))!.Items);

        var task = Assert.Single((await operatorClient.GetFromJsonAsync<PagedResponse<CrmTaskDto>>("/api/crm/tasks?size=200"))!.Items, t => t.StudentId == studentId);
        Assert.Equal("Debt", task.Type);
        Assert.Equal("DEBT_OVERDUE", task.ReasonCode);
        Assert.Contains("300 000", task.Note);

        var invoice = await InvoiceAsync(admin, studentId);
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync($"/api/finance/invoices/{invoice.Id}/payments",
            new { amount = 300000, method = "Click" })).StatusCode);

        Assert.Equal("Active", (await admin.GetFromJsonAsync<StudentDto>($"/api/students/{studentId}"))!.Status);
        Assert.DoesNotContain((await operatorClient.GetFromJsonAsync<PagedResponse<CrmTaskDto>>("/api/crm/tasks?size=200"))!.Items, t => t.StudentId == studentId);
    }

    // ---- helpers -------------------------------------------------------------------------------

    private async Task RunBillingAsync(DateTimeOffset now, Guid studentId)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<BillingRules>().RunAsync(now, studentId: studentId);
    }

    private static async Task<InvoiceDto> InvoiceAsync(HttpClient client, Guid studentId) =>
        (await client.GetFromJsonAsync<PagedResponse<InvoiceDto>>($"/api/finance/invoices?studentId={studentId}"))!.Items.Single();

    private static async Task<Guid> CreateGroupAsync(HttpClient admin, Guid branchId, Guid courseId)
    {
        var response = await admin.PostAsJsonAsync("/api/groups", new { branchId, courseId, name = $"FIN-{Guid.NewGuid():N}", capacity = 10 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<GroupDto>())!.Id;
    }

    private static async Task<(Guid StudentId, string Email)> ConvertAsync(HttpClient operatorClient, Guid branchId, Guid groupId)
    {
        var lead = await (await operatorClient.PostAsJsonAsync("/api/leads", new
        {
            branchId, fullName = "Finance Student", phone = "+998900000044", source = "Website", interestedCourse = "IELTS"
        })).Content.ReadFromJsonAsync<LeadDto>();

        var email = $"finance-{Guid.NewGuid():N}@demo-center.test";
        var response = await operatorClient.PostAsJsonAsync($"/api/leads/{lead!.Id}/convert",
            new { groupId, email, password = DbSeeder.DemoPassword });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return ((await response.Content.ReadFromJsonAsync<StudentDto>())!.Id, email);
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
    private record BranchDto(Guid Id, string Name);
    private record CourseDto(Guid Id, string Name, string LevelCategory, decimal MonthlyFee);
    private record GroupDto(Guid Id);
    private record LeadDto(Guid Id);
    private record StudentDto(Guid Id, string Status);
    private record InvoiceDto(Guid Id, Guid StudentId, decimal Amount, decimal PaidAmount, decimal Balance, string Status, bool IsOverdue, DateOnly DueDate);
    private record PaymentDto(Guid Id, decimal Amount, string Method, bool IsVoided);
    private record StudentFinanceDto(decimal TotalBalance, decimal OverdueAmount, List<InvoiceDto> Invoices, List<PaymentDto> Payments);
    private record CrmTaskDto(Guid Id, Guid? StudentId, string Type, string? ReasonCode, string? Note);
    private record TimelineEventDto(Guid Id, string EventType);
}
