using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduCenter.Api.Automation;
using EduCenter.Infrastructure.Seed;
using Microsoft.Extensions.DependencyInjection;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// Notification Engine (docs/05_phase3_technical_plan.md Sprint 3): event-driven messages reach the right people
/// only, muted types are skipped, read state is personal, and timer reminders are delivered exactly once.
/// Uses the seeded Student Demo (IELTS-21, whose parent is Parent Demo and whose main teacher is Teacher Demo).
/// </summary>
[Collection("Api")]
public class NotificationsTests(EduCenterWebApplicationFactory factory)
{
    [Fact]
    public async Task ExamResult_ReachesStudentAndParent_MutedTypeIsSkipped_AndReadStateIsPersonal()
    {
        var teacher = await AuthenticatedClientAsync("teacher@demo-center.test");
        var student = await AuthenticatedClientAsync("student@demo-center.test");
        var parent = await AuthenticatedClientAsync("parent@demo-center.test");
        var studentId = (await student.GetFromJsonAsync<PagedResponse<IdDto>>("/api/students?size=200"))!.Items.Single().Id;
        var groupId = (await student.GetFromJsonAsync<List<IdDto>>("/api/groups"))!.Single().Id;

        var firstExam = await RecordExamAsync(teacher, groupId, studentId);

        var own = Assert.Single(await InboxAsync(student), n => n.Title.Contains(firstExam));
        Assert.Equal("EXAM_RESULT", own.Type);
        Assert.Null(own.ReadAt);
        Assert.Single(await InboxAsync(parent), n => n.Title.Contains(firstExam));
        Assert.DoesNotContain(await InboxAsync(teacher), n => n.Title.Contains(firstExam));

        // Nobody marks someone else's notification; marking your own lowers your unread count.
        Assert.Equal(HttpStatusCode.NotFound, (await parent.PostAsync($"/api/notifications/{own.Id}/read", null)).StatusCode);
        var unreadBefore = (await student.GetFromJsonAsync<CountDto>("/api/notifications/unread-count"))!.Count;
        Assert.Equal(HttpStatusCode.NoContent, (await student.PostAsync($"/api/notifications/{own.Id}/read", null)).StatusCode);
        Assert.Equal(unreadBefore - 1, (await student.GetFromJsonAsync<CountDto>("/api/notifications/unread-count"))!.Count);

        // Settings list only what the role can receive; muting stops delivery for that user only.
        var preferences = (await parent.GetFromJsonAsync<List<PreferenceDto>>("/api/notifications/preferences"))!;
        Assert.Contains(preferences, p => p.Type == "EXAM_RESULT" && p.InAppEnabled);
        Assert.DoesNotContain(preferences, p => p.Type == "TASK_ASSIGNED");
        Assert.Equal(HttpStatusCode.BadRequest, (await parent.PutAsJsonAsync("/api/notifications/preferences/TASK_ASSIGNED", new { inAppEnabled = false })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await parent.PutAsJsonAsync("/api/notifications/preferences/EXAM_RESULT", new { inAppEnabled = false })).StatusCode);
        try
        {
            var secondExam = await RecordExamAsync(teacher, groupId, studentId);
            Assert.Single(await InboxAsync(student), n => n.Title.Contains(secondExam));
            Assert.DoesNotContain(await InboxAsync(parent), n => n.Title.Contains(secondExam));
        }
        finally
        {
            await parent.PutAsJsonAsync("/api/notifications/preferences/EXAM_RESULT", new { inAppEnabled = true });
        }

        await student.PostAsync("/api/notifications/read-all", null);
        Assert.Equal(0, (await student.GetFromJsonAsync<CountDto>("/api/notifications/unread-count"))!.Count);
    }

    [Fact]
    public async Task Absence_NewHomework_SupportProposalAndAssignment_NotifyTheirAudiences()
    {
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var teacher = await AuthenticatedClientAsync("teacher@demo-center.test");
        var support = await AuthenticatedClientAsync("support@demo-center.test");
        var student = await AuthenticatedClientAsync("student@demo-center.test");
        var parent = await AuthenticatedClientAsync("parent@demo-center.test");

        var studentId = (await student.GetFromJsonAsync<PagedResponse<IdDto>>("/api/students?size=200"))!.Items.Single().Id;
        var group = (await student.GetFromJsonAsync<List<GroupDto>>("/api/groups"))!.Single();
        var teacherId = (await superAdmin.GetFromJsonAsync<PagedResponse<UserDto>>("/api/users?role=TEACHER"))!.Items.First(u => u.Email == "teacher@demo-center.test").Id;
        var supportId = (await superAdmin.GetFromJsonAsync<PagedResponse<UserDto>>("/api/users?role=SUPPORT_TEACHER"))!.Items.First(u => u.Email == "support@demo-center.test").Id;

        // A new lesson: the group's students hear about it.
        var topic = $"Notify topic {Guid.NewGuid():N}"[..22];
        var startsAt = DateTimeOffset.UtcNow.Date.AddDays(Random.Shared.Next(400, 4000)).AddMinutes(Random.Shared.Next(0, 1200));
        var lessonResponse = await superAdmin.PostAsJsonAsync("/api/schedule/lessons", new
        {
            branchId = group.BranchId, groupId = group.Id, teacherId, roomName = $"N-{Guid.NewGuid():N}"[..8],
            startsAt, endsAt = startsAt.AddMinutes(90), topic
        });
        Assert.Equal(HttpStatusCode.Created, lessonResponse.StatusCode);
        var lessonId = (await lessonResponse.Content.ReadFromJsonAsync<IdDto>())!.Id;
        Assert.Contains(await InboxAsync(student), n => n.Type == "SCHEDULE_CHANGED" && n.Body.Contains(topic));

        // Absent → the parent is told; the student is not.
        Assert.Equal(HttpStatusCode.NoContent, (await teacher.PostAsJsonAsync($"/api/lessons/{lessonId}/attendance",
            new { items = new[] { new { studentId, status = "Absent" } } })).StatusCode);
        Assert.Contains(await InboxAsync(parent), n => n.Type == "ABSENT" && n.Link == $"/students/{studentId}");
        Assert.DoesNotContain(await InboxAsync(student), n => n.Type == "ABSENT");

        // New homework → enrolled students.
        var title = $"Notify HW {Guid.NewGuid():N}"[..18];
        Assert.Equal(HttpStatusCode.Created, (await teacher.PostAsJsonAsync("/api/homework", new
        {
            lessonId, title, description = (string?)null, type = "Writing", deadline = startsAt.AddDays(3), maxScore = 10
        })).StatusCode);
        Assert.Contains(await InboxAsync(student), n => n.Type == "HOMEWORK_CREATED" && n.Title.Contains(title));

        // Teacher proposes support → the branch administrator is asked to review; assignment → the support teacher.
        var proposal = await teacher.PostAsJsonAsync("/api/support-cases", new { studentId, groupId = group.Id, reason = "Writing'da grammatik xatolar ko'p" });
        Assert.Equal(HttpStatusCode.Created, proposal.StatusCode);
        var caseId = (await proposal.Content.ReadFromJsonAsync<IdDto>())!.Id;
        Assert.Contains(await InboxAsync(admin), n => n.Type == "SUPPORT_RECOMMENDED" && n.Link == "/support");

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/support-cases/{caseId}/assign", new { supportTeacherId = supportId })).StatusCode);
        Assert.Contains(await InboxAsync(support), n => n.Type == "SUPPORT_ASSIGNED");
        await support.PostAsJsonAsync($"/api/support-cases/{caseId}/resolve", new { note = "Test yakuni" });
    }

