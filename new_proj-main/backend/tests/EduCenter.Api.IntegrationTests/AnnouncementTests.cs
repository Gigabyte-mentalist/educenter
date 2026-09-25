using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduCenter.Api.Announcements;
using EduCenter.Infrastructure;
using EduCenter.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// Announcements (docs/21.09-audit-jurnali S06.05): who publishes, and who reads what — by where it applies (the organization, a branch, a group)
/// and by audience. The people are the demo accounts (Chilonzor's administrator, its student, parent and teacher) and a student made for the
/// occasion in a branch of their own and in a group of their own.
/// </summary>
[Collection("Api")]
public class AnnouncementTests(EduCenterWebApplicationFactory factory)
{
    [Fact]
    public async Task TheAdministrator_AnnouncesToTheirBranch_ItsStudentsSeeIt_AndAnotherBranchDoesNot()
    {
        var w = await BuildAsync();

        var created = await PublishAsync(w.Admin, new { title = Unique("Bayram"), body = "Dam olish kuni.", audience = "All", branchId = w.Branch });

        Assert.Contains(await ListAsync(w.Student), a => a.Id == created.Id);          // Chilonzor's own demo student
        Assert.Contains(await ListAsync(w.GroupStudent), a => a.Id == created.Id);     // ...and the one made for this test, same branch
        Assert.DoesNotContain(await ListAsync(w.OtherBranchStudent), a => a.Id == created.Id);
        Assert.Equal(w.Branch, created.BranchId);
        Assert.True(created.CanManage);
        Assert.Contains(await ListAsync(w.Admin), a => a.Id == created.Id && a.CanManage);
        Assert.DoesNotContain(await ListAsync(w.Student), a => a.CanManage);   // a reader never gets edit buttons
    }

    [Fact]
    public async Task AGroupAnnouncement_IsForThatGroupsMembersOnly()
    {
        var w = await BuildAsync();

        var created = await PublishAsync(w.Admin, new { title = Unique("Guruh"), body = "Ertaga yig'ilish.", audience = "All", groupId = w.Group });

        Assert.Equal(w.Branch, created.BranchId);   // a group carries its branch
        Assert.Contains(await ListAsync(w.GroupStudent), a => a.Id == created.Id);
        Assert.DoesNotContain(await ListAsync(w.Student), a => a.Id == created.Id);              // same branch, another group
        Assert.DoesNotContain(await ListAsync(w.OtherBranchStudent), a => a.Id == created.Id);
        Assert.DoesNotContain(await ListAsync(w.Parent), a => a.Id == created.Id);               // the demo parent's child is in another group
        Assert.Contains(await ListAsync(w.Admin), a => a.Id == created.Id);                      // the branch's own manager sees its groups' too
    }

    [Fact]
    public async Task TheAudience_DecidesWhoReadsIt()
    {
        var w = await BuildAsync();
        var forParents = await PublishAsync(w.Admin, new { title = Unique("Ota-onalar"), body = "Yig'ilish.", audience = "Parents", branchId = w.Branch });
        var forStudents = await PublishAsync(w.Admin, new { title = Unique("Studentlar"), body = "Imtihon.", audience = "Students", branchId = w.Branch });
        var forStaff = await PublishAsync(w.Admin, new { title = Unique("Xodimlar"), body = "Kengash.", audience = "Staff", branchId = w.Branch });

        var parent = await ListAsync(w.Parent);
        var student = await ListAsync(w.Student);
        var teacher = await ListAsync(w.Teacher);
        Assert.Equal((true, false, false), (parent.Any(a => a.Id == forParents.Id), parent.Any(a => a.Id == forStudents.Id), parent.Any(a => a.Id == forStaff.Id)));
        Assert.Equal((false, true, false), (student.Any(a => a.Id == forParents.Id), student.Any(a => a.Id == forStudents.Id), student.Any(a => a.Id == forStaff.Id)));
        Assert.Equal((false, false, true), (teacher.Any(a => a.Id == forParents.Id), teacher.Any(a => a.Id == forStudents.Id), teacher.Any(a => a.Id == forStaff.Id)));

        // Whoever manages announcements sees what they addressed to others too — they have to be able to find it to change it.
        var admin = await ListAsync(w.Admin);
        Assert.All(new[] { forParents, forStudents, forStaff }, a => Assert.Contains(admin, x => x.Id == a.Id && x.CanManage));
    }

