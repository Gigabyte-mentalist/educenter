using System.Net;
using System.Net.Http.Json;
using EduCenter.Api.Users;
using EduCenter.Infrastructure;
using EduCenter.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// A teacher's portfolio (docs/21.09-audit-jurnali S05.02): About, credentials (IELTS, certificates) with an optional scan and an
/// administrator's confirmation, experience. The rules that matter: only the teacher, their branch's administrator and the Super Admin
/// touch it; a teacher cannot confirm their own certificate; changing what was confirmed withdraws the confirmation; the scan is never
/// served to anyone else.
/// </summary>
[Collection("Api")]
public class TeacherPortfolioTests(EduCenterWebApplicationFactory factory)
{
    private readonly LmsWorld world = new(factory);

    // ---- the teacher's own portfolio -----------------------------------------------------------

    [Fact]
    public async Task ATeacher_FillsInTheirOwnPortfolio_AboutCredentialsAndExperience()
    {
        var t = await NewTeacherAsync();

        var about = await t.Client.PutAsJsonAsync($"/api/teachers/{t.Id}/profile", new
        {
            bio = "Ten years of IELTS.", specialization = "IELTS Writing", yearsOfExperience = 10, education = "UzSWLU",
            languages = "English, Russian", teachingSince = "2016-09-01",
        });
        Assert.Equal(HttpStatusCode.OK, about.StatusCode);

        var ielts = await AddCredentialAsync(t.Client, t.Id, new { type = "ExamScore", title = "IELTS Academic", issuer = "British Council", score = "8.0 (L 8.5, R 8.0, W 7.5, S 8.0)" });
        Assert.False(ielts.Credential.IsVerified); // what a teacher adds about themselves starts unconfirmed
        var job = await t.Client.PostAsJsonAsync($"/api/teachers/{t.Id}/experience",
            new { employer = "Westminster", position = "Teacher", fromYear = 2018, toYear = 2022, description = "Groups of 12" });
        Assert.Equal(HttpStatusCode.Created, job.StatusCode);
        var jobId = (await job.Content.ReadFromJsonAsync<Experience>())!.Id;

        var portfolio = await PortfolioAsync(t.Client, t.Id);
        Assert.Equal(("IELTS Writing", 10), (portfolio.Profile.Specialization, portfolio.Profile.YearsOfExperience));
        Assert.Equal(ielts.Credential.Id, Assert.Single(portfolio.Credentials).Id);
        Assert.Equal("Westminster", Assert.Single(portfolio.Experience).Employer);

        var edited = await t.Client.PatchAsJsonAsync($"/api/teachers/{t.Id}/experience/{jobId}",
            new { employer = "Westminster", position = "Senior teacher", fromYear = 2018, toYear = (int?)null });
        Assert.Equal("Senior teacher", (await edited.Content.ReadFromJsonAsync<Experience>())!.Position);
        Assert.Equal(HttpStatusCode.NoContent, (await t.Client.DeleteAsync($"/api/teachers/{t.Id}/experience/{jobId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await t.Client.DeleteAsync($"/api/teachers/{t.Id}/credentials/{ielts.Credential.Id}")).StatusCode);
        var afterwards = await PortfolioAsync(t.Client, t.Id);
        Assert.Empty(afterwards.Credentials);
        Assert.Empty(afterwards.Experience);
    }

    [Fact]
    public async Task AColleague_CannotReadOrChangeAnothersPortfolio()
    {
        var t = await NewTeacherAsync();
        var colleague = await world.ClientAsync("teacher@demo-center.test");
        var support = await world.ClientAsync("support@demo-center.test");

        foreach (var other in new[] { colleague, support })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync($"/api/teachers/{t.Id}/portfolio")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await other.PutAsJsonAsync($"/api/teachers/{t.Id}/profile", new { bio = "x" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await other.PostAsJsonAsync($"/api/teachers/{t.Id}/credentials",
                new { type = "Other", title = "Something" })).StatusCode);
        }
    }

    [Fact]
    public async Task AnAdministrator_WorksOnTheirOwnBranchsTeacher_NotAnotherBranchs_AndTheOperatorNotAtAll()
    {
        var t = await NewTeacherAsync();
        var admin = await world.ClientAsync("admin@demo-center.test");
        var superAdmin = await world.ClientAsync("superadmin@demo-center.test");
        var operatorClient = await world.ClientAsync("operator@demo-center.test");
        var otherBranch = (await superAdmin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))!.First(b => b.Id != t.BranchId).Id;
        var abroad = await NewTeacherAsync(otherBranch);

        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/teachers/{t.Id}/profile", new { specialization = "General English" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/teachers/{t.Id}/portfolio")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync($"/api/teachers/{abroad.Id}/portfolio")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PutAsJsonAsync($"/api/teachers/{abroad.Id}/profile", new { bio = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await superAdmin.GetAsync($"/api/teachers/{abroad.Id}/portfolio")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.GetAsync($"/api/teachers/{t.Id}/portfolio")).StatusCode);
    }

