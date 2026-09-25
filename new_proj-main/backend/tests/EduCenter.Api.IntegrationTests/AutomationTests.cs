using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduCenter.Api.Automation;
using EduCenter.Infrastructure.Seed;
using Microsoft.Extensions.DependencyInjection;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// Automation Engine (docs/05_phase3_technical_plan.md Sprint 5): rule settings are organization-wide, visible to
/// administrators, editable only with automation.manage, validated against the catalog, and actually change what the
/// rules do. Every test that edits a rule restores the defaults — the whole collection shares one organization.
/// </summary>
[Collection("Api")]
public class AutomationTests(EduCenterWebApplicationFactory factory)
{
    [Fact]
    public async Task Rules_AreVisibleToAdministrators_ChangeableOnlyBySuperAdmin_AndValidated()
    {
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");

        var rules = (await superAdmin.GetFromJsonAsync<List<RuleDto>>("/api/automation/rules"))!;
        Assert.Equal(11, rules.Count); // the seven follow-up/billing/reminder rules and the four learning signals (S04.05)
        var absence = rules.Single(r => r.Code == "ABSENCE_FOLLOW_UP");
        Assert.True(absence.Enabled);
        Assert.Equal(2, absence.Parameters.Single(p => p.Key == "consecutiveAbsences").Value);

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/automation/rules")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PutAsJsonAsync("/api/automation/rules/ABSENCE_FOLLOW_UP", new { enabled = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.GetAsync("/api/automation/rules")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsync("/api/automation/run", null)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await superAdmin.PutAsJsonAsync("/api/automation/rules/NO_SUCH_RULE", new { enabled = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await superAdmin.PutAsJsonAsync("/api/automation/rules/ABSENCE_FOLLOW_UP",
            new { enabled = true, parameters = new Dictionary<string, int> { ["consecutiveAbsences"] = 50 } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await superAdmin.PutAsJsonAsync("/api/automation/rules/HOMEWORK_SUPPORT_SIGNAL",
            new { enabled = true, parameters = new Dictionary<string, int> { ["window"] = 3, ["maxSubmitted"] = 3 } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await superAdmin.PutAsJsonAsync("/api/automation/rules/LESSON_REMINDER",
            new { enabled = true, parameters = new Dictionary<string, int> { ["unknown"] = 1 } })).StatusCode);

        var run = await superAdmin.PostAsync("/api/automation/run", null);
        Assert.Equal(HttpStatusCode.OK, run.StatusCode);
    }

    [Fact]
    public async Task AbsenceThreshold_ComesFromTheRule_AndResetRestoresTheDefault()
    {
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        var teacher = await AuthenticatedClientAsync("teacher@demo-center.test");

        var updated = await superAdmin.PutAsJsonAsync("/api/automation/rules/ABSENCE_FOLLOW_UP",
            new { enabled = true, parameters = new Dictionary<string, int> { ["consecutiveAbsences"] = 3 } });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var rule = await updated.Content.ReadFromJsonAsync<RuleDto>();
        Assert.True(rule!.IsCustomized);
        Assert.Equal(3, rule.Parameters.Single(p => p.Key == "consecutiveAbsences").Value);

        try
        {
            var branchId = (await admin.GetFromJsonAsync<List<IdDto>>("/api/branches"))![0].Id;
            var teacherId = (await superAdmin.GetFromJsonAsync<PagedResponse<UserDto>>("/api/users?role=TEACHER"))!.Items.First(u => u.Email == "teacher@demo-center.test").Id;
            var course = await (await superAdmin.PostAsJsonAsync("/api/courses", new { name = "Automation Course", levelCategory = "IELTS" })).Content.ReadFromJsonAsync<IdDto>();
            var group = await (await superAdmin.PostAsJsonAsync("/api/groups",
                new { branchId, courseId = course!.Id, name = $"AUTO-{Guid.NewGuid():N}", capacity = 10 })).Content.ReadFromJsonAsync<IdDto>();
            await superAdmin.PostAsJsonAsync($"/api/groups/{group!.Id}/staff", new { userId = teacherId, role = "MainTeacher" });

            var lead = await (await operatorClient.PostAsJsonAsync("/api/leads", new
            {
                branchId, fullName = "Automation Student", phone = "+998900000011", source = "Website", interestedCourse = "IELTS"
            })).Content.ReadFromJsonAsync<IdDto>();
            var converted = await operatorClient.PostAsJsonAsync($"/api/leads/{lead!.Id}/convert",
                new { groupId = group.Id, email = $"automation-{Guid.NewGuid():N}@demo-center.test", password = DbSeeder.DemoPassword });
            var studentId = (await converted.Content.ReadFromJsonAsync<IdDto>())!.Id;

            var start = DateTimeOffset.UtcNow.Date.AddDays(Random.Shared.Next(400, 4000)).AddMinutes(Random.Shared.Next(0, 1200));
            var lessons = new List<Guid>();
            for (var i = 0; i < 3; i++)
            {
                var response = await superAdmin.PostAsJsonAsync("/api/schedule/lessons", new
                {
                    branchId, groupId = group.Id, teacherId, roomName = $"A-{Guid.NewGuid():N}"[..8],
                    startsAt = start.AddDays(2 * i), endsAt = start.AddDays(2 * i).AddMinutes(90)
                });
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                lessons.Add((await response.Content.ReadFromJsonAsync<IdDto>())!.Id);
            }

            await MarkAbsentAsync(teacher, lessons[0], studentId);
            await MarkAbsentAsync(teacher, lessons[1], studentId);
            Assert.Empty((await admin.GetFromJsonAsync<PagedResponse<SupportCaseDto>>($"/api/support-cases?studentId={studentId}"))!.Items);

            await MarkAbsentAsync(teacher, lessons[2], studentId);
            var supportCase = Assert.Single((await admin.GetFromJsonAsync<PagedResponse<SupportCaseDto>>($"/api/support-cases?studentId={studentId}"))!.Items);
            Assert.Contains("3", supportCase.Reason);

            var runs = (await superAdmin.GetFromJsonAsync<List<RunDto>>("/api/automation/runs"))!;
            Assert.Contains(runs, r => r.RuleCode == "ABSENCE_FOLLOW_UP" && r.Trigger == "Event" && r.ItemCount > 0);
        }
        finally
        {
            await superAdmin.DeleteAsync("/api/automation/rules/ABSENCE_FOLLOW_UP");
        }

        var reset = (await superAdmin.GetFromJsonAsync<List<RuleDto>>("/api/automation/rules"))!.Single(r => r.Code == "ABSENCE_FOLLOW_UP");
        Assert.False(reset.IsCustomized);
        Assert.Equal(2, reset.Parameters.Single(p => p.Key == "consecutiveAbsences").Value);
    }

    [Fact]
    public async Task SwitchingOffTheDebtTaskAction_StopsDebtTasks_UntilTheRuleIsRestored()
    {
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        var branchId = (await admin.GetFromJsonAsync<List<IdDto>>("/api/branches"))![0].Id;

        var course = await (await admin.PostAsJsonAsync("/api/courses",
            new { name = $"Auto Debt {Guid.NewGuid():N}"[..20], levelCategory = "A2", monthlyFee = 250000 })).Content.ReadFromJsonAsync<IdDto>();
        var group = await (await admin.PostAsJsonAsync("/api/groups",
            new { branchId, courseId = course!.Id, name = $"ADT-{Guid.NewGuid():N}", capacity = 10 })).Content.ReadFromJsonAsync<IdDto>();
        var lead = await (await operatorClient.PostAsJsonAsync("/api/leads", new
        {
            branchId, fullName = "Auto Debt Student", phone = "+998900000012", source = "Website", interestedCourse = "A2"
        })).Content.ReadFromJsonAsync<IdDto>();
        var converted = await operatorClient.PostAsJsonAsync($"/api/leads/{lead!.Id}/convert",
            new { groupId = group!.Id, email = $"auto-debt-{Guid.NewGuid():N}@demo-center.test", password = DbSeeder.DemoPassword });
        var studentId = (await converted.Content.ReadFromJsonAsync<IdDto>())!.Id;
        var later = DateTimeOffset.UtcNow.AddDays(5);

        Assert.Equal(HttpStatusCode.OK, (await superAdmin.PutAsJsonAsync("/api/automation/rules/DEBT_FOLLOW_UP",
            new { enabled = true, actions = new Dictionary<string, bool> { ["createTask"] = false } })).StatusCode);
        try
        {
            await RunBillingAsync(later, studentId);
            Assert.DoesNotContain((await operatorClient.GetFromJsonAsync<PagedResponse<TaskDto>>("/api/crm/tasks?size=200"))!.Items, t => t.StudentId == studentId);
            // The books still move: the student is Debt regardless of the follow-up rule.
            Assert.Equal("Debt", (await admin.GetFromJsonAsync<StudentDto>($"/api/students/{studentId}"))!.Status);
        }
        finally
        {
            await superAdmin.DeleteAsync("/api/automation/rules/DEBT_FOLLOW_UP");
        }

        await RunBillingAsync(later, studentId);
        Assert.Single((await operatorClient.GetFromJsonAsync<PagedResponse<TaskDto>>("/api/crm/tasks?size=200"))!.Items, t => t.StudentId == studentId);
        Assert.Contains((await superAdmin.GetFromJsonAsync<List<RunDto>>("/api/automation/runs"))!, r => r.RuleCode == "DEBT_FOLLOW_UP");
    }

    // ---- helpers -------------------------------------------------------------------------------

    private async Task RunBillingAsync(DateTimeOffset now, Guid studentId)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<BillingRules>().RunAsync(now, studentId: studentId);
    }

    private static async Task MarkAbsentAsync(HttpClient teacher, Guid lessonId, Guid studentId)
    {
        var response = await teacher.PostAsJsonAsync($"/api/lessons/{lessonId}/attendance",
            new { items = new[] { new { studentId, status = "Absent" } } });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
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
    private record IdDto(Guid Id);
    private record UserDto(Guid Id, string Email);
    private record StudentDto(Guid Id, string Status);
    private record TaskDto(Guid Id, Guid? StudentId, string? ReasonCode);
    private record SupportCaseDto(Guid Id, string Reason, string Status);
    private record ParameterDto(string Key, int Value, int Default);
    private record RuleDto(string Code, bool Enabled, bool IsCustomized, List<ParameterDto> Parameters);
    private record RunDto(Guid Id, string RuleCode, string Trigger, int ItemCount, string Summary);
}
