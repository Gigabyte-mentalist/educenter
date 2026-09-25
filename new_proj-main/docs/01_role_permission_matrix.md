# ROLE & PERMISSION MATRIX
## Barcha permissionlarning to'liq jadvali

**Hujjat turi:** Technical Specification (TZ hujjatining davomi)
**Versiya:** 1.0
**Bog'liq hujjat:** `00_TZ.md` (§3, §4)

---

## 1. Model asoslari

Ruxsatlar uchta o'lchamda aniqlanadi: **Permission** (nima qilish mumkin), **Scope** (qaysi ma'lumotlar doirasida) va **Resource** (qaysi obyekt ustida). `students.view` va `students.edit` alohida permission — ko'rish huquqi tahrirlash huquqini bermaydi.

### Scope darajalari

| Scope kodi | Ma'nosi |
|---|---|
| `ALL_ORGANIZATION` | Tashkilotdagi barcha filiallar |
| `MY_BRANCHES` | Foydalanuvchiga biriktirilgan filial(lar) |
| `MY_GROUPS` | Foydalanuvchi asosiy teacher bo'lgan guruhlar |
| `ASSIGNED_GROUPS` | Foydalanuvchi support teacher sifatida biriktirilgan guruhlar |
| `SELF` | Faqat o'ziga tegishli ma'lumot |
| `MY_CHILDREN` | Parent'ning farzandlariga tegishli ma'lumot |
| `NONE` | Default holatda ruxsat yo'q (explicit grant kerak) |

### Permission action turlari

`view`, `create`, `edit`, `delete`, `approve`, `assign`, `export`. Har bir resurs uchun bu action'larning barchasi mavjud bo'lavermaydi — quyidagi jadvalda faqat mantiqiy bo'lganlari ko'rsatilgan.

---

## 2. Rol qisqartmalari

| Kod | Rol |
|---|---|
| SA | Super Admin |
| AD | Administrator / Branch Manager |
| OP | Operator / Reception |
| TC | Teacher (main) |
| ST | Support Teacher |
| SD | Student |
| PR | Parent |
| DV | Developer |

---

## 3. Modul bo'yicha permission jadvali

Format: `action → scope`. Bo'sh katak = ruxsat yo'q.

### 3.1. Organization / Branch

