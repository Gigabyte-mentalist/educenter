using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduCenter.Api.Automation;
using EduCenter.Infrastructure.Seed;
using Microsoft.Extensions.DependencyInjection;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// Support Needed Engine (docs/00_TZ.md §8) and the CRM follow-up rules (§10), end to end:
/// attendance → signal → recommendation + operator task → review → assignment → resolution, and
/// attended trial → 24h without enrollment → follow-up task + raised lead priority. The timer-driven
/// sweep is disabled in tests (Automation__Enabled=false); tests invoke <see cref="FollowUpRules"/> directly.
/// </summary>
[Collection("Api")]
public class SupportAndFollowUpTests(EduCenterWebApplicationFactory factory)
{
    [Fact]
    public async Task TwoConsecutiveAbsences_RaiseRecommendationAndTask_ThenAdminAssigns_AndSupportTeacherResolves()
    {
        var admin = await AuthenticatedClientAsync("admin@demo-center.test");
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");
        var teacher = await AuthenticatedClientAsync("teacher@demo-center.test");
        var support = await AuthenticatedClientAsync("support@demo-center.test");

        var branchId = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var teacherId = await UserIdAsync(superAdmin, "TEACHER", "teacher@demo-center.test");
        var supportId = await UserIdAsync(superAdmin, "SUPPORT_TEACHER", "support@demo-center.test");

        var group = await CreateGroupAsync(superAdmin, branchId, "SUP");
        await superAdmin.PostAsJsonAsync($"/api/groups/{group.Id}/staff", new { userId = teacherId, role = "MainTeacher" });
        await superAdmin.PostAsJsonAsync($"/api/groups/{group.Id}/staff", new { userId = supportId, role = "SupportTeacher" });

        var (studentId, leadName) = await ConvertNewStudentAsync(operatorClient, branchId, group.Id);

        var firstStart = FarFutureSlot();
        var lesson1 = await CreateLessonAsync(superAdmin, branchId, group.Id, teacherId, firstStart);
        var lesson2 = await CreateLessonAsync(superAdmin, branchId, group.Id, teacherId, firstStart.AddDays(2));

        await MarkAsync(teacher, lesson1, studentId, "Absent");
        Assert.Empty((await teacher.GetFromJsonAsync<PagedResponse<SupportCaseDto>>($"/api/support-cases?studentId={studentId}"))!.Items);

        await MarkAsync(teacher, lesson2, studentId, "Absent");
        var cases = (await teacher.GetFromJsonAsync<PagedResponse<SupportCaseDto>>($"/api/support-cases?studentId={studentId}"))!.Items;
        var supportCase = Assert.Single(cases);
        Assert.Equal("Recommended", supportCase.Status);
        Assert.Equal("System", supportCase.Source);
        Assert.Equal("CONSECUTIVE_ABSENCES", supportCase.SignalCode);

        // The operator who converted the lead gets the call; the branch administrator sees it too
        // (regression: MyBranches used to match lead tasks only).
        var operatorTask = Assert.Single((await operatorClient.GetFromJsonAsync<PagedResponse<CrmTaskDto>>("/api/crm/tasks?size=200"))!.Items,
            t => t.StudentId == studentId);
        Assert.Equal("Absence", operatorTask.Type);
        Assert.Equal("CONSECUTIVE_ABSENCES", operatorTask.ReasonCode);
        Assert.Equal(leadName, operatorTask.SubjectName);
        Assert.Contains((await admin.GetFromJsonAsync<PagedResponse<CrmTaskDto>>("/api/crm/tasks?size=200"))!.Items, t => t.Id == operatorTask.Id);

        // Re-saving the same attendance and running the sweep must not duplicate anything.
        await MarkAsync(teacher, lesson2, studentId, "Absent");
        await RunSweepAsync();
        Assert.Single((await teacher.GetFromJsonAsync<PagedResponse<SupportCaseDto>>($"/api/support-cases?studentId={studentId}"))!.Items);
        Assert.Single((await operatorClient.GetFromJsonAsync<PagedResponse<CrmTaskDto>>("/api/crm/tasks?size=200"))!.Items, t => t.StudentId == studentId);

        // Review: the teacher may propose but not assign; a second proposal for an open case conflicts.
        var teacherAssign = await teacher.PostAsJsonAsync($"/api/support-cases/{supportCase.Id}/assign", new { supportTeacherId = supportId });
        Assert.Equal(HttpStatusCode.Forbidden, teacherAssign.StatusCode);
        var duplicate = await teacher.PostAsJsonAsync("/api/support-cases", new { studentId, groupId = group.Id, reason = "Writing zaif" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var candidates = (await admin.GetFromJsonAsync<List<SupportCandidateDto>>($"/api/support-cases/{supportCase.Id}/candidates"))!;
        Assert.Contains(candidates, c => c.UserId == supportId);

        var assign = await admin.PostAsJsonAsync($"/api/support-cases/{supportCase.Id}/assign", new { supportTeacherId = supportId });
        Assert.Equal(HttpStatusCode.NoContent, assign.StatusCode);

        var timeline = (await admin.GetFromJsonAsync<List<TimelineEventDto>>($"/api/students/{studentId}/timeline"))!;
        Assert.Contains(timeline, e => e.EventType == "SUPPORT_RECOMMENDED");
        Assert.Contains(timeline, e => e.EventType == "SUPPORT_ASSIGNED");

        var assigned = Assert.Single((await support.GetFromJsonAsync<PagedResponse<SupportCaseDto>>($"/api/support-cases?studentId={studentId}"))!.Items);
        Assert.Equal("Assigned", assigned.Status);
        Assert.Equal(supportId, assigned.SupportTeacherId);

        var resolve = await support.PostAsJsonAsync($"/api/support-cases/{supportCase.Id}/resolve", new { note = "Ikki mashg'ulotdan so'ng davomat tiklandi." });
        Assert.Equal(HttpStatusCode.NoContent, resolve.StatusCode);

        // A closed case covers its trigger: the sweep does not reopen it for the same two absences.
        await RunSweepAsync();
        var final = Assert.Single((await admin.GetFromJsonAsync<PagedResponse<SupportCaseDto>>($"/api/support-cases?studentId={studentId}"))!.Items);
        Assert.Equal("Resolved", final.Status);
    }

    [Fact]
    public async Task AttendedTrialWithoutEnrollment_After24Hours_CreatesOneFollowUpTask_AndRaisesPriority()
    {
        var superAdmin = await AuthenticatedClientAsync("superadmin@demo-center.test");
        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");

        var branchId = (await operatorClient.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var group = await CreateGroupAsync(superAdmin, branchId, "TRL");

        var pendingLead = await CreateLeadAsync(operatorClient, branchId, "Trial Pending");
        var convertedLead = await CreateLeadAsync(operatorClient, branchId, "Trial Converted");

        foreach (var lead in new[] { pendingLead, convertedLead })
        {
            var trial = await (await operatorClient.PostAsJsonAsync($"/api/leads/{lead.Id}/trial",
                new { groupId = group.Id, scheduledAt = DateTimeOffset.UtcNow.AddDays(-2) })).Content.ReadFromJsonAsync<TrialDto>();
            var attended = await operatorClient.PatchAsJsonAsync($"/api/trial-lessons/{trial!.Id}/attendance", new { attended = true });
            Assert.Equal(HttpStatusCode.NoContent, attended.StatusCode);
        }

        var convert = await operatorClient.PostAsJsonAsync($"/api/leads/{convertedLead.Id}/convert", new
        {
            groupId = group.Id,
            email = $"trial-converted-{Guid.NewGuid():N}@demo-center.test",
            password = DbSeeder.DemoPassword
        });
        Assert.Equal(HttpStatusCode.Created, convert.StatusCode);

        // TZ §9: the lead-stage history (origin + trial) is part of the student's timeline after conversion.
        var convertedStudent = await convert.Content.ReadFromJsonAsync<StudentDto>();
        var timeline = (await superAdmin.GetFromJsonAsync<List<TimelineEventDto>>($"/api/students/{convertedStudent!.Id}/timeline"))!;
        Assert.Contains(timeline, e => e.EventType == "LEAD_CREATED");
        Assert.Contains(timeline, e => e.EventType == "TRIAL_LESSON");
        Assert.Contains(timeline, e => e.EventType == "ENROLLED");

        await RunSweepAsync();
        await RunSweepAsync(); // idempotent

        var tasks = (await operatorClient.GetFromJsonAsync<PagedResponse<CrmTaskDto>>("/api/crm/tasks?size=200"))!.Items;
        var followUp = Assert.Single(tasks, t => t.LeadId == pendingLead.Id);
        Assert.Equal("FollowUp", followUp.Type);
        Assert.Equal("TRIAL_FOLLOW_UP", followUp.ReasonCode);
        Assert.DoesNotContain(tasks, t => t.LeadId == convertedLead.Id && t.ReasonCode == "TRIAL_FOLLOW_UP");

        var leads = (await operatorClient.GetFromJsonAsync<PagedResponse<LeadDto>>("/api/leads?size=200"))!.Items;
        Assert.Equal("High", leads.Single(l => l.Id == pendingLead.Id).Priority);
    }

    [Fact]
    public async Task SupportCases_AreNotVisibleToStudentsOrOperators()
    {
        var student = await AuthenticatedClientAsync("student@demo-center.test");
        var operatorClient = await AuthenticatedClientAsync("operator@demo-center.test");

        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync("/api/support-cases")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.GetAsync("/api/support-cases")).StatusCode);
    }

    // ---- helpers -------------------------------------------------------------------------------

    private async Task RunSweepAsync()
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<FollowUpRules>().RunSweepAsync(DateTimeOffset.UtcNow);
    }

