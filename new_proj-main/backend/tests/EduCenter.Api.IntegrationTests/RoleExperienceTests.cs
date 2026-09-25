using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduCenter.Api.Ai.Knowledge;
using EduCenter.Infrastructure;
using EduCenter.Infrastructure.Seed;
using Microsoft.Extensions.DependencyInjection;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// What each role is shown: the screens offer only what the person can do, so nobody meets "Bu amal uchun ruxsatingiz yo'q"
/// after clicking. The server still refuses everything else — these tests pin the data the UI decides from.
/// </summary>
[Collection("Api")]
public class RoleExperienceTests(EduCenterWebApplicationFactory factory)
{
    private const string Text = """
        Conditionals in speaking.

        Use the second conditional to talk about unreal situations: "If I had more time, I would learn another language."
        The structure is if plus the past simple, then would plus the base form.
        """;

    [Fact]
    public async Task MyPermissions_ListsWhatTheRoleHolds_AndNothingItDoesNot()
    {
        var teacher = await AuthenticatedClientAsync("teacher@demo-center.test");

        var permissions = (await teacher.GetFromJsonAsync<List<PermissionDto>>("/api/me/permissions"))!;

        var groupsView = permissions.Single(p => p.Code == "groups.view");
        Assert.Equal("MyGroups", groupsView.Scope);
        Assert.DoesNotContain(permissions, p => p.Code == "groups.create");
        Assert.DoesNotContain(permissions, p => p.Code == "branch.create");
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/me/permissions")).StatusCode);
    }

