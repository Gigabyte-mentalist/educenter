using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduCenter.Infrastructure;
using EduCenter.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EduCenter.Api.IntegrationTests;

/// <summary>
/// Complaint workflow (docs/21.09-audit-jurnali S06.07): New → Assigned → InProgress → Resolved → Closed. The people are the demo accounts (Chilonzor's
/// administrator, the operator, the teacher, a super admin) and, made for the occasion, a student and a parent in Chilonzor and another in a branch of
/// their own — the second branch is what shows that a complaint stays where it was filed.
/// </summary>
[Collection("Api")]
public class ComplaintTests(EduCenterWebApplicationFactory factory)
{
    [Fact]
    public async Task AParentFilesAComplaint_TheirBranchsAdministratorSeesIt_AnotherBranchsDoesNot()
    {
        var w = await BuildAsync();

        var here = await FileAsync(w.Parent, new { subject = Unique("Dars vaqti"), body = "Dars kech boshlanadi." });
        Assert.Equal("New", here.Status);
        Assert.Equal(w.Branch, here.BranchId);       // the only child, in the only branch: both are known without being said
        Assert.Equal(w.StudentId, here.StudentId);
        Assert.True(here.IsMine);
        Assert.Equal("Zz Complaint Student", here.StudentName);

        var elsewhere = await FileAsync(w.OtherParent, new { subject = Unique("Boshqa filial"), body = "Xona sovuq." });
        Assert.Equal(w.OtherBranch, elsewhere.BranchId);

        // Chilonzor's administrator reads Chilonzor's; the other branch's is not theirs — not in the list, and not there by its id either.
        Assert.Contains(await ListAsync(w.Admin), c => c.Id == here.Id && !c.IsMine && c.CanManage);
        Assert.DoesNotContain(await ListAsync(w.Admin), c => c.Id == elsewhere.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await w.Admin.GetAsync($"/api/complaints/{elsewhere.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await w.Admin.GetAsync($"/api/complaints/{here.Id}")).StatusCode);

        // The organization's manager and the operator (both branches) read both; the one who wrote it reads it.
        var superList = await ListAsync(w.SuperAdmin);
        Assert.All(new[] { here, elsewhere }, c => Assert.Contains(superList, x => x.Id == c.Id));
        Assert.Contains(await ListAsync(w.Operator), c => c.Id == here.Id);
        Assert.Equal(HttpStatusCode.OK, (await w.Parent.GetAsync($"/api/complaints/{here.Id}")).StatusCode);

        // The administrator of that branch was told, and its text stayed in the app.
        var told = Assert.Single(await NoticesAsync(w.Admin, "COMPLAINT_CREATED"), n => n.Link == $"/complaints?highlight={here.Id}");
        Assert.Equal("Yangi shikoyat", told.Title);
        Assert.DoesNotContain(await NoticesAsync(w.Admin, "COMPLAINT_CREATED"), n => n.Link == $"/complaints?highlight={elsewhere.Id}");
    }

    [Fact]
    public async Task EachPersonWhoFiles_SeesOnlyTheirOwn()
    {
        var w = await BuildAsync();

        var byStudent = await FileAsync(w.Student, new { subject = Unique("Talaba"), body = "Ustoz tez gapiradi." });
        var byParent = await FileAsync(w.Parent, new { subject = Unique("Ota-ona"), body = "Narx qimmat." });
        var byTeacher = await FileAsync(w.Teacher, new { subject = Unique("Ustoz"), body = "Proyektor ishlamayapti.", branchId = w.Branch });

        Assert.Equal(w.StudentId, byStudent.StudentId);   // a student's is about themselves without saying so
        Assert.Null(byTeacher.StudentId);

        // A student, a parent and a teacher do not hold the view permission: what they read is what they wrote.
        Assert.Equal(new[] { byStudent.Id }, (await ListAsync(w.Student)).Select(c => c.Id).ToArray());
        Assert.Equal(new[] { byParent.Id }, (await ListAsync(w.Parent)).Select(c => c.Id).ToArray());
        var teacherOwn = await ListAsync(w.Teacher);   // the demo teacher is shared with other tests: theirs may be there too, and only theirs
        Assert.Contains(teacherOwn, c => c.Id == byTeacher.Id);
        Assert.All(teacherOwn, c => Assert.True(c.IsMine));
        Assert.Equal(HttpStatusCode.NotFound, (await w.Student.GetAsync($"/api/complaints/{byParent.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await w.OtherParent.GetAsync($"/api/complaints/{byParent.Id}")).StatusCode);

        // The office files on somebody's behalf, about a student they can open.
        var byOperator = await FileAsync(w.Operator, new { subject = Unique("Telefon"), body = "Ota-ona qo'ng'iroq qildi.", studentId = w.StudentId });
        Assert.Equal(w.Branch, byOperator.BranchId);
        Assert.Equal(w.StudentId, byOperator.StudentId);
        Assert.Contains(await ListAsync(w.Admin), c => c.Id == byOperator.Id);

        Assert.All(await ListAsync(w.Student), c => Assert.True(c.IsMine));
    }

    [Fact]
    public async Task AComplaintIsHandedToStaffOfItsBranch_WhoIsToldOfIt()
    {
        var w = await BuildAsync();
        var here = await FileAsync(w.Parent, new { subject = Unique("Biriktirish"), body = "x" });
        var elsewhere = await FileAsync(w.OtherParent, new { subject = Unique("Boshqa"), body = "x" });

        // Who can take it: those who read complaints and reach its branch — Chilonzor's administrator is not a candidate for the other branch's.
        var candidates = await CandidatesAsync(w.Admin, here.Id);
        Assert.Equal(new[] { true, true, true }, new[] { w.AdminId, w.OperatorId, w.SuperAdminId }.Select(id => candidates.Contains(id)));
        Assert.DoesNotContain(w.TeacherId, candidates);
        var otherCandidates = await CandidatesAsync(w.SuperAdmin, elsewhere.Id);
        Assert.DoesNotContain(w.AdminId, otherCandidates);
        Assert.Contains(w.OperatorId, otherCandidates);

        Assert.Equal(HttpStatusCode.BadRequest, (await AssignAsync(w.Admin, here.Id, w.TeacherId)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await AssignAsync(w.SuperAdmin, elsewhere.Id, w.AdminId)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await AssignAsync(w.Admin, here.Id, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AssignAsync(w.Admin, Guid.NewGuid(), w.OperatorId)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await AssignAsync(w.Admin, elsewhere.Id, w.OperatorId)).StatusCode);   // not the administrator's branch

        Assert.Equal(HttpStatusCode.NoContent, (await AssignAsync(w.Admin, here.Id, w.OperatorId)).StatusCode);
        var assigned = await GetAsync(w.Admin, here.Id);
        Assert.Equal("Assigned", assigned.Status);
        Assert.Equal(w.OperatorId, assigned.AssignedToUserId);

        Assert.Single(await NoticesAsync(w.Operator, "COMPLAINT_ASSIGNED"), n => n.Link == $"/complaints?highlight={here.Id}");
        // Handing it to someone else while it is open keeps where it stands; the new person is told, the old one is not told again.
        Assert.Equal(HttpStatusCode.NoContent, (await AssignAsync(w.Admin, here.Id, w.AdminId)).StatusCode);
        var reassigned = await GetAsync(w.Admin, here.Id);
        Assert.Equal("Assigned", reassigned.Status);
        Assert.Equal(w.AdminId, reassigned.AssignedToUserId);
        Assert.DoesNotContain(await NoticesAsync(w.Admin, "COMPLAINT_ASSIGNED"), n => n.Link == $"/complaints?highlight={here.Id}");   // they gave it to themselves
    }

    [Fact]
    public async Task AComplaintOnlyMovesForward_OneStepAtATime()
    {
        var w = await BuildAsync();
        var c = await FileAsync(w.Parent, new { subject = Unique("Holat"), body = "x" });

        // Nobody is on it yet: it cannot be started, and it cannot be called "assigned" without naming who.
        Assert.Equal(HttpStatusCode.Conflict, (await StatusAsync(w.Admin, c.Id, "InProgress")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await StatusAsync(w.Admin, c.Id, "Assigned")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await StatusAsync(w.Admin, c.Id, "New")).StatusCode);

        await AssignAsync(w.Admin, c.Id, w.OperatorId);
        Assert.Equal(HttpStatusCode.NoContent, (await StatusAsync(w.Admin, c.Id, "InProgress")).StatusCode);
        Assert.Equal("InProgress", (await GetAsync(w.Admin, c.Id)).Status);

        // Never back, never the same twice, never past the outcome that has to be written.
        Assert.Equal(HttpStatusCode.Conflict, (await StatusAsync(w.Admin, c.Id, "Assigned")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await StatusAsync(w.Admin, c.Id, "New")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await StatusAsync(w.Admin, c.Id, "InProgress")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await StatusAsync(w.Admin, c.Id, "Resolved")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await StatusAsync(w.Admin, c.Id, "Closed")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Admin.PostAsJsonAsync($"/api/complaints/{c.Id}/status", new { status = "Nonsense" })).StatusCode);
        Assert.Equal("InProgress", (await GetAsync(w.Admin, c.Id)).Status);

        // Once resolved it cannot be reassigned or restarted; the branch may close it if the author never rates it.
        await ResolveAsync(w.Admin, c.Id, "Vaqt o'zgartirildi.");
        Assert.Equal(HttpStatusCode.Conflict, (await AssignAsync(w.Admin, c.Id, w.AdminId)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await StatusAsync(w.Admin, c.Id, "InProgress")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await StatusAsync(w.Admin, c.Id, "Closed")).StatusCode);
        var closed = await GetAsync(w.Admin, c.Id);
        Assert.Equal("Closed", closed.Status);
        Assert.Null(closed.SatisfactionScore);
        Assert.Equal(HttpStatusCode.Conflict, (await FeedbackAsync(w.Parent, c.Id, 9)).StatusCode);   // closed unrated: the door is shut
    }

    [Fact]
    public async Task ResolvingNeedsTheOutcomeInWords_AndTheAuthorIsAskedForARating()
    {
        var w = await BuildAsync();
        var c = await FileAsync(w.Parent, new { subject = Unique("Yechim"), body = "x" });

        await AssignAsync(w.Admin, c.Id, w.OperatorId);
        Assert.Equal(HttpStatusCode.BadRequest, (await ResolveAsync(w.Operator, c.Id, "  ")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ResolveAsync(w.Operator, c.Id, new string('x', 2001))).StatusCode);
        Assert.Equal("Assigned", (await GetAsync(w.Admin, c.Id)).Status);

        // The person it was handed to may settle it — they hold no manage permission, they were given the complaint — and nobody else at that level may.
        var mine = await GetAsync(w.Operator, c.Id);
        Assert.Equal((true, false), (mine.CanAct, mine.CanManage));
        var other = await FileAsync(w.Parent, new { subject = Unique("Begona"), body = "x" });
        await AssignAsync(w.Admin, other.Id, w.AdminId);
        Assert.Equal(HttpStatusCode.Forbidden, (await ResolveAsync(w.Operator, other.Id, "Men emasman.")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await StatusAsync(w.Operator, other.Id, "InProgress")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await ResolveAsync(w.Operator, c.Id, "Ustoz ogohlantirildi, dars vaqti to'g'rilandi.")).StatusCode);
        var resolved = await GetAsync(w.Parent, c.Id);
        Assert.Equal("Resolved", resolved.Status);
        Assert.Equal("Ustoz ogohlantirildi, dars vaqti to'g'rilandi.", resolved.Resolution);
        Assert.True(resolved.CanRate);
        Assert.NotNull(resolved.ResolvedAt);
        Assert.False((await GetAsync(w.Admin, c.Id)).CanRate);   // the verdict is the author's alone

        Assert.Single(await NoticesAsync(w.Parent, "COMPLAINT_RESOLVED"), n => n.Link == $"/complaints?highlight={c.Id}");
        Assert.Equal(HttpStatusCode.Conflict, (await ResolveAsync(w.Operator, c.Id, "Yana.")).StatusCode);   // once
    }

    [Fact]
    public async Task OnlyTheAuthorRates_AfterTheOutcome_FromZeroToTen()
    {
        var w = await BuildAsync();
        var c = await FileAsync(w.Parent, new { subject = Unique("Baho"), body = "x" });

        // Before it is resolved there is nothing to rate; a New complaint has no outcome at all.
        Assert.Equal(HttpStatusCode.Conflict, (await FeedbackAsync(w.Parent, c.Id, 8)).StatusCode);
        await AssignAsync(w.Admin, c.Id, w.OperatorId);
        Assert.Equal(HttpStatusCode.Conflict, (await FeedbackAsync(w.Parent, c.Id, 8)).StatusCode);
        await ResolveAsync(w.Admin, c.Id, "Hal qilindi.");

        Assert.Equal(HttpStatusCode.Forbidden, (await FeedbackAsync(w.Admin, c.Id, 10)).StatusCode);        // a manager does not speak for the author
        Assert.Equal(HttpStatusCode.Forbidden, (await FeedbackAsync(w.SuperAdmin, c.Id, 10)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await FeedbackAsync(w.OtherParent, c.Id, 10)).StatusCode);   // another branch's parent cannot even see it
        Assert.Equal(HttpStatusCode.BadRequest, (await FeedbackAsync(w.Parent, c.Id, 11)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await FeedbackAsync(w.Parent, c.Id, -1)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await FeedbackAsync(w.Parent, c.Id, 5, new string('x', 1001))).StatusCode);
        Assert.Equal("Resolved", (await GetAsync(w.Parent, c.Id)).Status);

        Assert.Equal(HttpStatusCode.NoContent, (await FeedbackAsync(w.Parent, c.Id, 7, "  Yaxshi, lekin kech.  ")).StatusCode);
        var closed = await GetAsync(w.Admin, c.Id);
        Assert.Equal("Closed", closed.Status);
        Assert.Equal(7, closed.SatisfactionScore);
        Assert.Equal("Yaxshi, lekin kech.", closed.FeedbackComment);
        Assert.False((await GetAsync(w.Parent, c.Id)).CanRate);
        Assert.Equal(HttpStatusCode.Conflict, (await FeedbackAsync(w.Parent, c.Id, 1)).StatusCode);   // one verdict

        // Zero is a score, not a missing one.
        var second = await FileAsync(w.Parent, new { subject = Unique("Nol"), body = "x" });
        await AssignAsync(w.Admin, second.Id, w.OperatorId);
        await ResolveAsync(w.Admin, second.Id, "Urinib ko'rildi.");
        Assert.Equal(HttpStatusCode.NoContent, (await FeedbackAsync(w.Parent, second.Id, 0)).StatusCode);
        Assert.Equal(0, (await GetAsync(w.Admin, second.Id)).SatisfactionScore);
    }

    [Fact]
    public async Task OnlyThoseWhoRunABranchHandOutAndSettle()
    {
        var w = await BuildAsync();
        var c = await FileAsync(w.Parent, new { subject = Unique("Ruxsat"), body = "x" });
        await AssignAsync(w.Admin, c.Id, w.OperatorId);

        foreach (var who in new[] { w.Teacher, w.Student, w.Parent, w.OtherParent })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await AssignAsync(who, c.Id, w.OperatorId)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await StatusAsync(who, c.Id, "InProgress")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await ResolveAsync(who, c.Id, "Yechim")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await who.GetAsync($"/api/complaints/{c.Id}/candidates")).StatusCode);
        }

        // The operator reads the branch's complaints but neither hands them out nor closes them (they may settle the one they were given).
        var another = await FileAsync(w.Parent, new { subject = Unique("Operator"), body = "x" });
        Assert.Equal(HttpStatusCode.Forbidden, (await AssignAsync(w.Operator, another.Id, w.OperatorId)).StatusCode);
        var seen = await GetAsync(w.Operator, another.Id);
        Assert.Equal((false, false), (seen.CanManage, seen.CanAct));

        // Not their branch: an administrator of Chilonzor cannot touch the other branch's complaint.
        var elsewhere = await FileAsync(w.OtherParent, new { subject = Unique("Chet"), body = "x" });
        Assert.Equal(HttpStatusCode.NotFound, (await ResolveAsync(w.Admin, elsewhere.Id, "Yechim")).StatusCode);

        // Those who hold no complaint permission at all have nothing to do here, and neither does a stranger.
        using var support = await ClientAsync("support@demo-center.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await support.GetAsync("/api/complaints")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await support.PostAsJsonAsync("/api/complaints", new { subject = "x", body = "x", branchId = w.Branch })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/complaints")).StatusCode);
    }

    [Fact]
    public async Task AComplaintNeedsAMatterAndATextAndAPlace()
    {
        var w = await BuildAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await w.Parent.PostAsJsonAsync("/api/complaints", new { subject = " ", body = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Parent.PostAsJsonAsync("/api/complaints", new { subject = "x", body = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Parent.PostAsJsonAsync("/api/complaints", new { subject = new string('x', 201), body = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Parent.PostAsJsonAsync("/api/complaints", new { subject = "x", body = new string('x', 4001) })).StatusCode);

        // The place: a teacher (two branches) has to say which; the administrator may only file where they work; somebody else's student is not theirs to file about.
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Teacher.PostAsJsonAsync("/api/complaints", new { subject = "x", body = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Admin.PostAsJsonAsync("/api/complaints", new { subject = "x", body = "x", branchId = w.OtherBranch })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Admin.PostAsJsonAsync("/api/complaints", new { subject = "x", body = "x", studentId = w.OtherStudentId })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Parent.PostAsJsonAsync("/api/complaints", new { subject = "x", body = "x", studentId = w.OtherStudentId })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await w.Operator.PostAsJsonAsync("/api/complaints", new { subject = "x", body = "x", studentId = Guid.NewGuid() })).StatusCode);
        // A Super Admin reads and runs the whole board but files nothing of their own.
        Assert.Equal(HttpStatusCode.Forbidden, (await w.SuperAdmin.PostAsJsonAsync("/api/complaints", new { subject = "x", body = "x", branchId = w.Branch })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Operator.PostAsJsonAsync("/api/complaints", new { subject = "x", body = "x", studentId = w.StudentId, branchId = w.OtherBranch })).StatusCode);

        // A parent of two children says which one; with a named child the branch follows from it.
        var secondChild = await NewStudentAsync(w.SuperAdmin, w.Group);
        var link = await w.SuperAdmin.PostAsJsonAsync($"/api/students/{secondChild.Id}/parents", new { userId = w.ParentUserId, relationship = "Father" });
        Assert.Equal(HttpStatusCode.Created, link.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Parent.PostAsJsonAsync("/api/complaints", new { subject = "x", body = "x" })).StatusCode);
        var named = await FileAsync(w.Parent, new { subject = Unique("Ikki farzand"), body = "x", studentId = secondChild.Id });
        Assert.Equal(secondChild.Id, named.StudentId);
        Assert.Equal(w.Branch, named.BranchId);
    }

    [Fact]
    public async Task TheListIsPaged_FiltersByStatus_AndSearches()
    {
        var w = await BuildAsync();
        var marker = Guid.NewGuid().ToString("N")[..10];
        var first = await FileAsync(w.Parent, new { subject = $"Qidiruv {marker} bir", body = "x" });
        var second = await FileAsync(w.Parent, new { subject = $"Qidiruv {marker} ikki", body = "x" });
        await FileAsync(w.OtherParent, new { subject = $"Qidiruv {marker} boshqa filial", body = "x" });
        await AssignAsync(w.Admin, second.Id, w.OperatorId);

        var all = (await w.Admin.GetFromJsonAsync<PagedResponse<ComplaintRow>>($"/api/complaints?search={marker}"))!;
        Assert.Equal(new[] { second.Id, first.Id }, all.Items.Select(c => c.Id).ToArray());   // newest first, and only this branch's

        var pageOne = (await w.Admin.GetFromJsonAsync<PagedResponse<ComplaintRow>>($"/api/complaints?search={marker}&size=1&page=1"))!;
        var pageTwo = (await w.Admin.GetFromJsonAsync<PagedResponse<ComplaintRow>>($"/api/complaints?search={marker}&size=1&page=2"))!;
        Assert.Equal((2, second.Id, first.Id), (pageOne.Total, pageOne.Items.Single().Id, pageTwo.Items.Single().Id));

        var open = (await w.Admin.GetFromJsonAsync<PagedResponse<ComplaintRow>>($"/api/complaints?search={marker}&statuses=New"))!;
        Assert.Equal(new[] { first.Id }, open.Items.Select(c => c.Id).ToArray());
        var both = (await w.Admin.GetFromJsonAsync<PagedResponse<ComplaintRow>>($"/api/complaints?search={marker}&statuses=New&statuses=Assigned"))!;
        Assert.Equal(2, both.Total);

        // By the writer's name and by the student's, too.
        var byName = (await w.Admin.GetFromJsonAsync<PagedResponse<ComplaintRow>>($"/api/complaints?search=Zz Complaint Parent&size=200"))!;
        Assert.Contains(byName.Items, c => c.Id == first.Id);
    }

    [Fact]
    public async Task TheTimelineRemembersIt_ButOnlyForThoseWhoHandleComplaints()
    {
        var w = await BuildAsync();
        var c = await FileAsync(w.Parent, new { subject = Unique("Tarix"), body = "Bu matn tarixga tushmasin." });
        await AssignAsync(w.Admin, c.Id, w.OperatorId);
        await ResolveAsync(w.Admin, c.Id, "Yechildi.");

        var seenByStaff = await TimelineAsync(w.Admin, w.StudentId);
        Assert.Equal(new[] { "COMPLAINT_CREATED", "COMPLAINT_RESOLVED" }, seenByStaff.Select(e => e.EventType).Where(t => t.StartsWith("COMPLAINT")).OrderBy(t => t).ToArray());
        Assert.All(seenByStaff.Where(e => e.EventType.StartsWith("COMPLAINT")), e => Assert.DoesNotContain("tarixga", e.PayloadJson ?? ""));
        Assert.Contains(await TimelineAsync(w.Operator, w.StudentId), e => e.EventType == "COMPLAINT_CREATED");

        // The student, and their parent, read the same timeline: a complaint — often about one of them — is not shown there.
        Assert.DoesNotContain(await TimelineAsync(w.Student, w.StudentId), e => e.EventType.StartsWith("COMPLAINT"));
        Assert.DoesNotContain(await TimelineAsync(w.Parent, w.StudentId), e => e.EventType.StartsWith("COMPLAINT"));

        // No student, no timeline entry.
        var general = await FileAsync(w.Teacher, new { subject = Unique("Umumiy"), body = "x", branchId = w.Branch });
        Assert.Null(general.StudentId);
    }

    // ---- the world ---------------------------------------------------------------------------

    private sealed record World(
        HttpClient SuperAdmin, HttpClient Admin, HttpClient Operator, HttpClient Teacher,
        HttpClient Student, HttpClient Parent, HttpClient OtherParent,
        Guid SuperAdminId, Guid AdminId, Guid OperatorId, Guid TeacherId,
        Guid StudentId, Guid ParentUserId, Guid OtherStudentId,
        Guid Branch, Guid OtherBranch, Guid Group);

    private record NewStudent(Guid Id, string Email);

    /// <summary>A student and a parent in Chilonzor (the administrator's branch), and another student and parent in a second branch.</summary>
    private async Task<World> BuildAsync()
    {
        var superAdmin = await ClientAsync("superadmin@demo-center.test");
        var admin = await ClientAsync("admin@demo-center.test");
        var branch = (await admin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))![0].Id;
        var otherBranch = (await superAdmin.GetFromJsonAsync<List<BranchDto>>("/api/branches"))!.First(b => b.Id != branch).Id;

        var group = await NewGroupAsync(superAdmin, branch);
        var otherGroup = await NewGroupAsync(superAdmin, otherBranch);
        var student = await NewStudentAsync(superAdmin, group);
        var otherStudent = await NewStudentAsync(superAdmin, otherGroup);
        var (parent, parentUserId) = await NewParentAsync(superAdmin, student.Id);
        var (otherParent, _) = await NewParentAsync(superAdmin, otherStudent.Id);

        return new World(
            superAdmin, admin, await ClientAsync("operator@demo-center.test"), await ClientAsync("teacher@demo-center.test"),
            await ClientAsync(student.Email), parent, otherParent,
            await UserIdAsync("superadmin@demo-center.test"), await UserIdAsync("admin@demo-center.test"),
            await UserIdAsync("operator@demo-center.test"), await UserIdAsync("teacher@demo-center.test"),
            student.Id, parentUserId, otherStudent.Id, branch, otherBranch, group);
    }

    private static string Unique(string what) => $"{what} {Guid.NewGuid():N}"[..Math.Min(80, what.Length + 9)];

    private static async Task<ComplaintRow> FileAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/complaints", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ComplaintRow>())!;
    }

    private static async Task<List<ComplaintRow>> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<PagedResponse<ComplaintRow>>("/api/complaints?size=200"))!.Items;

    private static async Task<ComplaintRow> GetAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<ComplaintRow>($"/api/complaints/{id}"))!;

