using System.Net;
using System.Net.Http.Json;
using EduCenter.Api.Automation;
using EduCenter.Api.Students;
using EduCenter.Infrastructure;
using EduCenter.Modules.Ai.Domain;
using EduCenter.Modules.Students.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// The learning signals of TZ §7 that were missing (docs/21.09-audit-jurnali S04.05): a weak Writing grammar, low Listening results and
/// a Reading band far behind the rest of the exam each raise a support recommendation with the numbers behind it, and tell the
/// group's main teacher. Each is a rule of the Automation Engine — switchable, with thresholds and actions the organization sets.
/// </summary>
[Collection("Api")]
public class LearningSignalTests(EduCenterWebApplicationFactory factory)
{
    private readonly LmsWorld world = new(factory);

    // ---- Writing → grammar weak -------------------------------------------------------------------

    [Fact]
    public async Task WeakWritingGrammar_InTheLastThreeEssays_RaisesACaseWithTheNumbers_AndTellsTheTeacher()
    {
        var c = await world.CreateAsync();
        await AddEssaysAsync(c, 5.0m, 5.0m, 4.5m); // oldest first

        await RunAsync(c);

        var raised = Assert.Single(await CasesAsync(c, SupportSignalCode.WritingGrammarWeak));
        Assert.Equal("Oxirgi 3 ta insho: grammatika 4.5, 5.0, 5.0 (chegara 5.5)", raised.Reason);
        Assert.Equal((c.GroupId, SupportCaseStatus.Recommended, SupportCaseSource.System), (raised.GroupId, raised.Status, raised.Source));
        Assert.Equal(1, await TeacherNotificationsAsync(c));

        // The same weakness seen again on the next sweep is not news.
        await RunAsync(c);
        Assert.Single(await CasesAsync(c, SupportSignalCode.WritingGrammarWeak));
        Assert.Equal(1, await TeacherNotificationsAsync(c));

        // The reviewer reads the numbers at /support without opening the profile.
        var admin = await world.ClientAsync("admin@demo-center.test");
        var listed = (await admin.GetFromJsonAsync<PagedResponse<CaseDto>>($"/api/support-cases?studentId={c.First.Id}"))!.Items;
        Assert.Contains(listed, x => x.SignalCode == "WRITING_GRAMMAR_WEAK" && x.Reason.Contains("4.5, 5.0, 5.0"));
    }

    [Fact]
    public async Task WritingGrammar_StaysQuiet_WhenThereAreTooFewEssays_OneIsGoodEnough_TheTeacherCorrectedIt_OrItNeverFinished()
    {
        var fewEssays = await world.CreateAsync();
        await AddEssaysAsync(fewEssays, 4.5m, 4.5m);

        var oneGood = await world.CreateAsync();
        await AddEssaysAsync(oneGood, 4.5m, 6.5m, 4.5m);

        // The AI said 4.5 three times, but the teacher put the newest right: their grammar band is what counts.
        var corrected = await world.CreateAsync();
        await AddEssaysAsync(corrected, 4.5m, 4.5m);
        await AddEssayAsync(corrected, 4.5m, teacherGrammar: 6.5m);

        var unfinished = await world.CreateAsync();
        await AddEssaysAsync(unfinished, 4.5m, 4.5m);
        await AddEssayAsync(unfinished, 4.5m, failed: true);

        await RunAsync(fewEssays);

        foreach (var c in new[] { fewEssays, oneGood, corrected, unfinished })
            Assert.Empty(await CasesAsync(c, SupportSignalCode.WritingGrammarWeak));
    }

    // ---- Listening → several low results -----------------------------------------------------------

    [Fact]
    public async Task LowListening_InTheLastTwoExams_RaisesACaseWithTheNumbers()
    {
        var c = await world.CreateAsync();
        var admin = await world.ClientAsync("admin@demo-center.test");
        await RecordExamAsync(admin, c, "2020-01-01", new { listening = 4.5 });
        await RecordExamAsync(admin, c, "2020-02-01", new { listening = 4.0 });

        await RunAsync(c);

        var raised = Assert.Single(await CasesAsync(c, SupportSignalCode.ListeningLow));
        Assert.Equal("Oxirgi 2 ta imtihon: Listening 4.0, 4.5 (chegara 5.0)", raised.Reason);
        Assert.Equal(1, await TeacherNotificationsAsync(c));
    }

