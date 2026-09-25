# EduCenter Backend

Modular Monolith (ASP.NET Core / .NET 10). Papka strukturasi `../docs/03_phase1_technical_plan.md` §1 ga mos.

## Holat

.NET 10 SDK o'rnatildi, solution scaffold qilindi va build/test tasdiqlandi:

```
dotnet build EduCenter.slnx   # Build succeeded, 0 Warning(s), 0 Error(s)
dotnet test EduCenter.slnx    # Passed
```

`EduCenter.slnx` — .NET 10'dagi yangi XML-based solution format (`dotnet new sln` default).

## Loyihalar

| Loyiha | Turi | Vazifasi |
|---|---|---|
| `EduCenter.Api` | webapi | Composition root — barcha modullarni bog'laydi |
| `EduCenter.SharedKernel` | classlib | `BaseEntity`, `Result<T>`, `ITenantContext`, `ScopeType` |
| `EduCenter.Infrastructure` | classlib | EF Core DbContext, Postgres (keyingi sprint) |
| `EduCenter.Modules.Identity` | classlib | Auth, User, Role, Permission |
| `EduCenter.Modules.Organizations` | classlib | Organization, Branch |
| `EduCenter.Modules.Academic` | classlib | Course, Group, Schedule, GroupStaff |
| `EduCenter.Modules.Audit` | classlib | AuditLog |
| `*.Tests` | xunit | Identity va Academic uchun test loyihalari |

