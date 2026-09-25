using System.Net;
using System.Net.Http.Json;
using EduCenter.Api.Students;
using EduCenter.Infrastructure;
using EduCenter.Infrastructure.Seed;
using EduCenter.Modules.Ai.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// The Student Learning Profile (docs/21.09-audit-jurnali S04.02, TZ §7): eight figures per student — four IELTS skills, Grammar,
/// Vocabulary, Attendance, Homework — each with the number of results behind it and a trend, and null (never zero) where nothing
/// is behind it. Speaking comes from the exam only: an AI practice band is an estimate off a transcript, shown in the Speaking tab, not averaged in.
/// </summary>
[Collection("Api")]
public class LearningProfileTests(EduCenterWebApplicationFactory factory)
{
    private readonly LmsWorld world = new(factory);

    [Fact]
    public async Task Skills_AreAWeightedAverageOfTheNewestExams_WithASampleSizeAndATrend()
    {
        var c = await world.CreateAsync();
        var admin = await world.ClientAsync("admin@demo-center.test");
        var oldest = await ExamAsync(admin, c.GroupId, "2020-01-01");
        var middle = await ExamAsync(admin, c.GroupId, "2020-02-01");
        var newest = await ExamAsync(admin, c.GroupId, "2020-03-01");
        await RecordAsync(admin, oldest, c.First.Id, new { listening = 5.0, grammar = 60 });
        await RecordAsync(admin, middle, c.First.Id, new { listening = 6.0, grammar = 70 });
        await RecordAsync(admin, newest, c.First.Id, new { listening = 7.0, reading = 6.5, grammar = 80 });

        var profile = await ProfileAsync(admin, c.First.Id);

        // Newest first 7, 6, 5 with weights 5, 4, 3: (35 + 24 + 15) / 12 = 6.17. The newest two (6.5) sit above the oldest (5.0).
        Assert.Equal((6.2m, 3, 1), (profile.Listening.Value, profile.Listening.SampleSize, profile.Listening.Trend));
        // Percentages work the same way: (400 + 280 + 180) / 12 = 71.67.
        Assert.Equal((71.7m, 3, 1), (profile.Grammar.Value, profile.Grammar.SampleSize, profile.Grammar.Trend));
        // One result is an average of one, and there is nothing yet to compare it with.
        Assert.Equal((6.5m, 1), (profile.Reading.Value, profile.Reading.SampleSize));
        Assert.Null(profile.Reading.Trend);
    }

    [Fact]
    public async Task WithoutAnyResults_EveryFigureIsNull_NotZero()
    {
        var c = await world.CreateAsync();
        var profile = await ProfileAsync(await world.ClientAsync("admin@demo-center.test"), c.First.Id);

        foreach (var figure in new[] { profile.Listening, profile.Reading, profile.Writing, profile.Speaking, profile.Grammar,
                     profile.Vocabulary, profile.Attendance, profile.HomeworkCompletion })
        {
            Assert.Null(figure.Value);
            Assert.Equal(0, figure.SampleSize);
            Assert.Null(figure.Trend);
        }
    }

    [Fact]
    public async Task Writing_TakesTheTeachersCorrectionOverTheAi_AndIgnoresEssaysThatNeverFinished()
    {
        var c = await world.CreateAsync();
        var admin = await world.ClientAsync("admin@demo-center.test");
        await RecordAsync(admin, await ExamAsync(admin, c.GroupId, "2020-06-01"), c.First.Id, new { writing = 7.0 });

        await WithDbAsync(async db =>
        {
            var organizationId = await db.Students.Where(s => s.Id == c.First.Id).Select(s => s.OrganizationId).SingleAsync();
            var teacherId = await db.Users.Where(u => u.Email == "teacher@demo-center.test").Select(u => u.Id).SingleAsync();

            var corrected = NewEvaluation(organizationId, c.First);
            corrected.Complete(5m, 5m, 5m, 5m, 5.0m, "{}", Guid.NewGuid(), null);
            corrected.Override(6.5m, null, null, null, null, "Better than the AI thought.", teacherId);

            var failed = NewEvaluation(organizationId, c.First);
            failed.Fail("provider outage", null);
            var rejected = NewEvaluation(organizationId, c.First);
            rejected.Reject("prompt injection", null);
            db.WritingEvaluations.AddRange(corrected, failed, rejected);
            await db.SaveChangesAsync();
        });

        var writing = (await ProfileAsync(admin, c.First.Id)).Writing;

        // The essay (teacher's 6.5, not the AI's 5.0) is the newest, the exam (7.0) the one before: (32.5 + 28) / 9 = 6.72.
        // Had the AI's band stood it would be 5.9; had the unfinished essays counted there would be more than two results.
        Assert.Equal((6.7m, 2), (writing.Value, writing.SampleSize));
    }

