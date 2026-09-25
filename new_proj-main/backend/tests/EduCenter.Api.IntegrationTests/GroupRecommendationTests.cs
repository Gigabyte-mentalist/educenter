using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduCenter.Infrastructure.Seed;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// Group Recommendation Engine (docs/00_TZ.md §12, audit §3.9): ranks a lead's branch-visible open groups
/// by course-name match, same branch, free seats and whether the group is already active. No fake "AI
/// match" — a plain, explainable score over data the domain actually has.
/// </summary>
[Collection("Api")]
public class GroupRecommendationTests(EduCenterWebApplicationFactory factory)
{
    [Fact]
    public async Task WithoutToken_ReturnsUnauthorized()
    {
        var response = await factory.CreateClient().GetAsync($"/api/leads/{Guid.NewGuid()}/group-recommendations");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RanksCourseAndBranchMatchAboveAnUnrelatedFullGroup()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var branchId = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;

        var matchingCourse = await (await admin.PostAsJsonAsync("/api/courses",
            new { name = $"IELTS Rec {Guid.NewGuid():N}"[..24], levelCategory = "IELTS" })).Content.ReadFromJsonAsync<CourseDto>();
        var matchingGroup = await (await admin.PostAsJsonAsync("/api/groups",
            new { branchId, courseId = matchingCourse!.Id, name = $"REC-MATCH-{Guid.NewGuid():N}"[..20], capacity = 10 }))
            .Content.ReadFromJsonAsync<GroupDto>();

        var otherCourse = await (await admin.PostAsJsonAsync("/api/courses",
            new { name = $"General {Guid.NewGuid():N}"[..24], levelCategory = "General" })).Content.ReadFromJsonAsync<CourseDto>();
        var fullGroup = await (await admin.PostAsJsonAsync("/api/groups",
            new { branchId, courseId = otherCourse!.Id, name = $"REC-FULL-{Guid.NewGuid():N}"[..20], capacity = 1 }))
            .Content.ReadFromJsonAsync<GroupDto>();

        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        var lead = await (await operatorClient.PostAsJsonAsync("/api/leads", new
        {
            branchId, fullName = "Recommendation Lead", phone = "+998900000088", source = "Website", interestedCourse = "IELTS",
        })).Content.ReadFromJsonAsync<LeadDto>();

        // Fill the unrelated group's one seat so it has zero free seats and must be excluded entirely.
        var fillEmail = $"rec-fill-{Guid.NewGuid():N}@demo-center.test";
        var fillLead = await (await operatorClient.PostAsJsonAsync("/api/leads", new
        {
            branchId, fullName = "Filler", phone = "+998900000089", source = "Website", interestedCourse = "General",
        })).Content.ReadFromJsonAsync<LeadDto>();
        Assert.Equal(HttpStatusCode.Created, (await operatorClient.PostAsJsonAsync($"/api/leads/{fillLead!.Id}/convert",
            new { groupId = fullGroup!.Id, email = fillEmail, password = DbSeeder.DemoPassword })).StatusCode);

        var recommendations = await operatorClient.GetFromJsonAsync<List<GroupRecommendationDto>>(
            $"/api/leads/{lead!.Id}/group-recommendations");

        // The demo seed already has other genuinely-matching IELTS groups in this branch, so this new group
        // isn't guaranteed to rank first — only that it scores well and the zero-seat group is excluded.
        Assert.NotNull(recommendations);
        Assert.DoesNotContain(recommendations!, r => r.GroupId == fullGroup.Id);
        var match = Assert.Single(recommendations!, r => r.GroupId == matchingGroup!.Id);
        Assert.True(match.MatchPercent >= 70, $"Expected a strong match, got {match.MatchPercent}%.");
        Assert.Contains(match.Reasons, r => r.Contains("kursiga"));
    }

    [Fact]
    public async Task Administrator_DoesNotSeeGroupsOutsideTheirBranch()
    {
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var adminBranch = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var otherBranch = (await superAdmin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))!
            .First(b => b.Id != adminBranch).Id;

        var course = await (await superAdmin.PostAsJsonAsync("/api/courses",
            new { name = $"Other Branch Course {Guid.NewGuid():N}"[..30], levelCategory = "IELTS" })).Content.ReadFromJsonAsync<CourseDto>();
        var otherGroup = await (await superAdmin.PostAsJsonAsync("/api/groups",
            new { branchId = otherBranch, courseId = course!.Id, name = $"REC-OTHER-{Guid.NewGuid():N}"[..20], capacity = 10 }))
            .Content.ReadFromJsonAsync<GroupDto>();

        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        var lead = await (await operatorClient.PostAsJsonAsync("/api/leads", new
        {
            branchId = adminBranch, fullName = "Cross Branch Lead", phone = "+998900000090", source = "Website", interestedCourse = "IELTS",
        })).Content.ReadFromJsonAsync<LeadDto>();

