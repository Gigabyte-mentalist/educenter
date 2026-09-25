using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EduCenter.Infrastructure.Seed;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// Chat (docs/05_phase3_technical_plan.md Sprint 6): who sees which chat follows matrix §3.8 and the caller's scope,
/// messages arrive in real time over SignalR, and moderation/pinning/reactions/attachments respect the same rules.
/// </summary>
[Collection("Api")]
public class ChatTests(EduCenterWebApplicationFactory factory)
{
    [Fact]
    public async Task GroupChat_IsVisibleToTheGroup_DeliversInRealTime_AndTracksReads()
    {
        var arrange = await ArrangeGroupAsync();
        var operatorClient = await ClientAsync("operator@demo-center.test");

        var teacherChat = (await arrange.Teacher.GetFromJsonAsync<List<ChatItem>>("/api/chats"))!.Single(c => c.GroupId == arrange.GroupId);
        Assert.Equal("Group", teacherChat.Type);
        Assert.Contains((await arrange.Student.GetFromJsonAsync<List<ChatItem>>("/api/chats"))!, c => c.Id == teacherChat.Id);
        Assert.DoesNotContain((await operatorClient.GetFromJsonAsync<List<ChatItem>>("/api/chats"))!, c => c.Id == teacherChat.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await operatorClient.GetAsync($"/api/chats/{teacherChat.Id}/messages")).StatusCode);

        await using var connection = await ConnectAsync(arrange.TeacherToken);
        var received = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<JsonElement>("messageCreated", message => received.TrySetResult(message));
        Assert.True(await connection.InvokeAsync<int>("SyncChats") > 0);

        var sent = await SendAsync(arrange.Student, teacherChat.Id, "Salom, ustoz!");
        var pushed = await received.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(sent.Id, pushed.GetProperty("id").GetGuid());
        Assert.Equal("Salom, ustoz!", pushed.GetProperty("body").GetString());

        Assert.Equal(1, await UnreadAsync(arrange.Teacher, teacherChat.Id));
        Assert.Equal(HttpStatusCode.NoContent, (await arrange.Teacher.PostAsync($"/api/chats/{teacherChat.Id}/read", null)).StatusCode);
        Assert.Equal(0, await UnreadAsync(arrange.Teacher, teacherChat.Id));
        var detail = (await arrange.Student.GetFromJsonAsync<ChatDetail>($"/api/chats/{teacherChat.Id}"))!;
        Assert.Contains(detail.Readers, r => r.UserId == arrange.TeacherId);
        Assert.False(detail.CanModerate);

