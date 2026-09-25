using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduCenter.Api.Notifications;
using EduCenter.Api.Notifications.Delivery;
using EduCenter.Infrastructure;
using EduCenter.Infrastructure.Seed;
using EduCenter.Modules.Notifications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// Email and SMS (docs/21.09-audit-jurnali S06.03). The channels are the test host's fakes: nothing here can reach a mail server or the paid SMS
/// gateway. An exam result is the trigger — it is one of the four types mailed by default, and reaches both the student and the parent.
/// </summary>
[Collection("Api")]
public class NotificationDeliveryTests(EduCenterWebApplicationFactory factory) : IDisposable
{
    private FakeNotificationSender Email => factory.FakeEmail;
    private FakeNotificationSender Sms => factory.FakeSms;

    public void Dispose()
    {
        Email.Reset();
        Sms.Reset();
    }

    [Fact]
    public async Task AnEnabledChannel_SendsTheMessage_AndRecordsItAsSent()
    {
        Email.IsEnabled = true;
        var (teacher, groupId, studentId) = await ExamWorldAsync();
        var started = DateTimeOffset.UtcNow;

        await RecordExamAsync(teacher, groupId, studentId);

        // The student and their parent both got a mail, addressed to their own mailbox.
        Assert.Equal(new[] { "parent@demo-center.test", "student@demo-center.test" }, Email.Sent.Select(s => s.Target.Email!).Order().ToArray());
        var rows = await DeliveriesAsync(started, NotificationChannel.Email);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, d => Assert.Equal((DeliveryStatus.Sent, 1), (d.Status, d.Attempts)));
        Assert.All(rows, d => Assert.NotNull(d.SentAt));
    }

    [Fact]
    public async Task AChannelThatIsOff_GetsNothing_AndSmsIsOffForEveryTypeByDefault()
    {
        var (teacher, groupId, studentId) = await ExamWorldAsync();
        var started = DateTimeOffset.UtcNow;

        await RecordExamAsync(teacher, groupId, studentId);   // both channels disabled
        Assert.Empty(await DeliveriesAsync(started));
        Assert.Equal(0, Email.Calls);

        Email.IsEnabled = true;
        Sms.IsEnabled = true;
        await RecordExamAsync(teacher, groupId, studentId);   // mail is on by default for exam results; SMS is on for nothing
        Assert.NotEmpty(Email.Sent);
        Assert.Equal(0, Sms.Calls);
        Assert.Empty(await DeliveriesAsync(started, NotificationChannel.Sms));
    }

    [Fact]
    public async Task ASenderThatFails_LeavesTheInboxMessage_AndTheDeliveryFailsAfterThreeAttempts()
    {
        Email.IsEnabled = true;
        Email.Behaviour = _ => SendResult.Failed("SMTP server is down");
        var (teacher, groupId, studentId) = await ExamWorldAsync();
        var student = await ClientAsync("student@demo-center.test");
        var started = DateTimeOffset.UtcNow;

        var examName = await RecordExamAsync(teacher, groupId, studentId);

        Assert.Contains(await InboxAsync(student), n => n.Title.Contains(examName));   // the message is there whatever the mail server did
        var rows = await DeliveriesAsync(started, NotificationChannel.Email);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, d => Assert.Equal((DeliveryStatus.Failed, NotificationDelivery.MaxAttempts, "SMTP server is down"), (d.Status, d.Attempts, d.Error)));
        Assert.Equal(2 * NotificationDelivery.MaxAttempts, Email.Calls);
        Assert.Empty(Email.Sent);
    }

    [Fact]
    public async Task ASenderThatThrows_IsARetriedFailureToo_NotACrashOfTheRequest()
    {
        Email.IsEnabled = true;
        var attempts = 0;
        Email.Behaviour = _ => ++attempts < 2 ? throw new InvalidOperationException("connection reset") : SendResult.Ok();
        var (teacher, groupId, studentId) = await ExamWorldAsync();
        var started = DateTimeOffset.UtcNow;

        await RecordExamAsync(teacher, groupId, studentId);   // still 201: a delivery problem is not the teacher's problem

        // The first recipient's first attempt threw and the second attempt worked; the other recipient's went through first time.
        var rows = await DeliveriesAsync(started, NotificationChannel.Email);
        Assert.All(rows, d => Assert.Equal(DeliveryStatus.Sent, d.Status));
        Assert.Contains(rows, d => d.Attempts == 2);
    }

    [Fact]
    public async Task TheSameDedupeKey_IsDeliveredOnce_OnEachChannel()
    {
        Email.IsEnabled = true;
        var key = $"test-dedupe:{Guid.NewGuid():N}";

        for (var i = 0; i < 2; i++)
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var student = await db.Users.Where(u => u.Email == "student@demo-center.test").Select(u => new { u.Id, u.OrganizationId }).SingleAsync();
            await scope.ServiceProvider.GetRequiredService<NotificationDispatcher>().NotifyAsync(student.OrganizationId, [student.Id],
                new NotificationMessage(NotificationType.PaymentDue, "To'lov muddati yaqinlashmoqda", "500 000 so'm", "/dashboard", key));
            await db.SaveChangesAsync();
        }

        using var check = factory.Services.CreateScope();
        var rows = await check.ServiceProvider.GetRequiredService<AppDbContext>().NotificationDeliveries.Where(d => d.DedupeKey == key).ToListAsync();
        Assert.Equal(DeliveryStatus.Sent, Assert.Single(rows).Status);
        Assert.Single(Email.Sent);
    }

    [Fact]
    public async Task WhenTheDailySmsAllowanceIsSpent_TheRestAreSkipped()
    {
        Sms.IsEnabled = true;
        var (teacher, groupId, studentId) = await ExamWorldAsync();
        var student = await ClientAsync("student@demo-center.test");
        Assert.Equal(HttpStatusCode.NoContent, (await student.PutAsJsonAsync("/api/notifications/preferences/EXAM_RESULT", new { sms = true })).StatusCode);
        try
        {
            var started = DateTimeOffset.UtcNow;
            for (var i = 0; i < 4; i++) await RecordExamAsync(teacher, groupId, studentId);   // the host's allowance is 2 a day

            var rows = await DeliveriesAsync(started, NotificationChannel.Sms);
            Assert.Equal(4, rows.Count);
            Assert.Equal(2, rows.Count(d => d.Status == DeliveryStatus.Sent));
            Assert.All(rows.Where(d => d.Status == DeliveryStatus.Skipped), d => Assert.Contains("limit", d.Error));
            Assert.Equal(2, Sms.Sent.Count);
        }
        finally
        {
            await student.PutAsJsonAsync("/api/notifications/preferences/EXAM_RESULT", new { sms = false });
        }
    }

    [Fact]
    public async Task WhatLeavesTheApp_IsTheGenericTitleAndALink_NotTheAmountTheNameOrTheMark()
    {
        Email.IsEnabled = true;
        var (teacher, groupId, studentId) = await ExamWorldAsync();

        var examName = await RecordExamAsync(teacher, groupId, studentId);

        var (_, message) = Email.Sent.First();
        Assert.Equal("Imtihon natijasi", message.Subject);
        Assert.Equal($"Imtihon natijasi. Batafsil: https://educenter.test/students/{studentId}", message.Body);
        Assert.DoesNotContain(examName, message.Subject + message.Body);
        Assert.DoesNotContain("Student Demo", message.Body);
        Assert.DoesNotContain("6.5", message.Body);
    }

    [Fact]
    public async Task ASettingIsPerChannel_AndMutingTheInboxDoesNotSilenceTheMail()
    {
        Email.IsEnabled = true;
        var (teacher, groupId, studentId) = await ExamWorldAsync();
        var parent = await ClientAsync("parent@demo-center.test");

        var defaults = (await parent.GetFromJsonAsync<List<PreferenceDto>>("/api/notifications/preferences"))!.Single(p => p.Type == "EXAM_RESULT");
        Assert.Equal((true, true, false), (defaults.Channels.InApp, defaults.Channels.Email, defaults.Channels.Sms));   // the mail default; SMS is off

        Assert.Equal(HttpStatusCode.NoContent, (await parent.PutAsJsonAsync("/api/notifications/preferences/EXAM_RESULT", new { inApp = false })).StatusCode);
        try
        {
            var saved = (await parent.GetFromJsonAsync<List<PreferenceDto>>("/api/notifications/preferences"))!.Single(p => p.Type == "EXAM_RESULT");
            Assert.Equal((false, false, true, false), (saved.InAppEnabled, saved.Channels.InApp, saved.Channels.Email, saved.Channels.Sms));

            var examName = await RecordExamAsync(teacher, groupId, studentId);

            Assert.DoesNotContain(await InboxAsync(parent), n => n.Title.Contains(examName));   // the inbox is muted...
            Assert.Contains(Email.Sent, s => s.Target.Email == "parent@demo-center.test");        // ...the mail is not
        }
        finally
        {
            await parent.PutAsJsonAsync("/api/notifications/preferences/EXAM_RESULT", new { inApp = true });
        }

        // The older single switch still works, and turning the mail off is remembered.
        Assert.Equal(HttpStatusCode.NoContent, (await parent.PutAsJsonAsync("/api/notifications/preferences/EXAM_RESULT", new { email = false, inAppEnabled = true })).StatusCode);
        try
        {
            var mailOff = (await parent.GetFromJsonAsync<List<PreferenceDto>>("/api/notifications/preferences"))!.Single(p => p.Type == "EXAM_RESULT");
            Assert.Equal((true, false), (mailOff.Channels.InApp, mailOff.Channels.Email));

            Email.Sent.Clear();
            await RecordExamAsync(teacher, groupId, studentId);
            Assert.DoesNotContain(Email.Sent, s => s.Target.Email == "parent@demo-center.test");
        }
        finally
        {
            await parent.PutAsJsonAsync("/api/notifications/preferences/EXAM_RESULT", new { email = true });
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await parent.PutAsJsonAsync("/api/notifications/preferences/EXAM_RESULT", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await parent.PutAsJsonAsync("/api/notifications/preferences/TASK_ASSIGNED", new { email = true })).StatusCode);
    }

    [Fact]
    public async Task TheSettingsScreen_IsToldWhichChannelsCanReachThePerson()
    {
        var student = await ClientAsync("student@demo-center.test");

        var off = (await student.GetFromJsonAsync<ChannelsDto>("/api/notifications/channels"))!;
        Assert.False(off.Email.Available);
        Assert.Contains("sozlanmagan", off.Email.Reason);
        Assert.False(off.Sms.Available);

        Email.IsEnabled = true;
        Sms.IsEnabled = true;
        var on = (await student.GetFromJsonAsync<ChannelsDto>("/api/notifications/channels"))!;
        Assert.True(on.Email.Available);
        Assert.True(on.Sms.Available);   // the demo student has an email and a phone number
    }

    // ---- helpers ----------------------------------------------------------------------------

    private async Task<(HttpClient Teacher, Guid GroupId, Guid StudentId)> ExamWorldAsync()
    {
        var teacher = await ClientAsync("teacher@demo-center.test");
        var student = await ClientAsync("student@demo-center.test");
        var studentId = (await student.GetFromJsonAsync<PagedResponse<IdDto>>("/api/students?size=200"))!.Items.Single().Id;
        var groupId = (await student.GetFromJsonAsync<List<IdDto>>("/api/groups"))!.Single().Id;
        return (teacher, groupId, studentId);
    }

    private static async Task<string> RecordExamAsync(HttpClient teacher, Guid groupId, Guid studentId)
    {
        var name = $"Delivery Mock {Guid.NewGuid():N}"[..22];
        var exam = await (await teacher.PostAsJsonAsync("/api/exams",
            new { groupId, name, type = "MockIelts", date = DateOnly.FromDateTime(DateTime.UtcNow) })).Content.ReadFromJsonAsync<IdDto>();
        var result = await teacher.PostAsJsonAsync($"/api/exams/{exam!.Id}/results", new { studentId, overall = 6.5 });
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);
        return name;
    }

    private async Task<List<NotificationDelivery>> DeliveriesAsync(DateTimeOffset since, NotificationChannel? channel = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var query = db.NotificationDeliveries.AsNoTracking().Where(d => d.CreatedAt >= since);
        if (channel is not null) query = query.Where(d => d.Channel == channel);
        return await query.OrderBy(d => d.CreatedAt).ToListAsync();
    }

    private static async Task<List<NotificationDto>> InboxAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<NotificationDto>>("/api/notifications?limit=100"))!;

    private async Task<HttpClient> ClientAsync(string email)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = DbSeeder.DemoPassword });
        var tokens = await response.Content.ReadFromJsonAsync<TokenResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        return client;
    }

    private record TokenResponseDto(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken);
    private record IdDto(Guid Id);
    private record ChannelChoices(bool InApp, bool Email, bool Sms);
    private record PreferenceDto(string Type, bool InAppEnabled, ChannelChoices Channels);
    private record ChannelState(bool Available, string? Reason);
    private record ChannelsDto(ChannelState Email, ChannelState Sms);
    private record NotificationDto(Guid Id, string Type, string Title, string Body, string? Link, DateTimeOffset? ReadAt);
}
