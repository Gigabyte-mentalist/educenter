using EduCenter.Modules.Academic.Domain;
using EduCenter.Modules.Crm.Domain;
using EduCenter.Modules.Identity.Domain;
using EduCenter.Modules.Lms.Domain;
using EduCenter.Modules.Organizations.Domain;
using EduCenter.Modules.Students.Domain;
using EduCenter.Modules.Timeline.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Infrastructure.Seed;

/// <summary>
/// Local-dev seed: roles + permissions from PermissionSeedData, one demo Organization with 2 Branches,
/// one user per role, and a realistic slice of content (courses, groups, leads at every lifecycle
/// stage, enrolled students, lessons with attendance, homework with a reviewed submission, an exam
/// with results) so every screen has something to show right after `docker compose up`.
/// Idempotent: content is created only when the demo Organization doesn't exist yet.
/// </summary>
public static class DbSeeder
{
    /// <summary>Shared password for every demo account — local dev only.</summary>
    public const string DemoPassword = "Passw0rd!";

    /// <param name="demoPassword">The password every demo account gets. Null keeps the published
    /// <see cref="DemoPassword"/> (a developer machine); a deployment reachable from the internet passes its own secret.</param>
    public static async Task SeedAsync(AppDbContext db, string? demoPassword = null)
    {
        var roles = await SeedReferenceDataAsync(db);
        await SeedDemoTenantAsync(db, roles, demoPassword ?? DemoPassword);
        await SeedDemoCourseFeesAsync(db);

        await db.SaveChangesAsync();
        await BackfillRoomsAsync(db);
    }

