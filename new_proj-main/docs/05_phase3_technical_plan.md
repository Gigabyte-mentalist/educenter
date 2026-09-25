# PHASE 3 — Finance + Automation + Communication
## Sprint darajasidagi texnik reja

**Hujjat turi:** Technical Specification (TZ hujjatining davomi)
**Versiya:** 1.0
**Bog'liq hujjatlar:** `00_TZ.md` (§58.Phase3, §10, §22–§25, §39–§40), `01_role_permission_matrix.md` (§3.7–§3.8), `02_database_erd.md` (§5–§6), `04_phase2_technical_plan.md`

**Fazaning maqsadi:** markazning pul oqimini tizimga olib kirish (hisob-faktura → to'lov → qarz → follow-up), Phase 2'da boshlangan avtomatikani sozlanadigan Automation Engine'ga aylantirish, va student/parent/xodimlarni bildirishnomalar (in-app, Telegram) hamda real-time chat bilan bog'lash.

**Tartib sababi:** Finance birinchi — lifecycle'dagi `PAID`/`DEBT` statuslari, "payment overdue 3 days" qoidasi va Parent portalidagi to'lov/qarz ma'lumoti unga bog'liq. Automation Engine keyin — uni uchta real qoida (trial, absence, debt) bilan qurish mumkin bo'ladi. Chat oxirida — boshqa modullarga bog'liq emas.

---

## 1. Finance bo'yicha qabul qilingan biznes qarorlar

TZ §25 imkoniyatlar ro'yxatini beradi, lekin hisob-kitob qoidalarini ochiq qoldiradi. Quyidagi standartlar tanlandi; har biri keyinchalik sozlama sifatida o'zgartirilishi mumkin:

| # | Qaror | Standart |
|---|---|---|
| 1 | Valyuta | UZS; summalar `numeric(18,2)` |
| 2 | Narx qayerda | `Course.MonthlyFee` — kursning oylik narxi. `0` = bepul, hisob-faktura yaratilmaydi. Guruh darajasidagi narx keyinroq kerak bo'lsa qo'shiladi |
| 3 | Hisob-kitob davri | Har faol enrollment uchun **oylik**, guruhga yozilgan kundan hisoblanadi (masalan 4-sentyabr → 4-sentyabr…3-oktyabr). Oy oxiridagi kunlar `AddMonths` bilan qisqaradi, lekin har doim boshlang'ich sanadan qayta hisoblanadi — "suzib ketmaydi" |
| 4 | To'lov muddati | Davr boshlanadigan kun (oldindan to'lov) |
| 5 | Hisob-faktura qachon yaratiladi | Konvertatsiya paytida (birinchi oy), keyin fon sweep har yangi davr boshlanganda. **O'tmish davrlari uchun hisob-faktura o'ylab topilmaydi** — enrollment'da hali hisob-faktura bo'lmasa, faqat hozirgi davrdan boshlanadi |
| 6 | Qarz | Muddati o'tgan (`DueDate < bugun`, filial vaqt zonasi bo'yicha) va qoldig'i bor hisob-fakturalar yig'indisi. Student statusi avtomatik `Debt` ⇄ `Active` |
| 7 | To'lov | Doim bitta hisob-fakturaga; summa qoldiqdan oshmaydi (ortiqcha to'lov/balans — Sprint 2) |
| 8 | To'lovni "tahrirlash" | Summa tahrirlanmaydi — to'lov sabab bilan **bekor qilinadi** (void) va kerak bo'lsa yangisi yoziladi. Ikkalasi audit jurnalida (TZ §52 "payment edited") |
| 9 | Debt follow-up | Eng yangi muddati o'tgan hisob-faktura 3 kundan oshsa → operatorga `Debt` vazifa (TZ §10). Qarz yopilsa ochiq vazifa avtomatik bekor qilinadi |
| 10 | Operator | Qarzni ko'radi (`finance.debt.view`), to'lovlarni ko'rmaydi va qabul qilmaydi |

**Permission o'zgarishi:** matritsadagi `payments.view — OP: MY_BRANCHES (debt only)` alohida `finance.debt.view` permissioniga ajratildi. Sabab: matritsa §4 qoida 6 — kodda rol nomi bilan tekshiruv yozilmaydi; "faqat qarz" chegarasini faqat alohida permission ifodalay oladi.

---

## 2. Yangi backend moduli