    [Fact]
    public async Task AnExpiredAnnouncement_IsGoneForEveryone_AndAPastExpiryCannotBeSet()
    {
        var w = await BuildAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await w.Admin.PostAsJsonAsync("/api/announcements",
            new { title = "Eskirgan", body = "x", audience = "All", branchId = w.Branch, expiresAt = DateTimeOffset.UtcNow.AddMinutes(-5) })).StatusCode);

        var created = await PublishAsync(w.Admin, new
        {
            title = Unique("Muddatli"), body = "Bir soatlik.", audience = "All", branchId = w.Branch, expiresAt = DateTimeOffset.UtcNow.AddHours(1),
        });
        Assert.Contains(await ListAsync(w.Student), a => a.Id == created.Id);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Announcements.Where(a => a.Id == created.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }

        Assert.DoesNotContain(await ListAsync(w.Student), a => a.Id == created.Id);
        Assert.DoesNotContain(await ListAsync(w.Admin), a => a.Id == created.Id);
    }

    [Fact]
    public async Task OnlyThoseWhoRunABranchPublish_AndAnAdministratorOnlyForTheirOwn()
    {
        var w = await BuildAsync();
        var body = new { title = "Ruxsatsiz", body = "x", audience = "All", branchId = w.Branch };

        foreach (var who in new[] { w.Teacher, w.Student, w.Parent })
            Assert.Equal(HttpStatusCode.Forbidden, (await who.PostAsJsonAsync("/api/announcements", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await ClientAsync("operator@demo-center.test")).PostAsJsonAsync("/api/announcements", body)).StatusCode);

        // The administrator's scope is their own branches: not another branch, not another branch's group, not the whole organization.
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Admin.PostAsJsonAsync("/api/announcements", new { title = "Boshqa filial", body = "x", audience = "All", branchId = w.OtherBranch })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Admin.PostAsJsonAsync("/api/announcements", new { title = "Boshqa guruh", body = "x", audience = "All", groupId = w.OtherGroup })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Admin.PostAsJsonAsync("/api/announcements", new { title = "Hammaga", body = "x", audience = "All" })).StatusCode);

        // The organization's manager may address all of it.
        var everyone = await PublishAsync(w.SuperAdmin, new { title = Unique("Hammaga"), body = "Yangi yil.", audience = "All" });
        Assert.Null(everyone.BranchId);
        Assert.Contains(await ListAsync(w.Student), a => a.Id == everyone.Id);
        Assert.Contains(await ListAsync(w.OtherBranchStudent), a => a.Id == everyone.Id);
        Assert.Contains(await ListAsync(w.Parent), a => a.Id == everyone.Id);
    }

    [Fact]
    public async Task ThePeopleItIsFor_AreToldOnce_AndNobodyElseIs()
    {
        var w = await BuildAsync();
        var inboxBefore = await AnnouncementNoticesAsync(w.GroupStudent);

        var created = await PublishAsync(w.Admin, new { title = Unique("Xabar"), body = "Diqqat!", audience = "Students", groupId = w.Group });

        var told = (await AnnouncementNoticesAsync(w.GroupStudent)).Where(n => n.Title == created.Title).ToList();
        Assert.Equal(inboxBefore.Count + 1, (await AnnouncementNoticesAsync(w.GroupStudent)).Count);
        Assert.Equal("/announcements", Assert.Single(told).Link);

        // A second run of the same announcement (a retried job) tells no one twice.
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AnnouncementNotifier>().NotifyAsync(created.Id);
        }
        Assert.Equal(inboxBefore.Count + 1, (await AnnouncementNoticesAsync(w.GroupStudent)).Count);

        Assert.DoesNotContain(await AnnouncementNoticesAsync(w.Student), n => n.Title == created.Title);   // another group
        Assert.DoesNotContain(await AnnouncementNoticesAsync(w.Parent), n => n.Title == created.Title);    // audience: students
        Assert.DoesNotContain(await AnnouncementNoticesAsync(w.Admin), n => n.Title == created.Title);     // the author is not told of their own
    }

    [Fact]
    public async Task TheList_IsPaged_NewestFirst()
    {
        var w = await BuildAsync();
        var titles = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            titles.Add((await PublishAsync(w.Admin, new { title = Unique($"Sahifa {i}"), body = "x", audience = "Students", groupId = w.Group })).Title);
            await Task.Delay(15);   // PublishedAt is a timestamp: keep the three apart
        }

        var first = (await w.GroupStudent.GetFromJsonAsync<PagedResponse<AnnouncementRow>>("/api/announcements?size=2&page=1"))!;
        var second = (await w.GroupStudent.GetFromJsonAsync<PagedResponse<AnnouncementRow>>("/api/announcements?size=2&page=2"))!;

        Assert.Equal(2, first.Items.Count);
        Assert.True(first.Total >= 3);
        Assert.Equal(new[] { titles[2], titles[1] }, first.Items.Select(a => a.Title).ToArray());
        Assert.Equal(titles[0], second.Items.First().Title);
    }

    [Fact]
    public async Task ItCanBeEditedAndTaken_ByThoseWhoRunItsBranch_NotByOthers()
    {
        var w = await BuildAsync();
        var created = await PublishAsync(w.Admin, new
        {
            title = Unique("Tahrir"), body = "Birinchi variant.", audience = "Students", branchId = w.Branch, expiresAt = DateTimeOffset.UtcNow.AddDays(3),
        });

        var edited = await w.Admin.PatchAsJsonAsync($"/api/announcements/{created.Id}", new { body = "Ikkinchi variant.", audience = "All", clearExpiry = true });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var dto = (await edited.Content.ReadFromJsonAsync<AnnouncementRow>())!;
        Assert.Equal(("Ikkinchi variant.", "All", created.Title), (dto.Body, dto.Audience, dto.Title));
        Assert.Null(dto.ExpiresAt);
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Admin.PatchAsJsonAsync($"/api/announcements/{created.Id}", new { title = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Admin.PatchAsJsonAsync($"/api/announcements/{created.Id}", new { expiresAt = DateTimeOffset.UtcNow.AddDays(-1) })).StatusCode);

        // Another branch's announcement is not this administrator's to touch; a teacher's is nobody's.
        var elsewhere = await PublishAsync(w.SuperAdmin, new { title = Unique("Boshqa"), body = "x", audience = "All", branchId = w.OtherBranch });
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Admin.PatchAsJsonAsync($"/api/announcements/{elsewhere.Id}", new { title = "Buzildi" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Admin.DeleteAsync($"/api/announcements/{elsewhere.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Teacher.DeleteAsync($"/api/announcements/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await w.Admin.DeleteAsync($"/api/announcements/{Guid.NewGuid()}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await w.Admin.DeleteAsync($"/api/announcements/{created.Id}")).StatusCode);
        Assert.DoesNotContain(await ListAsync(w.Student), a => a.Id == created.Id);
    }

    [Fact]
    public async Task ATitleAndATextAreRequired_AndBounded()
    {
        var w = await BuildAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await w.Admin.PostAsJsonAsync("/api/announcements", new { title = " ", body = "x", audience = "All", branchId = w.Branch })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Admin.PostAsJsonAsync("/api/announcements", new { title = "x", body = "", audience = "All", branchId = w.Branch })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Admin.PostAsJsonAsync("/api/announcements", new { title = new string('x', 201), body = "x", audience = "All", branchId = w.Branch })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Admin.PostAsJsonAsync("/api/announcements", new { title = "x", body = new string('x', 4001), audience = "All", branchId = w.Branch })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Admin.PostAsJsonAsync("/api/announcements", new { title = "x", body = "x", audience = "Everyone", branchId = w.Branch })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Admin.PostAsJsonAsync("/api/announcements", new { title = "x", body = "x", audience = "All", branchId = w.Branch, groupId = w.OtherGroup })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/announcements")).StatusCode);
    }

    // ---- the world ---------------------------------------------------------------------------

    private sealed record World(
        HttpClient SuperAdmin, HttpClient Admin, HttpClient Teacher, HttpClient Student, HttpClient Parent,
        HttpClient GroupStudent, HttpClient OtherBranchStudent,
        Guid Branch, Guid OtherBranch, Guid Group, Guid OtherGroup);

    /// <summary>Chilonzor (the administrator's branch) with a group of its own holding one new student, and another branch with a group and a student.</summary>
    private async Task<World> BuildAsync()
    {
        var superAdmin = await ClientAsync("superadmin@demo-center.test");
        var admin = await ClientAsync("admin@demo-center.test");
        var branches = (await superAdmin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))!;
        var branch = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var otherBranch = branches.First(b => b.Id != branch).Id;

        var group = await NewGroupAsync(superAdmin, branch);
        var otherGroup = await NewGroupAsync(superAdmin, otherBranch);
        var groupStudent = await NewStudentClientAsync(superAdmin, group);
        var otherStudent = await NewStudentClientAsync(superAdmin, otherGroup);

        return new World(superAdmin, admin, await ClientAsync("teacher@demo-center.test"), await ClientAsync("student@demo-center.test"),
            await ClientAsync("parent@demo-center.test"), groupStudent, otherStudent, branch, otherBranch, group, otherGroup);
    }

    private static string Unique(string what) => $"{what} {Guid.NewGuid():N}"[..Math.Min(60, what.Length + 9)];

    private static async Task<AnnouncementRow> PublishAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/announcements", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AnnouncementRow>())!;
    }

    private static async Task<List<AnnouncementRow>> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<PagedResponse<AnnouncementRow>>("/api/announcements?size=200"))!.Items;

    private static async Task<List<NoticeRow>> AnnouncementNoticesAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<NoticeRow>>("/api/notifications?limit=100"))!.Where(n => n.Type == "ANNOUNCEMENT").ToList();

    private static async Task<Guid> NewGroupAsync(HttpClient superAdmin, Guid branchId)
    {
        var course = await superAdmin.PostAsJsonAsync("/api/courses", new { name = $"Ann Course {Guid.NewGuid():N}"[..25], levelCategory = "General" });
        Assert.Equal(HttpStatusCode.Created, course.StatusCode);
        var group = await superAdmin.PostAsJsonAsync("/api/groups", new
        {
            branchId, courseId = (await course.Content.ReadFromJsonAsync<IdDto>())!.Id, name = $"ANN-{Guid.NewGuid():N}"[..20], capacity = 20,
        });
        Assert.Equal(HttpStatusCode.Created, group.StatusCode);
        return (await group.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    /// <summary>A student in the group, signed in — after the forced first-session password change of an account made through the API (S01.05).</summary>
    private async Task<HttpClient> NewStudentClientAsync(HttpClient superAdmin, Guid groupId)
    {
        var email = $"ann-{Guid.NewGuid():N}@demo-center.test";
        var response = await superAdmin.PostAsJsonAsync("/api/students", new
        {
            // Named to sort last in the student list, which other tests read the first page of.
            fullName = "Zz Announcement Student", phone = "+998900000075", email, password = DbSeeder.DemoPassword, groupId,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ClientAsync(email);
    }

    private async Task<HttpClient> ClientAsync(string email)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = DbSeeder.DemoPassword });
        var tokens = (await response.Content.ReadFromJsonAsync<TokenDto>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        if (tokens.MustChangePassword)
        {
            var change = await client.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = DbSeeder.DemoPassword, newPassword = "Chosen-Pass-42" });
            Assert.Equal(HttpStatusCode.OK, change.StatusCode);
            tokens = (await change.Content.ReadFromJsonAsync<TokenDto>())!;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        }

        return client;
    }

    private record TokenDto(string AccessToken, bool MustChangePassword);
    private record IdDto(Guid Id);
    private record BranchDto(Guid Id, string Name);
    private record NoticeRow(Guid Id, string Type, string Title, string? Link);
    private record AnnouncementRow(
        Guid Id, string Title, string Body, string Audience, Guid? BranchId, string? BranchName, Guid? GroupId, string? GroupName,
        DateTimeOffset? ExpiresAt, bool CanManage);
}