    [Fact]
    public async Task Listening_StaysQuiet_WithOneExam_OrWhenOneOfTheLastTwoIsNotBelowTheLimit()
    {
        var admin = await world.ClientAsync("admin@demo-center.test");
        var oneExam = await world.CreateAsync();
        await RecordExamAsync(admin, oneExam, "2020-01-01", new { listening = 3.5 });

        // 5.0 is the limit itself — "below" means strictly below.
        var atTheLimit = await world.CreateAsync();
        await RecordExamAsync(admin, atTheLimit, "2020-01-01", new { listening = 4.0 });
        await RecordExamAsync(admin, atTheLimit, "2020-02-01", new { listening = 5.0 });

        // An exam that did not grade Listening is not a low Listening result, nor a high one.
        var skipped = await world.CreateAsync();
        await RecordExamAsync(admin, skipped, "2020-01-01", new { listening = 4.0 });
        await RecordExamAsync(admin, skipped, "2020-02-01", new { reading = 4.0 });

        await RunAsync(oneExam);

        foreach (var c in new[] { oneExam, atTheLimit, skipped })
            Assert.Empty(await CasesAsync(c, SupportSignalCode.ListeningLow));
    }

    // ---- Reading → time management -----------------------------------------------------------------

    [Fact]
    public async Task ReadingFarBehindTheRestOfTheExam_RaisesACaseWithTheNumbers()
    {
        var c = await world.CreateAsync();
        var admin = await world.ClientAsync("admin@demo-center.test");
        await RecordExamAsync(admin, c, "2020-01-01", new { listening = 7.0, writing = 7.0, speaking = 6.5, reading = 5.5 });

        await RunAsync(c);

        var raised = Assert.Single(await CasesAsync(c, SupportSignalCode.ReadingTime));
        Assert.Equal("Oxirgi imtihonda Reading 5.5, boshqa bo'limlar o'rtachasi 6.8 (farq 1.3, chegara 1.0)", raised.Reason);
    }

    [Fact]
    public async Task Reading_StaysQuiet_WhenTheGapIsSmall_ThereIsNothingToCompareWith_OrOnlyAnOlderExamShowedIt()
    {
        var admin = await world.ClientAsync("admin@demo-center.test");
        var smallGap = await world.CreateAsync();
        await RecordExamAsync(admin, smallGap, "2020-01-01", new { listening = 7.0, writing = 7.0, reading = 6.5 });

        var oneOther = await world.CreateAsync();
        await RecordExamAsync(admin, oneOther, "2020-01-01", new { listening = 7.5, reading = 4.5 });

        // Reading was far behind in the first exam but has caught up in the latest, which is the one that is judged.
        var recovered = await world.CreateAsync();
        await RecordExamAsync(admin, recovered, "2020-01-01", new { listening = 7.0, writing = 7.0, reading = 5.0 });
        await RecordExamAsync(admin, recovered, "2020-02-01", new { listening = 7.0, writing = 7.0, reading = 7.0 });

        await RunAsync(smallGap);

        foreach (var c in new[] { smallGap, oneOther, recovered })
            Assert.Empty(await CasesAsync(c, SupportSignalCode.ReadingTime));
    }

    // ---- Speaking → low fluency --------------------------------------------------------------------

    [Fact]
    public async Task LowSpeakingFluency_InTheLastThreeSharedSessions_RaisesACaseWithNumbersOnly()
    {
        var c = await world.CreateAsync();
        await AddSpeakingSessionsAsync(c, 4.5m, 4.0m, 4.5m); // oldest first

        await RunAsync(c);

        var raised = Assert.Single(await CasesAsync(c, SupportSignalCode.SpeakingFluencyLow));
        // Only numbers — the reviewer is told how the student did, never what they said.
        Assert.Equal("Oxirgi 3 ta Speaking mashqi: fluency 4.5, 4.0, 4.5 (chegara 5.0)", raised.Reason);
        Assert.Equal(1, await TeacherNotificationsAsync(c));
    }