    [Fact]
    public async Task TeacherList_MarksColleaguesAsNotOpenable_ButTheirOwnProfileIsOpenable()
    {
        var teacher = await AuthenticatedClientAsync("teacher@demo-center.test");
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");

        var seenByTeacher = (await teacher.GetFromJsonAsync<List<TeacherRow>>("/api/teachers"))!;
        var seenByAdmin = (await superAdmin.GetFromJsonAsync<List<TeacherRow>>("/api/teachers"))!;

        Assert.True(seenByTeacher.Single(t => t.Email == "teacher@demo-center.test").CanOpen);
        var colleague = seenByTeacher.Single(t => t.Email == "support@demo-center.test");
        Assert.False(colleague.CanOpen);
        Assert.True(seenByAdmin.All(t => t.CanOpen));

        // The flag is not decoration: opening the colleague's page is refused.
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync($"/api/teachers/{colleague.Id}")).StatusCode);
    }

    [Fact]
    public async Task KnowledgeTargets_OfferATeacherOnlyTheirGroupsAndCourses()
    {
        var teacher = await AuthenticatedClientAsync("teacher@demo-center.test");
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");

        var forTeacher = (await teacher.GetFromJsonAsync<TargetsDto>("/api/ai/knowledge/targets"))!;
        Assert.DoesNotContain("Organization", forTeacher.Scopes);
        Assert.DoesNotContain("Branch", forTeacher.Scopes);
        Assert.Contains("Group", forTeacher.Scopes);
        Assert.Contains("Course", forTeacher.Scopes);
        Assert.Empty(forTeacher.Branches);
        Assert.Contains(forTeacher.Groups, g => g.Name == "IELTS-21");

        var forAdmin = (await superAdmin.GetFromJsonAsync<TargetsDto>("/api/ai/knowledge/targets"))!;
        Assert.Contains("Organization", forAdmin.Scopes);
    }

    [Fact]
    public async Task Teacher_CannotAttachMaterialToAWholeBranch()
    {
        var teacher = await AuthenticatedClientAsync("teacher@demo-center.test");
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var branchId = (await superAdmin.GetFromJsonAsync<List<BranchRow>>("/api/branches"))![0].Id;

        var response = await UploadAsync(teacher, "branch-wide.txt", Text, "Branch", ("branchId", branchId.ToString()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Preview_OfADocumentWhoseFileIsGone_IsRebuiltFromTheIndex()
    {
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var uploaded = await UploadAsync(superAdmin, "conditionals.txt", Text, "Organization");
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        var document = (await uploaded.Content.ReadFromJsonAsync<DocumentRow>())!;
        Assert.True(document.FileAvailable);

        // Simulate the lost App_Data volume: the row and its chunks survive, the file does not.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var key = db.KnowledgeDocuments.Where(d => d.Id == document.Id).Select(d => d.StorageKey).Single();
            scope.ServiceProvider.GetRequiredService<KnowledgeFileStorage>().Delete(key);
        }

        var preview = (await superAdmin.GetFromJsonAsync<PreviewRow>($"/api/ai/knowledge/documents/{document.Id}/preview"))!;
        Assert.True(preview.Restored);
        Assert.Contains("second conditional", preview.Text, StringComparison.OrdinalIgnoreCase);

        var listed = (await superAdmin.GetFromJsonAsync<List<DocumentRow>>("/api/ai/knowledge/documents"))!.Single(d => d.Id == document.Id);
        Assert.False(listed.FileAvailable);

        // Re-indexing needs the file; without it the working index must not be flipped to "Failed".
        Assert.Equal(HttpStatusCode.Conflict, (await superAdmin.PostAsync($"/api/ai/knowledge/documents/{document.Id}/reingest", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await superAdmin.GetAsync($"/api/ai/knowledge/documents/{document.Id}/file")).StatusCode);
        Assert.Equal("Ready", (await superAdmin.GetFromJsonAsync<DocumentRow>($"/api/ai/knowledge/documents/{document.Id}"))!.Status);
    }

    [Fact]
    public async Task GroupOverview_ShowsATeacherTheirGroupResults_AndAStudentOnlyTheGroup()
    {
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var teacher = await AuthenticatedClientAsync("teacher@demo-center.test");
        var student = await AuthenticatedClientAsync("student@demo-center.test");
        var groups = (await superAdmin.GetFromJsonAsync<List<GroupRow>>("/api/groups"))!;
        var ielts21 = groups.First(g => g.Name == "IELTS-21");

        var forTeacher = (await teacher.GetFromJsonAsync<OverviewRow>($"/api/groups/{ielts21.Id}/overview"))!;
        Assert.Equal("IELTS-21", forTeacher.Name);
        Assert.True(forTeacher.StudentCount >= 3, "The demo students are enrolled in IELTS-21.");
        Assert.NotNull(forTeacher.Results);
        Assert.Equal(forTeacher.StudentCount, forTeacher.Results!.Students.Count);
        Assert.InRange(forTeacher.Results.AttendanceRate, 0, 100);
        Assert.Contains(forTeacher.Staff, s => s.Role == "MainTeacher");

        var forStudent = (await student.GetFromJsonAsync<OverviewRow>($"/api/groups/{ielts21.Id}/overview"))!;
        Assert.Null(forStudent.Results);
        Assert.Equal(forTeacher.StudentCount, forStudent.StudentCount);

        // A group the student is not in stays closed to them.
        var other = groups.First(g => g.Name == "IELTS-Adv-1");
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync($"/api/groups/{other.Id}/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await teacher.GetAsync($"/api/groups/{Guid.NewGuid()}/overview")).StatusCode);
    }

    [Fact]
    public async Task GroupList_CarriesTheEnrolledStudentCount()
    {
        var teacher = await AuthenticatedClientAsync("teacher@demo-center.test");

        var groups = (await teacher.GetFromJsonAsync<List<GroupRow>>("/api/groups"))!;

        Assert.All(groups, g => Assert.InRange(g.StudentCount, 0, g.Capacity));
        Assert.True(groups.First(g => g.Name == "IELTS-21").StudentCount >= 3);
    }

    [Fact]
    public async Task ScheduleList_NamesTheTeacherOfEachLesson()
    {
        var teacher = await AuthenticatedClientAsync("teacher@demo-center.test");

        var lessons = (await teacher.GetFromJsonAsync<List<LessonRow>>("/api/schedule"))!;

        Assert.NotEmpty(lessons);
        Assert.All(lessons, l => Assert.Equal("Teacher Demo", l.TeacherName));
    }

    private static Task<HttpResponseMessage> UploadAsync(
        HttpClient client, string fileName, string content, string scope, params (string Key, string Value)[] extra)
    {
        var form = new MultipartFormDataContent();
        var file = new StringContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "file", fileName);
        form.Add(new StringContent(Path.GetFileNameWithoutExtension(fileName)), "title");
        form.Add(new StringContent(scope), "scope");
        form.Add(new StringContent("Students"), "visibility");
        foreach (var (key, value) in extra) form.Add(new StringContent(value), key);

        return client.PostAsync("/api/ai/knowledge/documents", form);
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
    private record PermissionDto(string Code, string Scope);
    private record TeacherRow(Guid Id, string FullName, string Email, string Status, bool CanOpen);
    private record TargetRow(Guid Id, string Name);
    private record TargetsDto(List<string> Scopes, List<TargetRow> Branches, List<TargetRow> Courses, List<TargetRow> Groups);
    private record BranchRow(Guid Id, string Name);
    private record DocumentRow(Guid Id, string Status, bool FileAvailable);
    private record PreviewRow(string Text, bool Truncated, bool Restored);
    private record GroupRow(Guid Id, string Name, int Capacity, int StudentCount);
    private record StaffRow(Guid UserId, string FullName, string Role);
    private record StudentResultRow(Guid StudentId, string FullName);
    private record ResultsRow(double AttendanceRate, double HomeworkCompletionRate, List<StudentResultRow> Students);
    private record OverviewRow(Guid Id, string Name, int StudentCount, List<StaffRow> Staff, ResultsRow? Results);
    private record LessonRow(Guid Id, string? TeacherName);
}