    [Fact]
    public async Task ANonTeacher_IsNotFound_AndAnAnonymousCaller_IsRefused()
    {
        var superAdmin = await world.ClientAsync("superadmin@demo-center.test");
        var nonTeacherId = await world.UserIdAsync("operator@demo-center.test", "OPERATOR");

        Assert.Equal(HttpStatusCode.NotFound, (await superAdmin.GetAsync($"/api/teachers/{nonTeacherId}/portfolio")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync($"/api/teachers/{nonTeacherId}/portfolio")).StatusCode);
    }

    // ---- "my teacher" (student and parent) ---------------------------------------------------------

    [Fact]
    public async Task AStudentAndAParent_ReadOnlyTheConfirmedPartOfTheirTeachersPortfolio()
    {
        var superAdmin = await world.ClientAsync("superadmin@demo-center.test");
        var teacherId = await world.UserIdAsync("teacher@demo-center.test", "TEACHER");
        var confirmed = await superAdmin.PostAsJsonAsync($"/api/teachers/{teacherId}/credentials", new
        {
            type = "ExamScore", title = "Public IELTS", score = "8.5", reference = "TRF-SECRET-123", note = "private note", verified = true,
        });
        Assert.Equal(HttpStatusCode.Created, confirmed.StatusCode);
        var hidden = (await (await superAdmin.PostAsJsonAsync($"/api/teachers/{teacherId}/credentials",
            new { type = "Certificate", title = "Unconfirmed Hidden Cert" })).Content.ReadFromJsonAsync<Result>())!.Credential;
        var shownId = (await confirmed.Content.ReadFromJsonAsync<Result>())!.Credential.Id;
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(superAdmin, $"/api/teachers/{teacherId}/credentials/{shownId}/file", "scan.pdf", TestFiles.Pdf(7))).StatusCode);
        // A new scan withdraws the confirmation (it is part of what was checked): confirm again.
        Assert.Equal(HttpStatusCode.OK, (await superAdmin.PostAsync($"/api/teachers/{teacherId}/credentials/{shownId}/verify", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(superAdmin, $"/api/teachers/{teacherId}/credentials/{hidden.Id}/file", "secret.pdf", TestFiles.Pdf())).StatusCode);
        // The scan of a confirmed credential opens in the viewer for the student and the parent; an unconfirmed one does not.
        foreach (var email in new[] { "student@demo-center.test", "parent@demo-center.test" })
        {
            var reader = await world.ClientAsync(email);
            var file = await reader.GetAsync($"/api/teachers/{teacherId}/portfolio/public/credentials/{shownId}/file");
            Assert.Equal(HttpStatusCode.OK, file.StatusCode);
            Assert.Equal(TestFiles.Pdf(7), await file.Content.ReadAsByteArrayAsync());
            Assert.Contains("no-store", file.Headers.CacheControl!.ToString());
            Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync($"/api/teachers/{teacherId}/portfolio/public/credentials/{hidden.Id}/file")).StatusCode);
        }

        foreach (var email in new[] { "student@demo-center.test", "parent@demo-center.test" })
        {
            var response = await (await world.ClientAsync(email)).GetAsync($"/api/teachers/{teacherId}/portfolio/public");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var raw = await response.Content.ReadAsStringAsync();

            Assert.Contains("Public IELTS", raw);
            Assert.Contains("8.5", raw);
            Assert.DoesNotContain("Unconfirmed Hidden Cert", raw);
            foreach (var secret in new[] { "TRF-SECRET-123", "private note", "storageKey", "verifiedBy", "fileName", "scan.pdf" })
                Assert.DoesNotContain(secret, raw, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task ATeacherOfAnotherGroup_TheOffice_AndTheFullPortfolio_StayClosedToStudents()
    {
        var elsewhere = await NewTeacherAsync(); // teaches nobody the demo student knows
        var student = await world.ClientAsync("student@demo-center.test");

        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync($"/api/teachers/{elsewhere.Id}/portfolio/public")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync($"/api/teachers/{elsewhere.Id}/portfolio")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await student.GetAsync($"/api/teachers/{Guid.NewGuid()}/portfolio/public")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync($"/api/teachers/{elsewhere.Id}/portfolio/public/credentials/{Guid.NewGuid()}/file")).StatusCode);
        // The office reads the full portfolio; the student-facing view is not theirs.
        foreach (var email in new[] { "admin@demo-center.test", "teacher@demo-center.test", "operator@demo-center.test" })
            Assert.Equal(HttpStatusCode.Forbidden, (await (await world.ClientAsync(email)).GetAsync($"/api/teachers/{elsewhere.Id}/portfolio/public")).StatusCode);
    }

    // ---- confirming ----------------------------------------------------------------------------

    [Fact]
    public async Task OnlyTheOfficeConfirms_AndNeverForItself()
    {
        var t = await NewTeacherAsync();
        var admin = await world.ClientAsync("admin@demo-center.test");
        var credential = (await AddCredentialAsync(t.Client, t.Id, new { type = "Certificate", title = "CELTA", issuer = "Cambridge" })).Credential;

        Assert.Equal(HttpStatusCode.Forbidden, (await t.Client.PostAsync($"/api/teachers/{t.Id}/credentials/{credential.Id}/verify", null)).StatusCode);

        var verified = await admin.PostAsync($"/api/teachers/{t.Id}/credentials/{credential.Id}/verify", null);
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        var body = (await verified.Content.ReadFromJsonAsync<Credential>())!;
        Assert.True(body.IsVerified);
        Assert.False(string.IsNullOrEmpty(body.VerifiedByName));

        Assert.Equal(HttpStatusCode.Forbidden, (await t.Client.PostAsync($"/api/teachers/{t.Id}/credentials/{credential.Id}/unverify", null)).StatusCode);
        var withdrawn = (await (await admin.PostAsync($"/api/teachers/{t.Id}/credentials/{credential.Id}/unverify", null)).Content.ReadFromJsonAsync<Credential>())!;
        Assert.False(withdrawn.IsVerified);
    }

    [Fact]
    public async Task AnAdministrator_CannotConfirmTheirOwnCredential_ButCanEnterOneAlreadyConfirmed()
    {
        var t = await NewTeacherAsync();
        var admin = await world.ClientAsync("admin@demo-center.test");
        var adminId = await world.UserIdAsync("admin@demo-center.test", "ADMINISTRATOR");

        // Entered by the administrator, confirmed on the spot: allowed for someone who may confirm, refused for anyone else.
        var entered = await admin.PostAsJsonAsync($"/api/teachers/{t.Id}/credentials", new { type = "ExamScore", title = "IELTS", score = "7.5", verified = true });
        Assert.Equal(HttpStatusCode.Created, entered.StatusCode);
        Assert.True((await entered.Content.ReadFromJsonAsync<Result>())!.Credential.IsVerified);
        Assert.Equal(HttpStatusCode.Forbidden, (await t.Client.PostAsJsonAsync($"/api/teachers/{t.Id}/credentials",
            new { type = "ExamScore", title = "IELTS", score = "9.0", verified = true })).StatusCode);

        // An administrator who is (also) a teacher does not vouch for their own paper.
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsync($"/api/teachers/{adminId}/credentials/{Guid.NewGuid()}/verify", null)).StatusCode);
    }

    [Fact]
    public async Task EditingWhatWasConfirmed_TakesTheConfirmationAway_ButTheNoteDoesNot()
    {
        var t = await NewTeacherAsync();
        var admin = await world.ClientAsync("admin@demo-center.test");
        var credential = (await AddCredentialAsync(t.Client, t.Id, new { type = "ExamScore", title = "IELTS Academic", score = "8.0" })).Credential;
        await admin.PostAsync($"/api/teachers/{t.Id}/credentials/{credential.Id}/verify", null);

        var noteOnly = await PatchAsync(t.Client, t.Id, credential.Id, new { type = "ExamScore", title = "IELTS Academic", score = "8.0", note = "TRF on the desk" });
        Assert.False(noteOnly.VerificationReset);
        Assert.True(noteOnly.Credential.IsVerified);

        var scoreChanged = await PatchAsync(t.Client, t.Id, credential.Id, new { type = "ExamScore", title = "IELTS Academic", score = "9.0", note = "TRF on the desk" });
        Assert.True(scoreChanged.VerificationReset);
        Assert.False(scoreChanged.Credential.IsVerified);
        Assert.Null(scoreChanged.Credential.VerifiedByName);
        Assert.Equal("TRF on the desk", scoreChanged.Credential.Note);
    }

    // ---- the scan ------------------------------------------------------------------------------

    [Fact]
    public async Task TheScan_IsUploadedReplacedAndRemoved_AndTheChangeWithdrawsTheConfirmation()
    {
        var t = await NewTeacherAsync();
        var admin = await world.ClientAsync("admin@demo-center.test");
        var credential = (await AddCredentialAsync(t.Client, t.Id, new { type = "Certificate", title = "TESOL" })).Credential;
        var url = $"/api/teachers/{t.Id}/credentials/{credential.Id}/file";

        var first = await UploadAsync(t.Client, url, "scan.pdf", TestFiles.Pdf(16));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var stored = (await first.Content.ReadFromJsonAsync<Result>())!.Credential.File!;
        Assert.Equal(("scan.pdf", 16L), (stored.FileName, stored.Size));
        var download = await admin.GetAsync(url);
        Assert.Equal(TestFiles.Pdf(16), await download.Content.ReadAsByteArrayAsync());
        Assert.Contains("no-store", download.Headers.CacheControl!.ToString());

        await admin.PostAsync($"/api/teachers/{t.Id}/credentials/{credential.Id}/verify", null);
        var replaced = (await (await UploadAsync(t.Client, url, "better.png", TestFiles.Png(9))).Content.ReadFromJsonAsync<Result>())!;
        Assert.True(replaced.VerificationReset);
        Assert.Equal(TestFiles.Png(9), await (await t.Client.GetAsync(url)).Content.ReadAsByteArrayAsync());

        var removed = await t.Client.DeleteAsync(url);
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.Null((await removed.Content.ReadFromJsonAsync<Result>())!.Credential.File);
        Assert.Equal(HttpStatusCode.NotFound, (await t.Client.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task TheScan_IsValidated_AndServedOnlyToTheTeacherAndTheOffice()
    {
        var t = await NewTeacherAsync();
        var credential = (await AddCredentialAsync(t.Client, t.Id, new { type = "Degree", title = "BA" })).Credential;
        var url = $"/api/teachers/{t.Id}/credentials/{credential.Id}/file";

        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(t.Client, url, "big.pdf", new byte[10 * 1024 * 1024 + 1])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(t.Client, url, "virus.exe", [1])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(t.Client, url, "picture.svg", [1])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(t.Client, url, "empty.pdf", [])).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(t.Client, url, "ok.pdf", TestFiles.Pdf())).StatusCode);

        foreach (var outsider in new[] { "student@demo-center.test", "parent@demo-center.test", "teacher@demo-center.test", "operator@demo-center.test" })
            Assert.Equal(HttpStatusCode.Forbidden, (await (await world.ClientAsync(outsider)).GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await world.ClientAsync("teacher@demo-center.test")).PostAsync(url,
            new MultipartFormDataContent { { new ByteArrayContent([1]), "file", "x.pdf" } })).StatusCode);
    }

    // ---- the header ----------------------------------------------------------------------------

    [Fact]
    public async Task TheHeader_BoastsOnlyOfConfirmedExamScores_NotOfExpiredOnes_AndOnlyToThoseWhoMayRead()
    {
        var t = await NewTeacherAsync();
        var admin = await world.ClientAsync("admin@demo-center.test");
        await t.Client.PutAsJsonAsync($"/api/teachers/{t.Id}/profile", new { specialization = "IELTS" });

        var confirmed = await AddCredentialAsync(t.Client, t.Id, new { type = "ExamScore", title = "IELTS Academic", score = "8.0", issuedOn = "2025-05-01", expiresOn = "2099-01-01" });
        var lapsed = await AddCredentialAsync(t.Client, t.Id, new { type = "ExamScore", title = "TOEFL", score = "110", issuedOn = "2015-05-01", expiresOn = "2017-05-01" });
        var unconfirmed = await AddCredentialAsync(t.Client, t.Id, new { type = "ExamScore", title = "CEFR", score = "C2" });
        var certificate = await AddCredentialAsync(t.Client, t.Id, new { type = "Certificate", title = "CELTA" });
        foreach (var c in new[] { confirmed, lapsed, certificate })
            await admin.PostAsync($"/api/teachers/{t.Id}/credentials/{c.Credential.Id}/verify", null);

        var portfolio = await PortfolioAsync(admin, t.Id);
        Assert.Equal(new[] { ("IELTS Academic", "8.0") }, portfolio.Headline.Select(h => (h.Title, h.Score)).ToArray());
        Assert.True(portfolio.Credentials.Single(c => c.Id == lapsed.Credential.Id).IsExpired);
        Assert.False(portfolio.Credentials.Single(c => c.Id == unconfirmed.Credential.Id).IsVerified);

        var row = (await admin.GetFromJsonAsync<List<ListRow>>("/api/teachers"))!.Single(r => r.Id == t.Id);
        Assert.Equal("IELTS", row.Specialization);
        Assert.Equal("IELTS Academic", Assert.Single(row.Headline!).Title);

        // A colleague sees the row (they co-teach or list peers) without the portfolio; an operator sees the teacher, not the portfolio.
        var operatorRow = (await (await world.ClientAsync("operator@demo-center.test")).GetFromJsonAsync<List<ListRow>>("/api/teachers"))!.Single(r => r.Id == t.Id);
        Assert.Null(operatorRow.Specialization);
        Assert.Null(operatorRow.Headline);
        var summary = await admin.GetFromJsonAsync<SummaryRow>($"/api/teachers/{t.Id}/summary");
        Assert.Equal("IELTS", summary!.Specialization);
    }

    // ---- notifications and the audit trail -----------------------------------------------------

    [Fact]
    public async Task TheAdministratorIsToldWhatWaits_AndTheTeacherWhenItIsConfirmed()
    {
        var t = await NewTeacherAsync();
        var admin = await world.ClientAsync("admin@demo-center.test");
        var credential = (await AddCredentialAsync(t.Client, t.Id, new { type = "ExamScore", title = "IELTS Academic", score = "8.0" })).Credential;

        Assert.Contains(await NotificationsOfAsync(admin), n => n.Type == "CREDENTIAL_SUBMITTED" && n.Link == $"/teachers/{t.Id}");
        Assert.DoesNotContain(await NotificationsOfAsync(t.Client), n => n.Type == "CREDENTIAL_SUBMITTED");

        await admin.PostAsync($"/api/teachers/{t.Id}/credentials/{credential.Id}/verify", null);
        Assert.Contains(await NotificationsOfAsync(t.Client), n => n.Type == "CREDENTIAL_VERIFIED" && n.Body.Contains("IELTS Academic"));

        // What an administrator enters themselves needs no telling.
        var before = (await NotificationsOfAsync(admin)).Count(n => n.Type == "CREDENTIAL_SUBMITTED");
        await admin.PostAsJsonAsync($"/api/teachers/{t.Id}/credentials", new { type = "Course", title = "Cambridge course" });
        Assert.Equal(before, (await NotificationsOfAsync(admin)).Count(n => n.Type == "CREDENTIAL_SUBMITTED"));
    }

    [Fact]
    public async Task TheConfirmation_IsInTheAuditLog()
    {
        var t = await NewTeacherAsync();
        var admin = await world.ClientAsync("admin@demo-center.test");
        var credential = (await AddCredentialAsync(t.Client, t.Id, new { type = "Certificate", title = "CELTA" })).Credential;
        await admin.PostAsync($"/api/teachers/{t.Id}/credentials/{credential.Id}/verify", null);

        var page = (await admin.GetFromJsonAsync<AuditPage>($"/api/audit-logs?resource=TeacherCredential&resourceId={credential.Id}"))!;

        Assert.Contains(page.Items, a => a.Action == "Added");
        Assert.Contains(page.Items, a => a.Action == "Modified" && a.NewValueJson!.Contains("\"IsVerified\":true"));
    }

    // ---- limits and validation -----------------------------------------------------------------

    [Fact]
    public async Task ATeacher_CannotHoldMoreThanFiftyCredentialsOrThirtyJobs()
    {
        var t = await NewTeacherAsync();
        for (var i = 0; i < 50; i++)
            Assert.Equal(HttpStatusCode.Created, (await t.Client.PostAsJsonAsync($"/api/teachers/{t.Id}/credentials", new { type = "Other", title = $"Award {i}" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await t.Client.PostAsJsonAsync($"/api/teachers/{t.Id}/credentials", new { type = "Other", title = "One too many" })).StatusCode);

        for (var i = 0; i < 30; i++)
            Assert.Equal(HttpStatusCode.Created, (await t.Client.PostAsJsonAsync($"/api/teachers/{t.Id}/experience", new { employer = $"School {i}", position = "Teacher", fromYear = 2010 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await t.Client.PostAsJsonAsync($"/api/teachers/{t.Id}/experience", new { employer = "One more", position = "Teacher", fromYear = 2010 })).StatusCode);
    }

    [Fact]
    public async Task BadInput_IsRefusedWithAReadableReason()
    {
        var t = await NewTeacherAsync();
        string Credentials() => $"/api/teachers/{t.Id}/credentials";
        var bad = new (string Url, object Body)[]
        {
            (Credentials(), new { type = "Other", title = "  " }),
            (Credentials(), new { title = "No type" }),
            (Credentials(), new { type = "ExamScore", title = "IELTS" }), // an exam needs its score
            (Credentials(), new { type = "Other", title = new string('x', 201) }),
            (Credentials(), new { type = "Course", title = "Backwards", issuedOn = "2024-05-01", expiresOn = "2023-05-01" }),
            ($"/api/teachers/{t.Id}/experience", new { employer = "School", position = "Teacher", fromYear = 2020, toYear = 2019 }),
            ($"/api/teachers/{t.Id}/experience", new { employer = "School", position = "", fromYear = 2020 }),
            ($"/api/teachers/{t.Id}/experience", new { employer = "School", position = "Teacher", fromYear = 1900 }),
        };
        foreach (var (url, body) in bad)
        {
            var response = await t.Client.PostAsJsonAsync(url, body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.False(string.IsNullOrWhiteSpace((await response.Content.ReadFromJsonAsync<ErrorBody>())!.Error));
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await t.Client.PutAsJsonAsync($"/api/teachers/{t.Id}/profile", new { yearsOfExperience = 61 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await t.Client.PutAsJsonAsync($"/api/teachers/{t.Id}/profile", new { bio = new string('x', 2001) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await t.Client.PutAsJsonAsync($"/api/teachers/{t.Id}/profile", new { teachingSince = "2999-01-01" })).StatusCode);
    }

    // ---- the account -----------------------------------------------------------------------------

    [Fact]
    public async Task DeletingTheAccount_TakesThePortfolioAndItsScansWithIt()
    {
        var t = await NewTeacherAsync();
        var superAdmin = await world.ClientAsync("superadmin@demo-center.test");
        var credential = (await AddCredentialAsync(t.Client, t.Id, new { type = "Certificate", title = "CELTA" })).Credential;
        await t.Client.PutAsJsonAsync($"/api/teachers/{t.Id}/profile", new { bio = "Bio" });
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(t.Client, $"/api/teachers/{t.Id}/credentials/{credential.Id}/file", "scan.pdf", TestFiles.Pdf())).StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var key = await db.TeacherCredentials.Where(c => c.Id == credential.Id).Select(c => c.StorageKey).SingleAsync();
        var storage = scope.ServiceProvider.GetRequiredService<TeacherCredentialFileStorage>();
        Assert.NotNull(storage.PathFor(key!));

        Assert.Equal(HttpStatusCode.NoContent, (await superAdmin.DeleteAsync($"/api/users/{t.Id}")).StatusCode);

        Assert.False(await db.TeacherCredentials.AnyAsync(c => c.UserId == t.Id));
        Assert.False(await db.TeacherProfiles.AnyAsync(p => p.UserId == t.Id));
        Assert.Null(storage.PathFor(key!));
    }

    // ---- helpers -------------------------------------------------------------------------------

    private sealed record NewTeacher(Guid Id, string Email, Guid BranchId, HttpClient Client);

    /// <summary>A fresh teacher in the administrator's branch (or another). Named to sort after "Teacher Demo": other tests look the demo
    /// teacher up on the first page of the name-ordered user list.</summary>
    private async Task<NewTeacher> NewTeacherAsync(Guid? branchId = null)
    {
        var admin = await world.ClientAsync("admin@demo-center.test");
        var superAdmin = await world.ClientAsync("superadmin@demo-center.test");
        var branch = branchId ?? (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var email = $"portfolio-{Guid.NewGuid():N}@demo-center.test";
        var response = await superAdmin.PostAsJsonAsync("/api/users", new
        {
            fullName = $"Teacher Portfolio {Guid.NewGuid():N}"[..34], email, phone = "+998900000057",
            password = DbSeeder.DemoPassword, roleCode = "TEACHER", branchIds = new[] { branch },
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
        return new NewTeacher(id, email, branch, await world.ClientAsync(email));
    }

    private static async Task<Result> AddCredentialAsync(HttpClient client, Guid teacherId, object body)
    {
        var response = await client.PostAsJsonAsync($"/api/teachers/{teacherId}/credentials", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Result>())!;
    }

    private static async Task<Result> PatchAsync(HttpClient client, Guid teacherId, Guid credentialId, object body)
    {
        var response = await client.PatchAsJsonAsync($"/api/teachers/{teacherId}/credentials/{credentialId}", body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Result>())!;
    }

    private static async Task<Portfolio> PortfolioAsync(HttpClient client, Guid teacherId)
    {
        var response = await client.GetAsync($"/api/teachers/{teacherId}/portfolio");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Portfolio>())!;
    }

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, string url, string fileName, byte[] bytes) =>
        client.PostAsync(url, new MultipartFormDataContent { { new ByteArrayContent(bytes), "file", fileName } });

    private static async Task<List<NotificationRow>> NotificationsOfAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<NotificationRow>>("/api/notifications?limit=100"))!;

    private record IdDto(Guid Id);
    private record BranchDto(Guid Id, string Name);
    private record ErrorBody(string Error);
    private record FileInfo_(string FileName, string ContentType, long Size);
    private record Credential(Guid Id, string Type, string Title, string? Note, FileInfo_? File, bool IsVerified, string? VerifiedByName, bool IsExpired);
    private record Result(Credential Credential, bool VerificationReset);
    private record Experience(Guid Id, string Employer, string Position);
    private record Profile(string? Specialization, int? YearsOfExperience);
    private record Chip(string Title, string? Score);
    private record Portfolio(Profile Profile, List<Credential> Credentials, List<Experience> Experience, List<Chip> Headline);
    private record ListRow(Guid Id, string? Specialization, List<Chip>? Headline);
    private record SummaryRow(string? Specialization);
    private record NotificationRow(string Type, string Body, string? Link);
    private record AuditRow(string Action, string? NewValueJson);
    private record AuditPage(List<AuditRow> Items);
}
