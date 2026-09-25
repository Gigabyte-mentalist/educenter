using EduCenter.Modules.Academic.Domain;
using EduCenter.Modules.Crm.Domain;
using EduCenter.Modules.Finance.Domain;
using EduCenter.Modules.Identity.Domain;
using EduCenter.Modules.Lms.Domain;
using EduCenter.Modules.Organizations.Domain;
using EduCenter.Modules.Students.Domain;
using EduCenter.Modules.Timeline.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Infrastructure.Seed;

/// <summary>
/// A centre of a realistic size for local development: ~170 students in 19 groups over three branches, 14 teachers,
/// two months of lessons with attendance, homework and exam results, invoices with payments and debtors, and a lead
/// pipeline. <see cref="DbSeeder"/> gives every screen something to show; this is what shows how the product behaves
/// with hundreds of rows in every list.
/// <para>
/// Opt-in (<c>Seed:BulkDemoData</c>): the integration tests boot the same Development seed and count rows, so a big
/// dataset there would change every one of them. Idempotent — it does nothing once its first student exists — and
/// deterministic, so two fresh databases hold the same people.
/// </para>
/// </summary>
public static class DemoDataSeeder
{
    /// <summary>Equal to the seats the group specs leave free, so no group is left half-empty by rounding.</summary>
    private const int StudentCount = 180;
    private const int WeeksBack = 5;
    private const int WeeksAhead = 3;
    private static readonly TimeSpan Tashkent = TimeSpan.FromHours(5);

    private static readonly string[] MaleNames =
    [
        "Aziz", "Bekzod", "Javohir", "Sardor", "Otabek", "Jasur", "Dilshod", "Shaxzod", "Abdulla", "Sherzod",
        "Ulugbek", "Timur", "Farrux", "Anvar", "Rustam", "Behruz", "Doston", "Eldor", "Jahongir", "Kamron",
        "Mirzo", "Nodir", "Oybek", "Sanjar", "Umid", "Zafar", "Islom", "Muhammad", "Akmal", "Bobur",
    ];

    private static readonly string[] FemaleNames =
    [
        "Malika", "Nigora", "Dilnoza", "Madina", "Zilola", "Sevara", "Gulnoza", "Feruza", "Shahlo", "Nilufar",
        "Laylo", "Kamola", "Aziza", "Barno", "Dildora", "Iroda", "Mohira", "Nodira", "Ozoda", "Sitora",
        "Zarina", "Yulduz", "Munisa", "Umida", "Charos", "Shirin", "Komila", "Xurshida", "Lola", "Robiya",
    ];

    /// <summary>Masculine forms; <see cref="Feminine"/> derives the feminine one.</summary>
    private static readonly string[] Surnames =
    [
        "Karimov", "Rahimov", "Yusupov", "Aliyev", "Nazarov", "Toshev", "Ergashev", "Qodirov", "Sodiqov", "Rustamov",
        "Abdullayev", "Ibragimov", "Hasanov", "Umarov", "Ismoilov", "Saidov", "Mirzayev", "Xolmatov", "Tursunov",
        "Normatov", "Sultonov", "Ahmedov", "Jalilov", "Qosimov", "Boboyev", "Ochilov", "Davlatov", "Ganiyev",
        "Nurmatov", "Kamolov", "Botirov", "Murodov", "Orifov", "Shukurov", "Zokirov", "Usmonov", "Yoldoshev", "Sharipov",
    ];

    private static readonly string[] LessonTopics =
    [
        "Introduction & diagnostic", "Present Simple vs Continuous", "Listening strategies", "Writing Task 1 — charts",
        "Speaking Part 2 practice", "Reading — skimming & scanning", "Past tenses", "Vocabulary: work & study",
        "Conditionals", "Writing Task 2 — opinion essay", "Listening — maps & diagrams", "Speaking Part 3 discussion",
        "Present Perfect", "Modal verbs", "Reported speech", "Passive voice", "Vocabulary: environment",
        "Reading — matching headings", "Mock test review", "Error correction workshop",
    ];

    private static readonly string[] SubmissionTexts =
    [
        "In my opinion, learning a foreign language opens many doors. Firstly, it helps people find better jobs.",
        "The chart shows how the number of students changed over five years. Overall, there was a steady rise.",
        "Last weekend I visited my grandparents. We cooked plov together and talked about our family history.",
        "Technology has changed the way we study. Some people think it is distracting, but I believe it is useful.",
        "The graph illustrates energy consumption in four countries. The highest figure belongs to the first country.",
    ];