    /// <summary>Gives every lesson that only named its room a <see cref="Room"/> to be held in (S07.03): one room per branch and name, matched
    /// without regard to case so "A-12" and "a-12" become one. What the <c>AddRooms</c> migration does for rows that already exist, for
    /// lessons a seed has just written. Idempotent, and saves for itself.</summary>
    public static async Task BackfillRoomsAsync(AppDbContext db, CancellationToken ct = default)
    {
        var unlinked = await db.Lessons.Where(l => l.RoomId == null).ToListAsync(ct);
        if (unlinked.Count == 0) return;

        var rooms = await db.Rooms.ToListAsync(ct);
        foreach (var named in unlinked.GroupBy(l => (l.OrganizationId, l.BranchId, Name: l.RoomName.Trim().ToLowerInvariant())))
        {
            var room = rooms.FirstOrDefault(r => r.BranchId == named.Key.BranchId && r.Name.ToLowerInvariant() == named.Key.Name);
            if (room is null)
            {
                room = new Room(named.Key.OrganizationId, named.Key.BranchId, named.First().RoomName, capacity: null, equipment: null);
                db.Rooms.Add(room);
                rooms.Add(room);
            }
            foreach (var lesson in named) lesson.LinkRoom(room.Id);
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Roles, permissions and the role→permission matrix — the part of the seed a real deployment needs as
    /// much as a dev box does. Kept apart from the demo tenant (whose accounts share a published password) so a
    /// production start can load it without any demo account. Saves nothing itself: the caller commits.</summary>
    public static async Task<Dictionary<string, Role>> SeedReferenceDataAsync(AppDbContext db)
    {
        var roles = await SeedRolesAsync(db);
        var permissions = await SeedPermissionsAsync(db);
        await SeedRolePermissionsAsync(db, roles, permissions);
        return roles;
    }

    private static async Task<Dictionary<string, Role>> SeedRolesAsync(AppDbContext db)
    {
        var existing = await db.Roles.ToDictionaryAsync(r => r.Code);

        foreach (var (code, name) in PermissionSeedData.Roles)
        {
            if (existing.ContainsKey(code)) continue;
            var role = new Role(code, name);
            db.Roles.Add(role);
            existing[code] = role;
        }

        return existing;
    }

    private static async Task<Dictionary<string, Permission>> SeedPermissionsAsync(AppDbContext db)
    {
        var existing = await db.Permissions.ToDictionaryAsync(p => p.Code);

        foreach (var code in PermissionSeedData.Grants.Select(g => g.Code).Distinct())
        {
            if (existing.ContainsKey(code)) continue;
            var grant = PermissionSeedData.Grants.First(g => g.Code == code);
            var permission = new Permission(grant.Code, grant.Resource, grant.Action);
            db.Permissions.Add(permission);
            existing[code] = permission;
        }

        return existing;
    }

    private static async Task SeedRolePermissionsAsync(
        AppDbContext db, Dictionary<string, Role> roles, Dictionary<string, Permission> permissions)
    {
        var existingRows = await db.RolePermissions.ToListAsync();
        var existingPairs = existingRows.Select(rp => (rp.RoleId, rp.PermissionId)).ToHashSet();

        foreach (var grant in PermissionSeedData.Grants)
        {
            var role = roles[grant.RoleCode];
            var permission = permissions[grant.Code];

            var current = existingRows.FirstOrDefault(rp => rp.RoleId == role.Id && rp.PermissionId == permission.Id);
            if (current is null)
            {
                db.RolePermissions.Add(new RolePermission(role.Id, permission.Id, grant.Scope));
                continue;
            }

            // Keep an existing database in step with the matrix when a grant's scope is revised
            // (e.g. Operator crm.call.log Self → MyBranches) without requiring a DB reset.
            if (current.DefaultScope != grant.Scope)
            {
                db.RolePermissions.Remove(current);
                db.RolePermissions.Add(new RolePermission(role.Id, permission.Id, grant.Scope));
            }
        }

        _ = existingPairs;
    }

    /// <summary>Dev convenience for databases seeded before Finance existed: gives the demo courses their price
    /// once — only while every course of the demo organization is still free — so billing has something to bill.</summary>
    private static async Task SeedDemoCourseFeesAsync(AppDbContext db)
    {
        var org = await db.Organizations.FirstOrDefaultAsync(o => o.TenantSlug == "demo-center");
        if (org is null) return;

        var courses = await db.Courses.Where(c => c.OrganizationId == org.Id).ToListAsync();
        if (courses.Count == 0 || courses.Any(c => c.MonthlyFee > 0)) return;

        foreach (var course in courses)
        {
            var fee = course.Name switch
            {
                "IELTS Intermediate" => 800_000m,
                "IELTS Advanced" => 950_000m,
                "General English A2" => 550_000m,
                _ => 0m
            };
            if (fee > 0) course.Update(course.Name, course.LevelCategory, fee);
        }
    }

    private static async Task SeedDemoTenantAsync(AppDbContext db, Dictionary<string, Role> roles, string demoPassword)
    {
        if (await db.Organizations.AnyAsync()) return;

        var org = new Organization("Demo Learning Center", "demo-center");
        var chilonzor = org.AddBranch("Chilonzor filiali", "Toshkent, Chilonzor", "Asia/Tashkent");
        var yunusobod = org.AddBranch("Yunusobod filiali", "Toshkent, Yunusobod", "Asia/Tashkent");
        db.Organizations.Add(org);
        await db.SaveChangesAsync(); // need org/branch ids before creating users

        var hasher = new PasswordHasher<User>();
        User NewUser(string roleCode, string fullName, string email, string phone, params Guid[] branchIds)
        {
            var user = new User(org.Id, fullName, phone, email, passwordHash: string.Empty);
            user.SetPasswordHash(hasher.HashPassword(user, demoPassword));
            user.AssignRole(roles[roleCode].Id);
            foreach (var branchId in branchIds) user.AssignBranch(branchId);
            db.Users.Add(user);
            return user;
        }

        // Every demo user is attached to a branch — a branch-scoped Administrator would otherwise be
        // unable to see (or assign) the teacher at all.
        NewUser(RoleCode.SuperAdmin, "Super Admin Demo", "superadmin@demo-center.test", "+998900000001", chilonzor.Id, yunusobod.Id);
        NewUser(RoleCode.Administrator, "Administrator Demo", "admin@demo-center.test", "+998900000002", chilonzor.Id);
        var operatorUser = NewUser(RoleCode.Operator, "Operator Demo", "operator@demo-center.test", "+998900000003", chilonzor.Id, yunusobod.Id);
        var teacher = NewUser(RoleCode.Teacher, "Teacher Demo", "teacher@demo-center.test", "+998900000004", chilonzor.Id, yunusobod.Id);
        var support = NewUser(RoleCode.SupportTeacher, "Support Teacher Demo", "support@demo-center.test", "+998900000005", chilonzor.Id);
        var studentUser = NewUser(RoleCode.Student, "Student Demo", "student@demo-center.test", "+998900000006", chilonzor.Id);
        var parentUser = NewUser(RoleCode.Parent, "Parent Demo", "parent@demo-center.test", "+998900000007", chilonzor.Id);
        NewUser(RoleCode.Developer, "Developer Demo", "developer@demo-center.test", "+998900000008");

        // ---- Academic --------------------------------------------------------------------------
        var ieltsIntermediate = new Course(org.Id, "IELTS Intermediate", "IELTS", monthlyFee: 800_000m);
        var ieltsAdvanced = new Course(org.Id, "IELTS Advanced", "IELTS", monthlyFee: 950_000m);
        var generalA2 = new Course(org.Id, "General English A2", "A2", monthlyFee: 550_000m);
        db.Courses.AddRange(ieltsIntermediate, ieltsAdvanced, generalA2);

        var ielts21 = new Group(org.Id, chilonzor.Id, ieltsIntermediate.Id, "IELTS-21", 12);
        var ieltsAdv1 = new Group(org.Id, chilonzor.Id, ieltsAdvanced.Id, "IELTS-Adv-1", 10);
        var genEng = new Group(org.Id, yunusobod.Id, generalA2.Id, "GenEng-A2-1", 15);
        ielts21.Activate();
        genEng.Activate();
        db.Groups.AddRange(ielts21, ieltsAdv1, genEng);

        ielts21.AssignStaff(teacher.Id, GroupStaffRole.MainTeacher);
        ielts21.AssignStaff(support.Id, GroupStaffRole.SupportTeacher);
        ieltsAdv1.AssignStaff(teacher.Id, GroupStaffRole.MainTeacher);
        genEng.AssignStaff(teacher.Id, GroupStaffRole.MainTeacher);

        // ---- Students (the seeded Student Demo account is also a real enrolled student) --------
        var seq = 1000;
        Student NewStudent(User user, string level)
        {
            var student = new Student(org.Id, user.Id, $"STU-{DateTime.UtcNow:yyyyMM}-{seq++}",
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-14)), level);
            db.Students.Add(student);
            return student;
        }
        void Enroll(Student student, Group group)
        {
            db.Enrollments.Add(new Enrollment(org.Id, student.Id, group.Id));
            db.StudentTimelineEvents.Add(new StudentTimelineEvent(org.Id, student.Id, TimelineEventType.Enrolled,
                $$"""{"groupId":"{{group.Id}}"}"""));
        }

        var demoStudent = NewStudent(studentUser, "B1");
        Enroll(demoStudent, ielts21);

        var parent = new Parent(org.Id, parentUser.Id);
        db.Parents.Add(parent);
        db.StudentParents.Add(new StudentParent(demoStudent.Id, parent.Id, ParentRelationship.Guardian));

        var javlonUser = NewUser(RoleCode.Student, "Javlon Karimov", "javlon.karimov@demo-center.test", "+998901112266", chilonzor.Id);
        var feruzaUser = NewUser(RoleCode.Student, "Feruza Tosheva", "feruza.tosheva@demo-center.test", "+998901112299", chilonzor.Id);
        var nigoraUser = NewUser(RoleCode.Student, "Nigora Sodiqova", "nigora.sodiqova@demo-center.test", "+998901112277", yunusobod.Id);
        var javlon = NewStudent(javlonUser, "B1");
        var feruza = NewStudent(feruzaUser, "B2");
        var nigora = NewStudent(nigoraUser, "A2");
        Enroll(javlon, ielts21);
        Enroll(feruza, ielts21);
        Enroll(nigora, genEng);

        // ---- CRM: leads at every stage of the lifecycle -----------------------------------------
        Lead NewLead(Branch branch, string name, string phone, string source, string course)
        {
            var lead = new Lead(org.Id, branch.Id, name, phone, source, course, operatorUser.Id);
            db.Leads.Add(lead);
            return lead;
        }
        void Call(Lead lead, CallOutcome outcome, string notes)
            => db.CrmCalls.Add(new CrmCall(org.Id, lead.Id, operatorUser.Id, outcome, notes));

        NewLead(chilonzor, "Dilnoza Rustamova", "+998901112233", "Instagram", "IELTS"); // untouched → "Yangi"

        var sardor = NewLead(chilonzor, "Sardor Aliyev", "+998901112244", "Telegram", "IELTS");
        Call(sardor, CallOutcome.Answered, "Kurs haqida gaplashdik, narxlarni yubordim");
        sardor.SetStatus(LeadStatus.Contacted);

        var malika = NewLead(yunusobod, "Malika Yusupova", "+998901112255", "Website", "General English");
        Call(malika, CallOutcome.CallLater, "Ish joyida band, kechqurun qo'ng'iroq qilish kerak");
        malika.SetStatus(LeadStatus.Contacted);
        db.CrmTasks.Add(new CrmTask(org.Id, malika.Id, null, operatorUser.Id, CrmTaskType.CallBack, DateTimeOffset.UtcNow.AddHours(6)));

        var otabek = NewLead(chilonzor, "Otabek Nazarov", "+998901112288", "Call", "IELTS");
        Call(otabek, CallOutcome.NotInterested, "Narxi qimmat dedi");
        otabek.MarkLost("NotInterested");

        var kamola = NewLead(chilonzor, "Kamola Ergasheva", "+998901112200", "Referral", "IELTS");
        Call(kamola, CallOutcome.TrialBooked, "Trial darsga yozildi");
        kamola.SetStatus(LeadStatus.TrialBooked);
        db.TrialLessons.Add(new TrialLesson(org.Id, kamola.Id, ielts21.Id, DateTimeOffset.UtcNow.AddDays(3)));

        // An overdue follow-up so the Call Queue's "muddati o'tgan" section isn't empty.
        var bekzod = NewLead(yunusobod, "Bekzod Qodirov", "+998901112211", "Instagram", "General English");
        Call(bekzod, CallOutcome.NoAnswer, "Javob bermadi");
        db.CrmTasks.Add(new CrmTask(org.Id, bekzod.Id, null, operatorUser.Id, CrmTaskType.FollowUp, DateTimeOffset.UtcNow.AddDays(-2)));

        // Converted leads keep their history — they point at the students created above.
        foreach (var (name, phone, source, student, branch) in new[]
        {
            ("Javlon Karimov", "+998901112266", "Referral", javlon, chilonzor),
            ("Feruza Tosheva", "+998901112299", "Instagram", feruza, chilonzor),
            ("Nigora Sodiqova", "+998901112277", "Walk-in", nigora, yunusobod),
        })
        {
            var lead = NewLead(branch, name, phone, source, branch == yunusobod ? "General English" : "IELTS");
            lead.MarkConverted(student.Id);
        }

        // ---- Schedule: past lessons (with attendance) and upcoming ones -------------------------
        var now = DateTimeOffset.UtcNow;
        var today9 = new DateTimeOffset(now.Year, now.Month, now.Day, 4, 0, 0, TimeSpan.Zero); // 09:00 Tashkent (UTC+5)

        Lesson NewLesson(Group group, Branch branch, string room, DateTimeOffset start, int minutes, string topic)
        {
            var lesson = new Lesson(org.Id, branch.Id, group.Id, teacher.Id, room, start, start.AddMinutes(minutes), topic);
            db.Lessons.Add(lesson);
            return lesson;
        }
        void Mark(Lesson lesson, Student student, AttendanceStatus status)
        {
            db.Attendance.Add(new Attendance(org.Id, lesson.Id, student.Id, status));
            var type = status == AttendanceStatus.Present ? TimelineEventType.LessonAttended
                : status == AttendanceStatus.Absent ? TimelineEventType.LessonAbsent : null;
            if (type is not null)
                db.StudentTimelineEvents.Add(new StudentTimelineEvent(org.Id, student.Id, type, $$"""{"lessonId":"{{lesson.Id}}"}"""));
        }

        var l1 = NewLesson(ielts21, chilonzor, "A101", today9.AddDays(-7), 90, "Unit 1: Introduction & Listening strategies");
        var l2 = NewLesson(ielts21, chilonzor, "A101", today9.AddDays(-5), 90, "Unit 2: Writing Task 1 — describing charts");
        var l3 = NewLesson(ielts21, chilonzor, "A101", today9.AddDays(-2), 90, "Unit 3: Speaking Part 2 practice");
        var g1 = NewLesson(genEng, yunusobod, "B202", today9.AddDays(-4).AddHours(5), 60, "Grammar: Present Simple");
        NewLesson(ielts21, chilonzor, "A101", today9.AddDays(2), 90, "Unit 4: Reading — skimming & scanning");
        NewLesson(ieltsAdv1, chilonzor, "C303", today9.AddDays(3).AddHours(2), 90, "Advanced Listening: lectures");
        NewLesson(genEng, yunusobod, "B202", today9.AddDays(3).AddHours(5), 60, "Vocabulary: Daily routine");

        foreach (var lesson in new[] { l1, l2, l3 })
        {
            Mark(lesson, demoStudent, AttendanceStatus.Present);
            Mark(lesson, feruza, AttendanceStatus.Present);
        }
        Mark(l1, javlon, AttendanceStatus.Present);
        Mark(l2, javlon, AttendanceStatus.Absent);
        Mark(l3, javlon, AttendanceStatus.Absent); // 2 consecutive absences → support signal (TZ §7)
        Mark(g1, nigora, AttendanceStatus.Present);

        // ---- Homework: one assignment, one reviewed submission, one still pending ---------------
        var hw = new Homework(org.Id, l2.Id, "Writing Task 1 — Bar Chart",
            "150 so'zdan kam bo'lmagan holda berilgan bar chart'ni tavsiflang.", HomeworkType.Writing, now.AddDays(2), 100);
        db.Homework.Add(hw);

        var feruzaSubmission = new HomeworkSubmission(org.Id, hw.Id, feruza.Id, null,
            "The bar chart illustrates the number of students enrolled in different courses over three years. Overall, IELTS enrolment rose steadily while General English remained stable...");
        feruzaSubmission.Review(78, "Yaxshi struktura; ba'zi grammatik xatolar bor, coherence yaxshilanishi kerak.", teacher.Id);
        db.HomeworkSubmissions.Add(feruzaSubmission);
        db.StudentTimelineEvents.Add(new StudentTimelineEvent(org.Id, feruza.Id, TimelineEventType.HomeworkSubmitted, $$"""{"homeworkId":"{{hw.Id}}","title":"{{hw.Title}}"}"""));
        db.StudentTimelineEvents.Add(new StudentTimelineEvent(org.Id, feruza.Id, TimelineEventType.HomeworkReviewed, $$"""{"homeworkId":"{{hw.Id}}","score":78}"""));

        db.HomeworkSubmissions.Add(new HomeworkSubmission(org.Id, hw.Id, demoStudent.Id, null,
            "The chart shows how many students joined each course between 2023 and 2025..."));
        db.StudentTimelineEvents.Add(new StudentTimelineEvent(org.Id, demoStudent.Id, TimelineEventType.HomeworkSubmitted, $$"""{"homeworkId":"{{hw.Id}}","title":"{{hw.Title}}"}"""));

        // ---- Exam: Mock IELTS with results ------------------------------------------------------
        var mock = new Exam(org.Id, ielts21.Id, "Mock IELTS #1", ExamType.MockIelts, DateOnly.FromDateTime(now.UtcDateTime.AddDays(-1)));
        db.Exams.Add(mock);
        void Result(Student student, decimal l, decimal r, decimal w, decimal s, decimal overall)
        {
            db.ExamResults.Add(new ExamResult(org.Id, mock.Id, student.Id, l, r, w, s, overall));
            db.StudentTimelineEvents.Add(new StudentTimelineEvent(org.Id, student.Id, TimelineEventType.ExamResultRecorded,
                $$"""{"examId":"{{mock.Id}}","examName":"{{mock.Name}}","overall":{{overall.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}"""));
        }
        Result(feruza, 7.0m, 7.0m, 6.5m, 7.0m, 7.0m);
        Result(javlon, 6.0m, 6.5m, 5.5m, 6.0m, 6.0m);
        Result(demoStudent, 6.5m, 6.0m, 6.0m, 6.5m, 6.5m);
    }
}