    [Fact]
    public async Task Speaking_ComesOnlyFromExams_NeverFromTheAiPracticeEstimates()
    {
        var c = await world.CreateAsync();
        var admin = await world.ClientAsync("admin@demo-center.test");

        await WithDbAsync(async db =>
        {
            var organizationId = await db.Students.Where(s => s.Id == c.First.Id).Select(s => s.OrganizationId).SingleAsync();
            var session = new SpeakingSession(organizationId, c.First.Id, c.First.UserId, SpeakingMode.IeltsPart1, "Hometown");
            session.Complete(9m, 9m, 9m, 9.0m, "{}");
            db.SpeakingSessions.Add(session);
            await db.SaveChangesAsync();
        });

        var withPractice = (await ProfileAsync(admin, c.First.Id)).Speaking;
        Assert.Null(withPractice.Value);
        Assert.Equal(0, withPractice.SampleSize);

        await RecordAsync(admin, await ExamAsync(admin, c.GroupId, "2020-04-01"), c.First.Id, new { speaking = 6.0 });
        var withExam = (await ProfileAsync(admin, c.First.Id)).Speaking;
        Assert.Equal((6.0m, 1), (withExam.Value, withExam.SampleSize));
    }

    [Fact]
    public async Task GrammarAndVocabulary_AlsoCountMarkedHomeworkOfThatType_AndHomeworkCompletionFollowsTheOverview()
    {
        var c = await world.CreateAsync();
        var admin = await world.ClientAsync("admin@demo-center.test");
        var teacher = await world.ClientAsync("teacher@demo-center.test");
        var student = await world.ClientAsync(c.First.Email);

        var homework = (await (await teacher.PostAsJsonAsync("/api/homework", new
        {
            lessonId = c.LessonId, title = "Tenses", type = "Grammar", deadline = DateTimeOffset.UtcNow.AddDays(3), maxScore = 10,
        })).Content.ReadFromJsonAsync<IdDto>())!;
        var submission = (await (await student.PostAsJsonAsync($"/api/homework/{homework.Id}/submissions", new { contentText = "Done" }))
            .Content.ReadFromJsonAsync<IdDto>())!;

        // Handed in but not yet marked: it counts as completed homework, but there is no grade to average yet.
        var unmarked = await ProfileAsync(admin, c.First.Id);
        Assert.Equal((100m, 1), (unmarked.HomeworkCompletion.Value, unmarked.HomeworkCompletion.SampleSize));
        Assert.Null(unmarked.Grammar.Value);

        Assert.Equal(HttpStatusCode.NoContent,
            (await teacher.PatchAsJsonAsync($"/api/homework/submissions/{submission.Id}/review", new { score = 8, feedback = "Good" })).StatusCode);
        await RecordAsync(admin, await ExamAsync(admin, c.GroupId, "2020-05-01"), c.First.Id, new { grammar = 60 });

        var profile = await ProfileAsync(admin, c.First.Id);
        // 8 out of 10 marked just now is 80%, the exam's 60% is older: (80 × 5 + 60 × 4) / 9 = 71.1. A Grammar task says nothing of Vocabulary.
        Assert.Equal((71.1m, 2), (profile.Grammar.Value, profile.Grammar.SampleSize));
        Assert.Null(profile.Vocabulary.Value);

        var overview = (await admin.GetFromJsonAsync<OverviewRow>($"/api/students/{c.First.Id}/overview"))!;
        Assert.Equal((double)profile.HomeworkCompletion.Value!, overview.HomeworkCompletionRate);
    }