    [Fact]
    public async Task SpeakingFluency_StaysQuiet_WithTooFewSessions_APrivateOne_ATeacherCorrectedOne_OrAnUnfinishedOne()
    {
        var few = await world.CreateAsync();
        await AddSpeakingSessionsAsync(few, 4.0m, 4.0m);

        // Recorded before the student was told staff could read it: it does not count, however low.
        var withPrivate = await world.CreateAsync();
        await AddSpeakingSessionsAsync(withPrivate, 4.0m, 4.0m);
        var privateOne = await AddSpeakingAsync(withPrivate, 4.0m);
        await MakePrivateAsync(privateOne);

        // The AI said 4.0 three times; the teacher listened and put the newest at 6.0.
        var corrected = await world.CreateAsync();
        await AddSpeakingSessionsAsync(corrected, 4.0m, 4.0m);
        await AddSpeakingAsync(corrected, 4.0m, teacherFluency: 6.0m);

        var unfinished = await world.CreateAsync();
        await AddSpeakingSessionsAsync(unfinished, 4.0m, 4.0m);
        await AddSpeakingAsync(unfinished, 4.0m, failed: true);

        await RunAsync(few);

        foreach (var c in new[] { few, withPrivate, corrected, unfinished })
            Assert.Empty(await CasesAsync(c, SupportSignalCode.SpeakingFluencyLow));
    }

    // ---- idempotency -------------------------------------------------------------------------------

    [Fact]
    public async Task ASignalIsRaisedOncePerTrigger_NotAgainAfterItIsDismissed_ButAgainForANewWeakResult()
    {
        var c = await world.CreateAsync();
        var admin = await world.ClientAsync("admin@demo-center.test");
        await RecordExamAsync(admin, c, "2020-01-01", new { listening = 4.5 });
        await RecordExamAsync(admin, c, "2020-02-01", new { listening = 4.0 });

        await RunAsync(c);
        await RunAsync(c);
        var first = Assert.Single(await CasesAsync(c, SupportSignalCode.ListeningLow));

        // The reviewer dismisses it: the same two results must not bring it back on the next sweep.
        var dismissed = await admin.PostAsJsonAsync($"/api/support-cases/{first.Id}/dismiss", new { note = "Bilamiz" });
        Assert.True(dismissed.IsSuccessStatusCode);
        await RunAsync(c);
        Assert.Single(await CasesAsync(c, SupportSignalCode.ListeningLow));

        // A third weak exam is a new trigger.
        await RecordExamAsync(admin, c, "2020-03-01", new { listening = 4.0 });
        await RunAsync(c);
        Assert.Equal(2, (await CasesAsync(c, SupportSignalCode.ListeningLow)).Count);
    }

    [Fact]
    public async Task AStudentWhoLeftTheirGroup_IsNotFlagged()
    {
        var c = await world.CreateAsync();
        await AddEssaysAsync(c, 4.5m, 4.5m, 4.5m);
        var admin = await world.ClientAsync("admin@demo-center.test");
        var enrollment = (await admin.GetFromJsonAsync<OverviewDto>($"/api/students/{c.First.Id}/overview"))!.Enrollments.Single().Id;
        Assert.True((await admin.PostAsJsonAsync($"/api/students/{c.First.Id}/enrollments/{enrollment}/drop", new { reason = "Sinov" })).IsSuccessStatusCode);

        await RunAsync(c);

        Assert.Empty(await CasesAsync(c, SupportSignalCode.WritingGrammarWeak));
    }

    // ---- the organization's settings -----------------------------------------------------------------