    private static async Task<List<Guid>> CandidatesAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<List<CandidateRow>>($"/api/complaints/{id}/candidates"))!.Select(c => c.UserId).ToList();

    private static Task<HttpResponseMessage> AssignAsync(HttpClient client, Guid id, Guid userId) =>
        client.PostAsJsonAsync($"/api/complaints/{id}/assign", new { userId });

    private static Task<HttpResponseMessage> StatusAsync(HttpClient client, Guid id, string status) =>
        client.PostAsJsonAsync($"/api/complaints/{id}/status", new { status });

    private static Task<HttpResponseMessage> ResolveAsync(HttpClient client, Guid id, string resolution) =>
        client.PostAsJsonAsync($"/api/complaints/{id}/resolve", new { resolution });

    private static Task<HttpResponseMessage> FeedbackAsync(HttpClient client, Guid id, int score, string? comment = null) =>
        client.PostAsJsonAsync($"/api/complaints/{id}/feedback", new { score, comment });

    private static async Task<List<NoticeRow>> NoticesAsync(HttpClient client, string type) =>
        (await client.GetFromJsonAsync<List<NoticeRow>>("/api/notifications?limit=100"))!.Where(n => n.Type == type).ToList();

    private static async Task<List<TimelineRow>> TimelineAsync(HttpClient client, Guid studentId) =>
        (await client.GetFromJsonAsync<List<TimelineRow>>($"/api/students/{studentId}/timeline"))!;

    private async Task<Guid> UserIdAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
    }

    private static async Task<Guid> NewGroupAsync(HttpClient superAdmin, Guid branchId)
    {
        var course = await superAdmin.PostAsJsonAsync("/api/courses", new { name = $"Cmp Course {Guid.NewGuid():N}"[..25], levelCategory = "General" });
        Assert.Equal(HttpStatusCode.Created, course.StatusCode);
        var group = await superAdmin.PostAsJsonAsync("/api/groups", new
        {
            branchId, courseId = (await course.Content.ReadFromJsonAsync<IdDto>())!.Id, name = $"CMP-{Guid.NewGuid():N}"[..20], capacity = 20,
        });
        Assert.Equal(HttpStatusCode.Created, group.StatusCode);
        return (await group.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    private static async Task<NewStudent> NewStudentAsync(HttpClient superAdmin, Guid groupId)
    {
        var email = $"cmp-{Guid.NewGuid():N}@demo-center.test";
        var response = await superAdmin.PostAsJsonAsync("/api/students", new
        {
            // Named to sort last in the student list, which other tests read the first page of.
            fullName = "Zz Complaint Student", phone = "+998900000077", email, password = DbSeeder.DemoPassword, groupId,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return new NewStudent((await response.Content.ReadFromJsonAsync<IdDto>())!.Id, email);
    }

    /// <summary>A guardian account made for the student and signed in.</summary>
    private async Task<(HttpClient Client, Guid UserId)> NewParentAsync(HttpClient superAdmin, Guid studentId)
    {
        var email = $"cmp-parent-{Guid.NewGuid():N}@demo-center.test";
        var response = await superAdmin.PostAsJsonAsync($"/api/students/{studentId}/parents", new
        {
            fullName = "Zz Complaint Parent", phone = "+998900000078", email, password = DbSeeder.DemoPassword, relationship = "Mother",
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ClientAsync(email), (await response.Content.ReadFromJsonAsync<ParentLinkRow>())!.UserId);
    }

    /// <summary>Signed in — after the forced first-session password change of an account made through the API (S01.05).</summary>
    private async Task<HttpClient> ClientAsync(string email)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = DbSeeder.DemoPassword });
        var tokens = (await response.Content.ReadFromJsonAsync<TokenDto>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        if (tokens.MustChangePassword)
        {
            var change = await client.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = DbSeeder.DemoPassword, newPassword = "Chosen-Pass-42" });
            Assert.Equal(HttpStatusCode.OK, change.StatusCode);
            tokens = (await change.Content.ReadFromJsonAsync<TokenDto>())!;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        }

        return client;
    }

    private record TokenDto(string AccessToken, bool MustChangePassword);
    private record IdDto(Guid Id);
    private record BranchDto(Guid Id, string Name);
    private record ParentLinkRow(Guid UserId);
    private record CandidateRow(Guid UserId, string FullName);
    private record NoticeRow(Guid Id, string Type, string Title, string? Link);
    private record TimelineRow(string EventType, string? PayloadJson);
    private record ComplaintRow(
        Guid Id, Guid BranchId, Guid? StudentId, string? StudentName, Guid AuthorUserId, string Subject, string Status,
        Guid? AssignedToUserId, string? Resolution, DateTimeOffset? ResolvedAt, int? SatisfactionScore, string? FeedbackComment,
        bool IsMine, bool CanManage, bool CanAct, bool CanRate);
}