| Permission | SA | AD | OP | TC | ST | SD | PR | DV |
|---|---|---|---|---|---|---|---|---|
| `branch.view` | ALL_ORG | MY_BRANCHES | MY_BRANCHES | MY_BRANCHES | ASSIGNED_GROUPS | — | — | — |
| `branch.create` | ✓ | — | — | — | — | — | — | — |
| `branch.edit` | ✓ | MY_BRANCHES (limited) | — | — | — | — | — | — |
| `org.settings.edit` (markaz nomi, logo, brend rangi, valyuta, ish vaqti — `/settings/organization`) | ✓ | — | — | — | — | — | — | — |
| `dashboard.teachers.view` (teacher league table on a branch's dashboard) | ALL_ORG | MY_BRANCHES | — | — | — | — | — | — |

### 3.2. Users & Staff

| Permission | SA | AD | OP | TC | ST | SD | PR | DV |
|---|---|---|---|---|---|---|---|---|
| `users.view` | ALL_ORG | MY_BRANCHES | MY_BRANCHES | MY_GROUPS (peers) | ASSIGNED_GROUPS | SELF | MY_CHILDREN | — |
| `users.create` | ✓ | MY_BRANCHES (operator/teacher) | — | — | — | — | — | — |
| `users.edit` | ✓ | MY_BRANCHES | — | SELF | SELF | SELF | SELF | — |
| `users.deactivate` | ✓ | MY_BRANCHES | — | — | — | — | — | — |
| `users.password.reset` | ✓ | MY_BRANCHES | — | — | — | — | — | — |
| `role.assign` | ✓ | MY_BRANCHES (limited roles) | — | — | — | — | — | — |
| `permission.policy.edit` (ruxsatlar matritsasi muharriri — `/settings/permissions`; boshqa rolga berib bo'lmaydi) | ✓ | — | — | — | — | — | — | — |
| `teacher.portfolio.view` (teacher's IELTS, certificates, experience) | ALL_ORG | MY_BRANCHES | — | SELF | SELF | — | — | — |
| `teacher.portfolio.edit` | ALL_ORG | MY_BRANCHES (teacher/support teacher only) | — | SELF | SELF | — | — | — |
| `teacher.portfolio.verify` (confirm a certificate) | ALL_ORG | MY_BRANCHES | — | — | — | — | — | — |
| `teacher.portfolio.view.public` (confirmed facts of the teacher of one's own group; no scan/reference) | — | — | — | — | — | SELF | MY_CHILDREN | — |

### 3.3. Students

| Permission | SA | AD | OP | TC | ST | SD | PR | DV |
|---|---|---|---|---|---|---|---|---|
| `students.view` | ALL_ORG | MY_BRANCHES | MY_BRANCHES | MY_GROUPS | ASSIGNED_GROUPS | SELF | MY_CHILDREN | — |
| `students.create` | ✓ | ✓ | ✓ | — | — | — | — | — |
| `students.edit` | ✓ | ✓ | — | — | — | — | — | — |
| `students.contact.edit` | ✓ | ✓ | ✓ | — | — | — | — | — |
| `students.delete` | ✓ | — | — | — | — | — | — | — |
| `parents.manage` (link/unlink a guardian account) | ALL_ORG | MY_BRANCHES | MY_BRANCHES | — | — | — | — | — |
| `students.transfer` | ✓ | ✓ (initiate) | ✓ (initiate) | — | — | — | — | — |
| `students.freeze` | ✓ | ✓ (approve) | ✓ (request) | — | — | — | — | — |
| `student.enroll` (2nd group, drop, graduate) | ALL_ORG | MY_BRANCHES | MY_BRANCHES | — | — | — | — | — |

### 3.4. Groups / Courses / Schedule

| Permission | SA | AD | OP | TC | ST | SD | PR | DV |
|---|---|---|---|---|---|---|---|---|
| `groups.view` | ALL_ORG | MY_BRANCHES | MY_BRANCHES | MY_GROUPS | ASSIGNED_GROUPS | SELF (own group) | MY_CHILDREN | — |
| `groups.create` | ✓ | ✓ | — | — | — | — | — | — |
| `groups.edit` | ✓ | ✓ | — | — | — | — | — | — |
| `groups.delete` | ✓ | ✓ | — | — | — | — | — | — |
| `courses.manage` | ✓ | ✓ | — | — | — | — | — | — |
| `schedule.view` | ALL_ORG | MY_BRANCHES | MY_BRANCHES | MY_GROUPS | ASSIGNED_GROUPS | SELF | MY_CHILDREN | — |
| `schedule.edit` | ✓ | ✓ | — | — | — | — | — | — |
| `rooms.manage` (xona qo'shish/tahrirlash/nofaol qilish; ko'rish — `schedule.view`) | ✓ | MY_BRANCHES | — | — | — | — | — | — |
| `teacher.replace` | ✓ | ✓ | — | — | — | — | — | — |

### 3.5. Attendance / Homework / Exams (LMS)

| Permission | SA | AD | OP | TC | ST | SD | PR | DV |
|---|---|---|---|---|---|---|---|---|
| `attendance.view` | ALL_ORG | MY_BRANCHES | MY_BRANCHES | MY_GROUPS | ASSIGNED_GROUPS | SELF | MY_CHILDREN | — |
| `attendance.mark` | ✓ | ✓ | — | MY_GROUPS | — | — | — | — |
| `homework.view` | ALL_ORG | MY_BRANCHES | — | MY_GROUPS | ASSIGNED_GROUPS | SELF | MY_CHILDREN | — |
| `homework.create` | ✓ | ✓ | — | MY_GROUPS | ASSIGNED_GROUPS | — | — | — |
| `homework.review` | ✓ | ✓ | — | MY_GROUPS | ASSIGNED_GROUPS | — | — | — |
| `homework.submit` | — | — | — | — | — | SELF | — | — |
| `exams.create` | ✓ | ✓ | — | MY_GROUPS | — | — | — | — |
| `exams.score.edit` | ✓ | ✓ | — | MY_GROUPS | — | — | — | — |
| `exams.view` | ALL_ORG | MY_BRANCHES | MY_BRANCHES | MY_GROUPS | ASSIGNED_GROUPS | SELF | MY_CHILDREN | — |
| `resources.view` | ALL_ORG | MY_BRANCHES | MY_BRANCHES | MY_GROUPS | ASSIGNED_GROUPS | SELF | MY_CHILDREN | — |
| `resources.create` | ALL_ORG | MY_BRANCHES | — | MY_GROUPS | ASSIGNED_GROUPS | — | — | — |

### 3.6. CRM (Leads, Trial, Calls)

| Permission | SA | AD | OP | TC | ST | SD | PR | DV |
|---|---|---|---|---|---|---|---|---|
| `leads.view` | ALL_ORG | MY_BRANCHES | MY_BRANCHES | — | — | — | — | — |
| `leads.create` | ✓ | ✓ | ✓ | — | — | — | — | — |
| `leads.edit` | ✓ | ✓ | ✓ | — | — | — | — | — |
| `trial.book` | ✓ | ✓ | ✓ | — | — | — | — | — |
| `crm.task.view` | ALL_ORG | MY_BRANCHES | SELF-assigned | — | — | — | — | — |
| `crm.call.log` | ALL_ORG | MY_BRANCHES | MY_BRANCHES | — | — | — | — | — |

### 3.7. Finance

| Permission | SA | AD | OP | TC | ST | SD | PR | DV |
|---|---|---|---|---|---|---|---|---|
| `payments.view` | ALL_ORG | MY_BRANCHES | — | — | — | SELF | MY_CHILDREN | — |
| `finance.debt.view` | ALL_ORG | MY_BRANCHES | MY_BRANCHES | — | — | — | — | — |
| `payments.create` | ✓ | ✓ | — | — | — | — | — | — |
| `payments.edit` | ✓ | ✓ (limited) | — | — | — | — | — | — |
| `discount.apply` | ✓ | ✓ (limited %) | — | — | — | — | — | — |
| `promo.manage` | ALL_ORG | — | — | — | — | — | — | — |
| `refund.approve` | ✓ | ✓ (limited amount) | — | — | — | — | — | — |
| `finance.reports.view` | ALL_ORG | MY_BRANCHES | — | — | — | — | — | — |

> **2026-09-15 (Finance Sprint 2):** "limited" chegaralari rol nomi bilan emas, scope bilan ishlaydi — permission `ALL_ORG` bilan berilgan bo'lsa chegara yo'q, `MY_BRANCHES` bilan berilgan bo'lsa `Finance:AdministratorMaxDiscountPercent` (standart 20%) va `Finance:AdministratorMaxRefundAmount` (standart 1 000 000 so'm) qo'llanadi. `promo.manage` — oldindan tasdiqlangan promo-kodlarni yaratish; ularni `discount.apply` egasi qo'llaydi va promo-kodga filial chegarasi qo'llanmaydi.

### 3.8. Communication / Chat

| Permission | SA | AD | OP | TC | ST | SD | PR | DV |
|---|---|---|---|---|---|---|---|---|
| `chat.1to1.use` | ✓ | ✓ | ✓ | ✓ (own students) | ✓ (assigned) | ✓ (own teacher) | ✓ (permission-gated) | — |
| `chat.group.use` | ✓ | ✓ | — | MY_GROUPS | ASSIGNED_GROUPS | SELF (own group) | — | — |
| `chat.moderate` | ✓ | MY_BRANCHES | — | MY_GROUPS | — | — | — | — |
| `staff.channels.access` | ✓ | ✓ | ✓ | ✓ | ✓ | — | — | — |
| `announcements.view` (e'lonlar: o'z filiali, guruhlari yoki farzandlariga tegishlilari) | ALL_ORG | MY_BRANCHES | MY_BRANCHES | MY_GROUPS | ASSIGNED_GROUPS | SELF | MY_CHILDREN | — |
| `announcements.manage` (e'lon chiqarish, tahrirlash, o'chirish) | ALL_ORG | MY_BRANCHES | — | — | — | — | — | — |

> **2026-09-25 (S06.05):** e'lon bir tashkilotga (`branchId` va `groupId` bo'sh), bitta filialga yoki bitta guruhga qaratiladi va auditoriyasi bor (hamma / studentlar / ota-onalar / xodimlar). Administrator tashkilot bo'yicha e'lon bera olmaydi — faqat o'z filiali yoki guruhiga (`announcements.manage` scope'i `MY_BRANCHES`). `announcements.manage` egasi o'z scope'idagi barcha e'lonlarni auditoriyasidan qat'i nazar ko'radi; qolganlar faqat o'z auditoriyasiga (student → studentlar, ota-ona → ota-onalar, xodim → xodimlar) yo'naltirilganini va muddati o'tmaganini.

> **2026-09-15 (Phase 3 Sprint 6):** kodda scope bilan: `chat.1to1.use` — SA `ALL_ORG`, AD/OP `MY_BRANCHES` (filial xodimlari, studentlari va ularning ota-onalari), TC `MY_GROUPS` (o'z studentlari, ota-onalari va filialdagi hamkasblar), ST `ASSIGNED_GROUPS` (biriktirilgan studentlar va hamkasblar), SD `SELF` (o'z guruhlarining o'qituvchilari), PR `MY_CHILDREN` (farzandlarining asosiy o'qituvchilari va filial administratorlari). `chat.group.use`/`chat.moderate` — guruh scope'i; AD uchun `MY_BRANCHES`. `staff.channels.access` — SA `ALL_ORG`, qolganlar `MY_BRANCHES`; kanal auditoriyasi (O'qituvchilar, IELTS o'qituvchilari, Administratorlar) kanal katalogida rol bo'yicha, «E'lonlar»ga faqat filialni qamragan `chat.moderate` egasi yozadi. Direct chatda moderatsiya yo'q.

### 3.9. Support & Complaints

| Permission | SA | AD | OP | TC | ST | SD | PR | DV |
|---|---|---|---|---|---|---|---|---|
| `support.recommend.view` | ALL_ORG | MY_BRANCHES | — | MY_GROUPS | ASSIGNED_GROUPS | — | — | — |
| `support.propose` | ALL_ORG | MY_BRANCHES | — | MY_GROUPS | — | — | — | — |
| `support.assign` | ALL_ORG | MY_BRANCHES | — | — | — | — | — | — |
| `complaints.create` (shikoyat yuborish) | — | MY_BRANCHES | MY_BRANCHES | MY_GROUPS | — | SELF | MY_CHILDREN | — |
| `complaints.view` (filial shikoyatlarini o'qish; muallif o'zinikini har doim ko'radi) | ALL_ORG | MY_BRANCHES | MY_BRANCHES | — | — | — | — | — |
| `complaints.manage` (xodimga biriktirish, holat, yechim, yopish) | ALL_ORG | MY_BRANCHES | — | — | — | — | — | — |

> **2026-09-25 (S06.07):** oldingi `complaint.view` / `complaint.resolve` qatorlari kodga hech qachon tushmagan edi; ular uchta ruxsatga ajratildi. Shikoyat — mijozning noroziligi (`SupportCase` bu emas: u o'qishda qiynalayotgan student signali). Yuborish (`create`) — student (o'zi haqida), ota-ona (farzandi haqida), o'qituvchi, operator va administrator (mijoz nomidan); Super Admin o'zi shikoyat yozmaydi, u butun tashkilot doskasini boshqaradi. O'qish (`view`) — SA butun tashkilotniki, AD/OP o'z filiallariniki; `create` egasi `view`siz ham **o'z** shikoyatlarini ko'radi va baholaydi. Biriktirish/yopish — `manage` (SA/AD); biriktirilgan xodim (`view` bilan, masalan operator) o'ziga berilgan shikoyatni boshlay va yecha oladi, lekin boshqasiga tegmaydi. Holat faqat oldinga: `New → Assigned → InProgress → Resolved → Closed`. Talaba tarixi (timeline) o'qituvchi, student va ota-onaga ham ko'rinadi, shuning uchun shikoyat hodisalari (`COMPLAINT_CREATED/RESOLVED`) unda faqat `complaints.view` egasiga ko'rsatiladi.

> **2026-09-15:** oldingi `support.assign` qatoridagi "Teacher ✓ (propose)" alohida `support.propose` permissioniga ajratildi — Teacher tavsiya bera oladi, lekin yordamchi o'qituvchini faqat Super Admin / Administrator biriktiradi yoki rad etadi (TZ §8: "Teacher/Admin reviews → Approve → Assign"). Biriktirilgan case'ni o'sha Support Teacher o'zi `support.recommend.view` bilan yopadi (`POST /api/support-cases/{id}/resolve`, faqat o'ziga biriktirilgan bo'lsa).

### 3.10. Analytics / KPI / Audit

| Permission | SA | AD | OP | TC | ST | SD | PR | DV |
|---|---|---|---|---|---|---|---|---|
| `analytics.branch.view` | ALL_ORG | MY_BRANCHES | — | — | — | — | — | — |
| `analytics.global.view` | ✓ | — | — | — | — | — | — | — |
| `kpi.teacher.view` | ALL_ORG | MY_BRANCHES | — | SELF | — | — | — | — |
| `kpi.weights.edit` | ✓ | — | — | — | — | — | — | — |
| `audit.log.view` | ✓ | MY_BRANCHES (own actions scope) | — | — | — | — | — | — |

### 3.11. AI Platform

| Permission | SA | AD | OP | TC | ST | SD | PR | DV |
|---|---|---|---|---|---|---|---|---|
| `ai.tutor.use` | — | — | — | — | — | SELF | — | — |
| `ai.writing.use` | — | — | — | — | — | SELF (submit) | — | — |
| `ai.writing.review` | ALL_ORG | MY_BRANCHES | — | MY_GROUPS | ASSIGNED_GROUPS | — | — | — |
| `ai.writing.override` | ALL_ORG | MY_BRANCHES | — | MY_GROUPS | — | — | — | — |
| `ai.speaking.use` | — | — | — | — | — | SELF | — | — |
| `ai.speaking.review` | ALL_ORG | MY_BRANCHES | — | MY_GROUPS | ASSIGNED_GROUPS | — | — | — |
| `ai.speaking.override` | ALL_ORG | MY_BRANCHES | — | MY_GROUPS | — | — | — | — |
| `ai.knowledge.manage` | ALL_ORG | MY_BRANCHES | — | MY_GROUPS | — | — | — | — |
| `ai.usage.view` | ALL_ORG | MY_BRANCHES | — | — | — | — | — | — |
| `ai.limits.edit` | ALL_ORG | — | — | — | — | — | — | — |

> **2026-09-15 (Phase 4 Sprint 1):** oldingi `ai.writing.use` qatoridagi "TC/ST ✓ (review)" alohida `ai.writing.review` permissioniga ajratildi — bitta kod studentga "yuborish", o'qituvchiga "ko'rish" ma'nosini rol nomi bilan tekshirmasdan bera olmaydi (4-qoida 6). Matritsada bo'lmagan `ai.speaking.use` (Speaking MVP) va `ai.knowledge.manage` (Knowledge Base'ga material yuklash, TZ §38) qo'shildi. `ai.limits.edit` — kunlik limitlarni o'zgartirish va provider ulanishini tekshirish. Batafsil: `06_phase4_technical_plan.md` §2.

> **2026-09-24 (S04.03):** `ai.speaking.review` — o'qituvchi/administrator student profilining Speaking tabida yakunlangan mashqni ko'radi: baho, transkript va ovoz yozuvi (`GET /api/students/{id}/speaking/...`). Speaking mashqi avval faqat studentning o'ziga ko'rinardi; mahsulot qarori bilan xodimga ochildi, student mashq sahifasida ogohlantiriladi. **Faqat ogohlantirishdan keyin boshlangan mashqlar** ko'rinadi (`SpeakingSession.SharedWithStaff`) — eski yozuvlar "xodimga ko'rinmaydi" va'dasi bilan olingan, shuning uchun shaxsiy qoladi. Operator, Parent va Student'da yo'q.
> `ai.speaking.override` — yozuvni eshitgan o'qituvchi AI bahosini tuzatadi (`ai.writing.override` bilan bir xil doira: yordamchi o'qituvchi o'qiydi, lekin baho qo'ymaydi). O'qituvchi bandi AI bandining yonida saqlanadi (AI bahosi o'zgarmaydi), student unga bildirishnoma oladi.

