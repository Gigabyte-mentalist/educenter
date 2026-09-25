using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduCenter.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// Universal Search (docs/00_TZ.md §27.3, audit §3.18/§5.14). There is no single "search" permission — each
/// resource type is gated by the same code and scope its own screen already uses, so the interesting
/// property isn't "does it find things" but "does it only find what the caller could already reach": a
/// branch-scoped Administrator must not see another branch's student or lead, and a role that lacks
/// branch.view/leads.view entirely must never get those result types back, no matter what the query is.
/// </summary>
[Collection("Api")]
public class SearchTests(EduCenterWebApplicationFactory factory)
{
    [Fact]
    public async Task Search_WithoutToken_ReturnsUnauthorized()
    {
        var response = await factory.CreateClient().GetAsync("/api/search?q=test");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Search_WithShortQuery_ReturnsEmptyList()
    {
        var client = await AuthenticatedClientAsync("superadmin@demo-center.test");

        var results = await client.GetFromJsonAsync<List<SearchResultDto>>("/api/search?q=a");

        Assert.Empty(results!);
    }

    [Fact]
    public async Task Search_FindsEveryResourceType_ForSuperAdmin()
    {
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        var branchId = (await superAdmin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var tag = $"Zqx{Guid.NewGuid():N}"[..14];

        var course = await CreateCourseAsync(superAdmin, tag);
        var group = await CreateGroupAsync(superAdmin, branchId, course, tag);
        var teacherId = await CreateTeacherAsync(superAdmin, branchId, tag);
        var (studentId, leadId) = await CreateLeadAndConvertAsync(operatorClient, branchId, group, tag);
        var newBranch = await CreateBranchAsync(superAdmin, tag);

        var results = await superAdmin.GetFromJsonAsync<List<SearchResultDto>>($"/api/search?q={tag}");

        Assert.Contains(results!, r => r.Type == "student" && r.Id == studentId);
        Assert.Contains(results!, r => r.Type == "teacher" && r.Id == teacherId && r.Href == $"/teachers/{teacherId}");
        // Groups/branches/support have no per-record detail route yet (audit §3.18) — the href instead
        // carries a `?highlight=<id>` the list page scrolls to and rings (docs/09_audit_progress.md §1.15).
        // Leads gained a Lead 360° detail page (docs/kamchiliklar.docx §5), so this one now links straight there.
        Assert.Contains(results!, r => r.Type == "group" && r.Id == group && r.Href == $"/groups/{group}");
        Assert.Contains(results!, r => r.Type == "course" && r.Id == course);
        Assert.Contains(results!, r => r.Type == "lead" && r.Id == leadId && r.Href == $"/leads/{leadId}");
        Assert.Contains(results!, r => r.Type == "branch" && r.Id == newBranch && r.Href == $"/branches?highlight={newBranch}");
    }

    [Fact]
    public async Task Search_FindsSupportTicketByReason()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        var branchId = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var tag = $"Zqx{Guid.NewGuid():N}"[..14];

        var course = await CreateCourseAsync(admin, tag);
        var group = await CreateGroupAsync(admin, branchId, course, tag);
        var (studentId, _) = await CreateLeadAndConvertAsync(operatorClient, branchId, group, tag);

        var propose = await admin.PostAsJsonAsync("/api/support-cases",
            new { studentId, groupId = group, reason = $"Signal {tag}" });
        Assert.Equal(HttpStatusCode.Created, propose.StatusCode);

        var results = await admin.GetFromJsonAsync<List<SearchResultDto>>($"/api/search?q={tag}");

        var ticket = Assert.Single(results!, r => r.Type == "ticket");
        Assert.Equal($"/support?highlight={ticket.Id}", ticket.Href);
    }

    [Fact]
    public async Task Administrator_DoesNotFindStudentsLeadsOrGroupsOutsideTheirBranch()
    {
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        var adminBranch = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var otherBranch = (await superAdmin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))!
            .First(b => b.Id != adminBranch).Id;
        var tag = $"Zqx{Guid.NewGuid():N}"[..14];

        var course = await CreateCourseAsync(superAdmin, tag);
        var group = await CreateGroupAsync(superAdmin, otherBranch, course, tag);
        var (studentId, leadId) = await CreateLeadAndConvertAsync(operatorClient, otherBranch, group, tag);

        var adminResults = await admin.GetFromJsonAsync<List<SearchResultDto>>($"/api/search?q={tag}");
        var superResults = await superAdmin.GetFromJsonAsync<List<SearchResultDto>>($"/api/search?q={tag}");

        Assert.DoesNotContain(adminResults!, r => r.Type == "student" && r.Id == studentId);
        Assert.DoesNotContain(adminResults!, r => r.Type == "lead" && r.Id == leadId);
        Assert.DoesNotContain(adminResults!, r => r.Type == "group" && r.Id == group);
        Assert.Contains(superResults!, r => r.Type == "student" && r.Id == studentId);
        Assert.Contains(superResults!, r => r.Type == "lead" && r.Id == leadId);
        Assert.Contains(superResults!, r => r.Type == "group" && r.Id == group);
    }