        var recommendations = await admin.GetFromJsonAsync<List<GroupRecommendationDto>>(
            $"/api/leads/{lead!.Id}/group-recommendations");

        Assert.NotNull(recommendations);
        Assert.DoesNotContain(recommendations!, r => r.GroupId == otherGroup!.Id);
    }

    // ---- S06.01: level and preferred time -----------------------------------------------------------

    [Fact]
    public async Task LevelFit_RanksTheSameLevelAboveOneStepAwayAboveTwoStepsAway()
    {
        var (admin, operatorClient, branchId) = await SetupAsync();
        var (courseName, courseId) = await CreateCourseAsync(admin);
        var exact = await CreateGroupAsync(admin, branchId, courseId, "B1");
        var oneStep = await CreateGroupAsync(admin, branchId, courseId, "A2");
        var twoSteps = await CreateGroupAsync(admin, branchId, courseId, "C1");

        var lead = await CreateLeadAsync(operatorClient, branchId, courseName, currentLevel: "B1");
        var recommendations = (await operatorClient.GetFromJsonAsync<List<GroupRecommendationDto>>($"/api/leads/{lead.Id}/group-recommendations"))!;

        var exactRec = Assert.Single(recommendations, r => r.GroupId == exact);
        var oneStepRec = Assert.Single(recommendations, r => r.GroupId == oneStep);
        var twoStepsRec = Assert.Single(recommendations, r => r.GroupId == twoSteps);
        Assert.True(exactRec.MatchPercent > oneStepRec.MatchPercent, "Same level must outrank one step away.");
        Assert.True(oneStepRec.MatchPercent > twoStepsRec.MatchPercent, "One step away must outrank two.");
        Assert.Contains("Daraja mos (B1)", exactRec.Reasons);
        Assert.Contains("Daraja yaqin (A2)", oneStepRec.Reasons);
        Assert.DoesNotContain(twoStepsRec.Reasons, r => r.StartsWith("Daraja"));
    }

    [Fact]
    public async Task PreferredTime_RanksTheGroupThatMeetsInThatWindowHigher()
    {
        var (admin, operatorClient, branchId) = await SetupAsync();
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var (courseName, courseId) = await CreateCourseAsync(admin);
        var evening = await CreateGroupAsync(admin, branchId, courseId, "E");
        var morning = await CreateGroupAsync(admin, branchId, courseId, "M");

        // Tashkent is UTC+5: 13:00 UTC is 18:00 there (evening), 04:00 UTC is 09:00 (morning).
        var day = DateTimeOffset.UtcNow.Date.AddDays(10);
        await AddLessonAsync(admin, superAdmin, branchId, evening, new DateTimeOffset(day.AddHours(13), TimeSpan.Zero));
        await AddLessonAsync(admin, superAdmin, branchId, morning, new DateTimeOffset(day.AddHours(4), TimeSpan.Zero));

        var lead = await CreateLeadAsync(operatorClient, branchId, courseName, preferredTime: "Evening");
        var recommendations = (await operatorClient.GetFromJsonAsync<List<GroupRecommendationDto>>($"/api/leads/{lead.Id}/group-recommendations"))!;

        var eveningRec = Assert.Single(recommendations, r => r.GroupId == evening);
        var morningRec = Assert.Single(recommendations, r => r.GroupId == morning);
        Assert.True(eveningRec.MatchPercent > morningRec.MatchPercent);
        Assert.Contains("Qulay vaqtda (kechqurun)", eveningRec.Reasons);
        Assert.DoesNotContain(morningRec.Reasons, r => r.StartsWith("Qulay vaqtda"));
    }

    [Fact]
    public async Task WithoutLevelOrTime_AFactorWithNothingToCompareIsLeftOutNotScoredZero()
    {
        var (admin, operatorClient, branchId) = await SetupAsync();
        var (courseName, courseId) = await CreateCourseAsync(admin);
        var group = await CreateGroupAsync(admin, branchId, courseId, "");   // its name names no level, and it has no lessons

        async Task<GroupRecommendationDto> RecommendationForAsync(string? level, string? time)
        {
            var lead = await CreateLeadAsync(operatorClient, branchId, courseName, currentLevel: level, preferredTime: time);
            var all = (await operatorClient.GetFromJsonAsync<List<GroupRecommendationDto>>($"/api/leads/{lead.Id}/group-recommendations"))!;
            return Assert.Single(all, r => r.GroupId == group);
        }

        var baseline = await RecommendationForAsync(null, null);
        var levelOnly = await RecommendationForAsync("B1", null);          // the group names no level → nothing to compare
        var timeOnly = await RecommendationForAsync(null, "Evening");      // the group has no lessons → nothing to compare

        // Course + branch + seats (+ "already running") rescaled to 100%: a strong match, not a penalised one.
        Assert.True(baseline.MatchPercent >= 90, $"Expected a strong match, got {baseline.MatchPercent}%.");
        Assert.Equal(baseline.MatchPercent, levelOnly.MatchPercent);
        Assert.Equal(baseline.MatchPercent, timeOnly.MatchPercent);
        Assert.DoesNotContain(levelOnly.Reasons.Concat(timeOnly.Reasons), r => r.StartsWith("Daraja") || r.StartsWith("Qulay vaqtda"));
    }

    [Fact]
    public async Task Scores_StayBetweenZeroAndOneHundred()
    {
        var (admin, operatorClient, branchId) = await SetupAsync();
        var (courseName, courseId) = await CreateCourseAsync(admin);
        await CreateGroupAsync(admin, branchId, courseId, "B1");

        var lead = await CreateLeadAsync(operatorClient, branchId, courseName, currentLevel: "B1", preferredTime: "Morning,Evening,Weekend");
        var recommendations = (await operatorClient.GetFromJsonAsync<List<GroupRecommendationDto>>($"/api/leads/{lead.Id}/group-recommendations"))!;

        Assert.NotEmpty(recommendations);
        Assert.All(recommendations, r => Assert.InRange(r.MatchPercent, 0, 100));
        Assert.Equal(recommendations.OrderByDescending(r => r.MatchPercent).Select(r => r.MatchPercent), recommendations.Select(r => r.MatchPercent));
    }

    [Fact]
    public async Task UpdatePreferences_SavesNormalisesAndClears_AndKeepsToTheLeadsBranch()
    {
        var (admin, operatorClient, branchId) = await SetupAsync();
        var lead = await CreateLeadAsync(operatorClient, branchId, "IELTS");

        var saved = await operatorClient.PatchAsJsonAsync($"/api/leads/{lead.Id}", new { preferredTime = "weekend, evening, evening", currentLevel = " B1 " });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var dto = (await saved.Content.ReadFromJsonAsync<LeadDto>())!;
        Assert.Equal("Evening,Weekend", dto.PreferredTime);
        Assert.Equal("B1", dto.CurrentLevel);
        Assert.Equal("Evening,Weekend", (await operatorClient.GetFromJsonAsync<LeadDto>($"/api/leads/{lead.Id}"))!.PreferredTime);

        // A field left out stays; an empty one clears.
        var cleared = (await (await operatorClient.PatchAsJsonAsync($"/api/leads/{lead.Id}", new { preferredTime = "" })).Content.ReadFromJsonAsync<LeadDto>())!;
        Assert.Null(cleared.PreferredTime);
        Assert.Equal("B1", cleared.CurrentLevel);

        Assert.Equal(HttpStatusCode.BadRequest, (await operatorClient.PatchAsJsonAsync($"/api/leads/{lead.Id}", new { preferredTime = "Night" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await operatorClient.PatchAsJsonAsync($"/api/leads/{lead.Id}", new { currentLevel = new string('x', 51) })).StatusCode);

        var teacher = await AuthenticatedClientAsync("teacher@demo-center.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PatchAsJsonAsync($"/api/leads/{lead.Id}", new { currentLevel = "C1" })).StatusCode);

        // The administrator only looks after their own branch: a lead of another branch is out of reach.
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var otherBranch = (await superAdmin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))!.First(b => b.Id != branchId).Id;
        var foreign = await CreateLeadAsync(operatorClient, otherBranch, "IELTS");
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PatchAsJsonAsync($"/api/leads/{foreign.Id}", new { currentLevel = "C1" })).StatusCode);
    }

    [Fact]
    public async Task Create_TakesPreferencesAndRejectsAnUnknownTimeCode()
    {
        var (_, operatorClient, branchId) = await SetupAsync();

        var lead = await CreateLeadAsync(operatorClient, branchId, "IELTS", currentLevel: "IELTS Foundation", preferredTime: "Afternoon");
        var read = (await operatorClient.GetFromJsonAsync<LeadDto>($"/api/leads/{lead.Id}"))!;
        Assert.Equal("Afternoon", read.PreferredTime);
        Assert.Equal("IELTS Foundation", read.CurrentLevel);

        var bad = await operatorClient.PostAsJsonAsync("/api/leads", new
        {
            branchId, fullName = "Bad Time", phone = "+998900000091", source = "Website", interestedCourse = "IELTS", preferredTime = "Midnight",
        });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Theory]
    [InlineData("Morning, weekend", "Morning,Weekend")]
    [InlineData("Weekend,Morning,Morning", "Morning,Weekend")]
    [InlineData("  ", null)]
    [InlineData(null, null)]
    public void PreferredTimeCodes_NormaliseToACanonicalOrder(string? raw, string? expected)
    {
        Assert.True(Modules.Crm.Domain.PreferredTimeCodes.TryNormalize(raw, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("General English A1", 0)]
    [InlineData("b1", 2)]
    [InlineData("IELTS  Foundation", 5)]
    [InlineData("IELTS Advanced Plus", 7)]
    [InlineData("B1A1x", null)]      // a token, not a substring
    [InlineData("Business English", null)]
    public void LevelScale_FindsAWholeLevelTokenAnywhereInTheText(string text, int? expected) =>
        Assert.Equal(expected, Api.Crm.LevelScale.Find(text));

    // ---- helpers --------------------------------------------------------------------------------

    private async Task<(HttpClient Admin, HttpClient Operator, Guid BranchId)> SetupAsync()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        var branchId = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        return (admin, operatorClient, branchId);
    }

    /// <summary>A course whose name no other test uses, so the lead's "interested course" matches only the groups made here.</summary>
    private static async Task<(string Name, Guid Id)> CreateCourseAsync(HttpClient admin)
    {
        var name = $"Zq{Guid.NewGuid():N}"[..14];
        var course = await (await admin.PostAsJsonAsync("/api/courses", new { name, levelCategory = "General" })).Content.ReadFromJsonAsync<CourseDto>();
        return (name, course!.Id);
    }

    private static async Task<Guid> CreateGroupAsync(HttpClient admin, Guid branchId, Guid courseId, string levelToken)
    {
        var name = $"REC {levelToken} {Guid.NewGuid():N}"[..24].Trim();
        var response = await admin.PostAsJsonAsync("/api/groups", new { branchId, courseId, name, capacity = 10 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<GroupDto>())!.Id;
    }

    private static async Task<LeadDto> CreateLeadAsync(HttpClient client, Guid branchId, string interestedCourse,
        string? currentLevel = null, string? preferredTime = null)
    {
        var response = await client.PostAsJsonAsync("/api/leads", new
        {
            branchId, fullName = "Recommendation Prefs", phone = "+998900000092", source = "Website", interestedCourse,
            currentLevel, preferredTime,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LeadDto>())!;
    }

    /// <summary>A lesson for the group, taught by a fresh teacher in a fresh room so nothing in the shared database can clash with it.</summary>
    private static async Task AddLessonAsync(HttpClient admin, HttpClient superAdmin, Guid branchId, Guid groupId, DateTimeOffset startsAt)
    {
        var teacher = await superAdmin.PostAsJsonAsync("/api/users", new
        {
            fullName = $"Rec Teacher {Guid.NewGuid():N}", email = $"rec-teacher-{Guid.NewGuid():N}@demo-center.test",
            phone = "+998900000093", password = DbSeeder.DemoPassword, roleCode = "TEACHER", branchIds = new[] { branchId },
        });
        Assert.Equal(HttpStatusCode.Created, teacher.StatusCode);
        var teacherId = (await teacher.Content.ReadFromJsonAsync<GroupDto>())!.Id;

        var lesson = await admin.PostAsJsonAsync("/api/schedule/lessons", new
        {
            branchId, groupId, teacherId, roomName = $"R-{Guid.NewGuid():N}"[..8], startsAt, endsAt = startsAt.AddMinutes(90), topic = "Recommendation",
        });
        Assert.Equal(HttpStatusCode.Created, lesson.StatusCode);
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
    private record LeadDto(Guid Id, string? PreferredTime = null, string? CurrentLevel = null);
    private record GroupRecommendationDto(Guid GroupId, string GroupName, Guid CourseId, string CourseName,
        int FreeSeats, int Capacity, int MatchPercent, List<string> Reasons);
}