    [Fact]
    public async Task Attendance_FollowsTheSharedRule_AndTheOverviewShowsTheSameFigure()
    {
        var c = await world.CreateAsync();
        var admin = await world.ClientAsync("admin@demo-center.test");
        var teacher = await world.ClientAsync("teacher@demo-center.test");

        // Present, Late, Absent and an excused absence: the excused one counts for nothing, Late means the student came.
        foreach (var status in new[] { "Present", "Late", "Absent", "Excused" })
            await MarkAsync(teacher, await world.AddPastLessonAsync(c), c.First.Id, status);

        // A lesson that was cancelled took place for nobody, whatever was marked on it before.
        var cancelled = await world.AddPastLessonAsync(c);
        await MarkAsync(teacher, cancelled, c.First.Id, "Absent");
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/schedule/lessons/{cancelled}/cancel", new { reason = "Sinov" })).StatusCode);

        var attendance = (await ProfileAsync(admin, c.First.Id)).Attendance;
        Assert.Equal((66.7m, 3), (attendance.Value, attendance.SampleSize)); // 2 of 3 counted lessons
        Assert.Null(attendance.Trend); // fewer than six lessons say nothing about a direction

        var overview = (await admin.GetFromJsonAsync<OverviewRow>($"/api/students/{c.First.Id}/overview"))!;
        Assert.Equal((double)attendance.Value!, overview.AttendanceRate);
        Assert.Equal((2, 1), (overview.LessonsAttended, overview.LessonsAbsent));
    }