    private sealed record CourseSpec(string Name, string Level, decimal Fee);

    private sealed record GroupSpec(
        string Name, string Course, string Branch, string Days, int Slot, string Room, string Teacher, int Capacity, int Fill,
        string? Support = null);

    private sealed record ExistingGroupSpec(string Name, string Days, int Slot, string Room, int ExtraFill);

    private static readonly CourseSpec[] NewCourses =
    [
        new("IELTS Foundation", "IELTS", 700_000m), new("General English A1", "A1", 500_000m),
        new("General English B1", "B1", 600_000m), new("Kids English", "Kids", 450_000m),
        new("Business English", "B2", 750_000m), new("TOEFL Preparation", "TOEFL", 900_000m),
    ];

    /// <summary>Slot → local start time. Kids groups meet for 60 minutes, everyone else for 90.</summary>
    private static readonly (int Hour, int Minute)[] Slots = [(9, 0), (11, 0), (14, 0), (16, 0), (18, 0)];

    private const string Chilonzor = "Chilonzor filiali";
    private const string Yunusobod = "Yunusobod filiali";
    private const string Sergeli = "Sergeli filiali";

    // Every (room, days, slot) and every (teacher, days, slot) below is unique, so the schedule the seed writes has
    // none of the conflicts the schedule screen exists to prevent.
    private static readonly GroupSpec[] NewGroups =
    [
        new("IELTS-22", "IELTS Intermediate", Chilonzor, "MWF", 1, "A102", "t1", 12, 11),
        new("IELTS-23", "IELTS Intermediate", Chilonzor, "TTS", 1, "A102", "t1", 12, 10),
        new("IELTS-Adv-2", "IELTS Advanced", Chilonzor, "MWF", 3, "C303", "t2", 10, 9, "s1"),
        new("IELTS-Found-1", "IELTS Foundation", Chilonzor, "TTS", 0, "A103", "t2", 14, 12),
        new("GenEng-B1-1", "General English B1", Chilonzor, "MWF", 0, "A103", "t3", 15, 12),
        new("GenEng-A1-1", "General English A1", Chilonzor, "TTS", 2, "A101", "t3", 15, 13, "s2"),
        new("IELTS-Y1", "IELTS Intermediate", Yunusobod, "MWF", 2, "B201", "t4", 12, 10),
        new("IELTS-Y2", "IELTS Foundation", Yunusobod, "TTS", 3, "B201", "t4", 14, 11),
        new("GenEng-A2-2", "General English A2", Yunusobod, "MWF", 3, "B203", "t5", 15, 12, "s3"),
        new("GenEng-B1-2", "General English B1", Yunusobod, "TTS", 0, "B203", "t5", 15, 9),
        new("Kids-1", "Kids English", Yunusobod, "MWF", 0, "B202", "t6", 12, 9),
        new("Business-1", "Business English", Yunusobod, "TTS", 4, "B201", "t6", 12, 7),
        new("GenEng-A2-S1", "General English A2", Sergeli, "MWF", 1, "S101", "t7", 15, 11),
        new("IELTS-S1", "IELTS Intermediate", Sergeli, "TTS", 2, "S102", "t8", 12, 9, "s4"),
        new("Kids-S1", "Kids English", Sergeli, "MWF", 2, "S103", "t9", 12, 8),
        new("TOEFL-1", "TOEFL Preparation", Sergeli, "TTS", 4, "S101", "t10", 10, 7),
    ];

    private static readonly ExistingGroupSpec[] ExistingGroups =
    [
        new("IELTS-21", "TTS", 3, "A101", 7),
        new("IELTS-Adv-1", "MWF", 4, "C303", 6),
        new("GenEng-A2-1", "MWF", 3, "B202", 7),
    ];

    private static readonly (string Key, string Name, string Branch)[] TeacherSpecs =
    [
        ("t1", "Dilshod Rahmonov", Chilonzor), ("t2", "Nargiza Abdurahmonova", Chilonzor),
        ("t3", "Bahodir Tursunov", Chilonzor), ("t4", "Shahnoza Karimova", Yunusobod),
        ("t5", "Jamshid Ergashev", Yunusobod), ("t6", "Mavluda Saidova", Yunusobod),
        ("t7", "Sanjar Nurmatov", Sergeli), ("t8", "Gulchehra Yusupova", Sergeli),
        ("t9", "Otabek Ismoilov", Sergeli), ("t10", "Zuhra Qosimova", Sergeli),
        ("s1", "Madina Botirova", Chilonzor), ("s2", "Ravshan Orifov", Chilonzor),
        ("s3", "Nodira Sultonova", Yunusobod), ("s4", "Kamron Shukurov", Sergeli),
    ];

