using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduCenter.Api.Automation;
using EduCenter.Infrastructure;
using EduCenter.Modules.Students.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// Individual homework (docs/21.09-audit-jurnali S03.04, TZ §16 "group/student assignment"): a task set for one student or a few.
/// The risk is not the new column but every place that used to assume a homework is for the whole group — the completion
/// percentages, the dashboard's "still to hand in", the reminders and the support signal — so most of these tests read those.
/// </summary>
[Collection("Api")]
public class HomeworkTargetTests(EduCenterWebApplicationFactory factory)
{
    private readonly LmsWorld world = new(factory);

    [Fact]
    public async Task AnIndividualHomework_IsSeenAndHandedInByItsStudentOnly()
    {
        var c = await world.CreateAsync(students: 2);
        var (a, b) = (c.Students[0], c.Students[1]);
        var teacher = await world.ClientAsync("teacher@demo-center.test");
        await LinkParentAsync(a.Id);

        var created = await CreateAsync(teacher, c.LessonId, [a.Id]);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var homework = (await created.Content.ReadFromJsonAsync<HomeworkDto>())!;
        Assert.True(homework.Targeted);
        Assert.Equal(new[] { a.Id }, homework.StudentIds);

        // The student it was given to and their parent see it — and are told it is individual, not who else it was given to.
        var seenByA = (await (await world.ClientAsync(a.Email)).GetFromJsonAsync<List<HomeworkDto>>($"/api/homework?lessonId={c.LessonId}"))!.Single();
        Assert.True(seenByA.Targeted);
        Assert.Null(seenByA.StudentIds);
        Assert.Single((await (await world.ClientAsync("parent@demo-center.test")).GetFromJsonAsync<List<HomeworkDto>>($"/api/homework?lessonId={c.LessonId}"))!);

        // The teacher sees who it was given to.
        Assert.Equal(new[] { a.Id }, (await teacher.GetFromJsonAsync<List<HomeworkDto>>($"/api/homework?lessonId={c.LessonId}"))!.Single().StudentIds);

        // A classmate neither sees it nor can hand it in.
        var classmate = await world.ClientAsync(b.Email);
        Assert.Empty((await classmate.GetFromJsonAsync<List<HomeworkDto>>($"/api/homework?lessonId={c.LessonId}"))!);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await classmate.PostAsJsonAsync($"/api/homework/{homework.Id}/submissions", new { contentText = "Not mine" })).StatusCode);

        Assert.Equal(HttpStatusCode.Created,
            (await (await world.ClientAsync(a.Email)).PostAsJsonAsync($"/api/homework/{homework.Id}/submissions", new { contentText = "Mine" })).StatusCode);
    }

    /// <summary>The 7-places risk: a student who was never given the individual task must not look as if they had skipped it.</summary>
    [Fact]
    public async Task TheOtherStudentsCompletionRate_DoesNotFall_AndTheirDashboardDoesNotNagAboutIt()
    {
        var c = await world.CreateAsync(students: 2);
        var (a, b) = (c.Students[0], c.Students[1]);
        var teacher = await world.ClientAsync("teacher@demo-center.test");

        var groupWide = (await (await CreateAsync(teacher, c.LessonId, null)).Content.ReadFromJsonAsync<HomeworkDto>())!;
        var individual = (await (await CreateAsync(teacher, c.LessonId, [a.Id])).Content.ReadFromJsonAsync<HomeworkDto>())!;

        var clientB = await world.ClientAsync(b.Email);
        Assert.Equal(HttpStatusCode.Created, (await clientB.PostAsJsonAsync($"/api/homework/{groupWide.Id}/submissions", new { contentText = "Done" })).StatusCode);

        var admin = await world.ClientAsync("admin@demo-center.test");
        var overviewB = (await admin.GetFromJsonAsync<StudentOverviewDto>($"/api/students/{b.Id}/overview"))!;
        Assert.Equal((1, 1, 100.0), (overviewB.HomeworkAssigned, overviewB.HomeworkSubmitted, overviewB.HomeworkCompletionRate));
        var overviewA = (await admin.GetFromJsonAsync<StudentOverviewDto>($"/api/students/{a.Id}/overview"))!;
        Assert.Equal((2, 0, 0.0), (overviewA.HomeworkAssigned, overviewA.HomeworkSubmitted, overviewA.HomeworkCompletionRate));

        // The student's own list on the profile, and the dashboard's "to hand in".
        Assert.Equal(new[] { groupWide.Id }, (await admin.GetFromJsonAsync<List<ProfileHomeworkRow>>($"/api/students/{b.Id}/homework"))!.Select(r => r.Id));
        Assert.DoesNotContain((await clientB.GetFromJsonAsync<StudentDashboard>("/api/dashboard/student"))!.PendingHomework, h => h.Id == individual.Id);
        Assert.Contains((await (await world.ClientAsync(a.Email)).GetFromJsonAsync<StudentDashboard>("/api/dashboard/student"))!.PendingHomework, h => h.Id == individual.Id);

        // The group's rate: three (homework, student) pairs were given out — the group-wide task to two students, the individual one to one.
        Assert.Equal(HttpStatusCode.Created,
            (await (await world.ClientAsync(a.Email)).PostAsJsonAsync($"/api/homework/{individual.Id}/submissions", new { contentText = "Done too" })).StatusCode);
        var group = (await admin.GetFromJsonAsync<GroupOverview>($"/api/groups/{c.GroupId}/overview"))!;
        Assert.Equal(66.7, group.Results!.HomeworkCompletionRate); // 2 of 3 — the old formula (2 homework × 2 students) said 50
    }

    [Fact]
    public async Task OnlyTheTargetedStudentsAreTold_TheirParentsToo_AndOnlyTheyAreToldOfAMovedDeadline()
    {
        var c = await world.CreateAsync(students: 2);
        var (a, b) = (c.Students[0], c.Students[1]);
        var teacher = await world.ClientAsync("teacher@demo-center.test");
        var parentUserId = await LinkParentAsync(a.Id);

        var (aBefore, bBefore, parentBefore) = (await CountAsync(a.UserId, "HOMEWORK_CREATED"), await CountAsync(b.UserId, "HOMEWORK_CREATED"),
            await CountAsync(parentUserId, "HOMEWORK_CREATED"));
        var homework = (await (await CreateAsync(teacher, c.LessonId, [a.Id])).Content.ReadFromJsonAsync<HomeworkDto>())!;
        Assert.Equal((aBefore + 1, bBefore, parentBefore + 1),
            (await CountAsync(a.UserId, "HOMEWORK_CREATED"), await CountAsync(b.UserId, "HOMEWORK_CREATED"), await CountAsync(parentUserId, "HOMEWORK_CREATED")));

        var (aChanged, bChanged) = (await CountAsync(a.UserId, "HOMEWORK_CHANGED"), await CountAsync(b.UserId, "HOMEWORK_CHANGED"));
        Assert.Equal(HttpStatusCode.OK, (await teacher.PatchAsJsonAsync($"/api/homework/{homework.Id}", new { deadline = homework.Deadline.AddDays(2) })).StatusCode);
        Assert.Equal((aChanged + 1, bChanged), (await CountAsync(a.UserId, "HOMEWORK_CHANGED"), await CountAsync(b.UserId, "HOMEWORK_CHANGED")));

        // A whole-group homework still tells the whole group.
        await CreateAsync(teacher, c.LessonId, null);
        Assert.Equal(bBefore + 1, await CountAsync(b.UserId, "HOMEWORK_CREATED"));
    }

    [Fact]
    public async Task EveryNamedStudent_MustBeAnActiveStudentOfTheLessonsGroup()
    {
        var c = await world.CreateAsync();
        var elsewhere = (await world.CreateAsync()).First; // enrolled, but in another group
        var teacher = await world.ClientAsync("teacher@demo-center.test");

        var stranger = await CreateAsync(teacher, c.LessonId, [c.First.Id, elsewhere.Id]);
        Assert.Equal(HttpStatusCode.BadRequest, stranger.StatusCode);
        Assert.Contains(elsewhere.Id.ToString(), await stranger.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateAsync(teacher, c.LessonId, [Guid.NewGuid()])).StatusCode);

        // Nothing was created by the refused calls.
        Assert.Empty((await teacher.GetFromJsonAsync<List<HomeworkDto>>($"/api/homework?lessonId={c.LessonId}"))!);
    }

    [Fact]
    public async Task NoListOrAnEmptyOne_IsTheWholeGroup_ExactlyAsBefore()
    {
        var c = await world.CreateAsync(students: 2);
        var teacher = await world.ClientAsync("teacher@demo-center.test");

        var omitted = (await (await CreateAsync(teacher, c.LessonId, null)).Content.ReadFromJsonAsync<HomeworkDto>())!;
        var empty = (await (await CreateAsync(teacher, c.LessonId, [])).Content.ReadFromJsonAsync<HomeworkDto>())!;
        Assert.False(omitted.Targeted || empty.Targeted);
        Assert.Null(omitted.StudentIds);

        foreach (var student in c.Students)
        {
            var seen = (await (await world.ClientAsync(student.Email)).GetFromJsonAsync<List<HomeworkDto>>($"/api/homework?lessonId={c.LessonId}"))!;
            Assert.Equal(new[] { omitted.Id, empty.Id }.Order(), seen.Select(h => h.Id).Order());
        }
    }

    [Fact]
    public async Task TheSupportSignal_DoesNotCountHomeworkThatWasNotGivenToTheStudent()
    {
        var c = await world.CreateAsync(students: 2);
        var (a, b) = (c.Students[0], c.Students[1]);
        var teacher = await world.ClientAsync("teacher@demo-center.test");

        // Three individual tasks for A, all due by the time the sweep runs — B was given none of them, so B has missed none.
        var due = DateTimeOffset.UtcNow.AddHours(1);
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Created, (await CreateAsync(teacher, c.LessonId, [a.Id], due.AddMinutes(i))).StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var organizationId = await db.Groups.Where(g => g.Id == c.GroupId).Select(g => g.OrganizationId).SingleAsync();
        await scope.ServiceProvider.GetRequiredService<FollowUpRules>().RunHomeworkSignalsAsync(DateTimeOffset.UtcNow.AddHours(5), organizationId);

        var raisedFor = await db.SupportCases
            .Where(s => s.GroupId == c.GroupId && s.SignalCode == SupportSignalCode.HomeworkMissed)
            .Select(s => s.StudentId).ToListAsync();
        Assert.Equal(new[] { a.Id }, raisedFor);
    }

    [Fact]
    public async Task TheWorksheetOfAnIndividualHomework_IsForItsStudentsOnly_AndErasingTheHomeworkErasesWhoItWasFor()
    {
        var c = await world.CreateAsync(students: 2);
        var (a, b) = (c.Students[0], c.Students[1]);
        var teacher = await world.ClientAsync("teacher@demo-center.test");

        var form = new MultipartFormDataContent
        {
            { new StringContent(c.LessonId.ToString()), "lessonId" },
            { new StringContent("Extra practice"), "title" },
            { new StringContent("Grammar"), "type" },
            { new StringContent(DateTimeOffset.UtcNow.AddDays(7).ToString("O")), "deadline" },
            { new StringContent("10"), "maxScore" },
            { new StringContent(a.Id.ToString()), "studentIds" },
            { new ByteArrayContent([0x25, 0x50, 0x44, 0x46, 1, 2, 3]), "file", "extra.pdf" },
        };
        var created = await teacher.PostAsync("/api/homework/upload", form);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var homework = (await created.Content.ReadFromJsonAsync<HomeworkDto>())!;
        Assert.Equal(new[] { a.Id }, homework.StudentIds);

        Assert.Equal(HttpStatusCode.OK, (await (await world.ClientAsync(a.Email)).GetAsync($"/api/homework/{homework.Id}/attachment")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await world.ClientAsync(b.Email)).GetAsync($"/api/homework/{homework.Id}/attachment")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await teacher.DeleteAsync($"/api/homework/{homework.Id}")).StatusCode);
        using var scope = factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().HomeworkTargets.AnyAsync(t => t.HomeworkId == homework.Id));
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static Task<HttpResponseMessage> CreateAsync(HttpClient client, Guid lessonId, Guid[]? studentIds, DateTimeOffset? deadline = null) =>
        client.PostAsJsonAsync("/api/homework", new
        {
            lessonId, title = "Homework", type = "Grammar", deadline = deadline ?? DateTimeOffset.UtcNow.AddDays(7), maxScore = 10, studentIds,
        });

    /// <summary>The demo parent watches this student too. Returns the parent's user id.</summary>
    private async Task<Guid> LinkParentAsync(Guid studentId)
    {
        var parentUserId = await world.UserIdAsync("parent@demo-center.test", "PARENT");
        var admin = await world.ClientAsync("admin@demo-center.test");
        Assert.Equal(HttpStatusCode.Created,
            (await admin.PostAsJsonAsync($"/api/students/{studentId}/parents", new { userId = parentUserId, relationship = "Mother" })).StatusCode);
        return parentUserId;
    }

    private async Task<int> CountAsync(Guid userId, string type)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Notifications.CountAsync(n => n.UserId == userId && n.Type == type);
    }

    private record HomeworkDto(Guid Id, Guid LessonId, string Title, DateTimeOffset Deadline, bool Targeted, List<Guid>? StudentIds);
    private record StudentOverviewDto(int HomeworkAssigned, int HomeworkSubmitted, double HomeworkCompletionRate);
    private record ProfileHomeworkRow(Guid Id);
    private record PendingHomework(Guid Id);
    private record StudentDashboard(List<PendingHomework> PendingHomework);
    private record GroupResults(double HomeworkCompletionRate);
    private record GroupOverview(GroupResults? Results);
}