        // Moderation: the main teacher (chat.moderate MY_GROUPS) pins and removes; the student cannot pin.
        Assert.Equal(HttpStatusCode.Forbidden, (await arrange.Student.PostAsync($"/api/chats/{teacherChat.Id}/messages/{sent.Id}/pin", null)).StatusCode);
        var pinned = await (await arrange.Teacher.PostAsync($"/api/chats/{teacherChat.Id}/messages/{sent.Id}/pin", null)).Content.ReadFromJsonAsync<Message>();
        Assert.NotNull(pinned!.PinnedAt);
        var removed = await (await arrange.Teacher.DeleteAsync($"/api/chats/{teacherChat.Id}/messages/{sent.Id}")).Content.ReadFromJsonAsync<Message>();
        Assert.True(removed!.IsDeleted);
        Assert.Null(removed.Body);
    }

    [Fact]
    public async Task DirectChat_IsLimitedToContacts_AndItsTwoParticipants()
    {
        var arrange = await ArrangeGroupAsync();
        var superAdmin = await ClientAsync("superadmin@demo-center.test");
        var operatorClient = await ClientAsync("operator@demo-center.test");
        var operatorId = (await superAdmin.GetFromJsonAsync<PagedResponse<UserDto>>("/api/users?role=OPERATOR"))!.Items.First(u => u.Email == "operator@demo-center.test").Id;

        var contacts = (await arrange.Student.GetFromJsonAsync<List<Contact>>("/api/chats/contacts"))!;
        Assert.Contains(contacts, c => c.UserId == arrange.TeacherId);
        Assert.DoesNotContain(contacts, c => c.UserId == operatorId);
        Assert.Equal(HttpStatusCode.Forbidden, (await arrange.Student.PostAsJsonAsync("/api/chats/direct", new { userId = operatorId })).StatusCode);

        var chat = await (await arrange.Student.PostAsJsonAsync("/api/chats/direct", new { userId = arrange.TeacherId })).Content.ReadFromJsonAsync<ChatItem>();
        var again = await (await arrange.Teacher.PostAsJsonAsync("/api/chats/direct", new { userId = arrange.StudentUserId })).Content.ReadFromJsonAsync<ChatItem>();
        Assert.Equal(chat!.Id, again!.Id);
        Assert.Equal("Direct", chat.Type);

        var question = await SendAsync(arrange.Student, chat.Id, "Uyga vazifa qaysi sahifadan?");
        using var reply = new MultipartFormDataContent { { new StringContent("12-sahifa"), "Body" }, { new StringContent(question.Id.ToString()), "ReplyToMessageId" } };
        var answer = await (await arrange.Teacher.PostAsync($"/api/chats/{chat.Id}/messages", reply)).Content.ReadFromJsonAsync<Message>();
        Assert.Equal(question.Id, answer!.ReplyTo!.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await operatorClient.GetAsync($"/api/chats/{chat.Id}/messages")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await superAdmin.GetAsync($"/api/chats/{chat.Id}")).StatusCode);

        // No moderation in private chats: only the author deletes; either participant pins.
        Assert.Equal(HttpStatusCode.Forbidden, (await arrange.Teacher.DeleteAsync($"/api/chats/{chat.Id}/messages/{question.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await arrange.Student.DeleteAsync($"/api/chats/{chat.Id}/messages/{question.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await arrange.Student.PostAsync($"/api/chats/{chat.Id}/messages/{answer.Id}/pin", null)).StatusCode);

        var liked = await (await arrange.Teacher.PostAsJsonAsync($"/api/chats/{chat.Id}/messages/{answer.Id}/reactions", new { emoji = "👍" })).Content.ReadFromJsonAsync<Message>();
        Assert.Equal(1, Assert.Single(liked!.Reactions).Count);
        var unliked = await (await arrange.Teacher.PostAsJsonAsync($"/api/chats/{chat.Id}/messages/{answer.Id}/reactions", new { emoji = "👍" })).Content.ReadFromJsonAsync<Message>();
        Assert.Empty(unliked!.Reactions);
        Assert.Equal(HttpStatusCode.BadRequest, (await arrange.Teacher.PostAsJsonAsync($"/api/chats/{chat.Id}/messages/{answer.Id}/reactions", new { emoji = "🔥" })).StatusCode);

        var page = (await arrange.Teacher.GetFromJsonAsync<List<Message>>($"/api/chats/{chat.Id}/messages?search=sahifa"))!;
        Assert.Equal(answer.Id, Assert.Single(page).Id); // the deleted question no longer matches
    }

    [Fact]
    public async Task StaffChannels_FollowRoleAndBranch_AndAnnouncementsAreModeratorOnly()
    {
        var admin = await ClientAsync("admin@demo-center.test");
        var teacher = await ClientAsync("teacher@demo-center.test");
        var operatorClient = await ClientAsync("operator@demo-center.test");
        var student = await ClientAsync("student@demo-center.test");
        var branchId = (await admin.GetFromJsonAsync<List<IdDto>>("/api/branches"))![0].Id;

        List<string?> Kinds(List<ChatItem> chats) => chats.Where(c => c.Type == "StaffChannel" && c.BranchId == branchId).Select(c => c.ChannelKind).ToList();

        var adminKinds = Kinds((await admin.GetFromJsonAsync<List<ChatItem>>("/api/chats"))!);
        Assert.Contains("Administrators", adminKinds);
        Assert.Contains("Announcements", adminKinds);

        var teacherChats = (await teacher.GetFromJsonAsync<List<ChatItem>>("/api/chats"))!;
        Assert.Contains("Teachers", Kinds(teacherChats));
        Assert.DoesNotContain("Administrators", Kinds(teacherChats));

        var operatorKinds = Kinds((await operatorClient.GetFromJsonAsync<List<ChatItem>>("/api/chats"))!);
        Assert.Contains("General", operatorKinds);
        Assert.DoesNotContain("Teachers", operatorKinds);

        Assert.DoesNotContain((await student.GetFromJsonAsync<List<ChatItem>>("/api/chats"))!, c => c.Type == "StaffChannel");

        var announcements = teacherChats.Single(c => c.ChannelKind == "Announcements" && c.BranchId == branchId);
        Assert.False(announcements.CanPost);
        using var teacherPost = new MultipartFormDataContent { { new StringContent("Ertaga yig'ilish"), "Body" } };
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PostAsync($"/api/chats/{announcements.Id}/messages", teacherPost)).StatusCode);
        await SendAsync(admin, announcements.Id, "Ertaga soat 10:00 da umumiy yig'ilish.");
    }

    [Fact]
    public async Task Attachments_AreValidated_AndServedOnlyToThoseWhoCanReadTheChat()
    {
        var arrange = await ArrangeGroupAsync();
        var operatorClient = await ClientAsync("operator@demo-center.test");
        var chatId = (await arrange.Teacher.GetFromJsonAsync<List<ChatItem>>("/api/chats"))!.Single(c => c.GroupId == arrange.GroupId).Id;

        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];
        using var upload = new MultipartFormDataContent();
        var file = new ByteArrayContent(png);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        upload.Add(file, "File", "doska.png");
        var response = await arrange.Student.PostAsync($"/api/chats/{chatId}/messages", upload);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var message = await response.Content.ReadFromJsonAsync<Message>();
        Assert.True(message!.Attachment!.IsImage);
        Assert.Equal("doska.png", message.Attachment.FileName);

        var download = await arrange.Teacher.GetAsync($"/api/chats/{chatId}/messages/{message.Id}/attachment");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(png, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await operatorClient.GetAsync($"/api/chats/{chatId}/messages/{message.Id}/attachment")).StatusCode);

        using var bad = new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "File", "setup.exe" } };
        Assert.Equal(HttpStatusCode.BadRequest, (await arrange.Student.PostAsync($"/api/chats/{chatId}/messages", bad)).StatusCode);
    }

    // ---- helpers -------------------------------------------------------------------------------

    private record Arrangement(Guid GroupId, Guid TeacherId, Guid StudentUserId, HttpClient Teacher, string TeacherToken, HttpClient Student);

    /// <summary>A fresh group with the demo teacher as main teacher and a newly converted student who can log in.</summary>
    private async Task<Arrangement> ArrangeGroupAsync()
    {
        var superAdmin = await ClientAsync("superadmin@demo-center.test");
        var admin = await ClientAsync("admin@demo-center.test");
        var operatorClient = await ClientAsync("operator@demo-center.test");
        var (teacher, teacherToken) = await LoginAsync("teacher@demo-center.test");

        var branchId = (await admin.GetFromJsonAsync<List<IdDto>>("/api/branches"))![0].Id;
        var teacherId = (await superAdmin.GetFromJsonAsync<PagedResponse<UserDto>>("/api/users?role=TEACHER"))!.Items.First(u => u.Email == "teacher@demo-center.test").Id;
        var course = await (await superAdmin.PostAsJsonAsync("/api/courses", new { name = "Chat Course", levelCategory = "B1" })).Content.ReadFromJsonAsync<IdDto>();
        var group = await (await superAdmin.PostAsJsonAsync("/api/groups",
            new { branchId, courseId = course!.Id, name = $"CHAT-{Guid.NewGuid():N}"[..20], capacity = 10 })).Content.ReadFromJsonAsync<IdDto>();
        await superAdmin.PostAsJsonAsync($"/api/groups/{group!.Id}/staff", new { userId = teacherId, role = "MainTeacher" });

        var email = $"chat-{Guid.NewGuid():N}@demo-center.test";
        var lead = await (await operatorClient.PostAsJsonAsync("/api/leads", new
        {
            branchId, fullName = "Chat Student", phone = $"+99890{Random.Shared.Next(1_000_000, 9_999_999)}", source = "Website", interestedCourse = "B1"
        })).Content.ReadFromJsonAsync<IdDto>();
        var converted = await operatorClient.PostAsJsonAsync($"/api/leads/{lead!.Id}/convert", new { groupId = group.Id, email, password = DbSeeder.DemoPassword });
        Assert.True(converted.IsSuccessStatusCode, await converted.Content.ReadAsStringAsync());

        var (student, _) = await LoginAsync(email);
        var studentUserId = (await superAdmin.GetFromJsonAsync<PagedResponse<UserDto>>("/api/users?role=STUDENT"))!.Items.First(u => u.Email == email).Id;
        return new Arrangement(group.Id, teacherId, studentUserId, teacher, teacherToken, student);
    }

    private async Task<HubConnection> ConnectAsync(string token)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "hubs/chat"), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();
        await connection.StartAsync();
        return connection;
    }

    private static async Task<Message> SendAsync(HttpClient client, Guid chatId, string body)
    {
        using var content = new MultipartFormDataContent { { new StringContent(body), "Body" } };
        var response = await client.PostAsync($"/api/chats/{chatId}/messages", content);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Message>())!;
    }

    private static async Task<int> UnreadAsync(HttpClient client, Guid chatId) =>
        (await client.GetFromJsonAsync<List<ChatItem>>("/api/chats"))!.Single(c => c.Id == chatId).UnreadCount;

    private async Task<HttpClient> ClientAsync(string email) => (await LoginAsync(email)).Client;

    private async Task<(HttpClient Client, string Token)> LoginAsync(string email)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = DbSeeder.DemoPassword });
        var tokens = await response.Content.ReadFromJsonAsync<TokenResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        return (client, tokens.AccessToken);
    }

    private record TokenResponseDto(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken);
    private record IdDto(Guid Id);
    private record UserDto(Guid Id, string Email);
    private record Contact(Guid UserId, string FullName, string? RoleCode);
    private record ChatItem(Guid Id, string Type, Guid? GroupId, Guid? BranchId, string? ChannelKind, int UnreadCount, bool CanPost);
    private record Reader(Guid UserId, string FullName, DateTimeOffset LastReadAt);
    private record ChatDetail(ChatItem Chat, bool CanModerate, bool CanPin, List<Reader> Readers);
    private record Reply(Guid Id, string Preview);
    private record Attachment(string FileName, string ContentType, long Size, bool IsImage);
    private record Reaction(string Emoji, int Count, List<Guid> UserIds);
    private record Message(Guid Id, string? Body, bool IsDeleted, Reply? ReplyTo, Attachment? Attachment, List<Reaction> Reactions, DateTimeOffset? PinnedAt);
}