```
EduCenter.Modules.Finance — Invoice, Payment (Sprint 1); Discount, Refund, PromoCode (Sprint 2)
```

Boshqa modullarga faqat `Guid` orqali bog'lanadi (Student, Enrollment, Group, Branch). Hisob-kitob va follow-up qoidalari `EduCenter.Api/Automation`da (`BillingRules`, `FollowUpRules`, `NotificationRules`) — Sprint 5'dan beri tashkilot sozlamalari bilan Automation Engine orqali boshqariladi (`EduCenter.Modules.Automation`).

---

## 3. Sprint bo'linishi

### Sprint 1 — Finance core: narx, hisob-faktura, to'lov, qarz
- `Course.MonthlyFee` + `PATCH /api/courses/{id}` (`courses.manage`)
- `Invoice` (`Pending → PartiallyPaid → Paid`, yoki `Cancelled`), `Payment` (void bilan)
- `BillingRules`: oylik hisob-faktura yaratish, `Debt` statusini sinxronlash, debt follow-up vazifasi; `AutomationSweepService`ga ulanadi; konvertatsiyadan keyin birinchi hisob-faktura darhol
- API: `GET /api/finance/invoices`, `GET /api/finance/debtors`, `POST /api/finance/invoices/{id}/payments`, `POST /api/finance/payments/{id}/void`, `POST /api/finance/invoices/{id}/cancel`, `GET /api/finance/payments`, `GET /api/students/{id}/finance`, `POST /api/finance/billing/run`
- Permission: `payments.view`, `payments.create`, `payments.edit`, `finance.debt.view`
- Timeline: `PAYMENT_RECEIVED`, `PAYMENT_VOIDED`; audit: `Invoice`, `Payment`, `Course`
- UI: `/finance` (Qarzdorlar / Hisob-fakturalar / To'lovlar), Student 360° **To'lovlar** tabi, kurs narxlari (Guruhlar sahifasida), Call Queue'da qarz vazifalari

### Sprint 2 — Chegirma, refund, moliyaviy hisobotlar
- `Discount` (foiz/qat'iy; Administrator uchun maksimal foiz chegarasi — `discount.apply`), `PromoCode`
- `Refund` (Administrator uchun maksimal summa chegarasi — `refund.approve`)
- Oldindan to'lov / balans (ortiqcha to'lovni keyingi hisob-fakturaga o'tkazish)
- Hisobotlar (`finance.reports.view`, TZ §5.2 Finance): revenue, expected revenue, paid, overdue, debt, discounts, refunds — filial va davr bo'yicha; Super Admin global ko'rinishi
- Freeze adjustment (TZ §21 "payment impact") — Student freeze oqimi bilan birga

### Sprint 3 — Notification Engine (TZ §39)
- `Notification` (in-app), foydalanuvchi sozlamalari (qaysi kanal, qaysi event)
- `NotificationDispatcher` — kanal abstraksiyasi (InApp, Telegram, keyinchalik Push/Email/SMS)
- Eventlar: `PAYMENT_DUE`, `PAYMENT_OVERDUE`, `ABSENT`, `EXAM_RESULT`, `HOMEWORK_CREATED`, `HOMEWORK_DEADLINE`, `SUPPORT_ASSIGNED`, `SCHEDULE_CHANGED`, `TEACHER_CHANGED`, `LESSON_REMINDER`
- UI: bildirishnomalar qo'ng'irog'i (header), o'qilgan/o'qilmagan

### Sprint 4 — Telegram integratsiyasi + Parent notifications (TZ §23)
> **Keyinga qoldirildi (2026-09-15):** bot tokeni hozircha yo'q — foydalanuvchi eslatganda bajariladi. Tayyorgarlik: `notification_preferences.Channel` ustunida `Telegram` qiymati allaqachon bor, mavjud barcha bildirishnoma turlari shu kanalga ulanadi.
- Bot bilan akkauntni bog'lash (bir martalik deep-link token)
- Student va Parent bildirishnomalari; sensitiv ma'lumot yuborilmaydi — platformaga xavfsiz havola
- Parent portalida to'lov/qarz, davomat, imtihon natijalari ko'rinishi (TZ §24)

### Sprint 5 — Automation Engine (TZ §40)
- `AutomationRule` (trigger → condition → action), chegaralar sozlanadigan (2 yo'qlik, 24 soat, 3 kun)
- Mavjud `FollowUpRules`/`BillingRules` qoidalari engine'ga ko'chiriladi; action'lar: operator vazifasi, bildirishnoma, support tavsiyasi
- Background jobs: TZ §44 bo'yicha Hangfire (dashboard, retry) — hozirgi `BackgroundService` o'rniga
- Super Admin uchun qoidalarni yoqish/o'chirish va chegaralarni o'zgartirish UI

### Sprint 6 — SignalR Chat (TZ §22)
- 1-to-1, guruh chati, filial staff kanallari; text/file/reply/read status/typing
- Permission: `chat.1to1.use`, `chat.group.use`, `chat.moderate`, `staff.channels.access`

---

## 4. Definition of Done (Phase 3 yakuni)

- [ ] Kursga narx qo'yilganda faol studentlarga oylik hisob-faktura avtomatik chiqadi;
- [ ] Administrator to'lov qabul qiladi, xato to'lovni sabab bilan bekor qiladi — hammasi audit jurnalida;
- [ ] Muddati o'tgan qarz studentni `Debt` qiladi va 3 kundan keyin operator vazifasini yaratadi; to'lov ikkalasini yopadi;
- [ ] Chegirma/refund chegaralari Administrator uchun ishlaydi; filial moliyaviy hisoboti ko'rinadi;
- [ ] Student/Parent to'lov holatini ko'radi va Telegram orqali eslatma oladi;
- [x] Automation qoidalari sozlanadi (kod o'zgartirmasdan chegaralar);
- [x] Guruh chati va 1-to-1 chat real-time ishlaydi;
- [ ] Matritsa §3.7–§3.8 permissionlari kodda ishlaydi.

---

## 5. Sprint jurnali

### Sprint 1 — Finance core (2026-09-15, bajarildi)

- `EduCenter.Modules.Finance`: `Invoice` (`invoices`, unikal `EnrollmentId+PeriodStart`), `Payment` (`payments`, void bilan). `Course.MonthlyFee`. Migratsiya `AddFinanceCore`.
- `BillingRules` (`Automation/`): oylik hisob-faktura (enrollment sanasidan, filial vaqt zonasi — `BranchClock`), `Active ⇄ Debt` sinxronlash, 3 kunlik `Debt` vazifa (`DEBT_OVERDUE`); `AutomationSweepService` avval billing'ni, keyin follow-up'larni yuritadi. Konvertatsiyadan keyin birinchi oy darhol hisoblanadi.
- Operator vazifasi kimga tushishi `StaffResolver`ga ajratildi (absence va debt uchun umumiy).
- API: `/api/finance/invoices`, `/debtors`, `/invoices/{id}/payments`, `/payments/{id}/void`, `/invoices/{id}/cancel`, `/payments`, `/billing/run`; `/api/students/{id}/finance`; `PATCH /api/courses/{id}`.
- Permission: `payments.view` (SA/AD/SD/PR), `finance.debt.view` (SA/AD/OP — yangi), `payments.create`, `payments.edit`. Matritsa §3.7 va 3-qoida yangilandi.
- Audit: `Course`, `Invoice`, `Payment`. Timeline: `PAYMENT_RECEIVED`, `PAYMENT_VOIDED`.
- UI: `/finance` (Qarzdorlar / Hisob-fakturalar / To'lovlar, "Hisob-fakturalarni yangilash"), Student 360° **To'lovlar** tabi (Operator — faqat hisob-fakturalar), Guruhlar sahifasida kurs narxlari, Call Queue'da "Qarzdorlik".
- Demo: seed kurslarga narx beradi (IELTS Intermediate 800 000, Advanced 950 000, General English A2 550 000); mavjud dev bazada ham bir marta qo'yiladi — birinchi sweep faol studentlarga hisob-faktura chiqaradi.
- Testlar: **26/26 (2 unit + 24 integration)** (`FinanceTests`: narx ruxsatlari, konvertatsiyadagi hisob-faktura, qisman/to'liq to'lov, ortiqcha to'lov rad etilishi, void (sabab majburiy, qayta void — 409), Operator/Teacher chegaralari, student o'z hisobini ko'rishi; muddati o'tgan → `Debt` + bitta vazifa → to'lov ikkalasini yopadi).
- **Muhit muammosi:** shu kuni Windows Smart App Control (majburiy rejim) yangi build qilingan imzosiz assembly'larni bloklay boshladi (`0x800711C7`) — host'da `dotnet test/ef/run` ishlamay qoldi. Xavfsizlik sozlamasiga tegilmadi; build/test/migratsiya `backend/scripts/dotnet-in-docker.sh` orqali .NET SDK konteynerida bajarildi.

### Sprint 2 — Chegirma, promo-kod, refund, oldindan to'lov, hisobot (2026-09-15, bajarildi)

**Qarorlar**
- **Chegirma** hisob-fakturaga beriladi: foiz (hisob-faktura summasidan, butun so'mga yumaloqlanadi) yoki qat'iy summa; sabab majburiy; bitta hisob-fakturada bitta faol chegirma (boshqasi uchun avval olib tashlanadi — `discounts` jadvalida `RevokedAt IS NULL` bo'yicha unikal indeks).
- **Chegaralar scope bilan**, rol nomi bilan emas (matritsa §4 qoida 6): `discount.apply`/`refund.approve` `ALL_ORG` bilan berilgan bo'lsa chegara yo'q; `MY_BRANCHES` bilan — `Finance:AdministratorMaxDiscountPercent` (20%) va `Finance:AdministratorMaxRefundAmount` (1 000 000 so'm). Chegaradan oshsa 403 va tushunarli sabab. `GET /api/finance/limits` UI'ga chaqiruvchining o'z chegarasini beradi.
- **Promo-kod** oldindan tasdiqlangan chegirma: yaratish/to'xtatish — yangi `promo.manage` (faqat Super Admin); qo'llash — `discount.apply`, filial chegarasi qo'llanmaydi. Muddat, foydalanish limiti, kursga bog'lash. Chegirma olib tashlansa foydalanish qaytariladi.
- **Refund ≠ void.** Void — xato kiritilgan to'lov: `PaidAmount` kamayadi, qoldiq qayta ochiladi. Refund — studentga pul qaytarildi: `PaidAmount` kamayadi **va** hisob-faktura majburiyati shu summaga kamayadi (`CreditedAmount`) — qarz paydo bo'lmaydi. Refund qilingan to'lovni void qilib bo'lmaydi.
- **Oldindan to'lov** alohida kredit-balans o'rniga *oldindan hisob-faktura* bilan: `POST /api/students/{id}/advance-invoices` keyingi N oyning hisob-fakturalarini hozir chiqaradi (hozirgi davrdan eng ko'pi 12 oy), ular odatiy to'lov oqimida to'lanadi. Ortiqcha to'lov/balans hisobi kerak bo'lmadi.
- **Freeze adjustment** kiritilmadi — Student freeze funksiyasi hali yo'q (TZ §21); o'sha bilan birga qilinadi.

**Backend**
- Domen: `Discount`, `Refund`, `PromoCode`; `Invoice.DiscountAmount/CreditedAmount/NetAmount`, `Payment.RefundedAmount`. Migratsiya `AddDiscountsRefundsPromo`.
- `FinanceQueries` — scoping va read-model'lar uch controller uchun umumiy; `FinanceAdjustmentsController` (chegirma, promo, refund, limitlar), `FinanceReportsController`.
- Hisobot `GET /api/finance/reports/summary?from&to&branchId` (`finance.reports.view`, TZ §5.2 Finance): davrdagi tushum/qaytarilgan/sof tushum (filial mahalliy sanasi bo'yicha), davrda boshlangan hisob-fakturalar bo'yicha hisoblangan/chegirma/kutilgan tushum/to'langan/qoldiq, bugungi muddati o'tgan qarz va qarzdorlar; filial va to'lov usuli kesimida.
- Qarz/qoldiq hisoblari (`BillingRules`, qarzdorlar) chegirma va refundni hisobga oladi. Audit: `Discount`, `Refund`, `PromoCode`. Timeline: `DISCOUNT_APPLIED`, `DISCOUNT_REVOKED`, `REFUND_ISSUED`.

**Frontend**
- Hisob-fakturalar jadvalida chegirma/qaytarilgan summa ko'rinadi; amallar: To'lov, Chegirma (Foiz / Summa / Promo-kod — chegara oldindan ko'rsatiladi), Chegirmani olib tashlash.
- To'lovlar jadvalida Refund va Bekor qilish (farqi oynada tushuntirilgan).
- Student 360° "To'lovlar": Oldindan hisob-faktura, Qaytarilgan pullar.
- `/finance`: yangi **Hisobot** (joriy/o'tgan oy yoki ixtiyoriy davr, filial filtri) va **Promo-kodlar** tablari.

**Testlar:** 31/31 (2 unit + 29 integration) (`FinanceAdjustmentsTests`: 20% chegara va Super Admin, bitta faol chegirma, olib tashlash; promo-kod ruxsati, dublikat kod, limit, olib tashlashda foydalanish qaytishi; refund qarz yaratmasligi, 1 mln chegara, refund qilingan to'lovni void qilib bo'lmasligi; oldindan hisob-faktura va 12 oylik chegara; hisobot raqamlari va ruxsatlar).

### Sprint 3 — Notification Engine (2026-09-15, bajarildi)

**Model**
- Yangi modul `EduCenter.Modules.Notifications`: `Notification` (`notifications` — foydalanuvchi inbox'i: tur, sarlavha, matn, ilova ichidagi havola, o'qilgan vaqti) va `NotificationPreference` (`notification_preferences` — foydalanuvchi × tur × kanal; qator yo'q = yoqilgan). Kanal ustuni hozirdan bor (`InApp`, `Telegram`), Sprint 4 shu jadvalga ulanadi. Migratsiya `AddNotifications`.
- `NotificationDispatcher`: o'chirilgan turni, tashkilotdan tashqari foydalanuvchini va (dedupe kaliti bo'lsa) allaqachon yetkazilganini tashlab ketadi. Qatorni faqat kontekstga qo'shadi — sababchi o'zgarish bilan birga bitta `SaveChanges`da saqlanadi, ya'ni amalga oshmagan ish haqida xabar ketmaydi.
- `NotificationRecipients`: student / ota-ona / guruh studentlari / guruh o'qituvchilari / filial administratorlari.
- `NotificationCatalog`: har tur kimga tegishli — sozlamalar sahifasi faqat rolga tegishli turlarni ko'rsatadi.

**Hodisalar**

| Tur | Qachon | Kimga |
|---|---|---|
| `PAYMENT_DUE` | to'lov kuniga ≤3 kun (sweep, bir marta) | Student, ota-ona |
| `PAYMENT_OVERDUE` | muddatdan 3 kun o'tganda (sweep, bir marta) | Student, ota-ona |
| `ABSENT` | davomatda `Absent` | Ota-ona |
| `ABSENCE_STREAK` | ketma-ket 2 yo'qlik signali | Asosiy o'qituvchi |
| `EXAM_RESULT` | natija kiritilganda | Student, ota-ona |
| `HOMEWORK_CREATED` | homework berilganda | Guruh studentlari |
| `HOMEWORK_DEADLINE` | muddatga ≤24 soat, topshirmaganlar (sweep) | Student |
| `HOMEWORK_REVIEWED` | baholanganda | Student, ota-ona |
| `LESSON_REMINDER` | darsga ≤2 soat (sweep) | Guruh studentlari, o'qituvchi |
| `SCHEDULE_CHANGED` | jadvalga dars qo'shilganda | Guruh studentlari, o'qituvchi |
| `TEACHER_ASSIGNED` | guruhga o'qituvchi biriktirilganda | O'qituvchining o'zi; asosiy o'qituvchi bo'lsa guruh studentlari |
| `SUPPORT_RECOMMENDED` | tizim signali yoki xodim tavsiyasi | Filial administratorlari |
| `SUPPORT_ASSIGNED` | yordamchi o'qituvchi biriktirilganda | Yordamchi o'qituvchi, student |
| `TASK_ASSIGNED` | avtomatik vazifa (qarz, yo'qlik, trial) | Vazifa egasi (operator) |

- TZ §39'dagi `TEACHER_CHANGED` va jadval *tahriri* uchun tizimda hali tahrirlash oqimi yo'q (o'qituvchini almashtirish TZ §19, dars tahriri) — hozircha `TEACHER_ASSIGNED` va "yangi dars" ko'rinishida; o'sha oqimlar qo'shilganda shu turlar ulanadi.
- Sensitiv ma'lumot: matn faqat qabul qiluvchi o'zi ko'ra oladigan narsani aytadi (TZ §23), batafsil ma'lumot havola orqali platformada.
- `NotificationRules` sweep'da (`AutomationSweepService`, billing va follow-up'lardan keyin); vaqtlar filial vaqt zonasida (`BranchClock.LocalDateTime`).

**API** (`[Authorize]`, permission kodisiz — har kim faqat o'z inbox'i): `GET /api/notifications?unreadOnly&limit&before`, `GET /unread-count`, `POST /{id}/read`, `POST /read-all`, `GET /preferences`, `PUT /preferences/{type}`.

**UI:** sidebar'da qo'ng'iroq (o'qilmaganlar soni, har daqiqada yangilanadi) → bildirishnomalar oynasi (Bugun / Oldinroq, bosilganda o'qilgan bo'ladi va tegishli sahifa ochiladi, "Hammasini o'qilgan qilish"); `/settings/notifications` — rolga tegishli turlar uchun yoqish/o'chirish (foydalanuvchi menyusidan ham).

**Testlar:** 34/34 (2 unit + 32 integration) (`NotificationsTests`: imtihon natijasi student va ota-onaga, o'qituvchiga emas; begona bildirishnomani belgilab bo'lmaydi; o'chirilgan tur yetkazilmaydi; rolga tegishli bo'lmagan tur sozlanmaydi; yangi dars / yo'qlik (ota-onaga, studentga emas) / homework / support tavsiyasi va biriktirish; to'lov eslatmalari bir martadan va qarz vazifasi haqida operatorga xabar).

### Sprint 4 — Telegram (keyinga qoldirildi)

Bot tokeni yo'q; foydalanuvchi eslatganda bajariladi. Tartib: Sprint 5 → Sprint 6, keyin Sprint 4.

### Sprint 5 — Automation Engine (2026-09-15, bajarildi)

**Model**
- Yangi modul `EduCenter.Modules.Automation`: `AutomationRule` (`automation_rules` — tashkilot × qoida kodi, yoqilgan, parametrlar va amallar JSON; **qator yo'q = standart**) va `AutomationRunLog` (`automation_run_logs` — qoida, qanday ishga tushgani `Sweep`/`Event`/`Manual`, nechta natija, qisqa matn). Migratsiya `AddAutomationRules`. `AutomationRule` o'zgarishlari audit jurnalida.
- Qoidalar katalogi kodda (`Api/Automation/AutomationCatalog.cs`) — trigger → shart → amal, har parametrning oralig'i va standarti. Bazadagi qiymat har o'qilganda katalog oralig'iga siqiladi, noma'lum kalitlar e'tiborga olinmaydi. Standartlar Sprint 5'gacha qattiq yozilgan qiymatlarni aynan takrorlaydi — sozlamani ochmagan tashkilotda xulq o'zgarmaydi.
- Jurnalga faqat natija bergan ishga tushishlar yoziladi (har 5 daqiqadagi bo'sh tekshiruvlar emas).

**Qoidalar**

| Kod | Hodisa | Parametrlar (standart) | Amallar |
|---|---|---|---|
| `TRIAL_FOLLOW_UP` | trial darsga keldi | kutish 1–168 soat (24) | operator vazifasi + xabar; lid «Muhim» |
| `ABSENCE_FOLLOW_UP` | davomatda yo'qlik (darhol) | ketma-ket 2–10 dars (2) | qo'ng'iroq vazifasi; support tavsiyasi; asosiy o'qituvchiga xabar |
| `HOMEWORK_SUPPORT_SIGNAL` | homework muddati o'tdi | oxirgi 2–10 ta (3), ko'pi bilan 0–9 topshirilgan (1), M < N | support tavsiyasi |
| `DEBT_FOLLOW_UP` | to'lov muddati o'tdi | 1–60 kun (3) | operator vazifasi + xabar |
| `PAYMENT_REMINDERS` | to'lov muddati | oldin 0–14 kun (3), keyin 1–60 kun (3) | muddat eslatmasi; kechikish xabari |
| `HOMEWORK_DEADLINE_REMINDER` | homework muddati yaqin | 1–72 soat (24) | — |
| `LESSON_REMINDER` | dars yaqin | 1–24 soat (2) | — |

- Hisob-faktura chiqarish va `Active ⇄ Debt` holati qoida emas — bu buxgalteriya, doim ishlaydi; qarz *vazifasi* qoidaga bo'ysunadi.
- Qoidalar tashkilot bo'yicha ishlaydi (sweep har tashkilotni o'z sozlamasi bilan aylanadi); `AutomationSettings` so'rov/ish davomida keshlaydi.
- O'qituvchiga yo'qlik xabari endi support tavsiyasidan mustaqil (o'z dedupe kaliti: student × guruh × trigger vaqti) — support o'chirilsa ham o'qituvchi xabar oladi.

**Fon ishlari — Hangfire** (TZ §44): `BackgroundService` o'rniga `automation-sweep` recurring job (`*/5 * * * *`, `Automation:SweepIntervalSeconds`dan), PostgreSQL storage `hangfire` sxemasida (Hangfire o'zi yaratadi). `[DisableConcurrentExecution]` — bir nechta API nusxasida ham ustma-ust ishlamaydi; `[AutomaticRetry(0)]` — qoidalar idempotent, keyingi ishga tushish joriy ma'lumotdan boshlaydi. Tartib: billing → follow-up/support → eslatmalar (`AutomationRunner`). Dashboard `/hangfire` faqat Development'da, faqat o'qish uchun. Testlarda `Automation__Enabled=false` — Hangfire ro'yxatdan o'tmaydi.

**Permission:** `automation.view` (SA — ALL_ORG, AD — MY_BRANCHES, faqat ko'rish), `automation.manage` (SA). Matritsa §3.13.

**API** `/api/automation`: `GET rules` (view) — katalog + joriy qiymatlar, standartlar, kim/qachon o'zgartirgan, oxirgi natija; `PUT rules/{code}` (manage) — oraliq, noma'lum kalit va o'zaro shart tekshiruvi (400), noma'lum qoida 404; `DELETE rules/{code}` (manage) — standartga qaytarish; `GET runs?limit` (view); `POST run` (manage) — tashkilot uchun hozir ishga tushirish.

**UI:** `/automation` (Tashkilot → «Avtomatlashtirish», SA/AD) — har qoida kartasi: yoqish tugmasi, oraliq va standart ko'rsatilgan parametrlar, amallar, «Saqlash» / «Standartga qaytarish», oxirgi natija va kim o'zgartirgani; «Hozir ishga tushirish»; «So'nggi faoliyat» jadvali. Administrator faqat ko'radi.

**Testlar:** 37/37 (2 unit + 35 integration) (`AutomationTests`: 7 qoida, AD ko'radi lekin o'zgartira olmaydi, Operator 403; oraliq/o'zaro shart/noma'lum kalit → 400, noma'lum qoida → 404; chegara 3 bo'lsa 2 yo'qlik tavsiya yaratmaydi, 3-si yaratadi va jurnalga `Event` bilan yoziladi, reset standartni qaytaradi; qarz vazifasi amali o'chirilsa vazifa yo'q, lekin student baribir `Debt`; qayta yoqilganda vazifa paydo bo'ladi).

### Sprint 6 — SignalR Chat (2026-09-15, bajarildi)

**Model** — yangi modul `EduCenter.Modules.Chat`, migratsiya `AddChat`:
- `ChatRoom` (`chats`): `Direct` / `Group` / `StaffChannel`. Guruh chati guruhga bittadan, filial kanali (filial × tur) bittadan, direct chat juftlikka bittadan — unique indekslar bilan. Guruh chatlari va kanallar birinchi ko'rilganda yaratiladi.
- A'zolik guruh chati va kanal uchun **saqlanmaydi**: har so'rovda guruhdagi enrollment/xodimlik va filial xodimlari bo'yicha hisoblanadi (student guruhdan chiqsa chatdan ham chiqadi). `ChatMember` — o'qish kursori (`LastReadAt`); direct chatda ikki qatori a'zolik ham.
- `ChatMessage`: matn (≤4000), javob (`ReplyToMessageId`), ilova (nomi, turi, hajmi, saqlash kaliti), soft delete (kim o'chirgan), pin. `ChatReaction`: 6 ta ruxsat etilgan emoji, foydalanuvchi × xabar × emoji unique.

**Kim nimani ko'radi** (`Api/Chat/ChatAccess.cs`, matritsa §3.8):

| Chat | O'qish/yozish | Moderatsiya (o'chirish, pin) |
|---|---|---|
| Guruh | `chat.group.use` scope'i guruhni qamrasa (SA hammasi, AD filial, TC asosiy o'qituvchi, ST biriktirilgan, SD o'z guruhi) | `chat.moderate` guruhni qamrasa (SA, AD filial, asosiy o'qituvchi) |
| Filial kanali | `staff.channels.access` filialni qamrasa + kanal auditoriyasi: Umumiy/E'lonlar — barcha xodim; O'qituvchilar — TC/ST/AD/SA; IELTS o'qituvchilari — IELTS guruhida ishlaydigan TC/ST va AD/SA; Administratorlar — AD/SA. E'lonlarga faqat moderator yozadi | `chat.moderate` filialni qamrasa (SA, AD) |
| Direct | ikki ishtirokchi; ochish — faqat kontaktlar ro'yxatidagi odam bilan (`chat.1to1.use` scope'i) | yo'q: har kim o'z xabarini o'chiradi, har ikki ishtirokchi pin qiladi |

Ruxsati yo'q chat 404 qaytaradi (mavjudligi oshkor qilinmaydi). `PermissionResolver` policy handler'dan ajratildi — bitta so'rovda bir nechta permission va hub uchun.

**Real-time** — `ChatHub` (`/hubs/chat`, JWT `access_token` query orqali faqat `/hubs` yo'lida): hub faqat yetkazadi, yozuvlar REST orqali. Ulanish `SyncChats` bilan ko'rinadigan chatlarga obuna bo'ladi (ro'yxat o'zgarganda qayta), `Typing`. Server hodisalari: `messageCreated`, `messageUpdated`, `readUpdated`, `typing`, `chatActivity` (direct chat ishtirokchilariga). Guruhlar xotirada — bitta API nusxasi; bir nechta nusxa uchun backplane (Redis) kerak.

**Fayllar:** `ChatFileStorage` — lokal disk (`Chat:StoragePath`, standart `App_Data/chat-files`), tasodifiy kalit bilan, 10 MB, ruxsat etilgan kengaytmalar (rasm, PDF, Office, txt, zip, mp3/m4a); content type kengaytmadan, mijozdan emas; yuklab olish chatga kirish tekshiruvi orqali (rasm — inline, qolgani — attachment). Docker konteyneridagi fayllar konteyner qayta yaratilganda yo'qoladi — production uchun obyekt saqlash (S3/MinIO) keyingi ish.

**API** `/api/chats`: `GET` (ro'yxat: oxirgi xabar, o'qilmaganlar, yozish mumkinmi), `GET unread-count`, `GET contacts?search`, `POST direct`, `GET {id}` (o'quvchilar kursori, qadalganlar, moderatsiya huquqi), `GET {id}/messages?before&limit&search`, `POST {id}/messages` (multipart: matn, javob, fayl), `DELETE {id}/messages/{messageId}`, `POST/DELETE .../pin`, `POST .../reactions`, `POST {id}/read`, `GET .../attachment`.

**UI:** `/chat` (Muloqot → «Xabarlar», Developer'dan boshqa barcha rollar; sidebar'da o'qilmaganlar soni): chap tomonda Shaxsiy / Guruhlar / Filial kanallari, qidiruv, «Yangi suhbat» (kontaktlar); o'ngda suhbat — kun ajratgichlari, javob, reaksiyalar, pin paneli, o'chirish, rasm ko'rish va fayl yuklab olish, «yozmoqda…», ✓/✓✓ o'qildi belgisi, xabarlardan qidiruv, Enter — yuborish. Tor ekranda ro'yxat va suhbat navbatma-navbat.

**TZ §22'dan keyinga qolganlar:** xabarni tahrirlash, ovozli xabar yozish, xabarga to'g'ridan-to'g'ri o'tish (qidiruv natijasidan), bir nechta API nusxasi uchun backplane.

**Testlar:** 41/41 (2 unit + 39 integration) (`ChatTests`: guruh chati guruhga ko'rinadi, operatorga yo'q; SignalR orqali xabar keladi; o'qilmaganlar va o'qish kursori; o'qituvchi pin/o'chiradi, student pin qila olmaydi; direct chat faqat kontakt bilan, takroriy ochish o'sha chat, begona 404, javob, o'z xabarini o'chirish, reaksiya toggle va noto'g'ri emoji 400, qidiruv o'chirilganni topmaydi; kanallar rol va filial bo'yicha, E'lonlarga o'qituvchi yoza olmaydi; ilova yuklash/yuklab olish, begona 404, `.exe` 400).

