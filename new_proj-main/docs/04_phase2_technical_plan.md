# PHASE 2 — CRM + LMS
## Sprint darajasidagi texnik reja

**Hujjat turi:** Technical Specification (TZ hujjatining davomi)
**Versiya:** 1.0
**Bog'liq hujjatlar:** `00_TZ.md` (§58.Phase2, §10-§21), `01_role_permission_matrix.md`, `02_database_erd.md`, `03_phase1_technical_plan.md`

**Fazaning maqsadi:** Lead'dan to'liq enrolled studentgacha bo'lgan CRM lifecycle, operator call queue, va asosiy LMS (attendance, homework, exam) — Student 360°/Teacher 360°/Timeline ko'rinishlari uchun poydevor.

---

## 1. Muhim arxitektura qarori: Lead vs Student

TZ §10 lifecycle'iga ko'ra: `LEAD → CONTACTED → TRIAL_BOOKED → TRIAL_ATTENDED → ENROLLED → PAID → ACTIVE`.

**ENROLLED'dan oldin** shaxs hali **Student emas — Lead** (login akkaunti yo'q, faqat CRM yozuvi). Shuning uchun:

- `Lead` — CRM modulida, User/Student'siz mustaqil entity;
- `Student` — faqat enrollment paytida yaratiladi, shu vaqtda `User` akkaunti ham ochiladi (login uchun);
- `Lead.ConvertedStudentId` — konvertatsiya tarixini saqlaydi (Lead o'chirilmaydi, TZ §20 "oldingi tarix o'chirilmaydi" qoidasiga mos).

Modullar orasida to'g'ridan-to'g'ri C# reference yo'q — faqat `Guid` FK orqali (Phase 1'dagi kabi modular monolith qoidasi).

---

## 2. Yangi backend modullari

```
EduCenter.Modules.Crm          — Lead, TrialLesson, CrmCall, CrmTask
EduCenter.Modules.Students     — Student, Parent, StudentParent, Enrollment
EduCenter.Modules.Lms          — Attendance, Homework, HomeworkSubmission, Resource, Exam, ExamResult
EduCenter.Modules.Timeline     — StudentTimelineEvent (CRM + akademik tarixni birlashtiradi)
```

`EduCenter.Modules.Academic` (Phase 1) o'zgarmaydi — `Lesson` (jadval) allaqachon bor, Attendance/Homework/Exam shu Lesson'ga bog'lanadi.

---

## 3. Sprint bo'linishi

### Sprint 1 — CRM Core (Lead lifecycle + Operator Call Queue)
- `Lead`, `CrmCall`, `CrmTask` entitylari + EF konfiguratsiya + migration
- `POST /api/leads`, `GET /api/leads?status=&source=`, `PATCH /api/leads/{id}/status`
- `POST /api/leads/{id}/calls` — call outcome yozish (Answered/No answer/Call later/...), `Call later` → avtomatik `CrmTask` yaratadi (TZ §3.3)
- `GET /api/crm/tasks?assignedTo=me` — Operator uchun **Today's Tasks / Call Queue**
- Permission: `leads.view/create/edit`, `crm.task.view`, `crm.call.log` (Matrix §3.6 — bu fazada haqiqiy kodga aylanadi)

### Sprint 2 — Trial Lesson + Enrollment (Lead → Student konvertatsiyasi)
- `TrialLesson` entity + `POST /api/leads/{id}/trial` (guruhga bog'lash, `schedule.view` bilan mos)
- `Student`, `Parent`, `StudentParent`, `Enrollment` entitylari
- `POST /api/leads/{id}/convert` — Lead'ni Student + User (STUDENT roli) + Enrollment'ga aylantiradi, bitta transaction
- Trial follow-up automation (TZ §10): trial attended + 24 soatda enrollment yo'q → operator task (bu yerda oddiy check endpoint sifatida, to'liq Automation Engine Phase 3'da)

### Sprint 3 — Attendance + Homework
- `Attendance` (Lesson + Student), teacher tomonidan belgilanadi (`attendance.mark`)
- `Homework`, `HomeworkSubmission`, `Resource`
- `POST /api/lessons/{id}/attendance` (bulk — bitta darsdagi barcha studentlar uchun)
- `POST /api/homework`, `POST /api/homework/{id}/submissions`, `PATCH /api/homework/submissions/{id}/review`

### Sprint 4 — Exams + Student/Teacher 360° + Timeline
- `Exam`, `ExamResult` (Listening/Reading/Writing/Speaking/Overall)
- `StudentTimelineEvent` — domain event orqali avtomatik yoziladi (enrollment, payment kelajakda, attendance, homework, exam, support — Phase 2'da mavjud eventlar)
- `GET /api/students/{id}` — Student 360° (profile + tabs uchun asosiy endpoint)
- `GET /api/students/{id}/timeline`
- `GET /api/teachers/{id}` — Teacher 360° (guruhlar, studentlar, KPI — KPI hisoblash Phase 6'da, bu yerda faqat ro'yxatlar)

### Sprint 5 — Frontend: CRM + Student 360°
- Operator Call Queue sahifasi (Today's Tasks, TZ §3.3 misoliga mos)
- Lead ro'yxati + konvertatsiya oqimi
- Student 360° sahifasi (tablar: Overview/Learning/Attendance/Homework/Exams)
- Attendance/Homework kiritish UI (Teacher uchun)

---

## 4. Ma'lumotlar modeli qo'shimchasi

`database_erd_UZ.md` (`02_database_erd.md`) §2-§6 da allaqachon chizilgan — bu fazada shu jadvallar real bo'ladi:

`Lead, TrialLesson, CrmCall, CrmTask, Student, Parent, StudentParent, Enrollment, Attendance, Homework, HomeworkSubmission, Resource, Exam, ExamResult, StudentTimeline`

---

## 5. Muhim qoidalar (TZ'dan)

- **Lead statuslari** (§10): `LEAD, CONTACTED, TRIAL_BOOKED, TRIAL_ATTENDED, ENROLLED, PAID, ACTIVE, NO_ANSWER, CALL_LATER, TRIAL_MISSED, NOT_INTERESTED, LOST, DEBT, FROZEN, TRANSFERRED, GRADUATED, DROPPED` — Lead va Student/Enrollment orasida bo'linadi (Lead: LEAD..LOST; Enrollment: ACTIVE..DROPPED).
- **Call outcome'lar** (§3.3): Answered, No answer, Call later, Interested, Not interested, Payment promised, Trial booked, Wrong number, Transferred, Lost. `Call later` → avtomatik task.
- **Support Needed Engine** (§8) — Phase 2'da faqat signal yozish (attendance 2 ta ketma-ket yo'qlik, homework completion pasayishi); AI-based tavsiya Phase 4'da.
- **Attendance statuslari** (§15): Present, Absent, Late, Excused, Online.

---

## 6. Definition of Done (Phase 2 yakuni)

### 2026-09-07 — amaliy yangilanish

- Lead sahifasida **Trial darslar** oynasi qo'shildi: guruh/vaqtga yozish, triallar ro'yxati, Keldi/Kelmadi. Studentga aylantirilgan leadning trial holatini o'zgartirish backendda bloklanadi.
- Student 360° sahifasiga **Davomat, Homework, Imtihonlar** tablari qo'shildi. Homeworkda topshirish holati, ball va teacher izohi; imtihonda Listening/Reading/Writing/Speaking/Overall ko'rinadi.
- Yangi API: `GET /api/students/{id}/attendance`, `/homework`, `/exams`. Har biri tegishli view permission, studentga access, organization va ko'rinadigan guruhlar bilan cheklangan.
- Tablar faqat ochilganda ma'lumot yuklaydi; loading, bo'sh natija, xato va qayta urinish holatlari mavjud. Homework/davomatdan dars sahifasiga o'tish mumkin.
- Tekshirildi: backend build; frontend TypeScript; o'zgargan frontend fayllarining ESLint tekshiruvi. Alohida `educenter_student360_test_20260907` bazasida `LmsTests`, `Sprint4Tests`, `LeadConversionTests`: **4/4 integration test o'tdi**. O'z profilini ko'rish, boshqa studentga deny, teacherning boshqa guruh natijalarini ko'ra olmasligi va konvertatsiyadan keyingi trial guard tekshirildi. Development bazasi test uchun ishlatilmadi.
- Cheklov: brauzerda vizual tekshiruv hali bajarilmadi. Guruhlar uchun amaldagi scope qoidalari saqlangan: Student/Parent faqat faol enrollment guruhlarini ko'radi. Homework ro'yxati mavjud group-enrollment modeliga asoslangan; enrollment davri bo'yicha tarixiy assignment chegaralari alohida ish.
- Hali qolgan: Learning/progress tab, Resources, Teacher 360° UI, trial follow-up, support signallari va umumiy Phase 2 qabul tekshiruvi. Quyidagi umumiy checklist shu ishlar yopilganda qayta baholanadi.

### Teacher 360° — asosiy profil interfeysi

- `/teachers`: asosiy va yordamchi teacher ro'yxati, ism/email qidiruvi, holat. `/teachers/{id}`: Guruhlar, Studentlar va Jadval tablari. Student va dars sahifalariga havolalar mavjud.
- `GET /api/teachers` scope bilan filtrlanadi; teacher/support o'zini, administrator o'z filialiga biriktirilgan teacherlarni ko'radi. Profil endpointida teacher roli va filial tekshiruvlari qo'shildi.
- Profil studentlari faqat ko'rinadigan guruhlardan olinadi. Umumiy student soni noyob studentlar bo'yicha hisoblanadi; guruhlar kesimida studentlar alohida ko'rsatiladi.
- Tekshiruv: TypeScript, yangi teacher sahifalarining lint tekshiruvi, backend build o'tdi (NuGet feed SSL warninglari bor). Alohida bazada `Sprint4Tests` 2/2 o'tdi; teacher self-list va administrator cross-branch deny regressiyasi qo'shildi. Mahalliy backend qayta ishga tushirildi; teacher list/profile API va `/teachers` sahifasi HTTP 200 bilan tekshirildi. Vizual brauzer tekshiruvi bajarilmadi.
- KPI, Performance, Feedback, Chat va teacher profilining qolgan kengaytirilgan tablari hali bajarilmagan.

### Resources tafsilotlari

- Dars sahifasida **Materiallar** tabi: sarlavha, izoh va HTTP/HTTPS havola qo'shish, materiallarni yangi oynada ochish. Fayl upload/storage, materialni tahrirlash/o'chirish va alohida umumiy kutubxona hali qolgan.
- `LearningResource` modeli, `learning_resources` jadvali va `AddLearningResources` migratsiyasi qo'shildi. Migratsiya alohida test bazasi hamda mahalliy `educenter` development bazasiga qo'llandi.
- `GET /api/resources?lessonId=&groupId=`, `POST /api/resources`. Organization va guruh scope tekshiriladi; URL faqat HTTP/HTTPS, login/parol kiritilgan URL rad etiladi. Server havoladagi faylni yuklab olmaydi.
- `resources.view` va `resources.create` permissionlari seed va permission matritsasiga qo'shildi. Yangi permissionlar mavjud development bazasiga backend Development rejimida qayta ishga tushganda seed qilinadi.
- Tekshiruv: kengaytirilgan `LmsTests` 1/1 alohida test bazasida o'tdi (teacher yaratishi, student ko'rishi, student create deny, boshqa guruh teacher view/create cheklovi, xavfli URL rad etilishi). TypeScript va yangi Resources komponenti/API wrapper lint tekshiruvi o'tdi. Backend build o'tdi; NuGet vulnerability feed SSL sabab NU1900 warninglari mavjud. Dars sahifasidagi oldingi ikki `set-state-in-effect` lint xatosi saqlangan. Brauzer tekshiruvi bajarilmadi.

### Imtihonlar interfeysi tafsilotlari

- `/exams` sahifasi va rolga mos menyu qo'shildi: guruh filtri, imtihon ro'yxati, yaratish oynasi, student natijalari.
- Super Admin, Administrator va Teacher imtihon yaratadi hamda faol guruh studentlariga ball kiritadi. Operator, Support Teacher, Student va Parent uchun ko'rish rejimi; haqiqiy access backend permission/scope orqali tekshiriladi.
- Listening/Reading/Writing/Speaking/Overall ballari 0–9 oralig'ida; baholanmagan bo'lim `null`. Kamida bitta ball majburiy. Overall qo'lda kiritiladi. Mavjud API doirasida natija bir marta yaratiladi; oldingi bahoni tahrirlash alohida ish.
- Saqlanganda natijalar, Student 360° va Timeline query keshlari yangilanadi. Yuklanish, bo'sh natija va xato holatlari mavjud.
- Backendda sana/tur va bo'sh ball validatsiyasi qo'shildi; Timeline payload standart JSON serializer bilan yoziladi.
- Tekshiruv: backend build (0 warning, 0 error), TypeScript va o'zgargan frontend fayllarining ESLint tekshiruvi o'tdi. Alohida test bazasida `Sprint4Tests` **2/2 o'tdi**: natija oqimi, takroriy/bo'sh/diapazondan tashqari ballni rad etish, Student create/score deny. Brauzerda vizual tekshiruv bajarilmadi.

### 2026-09-15 — Support Needed Engine, follow-up avtomatikasi va Phase 2 qabul tekshiruvi

**Support Needed Engine (TZ §8)**
- `SupportCase` (`support_cases`): `Recommended → Assigned → Resolved`, yoki ko'rib chiqishda `Dismissed`. TZ'dagi "Approve" va "Assign Support Teacher" bitta amal: administrator yordamchi o'qituvchini tanlab tasdiqlaydi.
- Signallar (qoidaga asoslangan; AI signallari Phase 4):
  - `CONSECUTIVE_ABSENCES` — studentning guruhdagi oxirgi 2 ta belgilangan darsi ham `Absent`. Davomat saqlangan zahoti tekshiriladi.
  - `HOMEWORK_MISSED` — guruhga qo'shilganidan beri muddati o'tgan oxirgi 3 ta homeworkdan ko'pi bilan 1 tasi topshirilgan. Fon sweep'da tekshiriladi.
  - `STAFF_PROPOSAL` — Teacher yoki Administrator Student 360° sahifasidan qo'lda tavsiya qiladi.
- Yordamchi o'qituvchi faqat case guruhiga `SupportTeacher` sifatida biriktirilgan xodimlardan tanlanadi (TZ §3.5 — support guruh assignment'i).
- API: `GET /api/support-cases?status=&studentId=`, `POST /api/support-cases`, `GET /{id}/candidates`, `POST /{id}/assign`, `/dismiss`, `/resolve`. Scope case guruhi orqali: Administrator — filial, Teacher — o'z guruhlari, Support Teacher — biriktirilgan guruhlari va o'ziga berilgan case'lar.
- Permissionlar: `support.recommend.view`, `support.propose` (yangi), `support.assign` — matritsa §3.9 yangilandi.
- Timeline: `SUPPORT_RECOMMENDED`, `SUPPORT_ASSIGNED`, `SUPPORT_DISMISSED`, `SUPPORT_RESOLVED`.
- UI: `/support` (Ko'rib chiqish kerak / Jarayonda / Yopilgan), Student 360° **Support** tabi (tavsiya qilish, biriktirish, rad etish, yakunlash).

**CRM follow-up avtomatikasi (TZ §10)**
- **Trial follow-up:** trial `Attended`, 24 soat o'tdi, lid konvertatsiya qilinmagan → lid operatoriga `FollowUp` vazifa (`TRIAL_FOLLOW_UP`) + `Lead.Priority = High` (lidlar ro'yxatida "Muhim").
- **Absence follow-up:** ketma-ket 2 yo'qlik → `Absence` vazifa (`CONSECUTIVE_ABSENCES`) + support tavsiyasi. Vazifa oluvchi: studentni konvertatsiya qilgan operator → filial operatori → filial administratori.
- Teacher/parent/operator **bildirishnomalari Phase 3** (Notifications/Telegram) — bu yerda faqat doimiy qism (vazifa, tavsiya) yaratiladi.
- Takrorlanmaslik: har qoida *trigger vaqti* bilan tekshiriladi (oxirgi yo'qlik belgilangan payt, eng yangi o'tgan muddat, trial vaqti). Shu paytdan keyin yaratilgan case/vazifa bo'lsa — hatto rad etilgan yoki bajarilgan bo'lsa ham — qoida jim turadi. Faqat yangi trigger qayta ishga tushiradi.
- `FollowUpRules` + `AutomationSweepService` (`BackgroundService`, har `Automation:SweepIntervalSeconds`=300 soniyada; `Automation:Enabled=false` bilan o'chiriladi). Bu Phase 3 Automation Engine'ning boshlang'ich nuqtasi.
- `CrmTask`ga `ReasonCode` + `Note` qo'shildi; `GET /api/crm/tasks` endi har vazifa kim haqida ekanini (`SubjectName/SubjectPhone` — liddan yoki studentdan) qaytaradi.
- **Tuzatilgan bug:** Administrator (MyBranches) Call Queue'da faqat lidga bog'langan vazifalarni ko'rardi — studentga bog'langan vazifalar (yo'qlik, qarz) umuman chiqmasdi. `Complete` endpointi ham shu scope bilan cheklandi.

**Timeline — CRM tarixi birlashtirildi (TZ §9)**
- Student 360° "Tarix" endi lid bosqichini ham ko'rsatadi: `LEAD_CREATED` (manba), har bir `CRM_CALL` (natija, izoh), har bir `TRIAL_LESSON` (guruh, kelgan/kelmagan). Ma'lumot lidning o'z yozuvlaridan o'qiladi (nusxa ko'chirilmaydi).

**Migratsiya:** `AddSupportCasesAndFollowUps` (`support_cases`; `crm_tasks.ReasonCode/Note`; `leads.Priority` default `Normal`).

**Tekshiruv:** 24/24 test (2 unit + 22 integration, alohida `educenter_test` bazasida). Yangi `SupportAndFollowUpTests`:
- ikki yo'qlik → tavsiya + operator vazifasi (administrator ham ko'radi) → qayta saqlash va sweep takrorlamaydi → Teacher biriktira olmaydi, takroriy tavsiya 409 → Administrator biriktiradi → timeline → Support Teacher yopadi → sweep yopilgan case'ni qayta ochmaydi;
- trial 24 soat → bitta follow-up + `Priority=High`, konvertatsiya qilingan lid uchun vazifa yo'q, timeline'da `LEAD_CREATED`/`TRIAL_LESSON`;
- Student/Operator uchun `/api/support-cases` → 403.

Dev bazada jonli tekshirildi: backend ishga tushganda sweep seed'dagi Javlon Karimov (IELTS-21, 2 ketma-ket yo'qlik) uchun tavsiya va operator vazifasini yaratdi; Administrator nomzodlar ro'yxatida "Support Teacher Demo" chiqdi.

**Phase 2 doirasidan tashqarida qolganlar:** bildirishnomalar (Phase 3), AI signallari (Phase 4), resource fayl yuklash va imtihon natijasini tahrirlash, Teacher KPI (Phase 6), student transfer/freeze (`students.transfer/freeze` — TZ §20–21), brauzerda vizual qabul tekshiruvi (foydalanuvchi tomonidan).

### Definition of Done — holat (2026-09-15)

- [x] Operator login qilib, Today's Tasks/Call Queue'ni ko'radi va call natijasini yozadi — `/crm/tasks`, `/leads`; `CrmTests.LoggingCallLaterOutcome_AutomaticallyCreatesFollowUpTask`;
- [x] `Call later` tanlanganda yangi task avtomatik yaratiladi — o'sha test;
- [x] Lead trial darsga yoziladi va keyin Student + User + Enrollment'ga konvertatsiya qilinadi (bitta amal, tranzaksion) — `TrialDrawer`, `LeadsController.Convert` (bitta `SaveChangesAsync`); `LeadConversionTests.ConvertedLead_ProducesAWorkingStudentLogin`;
- [x] Teacher darsda attendance va homework belgilay oladi — `/schedule/[lessonId]`; `LmsTests.FullLessonLifecycle_AttendanceHomeworkSubmissionReview_WorksEndToEnd`;
- [x] Student 360° sahifasida profil + attendance + homework + exam natijalari ko'rinadi — `/students/[id]` tablari, `GET /api/students/{id}/overview|attendance|homework|exams`; `Sprint4Tests.ExamResult_AppearsInStudentOverviewAndTimeline_AndIsVisibleToTheStudentThemself`;
- [x] Har bir muhim CRM/akademik hodisa Student Timeline'da ko'rinadi — akademik (`ENROLLED`, `LESSON_*`, `HOMEWORK_*`, `EXAM_RESULT_RECORDED`), support (`SUPPORT_*`) va lid bosqichi (`LEAD_CREATED`, `CRM_CALL`, `TRIAL_LESSON`); `SupportAndFollowUpTests`;
- [x] `01_role_permission_matrix.md`dagi CRM (§3.6) va LMS (§3.5) bo'limlaridagi barcha permission'lar kodda ishlaydi — har bir kod seed'da bor va kamida bitta API policy'da ishlatiladi (2026-09-15 avtomatik solishtirish).

**Phase 2 — CRM + LMS: yopildi.** Keyingi bosqich: Phase 3 (Finance → Automation Engine → Notifications/Telegram → Chat).