    private static DateTimeOffset FarFutureSlot() =>
        DateTimeOffset.UtcNow.Date.AddDays(Random.Shared.Next(400, 4000)).AddMinutes(Random.Shared.Next(0, 1200));

    private static async Task<Guid> UserIdAsync(HttpClient client, string role, string email) =>
        (await client.GetFromJsonAsync<PagedResponse<UserDto>>($"/api/users?role={role}"))!.Items.First(u => u.Email == email).Id;

    private static async Task<GroupDto> CreateGroupAsync(HttpClient superAdmin, Guid branchId, string prefix)
    {
        var course = await (await superAdmin.PostAsJsonAsync("/api/courses",
            new { name = $"{prefix} Course", levelCategory = "IELTS" })).Content.ReadFromJsonAsync<CourseDto>();
        return (await (await superAdmin.PostAsJsonAsync("/api/groups",
            new { branchId, courseId = course!.Id, name = $"{prefix}-{Guid.NewGuid():N}", capacity = 10 }))
            .Content.ReadFromJsonAsync<GroupDto>())!;
    }

    private static async Task<LeadDto> CreateLeadAsync(HttpClient operatorClient, Guid branchId, string name) =>
        (await (await operatorClient.PostAsJsonAsync("/api/leads", new
        {
            branchId, fullName = $"{name} {Guid.NewGuid():N}"[..30], phone = "+998900000055", source = "Website", interestedCourse = "IELTS"
        })).Content.ReadFromJsonAsync<LeadDto>())!;