    [Fact]
    public async Task PaymentReminders_AreDeliveredOnce_AndOperatorIsToldAboutTheDebtTask()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        var branchId = (await admin.GetFromJsonAsync<List<IdDto>>("/api/branches"))![0].Id;

        var course = await (await admin.PostAsJsonAsync("/api/courses",
            new { name = $"Notify {Guid.NewGuid():N}"[..20], levelCategory = "A2", monthlyFee = 300000 })).Content.ReadFromJsonAsync<IdDto>();
        var group = await (await admin.PostAsJsonAsync("/api/groups",
            new { branchId, courseId = course!.Id, name = $"NTF-{Guid.NewGuid():N}", capacity = 10 })).Content.ReadFromJsonAsync<IdDto>();
        var name = $"Notify Student {Guid.NewGuid():N}"[..23];
        var lead = await (await operatorClient.PostAsJsonAsync("/api/leads", new
        {
            branchId, fullName = name, phone = "+998900000022", source = "Website", interestedCourse = "A2"
        })).Content.ReadFromJsonAsync<IdDto>();
        var email = $"notify-{Guid.NewGuid():N}@demo-center.test";
        var converted = await operatorClient.PostAsJsonAsync($"/api/leads/{lead!.Id}/convert", new { groupId = group!.Id, email, password = DbSeeder.DemoPassword });
        Assert.Equal(HttpStatusCode.Created, converted.StatusCode);
        var studentId = (await converted.Content.ReadFromJsonAsync<IdDto>())!.Id;
        var student = await AuthenticatedClientAsync(email);

        // Due today → one "payment due" reminder, however many sweeps run.
        await RunNotificationsAsync(DateTimeOffset.UtcNow, studentId);
        await RunNotificationsAsync(DateTimeOffset.UtcNow, studentId);
        var due = Assert.Single(await InboxAsync(student), n => n.Type == "PAYMENT_DUE");
        Assert.Contains("300 000", due.Body);

        // Five days later: overdue past the 3-day threshold → one overdue reminder, and the debt task tells the operator.
        var later = DateTimeOffset.UtcNow.AddDays(5);
        await RunNotificationsAsync(later, studentId);
        await RunNotificationsAsync(later, studentId);
        Assert.Single(await InboxAsync(student), n => n.Type == "PAYMENT_OVERDUE");

        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<BillingRules>().RunAsync(later, studentId: studentId);
        Assert.Contains(await InboxAsync(operatorClient), n => n.Type == "TASK_ASSIGNED" && n.Body.Contains(name));
    }

    // ---- helpers -------------------------------------------------------------------------------

    private async Task RunNotificationsAsync(DateTimeOffset now, Guid studentId)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<NotificationRules>().RunAsync(now, studentId);
    }

    private static async Task<string> RecordExamAsync(HttpClient teacher, Guid groupId, Guid studentId)
    {
        var name = $"Notify Mock {Guid.NewGuid():N}"[..20];
        var exam = await (await teacher.PostAsJsonAsync("/api/exams",
            new { groupId, name, type = "MockIelts", date = DateOnly.FromDateTime(DateTime.UtcNow) })).Content.ReadFromJsonAsync<IdDto>();
        var result = await teacher.PostAsJsonAsync($"/api/exams/{exam!.Id}/results", new { studentId, overall = 6.5 });
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);
        return name;
    }

    private static async Task<List<NotificationDto>> InboxAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<NotificationDto>>("/api/notifications?limit=100"))!;

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
    private record GroupDto(Guid Id, Guid BranchId);
    private record UserDto(Guid Id, string Email);
    private record CountDto(int Count);
    private record PreferenceDto(string Type, bool InAppEnabled);
    private record NotificationDto(Guid Id, string Type, string Title, string Body, string? Link, DateTimeOffset? ReadAt);
}
