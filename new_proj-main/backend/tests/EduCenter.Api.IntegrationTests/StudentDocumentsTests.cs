using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduCenter.Infrastructure.Seed;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// Student 360° Documents tab (docs/00_TZ.md §7, audit §3.7): a passport scan, a signed contract, a receipt
/// attached to the student's own profile. Viewing follows the same office-staff-plus-the-student-and-their-
/// parent split as the rest of Student 360°; a teacher — who can see the student's academics — holds neither
/// permission at all, because paperwork isn't pedagogical.
/// </summary>
[Collection("Api")]
public class StudentDocumentsTests(EduCenterWebApplicationFactory factory)
{
    private static readonly byte[] SamplePng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    [Fact]
    public async Task List_WithoutToken_ReturnsUnauthorized()
    {
        var response = await factory.CreateClient().GetAsync($"/api/student-documents?studentId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Administrator_CanUploadDownloadAndDeleteAFile()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        var branchId = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var (studentId, _) = await ConvertAsync(operatorClient, admin, branchId);

        var upload = await UploadDocumentAsync(admin, studentId, "Pasport nusxasi", SamplePng, "pasport.png", "image/png");
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var document = await upload.Content.ReadFromJsonAsync<StudentDocumentDto>();
        Assert.Equal("pasport.png", document!.File!.FileName);

        var listed = await admin.GetFromJsonAsync<List<StudentDocumentDto>>($"/api/student-documents?studentId={studentId}");
        Assert.Contains(listed!, d => d.Id == document.Id);

        var download = await admin.GetAsync($"/api/student-documents/{document.Id}/file");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(SamplePng, await download.Content.ReadAsByteArrayAsync());

        var delete = await admin.DeleteAsync($"/api/student-documents/{document.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        var afterDelete = await admin.GetFromJsonAsync<List<StudentDocumentDto>>($"/api/student-documents?studentId={studentId}");
        Assert.DoesNotContain(afterDelete!, d => d.Id == document.Id);
    }

    [Fact]
    public async Task Administrator_CanCreateALinkDocument()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        var branchId = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var (studentId, _) = await ConvertAsync(operatorClient, admin, branchId);

        var response = await admin.PostAsJsonAsync("/api/student-documents",
            new { studentId, title = "Shartnoma", url = "https://drive.google.com/contract", description = (string?)null });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var document = await response.Content.ReadFromJsonAsync<StudentDocumentDto>();
        Assert.Null(document!.File);
        Assert.Equal("https://drive.google.com/contract", document.Url);
    }

    [Fact]
    public async Task Teacher_CannotViewOrManageStudentDocuments()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        var teacher = await AuthenticatedClientAsync("teacher@demo-center.test");
        var branchId = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var (studentId, _) = await ConvertAsync(operatorClient, admin, branchId);

        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync($"/api/student-documents?studentId={studentId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PostAsJsonAsync("/api/student-documents",
            new { studentId, title = "Urinish", url = "https://example.com", description = (string?)null })).StatusCode);
    }

    [Fact]
    public async Task Administrator_CannotAccessDocumentsOfStudentOutsideTheirBranch()
    {
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        var adminBranch = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var otherBranch = (await superAdmin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))!
            .First(b => b.Id != adminBranch).Id;
        var (studentId, _) = await ConvertAsync(operatorClient, superAdmin, otherBranch);

        var response = await admin.GetAsync($"/api/student-documents?studentId={studentId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Student_CanViewOwnDocuments_ButCannotUpload()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        var branchId = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var (studentId, email) = await ConvertAsync(operatorClient, admin, branchId);
        await admin.PostAsJsonAsync("/api/student-documents",
            new { studentId, title = "Shartnoma", url = "https://drive.google.com/contract", description = (string?)null });

        var student = await AuthenticatedClientAsync(email);

        var listed = await student.GetAsync($"/api/student-documents?studentId={studentId}");
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        Assert.Single((await listed.Content.ReadFromJsonAsync<List<StudentDocumentDto>>())!);

        var attempt = await student.PostAsJsonAsync("/api/student-documents",
            new { studentId, title = "Urinish", url = "https://example.com", description = (string?)null });
        Assert.Equal(HttpStatusCode.Forbidden, attempt.StatusCode);
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static async Task<HttpResponseMessage> UploadDocumentAsync(
        HttpClient client, Guid studentId, string title, byte[] bytes, string fileName, string contentType)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(studentId.ToString()), "StudentId" },
            { new StringContent(title), "Title" },
        };
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "File", fileName);
        return await client.PostAsync("/api/student-documents/upload", form);
    }

    private static async Task<(Guid StudentId, string Email)> ConvertAsync(HttpClient operatorClient, HttpClient admin, Guid branchId)
    {
        var course = await (await admin.PostAsJsonAsync("/api/courses",
            new { name = $"Docs Course {Guid.NewGuid():N}"[..24], levelCategory = "IELTS" })).Content.ReadFromJsonAsync<CourseDto>();
        var group = await (await admin.PostAsJsonAsync("/api/groups",
            new { branchId, courseId = course!.Id, name = $"DOC-{Guid.NewGuid():N}", capacity = 10 }))
            .Content.ReadFromJsonAsync<GroupDto>();

        var lead = await (await operatorClient.PostAsJsonAsync("/api/leads", new
        {
            branchId, fullName = "Documents Student", phone = "+998900000066", source = "Website", interestedCourse = "IELTS",
        })).Content.ReadFromJsonAsync<LeadDto>();

        var email = $"docs-{Guid.NewGuid():N}@demo-center.test";
        var response = await operatorClient.PostAsJsonAsync($"/api/leads/{lead!.Id}/convert",
            new { groupId = group!.Id, email, password = DbSeeder.DemoPassword });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var student = await response.Content.ReadFromJsonAsync<StudentDto>();
        return (student!.Id, email);
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
    private record CourseDto(Guid Id);
    private record GroupDto(Guid Id);
    private record LeadDto(Guid Id);
    private record StudentDto(Guid Id);
    private record StudentDocumentFileDto(string FileName, string ContentType, long Size);
    private record StudentDocumentDto(Guid Id, Guid StudentId, string Title, string? Url, string? Description,
        StudentDocumentFileDto? File, Guid UploadedBy, DateTimeOffset CreatedAt);
}