    [Fact]
    public async Task Access_FollowsTheStudentScope()
    {
        var mine = await world.CreateAsync(students: 2);
        var notMine = await world.CreateAsync(teacherStaffsGroup: false);

        var teacher = await world.ClientAsync("teacher@demo-center.test");
        Assert.Equal(HttpStatusCode.OK, (await teacher.GetAsync($"/api/students/{mine.First.Id}/learning-profile")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync($"/api/students/{notMine.First.Id}/learning-profile")).StatusCode);

        // A student reads their own profile and nobody else's — not even a classmate's.
        var student = await world.ClientAsync(mine.Students[0].Email);
        Assert.Equal(HttpStatusCode.OK, (await student.GetAsync($"/api/students/{mine.Students[0].Id}/learning-profile")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync($"/api/students/{mine.Students[1].Id}/learning-profile")).StatusCode);

        // A parent reads their own child's profile and no one else's; the operator, who may open any student of their branch, reads it too.
        var admin = await world.ClientAsync("admin@demo-center.test");
        var parentEmail = $"profile-parent-{Guid.NewGuid():N}@demo-center.test";
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync($"/api/students/{mine.Students[0].Id}/parents", new
        {
            fullName = "Profil Ota", phone = "+998901230002", email = parentEmail, password = DbSeeder.DemoPassword, relationship = "Father",
        })).StatusCode);
        var parent = await world.ClientAsync(parentEmail);
        Assert.Equal(HttpStatusCode.OK, (await parent.GetAsync($"/api/students/{mine.Students[0].Id}/learning-profile")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await parent.GetAsync($"/api/students/{mine.Students[1].Id}/learning-profile")).StatusCode);
        var operatorClient = await world.ClientAsync("operator@demo-center.test");
        Assert.Equal(HttpStatusCode.OK, (await operatorClient.GetAsync($"/api/students/{mine.First.Id}/learning-profile")).StatusCode);

        var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/students/{mine.First.Id}/learning-profile")).StatusCode);
    }

    // ---- the pure rules ---------------------------------------------------------------------------

    [Fact]
    public void WeightedAverage_FavoursTheNewest_AndLooksAtFiveAtMost()
    {
        Assert.Null(LearningProfileCalculator.WeightedAverage([]));
        Assert.Equal(6.5m, LearningProfileCalculator.WeightedAverage([6.5m]));
        Assert.Equal(74m / 12m, LearningProfileCalculator.WeightedAverage([7m, 6m, 5m]));
        // Weights 5, 4, 3, 2, 1 → (35 + 24 + 15 + 8 + 3) / 15; the sixth and seventh results are not looked at.
        Assert.Equal(85m / 15m, LearningProfileCalculator.WeightedAverage([7m, 6m, 5m, 4m, 3m, 9m, 9m]));
    }

    [Theory]
    [InlineData(new[] { 6.5, 6.5 }, 0)]       // level
    [InlineData(new[] { 6.5, 6.25 }, 1)]      // exactly a quarter band up is already a rise
    [InlineData(new[] { 6.5, 6.26 }, 0)]      // a hair under is not
    [InlineData(new[] { 6.0, 6.5 }, -1)]      // falling
    [InlineData(new[] { 7.0, 6.0, 5.0 }, 1)]  // three results: the newest two against the oldest
    [InlineData(new[] { 7.0, 7.0, 7.0, 5.0, 5.0, 5.0 }, 1)]     // the newest three against the three before them
    [InlineData(new[] { 7.0, 7.0, 7.0, 5.0, 5.0, 5.0, 9.0, 9.0 }, 1)] // anything older is ignored
    public void Trend_ComparesTheNewestWithTheOnesBefore_AgainstAThreshold(double[] newestFirst, int expected) =>
        Assert.Equal(expected, LearningProfileCalculator.Trend(newestFirst.Select(v => (decimal)v).ToList(), LearningProfileCalculator.BandTrendThreshold));

    [Fact]
    public void Trend_IsNullWhenThereIsNothingToCompare_OrTooLittleToTrust()
    {
        Assert.Null(LearningProfileCalculator.Trend([], 0.25m));
        Assert.Null(LearningProfileCalculator.Trend([6.5m], 0.25m));
        Assert.Null(LearningProfileCalculator.Trend([100m, 0m, 100m, 0m], 3m, group: 5, minSamples: 6));
        Assert.NotNull(LearningProfileCalculator.Trend([100m, 0m, 100m, 0m, 100m, 0m], 3m, group: 5, minSamples: 6));
    }

    [Fact]
    public void Band_SortsByDate_SoTheOrderResultsArriveInDoesNotMatter()
    {
        var day = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var shuffled = new[] { new ProfileSample(day.AddDays(2), 7m), new ProfileSample(day, 5m), new ProfileSample(day.AddDays(1), 6m) };
        var skill = LearningProfileCalculator.Band(shuffled);
        Assert.Equal((6.2m, 3, 1), (skill.Value, skill.SampleSize, skill.Trend));
        Assert.Equal(new SkillDto(null, 0, null), LearningProfileCalculator.Band([]));
    }

    [Fact]
    public void HomeworkCompletion_CountsEverythingGiven_ButTrendsOnlyWhatIsAlreadyDue()
    {
        var now = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var due = Enumerable.Range(1, 6).Select(i => (now.AddDays(-i), i > 3)).ToList(); // the three newest missed, the three older done
        var open = (now.AddDays(5), false);                                                // still open: not a miss

        var completion = LearningProfileCalculator.HomeworkCompletion([.. due, open], now);

        Assert.Equal((42.9m, 7, -1), (completion.Value, completion.SampleSize, completion.Trend)); // 3 of 7; newest due ones were missed
        Assert.Equal(new PercentDto(null, 0, null), LearningProfileCalculator.HomeworkCompletion([], now));
    }

    // ---- helpers ------------------------------------------------------------------------------------

    private static WritingEvaluation NewEvaluation(Guid organizationId, LmsWorld.Student student) =>
        new(organizationId, student.Id, student.UserId, WritingTaskType.General, "Write about your city.", "My city is big.", 4, "test-v1");

    private async Task WithDbAsync(Func<AppDbContext, Task> action)
    {
        using var scope = factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static async Task<Guid> ExamAsync(HttpClient admin, Guid groupId, string date)
    {
        var response = await admin.PostAsJsonAsync("/api/exams", new { groupId, name = $"Profile exam {date}", type = "Unit", date });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    private static async Task RecordAsync(HttpClient admin, Guid examId, Guid studentId, object scores)
    {
        var body = System.Text.Json.JsonSerializer.SerializeToNode(scores)!.AsObject();
        body["studentId"] = studentId;
        var response = await admin.PostAsJsonAsync($"/api/exams/{examId}/results", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task MarkAsync(HttpClient teacher, Guid lessonId, Guid studentId, string status) =>
        Assert.Equal(HttpStatusCode.NoContent, (await teacher.PostAsJsonAsync($"/api/lessons/{lessonId}/attendance",
            new { items = new[] { new { studentId, status } } })).StatusCode);

    private static async Task<Profile> ProfileAsync(HttpClient client, Guid studentId)
    {
        var response = await client.GetAsync($"/api/students/{studentId}/learning-profile");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Profile>())!;
    }

    private record IdDto(Guid Id);
    private record Figure(decimal? Value, int SampleSize, int? Trend);
    private record Profile(Figure Listening, Figure Reading, Figure Writing, Figure Speaking, Figure Grammar, Figure Vocabulary,
        Figure Attendance, Figure HomeworkCompletion);
    private record OverviewRow(int LessonsAttended, int LessonsAbsent, double AttendanceRate, double HomeworkCompletionRate);
}