    [Fact]
    public async Task Administrator_DoesNotFindBranchOutsideTheirScope()
    {
        // The demo Administrator holds branch.view at MyBranches — Yunusobod is real, but not theirs.
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");

        var results = await admin.GetFromJsonAsync<List<SearchResultDto>>("/api/search?q=Yunusobod");

        Assert.DoesNotContain(results!, r => r.Type == "branch");
    }

    [Fact]
    public async Task Student_SearchResults_NeverIncludeBranchesOrLeads()
    {
        // The demo Student holds neither branch.view nor leads.view at all — the query text matching a real
        // branch name must not matter, because the permission gate should stop it before scope even applies.
        var student = await AuthenticatedClientAsync("student@demo-center.test");

        var results = await student.GetFromJsonAsync<List<SearchResultDto>>("/api/search?q=Chilonzor");

        Assert.DoesNotContain(results!, r => r.Type == "branch");
        Assert.DoesNotContain(results!, r => r.Type == "lead");
    }

    // ---- payments (S06.06) ---------------------------------------------------------------------

    [Fact]
    public async Task Search_FindsAPayment_ByItsAmount_ByTheStudentsName_AndByItsNote()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        var branchId = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var tag = $"Zqx{Guid.NewGuid():N}"[..14];
        var group = await CreateGroupAsync(admin, branchId, await CreateCourseAsync(admin, tag), tag);
        var (studentId, _) = await CreateLeadAndConvertAsync(operatorClient, branchId, group, tag);
        var amount = Random.Shared.Next(1_000_001, 9_999_999);
        var paymentId = await AddPaymentAsync(studentId, group, amount, note: $"Naqd berdi {tag}");
        var grouped = amount.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", " ");

