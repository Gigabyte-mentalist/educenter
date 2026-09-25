using System.Net;
using System.Net.Http.Json;
using EduCenter.Infrastructure.Seed;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// The header of the student profile (docs/21.09-audit-jurnali S04.04, TZ §7): branch, course, group, the teachers and the parents'
/// contacts beside the student's own fields — on the overview, not on the student list, which would pay for the lookups per row.
/// </summary>
[Collection("Api")]
public class StudentHeaderTests(EduCenterWebApplicationFactory factory)
{
    private readonly LmsWorld world = new(factory);

    [Fact]
    public async Task Header_NamesTheBranchCourseGroupAndTeachers()
    {
        var c = await world.CreateAsync();
        var admin = await world.ClientAsync("admin@demo-center.test");
        var superAdmin = await world.ClientAsync("superadmin@demo-center.test");
        var supportId = await world.UserIdAsync("support@demo-center.test", "SUPPORT_TEACHER");
        Assert.Equal(HttpStatusCode.NoContent,
            (await superAdmin.PostAsJsonAsync($"/api/groups/{c.GroupId}/staff", new { userId = supportId, role = "SupportTeacher" })).StatusCode);

        var header = (await OverviewAsync(admin, c.First.Id)).Header!;

        var group = Assert.Single(header.Groups);
        Assert.Equal(c.GroupId, group.GroupId);
        Assert.StartsWith("LW-", group.GroupName);
        Assert.StartsWith("Lms Course", group.CourseName);
        var branches = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))!;
        Assert.Equal(branches.Single(b => b.Id == c.BranchId).Name, group.BranchName);
        var teacher = await NameOfAsync(superAdmin, "teacher@demo-center.test", "TEACHER");
        Assert.Equal([teacher], group.MainTeacherNames);
        Assert.Equal([await NameOfAsync(superAdmin, "support@demo-center.test", "SUPPORT_TEACHER")], group.SupportTeacherNames);
        Assert.False(header.HasPhoto);
        Assert.Null(header.PhotoVersion);
    }

    [Fact]
    public async Task Header_ListsTheParentsWithPhoneAndRelationship()
    {
        var c = await world.CreateAsync();
        var admin = await world.ClientAsync("admin@demo-center.test");
        Assert.Empty((await OverviewAsync(admin, c.First.Id)).Header!.Parents);

        var linked = await admin.PostAsJsonAsync($"/api/students/{c.First.Id}/parents", new
        {
            fullName = "Header Ona", phone = "+998901230000", email = $"header-{Guid.NewGuid():N}@demo-center.test",
            password = DbSeeder.DemoPassword, relationship = "Mother",
        });
        Assert.Equal(HttpStatusCode.Created, linked.StatusCode);

        var parent = Assert.Single((await OverviewAsync(admin, c.First.Id)).Header!.Parents);
        Assert.Equal(("Header Ona", "+998901230000", "Mother"), (parent.FullName, parent.Phone, parent.Relationship));

        // The student sees the same header on their own profile.
        var own = await world.ClientAsync(c.First.Email);
        Assert.Single((await OverviewAsync(own, c.First.Id)).Header!.Parents);
    }

    [Fact]
    public async Task StudentWithoutAGroup_HasAnEmptyHeader_NotAnError()
    {
        // Nobody's group covers them yet, so only the org-wide administrator can register them and open the profile.
        var superAdmin = await world.ClientAsync("superadmin@demo-center.test");
        var created = await superAdmin.PostAsJsonAsync("/api/students", new
        {
            fullName = "Guruhsiz Student", phone = "+998900000094", email = $"nogroup-{Guid.NewGuid():N}@demo-center.test", password = DbSeeder.DemoPassword,
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var studentId = (await created.Content.ReadFromJsonAsync<IdDto>())!.Id;

        var header = (await OverviewAsync(superAdmin, studentId)).Header!;
        Assert.Empty(header.Groups);
        Assert.Empty(header.Parents);
        Assert.False(header.HasPhoto);
    }

    [Fact]
    public async Task InSeveralGroups_TheOneJoinedFirstComesFirst_AndALeftGroupIsNotListed()
    {
        var first = await world.CreateAsync();
        var second = await world.CreateAsync();
        var third = await world.CreateAsync();
        var admin = await world.ClientAsync("admin@demo-center.test");
        var studentId = first.First.Id;

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/students/{studentId}/enrollments", new { groupId = second.GroupId })).StatusCode);
        var toLeave = await admin.PostAsJsonAsync($"/api/students/{studentId}/enrollments", new { groupId = third.GroupId });
        Assert.Equal(HttpStatusCode.OK, toLeave.StatusCode);
        var thirdEnrollment = (await toLeave.Content.ReadFromJsonAsync<MoveDto>())!.Enrollments.Single(e => e.GroupId == third.GroupId).Id;
        Assert.Equal(HttpStatusCode.OK,
            (await admin.PostAsJsonAsync($"/api/students/{studentId}/enrollments/{thirdEnrollment}/drop", new { reason = "Sinov" })).StatusCode);

        var groups = (await OverviewAsync(admin, studentId)).Header!.Groups;
        Assert.Equal([first.GroupId, second.GroupId], groups.Select(g => g.GroupId));
    }

    [Fact]
    public async Task Header_FollowsTheStudentScope()
    {
        var mine = await world.CreateAsync();
        var notMine = await world.CreateAsync(teacherStaffsGroup: false);
        var teacher = await world.ClientAsync("teacher@demo-center.test");

        Assert.NotNull((await OverviewAsync(teacher, mine.First.Id)).Header);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync($"/api/students/{notMine.First.Id}/overview")).StatusCode);
    }

    // ---- helpers ------------------------------------------------------------------------------------

    private static async Task<OverviewDto> OverviewAsync(HttpClient client, Guid studentId)
    {
        var response = await client.GetAsync($"/api/students/{studentId}/overview");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<OverviewDto>())!;
    }

    private static async Task<string> NameOfAsync(HttpClient superAdmin, string email, string role) =>
        (await superAdmin.GetFromJsonAsync<PagedResponse<UserDto>>($"/api/users?role={role}"))!.Items.First(u => u.Email == email).FullName;

    private record IdDto(Guid Id);
    private record BranchDto(Guid Id, string Name);
    private record UserDto(Guid Id, string Email, string FullName);
    private record EnrollmentDto(Guid Id, Guid GroupId);
    private record MoveDto(List<EnrollmentDto> Enrollments);
    private record GroupHeader(Guid GroupId, string GroupName, string BranchName, string CourseName,
        List<string> MainTeacherNames, List<string> SupportTeacherNames);
    private record ParentHeader(string FullName, string Phone, string Relationship);
    private record Header(bool HasPhoto, string? PhotoVersion, List<GroupHeader> Groups, List<ParentHeader> Parents);
    private record OverviewDto(Header? Header);
}