    [Fact]
    public async Task TheThresholdsAreTheOrganizationsToSet_TheNumberOfEssaysAndTheBandLimit()
    {
        var twoEssays = await world.CreateAsync();
        await AddEssaysAsync(twoEssays, 4.5m, 4.5m);
        var borderline = await world.CreateAsync();
        await AddEssaysAsync(borderline, 5.0m, 5.0m, 5.0m);

        await RunAsync(twoEssays);
        Assert.Empty(await CasesAsync(twoEssays, SupportSignalCode.WritingGrammarWeak)); // the default asks for three
        Assert.Single(await CasesAsync(borderline, SupportSignalCode.WritingGrammarWeak)); // 5.0 is below the default 5.5

        await WithRuleAsync("WRITING_GRAMMAR_SIGNAL", new { enabled = true, parameters = new { results = 2, maxBand = 40 } }, async () =>
        {
            var another = await world.CreateAsync();
            await AddEssaysAsync(another, 3.5m, 3.5m);
            var notLowEnough = await world.CreateAsync();
            await AddEssaysAsync(notLowEnough, 4.5m, 4.5m);

            await RunAsync(another);

            var raised = Assert.Single(await CasesAsync(another, SupportSignalCode.WritingGrammarWeak));
            Assert.Equal("Oxirgi 2 ta insho: grammatika 3.5, 3.5 (chegara 4.0)", raised.Reason);
            Assert.Empty(await CasesAsync(notLowEnough, SupportSignalCode.WritingGrammarWeak)); // 4.5 is not below a limit of 4.0
        });
    }

    [Fact]
    public async Task EachActionIsSwitchable_AndADisabledRuleDoesNothing()
    {
        await WithRuleAsync("LISTENING_LOW_SIGNAL", new { enabled = true, actions = new { raiseSupport = false, notifyTeacher = true } }, async () =>
        {
            var c = await world.CreateAsync();
            await RecordTwoLowListeningAsync(c);
            await RunAsync(c);
            Assert.Empty(await CasesAsync(c, SupportSignalCode.ListeningLow));
            Assert.Equal(1, await TeacherNotificationsAsync(c));
        });

        await WithRuleAsync("LISTENING_LOW_SIGNAL", new { enabled = true, actions = new { raiseSupport = true, notifyTeacher = false } }, async () =>
        {
            var c = await world.CreateAsync();
            await RecordTwoLowListeningAsync(c);
            await RunAsync(c);
            Assert.Single(await CasesAsync(c, SupportSignalCode.ListeningLow));
            Assert.Equal(0, await TeacherNotificationsAsync(c));
        });

        await WithRuleAsync("LISTENING_LOW_SIGNAL", new { enabled = false }, async () =>
        {
            var c = await world.CreateAsync();
            await RecordTwoLowListeningAsync(c);
            await RunAsync(c);
            Assert.Empty(await CasesAsync(c, SupportSignalCode.ListeningLow));
            Assert.Equal(0, await TeacherNotificationsAsync(c));
        });
    }