### 3.12. Developer / Observability

| Permission | SA | AD | OP | TC | ST | SD | PR | DV |
|---|---|---|---|---|---|---|---|---|
| `system.health.view` | — | — | — | — | — | — | — | ✓ |
| `logs.view` | — | — | — | — | — | — | — | ✓ |
| `system.metrics.view` | — | — | — | — | — | — | — | ✓ |
| `system.jobs.view` | — | — | — | — | — | — | — | ✓ |
| `production.data.request` | — | — | — | — | — | — | — | ✓ (approval kerak) |
| `production.data.approve` | ALL_ORG | — | — | — | — | — | — | — |
| `deployments.view` | ✓ | — | — | — | — | — | — | ✓ |

> **2026-09-21:** TZ §3.8/§53 ro'yxatining qolgan qismi (so'rov kechikishi p50/p95/p99, xato ulushi, sekin so'rovlar, disk, fon vazifalari, server xatolari, versiya) `system.metrics.view`, `system.jobs.view`, `logs.view`, `deployments.view` orqali ochildi — hammasi faqat o'qish va talaba ma'lumotini o'z ichiga olmaydi. `production.data.request` endi haqiqiy oqim: Developer bitta o'qish ruxsatini (`students.view`, `groups.view`, `attendance.view`, `exams.view`, `homework.view` — moliya va hisoblar **hech qachon**) sabab (≥20 belgi) va muddat (1-72 soat) bilan so'raydi, `production.data.approve` egasi (Super Admin) tasdiqlaydi/rad etadi/bekor qiladi; tasdiqlangan ruxsat `AllOrganization` scope bilan faqat `ExpiresAt` gacha amal qiladi (`PermissionResolver`). So'rov va qarorlar audit jurnaliga yoziladi (`DataAccessRequest` auditlanadigan turlar ro'yxatida).

