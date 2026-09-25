# TEXNIK AMALGA OSHIRISH REJASI

**Sana:** 2026-09-21
**Manba:** `docs/10_full_audit_2026-09-21.md` (8 kritik + 17 muhim + 15 texnik qarz bandi)
**Qamrov:** auditdagi **hamma narsa**, ikkitasidan tashqari:

| Chiqarilgan | Sabab |
|---|---|
| ❌ Faza 5 — Flutter ilova, Push notification, mobil xavfsizlik (TZ §51, §58) | Foydalanuvchi so'radi; `docs/07_mobile_design_brief.md` tayyor, alohida reja bo'ladi |
| ❌ Telegram bot (TZ §23) | Bot token yo'q |

Faza 6 bandlari (KPI, NPS, gamification, sertifikat, HR, xarajatlar, multi-tenant, advanced
speaking) **rejaga kiritilgan** — ular mobil ham, Telegram ham emas. Ular 10-bosqichda.

---

## 0. ISH TARTIBI (har bir bosqichda bir xil)

### Qoidalar
1. **Ruxsat** — yangi endpoint = `[Authorize(Policy = "kod")]` + `HttpContext.RequireResolvedScope()`.
   Rol nomi bo'yicha tekshirish **taqiqlanadi**. Yangi kod → `PermissionSeedData.Grants` ga qator
   (migration kerak emas, `DbSeeder` idempotent — faqat API restart).
2. **Scope** — student uchun `scope.CanAccessStudentAsync(db, id)`, guruh uchun
   `scope.CanAccessGroupAsync(...)` (`Authorization/StudentAccess.cs`, `GroupAccess.cs`).
3. **Ro'yxat endpointi = sahifalangan** — `query.ToPageAsync(paging, ct)`, qidiruv
   `PagingExtensions.ContainsPattern`. Enum massivi uchun `WhereIn(...)`.
4. **Hodisa yozuvi** — biznes o'zgarishi bo'lsa: `StudentTimelineEvent` (yangi turni
   `TimelineEventType` ga qo'shing) + `NotificationDispatcher.NotifyAsync(...)`
   (`NotificationRecipients` kimga yuborishni biladi) + `NotificationType` ga yangi kod.
5. **Frontend** — tugma/havola ko'rsatish `usePermissions().can("kod")` bilan, rol nomi bilan emas.
   Sana `lib/format-date.ts`, pul `formatMoney`. Rang: `bg-brand-solid`, `ink-*`, `brand-*`.
   Hover'ga bog'liq UI yo'q (iPad).
6. **Fayl yuklash** — `StudentDocumentFileStorage` naqshi: `Validate(IFormFile)` → `SaveAsync` →
   tasodifiy kalit + asl kengaytma, `AllowedTypes` oq ro'yxati, `[RequestSizeLimit]`,
   `SaveChangesAsync` xato bersa faylni o'chirish.

### Har bir bosqich oxirida
```bash
bash backend/scripts/dotnet-in-docker.sh test          # yashil bo'lishi shart
cd frontend && npx tsc --noEmit && npx eslint src --max-warnings=0
```
Keyin **commit** (`origin` — GitHub, hech qachon force-push qilinmaydi) va
`docs/09_audit_progress.md` ga bir band.

### Baholar haqida
Kunlar — bitta dasturchi uchun taxminiy. Backend + frontend + test + brauzerda tekshirish
birga hisoblangan.

---

## 1. BOSQICHLAR XARITASI

| # | Bosqich | Nima yopiladi | Kun |
|---|---|---|---|
| 0 | Repoga yozish | T15 | 0.5 |
| 1 | Odamlarni tizimga kiritish | K1, K2, K3, K8(a) | 6 |
| 2 | Yaratilganni tahrirlash | K4, K5, M9, kurs o'chirish | 5 |
| 3 | Elektron jurnalni yopish | K6, K7 | 5 |
| 4 | O'quv natijalari va signallar | M8, M2, M1, M3, M5 | 7 |
| 5 | Teacher 360° va analitika | M4, M6, M7 | 6 |
| 6 | CRM, aloqa va e'lonlar | M10, M11, M12, M14, M16, M17, K8(b) | 8 |
| 7 | Sozlamalar va jadval infratuzilmasi | M13, xona, jadval shabloni, to'lov rejasi | 7 |
| 8 | Speaking va AI qarzlari | M15, T8 | 5 |
| 9 | Ishlab chiqarishga tayyorgarlik | T1–T14 | 12 |
| 10 | Faza 6 (mobil va Telegramdan tashqari) | KPI, NPS, sertifikat, gamification, HR, moliya, multi-tenant | 30+ |

**Jami 1–9 bosqich: ~55 kun.** 10-bosqich alohida mahsulot rejasi darajasida.

### Bog'liqliklar (bu tartibni buzib bo'lmaydi)
```
0 → 1 → 2 → 3 → 4 → 5
              ↓
              6 (Email kanali) → K8(b) parol tiklash OTP
1 → 7 (jadval shabloni guruh tahririga bog'liq)
4 → 10.1 (KPI o'quv natijalarisiz hisoblanmaydi)
6 → 10.2 (NPS bildirishnoma kanaliga bog'liq)
9.1 (global query filter) → 10.7 (multi-tenant)
```

---

## 2. BOSQICH 0 — REPOGA YOZISH  ·  T15  ·  0.5 kun

124 ta fayl commit qilinmagan. Birinchi ish — shuni yopish, chunki keyingi har bir bosqich
shu kod ustiga quriladi.

**Tavsiya qilingan commit bo'linishi** (bittaga tiqmaslik kerak — `git log` tarix sifatida o'qilsin):

1. `Let the office move, freeze and resume a student's place` — `EnrollmentLifecycleController`,
   `StudentLifecycle.tsx`, `EnrollmentLifecycleTests`, audit interceptor kengaytmasi
2. `Let a lesson find a substitute teacher` — `ScheduleController` substitutes/replace,
   `TeacherReplacementTests`, xona datalist
3. `Give the product one search box` — `Search/`, `CommandPalette`, `use-highlight-target`, `SearchTests`
4. `Finish the student and teacher profiles` — `StudentDocuments*`, `OpenChatButton`,
   teacher performance, tegishli testlar
5. `Help the operator pick a group and place a level` — group recommendations, `PlacementAttempt`,
   `PlacementQuestionBank`, `TrialDrawer`, tegishli testlar
6. `Show every role only what it may do` — `MeController`, `use-permissions`, knowledge targets,
   `RoleExperienceTests`
7. `Give the developer the panel the spec promised` — `Observability/`, `DataAccessRequest`,
   `DeveloperAccessTests`
8. `Page every long list` — `PagingExtensions`, sahifalangan controllerlar va ekranlar, `PagingTests`
9. `Seed a centre of a realistic size` — `DemoDataSeeder`
10. `Write down what the audit found` — `docs/10_*`, `docs/11_*`, `CLAUDE.md`, `docs/09_*`

**Tayyorlik mezoni:** `git status` toza, `git log --oneline -10` o'qiladigan tarix.

---

## 3. BOSQICH 1 — ODAMLARNI TIZIMGA KIRITISH  ·  K1, K2, K3, K8(a)  ·  6 kun

> **Nega birinchi:** bu to'rttasisiz real markaz tizimga ko'chib o'tolmaydi. Qolgan hamma narsa
> shulardan keyin ma'noga ega.

### 1.1. Student yaratish (K1) — 1.5 kun

