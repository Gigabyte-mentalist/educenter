# PHASE 1 — FOUNDATION
## Sprint darajasidagi texnik reja

**Hujjat turi:** Technical Specification (TZ hujjatining davomi)
**Versiya:** 1.0
**Bog'liq hujjatlar:** `00_TZ.md` (§58.Phase1), `01_role_permission_matrix.md`, `02_database_erd.md`

**Fazaning maqsadi:** Auth + RBAC/ABAC/Scope + Organization/Branch + asosiy rollar + Courses/Groups/Schedule ishlaydigan holatga keltirish. Bu fazadan keyin CRM, Finance yoki AI yo'q — faqat xavfsiz, ko'p filialli admin skeleti.

---

## 1. Repo va solution strukturasi

Monorepo, modular monolith backend + alohida frontend.

```
d:\New proj\
├── backend\
│   └── EduCenter.sln
│       ├── src\
│       │   ├── EduCenter.Api\                 (ASP.NET Core Web API — composition root)
│       │   ├── EduCenter.Modules.Identity\    (Auth, User, Role, Permission)
│       │   ├── EduCenter.Modules.Organizations\ (Organization, Branch)
│       │   ├── EduCenter.Modules.Academic\    (Course, Group, Schedule, GroupStaff)
│       │   ├── EduCenter.Modules.Audit\       (AuditLog)
│       │   ├── EduCenter.SharedKernel\        (base entity, result types, tenant context)
│       │   └── EduCenter.Infrastructure\      (EF Core DbContext, Postgres, migrations)
│       └── tests\
│           ├── EduCenter.Modules.Identity.Tests\
│           └── EduCenter.Modules.Academic.Tests\
├── frontend\
│   └── (Next.js app — App Router, TypeScript, Tailwind)
├── docs\
│   (TZ va boshqa spec hujjatlar shu yerga ko'chiriladi — §9 ga qarang)
└── .gitignore
```

**Har bir modul ichida** (Clean Architecture qatlamlari):

```
EduCenter.Modules.Academic\
├── Domain\        (Entities, Value Objects, Domain Events)
├── Application\   (Commands, Queries — CQRS, handlers, DTOs)
└── Infrastructure\ (EF configurations, repository implementations)
```

Modullar orasida to'g'ridan-to'g'ri bog'liqlik yo'q — faqat `SharedKernel` va domain event orqali muloqot (masalan, `StudentEnrolledEvent`ni Academic yaratadi, Notifications keyingi fazada tinglaydi).

---

## 2. Texnologik tanlovlar (Phase 1 doirasida)

| Qatlam | Tanlov | Izoh |
|---|---|---|
| Backend | ASP.NET Core / .NET 10 | **.NET SDK hozircha muhitda o'rnatilmagan — birinchi amaliy qadam shu** |
| ORM | EF Core (Npgsql provider) | Migrations code-first |
| DB | PostgreSQL 16+ | Local: Docker container |
| Auth | ASP.NET Core Identity + JWT (access+refresh) | MFA — Phase 1'da faqat struktura, to'liq emas |
| Authorization | Custom `IAuthorizationHandler` + `ScopeRequirement` | Permission Matrix hujjatiga mos |
| Frontend | Next.js 15 (App Router) + TypeScript + Tailwind | `create-next-app` bilan boshlanadi |
| State/Query | TanStack Query | API bilan ishlash uchun |
| Testing | xUnit + FluentAssertions (backend), Playwright (keyinroq) | |

---

## 3. Ma'lumotlar modeli (Phase 1 subset)

`02_database_erd.md` §1 va §3 dan quyidagi entitylar shu fazada real jadvalga aylanadi:

`Organization, Branch, User, Role, Permission, RolePermission, UserPermission, AuditLog, Student (basic profil), Parent (basic profil), TeacherProfile, StaffAssignment, Course, Group, GroupStaff, Lesson (schedule, attendance/homework hali yo'q)`

`Enrollment` bu fazada faqat "student guruhga biriktirilgan" darajasida — CRM lifecycle statuslari (LEAD, TRIAL...) Phase 2'da qo'shiladi.

---

## 4. API yuzasi (Phase 1)

```
POST   /api/auth/login
POST   /api/auth/refresh
POST   /api/auth/logout

GET    /api/organizations/{id}
POST   /api/organizations
GET    /api/branches
POST   /api/branches
PATCH  /api/branches/{id}

GET    /api/users?role=&branchId=
POST   /api/users
PATCH  /api/users/{id}
POST   /api/users/{id}/roles

GET    /api/courses
POST   /api/courses
GET    /api/groups?branchId=
POST   /api/groups
POST   /api/groups/{id}/staff        (GroupStaff assign — MAIN_TEACHER|SUPPORT_TEACHER)

GET    /api/schedule?branchId=&from=&to=
POST   /api/schedule/lessons
GET    /api/schedule/conflicts       (conflict detector preview)

GET    /api/audit-logs?resource=&resourceId=
```

Har bir endpoint `[Authorize(Policy = "students.view")]` uslubidagi policy bilan himoyalanadi; policy handler ichida scope tekshiriladi (`ALL_ORGANIZATION` / `MY_BRANCHES` / ...).

---

## 5. Sprint bo'linishi (2 haftalik sprintlar, taxminiy)