Har bir modul ichida `Domain/`, `Application/`, `Infrastructure/` qatlamlari tayyor (hozircha bo'sh — `.gitkeep` bilan). Modullar bir-biriga to'g'ridan-to'g'ri bog'lanmaydi, faqat `SharedKernel` orqali.

## Ishga tushirish

```bash
cd backend
dotnet restore
dotnet build EduCenter.slnx
dotnet run --project src/EduCenter.Api
```

## Sprint 2 — RBAC/ABAC/Scope (bajarildi)

- Local Postgres: `docker-compose.yml` (port **5433** — 5432 shu mashinada boshqa loyiha tomonidan band, konfliktga tegilmadi);
- Domain entitylar: `Organization`, `Branch` (Organizations), `User`, `Role`, `Permission`, `RolePermission`, `UserPermission`, `UserRole`, `UserBranch` (Identity), `Course`, `Group`, `GroupStaff`, `Lesson` (Academic), `AuditLog` (Audit) — barchasi `../docs/02_database_erd.md` §1-§3 ga mos;
- `AppDbContext` (`EduCenter.Infrastructure`) — 14 jadval, migration `InitialCreate` qo'llandi;
- `PermissionRequirement` + `PermissionPolicyProvider` + `PermissionAuthorizationHandler` (`EduCenter.Api/Authorization`) — controllerlarda `[Authorize(Policy = "students.view")]` kabi permission-kod-nomi bilan ishlaydi, natijada `ResolvedScope` (ScopeType + BranchIds) `HttpContext.Items`ga yoziladi;
- JWT bearer authentication ulandi (token **tekshirish** tomoni tayyor; login/token **chiqarish** endpointi hali yo'q — Sprint 3);
- `PermissionSeedData` — `../docs/01_role_permission_matrix.md`dagi Phase 1 subsetini (Organization/Branch, Users, Groups/Courses/Schedule, Audit, Developer) kod holatiga o'tkazadi;
- `DbSeeder` — Development muhitida avtomatik ishga tushadi: rollar, permissionlar, role_permissions, 1 ta demo Organization + 2 Branch + har bir roldan 1 ta demo user. **Parollar hali real hash emas** — login Sprint 3'da qo'shiladi.

Tekshirilgan: `dotnet build` (0 xato), `dotnet test` (o'tdi), `dotnet run` orqali seed ishga tushib, Postgres'da 8 rol / 22 permission / 61 role_permission / 8 demo user yozilgani tasdiqlandi.

## Sprint 3 — Auth + Organization/Branch/Users API + Audit interceptor (bajarildi)

- `POST /api/auth/login`, `/refresh`, `/logout` — `Microsoft.AspNetCore.Identity.PasswordHasher<User>` bilan haqiqiy parol hash, JWT access token (15 min) + rotatsiyalanuvchi refresh token (7 kun, faqat SHA-256 hash saqlanadi, `refresh_tokens` jadvali);
- `GET/POST /api/organizations`, `GET/POST/PATCH /api/branches`, `GET/POST/PATCH /api/users`, `POST /api/users/{id}/roles` — barchasi `[Authorize(Policy = "resource.action")]` + `ResolvedScope` asosida filtrlanadi (`ScopeFilterExtensions.AllowedBranchIds`);
- `AuditSaveChangesInterceptor` (`EduCenter.Infrastructure/Auditing`) — User/Branch/Organization o'zgarishlarini avtomatik `audit_logs`ga yozadi (WHO — JWT claim'dan, WHAT/OLD/NEW — EF `ChangeTracker`dan);
- Demo parol: **barcha demo userlarda** `Passw0rd!` (`DbSeeder.DemoPassword`) — faqat local dev.

**Uçtan-uçga tekshirildi** (real HTTP so'rovlar bilan): login → JWT olindi → `GET /api/branches` Super Admin uchun 2 ta, Administrator uchun (MyBranches scope) faqat 1 ta filialni qaytardi → Administrator `branch.create`da 403 oldi (permission yo'q) → Super Admin filial va user yaratdi → `audit_logs`da ikkala action ham qayd etildi.

## Sprint 4 — Courses / Groups / Schedule + Conflict Detector (bajarildi)

- `GET/POST /api/courses`, `GET/POST /api/groups`, `POST /api/groups/{id}/staff` (MAIN_TEACHER/SUPPORT_TEACHER biriktirish, bitta guruhda bitta main teacher qoidasi domain darajasida — `Group.AssignStaff`);
- `GET /api/schedule`, `GET /api/schedule/conflicts` (preview), `POST /api/schedule/lessons` — **Conflict Detector** (`../docs/00_TZ.md` §18): bir xil filialda room, teacher yoki group band bo'lsa overlap tekshiradi, 409 + conflict ro'yxatini qaytaradi;
- **Tuzatilgan ikkita real bug** (live testda topildi):
  1. Npgsql `timestamp with time zone` faqat UTC qabul qiladi — `AppDbContext`ga barcha `DateTimeOffset` ustunlar uchun global UTC-normalizatsiya value converter qo'shildi (`AppDbContext.OnModelCreating`), shunda `+05:00` kabi mahalliy offsetlar bilan so'rov yuborish ishlaydi;
  2. EF Core `OrderBy` chaqiruvi `Select` (DTO proyeksiyasi)dan **oldin** bo'lishi kerak — aks holda LINQ-to-SQL translate bo'lmaydi (`ScheduleController.List`).

**Uçtan-uçga tekshirildi:** bir xil room+teacher+overlapping vaqt — 409 (ROOM+TEACHER+GROUP conflict); mustaqil guruh/room/teacher bilan bir xil vaqt — 201; schedule ro'yxati va conflict preview endpointlari to'g'ri javob qaytardi.

## Sprint 5 — Frontend integratsiyasi + Hardening (bajarildi)

**Frontend:**
- CORS: `localhost`/`127.0.0.1` ustidagi istalgan portga ruxsat (Development uchun qulay — bu mashinada boshqa loyiha 3000-portni band qilib qo'ygani uchun Next.js avtomatik 3002-portga o'tdi);
- `frontend/src/lib/api-client.ts` — fetch wrapper: bearer token qo'shadi, 401 kelsa bitta marta refresh qilib qayta so'raydi, `ApiError.body` orqali 409 conflict payload'ini ham uzatadi;
- `frontend/src/lib/api/*.ts` — auth, branches, users, academic (courses/groups/schedule), audit-logs uchun tiplangan API funksiyalari;
- Barcha admin sahifalar (`login`, `branches`, `users`, `groups`, `schedule`, `audit-logs`) haqiqiy API bilan ishlaydi — ro'yxat + yaratish formalari, schedule sahifasida conflict xabarlari ko'rsatiladi;
- `(admin)/layout.tsx` — route guard: token yo'q bo'lsa `/login`ga redirect, chap tomonda navigatsiya + "Chiqish" tugmasi.

**Backend hardening:**
- Rate limiting (`Microsoft.AspNetCore.RateLimiting`): global 300/min/IP, `/api/auth/login` uchun qattiqroq 10/min/IP (brute-force himoyasi, TZ §49) — live testda 11-so'rov `429`qaytardi;
- Xavfsizlik headerlari: `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`;
- `GET /api/audit-logs` endpointi qo'shildi (`audit.log.view` permission bilan);
- **Integration testlar** — yangi `EduCenter.Api.IntegrationTests` loyihasi, `WebApplicationFactory<Program>` + haqiqiy local Postgres orqali: login (muvaffaqiyatli/xato parol), refresh token rotation, scope isolation (`GetBranches_AsSuperAdmin_SeesAllOrganizationBranches` vs `..._AsAdministrator_SeesOnlyAssignedBranch`), permission denial (403). Testlar bir xil demo tashkilotni parallel seed qilib race condition berayotgani aniqlandi va `ICollectionFixture` bilan tuzatildi.

**Tekshirildi:** `dotnet build` (0 xato), `dotnet test` — **9/9 test o'tdi** (2 unit + 7 integration), `npm run build` xatosiz. CORS preflight, security headerlar, rate limiting va audit-logs endpointi live HTTP so'rovlar bilan tasdiqlandi.

---

# Phase 1 — Foundation: YAKUNLANDI

Barcha 5 sprint bajarildi (`../docs/03_phase1_technical_plan.md` §6, Definition of Done). Backend to'liq ishlaydigan Auth + RBAC/ABAC/Scope + CRUD + Conflict Detector + Audit + hardening tizimi; frontend admin panel skeleti haqiqiy API bilan bog'langan.

---

# Phase 2 — CRM + LMS

Reja: `../docs/04_phase2_technical_plan.md`.

## Sprint 1 — CRM Core: Lead lifecycle + Operator Call Queue (bajarildi)

- Yangi modul: `EduCenter.Modules.Crm` — `Lead`, `CrmCall`, `CrmTask` (docs §10, §3.3 asosida);
- **Muhim arxitektura qarori**: Lead va Student alohida entitylar — ENROLLED bo'lgunga qadar shaxsning login akkaunti yo'q, faqat CRM yozuvi (`../docs/04_phase2_technical_plan.md` §1);
- `GET/POST /api/leads`, `PATCH /api/leads/{id}/status`, `POST /api/leads/{id}/calls`;
- `GET /api/crm/tasks` (Operator's Today's Tasks — `crm.task.view` permission scope'ga qarab: SuperAdmin=hammasi, Administrator=filial bo'yicha, Operator=faqat o'ziniki), `POST /api/crm/tasks/{id}/complete`;
- **`CallOutcome.CallLater` → avtomatik `CrmTask` yaratadi** (ertangi kunga, TZ §3.3 qoidasi) — bu Phase 3'dagi to'liq Automation Engine'ning birinchi haqiqiy namunasi;
- `role_permission_matrix.md` §3.6 CRM qatorlari `PermissionSeedData`ga qo'shildi.

**Tuzatilgan real bug:** ASP.NET Core'ning default System.Text.Json enum'larni raqam sifatida serialize/deserialize qiladi (`"outcome": 2` emas, `"CallLater"` yuborilganda 400 xato). Bu **butun API bo'ylab** (User.Status, Group.Status, GroupStaffRole va h.k.) yashirin muammo edi — hech kim hali enum maydonini string bilan test qilmagan edi. `AddJsonOptions` bilan `JsonStringEnumConverter` global ravishda ulandi; frontend tiplari (`AppUser.status`, `Group.status`, `GroupStaff.role`) mos ravishda yangilandi.

**Uçtan-uçga tekshirildi:** Operator lead yaratdi → `CallLater` bilan qo'ng'iroq qildi → task avtomatik ochildi va queue'da ko'rindi → task yopildi → Administrator (MyBranches) ham shu taskni ko'ra oldi → Student roli `leads.view`da 403 oldi. Integration testlar: **11/11 o'tdi** (2 unit + 9 integration, jumladan 2 ta yangi CRM testi).

## Sprint 2 — Trial Lesson + Lead→Student konvertatsiyasi (bajarildi)

- Yangi modul: `EduCenter.Modules.Students` — `Student`, `Parent`, `StudentParent`, `Enrollment`;
- `TrialLesson` (CRM modulida) + `POST /api/leads/{id}/trial`, `PATCH /api/trial-lessons/{id}/attendance` (attended/missed → Lead status ham yangilanadi);
- **`POST /api/leads/{id}/convert`** — Lead'ni Student + User (STUDENT roli) + Enrollment'ga aylantiradi, bitta `SaveChangesAsync` chaqiruvida (barcha ID'lar client-side `Guid.NewGuid()` bilan generatsiya qilingani uchun oraliq save shart emas — haqiqiy tranzaksion atomiklik);
- Lead o'chirilmaydi — `MarkConverted(studentId)` bilan belgilanadi va `ConvertedStudentId` orqali yangi Student'ga bog'lanadi (TZ §20 "tarix o'chirilmaydi" qoidasi);
- `GET /api/students`, `GET /api/students/{id}` — minimal ko'rish (to'liq Student 360° Sprint 4'da);
- `role_permission_matrix.md`dagi `trial.book`, `students.view`, `students.create` qatorlari seed qilindi.

**Tuzatilgan real bug (testlar o'z-o'zini urdi):** integration test suite bir necha o'nlab marta login qiladi bitta IP'dan — Sprint 1'da qo'shilgan login rate limit (10/min) o'zining ustidan ishlab, `dotnet test` tasodifiy 429 bilan yiqila boshladi. Sabab: minimal API'ning top-level `Program.cs`'i `builder.Configuration`ni `WebApplicationFactory`ning `ConfigureAppConfiguration` hook'i ishlashidan **oldin** o'qiydi, shuning uchun in-memory override yetib bormasdi. Yechim: `EduCenterWebApplicationFactory` konstruktorida **process environment variable** (`RateLimiting__LoginPermitPerMinute`) o'rnatiladi — bu `WebApplication.CreateBuilder()` ishga tushishidan oldin allaqachon mavjud bo'ladi va sinxron o'qiladi. Rate limit qiymatlari endi `appsettings.json`dan sozlanadi (`RateLimiting:GlobalPermitPerMinute` / `LoginPermitPerMinute`), production standart qiymatlari (300/10) o'zgarmagan.

**Uçtan-uçga tekshirildi:** lead yaratildi → trial band qilindi → "attended" deb belgilandi (lead status → TrialAttended) → konvertatsiya qilindi → **yangi student darhol login qila oldi** → lead status "Converted" + `convertedStudentId` to'g'ri → qayta konvertatsiya urinishi 409 bilan bloklandi → students ro'yxatida ko'rindi. **12/12 test o'tdi** (2 unit + 10 integration).

## Sprint 3 — Attendance + Homework (bajarildi)

- Yangi modul: `EduCenter.Modules.Lms` — `Attendance`, `Homework`, `HomeworkSubmission`;
- `POST/GET /api/lessons/{id}/attendance` — bulk belgilash (bitta so'rovda butun ro'yxat), upsert (qayta belgilash yozuvni yangilaydi, dublikat yaratmaydi);
- `GET/POST /api/homework`, `POST /api/homework/{id}/submissions`, `GET /api/homework/{id}/submissions`, `PATCH /api/homework/submissions/{id}/review`;
- `EduCenter.Api/Authorization/GroupAccess.cs` — Attendance va Homework kontrollerlari ulashadigan umumiy scope-tekshiruv (`CanAccessGroupAsync`: AllOrg/MyBranches/MyGroups orqali MAIN_TEACHER/AssignedGroups orqali SUPPORT_TEACHER);
- `homework.view` permission qo'shildi (Matrix'da yo'q edi — Student/Parent homework ro'yxatini ko'rishi uchun zarur bo'lgani aniqlandi, `attendance.view` bilan bir xil scope naqshida).

**Ushbu sprintda ikkita jiddiy, Phase 1'dan qolib kelayotgan real bug topildi va tuzatildi** (ikkalasi ham avval hech qachon `curl` bilan sinalmagan endpointlarda yashiringan edi):

1. **`POST /api/groups/{id}/staff` — `DbUpdateConcurrencyException`.** Sabab: barcha entitylarimiz `Id`ni konstruktorda `Guid.NewGuid()` bilan o'zi belgilaydi, lekin EF Core'ning standart konvensiyasi Guid PK'larni "baza generatsiya qiladi" deb hisoblaydi. Natijada, allaqachon trek qilingan ota-entity (`Group`)ning collection-navigatsiyasiga (`Group.Staff`) to'g'ridan-to'g'ri `DbSet.Add()` chaqirmasdan yangi bola qo'shilganda, EF buni "bazada allaqachon mavjud" deb noto'g'ri xulosa chiqarib, INSERT o'rniga UPDATE yuborgan — va albatta 0 qator yangilangan. **Tuzatish:** `AppDbContext`da barcha `Id` ustunlari uchun global `ValueGenerated = Never` o'rnatildi. Bu Phase 1'dagi `User.AssignRole`/`AssignBranch` endpointlarida ham yashirin mavjud bo'lgan (ular xuddi shu naqshni ishlatadi) — endi ikkalasi ham tekshirildi va ishlaydi.
2. **`GET /api/homework` (Self scope) — LINQ tarjima xatosi.** `Homework`+`Lesson` join natijasini `ValueTuple`ga proyeksiya qilib, keyin `.Item2.GroupId` orqali filtrlash EF Core tomonidan SQL'ga o'girilmadi. **Tuzatish:** proyeksiyani `ValueTuple` o'rniga tekis anonim tipga o'zgartirish yetarli bo'ldi.

**Uçtan-uçga to'liq zanjir tekshirildi:** guruh yaratildi → teacher MAIN_TEACHER sifatida biriktirildi (bug #1 shu yerda topilgan) → dars yaratildi → lead → trial → student konvertatsiya qilindi va guruhga enroll bo'ldi → teacher attendance belgiladi → homework yaratdi → **student homework ro'yxatini ko'rdi (bug #2 shu yerda topilgan)** → topshirdi → qayta topshirish 409 bilan bloklandi → teacher ko'rib chiqib baholadi (85 ball) → student yangilangan bahoni ko'rdi. **13/13 test o'tdi** (2 unit + 11 integration, jumladan to'liq LMS lifecycle testi).

## Sprint 4 — Exams + Student/Teacher 360° + Timeline (bajarildi)

- Yangi modullar: `EduCenter.Modules.Lms`ga `Exam`/`ExamResult` qo'shildi; yangi `EduCenter.Modules.Timeline` — `StudentTimelineEvent`;
- `GET/POST /api/exams`, `GET/POST /api/exams/{id}/results` — natijalar upsert emas (bitta studentga bitta exam uchun bitta natija, qayta yozish 409);
- **Timeline avtomatik yoziladi** — to'liq domain-event dispatcher o'rniga (Sprint 4 ko'lamida ortiqcha bo'lardi) har bir hodisa sodir bo'lgan joyda to'g'ridan-to'g'ri yoziladi: `ENROLLED` (Lead konvertatsiyasida), `LESSON_ATTENDED`/`LESSON_ABSENT` (attendance belgilanganda), `HOMEWORK_SUBMITTED`/`HOMEWORK_REVIEWED`, `EXAM_RESULT_RECORDED`;
- `GET /api/students/{id}/overview` — to'liq Student 360°: enrollmentlar, attendance foizi, homework topshirish foizi;
- `GET /api/students/{id}/timeline` — xronologik tarix;
- `GET /api/teachers/{id}` — Teacher 360°: guruhlar (MAIN/SUPPORT roli bilan) + har bir guruhdagi student soni;
- `EduCenter.Api/Authorization/StudentAccess.cs` — Student 360° uchun scope-tekshiruv (`CanAccessStudentAsync`: AllOrg/MyBranches — Enrollment→Group.BranchId orqali; MyGroups/AssignedGroups — Enrollment→GroupStaff orqali; Self — Student.UserId; MyChildren — StudentParent→Parent.UserId orqali).

**Yana bir real bug topildi va tuzatildi:** `TeachersController`da Teacher o'z profilini ko'rayotganda scope tekshiruvi faqat `ScopeType.Self`ni kutgan edi, lekin Teacher rolining `users.view` grantida haqiqiy scope — `MyGroups` (chunki teacher odatda boshqa userlarni faqat o'z guruhlari orqali ko'radi). Natijada teacher o'zining guruhlar ro'yxatini bo'sh holda ko'rardi. **Tuzatish:** "o'z profilini ko'rish" tekshiruvi scope turidan qat'i nazar `scope.UserId == id` orqali ajratildi.

**Uçtan-uçga tekshirildi:** exam yaratildi → natija kiritildi (Timeline'da `EXAM_RESULT_RECORDED` paydo bo'ldi) → Student 360° overview'da enrollment va statistika to'g'ri chiqdi → student o'z natijasini (Self scope) va o'z timeline'ini ko'ra oldi → boshqa studentning ma'lumotiga urinish 403 bilan bloklandi → Teacher 360° (bug tuzatilgandan keyin) o'z guruhi va student sonini to'g'ri ko'rsatdi. **15/15 test o'tdi** (2 unit + 13 integration).

---

# Phase 2 (CRM + LMS) — backend yakunlandi

To'rtta sprint bajarildi: CRM Core (Lead lifecycle, Call Queue) → Trial+Enrollment (Lead→Student konvertatsiyasi) → Attendance+Homework → Exams+Student/Teacher 360°+Timeline. Backend'da 8 ta modul (`Identity, Organizations, Academic, Audit, Crm, Students, Lms, Timeline`), 15 test, va shu jarayonda topilgan **5 ta real bug** (2 tasi Phase 1'dan meros, 3 tasi Sprint 2-4'da) tuzatildi.

## Sprint 5 — Frontend (bajarildi)

Butunlay qayta qurilgan dizayn tizimi (teal/ochra palitra, Sora+IBM Plex tipografiya, `src/components/ui/*` komponent kutubxonasi, Ctrl+K qidiruv) va yangi sahifalar: `/crm/tasks` (Call Queue), `/leads` (lid boshqaruvi + konvertatsiya), `/students` + `/students/{id}` (Student 360°, Umumiy/Tarix tablar), `/schedule/{lessonId}` (**Attendance + Homework kiritish UI — Teacher uchun**, tablar bilan: davomat ro'yxati bulk saqlash, homework yaratish, topshiriqlarni ko'rish/baholash). Batafsil: `../frontend/README.md`.

Bu UI'ni qo'llab-quvvatlash uchun ikkita yangi backend endpoint qo'shildi:
- `GET /api/groups/{id}/students` — guruh ro'yxati (roster), attendance/homework checklist uchun;
- `GET /api/homework?lessonId=` — mavjud `groupId` filtriga qo'shimcha, bitta darsga tegishli homework'larni olish uchun.

## Audit — Phase 1 + Phase 2 to'liq qayta tekshiruv (bajarildi)

Butun kod bazasi (har bir controller, scope resolver, seed, frontend sahifalar) qo'lda qayta ko'rib chiqildi. Topilgan va tuzatilgan muammolar:

**Xavfsizlik / scope (backend)**
- **Privilege escalation:** filial Administratori `POST /api/users` orqali `SUPER_ADMIN` yarata olardi → endi faqat filial darajasidagi rollarni (Operator/Teacher/SupportTeacher/Student/Parent) va faqat o'z filiallariga biriktira oladi; boshqa filial foydalanuvchisini tahrirlash/rol berish → 403 (`UsersController.EnsureRoleAssignable / CanTouchUser`);
- **Cross-branch yozuv:** lid statusi/qo'ng'iroq/trial/konvertatsiya boshqa filial lidlari uchun ham ishlardi → `LoadLeadInScopeAsync` + `EnsureGroupInScopeAsync`; trial lesson ham filial tekshiruvi bilan;
- **Ma'lumot oqishi:** Student `GET /api/exams` orqali tashkilotdagi barcha imtihonlarni ko'rardi → `ScopedGroupsAsync` bilan join; `StudentsController.List` filial scope'ni e'tiborsiz qoldirardi (MyBranches → hamma student) → enrollment→group.BranchId orqali filtr; MyGroups/Self/MyChildren uchun ham ishlaydi;
- **Enrollment tekshiruvi yo'q edi:** attendance, homework submission, exam result guruhga yozilmagan studentni ham qabul qilardi → har birida `Enrollments.Any(Active)` sharti;
- **PasswordHash audit logga yozilardi** → `AuditSaveChangesInterceptor.RedactedProperties`;
- **Teacher/Student 403 olardi** `/api/schedule` va `/api/groups` ro'yxatida (`AllowedBranchIds == null` "taqiq" deb talqin qilinardi) → `GroupAccess.VisibleGroupIdsAsync / ScopedGroupsAsync` — staffing (Teacher/Support) yoki enrollment (Student/Parent) orqali ko'rinadigan guruhlar;
- `crm.call.log` matritsada faqat Operator (Self) edi — Super Admin/Administrator qo'ng'iroq yoza olmasdi → SA=ALL_ORG, AD/OP=MY_BRANCHES (`docs/01_role_permission_matrix.md` yangilandi; seed mavjud bazada scope'ni avtomatik yangilaydi).

**Validatsiya / to'g'rilik**
- Bir o'qituvchini guruhga ikki marta biriktirish unique-index orqali **500** berardi → `Group.AssignStaff` domain tekshiruvi → **409**;
- Dars yaratishda guruh/filial mosligi, `end > start`, xona, o'qituvchi mavjudligi tekshirilmasdi; teacher/group to'qnashuvi filial bilan chegaralangan edi (o'qituvchi ikki filialda bir vaqtda bo'lolmaydi) → `ScheduleController.ValidateCandidateAsync`;
- Attendance: takroriy studentId'lar, o'zgarmagan holatlar uchun ham timeline event yozilardi → dedupe + faqat o'zgarganlar; homework review: ball `0..MaxScore` oralig'ida; exam: ballar `0..9`; lead konvertatsiya: email/parol validatsiyasi, `Converted` guardlari, `StudentNumber` unikalligi (`NextStudentNumberAsync`);
- `GroupsController.AssignStaff` faqat Teacher/SupportTeacher rolidagi tashkilot foydalanuvchisini qabul qiladi; `Create` nom/sig'im/filial/kurs tekshiradi.

**Seed / demo**
- Demo foydalanuvchilarning hech biri (Administrator/Operator'dan tashqari) filialga biriktirilmagan edi → filial Administratori o'qituvchini umuman ko'ra olmasdi. Endi hamma demo foydalanuvchi filialga biriktirilgan;
- Demo kontent (3 kurs, 3 guruh, har bosqichdagi lidlar, 4 student + ota-ona, 7 dars, davomat, homework + baholangan/kutilayotgan topshiriq, Mock IELTS natijalari, timeline, Call Queue vazifalari) endi `DbSeeder`ning o'zida — baza qayta tiklansa ham (`DROP SCHEMA` + `dotnet ef database update` + `dotnet run`) demo tayyor.

**Testlar**: 15 → **20** (`AuditRegressionTests`: Administrator SUPER_ADMIN yarata olmaydi; boshqa filial foydalanuvchisini tahrirlay olmaydi; Teacher schedule/groups ro'yxatini oladi; Student boshqa guruh homework'iga topshira olmaydi; ikki marta staff biriktirish → 409). `LmsTests`/`Sprint4Tests` endi `[0]` o'rniga email bo'yicha o'qituvchini tanlaydi va darsni tasodifiy uzoq slotda yaratadi — doimiy dev bazasida qayta yurganda Conflict Detector'ga urilmaydi.

**Yangi endpoint**: `GET /api/homework/{id}/my-submissions` — Student/Parent o'z topshirig'i holatini ko'rishi uchun (`homework.view`, faqat personal scope).

## Phase 2 yopilishi — Support Needed Engine + follow-up avtomatikasi (bajarildi)

- `SupportCase` (TZ §8): signal → tavsiya → Administrator yordamchi o'qituvchini biriktiradi yoki rad etadi → Support Teacher yakunlaydi. Signallar: `CONSECUTIVE_ABSENCES` (davomat saqlanganda darhol), `HOMEWORK_MISSED` (sweep), `STAFF_PROPOSAL` (qo'lda). `/api/support-cases`, yangi `support.propose` permission.
- CRM follow-up (TZ §10): trial 24 soat → `FollowUp` vazifa + `Lead.Priority=High`; ketma-ket 2 yo'qlik → `Absence` vazifa. Har qoida trigger vaqti bo'yicha idempotent.
- `Automation/FollowUpRules.cs` + `AutomationSweepService` (fon, 300 s; `Automation__Enabled=false` bilan o'chadi — testlar shunday qiladi va qoidalarni DI orqali to'g'ridan-to'g'ri chaqiradi).
- Bug: Administrator Call Queue'da studentga bog'langan vazifalarni ko'rmasdi — tuzatildi; vazifa DTO'sida endi `SubjectName/Phone`, `ReasonCode`, `Note`.
- Student timeline lid bosqichini ham ko'rsatadi (`LEAD_CREATED`, `CRM_CALL`, `TRIAL_LESSON`).
- Migratsiya `AddSupportCasesAndFollowUps`. Testlar: **24/24**.
- DoD holati va batafsil: `../docs/04_phase2_technical_plan.md` §6.

# Phase 3 — Finance + Automation + Communication

Reja: `../docs/05_phase3_technical_plan.md`.

## Sprint 1 — Finance core (bajarildi)

- Yangi modul `EduCenter.Modules.Finance` (`Invoice`, `Payment`), `Course.MonthlyFee`, migratsiya `AddFinanceCore`.
- `Automation/BillingRules.cs`: oylik hisob-faktura (enrollment sanasidan; o'tmish davrlari hisoblanmaydi), `Active ⇄ Debt`, 3 kun kechikkan qarz → operator vazifasi. Fon sweep + konvertatsiya/to'lov paytida darhol.
- `Finance/FinanceController.cs`: hisob-fakturalar, qarzdorlar, to'lov qabul qilish, void (sabab bilan), hisob-fakturani bekor qilish, to'lovlar tarixi, `/api/students/{id}/finance`, `billing/run`.
- Permission: `payments.view`, `finance.debt.view` (Operator uchun faqat qarz tomoni), `payments.create`, `payments.edit`.
- Sozlamalar: `Finance:Currency` (UZS). Qarz follow-up kuni Sprint 5'dan beri Automation Engine qoidasi (`DEBT_FOLLOW_UP`).

## Sprint 2 — Chegirma, promo-kod, refund, oldindan to'lov, hisobot (bajarildi)

- `Discount` (foiz/summa, bitta faol), `PromoCode` (`promo.manage`), `Refund` (majburiyatni ham kamaytiradi — qarz yaratmaydi), oldindan hisob-faktura (12 oygacha). Migratsiya `AddDiscountsRefundsPromo`.
- Chegaralar scope bo'yicha: filial darajasida `Finance:AdministratorMaxDiscountPercent` (20) va `Finance:AdministratorMaxRefundAmount` (1 000 000).
- `Finance/FinanceQueries.cs` (umumiy scoping/read-model), `FinanceAdjustmentsController`, `FinanceReportsController` (`/api/finance/reports/summary`).
- Batafsil qarorlar: `../docs/05_phase3_technical_plan.md` §5 Sprint 2.

## Sprint 3 — Notification Engine (bajarildi)

- Modul `EduCenter.Modules.Notifications` (`Notification`, `NotificationPreference`), migratsiya `AddNotifications`.
- `Notifications/NotificationDispatcher.cs` (o'chirilgan tur, dedupe, tashkilot chegarasi; sababchi o'zgarish bilan bitta tranzaksiyada), `NotificationRecipients.cs`, `NotificationCatalog.cs`, `NotificationsController.cs` (`/api/notifications`).
- Hodisa manbalari: davomat, imtihon, homework (yaratish/baholash), jadval, guruh o'qituvchisi, support, avtomatik vazifalar; vaqtga bog'liq eslatmalar — `Automation/NotificationRules.cs` (to'lov muddati/kechikishi, homework muddati, dars eslatmasi).
- Hodisalar jadvali va qarorlar: `../docs/05_phase3_technical_plan.md` §5 Sprint 3.

## Sprint 5 — Automation Engine (bajarildi)

- Modul `EduCenter.Modules.Automation` (`AutomationRule`, `AutomationRunLog`), migratsiya `AddAutomationRules`.
- `Automation/AutomationCatalog.cs` (7 qoida: parametr oraliqlari, standartlar, amallar), `AutomationSettings.cs` (tashkilot sozlamasi, kesh) va jurnal, `AutomationRunner.cs` (billing → follow-up → eslatmalar) va Hangfire job, `AutomationController.cs` (`/api/automation`).
- `BillingRules`, `FollowUpRules`, `NotificationRules` qattiq yozilgan chegaralar o'rniga tashkilot qoidalarini o'qiydi va natijani jurnalga yozadi.
- Fon ishlari: **Hangfire** (`Hangfire.AspNetCore`, `Hangfire.PostgreSql`, sxema `hangfire`), recurring job `automation-sweep`; dashboard `/hangfire` faqat Development'da. `AutomationSweepService` olib tashlandi. `Automation__Enabled=false` — Hangfire ro'yxatdan o'tmaydi (testlar).
- Permission: `automation.view` (SA, AD), `automation.manage` (SA).
- Qoidalar jadvali: `../docs/05_phase3_technical_plan.md` §5 Sprint 5.

## Sprint 6 — SignalR Chat (bajarildi)

- Modul `EduCenter.Modules.Chat` (`ChatRoom`, `ChatMember`, `ChatMessage`, `ChatReaction`), migratsiya `AddChat`.
- `Chat/ChatAccess.cs` (kim ko'radi/yozadi/moderatsiya qiladi, kontaktlar, kanal katalogi), `ChatsController.cs` (`/api/chats`), `ChatHub.cs` (`/hubs/chat`), `ChatFileStorage.cs` (`Chat:StoragePath`, 10 MB).
- `Authorization/PermissionResolver.cs` — permission + scope aniqlash handler'dan ajratildi (bir so'rovda bir nechta kod, hub).
- Permission seed: `chat.1to1.use`, `chat.group.use`, `chat.moderate`, `staff.channels.access`.
- Batafsil: `../docs/05_phase3_technical_plan.md` §5 Sprint 6.

## Phase 4 Sprint 1 — AI Gateway (bajarildi)

- Modul `EduCenter.Modules.Ai` (`AiRequest`, `AiUsage` — faqat metadata, prompt/javob matni saqlanmaydi), migratsiya `AddAiGateway`.
- `Ai/AiGateway.cs` — yagona kirish nuqtasi: validatsiya → limit → prompt injection → router (fallback) → JSON/schema tekshiruvi va qayta so'rash → jurnal. `Ai/Providers/` — `OpenAiCompatibleChatProvider` (DeepSeek, local vLLM/Ollama), `FakeChatModelProvider` (dev/test), `AiProviderRegistry` (`Ai:Providers`, `Ai:Routes`). `Ai/Security/AiInputGuard.cs`, `Ai/AiLimits.cs`, `Ai/AiController.cs` (`/api/ai`).
- Provider ulash: `Ai__Providers__deepseek__Enabled=true` + `Ai__Providers__deepseek__ApiKey=...` (kalit repoga yozilmaydi); local server — `Ai__Providers__local__BaseUrl` (masalan `http://host:8000/v1`), `__ChatModel`, `__Enabled=true`. Tekshirish: `POST /api/ai/providers/{name}/check`.
- Development'da hamma vazifa oxirida `fake` provider'ga tushadi (`/api/ai/status` → `demoMode: true`).
- Permission seed: `ai.tutor.use`, `ai.writing.use`, `ai.writing.review`, `ai.writing.override`, `ai.speaking.use`, `ai.knowledge.manage`, `ai.usage.view`, `ai.limits.edit`.
- Batafsil: `../docs/06_phase4_technical_plan.md` §6 Sprint 1.

## Phase 4 Sprint 2 — Writing AI (bajarildi)

- `WritingEvaluation` (Ai moduli), migratsiya `AddWritingEvaluations`.
- `Ai/Writing/WritingRubric.cs` (validatsiya, IELTS prompt, schema, overall band hisoblash), `WritingEvaluator.cs` (navbat, fon ishi, homework hook), `WritingController.cs` (`/api/ai/writing`); `Ai/AiJobQueue.cs` — Hangfire yoki (testlarda) darhol.
- `Writing` turidagi homework topshirilganda AI baholash avtomatik navbatga qo'yiladi (`HomeworkController.Submit`).
- Bildirishnoma va timeline: `WRITING_EVALUATED`, `WRITING_OVERRIDDEN`. Override audit jurnaliga faqat band maydonlari bilan yoziladi.
- Batafsil: `../docs/06_phase4_technical_plan.md` §6 Sprint 2.

## Phase 4 Sprint 3 — RAG + AI Tutor (bajarildi)

- Domen: `KnowledgeDocument`, `KnowledgeChunk` (pgvector `vector(1024)` + generated `tsvector`), `AiConversation`, `AiConversationMessage`; migratsiya `AddKnowledgeBase` (HNSW va GIN indekslari qo'lda SQL bilan).
- `Ai/Knowledge/`: `KnowledgeFileStorage` (`Ai:StoragePath`, 20 MB, txt/md/csv/pdf/docx), `DocumentTextExtractor` (PdfPig, OpenXml), `KnowledgeChunker`, `KnowledgeIngestor` (parsing → injection tekshiruvi → chunk → embedding → saqlash), `KnowledgeRetriever` (scope filtri + vektor va full-text natijalarini RRF bilan birlashtirish), `KnowledgeController` (`/api/ai/knowledge`).
- `Ai/Tutor/`: `TutorService` (mavzu klassifikatori, student profili, manbalar bilan javob), `TutorController` (`/api/ai/tutor`).
- Embedding: `IEmbeddingProvider` — Ollama (`/api/embed`), OpenAI-mos (`/embeddings`) va testlar uchun deterministik fake; marshrut `Ai:Routes:Embedding`.
- Batafsil: `../docs/06_phase4_technical_plan.md` §6 Sprint 3.

## Phase 4 Sprint 4 — Speaking MVP (bajarildi)

- Domen: `SpeakingSession`, `SpeakingTurn`; migratsiya `AddSpeakingSessions`.
- `Ai/Speaking/`: `SpeakingAudioStorage` (`Ai:SpeakingStoragePath`, 15 MB), `SpeakingScript` (examiner promptlari, baholash sxemasi, nutq metrikalari), `SpeakingService` (sessiya oqimi), `SpeakingController` (`/api/ai/speaking`).
- `ISpeechToTextProvider` — `WhisperAsrProvider` va fake; `AiGateway.TranscribeAsync` limit/jurnal/fallback bilan.
- Whisper compose servisi: `docker compose up -d whisper` (GPU, `small` model). Host porti **19000**, API konteyneridan `http://educenter-whisper:9000`.
- Batafsil: `../docs/06_phase4_technical_plan.md` §6 Sprint 4.

## Phase 4 Sprint 5 — AI monitoring va benchmark (bajarildi)

- `Ai/AiUsageController.cs`: `GET /api/ai/usage/summary` (xarajat, tokenlar, kechikish, funksiya/provider/filial/student kesimi, sabablar va oxirgi xatolar) va `GET /api/ai/benchmarks/writing` (o'qituvchi tuzatgan baholashlar bo'yicha bias, o'rtacha xato, ±0.5 ulushi — prompt versiyasi bo'yicha). Ikkalasi ham `ai.usage.view`.
- Hisobotlar gateway jurnalidan o'qiydi, shuning uchun yangi AI funksiyasi qo'shilganda monitoring kodini o'zgartirish shart emas.
- Benchmark modelga qayta so'rov yubormaydi — hisobotni ochish pul sarflamaydi.
- Batafsil: `../docs/06_phase4_technical_plan.md` §6 Sprint 5.

## AI provayderlarni sozlash

Kalitlar va mashinaga xos sozlamalar **`src/EduCenter.Api/appsettings.Local.json`** faylida (git-ignored; namuna — `appsettings.Local.example.json`, `Program.cs` uni ixtiyoriy fayl sifatida o'qiydi). Repoga hech qachon yozilmaydi.

```bash
cp src/EduCenter.Api/appsettings.Local.example.json src/EduCenter.Api/appsettings.Local.json
# keyin faylga DeepSeek kalitini qo'ying
```

Local modellar — compose'dagi `ollama` servisi (GPU Docker Desktop NVIDIA runtime orqali):

```bash
docker compose up -d ollama
docker exec educenter-ollama ollama pull qwen3:8b   # matn
docker exec educenter-ollama ollama pull bge-m3     # embedding (1024 o'lchov)
```

Sozlamada `"Type": "Ollama"` va `"BaseUrl": "http://educenter-ollama:11434"` (hostdan `http://localhost:11434`) — native `/api/chat` ishlatiladi, shuning uchun `/v1` qo'shilmaydi. Sabab: Ollama'ning OpenAI-mos endpointida Qwen3'ning "thinking" rejimini o'chirib bo'lmaydi va javob bo'sh keladi. Embedding esa OpenAI-mos `/v1/embeddings` orqali ishlaydi.
Tekshirish: `POST /api/ai/providers/{name}/check` (`ai.limits.edit`), ro'yxat — `GET /api/ai/providers`.
Integration testlar `deepseek` va `local` provayderlarini environment o'zgaruvchisi bilan majburan o'chiradi — test yurgizish hech qachon pullik so'rov yubormaydi.

## Docker orqali build/test (`scripts/dotnet-in-docker.sh`)

Host'da `dotnet` yangi build qilingan DLL'larni yuklay olmasa (masalan, Windows Smart App Control: *"An Application Control policy has blocked this file"*), backend vositalari .NET SDK konteynerida compose tarmog'ida ishlaydi:

```bash
scripts/dotnet-in-docker.sh test                  # barcha testlar (educenter_test bazasida)
scripts/dotnet-in-docker.sh migration <Nomi>      # EF migratsiya — repoga ko'chiriladi
scripts/dotnet-in-docker.sh update-db             # dev bazaga migratsiyalar
scripts/dotnet-in-docker.sh api                   # API http://localhost:5167 da (konteyner: educenter-api)
scripts/dotnet-in-docker.sh api-stop
```

Manba konteynerga `bin`/`obj`siz nusxalanadi (host build'iga tegmaydi); NuGet paketlari va `dotnet-ef` nomlangan volume'larda keshlanadi.

## Ma'lum cheklovlar

- Rolga xos dashboard (Teacher/Student/Parent uchun "bugungi darslar" kabi alohida bosh sahifa) hali yo'q — hozircha rolga qarab yo'naltirish (`homeForRole`) va rolga mos sidebar/harakatlar bilan cheklangan;
- Frontend'da real brauzerda vizual tekshiruv to'liq qilinmadi (headless browser vositasi bu muhitda yo'q) — `npm run dev` bilan qo'lda tekshirish tavsiya etiladi (backend/frontend URL bog'lanishi `window.location.hostname`dan avtomatik aniqlanadi — Tailscale/LAN orqali ham ishlaydi);
- Seed avtomatik har Development ishga tushishda bajariladi (idempotent, lekin alohida CLI komandaga ajratish toza bo'lardi); integration testlar alohida `educenter_test` bazasida ishlaydi (`EduCenterWebApplicationFactory` har yurgizishda uni o'chirib, migratsiyalarni qayta qo'llaydi) — `dotnet test` dev bazadagi demo ma'lumotga tegmaydi;
- CORS Development uchun istalgan originga ochiq (bearer-token auth, cookie/credentials ishlatilmaydi — xavfsiz) — production uchun aniq origin ro'yxatiga toraytirilishi kerak;
- Timeline eventlar controller'lardan to'g'ridan-to'g'ri yoziladi — domain-event dispatcher keyingi refaktoring.

Batafsil: `../docs/03_phase1_technical_plan.md`, `../docs/04_phase2_technical_plan.md`, `../docs/01_role_permission_matrix.md`, `../docs/02_database_erd.md`.