> **✅ Bajarildi 2026-09-21.** Rejadan farqlar (operator uchun ikki ruxsat, filial xodimi guruhsiz yarata olmaydi,
> `StudentDto.Phone`, bildirishnoma yo'q) va sabablari — `docs/09_audit_progress.md` 1.26. Quyidagi matn asl reja.

**Muammo:** `new Student(...)` faqat `LeadsController.cs:414` da. `POST /api/users`
roleCode=STUDENT bilan `User` yaratadi, `Student` yaratmaydi.

**Domen:** o'zgarish yo'q. Migration yo'q.

**Backend** — yangi `backend/src/EduCenter.Api/Students/StudentAdminController.cs`:

| Endpoint | Ruxsat | Nima qiladi |
|---|---|---|
| `POST /api/students` | `students.create` | `User` (parol bilan) + `Student` + ixtiyoriy birinchi `Enrollment` — bitta tranzaksiyada |
| `PATCH /api/students/{id}` | `students.edit` *(yangi)* | `CurrentLevel`, `JoinedDate`, `StudentNumber`; `FullName`/`Phone`/`Email` — `User` ga o'tadi |

- `CreateStudentRequest(FullName, Phone, Email, Password, DateOnly JoinedDate, string? CurrentLevel,
  Guid? GroupId, List<Guid>? ParentUserIds)`
- Student raqami `LeadsController.NextStudentNumberAsync()` dan ko'chiriladi →
  umumiy `StudentNumbers.NextAsync(db, orgId)` helper qilinadi (ikki joyda takrorlanmasin).
- `GroupId` berilsa: guruh scope ichidami, yopilmaganmi, bo'sh o'rni bormi — `EnrollmentLifecycleController`
  dagi `transfer` tekshiruvlari bilan **bir xil** (ularni `EnrollmentRules` helper'iga chiqaring).
- Timeline: `ENROLLED`. Bildirishnoma: student + ota-onalar.

**Yangi ruxsatlar** (`PermissionSeedData`):
```
students.edit         — SuperAdmin AllOrganization, Administrator MyBranches
students.contact.edit — SuperAdmin AllOrganization, Administrator MyBranches, Operator MyBranches
```
*(Asl rejada `students.edit` operatorga ham berilgan edi; matritsa esa "limited (contact info)" deydi, shuning uchun bo'lindi.)*
`students.create` allaqachon bor (SuperAdmin, Administrator, Operator).

**Frontend:**
- `lib/api/students.ts` → `createStudent`, `updateStudent`
- `app/(admin)/students/page.tsx` → "Yangi student" tugmasi (`can("students.create")`) → `StudentCreateDrawer.tsx`
  (`TrialDrawer.tsx` naqshi: guruh tanlash kartalari bilan)
- `app/(admin)/students/[id]/page.tsx` → header'da "Tahrirlash" (`can("students.edit")`)

**Test** — `StudentAdminTests.cs` (~8): yaratish + guruhga yozish, parol < 8 belgi → 400,
takroriy email → 409, teacher rolida → 403, administrator boshqa filial guruhiga yoza olmasligi,
to'lgan guruhga → 409, tahrirlash, operator tahrirlay olishi.

### 1.2. Ota-ona va farzand bog'lami (K2) — 1.5 kun

**Muammo:** `new Parent(...)` faqat seeder'da. Parent Portal real markazda ishlamaydi.

**Domen:** `Parent` va `StudentParent` bor — o'zgarish yo'q. `StudentParent` ga
`Relationship` (ota/ona/vasiy) qo'shilsa foydali → **migration `AddParentRelationship`**.

**Backend** — `backend/src/EduCenter.Api/Students/StudentParentsController.cs`:

| Endpoint | Ruxsat | Nima qiladi |
|---|---|---|
| `GET /api/students/{id}/parents` | `students.view` | Farzandning vasiylari (ism, telefon, email, munosabat) |
| `POST /api/students/{id}/parents` | `parents.manage` *(yangi)* | Ikki rejim: mavjud `userId` ni biriktirish **yoki** yangi ota-ona hisobi yaratish (ism/telefon/email/parol) |
| `DELETE /api/students/{id}/parents/{parentId}` | `parents.manage` | Bog'lamni uzadi (`Parent` qatori qolaveradi — boshqa farzandi bo'lishi mumkin) |
| `GET /api/parents/{id}/children` | `students.view` | Bitta ota-onaning farzandlari |

- Yangi ota-ona yaratilganda `PARENT` roli beriladi, filial farzandning guruhidan olinadi.
- Bitta ota-ona bir nechta farzandga biriktirilishi mumkin (TZ §24) — `StudentParent` join shuni qo'llab-quvvatlaydi.
- Bildirishnoma: ota-onaga "sizga farzand biriktirildi" + login eslatmasi.

**Yangi ruxsat:**
```
parents.manage — SuperAdmin AllOrganization, Administrator MyBranches, Operator MyBranches
```

**Frontend:**
- `lib/api/students.ts` → `listStudentParents`, `addStudentParent`, `removeStudentParent`
- `students/[id]/page.tsx` → "Ota-onalar" — Umumiy tabining ichida karta (alohida tab emas,
  ko'pi bilan 2-3 qator) + `ParentLinkDrawer.tsx`
- Student header'iga ota-ona telefoni chiqadi (M3 ning bir qismi shu yerda yopiladi)

**Test** — `StudentParentsTests.cs` (~7): yangi ota-ona yaratish va bog'lash, mavjud user'ni
bog'lash, ikki farzandli ota-ona, bog'lamni uzish, teacher → 403, administrator boshqa filial
studentiga → 403, ota-ona biriktirilgandan keyin `/api/dashboard/parent` da farzand ko'rinishi.

### 1.3. Guruhga yozish, chiqarish, bitirish (K3) — 1.5 kun

**Muammo:** `Enrollment` faqat konvertatsiya va transferda yaratiladi.
`EnrollmentStatus.Dropped`, `StudentStatus.Graduated`, `StudentStatus.Dropped` — **hech qachon yozilmaydi**.

**Domen:** `Enrollment` ga `EndedAt` (`DateTimeOffset?`) va `EndReason` (`string?`) →
**migration `AddEnrollmentEnd`**.

**Backend** — `EnrollmentLifecycleController.cs` ga qo'shiladi (bu allaqachon "lifecycle" fayli):

| Endpoint | Ruxsat | Nima qiladi |
|---|---|---|
| `POST /api/students/{id}/enrollments` | `student.enroll` *(yangi)* | Qo'shimcha guruhga yozadi (sig'im, guruh holati, scope tekshiriladi). Bitta guruhga ikki marta yozib bo'lmaydi |
| `POST /api/students/{id}/enrollments/{enrollmentId}/drop` | `student.enroll` | Sabab majburiy → `Dropped` + `EndedAt`. Oxirgi faol enrollment bo'lsa student ham `Dropped` |
| `POST /api/students/{id}/graduate` | `student.enroll` | Faol enrollmentlarni `Completed` ga, studentni `Graduated` ga |

- `EnrollmentStatus` ga `Completed` qo'shiladi (hozir: Active/Frozen/Transferred/Dropped).
- **Moliya qoidasi:** `BillingRules` faqat `Active` enrollment'ga invoice yozadi → drop/graduate
  avtomatik hisob-kitobni to'xtatadi, qo'shimcha kod kerak emas. Ochiq invoice'lar tegilmaydi
  (o'qilgan davr uchun) — transfer/freeze bilan bir xil qaror.
- Timeline: `ENROLLED`, `ENROLLMENT_DROPPED`, `GRADUATED` (yangi turlar).
- Bildirishnoma: student + ota-ona + guruh o'qituvchisi.

**Yangi ruxsat:**
```
student.enroll — SuperAdmin AllOrganization, Administrator MyBranches, Operator MyBranches
```

**Frontend:** `StudentLifecycle.tsx` ga uchta amal qo'shiladi (hozir Ko'chirish/Muzlatish bor).
Guruh sahifasida (`groups/[id]`) "Student qo'shish" tugmasi ham shu endpointni chaqiradi.

**Test** — `EnrollmentLifecycleTests.cs` ga +8: ikkinchi guruhga yozish, takroriy yozish → 409,
to'lgan guruh → 409, drop, oxirgi drop studentni ham Dropped qilishi, graduate,
drop'dan keyin invoice chiqmasligi, operator ruxsati.

### 1.4. Parol: o'zgartirish va administrator tomonidan tiklash (K8a) — 1.5 kun

**Muammo:** `AuthController` da faqat login/refresh/logout. Hech kim parolini o'zgartira olmaydi.

> **Eslatma:** to'liq "parolni unutdim" oqimi (OTP) **6-bosqichga** qoldiriladi, chunki u Email/SMS
> kanaliga bog'liq (M12). Hozir o'z-o'zini o'zgartirish va administrator tiklashi yopiladi —
> bular hech qanday tashqi provayder talab qilmaydi.

**Domen:** `User` ga `PasswordChangedAt` (`DateTimeOffset?`), `MustChangePassword` (`bool`) →
**migration `AddPasswordChangeTracking`**.

**Backend** — `AuthController.cs`:

| Endpoint | Ruxsat | Nima qiladi |
|---|---|---|
| `POST /api/auth/change-password` | `[Authorize]` (har qanday kirgan user) | Eski parolni tekshiradi, yangisini qo'yadi, **barcha refresh tokenlarni bekor qiladi**, `MustChangePassword = false` |
| `POST /api/users/{id}/reset-password` | `users.edit` | Administrator vaqtinchalik parol beradi (yoki generatsiya qiladi), `MustChangePassword = true`, tokenlar bekor qilinadi. Javobda parol **bir marta** qaytadi |

- Parol siyosati bitta joyga: `PasswordPolicy.Validate(string)` — kamida 8 belgi
  (hozir `UsersController` va `LeadsController` da ikki marta takrorlangan).
- `MustChangePassword = true` bo'lsa login javobiga bayroq qo'shiladi; frontend `/login` dan
  keyin majburiy o'zgartirish ekraniga olib boradi.
- **Audit:** ikkala amal ham audit jurnaliga tushadi (parolning o'zi hech qachon yozilmaydi).
- Rate limit: `change-password` ga `LoginBruteForce` siyosati qo'llanadi.

**Frontend:**
- `app/(admin)/settings/security/page.tsx` — "Parolni o'zgartirish" (yangi sahifa,
  `UserMenu` dan havola)
- `users/[id]/page.tsx` → "Parolni tiklash" tugmasi (`can("users.edit")`), natijani
  bir martalik ko'rsatadigan modal
- `app/(auth)/change-password/page.tsx` — majburiy o'zgartirish ekrani

**Test** — `AuthTests.cs` ga +6: to'g'ri o'zgartirish, eski parol noto'g'ri → 400,
qisqa parol → 400, o'zgartirgandan keyin eski refresh token ishlamasligi,
administrator tiklashi, student boshqa odamning parolini tiklay olmasligi.

**Bosqich 1 tayyorlik mezoni:** bo'sh bazaga markazni qo'lda kiritib bo'ladi —
filial → kurs → guruh → o'qituvchi → student → ota-ona → guruhga yozish, va har kim
o'z parolini o'zgartira oladi.

---

## 4. BOSQICH 2 — YARATILGANNI TAHRIRLASH  ·  K4, K5, M9  ·  5 kun

### 2.1. Guruhni boshqarish (K4) — 1.5 kun

**Domen:** `Group` ga `Complete()` va `Cancel()` metodlari; `AssignStaff` yoniga `RemoveStaff(userId)`.
Migration yo'q (faqat kod).

**Backend** — `GroupsController.cs`:

| Endpoint | Ruxsat | Nima qiladi |
|---|---|---|
| `PATCH /api/groups/{id}` | `groups.edit` | Nom, sig'im, kurs. Sig'imni joriy studentlar sonidan past qilib bo'lmaydi → 409 |
| `POST /api/groups/{id}/status` | `groups.edit` | `Forming → Active → Completed`, yoki `Cancelled`. Teskari o'tish taqiqlanadi |
| `DELETE /api/groups/{id}/staff/{userId}` | `groups.edit` | O'qituvchini olib tashlaydi. Oxirgi `MainTeacher` ni olib tashlash → 409 (dars kimga qolishini aytish kerak) |
| `DELETE /api/groups/{id}` | `groups.delete` | **Faqat bo'sh guruh:** enrollment yo'q, dars yo'q. Aks holda 409 + "avval yoping" |

- `groups.delete` **allaqachon urug'langan** — faqat endpoint yozilishi kerak.
- `Completed` ga o'tganda: faol enrollmentlar `Completed` ga, kelajakdagi darslar bekor qilinadi
  (2.2 dagi cancel bilan), guruh chat'i arxivlanadi.
- Bildirishnoma: guruh studentlari + o'qituvchilar (`SCHEDULE_CHANGED` / yangi `GROUP_CLOSED`).

**Frontend:** `groups/[id]/page.tsx` — "Tahrirlash", "Guruhni yopish", staff qatorida ✕;
`groups/page.tsx` kartasida o'chirish faqat bo'sh guruhda.

**Test** — `GroupManagementTests.cs` (~9).

### 2.2. Darsni tahrirlash, ko'chirish, bekor qilish (K5) — 2 kun

**Domen:** `Lesson` ga:
- `LessonStatus { Scheduled, Cancelled }` + `CancelReason` (`string?`)
- `Reschedule(DateTimeOffset startsAt, DateTimeOffset endsAt, string roomName)`
- `Cancel(string reason)`

**Migration `AddLessonStatus`.**

**Backend** — `ScheduleController.cs`:

| Endpoint | Ruxsat | Nima qiladi |
|---|---|---|
| `PATCH /api/schedule/lessons/{id}` | `schedule.edit` | Vaqt / xona / o'qituvchi / mavzu. **Konflikt detektori qayta ishlaydi** (xona, o'qituvchi, guruh) → 409 |
| `POST /api/schedule/lessons/{id}/cancel` | `schedule.edit` | Sabab majburiy → `Cancelled`. Davomat va homework saqlanib qoladi |
| `DELETE /api/schedule/lessons/{id}` | `schedule.edit` | **Faqat toza dars:** davomat yo'q, homework yo'q, resurs yo'q. Aks holda 409 + "bekor qiling" |
| `POST /api/schedule/lessons/bulk` | `schedule.edit` | Bir necha darsni birdan ko'chirish/bekor qilish (bayram, filial yopiq) |

- **O'tib ketgan darsni** o'zgartirib bo'lmaydi (teacher replacement'dagi bilan bir xil qoida).
- Konflikt tekshiruvi mavjud `GET /api/schedule/conflicts` mantig'idan ajratib olinadi →
  `ScheduleConflicts.CheckAsync(db, ...)` helper (hozir `POST lessons` ichida ichkarida).
- Bildirishnoma: `SCHEDULE_CHANGED` — guruh studentlari + ota-onalar + o'qituvchi.
  **Bu hodisa turi kodda bor, lekin hozir hech kim yubormaydi** — shu yerda tiriladi.
- Bekor qilingan dars: davomat belgilanmaydi, dashboard "belgilanmagan davomat" hisobiga kirmaydi,
  jadvalda chizilgan holda ko'rinadi.

**Frontend:**
- `schedule/page.tsx` kun modalida har bir dars qatorida "Tahrirlash" / "Bekor qilish"
- `schedule/[lessonId]/page.tsx` sarlavhasida ham xuddi shu amallar
- Bekor qilingan dars: `line-through` + `ink-400`, "Bekor qilindi: <sabab>"

**Test** — `ScheduleEditTests.cs` (~11): ko'chirish, xona konflikti → 409, o'qituvchi konflikti → 409,
o'tib ketgan dars → 400, bekor qilish, bekor qilingan darsga davomat → 409, toza darsni o'chirish,
davomatli darsni o'chirish → 409, teacher rolida → 403, bulk cancel, bildirishnoma yuborilishi.

### 2.3. Imtihon va homeworkni tahrirlash/o'chirish (M9) — 1 kun

| Endpoint | Ruxsat | Qoida |
|---|---|---|
| `PATCH /api/exams/{id}` | `exams.create` | Nom, tur, sana. Natija kiritilgandan keyin ham mumkin |
| `DELETE /api/exams/{id}` | `exams.create` | Faqat natijasiz imtihon → aks holda 409 |
| `DELETE /api/exams/{id}/results/{studentId}` | `exams.score.edit` | Noto'g'ri kiritilgan natijani olib tashlaydi + timeline'ga yozadi |
| `PATCH /api/homework/{id}` | `homework.create` | Sarlavha, tavsif, muddat, maksimal ball |
| `DELETE /api/homework/{id}` | `homework.create` | Faqat topshiriqsiz homework → aks holda 409 |

Timeline: `EXAM_RESULT_REMOVED`. Bildirishnoma: muddat o'zgarsa studentlarga.

**Test** — `LmsTests.cs` ga +8.

### 2.4. Kursni o'chirish — 0.5 kun

`DELETE /api/courses/{id}` (`courses.manage`) — faqat guruhi yo'q kurs. Aks holda 409.

**Bosqich 2 tayyorlik mezoni:** har bir yaratilgan yozuvni tuzatish yoki yopish yo'li bor;
"noto'g'ri kiritdim" holati ma'murni bazaga kirishga majbur qilmaydi.

---

## 5. BOSQICH 3 — ELEKTRON JURNALNI YOPISH  ·  K6, K7  ·  5 kun

TZ §15: "Teacher bir dars ichida **topic, attendance, homework, notes, resources, scores**
kiritishi mumkin." Hozir 3 tasi bor.

### 3.1. Dars mavzusi va izohi (K6) — 1 kun

**Domen:** `Lesson` ga `Notes` (`string?`, 2000 belgi). `SetTopic` yoniga `SetNotes`.
**Migration `AddLessonNotes`.**

**Backend:** `PATCH /api/schedule/lessons/{id}` (2.2 da yaratilgan) mavzu va izohni ham oladi.
Lekin **o'qituvchi uchun alohida yo'l kerak** — unda `schedule.edit` yo'q:

| Endpoint | Ruxsat | Nima qiladi |
|---|---|---|
| `PATCH /api/lessons/{id}/journal` | `attendance.mark` | Faqat `Topic` va `Notes` — dars bergan odam yozadi |

Sabab: `schedule.edit` — jadval tuzish huquqi (administrator), `attendance.mark` — dars o'tkazish
huquqi (o'qituvchi). Mavzu — pedagogik yozuv, jadval emas.

**Frontend:** `schedule/[lessonId]/page.tsx` — yuqorida "Mavzu" input + "Dars izohi" textarea,
davomat bilan bir "Saqlash" tugmasida.

### 3.2. Davomat izohi va dars balli (K6) — 1 kun

**Domen:** `Attendance` ga `Note` (`string?`, 500) va `Score` (`int?`).
**Migration `AddAttendanceNoteAndScore`.**

- `Score` — TZ §15 dagi "scores": dars ichidagi faollik/og'zaki ball. `Lesson` ga
  `MaxLessonScore` (`int?`) qo'shiladi — o'qituvchi shkalani o'zi belgilaydi, bo'sh bo'lsa
  ball maydoni umuman ko'rinmaydi.
- `POST /api/lessons/{id}/attendance` request'i kengayadi: `[{ studentId, status, note, score }]`.

**Frontend:** davomat jadvaliga ikkita ustun. Telefonda ball ustuni yashiriladi (sig'maydi) —
qator bosilganda ochiladigan kichik forma.

**Test** — `LmsTests.cs` ga +5.

### 3.3. Homeworkga fayl yuklash (K7) — 2 kun

**Muammo:** `SubmitHomeworkRequest(ContentUrl, ContentText)` — faqat matn/havola.
`HomeworkType.FileUpload` enum'da bor, endpoint yo'q.

**Domen:**
- `Homework` ga `AttachmentKey`, `AttachmentName`, `AttachmentContentType`, `AttachmentSize`
  (o'qituvchining topshiriq fayli — worksheet, audio)
- `HomeworkSubmission` ga xuddi shu to'rtlik (studentning javob fayli)
**Migration `AddHomeworkAttachments`.**

**Backend** — yangi `HomeworkFileStorage.cs` (`StudentDocumentFileStorage` nusxasi):
- `App_Data/homework`, 20 MB (audio uchun 10 MB kam)
- `AllowedTypes`: pdf, doc(x), ppt(x), rasm, mp3, m4a, wav, txt

| Endpoint | Ruxsat | Nima qiladi |
|---|---|---|
| `POST /api/homework/upload` | `homework.create` | Topshiriq fayli bilan homework yaratadi (multipart) |
| `GET /api/homework/{id}/attachment` | `homework.view` | Topshiriq faylini beradi |
| `POST /api/homework/{id}/submissions/upload` | `homework.submit` | Student javob faylini yuklaydi |
| `GET /api/homework/submissions/{id}/attachment` | `homework.view` | Javob faylini beradi (student — faqat o'zinikini) |

**Frontend:** `LessonResources.tsx` dagi yuklash naqshi qayta ishlatiladi; student tomonda
`app/(admin)/students/[id]` Homework tabida va dars sahifasida.

**Test** — `HomeworkFilesTests.cs` (~8): yuklash/olish/o'chirish, hajm chegarasi, ruxsat etilmagan
tur, boshqa studentning javobini olishga urinish → 403, o'qituvchi hammasini ko'rishi.

### 3.4. Individual topshiriq (K7) — 1 kun

**Muammo:** homework faqat `LessonId` ga bog'lanadi → har doim butun guruhga.
TZ §16: "group/student assignment".

**Domen:** yangi `HomeworkTarget(HomeworkId, StudentId)`.
**Migration `AddHomeworkTargets`.**
Qator yo'q = butun guruh (orqaga moslik: mavjud homeworklar o'zgarishsiz ishlaydi).

**Backend:** `CreateHomeworkRequest` ga `List<Guid>? StudentIds`.
`GET /api/homework?lessonId=` va `my-submissions` filtrlari `HomeworkTarget` ni hisobga oladi.
Bildirishnoma faqat maqsadli studentlarga.

**Test** — `LmsTests.cs` ga +4.

**Bosqich 3 tayyorlik mezoni:** o'qituvchi bitta dars sahifasida mavzu, izoh, davomat, ball,
uy vazifasi (fayl bilan) va material — hammasini kirita oladi. TZ §15 to'liq yopiladi.

---

## 6. BOSQICH 4 — O'QUV NATIJALARI VA SIGNALLAR  ·  M8, M2, M1, M3, M5  ·  7 kun

### 4.1. ExamResult'ga Grammar va Vocabulary (M8) — 0.5 kun

**Domen:** `ExamResult` ga `Grammar` (`decimal?`) va `Vocabulary` (`decimal?`).
**Migration `AddExamGrammarVocabulary`.**

Bu **eng pastdagi g'isht**: usiz M2 (Learning Profile), M6 (Branch Academic dashboard) va
10.1 (Teacher KPI) hisoblanmaydi. Shuning uchun birinchi.

`POST /api/exams/{id}/results` va DTO'lar kengayadi; frontend imtihon natijalari jadvaliga
ikkita ustun. IELTS imtihonida bu ikkisi bo'sh qoladi (rubrikada yo'q) — nullable shuning uchun.

### 4.2. Student Learning Profile (M2) — 1.5 kun

**Backend** — `StudentLearningController.cs` ga:

| Endpoint | Ruxsat | Nima qiladi |
|---|---|---|
| `GET /api/students/{id}/learning-profile` | `students.view` | TZ §7 dagi sakkizta raqam |

Hisoblash (yangi jadval kerak emas — hammasi mavjud ma'lumotdan):
| Ko'rsatkich | Manba |
|---|---|
| Listening / Reading / Speaking | `ExamResult` oxirgi 5 ta natija o'rtachasi (og'irlikli: yangisi kuchliroq) |
| Writing | `ExamResult.Writing` **va** `WritingEvaluation.OverallBand` (o'qituvchi override'i ustun) |
| Grammar % / Vocabulary % | `ExamResult.Grammar/Vocabulary` (4.1) + `HomeworkType.Grammar/Vocabulary` topshiriqlari balli |
| Attendance % | `Attendance` (allaqachon bor) |
| Homework completion % | `HomeworkSubmission` (allaqachon bor) |

Har bir raqam yoniga `sampleSize` qaytariladi — 2 ta imtihondan chiqarilgan "6.5" bilan
20 tadan chiqarilgani bir xil ishonchda emas, UI buni ko'rsatishi kerak.
Ma'lumot yetmasa `null` — **soxta raqam ko'rsatilmaydi** (Teacher Performance'dagi bilan bir xil qaror).

**Frontend:** `StudentLearningTab.tsx` — TZ §7 dagi ko'rinish: har bir skill uchun polosa,
yonida trend o'qi (oxirgi 3 natija), pastida "Bu raqam N ta natijadan".

### 4.3. Student 360°: Learning, Writing, Speaking tablari (M1) — 1.5 kun

| Tab | Manba | Ruxsat |
|---|---|---|
| Learning | 4.2 endpoint | `students.view` |
| Writing | xodim: `GET /api/ai/writing/reviews?studentId=` (**mavjud, o'zgarish kerak emas**) · student o'z profilida: `GET /api/ai/writing/evaluations` | `ai.writing.review` / `ai.writing.use` |
| Speaking | `GET /api/ai/speaking/sessions` — **`studentId` filtri qo'shilishi kerak** | `ai.speaking.use` (o'ziniki) + `students.view` (xodim) |

Backend'da bitta o'zgarish: `SpeakingController.Sessions` hozir qattiq `s.UserId == scope.UserId`
bilan filtrlaydi (faqat o'zinikini). Unga ixtiyoriy `studentId` qo'shiladi va berilganda
`scope.CanAccessStudentAsync(db, studentId)` tekshiriladi — `WritingController.Reviews` dagi
aynan shu naqsh.

**Frontend:** `students/[id]/page.tsx` tab ro'yxatiga uchtasi qo'shiladi, `usePermissions` bilan
shartlanadi.

### 4.4. Student profili sarlavhasi (M3) — 1 kun

`StudentDto` ga qo'shiladi: `Phone`, `BranchName`, `CourseName`, `MainTeacherName`,
`SupportTeacherNames`, `ParentContacts`, `PhotoUrl`.

- `Phone` — `User.Phone` dan (bazada bor, DTO'ga chiqarilmagan)
- Filial/kurs/o'qituvchi — faol enrollment → group → branch/course/staff
- Ota-ona kontaktlari — 1.2 dan
- **Foto:** `User` ga `PhotoKey` + `UserPhotoStorage` (`StudentDocumentFileStorage` naqshi),
  `POST /api/users/{id}/photo` (`users.edit`). **Migration `AddUserPhoto`.**
  `Avatar.tsx` foto bo'lsa rasmni, bo'lmasa bosh harflarni ko'rsatadi.

Xuddi shu maydonlar `/teachers/{id}` sarlavhasiga ham qo'llanadi (bir xil `Avatar`).

### 4.5. Qolgan 4 ta Learning Signal (M5) — 2 kun

TZ §7 da 6 ta signal, 2 tasi bor. Qolgani `AutomationCatalog.All` ga yangi qoida sifatida
qo'shiladi (naqsh tayyor: kod, nom, trigger, tavsif, parametrlar, amallar):

| Qoida kodi | Shart | Parametrlar |
|---|---|---|
| `WritingGrammarSignal` | Oxirgi N ta writing baholashida grammatika bandi M dan past | N=3, M=5.5 |
| `SpeakingFluencySignal` | Oxirgi N ta speaking sessiyasida fluency bandi pasaymoqda yoki M dan past | N=3, M=5.0 |
| `ListeningLowSignal` | Oxirgi N ta imtihonda Listening M dan past | N=2, M=5.0 |
| `ReadingTimeSignal` | Reading balli boshqa skilllardan D bandga past (vaqt boshqaruvi belgisi) | D=1.0 |

Hammasi `FollowUpRules` ichida, mavjud idempotentlik naqshi bilan (trigger vaqtiga nisbatan) →
`SupportCase` yaratadi (`SupportCaseSource.System`) + administratorga bildirishnoma.

`AutomationSweepJob` allaqachon ularni chaqiradi — `RunSweepAsync` ga to'rtta qo'shiladi.
`SweepResult` record'i kengayadi.

**Frontend:** `/automation` sahifasi katalogdan avtomatik quriladi — **o'zgarish kerak emas**.
`/support` sahifasida signal sababi ko'rinadi (mavjud `SupportCase.Reason`).

**Test** — `SupportAndFollowUpTests.cs` ga +8 (har bir qoida uchun: ishga tushishi va
ishlamasligi kerak bo'lgan holat).

**Bosqich 4 tayyorlik mezoni:** o'qituvchi student profilini ochganda "bu bola nimada qiynalyapti"
degan savolga javob ko'radi — TZ §7 va §8 ning asosiy qiymati shu.

---

## 7. BOSQICH 5 — TEACHER 360° VA ANALITIKA  ·  M4, M6, M7  ·  6 kun

### 5.1. Teacher 360° qolgan tablari (M4) — 2 kun

TZ §6: 13 tab. Bor 4 ta (Guruhlar, Studentlar, Jadval, Natijadorlik).

| Tab | Manba | Izoh |
|---|---|---|
| Overview | `GET /api/teachers/{id}` kengaytiriladi | Ixtisoslik, filial(lar), ish boshlagan sana, guruh/student soni, KPI qisqacha |
| Attendance | `Lesson` + `Attendance` | **O'qituvchining o'z davomati emas** (uni kuzatadigan infratuzilma yo'q) — *guruhlaridagi davomat* |
| Homework | `Homework` + `HomeworkSubmission` | Yaratgan topshiriqlari, tekshirilmagani, o'rtacha tekshirish vaqti |
| Exam Results | `Exam` + `ExamResult` | Guruhlari bo'yicha o'rtacha ballar va trend |
| Support Teachers | `GroupStaff` | Guruhlarida ishlaydigan yordamchilar |
| Activity History | `AuditLog` (mavjud!) | Shu user qilgan amallar — `audit.log.view` ruxsati bilan |

`Feedback` va `KPI` tablari — 10-bosqich (NPS va KPI modeliga bog'liq).

**Backend:** `TeachersController` ga `GET /api/teachers/{id}/workload` (attendance + homework +
exams bir javobda — uchta alohida so'rov o'rniga).

**Yangi ruxsat:** yo'q — hammasi `users.view` + mavjud scope bilan.

### 5.2. Branch Dashboard: Academic, Teachers, CRM (M6) — 3 kun

Hozir `/branches/{id}` = org dashboard'ning filialga qulflangan nusxasi (faqat Finance to'liq).

**Backend** — `DashboardController.cs` ga uchta yangi bo'lim (bitta endpoint, uchta blok):

`GET /api/dashboard/branch/{id}?from=&to=` (`dashboard.view`, scope tekshiradi):

| Blok | Ko'rsatkichlar | Manba |
|---|---|---|
| Academic | Listening/Reading/Writing/Speaking o'rtachalari, Grammar %, Vocabulary %, exam trendi (oylik), davomat | `ExamResult` (4.1 dan keyin to'liq), `Attendance` |
| Teachers | Faollik (o'tkazgan darslari), guruh davomati, homework tekshirish tezligi (o'rtacha soat), student retention, support faolligi | `Lesson`, `Attendance`, `HomeworkSubmission.ReviewedAt - SubmittedAt`, `Enrollment` |
| CRM | Lidlar, bog'lanilgan, trial band, trial keldi, trial→enrolled, trial→paid, yo'qotilgan, **manba bo'yicha taqsimot** | `Lead`, `TrialLesson`, `Invoice` |

**Diqqat — unumdorlik:** bu og'ir hisob. Sana oralig'i bo'yicha guruhlangan SQL yoziladi
(`FinanceQueries.cs` naqshi — LINQ emas, `FromSql` bilan), va natija 5 daqiqaga keshlanadi
(hozircha xotirada; Redis kelganda — 9.6 da — u yerga ko'chadi).

**Frontend:** `branches/[id]/page.tsx` endi `OrganizationDashboard` ni emas, yangi
`BranchDashboard.tsx` ni ko'rsatadi: to'rtta bo'lim (Academic / Teachers / CRM / Finance) `Tabs` bilan.
Grafiklar uchun kutubxona kerak — `recharts` (Tailwind bilan mos, SSR muammosi yo'q).

### 5.3. Org dashboard: AI usage (M7) — 0.5 kun

`OrganizationDashboardDto` ga `AiRequests`, `AiCostUsd`, `AiActiveStudents` —
`AiUsageController.Summary` dagi hisob qayta ishlatiladi.
(Teacher KPI average — 10.1 da, KPI modeli tayyor bo'lgandan keyin.)

### 5.4. `kamchiliklar.docx` §2 — "CRM juda sodda" (0.5 kun tekshiruv)

Foydalanuvchining asl shikoyati. 09 §1.24 da lid→trial→student oqimi qayta yozilgan, lekin
**sudrash (drag) yo'llari Playwright bilan sinalmagan**. Bu bosqichda:
- doska sudrashini brauzerda uchdan-uch tekshirish (Yangi→Bog'lanildi→Trial→Konvertatsiya)
- 5.2 dagi CRM bloki bilan birga operator uchun "bugun nima qilishim kerak" ko'rinishi to'liq bo'ladi

**Bosqich 5 tayyorlik mezoni:** filial boshlig'i bitta ekrandan filialining o'quv, o'qituvchi,
CRM va moliya holatini ko'radi — TZ §5.2 to'liq.

---

## 8. BOSQICH 6 — CRM, ALOQA VA E'LONLAR  ·  M10–M12, M14, M16, M17, K8(b)  ·  8 kun

### 6.1. Lead: qulay vaqt va daraja (M10) — 1 kun

**Domen:** `Lead` ga `PreferredTime` (`string?` — "Ish kunlari kechqurun" kabi tanlov yoki
erkin matn) va `CurrentLevel` (`string?`).
**Migration `AddLeadPreferences`.**

**Ta'sir:** `GET /api/leads/{id}/group-recommendations` ballash formulasi qayta yoziladi —
hozir 4 omil (kurs 50%, filial 25%, bo'sh o'rin 20%, faollik 5%), yangisi 6 omil:

| Omil | Og'irlik |
|---|---|
| Qiziqqan kurs mosligi | 35% |
| **Daraja mosligi** (lid darajasi ↔ guruh kursi darajasi) | 20% |
| Filial | 15% |
| **Qulay vaqt** (guruhning yaqin darslari vaqti bilan taqqoslash) | 15% |
| Bo'sh o'rin | 10% |
| Guruh faolligi | 5% |

Placement testi (`PlacementAttempt`) natijasi `Lead.CurrentLevel` ga yoziladi — hozir faqat
konvertatsiya oynasida taklif qilinadi, endi lid kartasida ham turadi.

### 6.2. Manba bo'yicha marketing voronkasi (M11) — 1 kun

`GET /api/leads/funnel?from=&to=&branchId=` (`leads.view`):

```
manba → lidlar → bog'lanilgan → trial band → trial keldi → student bo'ldi → to'lov qildi
```
Har bosqich uchun son va oldingi bosqichga nisbatan konversiya %.

**Frontend:** `/leads` sahifasiga "Voronka" tab — gorizontal voronka diagrammasi + manba jadvali.
5.2 dagi Branch CRM bloki xuddi shu endpointdan oziqlanadi.

### 6.3. Email va SMS kanallari (M12) — 2 kun

**Muammo:** `NotificationChannel` da `InApp` va `Telegram`, faqat `InApp` yetkaziladi.

**Domen:** `NotificationChannel` ga `Email`, `Sms` qo'shiladi. `Notification` ga
`DeliveredChannels` (`string?` — yetkazilgan kanallar), `DeliveryError` (`string?`).
**Migration `AddNotificationDelivery`.**

**Arxitektura** — `INotificationSender` interfeysi (AI provayderlaridagi naqsh):
```csharp
public interface INotificationSender
{
    NotificationChannel Channel { get; }
    Task<SendResult> SendAsync(NotificationTarget target, NotificationMessage message, CancellationToken ct);
}
```
- `SmtpEmailSender` — `MailKit`, sozlama `Notifications:Email` (host, port, user, from)
- `SmsSender` — O'zbekiston provayderi (Eskiz yoki Play Mobile), `HttpClient` + API kalit,
  sozlama `Notifications:Sms`. Kalit **faqat `appsettings.Local.json` da**, hech qachon commit qilinmaydi
- `TelegramSender` — interfeys tayyor turadi, implementatsiya token kelganda (rejadan tashqari)

`NotificationDispatcher` o'zgaradi: inbox qatorini yozgandan keyin, foydalanuvchi
`NotificationPreference` da yoqib qo'ygan har bir kanal uchun **Hangfire fon vazifasi**
qo'yiladi (SMTP/SMS so'rovi HTTP so'rovini kutdirmasligi kerak).

**Frontend:** `/settings/notifications` jadvaliga ikkita ustun (Email, SMS) — komponent
kanallar ro'yxatidan quriladi, shuning uchun o'zgarish minimal.

**Test** — `NotificationsTests.cs` ga +6: soxta sender bilan yetkazish, o'chirilgan kanalga
yuborilmasligi, xato bo'lsa inbox qatori baribir qolishi, dedupe.

### 6.4. Parolni tiklash — OTP oqimi (K8b) — 1 kun

6.3 tugagandan keyin mumkin bo'ladi.

**Domen:** `PasswordResetToken(UserId, CodeHash, ExpiresAt, UsedAt, Attempts)`.
**Migration `AddPasswordResetTokens`.**

| Endpoint | Ruxsat | Nima qiladi |
|---|---|---|
| `POST /api/auth/forgot-password` | `[AllowAnonymous]` + rate limit | Email yoki telefon oladi. **Har doim 204 qaytaradi** (foydalanuvchi bor-yo'qligini oshkor qilmaydi). Mavjud bo'lsa 6 raqamli kod Email/SMS ga ketadi, 15 daqiqa amal qiladi |
| `POST /api/auth/reset-password` | `[AllowAnonymous]` + rate limit | Kod + yangi parol. 5 marta xato → token bekor |

Kod **hash qilib** saqlanadi (parol kabi). Muvaffaqiyatli tiklashdan keyin barcha refresh
tokenlar bekor qilinadi. Audit jurnaliga tushadi.

**Frontend:** `app/(auth)/forgot-password/page.tsx` — ikki qadamli forma.

### 6.5. E'lonlar (M14) — 1.5 kun

**Muammo:** Staff Channels ichida `Announcements` bor, lekin u faqat xodimlar uchun.
Student va ota-ona hech qanday e'lon ko'rmaydi (TZ §24).

**Domen:** yangi `Announcement`:
```
OrganizationId, BranchId?, GroupId?, Title, Body, AuthorId,
Audience { All, Students, Parents, Staff }, PublishedAt, ExpiresAt?
```
**Migration `AddAnnouncements`.**

| Endpoint | Ruxsat | Nima qiladi |
|---|---|---|
| `GET /api/announcements` | `announcements.view` *(yangi)* | Foydalanuvchiga tegishlilari (scope + audience bo'yicha) |
| `POST /api/announcements` | `announcements.manage` *(yangi)* | Filial yoki guruh yoki butun tashkilot |
| `PATCH` / `DELETE /api/announcements/{id}` | `announcements.manage` | |

E'lon chiqqanda `NotificationType.Announcement` bildirishnomasi yuboriladi (6.3 dan keyin —
Email/SMS bilan ham).

**Yangi ruxsatlar:**
```
announcements.view   — hamma rol (scope bilan: Student Self, Parent MyChildren, Teacher MyGroups…)
announcements.manage — SuperAdmin AllOrganization, Administrator MyBranches
```

**Frontend:** `/announcements` sahifasi + dashboard'da "So'nggi e'lonlar" kartasi
(student/parent/teacher dashboardlariga).

### 6.6. Universal Search: to'lov (M16) — 0.5 kun

`SearchController` ga sakkizinchi tur: `payment` — summa, student ismi yoki kvitansiya
raqami bo'yicha, `payments.view` ruxsati bilan. Natija `/finance?highlight=<id>` ga olib boradi
(1.17 dagi `useHighlightTarget` naqshi).
"Action" turi — buyruq palitrasi allaqachon navigatsiya beradi; unga "Yangi student", "Yangi lid",
"Dars qo'shish" kabi amallar qo'shiladi (`CommandPalette.tsx`, backend kerak emas).

### 6.7. Shikoyatlar oqimi (M17) — 1 kun

TZ §41: `Complaint → Assigned → In Progress → Resolved → Feedback`.

**Domen:** yangi `Complaint`:
```
OrganizationId, BranchId, StudentId?, AuthorUserId, Subject, Body,
Status { New, Assigned, InProgress, Resolved, Closed },
AssignedToUserId?, Resolution?, ResolvedAt?, SatisfactionScore? (0-10)
```
**Migration `AddComplaints`.**

| Endpoint | Ruxsat |
|---|---|
| `GET /api/complaints` (sahifalangan, status filtri) | `complaints.view` *(yangi)* |
| `POST /api/complaints` | `complaints.create` *(yangi)* — student, ota-ona, xodim |
| `POST /api/complaints/{id}/assign` | `complaints.manage` *(yangi)* |
| `POST /api/complaints/{id}/status` | `complaints.manage` |
| `POST /api/complaints/{id}/resolve` | `complaints.manage` |
| `POST /api/complaints/{id}/feedback` | `complaints.create` — muallif qanoatlanish ballini qo'yadi |

Timeline: `COMPLAINT_CREATED`, `COMPLAINT_RESOLVED` (TZ §41 "Complaint CRM timeline'da saqlanadi").

**Frontend:** `/complaints` — `/support` sahifasi naqshi (tab'lar: Yangi / Jarayonda / Yopilgan).

**Bosqich 6 tayyorlik mezoni:** markaz mijozlari bilan tizim ichidan aloqa qila oladi —
e'lon, bildirishnoma (email/sms), shikoyat va parol tiklash.

---

## 9. BOSQICH 7 — SOZLAMALAR VA JADVAL INFRATUZILMASI  ·  M13 + kichik bandlar  ·  7 kun

### 7.1. Tashkilot sozlamalari (M13) — 1.5 kun

**Muammo:** `org.settings.edit` ruxsati urug'lanadi, lekin hech qanday endpoint uni ishlatmaydi.

**Domen:** `Organization` ga `LogoKey`, `Currency` (standart "UZS"), `Locale`, `WorkdayStart`,
`WorkdayEnd`, `DefaultTimezone`, `BrandColor`.
**Migration `AddOrganizationSettings`.**

| Endpoint | Ruxsat |
|---|---|
| `GET /api/organizations/current` | `branch.view` |
| `PATCH /api/organizations/{id}` | `org.settings.edit` |
| `POST /api/organizations/{id}/logo` | `org.settings.edit` |

**Frontend:** `/settings/organization` — Super Admin uchun. Logo sidebar'da va login sahifasida
ko'rinadi; `BrandColor` `lib/theme.ts` dagi `--brand-*` tokenlarini almashtiradi.

Bu **10.7 (multi-tenant)** ning birinchi qadami — tenant-level config shu yerdan boshlanadi.

### 7.2. Ruxsat matritsasi muharriri (M13) — 1.5 kun

**Muammo:** `permission.policy.edit` urug'langan, UI yo'q. Grantlarni o'zgartirish uchun
`PermissionSeedData.cs` ni tahrirlab qayta deploy qilish kerak.

`PermissionResolver` **override'ni allaqachon o'qiydi** (09 §1.19) — faqat yozadigan yo'l yo'q.

> ⚠️ **Tuzatildi (2026-09-25, `09` §1.56):** bu faqat **foydalanuvchi** darajasi (`user_permissions`) uchun to'g'ri edi. Rol grantlarini
> (`role_permissions`) `DbSeeder` har startda qayta yozadi, shuning uchun muharrir ularni emas, ularning **ustidagi** qatlamni
> (`role_permission_overrides`) yozadi va resolver uni o'qiydi.

| Endpoint | Ruxsat |
|---|---|
| `GET /api/permissions/matrix` | `permission.policy.edit` | Barcha kod × rol × scope, qaysi biri override qilinganini ko'rsatib |
| `PUT /api/permissions/overrides` | `permission.policy.edit` | Bitta katakni o'zgartiradi (scope berish yoki butunlay olib tashlash) |
| `DELETE /api/permissions/overrides/{id}` | `permission.policy.edit` | Standart holatga qaytaradi |

**Xavfsizlik qoidasi:** Super Admin o'zidan `permission.policy.edit` ni olib tashlay olmaydi
(tizimni qulflab qo'yish) → 409. Har bir o'zgarish audit jurnaliga tushadi.

**Frontend:** `/settings/permissions` — matritsa jadvali (kod × rol), katak bosilganda scope
tanlagich. `docs/01_role_permission_matrix.md` bilan bir xil ko'rinish.

### 7.3. Xona (Room) entity'si — 1.5 kun

**Muammo:** `Lesson.RoomName` — matn. Xona sig'imi, jihozi yo'q; `GET /api/schedule/rooms`
faqat ishlatilgan nomlarni yig'adi.

**Domen:** yangi `Room(OrganizationId, BranchId, Name, Capacity, Equipment, IsActive)`.
`Lesson` ga `RoomId` (`Guid?`) — `RoomName` **saqlanib qoladi** (tarixiy yozuvlar uchun va
migratsiya oson bo'lishi uchun).
**Migration `AddRooms`** — mavjud `RoomName` qiymatlaridan xonalar generatsiya qilinadi
(migration ichida `INSERT ... SELECT DISTINCT`).

| Endpoint | Ruxsat |
|---|---|
| `GET/POST/PATCH/DELETE /api/rooms` | `rooms.manage` *(yangi)*, `GET` — `schedule.view` |
| `GET /api/rooms/{id}/availability?date=` | `schedule.view` | Kun bo'yicha bandlik |

Konflikt detektori `RoomId` bo'yicha ishlaydi (matn taqqoslashdan aniqroq).
Dars yaratishda: xona sig'imi guruh sig'imidan kichik bo'lsa **ogohlantirish** (xato emas).

### 7.4. Guruh jadval shabloni — 1.5 kun

**Muammo:** har bir dars alohida yaratiladi. 8 haftalik jadval faqat `DemoDataSeeder` da
tsikl bilan generatsiya qilinadi — mahsulotda bunday imkoniyat yo'q.

**Domen:** yangi `GroupScheduleSlot(GroupId, DayOfWeek, StartTime, DurationMinutes, RoomId?, TeacherId?)`.
**Migration `AddGroupScheduleSlots`.**

| Endpoint | Ruxsat |
|---|---|
| `GET/PUT /api/groups/{id}/schedule-template` | `groups.edit` | Haftalik slotlar (masalan Du/Cho/Ju 18:00, 90 daqiqa) |
| `POST /api/groups/{id}/generate-lessons` | `schedule.edit` | Shablondan sana oralig'iga darslar yaratadi. **Konflikt bo'lsa hech narsa yaratmaydi** — avval to'liq tekshirib, keyin yozadi (409 + konfliktlar ro'yxati) |

Bu 6.1 dagi "qulay vaqt" omilini ham haqiqiy qiladi: guruhning haftalik vaqti endi ma'lum.

**Frontend:** `groups/[id]` sahifasida "Jadval" tab — haftalik grid, "Darslarni yaratish"
tugmasi sana oralig'i bilan.

### 7.5. To'lov jadvali (payment schedule) — 1 kun

TZ §25 da "payment schedule" bor; hozir invoice oyma-oy avtomatik chiqadi, bo'lib to'lash rejasi yo'q.

**Domen:** `Invoice` ga `ParentInvoiceId` (`Guid?`) va `InstallmentNumber` (`int?`).
**Migration `AddInvoiceInstallments`.**

`POST /api/finance/invoices/{id}/split` (`payments.edit`) — bitta invoice'ni N qismga bo'ladi,
har biriga o'z muddati. `BillingRules` va qarzdorlar hisobiga ta'sir qilmaydi (jami bir xil).

**Bosqich 7 tayyorlik mezoni:** markaz o'z sozlamalarini, xonalarini, haftalik jadvalini va
ruxsat siyosatini kod tegmasdan boshqara oladi.

---

## 10. BOSQICH 8 — SPEAKING VA AI QARZLARI  ·  M15, T8  ·  5 kun

### 8.1. TTS provayderi (M15) — 2 kun

**Muammo:** TZ §34 pipeline'i `… → LLM → Response → TTS → Speaker`. Kodda `ISTTProvider`
(Whisper) va LLM bor, **TTS umuman yo'q** — imtihonchi savoli faqat matn.

**Arxitektura:** `ITextToSpeechProvider` — `ISpeechToTextProvider` bilan bir xil shaklda
(`Ai/Providers/SpeechToTextProvider.cs` naqshi):
```csharp
public interface ITextToSpeechProvider
{
    string Name { get; }
    Task<TtsResult> SynthesizeAsync(string text, string voice, CancellationToken ct);
}
```
Implementatsiyalar:
- `PiperTtsProvider` — lokal, bepul, Docker konteynerda (`rhasspy/piper`), inglizcha ovozlar.
  **Tavsiya etilgan birinchi variant** — DeepSeek kaliti kabi tashqi xarajat yo'q va bu mashinada
  Ollama allaqachon ishlaydi.
- `OpenAiCompatibleTtsProvider` — kelajakda pullik provayder uchun.

`AiOptions` ga `Tts` bo'limi (provider, voice, enabled). `AiGateway` ga `SynthesizeAsync` —
xuddi `TranscribeAsync` kabi limit/fallback/journal ichidan o'tadi.

**Backend:** `GET /api/ai/speaking/sessions/{id}/turns/{turnId}/question-audio` —
savol audiosini beradi (birinchi so'rovda generatsiya qilib `SpeakingAudioStorage` ga saqlaydi).

**Frontend:** `ai/speaking/page.tsx` — savol yonida ▶ tugmasi, avtomatik ijro sozlamasi.

### 8.2. Full Mock rejimi (M15) — 1 kun

`SpeakingMode` ga `FullMock`. `SpeakingScript.cs` da uchta qism ketma-ket:
Part 1 (4-5 savol) → Part 2 (karta + 1 daqiqa tayyorgarlik + 2 daqiqa javob) → Part 3 (4-5 savol).
`MaxTurns` shu rejim uchun 12 dan 15 ga ko'tariladi.
Baholash uchta qismni birga oladi va qismlar bo'yicha ham band beradi.

### 8.3. Answer Relevance (M15) — 0.5 kun

TZ §35 da 9 ta mezon. Hozir 3 tasi (Fluency, Lexical, Grammar) + 2 xom signal (WPM, pauzalar).
**Relevance** transkriptdan hisoblanadi — savol va javob bir promptda modelga beriladi,
`relevance` bandi JSON sxemaga qo'shiladi. `SpeakingSession` ga `RelevanceBand` (`decimal?`).
**Migration `AddSpeakingRelevance`.**

Pronunciation / Stress / Rhythm — **10.8** (xom audio tahlili kerak).

### 8.4. Fayl xavfsizligi va formatlar (T8) — 1.5 kun

**Muammo:** TZ §38 ning 2-qadami ("malware/security scan") o'tkazib yuborilgan.

- **ClamAV** konteyner sifatida (`docker-compose.yml` ga `clamav/clamav`),
  `IFileScanner` interfeysi + `ClamAvScanner` (TCP INSTREAM). Sozlama o'chirilgan bo'lsa
  `NoopScanner` (dev) — lekin **production'da majburiy**, `Program.cs` da tekshiruv.
- Skaner **barcha yuklash yo'llariga** qo'llanadi: knowledge, resources, student-documents,
  chat, homework (3.3), user photo (4.4).
- Fayl turi **mazmuni bo'yicha** tekshiriladi (magic bytes), faqat kengaytma bo'yicha emas.
- `DocumentTextExtractor` ga `.pptx`, `.txt`, `.md` qo'shiladi (hozir faqat `.pdf`, `.docx`).

**Test** — `FileSecurityTests.cs` (~5): EICAR test fayli rad etilishi, kengaytmasi almashtirilgan
fayl rad etilishi, yangi formatlar parsing qilinishi.

**Bosqich 8 tayyorlik mezoni:** Speaking moduli TZ §34 pipeline'ini to'liq bajaradi va
yuklangan hech bir fayl tekshirilmasdan diskka tushmaydi.

---

## 11. BOSQICH 9 — ISHLAB CHIQARISHGA TAYYORGARLIK  ·  T1–T14  ·  12 kun

> Bu bosqichsiz tizimni real markazga qo'yib bo'lmaydi. 1–8 bosqichlar funksiyani yopadi,
> bu bosqich **ishonchlilikni** yopadi.

### 9.1. Tenant izolyatsiyasi — global query filter (T1) — 2 kun  🔴

**Muammo:** `AppDbContext` da bironta `HasQueryFilter` yo'q. Har bir so'rov `OrganizationId` ni
qo'lda filtrlaydi. Bitta unutilgan `Where` → tashkilotlararo ma'lumot sizishi.

**Yechim:**
```csharp
// AppDbContext.OnModelCreating
foreach (var entityType in modelBuilder.Model.GetEntityTypes()
             .Where(t => typeof(TenantEntity).IsAssignableFrom(t.ClrType)))
{
    modelBuilder.Entity(entityType.ClrType).AddQueryFilter<TenantEntity>(
        e => tenant.OrganizationId == Guid.Empty || e.OrganizationId == tenant.OrganizationId);
}
```
- `ITenantProvider` — scoped servis, `HttpContext` dan `OrganizationId` ni oladi.
  `Guid.Empty` = tizim konteksti (seeder, Hangfire job'lari, migratsiya) — filter o'chadi.
- Hangfire job'lari uchun `TenantScope.System()` — aniq, o'qiladigan chetlab o'tish.
- Mavjud qo'lda filtrlar **qoladi** (ikki qavat himoya, hech narsa buzilmaydi).

**Test:** `TenantIsolationTests.cs` — ikkinchi tashkilot yaratiladi, birinchi tashkilot
foydalanuvchisi uning studentini/guruhini/to'lovini **hech qanday endpointdan** ololmasligi
tekshiriladi (asosiy 20 endpoint bo'ylab).

### 9.2. CORS, CSP, HSTS (T2, T3) — 1 kun

- CORS: `Cors:AllowedOrigins` konfiguratsiyasi. Bo'sh bo'lsa **faqat Development'da** hammasi
  ochiq; Production'da bo'sh ro'yxat = xato bilan ishga tushmaslik.
- `Content-Security-Policy`: `default-src 'self'` dan boshlab, Next.js uchun kerakli
  `'unsafe-inline'` style va `connect-src` API manziliga. Avval `Report-Only` rejimida
  bir hafta, keyin majburiy.
- `Strict-Transport-Security: max-age=31536000; includeSubDomains` — faqat HTTPS orqasida.
- `Permissions-Policy`: mikrofon **ruxsat etiladi** (Speaking), kamera/geolokatsiya yo'q.

### 9.3. MFA (T4) — 2 kun

TZ §49: "MFA for privileged users".

**Domen:** `User` ga `TotpSecret` (shifrlangan), `MfaEnabledAt`; `MfaRecoveryCode` jadvali.
**Migration `AddMfa`.**

| Endpoint | Nima qiladi |
|---|---|
| `POST /api/auth/mfa/setup` | TOTP siri + QR uchun `otpauth://` URI qaytaradi |
| `POST /api/auth/mfa/enable` | Birinchi kodni tekshiradi, 10 ta tiklash kodi beradi |
| `POST /api/auth/mfa/disable` | Parol + kod talab qiladi |
| `POST /api/auth/login` | MFA yoqilgan bo'lsa `mfaRequired: true` + qisqa muddatli `mfaToken` qaytaradi |
| `POST /api/auth/mfa/verify` | `mfaToken` + 6 raqamli kod → to'liq token juftligi |

`Otp.NET` kutubxonasi. **Majburiy qilish siyosati:** `org.settings` da "Super Admin va Developer
uchun MFA majburiy" bayrog'i (7.1 dagi sozlamalar ekranida).

### 9.4. Sessiya va qurilma boshqaruvi (T5) — 1 kun

**Domen:** `RefreshToken` ga `DeviceName`, `UserAgent`, `IpAddress`, `LastUsedAt`.
**Migration `AddSessionDetails`.**

| Endpoint | Nima qiladi |
|---|---|
| `GET /api/auth/sessions` | Mening faol sessiyalarim |
| `DELETE /api/auth/sessions/{id}` | Bittasini bekor qilish |
| `POST /api/auth/sessions/revoke-all` | Joriydan tashqari hammasini |
| `POST /api/users/{id}/sessions/revoke-all` | `users.deactivate` — administrator majburan chiqaradi |

**Frontend:** `/settings/security` sahifasiga (1.4 da yaratilgan) sessiyalar jadvali.

### 9.5. Obyekt saqlash — S3/MinIO (T6) — 1.5 kun

**Muammo:** barcha fayllar `App_Data/` da → API ni bir nechta nusxada ishga tushirib bo'lmaydi.

`IFileStore` abstraksiyasi (`SaveAsync`, `OpenAsync`, `DeleteAsync`, `ExistsAsync`) —
beshta mavjud storage klassi (`ChatFileStorage`, `ResourceFileStorage`,
`StudentDocumentFileStorage`, `KnowledgeFileStorage`, `SpeakingAudioStorage`) shu interfeysga
o'tadi, ularning validatsiya/nomlash mantig'i o'zgarmaydi.

- `LocalFileStore` — hozirgi xatti-harakat (dev)
- `S3FileStore` — `AWSSDK.S3`, MinIO bilan ham ishlaydi (`ServiceURL` + `ForcePathStyle`)
- `docker-compose.yml` ga `minio` servisi

**Ko'chirish:** bir martalik `dotnet run --migrate-files` buyrug'i `App_Data` dagi hamma narsani
S3'ga ko'chiradi (kalitlar o'zgarmaydi, shuning uchun bazaga tegilmaydi).

### 9.6. Redis (T7) — 1 kun

- **SignalR backplane:** `AddStackExchangeRedis(...)` — ikkinchi API nusxasida chat buzilmasligi uchun
- **Kesh:** `IDistributedCache` — 5.2 dagi og'ir dashboard so'rovlari, `/api/me/permissions`,
  AI limit hisoblagichlari
- `docker-compose.yml` ga `redis` servisi; Redis yo'q bo'lsa xotira keshiga tushadi (dev)

### 9.7. Qidiruv indekslari (T9) — 0.5 kun

**Migration `AddTrigramIndexes`:**
```sql
CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE INDEX ix_users_fullname_trgm ON users USING gin (full_name gin_trgm_ops);
CREATE INDEX ix_users_phone_trgm    ON users USING gin (phone gin_trgm_ops);
CREATE INDEX ix_leads_fullname_trgm ON leads USING gin (full_name gin_trgm_ops);
CREATE INDEX ix_groups_name_trgm    ON groups USING gin (name gin_trgm_ops);
```
`EXPLAIN ANALYZE` bilan oldin/keyin o'lchanadi va natija hujjatga yoziladi.

### 9.8. Backup va Disaster Recovery (T10) — 1.5 kun  🔴

TZ §57 **majburiy** deb belgilagan; hozir hech narsa yo'q.

- `pg_dump` kunlik + `pgBackRest` (yoki WAL arxivi) PITR uchun
- Backup shifrlanadi (`age` yoki `gpg`), tashqi diskka/S3'ga nusxalanadi
- **Restore testi** — oyda bir marta avtomatik: backup'dan vaqtinchalik bazaga tiklash +
  `SELECT count(*)` tekshiruvlari. Test o'tmasa — ogohlantirish
- `docs/12_disaster_recovery.md`: RTO/RPO, qadamma-qadam tiklash yo'riqnomasi, kim mas'ul

### 9.9. CI pipeline (T11) — 0.5 kun

`.github/workflows/ci.yml`:
```yaml
jobs:
  backend:  # postgres+pgvector servisi, dotnet test
  frontend: # npm ci, tsc --noEmit, eslint --max-warnings=0, next build
```
Har `push` va `pull_request` da. Keyinchalik `docker build` ham.

### 9.10. Frontend e2e testlari (T12) — 1.5 kun

Hozir **0 ta**. Playwright (MCP orqali allaqachon ishlatilmoqda, endi repoga):
- `frontend/e2e/` — `login`, `attendance`, `payment`, `lead-to-student`, `student-create`
- `npm run test:e2e`, CI'da headless
- Test bazasi: `dotnet-in-docker.sh` bilan ko'tarilgan API + `Seed:BulkDemoData=false`

Bu **beshta eng qimmat oqim** — ular buzilsa markaz ishlay olmaydi.

### 9.11. README (T13) — 0.5 kun

Loyihani noldan ko'tarish: talablar, `docker compose up`, migratsiya, seed, demo login,
portlar, arxitektura sxemasi, `docs/` ga yo'l xaritasi.

### 9.12. Hangfire dashboard (T14) — 0.5 kun

Dashboard `system.jobs.view` ruxsatini talab qilsin (hozir Development filtri).
`IDashboardAuthorizationFilter` JWT'ni cookie'dan yoki query'dan o'qiydi.

**Bosqich 9 tayyorlik mezoni:** tizimni real markazga qo'yish mumkin —
ma'lumot yo'qolmaydi, tiklash sinalgan, CI har o'zgarishni tekshiradi.

---

## 12. BOSQICH 10 — FAZA 6 (mobil va Telegramdan tashqari)  ·  30+ kun

Bular TZ §58 da Faza 6 deb belgilangan, lekin mobil ham, Telegram ham emas —
shuning uchun rejaga kiradi. Har biri alohida sprint darajasida.

### 10.1. Teacher KPI — sozlanadigan og'irliklar (TZ §6) — 4 kun

**Bog'liqlik:** 4.1 (Grammar/Vocabulary) va 5.1 (teacher workload) tugagan bo'lishi kerak.

**Domen:** `KpiWeights(OrganizationId, Metric, Weight)` — TZ §6 dagi 7 ta mezon:
Student Progress 30%, Attendance 15%, Homework Completion 15%, Punctuality 10%,
Feedback Speed 10%, Retention 10%, Student/Parent Feedback 10%.
**Migration `AddKpiWeights`.**

| Mezon | Manba | Eslatma |
|---|---|---|
| Student Progress | `ExamResult` trendi (guruh o'rtachasining o'sishi) | 4.1 dan keyin to'liq |
| Attendance | `Attendance` guruhlari bo'yicha | ✅ ma'lumot bor |
| Homework Completion | `HomeworkSubmission` / `Homework` | ✅ |
| **Punctuality** | **yangi:** `Lesson` ga `StartedAt` (o'qituvchi davomatni ochgan payt) | Infratuzilma yo'q edi — shu yerda tug'iladi |
| Feedback Speed | `ReviewedAt - SubmittedAt` | ✅ |
| Retention | `Enrollment` — guruhdan ketganlar ulushi | ✅ (1.3 dagi `Dropped` dan keyin haqiqiy) |
| Student/Parent Feedback | 10.2 dan | |

`GET /api/teachers/{id}/kpi`, `GET/PUT /api/settings/kpi-weights` (`kpi.weights.edit` — yangi ruxsat).
Og'irliklar yig'indisi 100 bo'lishi majburiy → 400.

**Frontend:** Teacher 360° ga "KPI" tab (radar diagramma), `/settings/kpi` — og'irliklar.
Org dashboard'ga `teacherKpiAverage` (M7 ning qolgan yarmi).

### 10.2. Feedback / NPS (TZ §41) — 3 kun

**Domen:** `FeedbackSurvey`, `FeedbackResponse(Score 0-10, Comment, TeacherId?, CourseId?, BranchId)`.
**Migration `AddFeedback`.**

- Avtomatik so'rov: kurs tugaganda, har chorakda, support yopilganda (Automation Engine qoidasi)
- NPS hisobi: promoter (9-10) − detractor (0-6)
- Dashboard: filial / o'qituvchi / kurs bo'yicha qanoatlanish (TZ §41)
- Ruxsat: `feedback.submit` (student, parent), `feedback.view` (SuperAdmin, Administrator)

### 10.3. Sertifikatlar (TZ §42) — 3 kun

**Domen:** `Certificate(StudentId, GroupId, CourseId, Number, IssuedAt, IssuedBy, VerificationCode, PdfKey)`.
**Migration `AddCertificates`.**

- Talab tekshiruvi: davomat ≥ X%, yakuniy imtihon ≥ Y ball (kurs darajasida sozlanadi)
- PDF generatsiya: `QuestPDF` (MIT litsenziya, .NET uchun eng qulay)
- QR → `GET /api/certificates/verify/{code}` — **`[AllowAnonymous]`**, faqat "haqiqiy/haqiqiy emas"
  va ism/kurs/sana qaytaradi (boshqa shaxsiy ma'lumot yo'q)
- Ruxsat: `certificates.issue`, `certificates.view`

### 10.4. Gamification (TZ §26) — 8 kun

Eng katta band. Bosqichlarga bo'linadi:

**10.4a — Asos (3 kun):** `StudentXp`, `XpEvent`, `Badge`, `StudentBadge`, `Streak`.
XP manbalari: darsga kelish, uy vazifasini o'z vaqtida topshirish, AI writing/speaking
sessiyasi, imtihon natijasi. Daraja = XP egri chizig'i. Streak = ketma-ket faol kunlar.
**Migration `AddGamification`.**

**10.4b — Challenges (2 kun):** `DailyChallenge`, `WeeklyChallenge` — Automation Engine
generatsiya qiladi, `Leaderboard` (guruh ichida, ixtiyoriy — TZ "optional" deydi;
maxfiylik uchun standart holatda **o'chiq**).

**10.4c — Interaktiv o'yinlar (3 kun):** Word Match, Sentence Builder, Speed Quiz,
Missing Word. Kontent manbai — `VocabularyItem` (kurs/daraja bo'yicha) + Spaced Repetition
(SM-2 algoritmi: `ReviewCard(StudentId, ItemId, EaseFactor, Interval, DueAt)`).
Grammar Battle va Pronunciation Practice — keyingi iteratsiya (birinchisi real-time,
ikkinchisi 10.8 ga bog'liq).

Frontend student uchun alohida ko'rinish — TZ §26: "platforma oddiy admin panel ko'rinishida bo'lmaydi".

### 10.5. HR moduli (TZ §43) — 5 kun

`Employee` (User ustida), `Contract`, `Workload`, `StaffAttendance`, `Vacation`, `SalaryRule`,
`Payroll`. Ruxsat: `hr.view`, `hr.manage`, `salary.view`, `salary.manage`.
O'qituvchi ish haqi: soatbay / oylik / studentdan foiz — `SalaryRule` bilan.
Punctuality (10.1) uchun `StaffAttendance` bu yerdan keladi.

### 10.6. Moliya: xarajatlar va cash flow (TZ §25) — 3 kun

`Expense(BranchId, Category, Amount, PaidAt, Description, ApprovedBy)`,
`ExpenseCategory` (ijara, kommunal, marketing, ish haqi, jihoz).
`GET /api/finance/reports/cashflow` — kirim (to'lovlar) − chiqim (xarajatlar + payroll),
oylik ko'rinishda. Dashboard'ga sof foyda.
Ruxsat: `expenses.view`, `expenses.manage`.

### 10.7. Multi-tenant SaaS (TZ §48) — 5 kun

**Bog'liqlik:** 9.1 (global query filter) va 7.1 (org settings) tugagan bo'lishi shart.

- Tashkilot ro'yxatdan o'tishi (`POST /api/organizations/register` — onboarding sehrgari)
- Tenant bo'yicha AI limitlari (`AiLimits` allaqachon tashkilot darajasida — kengaytiriladi)
- Tenant bo'yicha branding (7.1 dan)
- Tarif rejalari: `Plan(Name, MaxStudents, MaxBranches, AiRequestsPerMonth)`
- Subdomen yoki yo'l bo'yicha ajratish (`{org}.educenter.uz`)
- **Izolyatsiya testi majburiy** — 9.1 dagi test to'plami kengaytiriladi

### 10.8. Advanced Speaking Assessment (TZ §35) — 4 kun

Qolgan mezonlar: Pronunciation, Stress, Rhythm — **xom audiodan**.
- Whisper'ning word-level timestamp'lari (`word_timestamps=true`)
- Forced alignment (`WhisperX` yoki `Montreal Forced Aligner`) → fonema darajasida moslik
- Pronunciation ball: kutilgan fonema bilan aytilgan fonema farqi
- Stress/Rhythm: bo'g'in davomiyligi va urg'u taqsimoti
- Lokal GPU kerak (RTX 4090 mavjud — TZ §36)

---

## 13. XAVFLI JOYLAR VA QARORLAR

Bu rejadagi joylar — ularni bajarishdan **oldin** to'xtab o'ylash kerak:

| # | Xavf | Tavsiya |
|---|---|---|
| 1 | **9.1 global query filter** mavjud 190 endpointning hammasiga ta'sir qiladi | Alohida branch'da, 173 test yashil bo'lgandan keyingina merge. Qo'lda filtrlarni olib tashlamang |
| 2 | **2.2 dars bekor qilish** dashboard hisoblariga ta'sir qiladi ("belgilanmagan davomat") | Har bir hisobga `Status != Cancelled` qo'shilishini alohida tekshiring |
| 3 | **4.1 ExamResult migratsiyasi** — mavjud 500+ natija `null` bilan to'ladi | Bu to'g'ri: eski imtihonlarda Grammar/Vocabulary o'lchanmagan. UI `null` ni "—" deb ko'rsatsin |
| 4 | **6.3 SMS xarajati** — har bir xabar pul | Standart holatda **o'chiq**; tashkilot sozlamalaridan yoqiladi, kunlik limit bilan |
| 5 | **5.2 branch dashboard** og'ir SQL | Avval `EXPLAIN ANALYZE`, keyin kesh. 180 talabada emas, 5000 talabada o'lchang |
| 6 | **1.3 `Dropped` statusi** moliyaga ta'sir qiladi | Ochiq invoice'lar tegilmaydi — bu transfer/freeze bilan bir xil qaror, hujjatga yozilsin |
| 7 | **7.2 ruxsat muharriri** — o'zini qulflab qo'yish | Super Admin `permission.policy.edit` ni o'zidan olib tashlay olmasligi testda tekshirilsin |
| 8 | **10.4 gamification** — maxfiylik | Leaderboard standart holatda o'chiq; bolalar reytingi ota-onalar shikoyatiga sabab bo'ladi |

---

## 14. YANGI RUXSAT KODLARI (jami 17 ta)

`PermissionSeedData.Grants` ga qo'shiladigan kodlar, bosqich bo'yicha:

| Bosqich | Kod | Rollar |
|---|---|---|
| 1.1 | `students.edit` | SA·All, Admin·Branch |
| 1.1 | `students.contact.edit` | SA·All, Admin·Branch, Operator·Branch |
| 1.2 | `parents.manage` | SA·All, Admin·Branch, Operator·Branch |
| 1.3 | `student.enroll` | SA·All, Admin·Branch, Operator·Branch |
| 6.5 | `announcements.view` | hamma rol, scope bilan |
| 6.5 | `announcements.manage` | SA·All, Admin·Branch |
| 6.7 | `complaints.view` | SA·All, Admin·Branch, Operator·Branch |
| 6.7 | `complaints.create` | Student·Self, Parent·MyChildren, Teacher·MyGroups |
| 6.7 | `complaints.manage` | SA·All, Admin·Branch |
| 7.3 | `rooms.manage` | SA·All, Admin·Branch |
| 10.1 | `kpi.view` | SA·All, Admin·Branch, Teacher·Self |
| 10.1 | `kpi.weights.edit` | SA·All |
| 10.2 | `feedback.submit` | Student·Self, Parent·MyChildren |
| 10.2 | `feedback.view` | SA·All, Admin·Branch |
| 10.3 | `certificates.issue` | SA·All, Admin·Branch |
| 10.3 | `certificates.view` | SA·All, Admin·Branch, Student·Self, Parent·MyChildren |
| 10.6 | `expenses.view` / `expenses.manage` | SA·All, Admin·Branch |
| 10.5 | `hr.view` / `hr.manage` / `salary.view` / `salary.manage` | SA·All |

Har bir yangi kod `docs/01_role_permission_matrix.md` ga ham qator qo'shadi.

---

## 15. MIGRATSIYALAR RO'YXATI (jami 23 ta)

```
1.2  AddParentRelationship          6.3  AddNotificationDelivery
1.3  AddEnrollmentEnd               6.4  AddPasswordResetTokens
1.4  AddPasswordChangeTracking      6.5  AddAnnouncements
2.2  AddLessonStatus                6.7  AddComplaints
3.1  AddLessonNotes                 7.1  AddOrganizationSettings
3.2  AddAttendanceNoteAndScore      7.3  AddRooms
3.3  AddHomeworkAttachments         7.4  AddGroupScheduleSlots
3.4  AddHomeworkTargets             7.5  AddInvoiceInstallments
4.1  AddExamGrammarVocabulary       8.3  AddSpeakingRelevance
4.4  AddUserPhoto                   9.3  AddMfa
6.1  AddLeadPreferences             9.4  AddSessionDetails
                                    9.7  AddTrigramIndexes
```
Har biri: `bash backend/scripts/dotnet-in-docker.sh migration <Nom>` → repoga ko'chadi →
`update-db` bilan dev bazaga qo'llanadi.

10-bosqich migratsiyalari (`AddKpiWeights`, `AddFeedback`, `AddCertificates`,
`AddGamification`, HR, `AddExpenses`, multi-tenant) — o'sha bosqich boshlanganda aniqlashtiriladi.

---

## 16. KEYINGI QADAM

**Bugun:** 0-bosqich (commit) — 0.5 kun, hech qanday kod o'zgarmaydi, lekin ikki haftalik
ish xavfsiz joyga tushadi.

**Keyin:** 1-bosqichdan ketma-ket. Har bosqich oxirida testlar yashil, commit qilingan va
`docs/09_audit_progress.md` ga bir band yozilgan bo'lishi kerak.

Tartibni o'zgartirish mumkin, lekin **bog'liqliklar jadvalini** (1-bo'lim) buzmasdan:
4.1 → 4.2 → 5.2 va 6.3 → 6.4 zanjirlari majburiy.