---

### 3.13. Automation Engine

| Permission | SA | AD | OP | TC | ST | SD | PR | DV |
|---|---|---|---|---|---|---|---|---|
| `automation.view` | ALL_ORG | MY_BRANCHES (faqat ko'rish) | — | — | — | — | — | — |
| `automation.manage` | ALL_ORG | — | — | — | — | — | — | — |

> **2026-09-15 (Phase 3 Sprint 5):** qoidalar tashkilot darajasida (TZ §40) — chegaralar va amallarni faqat Super Admin o'zgartiradi, "Hozir ishga tushirish" ham shu permission bilan. Sozlama o'zgarishlari audit jurnaliga yoziladi.

---

## 4. Muhim qoidalar

1. **View ≠ Action.** Super Admin `students.view=ALL_ORGANIZATION` ga ega, lekin `homework.review` (teacher nomidan baholash) alohida permission — default berilmaydi.
2. **Support Teacher — global rol emas.** Ruxsatlar `group_staff.role = SUPPORT_TEACHER` assignment orqali `ASSIGNED_GROUPS` scope bilan ishlaydi (TZ §3.5).
3. **Operator moliyaviy tahrir huquqiga ega emas** — faqat `finance.debt.view` (hisob-fakturalar va qarzdorlar), `payments.view`/`payments.create`/`payments.edit` yo'q (TZ §25). 2026-09-15: oldingi "payments.view (debt only)" alohida permissionga ajratildi — "qarz ha, to'lovlar yo'q" chegarasini scope ifodalay olmaydi, rol nomi bilan tekshiruv esa 6-qoidaga zid.
4. **Developer'ga default holatda student PII yo'q.** `production.data.request` — Request→Reason→Approval→Expiration→Audit Log oqimi majburiy (TZ §3.8).
5. **AI hech qachon to'g'ridan-to'g'ri action bajarmaydi** — `ai.*.use` faqat suggest/generate; `payments.edit`, `students.delete` kabi riskli action'lar AI orqali chaqirilmaydi (TZ §37).
6. Yangi permission qo'shilganda: (a) resource nomi, (b) action turi, (c) default scope har bir rol uchun shu jadvalga qo'shiladi — kod ichida "hardcoded role check" yozilmaydi, faqat permission+scope tekshiriladi.

---

## 5. Implementatsiya eslatmasi (Phase 1)

Bu matritsa quyidagi entitylarga map qilinadi (TZ §47, Database ERD hujjatiga qarang):

```
Permission (code, resource, action)
Role (name)
RolePermission (role_id, permission_id, default_scope)
UserPermission (user_id, permission_id, scope_override)  -- individual overrides uchun
```

Backend'da authorization policy handler har bir so'rovda: `permission code` + `scope` + `resource egasi` ni tekshiradi (ASP.NET Core `IAuthorizationHandler` + custom `ScopeRequirement`).
