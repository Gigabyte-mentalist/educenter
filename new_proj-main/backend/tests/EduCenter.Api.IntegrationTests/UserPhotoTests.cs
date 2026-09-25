using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduCenter.Api.Users;
using EduCenter.Infrastructure;
using EduCenter.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// Profile pictures (docs/21.09-audit-jurnali S04.04): an image up to 2 MB per account — never SVG, which can carry script — that
/// its owner or the people who administer the account may change, and that only those who can open the person may see.
/// </summary>
[Collection("Api")]
public class UserPhotoTests(EduCenterWebApplicationFactory factory)
{
    private readonly LmsWorld world = new(factory);
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 9, 8, 7, 6];

    [Fact]
    public async Task Owner_UploadsReplacesAndRemoves_TheirPicture()
    {
        var c = await world.CreateAsync();
        var student = await world.ClientAsync(c.First.Email);
        var teacher = await world.ClientAsync("teacher@demo-center.test");
        Assert.Equal(HttpStatusCode.NotFound, (await student.GetAsync(PhotoUrl(c.First.UserId))).StatusCode); // none yet

        var first = await UploadAsync(student, c.First.UserId, "me.png", Png);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstVersion = (await first.Content.ReadFromJsonAsync<PhotoDto>())!.Version;

        var fetched = await student.GetAsync(PhotoUrl(c.First.UserId));
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        Assert.Equal("image/png", fetched.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Png, await fetched.Content.ReadAsByteArrayAsync());
        Assert.Contains("nosniff", fetched.Headers.GetValues("X-Content-Type-Options"));
        Assert.Contains("max-age=300", fetched.Headers.CacheControl!.ToString());

        // The header of the profile says there is one, and which version — so a client asks for the right address.
        var header = (await OverviewAsync(teacher, c.First.Id)).Header!;
        Assert.True(header.HasPhoto);
        Assert.Equal(firstVersion, header.PhotoVersion);

        var second = await UploadAsync(student, c.First.UserId, "newer.jpg", Jpeg);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.NotEqual(firstVersion, (await second.Content.ReadFromJsonAsync<PhotoDto>())!.Version);
        var replaced = await student.GetAsync(PhotoUrl(c.First.UserId));
        Assert.Equal("image/jpeg", replaced.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Jpeg, await replaced.Content.ReadAsByteArrayAsync());

        Assert.Equal(HttpStatusCode.NoContent, (await student.DeleteAsync(PhotoUrl(c.First.UserId))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await student.DeleteAsync(PhotoUrl(c.First.UserId))).StatusCode); // nothing to remove is fine
        Assert.Equal(HttpStatusCode.NotFound, (await student.GetAsync(PhotoUrl(c.First.UserId))).StatusCode);
        Assert.False((await OverviewAsync(teacher, c.First.Id)).Header!.HasPhoto);
    }

    [Fact]
    public async Task OnlyASmallRasterImageIsAccepted_NotSvgNorAnythingElse()
    {
        var c = await world.CreateAsync();
        var student = await world.ClientAsync(c.First.Email);
        var url = PhotoUrl(c.First.UserId);

        // The upload must be able to reach the limit itself: 2 MB + 1 byte is refused by the rule, not by the server's own cap.
        var tooBig = await UploadAsync(student, c.First.UserId, "big.png", new byte[2 * 1024 * 1024 + 1]);
        Assert.Equal(HttpStatusCode.BadRequest, tooBig.StatusCode);
        Assert.Contains("2 MB", await tooBig.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(student, c.First.UserId, "max.png", TestFiles.Png(2 * 1024 * 1024))).StatusCode);

        foreach (var name in new[] { "virus.exe", "logo.svg", "page.html", "photo.png.exe", "noextension" })
            Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(student, c.First.UserId, name, Png)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(student, c.First.UserId, "empty.png", [])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await student.PostAsync(url, new MultipartFormDataContent())).StatusCode);

        // None of the refusals touched the picture that was accepted.
        Assert.Equal(TestFiles.Png(2 * 1024 * 1024), await (await student.GetAsync(url)).Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task WhoMayChangeAPicture_FollowsWhoMayEditTheAccount()
    {
        var c = await world.CreateAsync(students: 2);
        var classmate = await world.ClientAsync(c.Students[1].Email);
        var teacher = await world.ClientAsync("teacher@demo-center.test");
        var admin = await world.ClientAsync("admin@demo-center.test");
        var superAdmin = await world.ClientAsync("superadmin@demo-center.test");
        var target = c.Students[0].UserId;

        // A classmate and even the student's teacher edit only their own account; the org's Super Admin edits anyone's.
        Assert.Equal(HttpStatusCode.Forbidden, (await UploadAsync(classmate, target, "x.png", Png)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await UploadAsync(teacher, target, "x.png", Png)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await classmate.DeleteAsync(PhotoUrl(target))).StatusCode);
        // The branch Administrator reaches a student of their own branch...
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(admin, target, "office.png", Png)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync(PhotoUrl(target))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(superAdmin, target, "super.png", Png)).StatusCode);

        // ...but not the Super Admin (S01.04): a branch Administrator must not reach someone org-wide, a picture included.
        var superId = await world.UserIdAsync("superadmin@demo-center.test", "SUPER_ADMIN");
        Assert.Equal(HttpStatusCode.Forbidden, (await UploadAsync(admin, superId, "hack.png", Png)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.DeleteAsync(PhotoUrl(superId))).StatusCode);

        // Staff change their own (users.edit at Self scope), and only their own.
        var teacherId = await world.UserIdAsync("teacher@demo-center.test", "TEACHER");
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(teacher, teacherId, "teacher.png", Png)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await UploadAsync(admin, Guid.NewGuid(), "ghost.png", Png)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await UploadAsync(factory.CreateClient(), target, "anon.png", Png)).StatusCode);
    }

    [Fact]
    public async Task WhoMayViewAPicture_FollowsWhoMayOpenThatPerson()
    {
        var c = await world.CreateAsync(students: 2);
        var other = await world.CreateAsync(teacherStaffsGroup: false);
        var owner = await world.ClientAsync(c.Students[0].Email);
        var target = c.Students[0].UserId;
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(owner, target, "me.png", Png)).StatusCode);

        // Those who can open the student's profile see the picture: the student, the teacher, the branch's people, the org.
        foreach (var email in new[] { c.Students[0].Email, "teacher@demo-center.test", "admin@demo-center.test", "operator@demo-center.test", "superadmin@demo-center.test" })
            Assert.Equal(HttpStatusCode.OK, (await (await world.ClientAsync(email)).GetAsync(PhotoUrl(target))).StatusCode);

        // A classmate cannot open the profile, so cannot see the picture; nor can a student of another group.
        Assert.Equal(HttpStatusCode.Forbidden, (await (await world.ClientAsync(c.Students[1].Email)).GetAsync(PhotoUrl(target))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await world.ClientAsync(other.First.Email)).GetAsync(PhotoUrl(target))).StatusCode);

        // A guardian sees their child's picture and no one else's.
        var admin = await world.ClientAsync("admin@demo-center.test");
        var parentEmail = $"photo-parent-{Guid.NewGuid():N}@demo-center.test";
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync($"/api/students/{c.Students[0].Id}/parents", new
        {
            fullName = "Foto Ota", phone = "+998901230001", email = parentEmail, password = DbSeeder.DemoPassword, relationship = "Father",
        })).StatusCode);
        var parent = await world.ClientAsync(parentEmail);
        Assert.Equal(HttpStatusCode.OK, (await parent.GetAsync(PhotoUrl(target))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(await world.ClientAsync("superadmin@demo-center.test"), c.Students[1].UserId, "other.png", Png)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await parent.GetAsync(PhotoUrl(c.Students[1].UserId))).StatusCode);
    }

    [Fact]
    public async Task ATeachersPicture_IsSeenByTheStudentsOfTheirGroups_AndByNoOneUnrelated()
    {
        var c = await world.CreateAsync();
        var other = await world.CreateAsync(teacherStaffsGroup: false);
        var teacher = await world.ClientAsync("teacher@demo-center.test");
        var teacherId = await world.UserIdAsync("teacher@demo-center.test", "TEACHER");
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(teacher, teacherId, "t.png", Png)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await (await world.ClientAsync(c.First.Email)).GetAsync(PhotoUrl(teacherId))).StatusCode);
        // The other classroom has this teacher on a lesson only, not on the group: to them the teacher is a stranger.
        Assert.Equal(HttpStatusCode.Forbidden, (await (await world.ClientAsync(other.First.Email)).GetAsync(PhotoUrl(teacherId))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await teacher.GetAsync(PhotoUrl(Guid.NewGuid()))).StatusCode);
    }

    [Fact]
    public async Task ErasingAnAccount_ErasesItsPictureFromDisk()
    {
        var superAdmin = await world.ClientAsync("superadmin@demo-center.test");
        var created = await superAdmin.PostAsJsonAsync("/api/users", new
        {
            fullName = "Foto Xodim", email = $"photo-staff-{Guid.NewGuid():N}@demo-center.test", phone = "+998900000095",
            password = DbSeeder.DemoPassword, roleCode = "OPERATOR", branchIds = Array.Empty<Guid>(),
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var userId = (await created.Content.ReadFromJsonAsync<IdDto>())!.Id;
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(superAdmin, userId, "staff.png", Png)).StatusCode);

        string key;
        using (var scope = factory.Services.CreateScope())
        {
            key = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.Where(u => u.Id == userId).Select(u => u.PhotoKey).SingleAsync())!;
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<UserPhotoStorage>().PathFor(key));
        }

        Assert.Equal(HttpStatusCode.NoContent, (await superAdmin.DeleteAsync($"/api/users/{userId}")).StatusCode);

        using var after = factory.Services.CreateScope();
        Assert.Null(after.ServiceProvider.GetRequiredService<UserPhotoStorage>().PathFor(key)); // no orphaned file left behind
    }

    // ---- helpers ------------------------------------------------------------------------------------

    private static string PhotoUrl(Guid userId) => $"/api/users/{userId}/photo";

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid userId, string fileName, byte[] bytes)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        return client.PostAsync(PhotoUrl(userId), form);
    }

    private static async Task<OverviewDto> OverviewAsync(HttpClient client, Guid studentId) =>
        (await client.GetFromJsonAsync<OverviewDto>($"/api/students/{studentId}/overview"))!;

    private record IdDto(Guid Id);
    private record PhotoDto(string Version);
    private record Header(bool HasPhoto, string? PhotoVersion);
    private record OverviewDto(Header? Header);
}
