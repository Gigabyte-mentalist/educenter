using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduCenter.Infrastructure.Seed;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// Registering and correcting a student directly. A student used to exist only as the end of a lead's conversion, so a
/// centre could not enter the people it already teaches. A branch-scoped caller must name a group — branch scope reaches
/// a student only through an enrollment, so an ungrouped student would be invisible to whoever registered them — and
/// the operator may correct only a student's contact details (matrix: <c>students.edit</c> "limited").
/// </summary>
[Collection("Api")]
public class StudentAdminTests(EduCenterWebApplicationFactory factory)
{
    [Fact]
    public async Task Create_WithGroup_MakesLoginStudentEnrollmentTimelineAndFirstInvoice()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var branchId = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var course = await CreateCourseAsync(admin, monthlyFee: 450000);
        var group = await CreateGroupAsync(admin, branchId, course, capacity: 5);
        var email = NewEmail();

        var response = await admin.PostAsJsonAsync("/api/students", new
        {
            fullName = "  Dilnoza Karimova ", phone = "+998901112233", email = email.ToUpperInvariant(), password = DbSeeder.DemoPassword,
            joinedDate = "2026-03-01", currentLevel = "B1", groupId = group,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var student = (await response.Content.ReadFromJsonAsync<StudentDto>())!;

        Assert.Equal("Dilnoza Karimova", student.FullName);
        Assert.Equal(email, student.Email); // stored lower-cased, so the login is case-insensitive
        Assert.Equal("+998901112233", student.Phone);
        Assert.StartsWith("STU-", student.StudentNumber);
        Assert.Equal("2026-03-01", student.JoinedDate); // a centre moving over keeps the real start date
        Assert.Equal("B1", student.CurrentLevel);
        Assert.Equal("Active", student.Status);

        // They can sign in with what was entered.
        var login = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password = DbSeeder.DemoPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var overview = (await admin.GetFromJsonAsync<OverviewDto>($"/api/students/{student.Id}/overview"))!;
        var enrollment = Assert.Single(overview.Enrollments);
        Assert.Equal(group, enrollment.GroupId);
        Assert.Equal("Active", enrollment.Status);

        var timeline = (await admin.GetFromJsonAsync<List<TimelineDto>>($"/api/students/{student.Id}/timeline"))!;
        Assert.Contains(timeline, e => e.EventType == "ENROLLED");

        var invoice = Assert.Single((await admin.GetFromJsonAsync<PagedResponse<InvoiceDto>>($"/api/finance/invoices?studentId={student.Id}"))!.Items);
        Assert.Equal(450000m, invoice.Amount);
    }

    [Fact]
    public async Task Create_ByBranchStaff_WithoutGroup_IsRejected_BecauseTheStudentWouldBeInvisibleToThem()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");

        foreach (var client in new[] { admin, operatorClient })
        {
            var response = await client.PostAsJsonAsync("/api/students", ValidBody(NewEmail()));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public async Task Create_BySuperAdmin_WithoutGroup_RegistersAnUnplacedStudent()
    {
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");

        var response = await superAdmin.PostAsJsonAsync("/api/students", ValidBody(NewEmail()));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var student = (await response.Content.ReadFromJsonAsync<StudentDto>())!;

        var overview = (await superAdmin.GetFromJsonAsync<OverviewDto>($"/api/students/{student.Id}/overview"))!;
        Assert.Empty(overview.Enrollments);

        // Not yet in any branch, so a branch administrator cannot see them until they are placed.
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync($"/api/students/{student.Id}")).StatusCode);
    }

    [Fact]
    public async Task Create_WithBadInput_IsRejected()
    {
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");

        async Task<HttpStatusCode> Post(object body) => (await superAdmin.PostAsJsonAsync("/api/students", body)).StatusCode;

        Assert.Equal(HttpStatusCode.BadRequest, await Post(new { fullName = "", phone = "+998900000000", email = NewEmail(), password = DbSeeder.DemoPassword }));
        Assert.Equal(HttpStatusCode.BadRequest, await Post(new { fullName = "Ali", phone = "", email = NewEmail(), password = DbSeeder.DemoPassword }));
        Assert.Equal(HttpStatusCode.BadRequest, await Post(new { fullName = "Ali", phone = "+998900000000", email = "not-an-email", password = DbSeeder.DemoPassword }));
        Assert.Equal(HttpStatusCode.BadRequest, await Post(new { fullName = "Ali", phone = "+998900000000", email = NewEmail(), password = "short" }));
        Assert.Equal(HttpStatusCode.BadRequest, await Post(new { fullName = "Ali", phone = "+998900000000", email = NewEmail(), password = DbSeeder.DemoPassword, joinedDate = "2099-01-01" }));
        Assert.Equal(HttpStatusCode.BadRequest, await Post(new { fullName = new string('x', 201), phone = "+998900000000", email = NewEmail(), password = DbSeeder.DemoPassword }));
    }