    private sealed class Cohort
    {
        public required Group Group { get; init; }
        public required Branch Branch { get; init; }
        public required Course Course { get; init; }
        public required User Teacher { get; init; }
        public required string Days { get; init; }
        public required int Slot { get; init; }
        public required string Room { get; init; }
        public List<(Student Student, Enrollment Enrollment, DateTimeOffset EnrolledAt)> Members { get; } = [];
        public List<Lesson> Lessons { get; } = [];
    }

    private sealed record Trait(double Attendance, double Diligence, double Ability, bool Debtor);

    /// <param name="demoPassword">Same meaning as in <see cref="DbSeeder.SeedAsync"/>: null keeps the published demo password.</param>
    public static async Task SeedAsync(AppDbContext db, string? demoPassword = null, CancellationToken ct = default)
    {
        var org = await db.Organizations.FirstOrDefaultAsync(o => o.TenantSlug == "demo-center", ct);
        if (org is null) return;

        // One of the groups this seed creates is the marker: once it exists the seed has already run.
        if (await db.Groups.AnyAsync(g => g.OrganizationId == org.Id && g.Name == NewGroups[0].Name, ct)) return;

        var rng = new Random(20260921);
        var existingEmails = (await db.Users.Select(u => u.Email).ToListAsync(ct)).ToHashSet();
        var students = BuildPeople(StudentCount, existingEmails, rng, seedOffset: 0);

        var roles = await db.Roles.ToDictionaryAsync(r => r.Code, ct);
        // Names are not unique in a database that has seen the security tests ("BFLA Branch" twice), so first-by-name.
        var branches = FirstByName(await db.Branches.Where(b => b.OrganizationId == org.Id).ToListAsync(ct), b => b.Name);
        if (!branches.TryGetValue(Sergeli, out var sergeliBranch))
        {
            sergeliBranch = new Branch(org.Id, Sergeli, "Toshkent, Sergeli", "Asia/Tashkent");
            db.Branches.Add(sergeliBranch);
            branches[Sergeli] = sergeliBranch;
        }

        var hash = new PasswordHasher<User>().HashPassword(
            new User(org.Id, "seed", "+998000000000", "seed@demo-center.test", string.Empty), demoPassword ?? DbSeeder.DemoPassword);

        User NewUser(string roleCode, string fullName, string email, string phone, params Guid[] branchIds)
        {
            var user = new User(org.Id, fullName, phone, email, string.Empty);
            user.SetPasswordHash(hash);
            user.AssignRole(roles[roleCode].Id);
            foreach (var branchId in branchIds) user.AssignBranch(branchId);
            db.Users.Add(user);
            existingEmails.Add(email);
            return user;
        }

        var operatorUser = await db.Users.Include(u => u.Branches).FirstAsync(u => u.Email == "operator@demo-center.test", ct);
        if (operatorUser.Branches.All(b => b.BranchId != sergeliBranch.Id)) operatorUser.AssignBranch(sergeliBranch.Id);
        var demoTeacher = await db.Users.Include(u => u.Branches).FirstAsync(u => u.Email == "teacher@demo-center.test", ct);
        if (demoTeacher.Branches.All(b => b.BranchId != sergeliBranch.Id)) demoTeacher.AssignBranch(sergeliBranch.Id);
        var receiver = await db.Users.FirstAsync(u => u.Email == "admin@demo-center.test", ct);

        NewUser(RoleCode.Administrator, "Yunusobod Administrator", "admin.yunusobod@demo-center.test", "+998900000021", branches[Yunusobod].Id);
        NewUser(RoleCode.Administrator, "Sergeli Administrator", "admin.sergeli@demo-center.test", "+998900000022", sergeliBranch.Id);

        // ---- Staff -----------------------------------------------------------------------------
        var staff = new Dictionary<string, User>();
        foreach (var (key, name, branch) in TeacherSpecs)
        {
            var email = UniqueEmail(name.Split(' ')[0], name.Split(' ')[1], existingEmails);
            staff[key] = NewUser(key.StartsWith('t') ? RoleCode.Teacher : RoleCode.SupportTeacher, name, email,
                $"+99890{7100000 + staff.Count * 3137:D7}", branches[branch].Id);
        }

        // ---- Courses ---------------------------------------------------------------------------
        var courses = FirstByName(await db.Courses.Where(c => c.OrganizationId == org.Id).ToListAsync(ct), c => c.Name);
        foreach (var spec in NewCourses.Where(c => !courses.ContainsKey(c.Name)))
        {
            var course = new Course(org.Id, spec.Name, spec.Level, spec.Fee);
            db.Courses.Add(course);
            courses[spec.Name] = course;
        }

        // ---- Groups ----------------------------------------------------------------------------
        var cohorts = new List<Cohort>();
        foreach (var spec in NewGroups)
        {
            var group = new Group(org.Id, branches[spec.Branch].Id, courses[spec.Course].Id, spec.Name, spec.Capacity);
            group.Activate();
            group.AssignStaff(staff[spec.Teacher].Id, GroupStaffRole.MainTeacher);
            if (spec.Support is not null) group.AssignStaff(staff[spec.Support].Id, GroupStaffRole.SupportTeacher);
            db.Groups.Add(group);
            cohorts.Add(new Cohort
            {
                Group = group, Branch = branches[spec.Branch], Course = courses[spec.Course], Teacher = staff[spec.Teacher],
                Days = spec.Days, Slot = spec.Slot, Room = spec.Room,
            });
        }

        var existingGroups = FirstByName(await db.Groups.Where(g => g.OrganizationId == org.Id).ToListAsync(ct), g => g.Name);
        var existingCourseNames = await db.Courses.Where(c => c.OrganizationId == org.Id).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        foreach (var spec in ExistingGroups.Where(s => existingGroups.ContainsKey(s.Name)))
        {
            var group = existingGroups[spec.Name];
            var branch = branches.Values.First(b => b.Id == group.BranchId);
            cohorts.Add(new Cohort
            {
                Group = group, Branch = branch, Course = courses[existingCourseNames[group.CourseId]], Teacher = demoTeacher,
                Days = spec.Days, Slot = spec.Slot, Room = spec.Room,
            });
        }

        // ---- Students and enrollments ------------------------------------------------------------
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.ToOffset(Tashkent).DateTime);

