using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduCenter.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// Files on homework (docs/21.09-audit-jurnali S03.03, TZ §16): the teacher attaches a worksheet or an audio clip, the student hands in a
/// file — with words or without — and each is given back only to whoever may see that homework or that student's work.
/// </summary>
[Collection("Api")]
public class HomeworkFilesTests(EduCenterWebApplicationFactory factory)
{
    private static readonly byte[] Worksheet = [0x25, 0x50, 0x44, 0x46, 0x2D, 1, 2, 3, 4, 5]; // "%PDF-…"
    private static readonly byte[] Answer = [0xFF, 0xD8, 0xFF, 0xE0, 9, 8, 7, 6];              // a JPEG start
    private readonly LmsWorld world = new(factory);

    [Fact]
    public async Task Teacher_AttachesAWorksheet_AndAStudentOfTheGroupTakesItBackByteForByte()
    {
        var c = await world.CreateAsync();
        var teacher = await world.ClientAsync("teacher@demo-center.test");

        var created = await CreateWithFileAsync(teacher, c.LessonId, Worksheet, "worksheet.pdf");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var homework = (await created.Content.ReadFromJsonAsync<HomeworkDto>())!;
        Assert.Equal(new FileDto("worksheet.pdf", "application/pdf", Worksheet.Length), homework.Attachment);

        var student = await world.ClientAsync(c.First.Email);
        var download = await student.GetAsync($"/api/homework/{homework.Id}/attachment");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(Worksheet, await download.Content.ReadAsByteArrayAsync());

        // The list tells the screens a file is there — by name and size, never by where the server keeps it.
        var listed = await student.GetStringAsync($"/api/homework?lessonId={c.LessonId}");
        Assert.Contains("worksheet.pdf", listed);
        Assert.DoesNotContain("attachmentKey", listed, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Student_HandsInAFileWithWords_TeacherStudentAndParentTakeItBack()
    {
        var c = await world.CreateAsync();
        var homework = await CreateJsonHomeworkAsync(await world.ClientAsync("teacher@demo-center.test"), c.LessonId);
        var admin = await world.ClientAsync("admin@demo-center.test");
        var parentUserId = await world.UserIdAsync("parent@demo-center.test", "PARENT");
        Assert.Equal(HttpStatusCode.Created,
            (await admin.PostAsJsonAsync($"/api/students/{c.First.Id}/parents", new { userId = parentUserId, relationship = "Mother" })).StatusCode);

        var student = await world.ClientAsync(c.First.Email);
        var submitted = await SubmitFileAsync(student, homework.Id, Answer, "answer.jpg", "My answer is attached");
        Assert.Equal(HttpStatusCode.Created, submitted.StatusCode);
        var submission = (await submitted.Content.ReadFromJsonAsync<SubmissionDto>())!;
        Assert.Equal(("My answer is attached", new FileDto("answer.jpg", "image/jpeg", Answer.Length)), (submission.ContentText, submission.File));

        var teacher = await world.ClientAsync("teacher@demo-center.test");
        Assert.Equal("answer.jpg", (await teacher.GetFromJsonAsync<List<SubmissionDto>>($"/api/homework/{homework.Id}/submissions"))!.Single().File!.FileName);

        foreach (var client in new[] { teacher, student, await world.ClientAsync("parent@demo-center.test") })
        {
            var download = await client.GetAsync($"/api/homework/submissions/{submission.Id}/attachment");
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
            Assert.Equal(Answer, await download.Content.ReadAsByteArrayAsync());
        }

        // The student's profile tab lists the file, so it can be taken from there too.
        var row = (await student.GetFromJsonAsync<List<ProfileHomeworkRow>>($"/api/students/{c.First.Id}/homework"))!.Single();
        Assert.Equal((submission.Id, "answer.jpg"), (row.SubmissionId, row.SubmissionFileName));
    }

    [Fact]
    public async Task NoOneElse_CanTakeAStudentsFile_OrAHomeworksWorksheet()
    {
        var c = await world.CreateAsync(students: 2);
        var teacher = await world.ClientAsync("teacher@demo-center.test");
        var homework = (await (await CreateWithFileAsync(teacher, c.LessonId, Worksheet, "worksheet.pdf")).Content.ReadFromJsonAsync<HomeworkDto>())!;
        var submission = (await (await SubmitFileAsync(await world.ClientAsync(c.Students[0].Email), homework.Id, Answer, "answer.jpg"))
            .Content.ReadFromJsonAsync<SubmissionDto>())!;

        // A classmate may read the worksheet — it is the group's task — but not the other student's answer.
        var classmate = await world.ClientAsync(c.Students[1].Email);
        Assert.Equal(HttpStatusCode.OK, (await classmate.GetAsync($"/api/homework/{homework.Id}/attachment")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await classmate.GetAsync($"/api/homework/submissions/{submission.Id}/attachment")).StatusCode);

        // A teacher who does not teach this group, and an operator (who holds no homework permission at all).
        var otherGroup = await world.CreateAsync(teacherStaffsGroup: false);
        var strangersHomework = (await (await CreateWithFileAsync(await world.ClientAsync("admin@demo-center.test"), otherGroup.LessonId, Worksheet, "other.pdf"))
            .Content.ReadFromJsonAsync<HomeworkDto>())!;
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync($"/api/homework/{strangersHomework.Id}/attachment")).StatusCode);
        var operatorClient = await world.ClientAsync("operator@demo-center.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.GetAsync($"/api/homework/{homework.Id}/attachment")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.GetAsync($"/api/homework/submissions/{submission.Id}/attachment")).StatusCode);

        // ...and a teacher of another group cannot attach a file to this group's lesson either — nothing is left on the disk.
        var before = FilesOnDisk();
        var refused = await CreateWithFileAsync(await world.ClientAsync("teacher@demo-center.test"), otherGroup.LessonId, Worksheet, "sneaky.pdf");
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(before, FilesOnDisk());
    }

    [Fact]
    public async Task ATooBigFile_AnEmptyOne_AndAForbiddenType_AreRefused_AndNothingIsKept()
    {
        var c = await world.CreateAsync();
        var teacher = await world.ClientAsync("teacher@demo-center.test");
        var homework = await CreateJsonHomeworkAsync(teacher, c.LessonId);
        var student = await world.ClientAsync(c.First.Email);
        var before = FilesOnDisk();

        var tooBig = new byte[20 * 1024 * 1024 + 1];
        var bigCreate = await CreateWithFileAsync(teacher, c.LessonId, tooBig, "huge.pdf");
        Assert.Equal(HttpStatusCode.BadRequest, bigCreate.StatusCode);
        Assert.Contains("20 MB", await bigCreate.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await SubmitFileAsync(student, homework.Id, tooBig, "huge.mp3")).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await CreateWithFileAsync(teacher, c.LessonId, [], "empty.pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SubmitFileAsync(student, homework.Id, [1, 2, 3], "run.exe")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateWithFileAsync(teacher, c.LessonId, [1, 2, 3], "script.js")).StatusCode);

        // The largest allowed file still goes through: an audio answer is why the ceiling is 20 MB, not 10.
        Assert.Equal(HttpStatusCode.Created, (await SubmitFileAsync(student, homework.Id, TestFiles.Mp3(20 * 1024 * 1024), "speaking.mp3")).StatusCode);
        Assert.Equal(before + 1, FilesOnDisk());
    }

    [Fact]
    public async Task ASecondAnswer_IsRefused_AndTheFileThatCameWithItIsNotKept()
    {
        var c = await world.CreateAsync();
        var homework = await CreateJsonHomeworkAsync(await world.ClientAsync("teacher@demo-center.test"), c.LessonId);
        var student = await world.ClientAsync(c.First.Email);

        var before = FilesOnDisk();
        Assert.Equal(HttpStatusCode.Created, (await SubmitFileAsync(student, homework.Id, Answer, "first.jpg")).StatusCode);
        Assert.Equal(before + 1, FilesOnDisk());

        var again = await SubmitFileAsync(student, homework.Id, Answer, "second.jpg");
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(before + 1, FilesOnDisk()); // one file on disk, the first
    }

    [Fact]
    public async Task ErasingAHomework_TakesItsWorksheetOffTheDisk()
    {
        var c = await world.CreateAsync();
        var teacher = await world.ClientAsync("teacher@demo-center.test");
        var before = FilesOnDisk();
        var homework = (await (await CreateWithFileAsync(teacher, c.LessonId, Worksheet, "worksheet.pdf")).Content.ReadFromJsonAsync<HomeworkDto>())!;
        Assert.Equal(before + 1, FilesOnDisk());

        Assert.Equal(HttpStatusCode.NoContent, (await teacher.DeleteAsync($"/api/homework/{homework.Id}")).StatusCode);
        Assert.Equal(before, FilesOnDisk());
    }

    [Fact]
    public async Task AFileOnlyAnswer_ToAWritingHomework_LeavesTheAiFlowAlone()
    {
        var c = await world.CreateAsync();
        var teacher = await world.ClientAsync("teacher@demo-center.test");
        var created = await teacher.PostAsJsonAsync("/api/homework", new
        {
            lessonId = c.LessonId, title = "Essay on the phone", type = "Writing", deadline = DateTimeOffset.UtcNow.AddDays(3), maxScore = 10,
        });
        var homework = (await created.Content.ReadFromJsonAsync<HomeworkDto>())!;

        var submitted = await SubmitFileAsync(await world.ClientAsync(c.First.Email), homework.Id, Answer, "essay.jpg");
        Assert.Equal(HttpStatusCode.Created, submitted.StatusCode);

        using var scope = factory.Services.CreateScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<AppDbContext>().WritingEvaluations.CountAsync(e => e.HomeworkId == homework.Id));
    }

    [Fact]
    public async Task TheJsonEndpoints_StillWork_WithoutAnyFile()
    {
        var c = await world.CreateAsync();
        var teacher = await world.ClientAsync("teacher@demo-center.test");
        var homework = await CreateJsonHomeworkAsync(teacher, c.LessonId);
        Assert.Null(homework.Attachment);

        var student = await world.ClientAsync(c.First.Email);
        var submitted = await student.PostAsJsonAsync($"/api/homework/{homework.Id}/submissions", new { contentText = "Plain text answer" });
        Assert.Equal(HttpStatusCode.Created, submitted.StatusCode);
        var submission = (await submitted.Content.ReadFromJsonAsync<SubmissionDto>())!;
        Assert.Null(submission.File);

        Assert.Equal(HttpStatusCode.NotFound, (await student.GetAsync($"/api/homework/{homework.Id}/attachment")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await teacher.GetAsync($"/api/homework/submissions/{submission.Id}/attachment")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await student.PostAsync($"/api/homework/{homework.Id}/submissions/upload", new MultipartFormDataContent())).StatusCode);
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static int FilesOnDisk() =>
        Directory.Exists(EduCenterWebApplicationFactory.HomeworkFilesPath) ? Directory.GetFiles(EduCenterWebApplicationFactory.HomeworkFilesPath).Length : 0;

    private static Task<HttpResponseMessage> CreateWithFileAsync(HttpClient client, Guid lessonId, byte[] bytes, string fileName) =>
        client.PostAsync("/api/homework/upload", new MultipartFormDataContent
        {
            { new StringContent(lessonId.ToString()), "lessonId" },
            { new StringContent("Worksheet 1"), "title" },
            { new StringContent("Grammar"), "type" },
            { new StringContent(DateTimeOffset.UtcNow.AddDays(7).ToString("O")), "deadline" },
            { new StringContent("10"), "maxScore" },
            { FilePart(bytes), "file", fileName },
        });

    private static Task<HttpResponseMessage> SubmitFileAsync(HttpClient client, Guid homeworkId, byte[] bytes, string fileName, string? text = null)
    {
        var form = new MultipartFormDataContent { { FilePart(bytes), "file", fileName } };
        if (text is not null) form.Add(new StringContent(text), "contentText");
        return client.PostAsync($"/api/homework/{homeworkId}/submissions/upload", form);
    }

    private static ByteArrayContent FilePart(byte[] bytes)
    {
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return part;
    }

    private static async Task<HomeworkDto> CreateJsonHomeworkAsync(HttpClient client, Guid lessonId)
    {
        var response = await client.PostAsJsonAsync("/api/homework", new
        {
            lessonId, title = "Plain homework", type = "Grammar", deadline = DateTimeOffset.UtcNow.AddDays(7), maxScore = 10,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<HomeworkDto>())!;
    }

    private record FileDto(string FileName, string ContentType, long Size);
    private record HomeworkDto(Guid Id, Guid LessonId, string Title, FileDto? Attachment);
    private record SubmissionDto(Guid Id, string? ContentText, FileDto? File);
    private record ProfileHomeworkRow(Guid Id, Guid? SubmissionId, string? SubmissionFileName);
}