    [Fact]
    public async Task TheRulesAppearInTheCatalog_WithBandsAsTenths_AndBadValuesAreRefused()
    {
        var superAdmin = await world.ClientAsync("superadmin@demo-center.test");
        var rules = (await superAdmin.GetFromJsonAsync<List<RuleDto>>("/api/automation/rules"))!;

        var writing = rules.Single(r => r.Code == "WRITING_GRAMMAR_SIGNAL");
        var limit = writing.Parameters.Single(p => p.Key == "maxBand");
        Assert.Equal((10, 30, 70, 55, 55), (limit.Scale, limit.Min, limit.Max, limit.Default, limit.Value)); // band 5.5, stored as 55
        Assert.Equal(new[] { "notifyTeacher", "raiseSupport" }, writing.Actions.Select(a => a.Key).Order());
        Assert.Contains(rules, r => r.Code == "LISTENING_LOW_SIGNAL");
        Assert.Contains(rules, r => r.Code == "READING_TIME_SIGNAL");
        Assert.Contains(rules, r => r.Code == "SPEAKING_FLUENCY_SIGNAL");

        var refused = await superAdmin.PutAsJsonAsync("/api/automation/rules/WRITING_GRAMMAR_SIGNAL", new { enabled = true, parameters = new { maxBand = 75 } });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("3.0 dan 7.0", await refused.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RunNow_ReportsTheLearningSignals_AndTheActivityLogNamesTheRule()
    {
        var superAdmin = await world.ClientAsync("superadmin@demo-center.test");
        var c = await world.CreateAsync();
        await AddEssaysAsync(c, 4.5m, 4.5m, 4.5m);

        var run = (await (await superAdmin.PostAsync("/api/automation/run", null)).Content.ReadFromJsonAsync<RunNowDto>())!;

        Assert.True(run.LearningSignals >= 1);
        Assert.Single(await CasesAsync(c, SupportSignalCode.WritingGrammarWeak));
        var log = (await superAdmin.GetFromJsonAsync<List<RunDto>>("/api/automation/runs"))!;
        Assert.Contains(log, r => r.RuleCode == "WRITING_GRAMMAR_SIGNAL" && r.Summary.Contains("support tavsiyasi"));
    }

    // ---- the questions the rules ask (pure) -------------------------------------------------------------

    [Fact]
    public void AllBelow_NeedsEnoughResults_AndEveryOneStrictlyBelow()
    {
        Assert.Equal([4.5m, 5.0m], LearningProfileCalculator.AllBelow([4.5m, 5.0m, 9m], 2, 5.5m)); // only the newest two are looked at
        Assert.Null(LearningProfileCalculator.AllBelow([4.5m], 2, 5.5m));
        Assert.Null(LearningProfileCalculator.AllBelow([4.5m, 5.5m], 2, 5.5m));
        Assert.Null(LearningProfileCalculator.AllBelow([6m, 4m], 2, 5.5m));
    }

    [Fact]
    public void ReadingGap_ComparesWithTheMeanOfAtLeastTwoOtherSections()
    {
        var (mean, gap) = LearningProfileCalculator.ReadingGap(5.5m, [7m, 7m, 6.5m])!.Value;
        Assert.Equal(20.5m / 3, mean);
        Assert.Equal(20.5m / 3 - 5.5m, gap);
        Assert.Null(LearningProfileCalculator.ReadingGap(5.5m, [7m]));
        Assert.True(LearningProfileCalculator.ReadingGap(7m, [6m, 6m])!.Value.Gap < 0); // ahead of the rest is a negative gap
    }

    // ---- helpers ------------------------------------------------------------------------------------------

    private async Task RunAsync(LmsWorld.Classroom c)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var organizationId = await db.Groups.Where(g => g.Id == c.GroupId).Select(g => g.OrganizationId).SingleAsync();
        await scope.ServiceProvider.GetRequiredService<FollowUpRules>().RunLearningSignalsAsync(organizationId);
    }

    private async Task<List<SupportCase>> CasesAsync(LmsWorld.Classroom c, string signalCode)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().SupportCases
            .Where(s => s.StudentId == c.First.Id && s.SignalCode == signalCode).OrderBy(s => s.CreatedAt).ToListAsync();
    }