        // One queue entry per free seat, in group order, so a group's students are contiguous and share its branch.
        var seats = new List<Cohort>();
        foreach (var spec in NewGroups) seats.AddRange(Enumerable.Repeat(cohorts.First(c => c.Group.Name == spec.Name), spec.Fill));
        foreach (var spec in ExistingGroups.Where(s => existingGroups.ContainsKey(s.Name)))
            seats.AddRange(Enumerable.Repeat(cohorts.First(c => c.Group.Name == spec.Name), spec.ExtraFill));

        var traits = new Dictionary<Guid, Trait>();
        var studentEntities = new List<Student>();
        var seq = 1100;
        for (var i = 0; i < students.Count; i++)
        {
            var cohort = seats[i % seats.Count];
            var person = students[i];
            var user = NewUser(RoleCode.Student, person.FullName, person.Email, person.Phone, cohort.Branch.Id);

            var enrolledAt = now.AddDays(-rng.Next(5, 160));
            var student = new Student(org.Id, user.Id, $"STU-{now:yyyyMM}-{seq++}",
                DateOnly.FromDateTime(enrolledAt.ToOffset(Tashkent).DateTime), LevelFor(cohort.Course.Name));
            db.Students.Add(student);
            studentEntities.Add(student);

            var chronicAbsentee = rng.NextDouble() < 0.08;
            traits[student.Id] = new Trait(
                Attendance: chronicAbsentee ? 0.35 + rng.NextDouble() * 0.15 : 0.72 + rng.NextDouble() * 0.27,
                Diligence: 0.35 + rng.NextDouble() * 0.6,
                Ability: Math.Clamp(6.0 + (rng.NextDouble() + rng.NextDouble() + rng.NextDouble() - 1.5) * 1.6, 3.5, 8.5),
                Debtor: rng.NextDouble() < 0.12);

            Enroll(db, org.Id, cohort, student, enrolledAt);
        }

        // A few students attend a second group — a spare seat in one that is not their own.
        for (var i = 0; i < 10 && i * 7 < studentEntities.Count; i++)
        {
            var student = studentEntities[i * 7];
            var owned = cohorts.Where(c => c.Members.Any(m => m.Student.Id == student.Id)).Select(c => c.Group.Id).ToHashSet();
            var second = cohorts.FirstOrDefault(c =>
                !owned.Contains(c.Group.Id) && c.Branch.Id == cohorts.First(x => owned.Contains(x.Group.Id)).Branch.Id
                && c.Members.Count < c.Group.Capacity);
            if (second is not null) Enroll(db, org.Id, second, student, now.AddDays(-rng.Next(5, 60)));
        }