    [Fact]
    public async Task Create_WithTakenEmailOrNumber_ReturnsConflict()
    {
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var email = NewEmail();
        var number = $"OLD-{Guid.NewGuid():N}"[..14];

        var first = await superAdmin.PostAsJsonAsync("/api/students", ValidBody(email, studentNumber: number));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(number, (await first.Content.ReadFromJsonAsync<StudentDto>())!.StudentNumber); // the centre's own numbering is kept

        Assert.Equal(HttpStatusCode.Conflict, (await superAdmin.PostAsJsonAsync("/api/students", ValidBody(email))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await superAdmin.PostAsJsonAsync("/api/students", ValidBody(NewEmail(), studentNumber: number))).StatusCode);
    }

    [Fact]
    public async Task Create_IntoFullGroup_ReturnsConflict()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var branchId = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var group = await CreateGroupAsync(admin, branchId, await CreateCourseAsync(admin), capacity: 1);

        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/students", ValidBody(NewEmail(), group))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/students", ValidBody(NewEmail(), group))).StatusCode);
    }

    [Fact]
    public async Task Create_IntoAnotherBranchsGroup_ByBranchAdministrator_IsForbidden()
    {
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var otherGroup = await GroupInOtherBranchAsync(superAdmin, admin);

        var response = await admin.PostAsJsonAsync("/api/students", ValidBody(NewEmail(), otherGroup));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_ByTeacherOrStudent_IsForbidden()
    {
        foreach (var user in new[] { "teacher@demo-center.test", "student@demo-center.test", "parent@demo-center.test" })
        {
            var client = await AuthenticatedClientAsync(user);
            var response = await client.PostAsJsonAsync("/api/students", ValidBody(NewEmail()));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task Update_ByAdministrator_ChangesTheWholeProfile()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var (studentId, _) = await RegisterInOwnBranchAsync(admin);
        var newNumber = $"NEW-{Guid.NewGuid():N}"[..14];

        var response = await admin.PatchAsJsonAsync($"/api/students/{studentId}", new
        {
            fullName = "Yangi Ism", phone = "+998907654321", studentNumber = newNumber, joinedDate = "2026-01-15", currentLevel = "IELTS Advanced",
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var student = (await admin.GetFromJsonAsync<StudentDto>($"/api/students/{studentId}"))!;
        Assert.Equal("Yangi Ism", student.FullName);
        Assert.Equal("+998907654321", student.Phone);
        Assert.Equal(newNumber, student.StudentNumber);
        Assert.Equal("2026-01-15", student.JoinedDate);
        Assert.Equal("IELTS Advanced", student.CurrentLevel);

        // An empty level clears it; a missing field leaves it alone.
        var cleared = await (await admin.PatchAsJsonAsync($"/api/students/{studentId}", new { currentLevel = "" })).Content.ReadFromJsonAsync<StudentDto>();
        Assert.Null(cleared!.CurrentLevel);
        Assert.Equal("Yangi Ism", cleared.FullName);
    }

    [Fact]
    public async Task Update_WithBadInputOrTakenNumber_IsRejected()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var (first, _) = await RegisterInOwnBranchAsync(admin);
        var (second, _) = await RegisterInOwnBranchAsync(admin);
        var takenNumber = (await admin.GetFromJsonAsync<StudentDto>($"/api/students/{first}"))!.StudentNumber;

        Assert.Equal(HttpStatusCode.Conflict, (await admin.PatchAsJsonAsync($"/api/students/{second}", new { studentNumber = takenNumber })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchAsJsonAsync($"/api/students/{second}", new { fullName = "  " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchAsJsonAsync($"/api/students/{second}", new { joinedDate = "2099-01-01" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchAsJsonAsync($"/api/students/{second}", new { studentNumber = "" })).StatusCode);

        // Keeping their own number is not a clash with themselves.
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/students/{first}", new { studentNumber = takenNumber })).StatusCode);
    }

    [Fact]
    public async Task Operator_MayCorrectContactDetails_ButNothingElse()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        var (studentId, _) = await RegisterInOwnBranchAsync(admin);

        var contact = await operatorClient.PatchAsJsonAsync($"/api/students/{studentId}/contact",
            new { fullName = "Tuzatilgan Ism", phone = "+998901234500", currentLevel = "C1", studentNumber = "HACK-1" });
        Assert.Equal(HttpStatusCode.OK, contact.StatusCode);

        // The contact route changes the name and phone only — level and number in the same body are ignored.
        var student = (await admin.GetFromJsonAsync<StudentDto>($"/api/students/{studentId}"))!;
        Assert.Equal("Tuzatilgan Ism", student.FullName);
        Assert.Equal("+998901234500", student.Phone);
        Assert.Null(student.CurrentLevel);
        Assert.NotEqual("HACK-1", student.StudentNumber);

        // The full profile is not theirs to rewrite.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await operatorClient.PatchAsJsonAsync($"/api/students/{studentId}", new { currentLevel = "C1" })).StatusCode);
    }

    [Fact]
    public async Task Update_ByTeacher_IsForbidden_OnBothRoutes()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var teacher = await AuthenticatedClientAsync("teacher@demo-center.test");
        var (studentId, _) = await RegisterInOwnBranchAsync(admin);

        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PatchAsJsonAsync($"/api/students/{studentId}", new { fullName = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PatchAsJsonAsync($"/api/students/{studentId}/contact", new { fullName = "X" })).StatusCode);
    }

    [Fact]
    public async Task Update_StudentInAnotherBranch_ByBranchAdministrator_IsForbidden()
    {
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var otherGroup = await GroupInOtherBranchAsync(superAdmin, admin);

        var created = await superAdmin.PostAsJsonAsync("/api/students", ValidBody(NewEmail(), otherGroup));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var studentId = (await created.Content.ReadFromJsonAsync<StudentDto>())!.Id;

        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PatchAsJsonAsync($"/api/students/{studentId}", new { fullName = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PatchAsJsonAsync($"/api/students/{studentId}/contact", new { fullName = "X" })).StatusCode);
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static string NewEmail() => $"registered-{Guid.NewGuid():N}@demo-center.test";

    private static object ValidBody(string email, Guid? groupId = null, string? studentNumber = null) => new
    {
        fullName = "Registered Student", phone = "+998900000077", email, password = DbSeeder.DemoPassword, groupId, studentNumber,
    };

    /// <summary>A student placed in a fresh group of the branch the administrator holds.</summary>
    private static async Task<(Guid StudentId, Guid GroupId)> RegisterInOwnBranchAsync(HttpClient admin)
    {
        var branchId = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var group = await CreateGroupAsync(admin, branchId, await CreateCourseAsync(admin), capacity: 5);
        var response = await admin.PostAsJsonAsync("/api/students", ValidBody(NewEmail(), group));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return ((await response.Content.ReadFromJsonAsync<StudentDto>())!.Id, group);
    }

    /// <summary>A group in a branch the branch administrator does not hold, made by the super admin who holds them all.</summary>
    private static async Task<Guid> GroupInOtherBranchAsync(HttpClient superAdmin, HttpClient admin)
    {
        var mine = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))!.Select(b => b.Id).ToHashSet();
        var other = (await superAdmin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))!.First(b => !mine.Contains(b.Id)).Id;
        return await CreateGroupAsync(superAdmin, other, await CreateCourseAsync(superAdmin), capacity: 5);
    }

    private static async Task<Guid> CreateCourseAsync(HttpClient client, decimal monthlyFee = 0)
    {
        var response = await client.PostAsJsonAsync("/api/courses",
            new { name = $"Registration Course {Guid.NewGuid():N}"[..26], levelCategory = "IELTS", monthlyFee });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CourseDto>())!.Id;
    }

    private static async Task<Guid> CreateGroupAsync(HttpClient client, Guid branchId, Guid courseId, int capacity)
    {
        var response = await client.PostAsJsonAsync("/api/groups",
            new { branchId, courseId, name = $"SA-{Guid.NewGuid():N}", capacity });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<GroupDto>())!.Id;
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
    private record CourseDto(Guid Id, string Name);
    private record GroupDto(Guid Id);
    private record StudentDto(Guid Id, Guid UserId, string FullName, string Email, string Phone, string StudentNumber, string JoinedDate, string? CurrentLevel, string Status);
    private record EnrollmentDto(Guid GroupId, string GroupName, string Status);
    private record OverviewDto(StudentDto Student, List<EnrollmentDto> Enrollments);
    private record TimelineDto(string EventType);
    private record InvoiceDto(Guid Id, decimal Amount, string Status);
}