    /// <summary>The messages the group's main teacher got about this student: one per signal that was told.</summary>
    private async Task<int> TeacherNotificationsAsync(LmsWorld.Classroom c)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var teacherId = await db.Users.Where(u => u.Email == "teacher@demo-center.test").Select(u => u.Id).SingleAsync();
        var link = $"/students/{c.First.Id}";
        return await db.Notifications.CountAsync(n => n.UserId == teacherId && n.Type == "LEARNING_SIGNAL" && n.Link == link);
    }

    /// <summary>Essays finished one after another, oldest first — each completes at its own moment, which is what orders them.</summary>
    private async Task AddEssaysAsync(LmsWorld.Classroom c, params decimal[] grammarBands)
    {
        foreach (var band in grammarBands) await AddEssayAsync(c, band);
    }

    private async Task AddEssayAsync(LmsWorld.Classroom c, decimal grammar, decimal? teacherGrammar = null, bool failed = false)
    {
        await Task.Delay(5); // distinct completion moments, whatever the clock's resolution
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var organizationId = await db.Students.Where(s => s.Id == c.First.Id).Select(s => s.OrganizationId).SingleAsync();
        var teacherId = await db.Users.Where(u => u.Email == "teacher@demo-center.test").Select(u => u.Id).SingleAsync();

        var essay = new WritingEvaluation(organizationId, c.First.Id, c.First.UserId, WritingTaskType.General, "Write about your city.", "My city is big.", 4, "test-v1");
        if (failed) essay.Fail("provider outage", null);
        else essay.Complete(5m, 5m, 5m, grammar, 5m, "{}", Guid.NewGuid(), null);
        if (teacherGrammar is { } corrected) essay.Override(6m, null, null, null, corrected, "Tuzatildi", teacherId);
        db.WritingEvaluations.Add(essay);
        await db.SaveChangesAsync();
    }

    private async Task AddSpeakingSessionsAsync(LmsWorld.Classroom c, params decimal[] fluencyBands)
    {
        foreach (var band in fluencyBands) await AddSpeakingAsync(c, band);
    }

    /// <summary>A finished practice, shared with staff as every new one is; oldest first, each finishing at its own moment.</summary>
    private async Task<Guid> AddSpeakingAsync(LmsWorld.Classroom c, decimal fluency, decimal? teacherFluency = null, bool failed = false)
    {
        await Task.Delay(5);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var organizationId = await db.Students.Where(s => s.Id == c.First.Id).Select(s => s.OrganizationId).SingleAsync();
        var teacherId = await db.Users.Where(u => u.Email == "teacher@demo-center.test").Select(u => u.Id).SingleAsync();

        var session = new SpeakingSession(organizationId, c.First.Id, c.First.UserId, SpeakingMode.IeltsPart1, "Hometown");
        if (failed) session.Fail("provider outage");
        else session.Complete(fluency, 6m, 6m, 6m, "{}");
        if (teacherFluency is { } corrected) session.Override(6m, corrected, null, null, "Eshitdim", teacherId);
        db.SpeakingSessions.Add(session);
        await db.SaveChangesAsync();
        return session.Id;
    }

    /// <summary>What every session recorded before staff could read practice looks like: there is no API for it, by design.</summary>
    private async Task MakePrivateAsync(Guid sessionId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE speaking_sessions SET \"SharedWithStaff\" = false WHERE \"Id\" = {sessionId}"));
    }

    private static async Task RecordExamAsync(HttpClient admin, LmsWorld.Classroom c, string date, object scores)
    {
        var exam = await admin.PostAsJsonAsync("/api/exams", new { groupId = c.GroupId, name = $"Signal exam {date}", type = "Unit", date });
        Assert.Equal(HttpStatusCode.Created, exam.StatusCode);
        var examId = (await exam.Content.ReadFromJsonAsync<IdDto>())!.Id;

        var body = System.Text.Json.JsonSerializer.SerializeToNode(scores)!.AsObject();
        body["studentId"] = c.First.Id;
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync($"/api/exams/{examId}/results", body)).StatusCode);
    }

    private async Task RecordTwoLowListeningAsync(LmsWorld.Classroom c)
    {
        var admin = await world.ClientAsync("admin@demo-center.test");
        await RecordExamAsync(admin, c, "2020-01-01", new { listening = 4.0 });
        await RecordExamAsync(admin, c, "2020-02-01", new { listening = 4.0 });
    }

    /// <summary>Applies the organization's settings for one rule, runs the body, and puts the catalog defaults back — the settings are
    /// shared by every test in the run.</summary>
    private async Task WithRuleAsync(string code, object settings, Func<Task> body)
    {
        var superAdmin = await world.ClientAsync("superadmin@demo-center.test");
        Assert.Equal(HttpStatusCode.OK, (await superAdmin.PutAsJsonAsync($"/api/automation/rules/{code}", settings)).StatusCode);
        try { await body(); }
        finally { await superAdmin.DeleteAsync($"/api/automation/rules/{code}"); }
    }

    private record IdDto(Guid Id);
    private record CaseDto(string SignalCode, string Reason);
    private record EnrollmentDto(Guid Id);
    private record OverviewDto(List<EnrollmentDto> Enrollments);
    private record ParameterDto(string Key, int Min, int Max, int Default, int Value, int Scale);
    private record ActionDto(string Key);
    private record RuleDto(string Code, List<ParameterDto> Parameters, List<ActionDto> Actions);
    private record RunNowDto(int LearningSignals);
    private record RunDto(string RuleCode, string Summary);
}