### Sprint 1 — Infra + Auth skeleton
- Docker Compose: PostgreSQL + pgAdmin (local dev)
- Solution/loyihalar yaratish (`dotnet new` orqali, SDK o'rnatilgach)
- `SharedKernel`: BaseEntity, TenantContext, Result<T>
- ASP.NET Core Identity integratsiyasi, JWT access+refresh token
- CI skeleton (build + test, lint)

### Sprint 2 — RBAC/ABAC/Scope
- `Role`, `Permission`, `RolePermission`, `UserPermission` entitylari + migration
- `ScopeRequirement` + `IAuthorizationHandler` implementatsiyasi
- Permission seed data — `01_role_permission_matrix.md` dan generatsiya qilinadi
- Unit testlar: har bir scope turi uchun access/deny case'lar

### Sprint 3 — Organization / Branch / Users
- Organization, Branch CRUD
- User CRUD + role assignment endpoint
- Multi-branch: bitta user bir nechta filialga biriktirilishi (`USER_BRANCH`)
- Audit Log yozish middleware/interceptor (EF Core `SaveChanges` interceptor)

### Sprint 4 — Courses / Groups / Schedule
- Course, Group, GroupStaff CRUD
- Lesson (schedule) CRUD
- **Conflict Detector**: room band, teacher band, group band tekshiruvi (TZ §18)
- Frontend: Next.js skeleton — login sahifasi, Branch/Group/Schedule admin ekranlari (asosiy CRUD UI, dizayn sistemasiz — funksional daraja)

### Sprint 5 — Hardening & Definition of Done
- Integration testlar (WebApplicationFactory)
- Rate limiting, secure headers (TZ §49 asoslari)
- Seed script: demo Organization + 2 Branch + har bir roldan foydalanuvchi
- README: local ishga tushirish qo'llanmasi

---

## 6. Definition of Done (Phase 1 yakuni)

- [x] Super Admin login qilib, yangi filial yarata oladi;
- [x] Administrator faqat o'z filialidagi ma'lumotlarni ko'radi (scope isolation — integration testda tasdiqlangan, `ScopeAuthorizationTests`);
- [x] Operator/Teacher/Student/Parent asosiy login + o'ziga tegishli dashboard skeleti ko'radi — *login barcha rollar uchun ishlaydi; rolga xos dashboard kontenti (masalan Teacher uchun "bugungi darslar") Phase 2 doirasida to'ldiriladi, bu yerda faqat admin panel skeleti (branches/users/groups/schedule/audit) qurilgan*;
- [x] Guruh yaratilganda MAIN_TEACHER va bir nechta SUPPORT_TEACHER biriktirish mumkin (`Group.AssignStaff`, bitta guruhda bitta main teacher qoidasi domain darajasida majburlangan);
- [x] Schedule'da room/teacher/group conflict avtomatik aniqlanadi va xato qaytaradi (Conflict Detector, live va integration testda tasdiqlangan);
- [x] Har bir muhim action (user yaratish, rol o'zgartirish, filial tahrirlash) Audit Log'da ko'rinadi (`AuditSaveChangesInterceptor`, avtomatik);
- [x] `01_role_permission_matrix.md`dagi Phase 1 doirasidagi permission'lar (Organization, Users, Groups, Schedule, Audit bo'limlari) kodda ishlaydi va seed qilingan.

**Phase 1 — Foundation yakunlandi.** Backend: Auth (JWT + refresh rotation), RBAC/ABAC/Scope, Organization/Branch/Users/Courses/Groups/Schedule CRUD, Conflict Detector, Audit Log, rate limiting, xavfsizlik headerlari, 9 ta test (2 unit + 7 integration, barchasi o'tgan). Frontend: login + branches to'liq API'ga ulangan, boshqa admin sahifalar (users/groups/schedule/audit-logs) real ma'lumot bilan ishlaydi, route guard bilan himoyalangan.

---

## 7. Bloker va oldindan hal qilinishi kerak bo'lgan narsalar

1. **.NET SDK o'rnatilmagan** — bu muhitda `dotnet` komandasi mavjud emas. Backend scaffolding boshlanishidan oldin .NET 10 SDK o'rnatilishi kerak.
2. **PostgreSQL** — local dev uchun Docker kerak (yoki mahalliy o'rnatilgan Postgres instance).
3. Node.js va npm mavjud (v24.19.0) — frontend scaffolding hoziroq boshlanishi mumkin.

---

## 8. Frontend skeleton (Phase 1 doirasida qilinadigan)

Next.js loyihasi quyidagi asosiy yo'nalishlar bilan:

```
frontend/src/app/
├── (auth)/login/
├── (admin)/branches/
├── (admin)/users/
├── (admin)/groups/
├── (admin)/schedule/
└── (admin)/audit-logs/
```

To'liq Design System (TZ §27) keyingi bosqichda alohida `Design System Specification` hujjati asosida quriladi — Phase 1'da faqat Tailwind bilan funksional, minimal UI.

---

## 9. Hujjatlar tashkiloti

Barcha spec hujjatlar `docs/` papkasida:

```
docs/
├── 00_TZ.md
├── 01_role_permission_matrix.md
├── 02_database_erd.md
├── 03_phase1_technical_plan.md
```