        foreach (var typed in new[] { amount.ToString(), grouped, amount.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture) })
        {
            var byAmount = await admin.GetFromJsonAsync<List<SearchResultDto>>($"/api/search?q={Uri.EscapeDataString(typed)}");
            var found = Assert.Single(byAmount!, r => r.Type == "payment" && r.Id == paymentId);
            Assert.Equal($"{grouped} so'm — Lead {tag}", found.Title);
            Assert.Equal($"{EduCenter.Api.Search.UzbekText.DayMonth(EduCenter.Api.Automation.BranchClock.Today("Asia/Tashkent", DateTimeOffset.UtcNow))} · Naqd", found.Subtitle);
            Assert.Equal($"/finance?highlight={paymentId}", found.Href);
        }

        Assert.Contains((await admin.GetFromJsonAsync<List<SearchResultDto>>($"/api/search?q={tag}"))!, r => r.Type == "payment" && r.Id == paymentId);   // by the student's name
        Assert.Contains((await admin.GetFromJsonAsync<List<SearchResultDto>>($"/api/search?q={Uri.EscapeDataString("Naqd berdi " + tag)}"))!, r => r.Type == "payment" && r.Id == paymentId);   // by the note
    }

    [Fact]
    public async Task APaymentOfAnotherBranch_IsNotFoundByItsAdministrator_AndNobodyWithoutTheFinanceScreenSeesThePaymentType()
    {
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        var adminBranch = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var otherBranch = (await superAdmin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))!.First(b => b.Id != adminBranch).Id;
        var tag = $"Zqx{Guid.NewGuid():N}"[..14];
        var group = await CreateGroupAsync(superAdmin, otherBranch, await CreateCourseAsync(superAdmin, tag), tag);
        var (studentId, _) = await CreateLeadAndConvertAsync(operatorClient, otherBranch, group, tag);
        var paymentId = await AddPaymentAsync(studentId, group, Random.Shared.Next(1_000_001, 9_999_999), note: null);

        Assert.Contains((await superAdmin.GetFromJsonAsync<List<SearchResultDto>>($"/api/search?q={tag}"))!, r => r.Type == "payment" && r.Id == paymentId);
        Assert.DoesNotContain((await admin.GetFromJsonAsync<List<SearchResultDto>>($"/api/search?q={tag}"))!, r => r.Type == "payment");

        // The operator holds no payments.view; a teacher none; a student or parent reads their own payments on their profile, not on /finance.
        foreach (var email in new[] { "operator@demo-center.test", "teacher@demo-center.test", "student@demo-center.test", "parent@demo-center.test" })
        {
            var client = await AuthenticatedClientAsync(email);
            Assert.DoesNotContain((await client.GetFromJsonAsync<List<SearchResultDto>>($"/api/search?q={tag}"))!, r => r.Type == "payment");
            Assert.DoesNotContain((await client.GetFromJsonAsync<List<SearchResultDto>>("/api/search?q=Student%20Demo"))!, r => r.Type == "payment");
        }
    }

    [Fact]
    public async Task AVoidedPayment_IsStillFound_AndSaysSo()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        var branchId = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var tag = $"Zqx{Guid.NewGuid():N}"[..14];
        var group = await CreateGroupAsync(admin, branchId, await CreateCourseAsync(admin, tag), tag);
        var (studentId, _) = await CreateLeadAndConvertAsync(operatorClient, branchId, group, tag);
        var paymentId = await AddPaymentAsync(studentId, group, Random.Shared.Next(1_000_001, 9_999_999), note: null, voided: true);

        var found = Assert.Single((await admin.GetFromJsonAsync<List<SearchResultDto>>($"/api/search?q={tag}"))!, r => r.Type == "payment");

        Assert.Equal(paymentId, found.Id);
        Assert.Equal("Bekor qilingan", found.Subtitle);
    }

    [Fact]
    public async Task ThePaymentsList_CanBeAskedForOnePayment_WhereverItWouldHaveFallen()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        var branchId = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var tag = $"Zqx{Guid.NewGuid():N}"[..14];
        var group = await CreateGroupAsync(admin, branchId, await CreateCourseAsync(admin, tag), tag);
        var (studentId, _) = await CreateLeadAndConvertAsync(operatorClient, branchId, group, tag);
        var paymentId = await AddPaymentAsync(studentId, group, Random.Shared.Next(1_000_001, 9_999_999), note: null);

        var page = (await admin.GetFromJsonAsync<PagedResponse<PaymentRow>>($"/api/finance/payments?id={paymentId}"))!;

        Assert.Equal(paymentId, Assert.Single(page.Items).Id);
    }

    /// <summary>A payment straight in the database, the way the tests of the branch dashboard and the lead funnel put theirs — the API takes payments only against an open invoice.</summary>
    private async Task<Guid> AddPaymentAsync(Guid studentId, Guid groupId, decimal amount, string? note, bool voided = false)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EduCenter.Infrastructure.AppDbContext>();
        var group = await db.Groups.Where(g => g.Id == groupId).Select(g => new { g.OrganizationId, g.BranchId }).SingleAsync();
        var enrollmentId = await db.Enrollments.Where(e => e.StudentId == studentId && e.GroupId == groupId).Select(e => e.Id).SingleAsync();
        var receivedBy = await db.Users.Where(u => u.Email == "operator@demo-center.test").Select(u => u.Id).SingleAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var invoice = new EduCenter.Modules.Finance.Domain.Invoice(group.OrganizationId, group.BranchId, studentId, enrollmentId, groupId, "Search invoice",
            today.AddDays(-2), today.AddDays(28), today.AddDays(-1), amount, "UZS");
        db.Invoices.Add(invoice);
        var payment = new EduCenter.Modules.Finance.Domain.Payment(group.OrganizationId, group.BranchId, invoice.Id, studentId, amount,
            EduCenter.Modules.Finance.Domain.PaymentMethod.Cash, DateTimeOffset.UtcNow, receivedBy, note);
        if (voided) payment.Void(receivedBy, "Entered by mistake");
        db.Payments.Add(payment);
        await db.SaveChangesAsync();
        return payment.Id;
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static async Task<Guid> CreateCourseAsync(HttpClient client, string tag)
    {
        var response = await client.PostAsJsonAsync("/api/courses", new { name = $"Course {tag}", levelCategory = "IELTS" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CourseDto>())!.Id;
    }

    private static async Task<Guid> CreateGroupAsync(HttpClient client, Guid branchId, Guid courseId, string tag)
    {
        var response = await client.PostAsJsonAsync("/api/groups", new { branchId, courseId, name = $"Group {tag}", capacity = 10 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<GroupDto>())!.Id;
    }

    private static async Task<Guid> CreateTeacherAsync(HttpClient superAdmin, Guid branchId, string tag)
    {
        var response = await superAdmin.PostAsJsonAsync("/api/users", new
        {
            fullName = $"Teacher {tag}",
            email = $"search-{Guid.NewGuid():N}@demo-center.test",
            phone = "+998900000088",
            password = DbSeeder.DemoPassword,
            roleCode = "TEACHER",
            branchIds = new[] { branchId },
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<UserDto>())!.Id;
    }

    private static async Task<Guid> CreateBranchAsync(HttpClient superAdmin, string tag)
    {
        var response = await superAdmin.PostAsJsonAsync("/api/branches",
            new { name = $"Branch {tag}", address = "Test", timezone = "Asia/Tashkent" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<BranchDto>())!.Id;
    }

    private static async Task<(Guid StudentId, Guid LeadId)> CreateLeadAndConvertAsync(
        HttpClient operatorClient, Guid branchId, Guid groupId, string tag)
    {
        var lead = await (await operatorClient.PostAsJsonAsync("/api/leads", new
        {
            branchId, fullName = $"Lead {tag}", phone = "+998900000099", source = "Website", interestedCourse = "IELTS",
        })).Content.ReadFromJsonAsync<LeadDto>();

        var email = $"search-{Guid.NewGuid():N}@demo-center.test";
        var response = await operatorClient.PostAsJsonAsync($"/api/leads/{lead!.Id}/convert",
            new { groupId, email, password = DbSeeder.DemoPassword });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var student = await response.Content.ReadFromJsonAsync<StudentDto>();
        return (student!.Id, lead.Id);
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
    private record CourseDto(Guid Id, string Name);
    private record GroupDto(Guid Id);
    private record UserDto(Guid Id);
    private record LeadDto(Guid Id);
    private record StudentDto(Guid Id);
    private record SearchResultDto(string Type, Guid Id, string Title, string? Subtitle, string Href);
    private record PaymentRow(Guid Id);
}