    private static async Task<(Guid StudentId, string LeadName)> ConvertNewStudentAsync(HttpClient operatorClient, Guid branchId, Guid groupId)
    {
        var lead = await CreateLeadAsync(operatorClient, branchId, "Support Student");
        var response = await operatorClient.PostAsJsonAsync($"/api/leads/{lead.Id}/convert", new
        {
            groupId, email = $"support-student-{Guid.NewGuid():N}@demo-center.test", password = DbSeeder.DemoPassword
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var student = await response.Content.ReadFromJsonAsync<StudentDto>();
        return (student!.Id, lead.FullName);
    }

    private static async Task<Guid> CreateLessonAsync(HttpClient superAdmin, Guid branchId, Guid groupId, Guid teacherId, DateTimeOffset startsAt)
    {
        var response = await superAdmin.PostAsJsonAsync("/api/schedule/lessons", new
        {
            branchId, groupId, teacherId, roomName = $"R-{Guid.NewGuid():N}"[..8], startsAt, endsAt = startsAt.AddMinutes(90)
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LessonDto>())!.Id;
    }

    private static async Task MarkAsync(HttpClient teacher, Guid lessonId, Guid studentId, string status)
    {
        var response = await teacher.PostAsJsonAsync($"/api/lessons/{lessonId}/attendance",
            new { items = new[] { new { studentId, status } } });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
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
    private record BranchDto(Guid Id, string Name, string Address, string Timezone);
    private record UserDto(Guid Id, string FullName, string Email, string Phone, string Status, List<string> Roles, List<Guid> BranchIds);
    private record CourseDto(Guid Id, string Name, string LevelCategory);
    private record GroupDto(Guid Id, Guid BranchId, Guid CourseId, string Name, int Capacity, string Status);
    private record LessonDto(Guid Id, Guid GroupId, Guid TeacherId, string RoomName, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? Topic);
    private record LeadDto(Guid Id, Guid BranchId, string FullName, string Status, Guid? ConvertedStudentId, string Priority);
    private record TrialDto(Guid Id, Guid LeadId, Guid GroupId, DateTimeOffset ScheduledAt, string AttendanceStatus);
    private record StudentDto(Guid Id, Guid UserId, string StudentNumber);
    private record CrmTaskDto(Guid Id, Guid? LeadId, Guid? StudentId, Guid AssignedTo, string Type, string Status,
        DateTimeOffset DueAt, string? ReasonCode, string? Note, string? SubjectName, string? SubjectPhone);
    private record SupportCaseDto(Guid Id, Guid StudentId, string StudentName, Guid GroupId, string Source, string SignalCode,
        string Reason, string Status, Guid? SupportTeacherId, string? SupportTeacherName);
    private record SupportCandidateDto(Guid UserId, string FullName, string Email);
    private record TimelineEventDto(Guid Id, string EventType, string? PayloadJson, DateTimeOffset OccurredAt);
}