        // Parents for the first 60 students: the account a guardian logs in with.
        var parentPeople = BuildPeople(60, existingEmails, rng, seedOffset: 900);
        for (var i = 0; i < parentPeople.Count; i++)
        {
            var student = studentEntities[i];
            var branchId = cohorts.First(c => c.Members.Any(m => m.Student.Id == student.Id)).Branch.Id;
            var parentUser = NewUser(RoleCode.Parent, parentPeople[i].FullName, parentPeople[i].Email, parentPeople[i].Phone, branchId);
            var parent = new Parent(org.Id, parentUser.Id);
            db.Parents.Add(parent);
            db.StudentParents.Add(new StudentParent(student.Id, parent.Id, ParentRelationship.Guardian));
        }

        // ---- Lessons, attendance, homework, exams ------------------------------------------------
        var thisMonday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        foreach (var cohort in cohorts)
        {
            SeedLessons(db, org.Id, cohort, thisMonday, now);
            SeedAttendance(db, org.Id, cohort, traits, rng, now);
            SeedHomework(db, org.Id, cohort, traits, rng, now);
            SeedExams(db, org.Id, cohort, traits, rng, today);
        }

        // ---- Finance -----------------------------------------------------------------------------
        foreach (var cohort in cohorts)
            SeedInvoices(db, org.Id, cohort, traits, receiver.Id, rng, now, today);

        // ---- CRM -----------------------------------------------------------------------------------
        SeedLeads(db, org.Id, operatorUser.Id, branches.Values.ToList(), cohorts, existingEmails, rng, now);

        await db.SaveChangesAsync(ct);
        await DbSeeder.BackfillRoomsAsync(db, ct);
    }

    private static void Enroll(AppDbContext db, Guid orgId, Cohort cohort, Student student, DateTimeOffset enrolledAt)
    {
        var enrollment = new Enrollment(orgId, student.Id, cohort.Group.Id);
        db.Enrollments.Add(enrollment);
        // The constructor stamps "now"; billing anchors on this date, so history has to be back-dated to look lived-in.
        db.Entry(enrollment).Property(e => e.EnrolledAt).CurrentValue = enrolledAt;
        cohort.Members.Add((student, enrollment, enrolledAt));
        db.StudentTimelineEvents.Add(new StudentTimelineEvent(orgId, student.Id, TimelineEventType.Enrolled,
            $$"""{"groupId":"{{cohort.Group.Id}}"}"""));
    }

    private static void SeedLessons(AppDbContext db, Guid orgId, Cohort cohort, DateOnly thisMonday, DateTimeOffset now)
    {
        var dayOffsets = cohort.Days == "MWF" ? new[] { 0, 2, 4 } : new[] { 1, 3, 5 };
        var (hour, minute) = Slots[cohort.Slot];
        var minutes = cohort.Course.Name == "Kids English" ? 60 : 90;

        var topicIndex = 0;
        for (var week = -WeeksBack; week <= WeeksAhead; week++)
        {
            foreach (var offset in dayOffsets)
            {
                var date = thisMonday.AddDays(week * 7 + offset);
                var start = new DateTimeOffset(date.Year, date.Month, date.Day, hour, minute, 0, Tashkent);
                var lesson = new Lesson(orgId, cohort.Branch.Id, cohort.Group.Id, cohort.Teacher.Id, cohort.Room,
                    start, start.AddMinutes(minutes), $"Unit {topicIndex + 1}: {LessonTopics[topicIndex % LessonTopics.Length]}");
                db.Lessons.Add(lesson);
                cohort.Lessons.Add(lesson);
                topicIndex++;
            }
        }
    }

    private static void SeedAttendance(
        AppDbContext db, Guid orgId, Cohort cohort, Dictionary<Guid, Trait> traits, Random rng, DateTimeOffset now)
    {
        foreach (var lesson in cohort.Lessons.Where(l => l.EndsAt <= now))
        {
            foreach (var (student, _, _) in cohort.Members.Where(m => m.EnrolledAt <= lesson.EndsAt))
            {
                var trait = traits[student.Id];
                AttendanceStatus status;
                if (rng.NextDouble() < trait.Attendance)
                {
                    var roll = rng.NextDouble();
                    status = roll < 0.90 ? AttendanceStatus.Present : roll < 0.97 ? AttendanceStatus.Late : AttendanceStatus.Online;
                }
                else
                {
                    status = rng.NextDouble() < 0.75 ? AttendanceStatus.Absent : AttendanceStatus.Excused;
                }

                db.Attendance.Add(new Attendance(orgId, lesson.Id, student.Id, status));
                var type = status == AttendanceStatus.Absent ? TimelineEventType.LessonAbsent
                    : status is AttendanceStatus.Present or AttendanceStatus.Late or AttendanceStatus.Online ? TimelineEventType.LessonAttended
                    : null;
                if (type is not null)
                    db.StudentTimelineEvents.Add(new StudentTimelineEvent(orgId, student.Id, type, $$"""{"lessonId":"{{lesson.Id}}"}"""));
            }
        }
    }

    private static void SeedHomework(
        AppDbContext db, Guid orgId, Cohort cohort, Dictionary<Guid, Trait> traits, Random rng, DateTimeOffset now)
    {
        var types = new[] { HomeworkType.Writing, HomeworkType.Grammar, HomeworkType.Vocabulary, HomeworkType.Reading };
        var titles = new[]
        {
            "Writing Task — short essay", "Grammar exercises", "Vocabulary word list quiz", "Reading — True/False/Not Given",
        };

        var past = cohort.Lessons.Where(l => l.EndsAt <= now).OrderBy(l => l.StartsAt).ToList();
        for (var p = 1; p < past.Count; p += 3)
        {
            var lesson = past[p];
            var kind = (p / 3) % types.Length;
            var deadline = lesson.EndsAt.AddDays(3);
            var homework = new Homework(orgId, lesson.Id, $"{titles[kind]} #{p / 3 + 1}",
                "Vazifani belgilangan muddatgacha yuboring.", types[kind], deadline, 100);
            db.Homework.Add(homework);

            foreach (var (student, _, _) in cohort.Members.Where(m => m.EnrolledAt <= lesson.StartsAt))
            {
                var diligence = traits[student.Id].Diligence * (deadline > now ? 0.4 : 1.0);
                if (rng.NextDouble() >= diligence) continue;

                var submission = new HomeworkSubmission(orgId, homework.Id, student.Id, null,
                    SubmissionTexts[rng.Next(SubmissionTexts.Length)]);
                if (deadline <= now && rng.NextDouble() < 0.75)
                    submission.Review(Math.Clamp((int)(traits[student.Id].Ability * 12 + rng.Next(-8, 9)), 40, 100),
                        "Yaxshi urinish. Grammatika va lug'atga ko'proq e'tibor bering.", cohort.Teacher.Id);
                db.HomeworkSubmissions.Add(submission);
                db.StudentTimelineEvents.Add(new StudentTimelineEvent(orgId, student.Id, TimelineEventType.HomeworkSubmitted,
                    $$"""{"homeworkId":"{{homework.Id}}","title":"{{homework.Title}}"}"""));
            }
        }
    }

    private static void SeedExams(
        AppDbContext db, Guid orgId, Cohort cohort, Dictionary<Guid, Trait> traits, Random rng, DateOnly today)
    {
        var exams = new List<(string Name, ExamType Type, DateOnly Date)> { ("Unit 1 Test", ExamType.Unit, today.AddDays(-21)) };
        if (cohort.Course.Name.StartsWith("IELTS") || cohort.Course.Name.StartsWith("TOEFL"))
            exams.Add(("Mock IELTS #1", ExamType.MockIelts, today.AddDays(-7)));

        foreach (var (name, type, date) in exams)
        {
            var exam = new Exam(orgId, cohort.Group.Id, name, type, date);
            db.Exams.Add(exam);

            var examDay = new DateTimeOffset(date.Year, date.Month, date.Day, 12, 0, 0, Tashkent);
            foreach (var (student, _, _) in cohort.Members.Where(m => m.EnrolledAt <= examDay))
            {
                if (rng.NextDouble() < 0.1) continue; // absent that day

                decimal Skill() => HalfBand(traits[student.Id].Ability + (rng.NextDouble() - 0.5) * 1.4);
                var (l, r, w, s) = (Skill(), Skill(), Skill(), Skill());
                var overall = HalfBand((double)(l + r + w + s) / 4);

                // Unit/Monthly tests grade Grammar and Vocabulary as percentages; a Mock IELTS does not — so the
                // Learning Profile and the branch dashboard see both an exam that has them and one that has not.
                decimal? grammar = null, vocabulary = null;
                if (type is ExamType.Unit or ExamType.Monthly)
                {
                    decimal Percent() => Math.Clamp(
                        Math.Round((decimal)(traits[student.Id].Ability / 9 * 100 + (rng.NextDouble() - 0.5) * 24)), 30m, 100m);
                    (grammar, vocabulary) = (Percent(), Percent());
                }

                db.ExamResults.Add(new ExamResult(orgId, exam.Id, student.Id, l, r, w, s, overall, grammar, vocabulary));
                db.StudentTimelineEvents.Add(new StudentTimelineEvent(orgId, student.Id, TimelineEventType.ExamResultRecorded,
                    $$"""{"examId":"{{exam.Id}}","examName":"{{name}}","overall":{{overall.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}"""));
            }
        }
    }

    private static decimal HalfBand(double value) => (decimal)(Math.Clamp(Math.Round(value * 2, MidpointRounding.AwayFromZero) / 2, 3.0, 9.0));

    private static void SeedInvoices(
        AppDbContext db, Guid orgId, Cohort cohort, Dictionary<Guid, Trait> traits, Guid receivedBy, Random rng,
        DateTimeOffset now, DateOnly today)
    {
        if (cohort.Course.MonthlyFee <= 0) return;

        var methods = new[] { PaymentMethod.Cash, PaymentMethod.Card, PaymentMethod.Click, PaymentMethod.Payme, PaymentMethod.BankTransfer };
        foreach (var (student, enrollment, enrolledAt) in cohort.Members)
        {
            var trait = traits[student.Id];
            var anchor = DateOnly.FromDateTime(enrolledAt.ToOffset(Tashkent).DateTime);

            // The same periods the billing job would have issued: one per month since enrolment, the current one included.
            for (var n = 0; anchor.AddMonths(n) <= today; n++)
            {
                var start = anchor.AddMonths(n);
                var end = anchor.AddMonths(n + 1);
                var isCurrent = end > today;
                var invoice = new Invoice(orgId, cohort.Branch.Id, student.Id, enrollment.Id, cohort.Group.Id,
                    $"{cohort.Group.Name} · {start:dd.MM.yyyy} – {end.AddDays(-1):dd.MM.yyyy}",
                    start, end, start, cohort.Course.MonthlyFee, "UZS");
                db.Invoices.Add(invoice);

                var paidChance = trait.Debtor ? (isCurrent ? 0.1 : 0.4) : (isCurrent ? 0.62 : 0.95);
                var roll = rng.NextDouble();
                if (roll >= paidChance && !(isCurrent && roll < paidChance + 0.08)) continue;

                var partial = roll >= paidChance; // the small band above the chance is a part-payment
                var amount = partial ? Math.Round(invoice.Amount / 2 / 1000m) * 1000m : invoice.Amount;
                var paidAt = new DateTimeOffset(start.Year, start.Month, start.Day, 10, 0, 0, Tashkent).AddDays(rng.Next(0, 6));
                if (paidAt > now) paidAt = now.AddHours(-1);

                invoice.ApplyPayment(amount);
                db.Payments.Add(new Payment(orgId, cohort.Branch.Id, invoice.Id, student.Id, amount,
                    methods[rng.Next(methods.Length)], paidAt, receivedBy, null));
            }
        }
    }

    private static void SeedLeads(
        AppDbContext db, Guid orgId, Guid operatorId, List<Branch> branches, List<Cohort> cohorts,
        HashSet<string> emails, Random rng, DateTimeOffset now)
    {
        var people = BuildPeople(46, emails, rng, seedOffset: 400);
        var sources = new[] { "Instagram", "Telegram", "Website", "Call", "Referral", "Walk-in" };
        var interests = new[] { "IELTS", "General English", "Kids English", "Business English", "TOEFL" };
        var ordered = branches.OrderBy(b => b.Name).ToList();

        for (var i = 0; i < people.Count; i++)
        {
            var branch = ordered[i % ordered.Count];
            var lead = new Lead(orgId, branch.Id, people[i].FullName, people[i].Phone, sources[i % sources.Length],
                interests[i % interests.Length], operatorId);
            db.Leads.Add(lead);

            void Call(CallOutcome outcome, string notes) => db.CrmCalls.Add(new CrmCall(orgId, lead.Id, operatorId, outcome, notes));

            switch (i)
            {
                case < 12: // untouched
                    break;
                case < 24:
                    Call(CallOutcome.Answered, "Kurs va narxlar haqida gaplashdik.");
                    lead.SetStatus(LeadStatus.Contacted);
                    break;
                case < 31: // a trial booked ahead (24-26), one already attended (27-29, ready to enrol), one missed (30)
                    Call(CallOutcome.TrialBooked, "Sinov darsiga yozildi.");
                    var trialGroup = cohorts.FirstOrDefault(c => c.Branch.Id == branch.Id);
                    if (trialGroup is null)
                    {
                        lead.SetStatus(LeadStatus.TrialBooked);
                        break;
                    }

                    var upcomingTrial = i < 27;
                    var trial = new TrialLesson(orgId, lead.Id, trialGroup.Group.Id,
                        upcomingTrial ? now.AddDays(1 + i % 5) : now.AddDays(-(1 + i % 3)));
                    db.TrialLessons.Add(trial);
                    if (upcomingTrial)
                    {
                        lead.SetStatus(LeadStatus.TrialBooked);
                    }
                    else if (i < 30)
                    {
                        trial.MarkAttended();
                        lead.SetStatus(LeadStatus.TrialAttended);
                    }
                    else
                    {
                        trial.MarkMissed();
                        lead.SetStatus(LeadStatus.TrialMissed);
                    }
                    break;
                case < 36:
                    Call(CallOutcome.NotInterested, "Hozircha o'qishni rejalashtirmayapti.");
                    lead.MarkLost("NotInterested");
                    break;
                case < 40:
                    Call(CallOutcome.WrongNumber, "Raqam noto'g'ri.");
                    lead.MarkLost("WrongNumber");
                    break;
                default: // waiting for a call back — fills the operator's queue, some of it overdue
                    Call(CallOutcome.CallLater, "Band edi, keyinroq qo'ng'iroq qilish kerak.");
                    lead.SetStatus(LeadStatus.Contacted);
                    db.CrmTasks.Add(new CrmTask(orgId, lead.Id, null, operatorId, CrmTaskType.CallBack,
                        now.AddHours(i % 3 == 0 ? -30 : (i % 3) * 5)));
                    break;
            }
        }
    }

    private sealed record Person(string FullName, string Email, string Phone);

    /// <summary>Deterministic, unique names — the same list on every fresh database, none clashing with existing accounts.</summary>
    private static List<Person> BuildPeople(int count, HashSet<string> takenEmails, Random rng, int seedOffset)
    {
        var people = new List<Person>(count);
        var claimed = new HashSet<string>(takenEmails);
        var prefixes = new[] { "90", "91", "93", "94", "95", "97", "99" };

        for (var i = 0; i < count; i++)
        {
            var index = i + seedOffset;
            var female = index % 2 == 0;
            var first = (female ? FemaleNames : MaleNames)[(index * 7 + 3) % (female ? FemaleNames.Length : MaleNames.Length)];
            var surname = Surnames[(index * 11 + 5) % Surnames.Length];
            var email = UniqueEmail(first, surname, claimed);
            claimed.Add(email);

            var lastName = female ? Feminine(surname) : surname;
            var phone = $"+998{prefixes[index % prefixes.Length]}{(1_000_000 + index * 7919 % 9_000_000):D7}";
            people.Add(new Person($"{first} {lastName}", email, phone));
        }

        _ = rng;
        return people;
    }

    private static string UniqueEmail(string first, string surname, HashSet<string> taken)
    {
        var stem = $"{first}.{surname}".ToLowerInvariant();
        var email = $"{stem}@demo-center.test";
        for (var n = 2; taken.Contains(email); n++) email = $"{stem}{n}@demo-center.test";
        return email;
    }

    private static Dictionary<string, T> FirstByName<T>(IEnumerable<T> rows, Func<T, string> name) =>
        rows.GroupBy(name).ToDictionary(g => g.Key, g => g.First());

    private static string Feminine(string surname) =>
        surname.EndsWith("ov") || surname.EndsWith("ev") ? surname + "a" : surname;

    private static string LevelFor(string courseName) => courseName switch
    {
        "General English A1" or "Kids English" => "A1",
        "General English A2" or "IELTS Foundation" => "A2",
        "General English B1" or "IELTS Intermediate" => "B1",
        "Business English" or "TOEFL Preparation" => "B2",
        "IELTS Advanced" => "C1",
        _ => "B1",
    };
}
