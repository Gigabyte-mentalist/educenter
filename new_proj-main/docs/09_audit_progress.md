# 08-AUDIT BO'YICHA BAJARILGAN ISHLAR VA QOLGANLARI

> **2026-09-21:** butun loyiha TZ bo'yicha qaytadan auditdan o'tkazildi — natija
> **`docs/10_full_audit_2026-09-21.md`** da. Bu fayl bajarilgan ishlar jurnali bo'lib qoladi,
> qolgan ishlar ro'yxati va keyingi sprintlar tartibi endi 10-hujjatda.

**Sana:** 2026-09-18
**Manba:** `docs/08_project_audit_report.md`
**Holat:** Phase 1-4 doirasidagi barcha band — Transfer/Freeze/Resume, Teacher Replacement, Room datalist,
Universal Search, Student 360°/Teacher 360° qolgan qismlari, CRM Operator call UX, Group Recommendation
Engine, Placement Test va Groups/Leads/Branches/Support highlight — yakunlandi (1.8-1.17). Faqat Phase 5/6
bandlari (Teacher KPI/Feedback, Gamification, Certificates, Flutter), texnik qarzlar va Telegram bot ataylab
ochiq qoldirildi (pastga qarang, foydalanuvchi so'ragan tartib bilan mos). Keyingi sessiya 2-bo'limdan
(❌ Boshlanmagan) davom etsin.

---

## 0. QISQACHA

| | Soni |
|---|---|
| ✅ To'liq bajarildi | 26 band (1.1-1.26) |
| ❌ Boshlanmagan | 6 band |
| ⚪ Hisobotdagi noto'g'ri band (tekshirilib, xato ekani aniqlandi) | 4 band |

**Commitlar:**
- `4cca2c5` — tungi rejim + responsive
- `8a784dc` — barcha rollar uchun dashboard + sana/pul formati
- `a5597fc` (backend), `a637bb4` (frontend), `d004d74` (hujjatlar) — 1.8-1.25: Transfer/Freeze/Resume, Teacher
  Replacement, Universal Search, Student 360°, Placement Test, Group Recommendation, rolga mos ko'rinish,
  guruh detali, katta demo ma'lumot, Developer imtiyozlari, lid → student oqimi, sahifalash, iPad menyusi.
  173 test; migratsiyalar `AddStudentDocuments`, `AddPlacementAttempts`, `AddDataAccessRequests`
- Commit qilinmagan: 1.26 — student yaratish/tahrirlash (`docs/11_implementation_plan.md` 1.1-qadam). **186 test o'tadi**

---

## 1. ✅ TO'LIQ BAJARILGAN

### 1.1. Dashboard (audit §3.1, §3.2, §3.3, §3.4, §3.16) — 🔴 kritik edi
Bitta `/dashboard` URL, orqasida har bir rol uchun alohida sahifa. Bitta
`dashboard.view` ruxsati, scope kesadi:

| Rol | Nima ko'radi |
|---|---|
| Super Admin | Butun tashkilot: o'quvchi, tushum, qarz, davomat, lid konversiyasi, ushlab qolish + **filiallar reytingi jadvali** |
| Administrator / Operator | Xuddi shu, faqat o'z filiallari |
| Teacher / Support | Bugungi va ertangi darslar, tekshirish kutayotgan uy vazifa va insholar, belgilanmagan davomat, **risk signallari** (3+ yo'qlik yoki ochiq support) |
| Student | Bugungi darslar, bajarilmagan vazifalar, davomat %, qarz, so'nggi AI natijalari, oxirgi imtihon |
| Parent | Har bir farzand uchun karta: davomat, vazifa, imtihon, qarz, bugungi dars + tugmalar |
| Developer | **Tizim monitoringi**: baza holati/hajmi/versiyasi, API uptime, AI provayderlar (24 soat: so'rov, xato %, p95, xarajat), writing navbati, avtomatlashtirish jurnali, jadval hajmlari |

Kod: `backend/src/EduCenter.Api/Dashboard/` (DashboardController, SystemController, DashboardDtos),
`frontend/src/components/dashboard/`, `frontend/src/app/(admin)/dashboard/page.tsx`.

Login endi har doim `/dashboard` ga tushadi (`lib/nav.ts` → `homeForRole`).
Sidebar va mobil pastki panelga "Bosh sahifa" qo'shildi.

**Test:** 10 ta yangi integration test (`DashboardTests.cs`) — scope izolyatsiyasi
tekshiriladi: student org dashboardini ololmaydi (403), parent faqat o'z
farzandlarini ko'radi, administrator faqat o'z filialini. Jami **89 test o'tadi**.

**Brauzerda tekshirildi:** superadmin, student, parent — real ma'lumot bilan.

### 1.2. Sana formati — **auditda yo'q, lekin jiddiy xato edi**
Node/Chromium ICU'sida `uz-UZ` uchun oy nomlari yo'q. Shu sababli **butun
mahsulot bo'ylab** har bir muddat, to'lov sanasi va vaqt belgisi `M09 17` ko'rinishida
chiqardi.

`frontend/src/lib/format-date.ts` yozildi (oylar qo'lda: yanvar…dekabr) va
**20 ta call-site** shunga o'tkazildi. Endi `17-sentabr`.

### 1.3. Pul formati — **auditda yo'q**
`Intl.NumberFormat("uz-UZ")` vergul bilan ajratardi: `3,975,000` — bu yerda
o'nlik nuqta sifatida o'qiladi. Endi probel: `3 975 000 so'm`.

### 1.4. AI Writing Reviews sahifasi (audit §3.17, §6.5)
**Hisobot yozilgunga qadar allaqachon qilingan** (`c0e2c20` commit).
`/ai/writing/reviews/page.tsx` mavjud, o'qituvchi o'z guruhidagi insholarni
ko'radi va bahoni o'zgartira oladi.

### 1.5. Accounts sahifasi (audit §3.19, §6.4)
**Allaqachon qilingan** (`42467ae` commit). 683 bayt — bu sahifa yupqa wrapper,
asosiy ekran `components/users/UserDirectory.tsx` da (qidiruv, filtr, paginatsiya,
bloklash/o'chirish). Fayl hajmi bo'yicha "chala" degan xulosa noto'g'ri edi.

### 1.6. Tungi rejim + responsive (foydalanuvchi so'ragan, auditda yo'q)
`4cca2c5` commit. `:root[data-theme="dark"]`, pre-paint script (oq miltillash yo'q),
uch holatli tanlagich (Kunduzgi/Tungi/Tizim). `lg` dan pastda sidebar overlay +
hamburger, telefon uchun pastki panel.

### 1.7. Audit qamrovi kengaytirildi
`AuditSaveChangesInterceptor` endi `Student`, `Enrollment`, `Lesson` ni ham yozadi —
transfer, muzlatish va o'qituvchi almashtirish avtomatik audit jurnaliga tushadi.

### 1.8. Student Transfer / Freeze / Resume (audit §3.5, §5.3) — 🔴 kritik edi
Backend: `backend/src/EduCenter.Api/Students/EnrollmentLifecycleController.cs`

| Endpoint | Nima qiladi |
|---|---|
| `GET /api/students/{id}/transfer-targets` | Ko'chirish mumkin bo'lgan guruhlar — scope ichida, yopilmagan, bo'sh joyi bor |
| `POST /api/students/{id}/transfer` | Eski enrollment → `Transferred`, yangisi yaratiladi. Sig'im, guruh holati, ikkala filial ham scope ichidami — tekshiriladi |
| `POST /api/students/{id}/freeze` | Barcha faol enrollment → `Frozen`, student → `Frozen`. Sabab majburiy, `resumeOn` ixtiyoriy |
| `POST /api/students/{id}/resume` | Qaytarish; guruh yopilgan bo'lsa rad etadi va sababini aytadi |

`BillingRules` allaqachon faqat `Active` enrollment'ga invoice yozadi, shuning uchun
muzlatish **avtomatik ravishda** hisob-kitobni to'xtatadi — qo'shimcha kod kerak emas.
Ochiq invoice'lar **atayin tegilmaydi** — ular o'quvchi haqiqatan o'qigan davr uchun.

Timeline event'lari: `TRANSFERRED`, `COURSE_FROZEN`, `COURSE_RESUMED`.
Bildirishnoma: o'quvchi + ota-onaga.
Ruxsatlar: `student.transfer`, `student.freeze` (SuperAdmin AllOrg, Administrator MyBranches).

Frontend: `frontend/src/components/StudentLifecycle.tsx` — student profilida
"Ko'chirish"/"Muzlatish" tugmalari (Faol holatda) yoki "Qayta faollashtirish"
(Frozen holatda), har biri drawer bilan. `canManageAcademic` orqali faqat
SuperAdmin/Administrator ko'radi.

**Test:** `EnrollmentLifecycleTests.cs` — 9 ta test (targets ro'yxati, transfer,
to'liq guruhga ko'chirish rad etilishi, sababsiz so'rov rad etilishi, freeze→resume
round-trip, teacher ruxsatsizligi, boshqa filialdagi studentga administrator kira
olmasligi). Brauzerda ham tekshirildi: Feruza Tosheva IELTS-21 → IELTS-Adv-1 ga
ko'chirildi, keyin muzlatildi, keyin qayta faollashtirildi — hammasi real ma'lumot
bilan ishladi.

### 1.9. Teacher Replacement (audit §3.12, §5.10)
Backend: `ScheduleController.cs` ichida

| Endpoint | Nima qiladi |
|---|---|
| `GET /api/schedule/lessons/{id}/substitutes` | Shu filialda ishlaydigan, shu vaqtda bo'sh o'qituvchilar |
| `POST /api/schedule/lessons/{id}/replace-teacher` | Almashtiradi. Zaxira o'qituvchining jadvali tekshiriladi (409 konflikt), o'tib ketgan darsni o'zgartirmaydi |

Guruhning har bir o'quvchisi timeline'iga `TEACHER_REPLACED` yoziladi — "o'sha kuni
kim dars bergan" degan savol keyinchalik so'raladi, jadvalda esa faqat zaxira ko'rinadi.
Bildirishnoma: guruh o'quvchilari + ikkala o'qituvchi.

`teacher.replace` ruxsati **Phase 1 dan beri mavjud edi**, faqat endpoint yo'q edi.

Frontend: `frontend/src/app/(admin)/schedule/[lessonId]/page.tsx` ichida
`ReplaceTeacherButton` — o'tib ketgan darsda tugma umuman ko'rinmaydi, zaxira
ro'yxati drawer ochilganda yuklanadi, konflikt bo'lsa ogohlantirish chiqadi.

**Test:** `TeacherReplacementTests.cs` — 8 ta test (band o'qituvchi ro'yxatdan
chiqarilishi, muvaffaqiyatli almashtirish, zaxira band bo'lsa 409, o'tib ketgan
darsda 400, sababsiz so'rovda 400, teacher rolida 403, xona ro'yxati). Brauzerda
ham tekshirildi: yangi kelajakdagi darsga Support Teacher Demo biriktirildi,
ro'yxat qayta ochilganda u endi band, asosiy o'qituvchi esa yana bo'sh ko'rindi.

### 1.10. Xona (Room) datalist (audit §3.11, §5.9)
Backend: `GET /api/schedule/rooms?branchId=…` — filialda ishlatilgan xona
nomlarini qaytaradi. Frontend: `schedule/page.tsx` dagi "Yangi dars" formasida
`<datalist>` orqali taklif qilinadi. Brauzerda tekshirildi: Chilonzor filiali
uchun `["A101", "C303"]` to'g'ri qaytdi.

To'liq `Room` entity (migration bilan, xona-band boshqaruvi) — keyingi bosqich,
hozircha matn maydoni + datalist yetarli.

### 1.11. Universal Search (audit §3.18, §5.14)
Backend: `backend/src/EduCenter.Api/Search/SearchController.cs` — `GET /api/search?q=`.
Yagona "search" ruxsati yo'q: har bir turdagi natija (student, teacher, group,
course, lead, branch, support ticket) o'z ekrani ishlatadigan ruxsat va scope
bilan tekshiriladi (`PermissionResolver.ResolveAsync` to'g'ridan-to'g'ri
chaqiriladi, bitta-endpoint-bitta-policy pipeline'idan tashqarida — xuddi chat
hub'i kabi). Ruxsati yo'q rol uchun natija turi butunlay ko'rinmaydi — 403
o'rniga jimgina yo'q bo'ladi. So'rov 2 belgidan qisqa bo'lsa bo'sh ro'yxat.

Frontend: `CommandPalette.tsx` — navigatsiya ro'yxati bilan bir qatorda, 250ms
debounce bilan backend'ga so'rov yuboradi, natijalar turi bo'yicha ikonka va
label bilan ko'rsatiladi ("Student", "O'qituvchi", "Guruh", "Kurs", "Lid",
"Filial", "Support"), bosilganda tegishli sahifaga o'tadi (student/teacher —
o'z profiliga; group/lead/branch/ticket — ro'yxat sahifasiga, aniq yozuvni
`?highlight=<id>` bilan belgilab — 1.17 ga qarang; course hamon oddiy
`/groups`ga, chunki alohida kurs qatori yo'q).

**Test:** `SearchTests.cs` — 7 ta test (barcha turlarni topish, support ticket
qidiruvi, administrator boshqa filialdagi student/lid/guruhni topa olmasligi,
administrator boshqa filialni topa olmasligi, student roli umuman filial/lid
natijasini olmasligi — ruxsat darajasida). Brauzerda tekshirildi: "Feruza"
qidiruvi ham student, ham lid sifatida topdi; "IELTS" guruh va kurslarni topdi;
"Yunusobod" administrator uchun natija bermadi (superadmin uchun beradi).

### 1.12. Student 360° qolgan tablari — to'liq (audit §3.7)
Uchala band ham yopildi:

- **Documents tab.** Yangi `StudentDocument` entity (`Modules.Students.Domain`,
  migratsiya `AddStudentDocuments`) — `LearningResource` bilan bir xil "havola
  yoki fayl" shakli. Backend: `backend/src/EduCenter.Api/Students/
  StudentDocumentsController.cs` + `StudentDocumentFileStorage.cs` (disk:
  `App_Data/student-documents`, 10 MB chegara, pdf/doc/docx/rasm). Ruxsatlar:
  `student.documents.view` (SuperAdmin/Administrator/Operator + student o'zi +
  ota-ona), `student.documents.manage` (faqat ofis xodimlari — teacher yo'q,
  chunki bu pedagogik emas). Frontend: `components/StudentDocuments.tsx`
  (`LessonResources.tsx` naqshi asosida), "Hujjatlar" tab.
- **Communication tab.** `components/OpenChatButton.tsx` — mavjud
  `POST /api/chats/direct` orqali suhbat ochib, `/chat?c=<id>` ga o'tkazadi.
  Yangi backend kod kerak bo'lmadi — `ChatAccess.ContactIdsAsync` allaqachon
  kimga yozish mumkinligini hal qilardi. Header'da "Xabar yozish" tugmasi va
  alohida "Muloqot" tab (o'z profilida ko'rinmaydi).
- **Payment holati.** Header'dagi statistika qatoriga "Balans" kartasi
  qo'shildi (`getStudentFinance`), faqat `canSeeFinance` rollari uchun — qarz
  bo'lsa qizil, balans bo'lsa sariq, aks holda brand rang.

**Test:** `StudentDocumentsTests.cs` — 6 ta test (upload/download/delete round-trip,
havola qo'shish, teacher butunlay ko'ra olmasligi, administrator boshqa filialga
kira olmasligi, student o'zinikini ko'radi lekin yukla olmaydi). Brauzerda
tekshirildi: Feruza Toshevaga havola-hujjat qo'shildi va ko'rindi, "Muloqot"
tab suhbat ochdi, "Balans: 950 000 so'm" ko'rindi; teacher sifatida kirilganda
"Hujjatlar" va "Balans" umuman ko'rinmadi (ruxsat yo'qligi sababli), "Muloqot"
esa ko'rindi.

### 1.13. Teacher 360° — Performance va Chat (audit §3.6, §5.13)
`/teachers/[id]` da yangi "Natijadorlik" tab: `GET /api/teachers/{id}/performance`
— davomat, homework bajarilishi, o'rtacha imtihon balli, mavjud ma'lumotlardan
hisoblanadi (yangi jadval kerak emas). TZ'dagi to'liq KPI modeli (sozlanadigan
og'irliklar, punctuality, feedback) **ataylab qilinmadi** — bu allaqachon
kodda "Phase 6" deb belgilangan (`TeachersController` klass izohi) va feedback
yig'ish mexanizmi yo'q holda soxta raqam ko'rsatishdan ko'ra ochiq qoldirish
maqbulroq. "O'qituvchining o'z davomati" (punctuality) ham shu sababli yo'q —
buni kuzatadigan infratuzilma umuman yo'q.

Chat: xuddi student profilidagidek, header'da "Xabar yozish" tugmasi
(`OpenChatButton`, o'z profilida yashiriladi).

**Test:** `TeacherPerformanceTests.cs` — 5 ta test (ruxsatsiz so'rov, student
id bilan so'ralsa 404, o'z profilini ko'rish, administrator boshqa filialdagi
teacher'ni ko'ra olmasligi, yangi ma'lumotdan davomat foizi to'g'ri hisoblanishi
— 2 studentdan 1 tasi kelgan bo'lsa 50%). Brauzerda tekshirildi: Support Teacher
Demo uchun "Natijadorlik" tabida real raqamlar (Davomat 77.8%, Homework 33.3%,
O'rtacha ball 6.62) chiqdi.

### 1.14. CRM Operator call natijasi UX (audit §3.8)
Backend allaqachon tayyor edi (`POST /api/leads/{id}/calls`) — faqat frontend
qulayligi yetishmasdi: `/crm/tasks` navbatidagi vazifani yopish uchun operator
avval `/leads`ga o'tib, lidni qidirib, keyin qaytib vazifani "Bajarildi" qilishi
kerak edi. Endi `leadId` bilan bog'langan vazifa kartasida (CallBack, FollowUp)
to'g'ridan-to'g'ri "Qo'ng'iroq" tugmasi bor — bosilganda `/leads`dagi bilan bir
xil natija-drawer ochiladi (natija + izoh), saqlanganda `logCall` chaqiriladi
**va** o'sha vazifa avtomatik yopiladi (`completeCrmTask`) — ikkita alohida
sahifaga borish o'rniga bitta karta ustida bitta amal. `CALL_OUTCOME_LABEL`
takrorlanishning oldini olish uchun `lib/api/crm.ts`ga ko'chirildi (ilgari
faqat `/leads` sahifasida edi). Debt/Absence vazifalari (`studentId` bilan,
`leadId`siz) tugmani ko'rmaydi — `CrmCall` faqat lid bilan bog'lanadi, domenda
studentga qo'ng'iroq yozib bo'lmaydi.

Yangi backend kod yo'q, migratsiya yo'q — 124/124 test o'zgarishsiz o'tdi.
Brauzerda operator sifatida tekshirildi: mavjud CallBack vazifasida "Qo'ng'iroq"
bosilib "Interested" natija saqlandi — vazifa navbatdan yo'qoldi (0/0/0),
lidning holati `/leads`da "Bog'lanildi"ga o'zgardi; boshqa lidga "Keyinroq
qo'ng'iroq qilish" natijasi bilan qo'ng'iroq yozilganda ertaga uchun yangi
CallBack vazifa avtomatik paydo bo'lgani ham tasdiqlandi.

### 1.15. Group Recommendation Engine (audit §3.9, TZ §12)
`GET /api/leads/{id}/group-recommendations` (`LeadsController.cs`) — lidning
filialida ko'rinadigan, o'rni bo'sh guruhlarni ballab, eng mosini birinchi
qatorga chiqaradi. TZ'da sanalgan omillardan faqat domenda haqiqatan mavjud
bo'lganlari hisoblanadi: qiziqqan kurs nomi mosligi (50%), lidning filiali bilan
bir xilligi (25%), bo'sh o'rin (20%, nisbatan), guruh allaqachon faolligi (5%).
"Student darajasi", "qulay vaqt" va "o'qituvchi bandligi" — TZ ro'yxatida bor,
lekin bazada bunga mos maydon yo'q (Lead'da daraja yo'q, Group'da haftalik
jadval yo'q), shuning uchun Teacher Performance'dagi kabi **ochiq qoldirildi**,
soxta raqam ko'rsatilmadi. O'rni yo'q guruhlar ro'yxatga umuman kirmaydi.

Frontend: `/leads` sahifasidagi "Guruh" tanlagichi (Convert va Trial
drawer'larining ikkalasida ham, `TrialDrawer.tsx`) endi ikki guruhga bo'lingan:
"Tavsiya etilgan" (foiz bilan) va "Boshqa guruhlar" (`<optgroup>`).

**Test:** `GroupRecommendationTests.cs` — 3 ta test (ruxsatsiz so'rov, kursi va
filiali mos guruh yuqori ball olishi va to'lgan guruh butunlay chiqarilishi,
administrator boshqa filialdagi guruhni ko'rmasligi). Brauzerda tekshirildi:
Nurmuhammad Samatov (qiziqqan kurs: Elementary) uchun "GenEng-A2-1 — 49% mos"
birinchi chiqdi.

### 1.16. Placement Test (audit §3.10, TZ §13)
Yangi domen: `PlacementAttempt` (`Modules.Crm.Domain`, migratsiya
`AddPlacementAttempts`) va operator tomonidan boshqariladigan, kodda yozilgan
12 ta savoldan iborat grammatika/lug'at testi (`PlacementQuestionBank`) — bu
kontent, foydalanuvchi ma'lumoti emas, shuning uchun bazaga urug'lanmaydi.
Backend (`LeadsController.cs`):

| Endpoint | Nima qiladi |
|---|---|
| `GET /api/placement-test/questions` | Savollar, **javob kaliti yo'q** |
| `POST /api/leads/{id}/placement-test` | Javoblarni serverda baholaydi, `PlacementAttempt` yozadi |
| `GET /api/leads/{id}/placement-test` | Shu lidning oldingi urinishlari tarixi |

Baholash: har savolning "og'irligi" bor (1-5, TZ darajasiga qarab), yig'indi
foizga aylantirilib, 8 ta teng qismga bo'linadi → A1/A2/B1/B2/C1/IELTS
Foundation/Intermediate/Advanced (TZ §13'dagi aynan shu ro'yxat). Yangi ruxsat:
`placement.test.log` (SuperAdmin/Administrator/Operator, `trial.book` bilan bir xil shaklda).

Frontend: `/leads` sahifasida "Daraja testi" tugmasi → `PlacementTestDrawer.tsx`
(savollar + radio, oldingi urinishlar tarixi, natija kartasi). "Studentga
aylantirish" drawer'ida yangi "Daraja" maydoni — oxirgi urinish natijasi
taklif qilinadi, "qo'llash" tugmasi bosilsa maydonga to'ladi (avtomatik emas —
operator har doim o'zi tasdiqlaydi), `ConvertLeadInput.currentLevel`ga boradi.

**Test:** `PlacementTestTests.cs` — 7 ta test (savollarda javob kaliti
chiqmasligi, hammasi to'g'ri → "IELTS Advanced", hammasi noto'g'ri → "A1",
tarix qaytishi, teacher ruxsatsizligi, administrator boshqa filialdagi lidga
kira olmasligi). Brauzerda tekshirildi: operator sifatida 12/12 to'g'ri javob
berilib "IELTS Advanced" chiqdi, Convert drawer'da taklif ko'rindi va
"qo'llash" bosilganda "Daraja" maydoniga to'g'ri yozildi.

### 1.17. Groups/Leads/Branches/Support uchun highlight (audit §3.18 bilan bog'liq)
Universal Search (1.11) bu to'rtta turni ro'yxat sahifasiga olib borardi, lekin
aniq yozuvni topib bo'lmasdi. To'liq detail-route yasash o'rniga (bu sahifalar
hali card/tab asosida, alohida route'ga ega emas) — `SearchController.cs`
endi har biriga `?highlight=<id>` qo'shadi; `lib/use-highlight-target.ts`
(`useHighlightTarget` hook) shu parametrni o'qib, mos elementni ekranga
skroll qiladi va `bg-brand-50` bilan bo'yaydi (ring emas — `box-shadow`
`<tr>` elementlarida ko'p brauzerda ishlamaydi, shuning uchun fon rangi
tanlandi, xuddi `ChatList`dagi "tanlangan chat" konventsiyasi kabi).
Support sahifasida qo'shimcha murakkablik bor edi: case uchta tab (Ko'rib
chiqish/Jarayonda/Yopilgan) orasida bo'linadi — `Tabs.tsx`ga ixtiyoriy
`defaultTabId` prop qo'shildi, sahifa case'ning statusidan kerakli tabni
oldindan hisoblab beradi.

Course qidiruv natijasi hamon oddiy `/groups`ga boradi (highlight'siz) —
default ko'rinishda alohida kurs qatori yo'q, faqat `canManage` rejimida
`CourseFees` jadvalida bor, shuning uchun bu holat kichik va alohida qoldirildi.

**Test:** mavjud `SearchTests.cs` uchta assertion yangilandi (endi aniq
`?highlight=<id>` hrefini tekshiradi). Brauzerda tekshirildi: qidiruv orqali
"Nurmuhammad Samatov"ga bosilganda `/leads?highlight=...` ochildi va qator
fon rangi bilan ajralib turdi; xuddi shu narsa `/branches` va `/groups` uchun;
`/support`da "Yopilgan" statusidagi case uchun sahifa to'g'ridan-to'g'ri
"Yopilgan" tabini ochib, case'ni bo'yab ko'rsatdi.

### 1.18. Bilim bazasi: "Fayl saqlash joyida topilmadi" (foydalanuvchi xabari)
Sabab `dotnet-in-docker.sh` `api` buyrug'ida `App_Data` volume'i yo'q paytda yuklangan 6 ta hujjatning
**fayli** yo'qolgan, lekin bazadagi qator va indekslangan bo'laklar (`knowledge_chunks`) saqlanib qolgan.
Volume allaqachon tuzatilgan (`educenter-app-data`), yangi yuklashlar yo'qolmaydi; eskilarini shunday qutqardik:
- `GET .../documents/{id}/preview` fayl yo'q bo'lsa matnni **bo'laklardan tiklaydi** (`KnowledgeChunker.Reassemble`
  — bo'laklar orasidagi 150 belgilik ustma-ustlikni olib tashlaydi), javobda `restored: true`. Bo'lak ham yo'q
  bo'lsa (karantindagi hujjat) aniq xabar: "qayta yuklang".
- Ro'yxat DTO'siga `fileAvailable`; UI'da "Asl fayl yo'qolgan" belgisi, ishlaydigan indeksni `Failed` ga
  aylantirib qo'ymaslik uchun **Qayta indekslash** va **Tasdiqlash** tugmalari yashiriladi (server ham
  `reingest` ga 409 qaytaradi).
- Faylning o'zi (yuklab olish) tiklanmaydi — kerak bo'lsa hujjatni qayta yuklash kerak.

### 1.19. Har bir rol o'ziga mos amallarni ko'radi (foydalanuvchi xabari §2)
Frontend endi rol nomini emas, **ruxsatni** so'raydi: `GET /api/me/permissions` (`MeController`, override rol
default'idan ustun — `PermissionResolver.ListAsync`) → `lib/use-permissions.ts` (`can(code)`).
- **O'qituvchilar:** ro'yxatda hamkasblar ko'rinadi, lekin faqat o'z profili havola (`TeacherListDto.CanOpen`);
  ilgari bosilganda "ruxsat yo'q" chiqardi.
- **Bilim bazasi:** `GET /api/ai/knowledge/targets` — yuklash formasi faqat ruxsat etilgan doirani ko'rsatadi
  (o'qituvchiga "Butun tashkilot"/"Filial" yo'q, faqat kurs va o'z guruhlari). Server tomonda o'qituvchi
  endi filialga ham material biriktira olmaydi (`Reach.AttachBranchIds`), ilgari kod izohi bilan zid edi.
- **Dars sahifasi:** yordamchi o'qituvchiga "Davomatni saqlash"/"Vazifa yaratish" endi ko'rinmaydi
  (`attendance.mark`/`homework.create` yo'q) — ilgari 403 bilan tugardi.
- Hali rol nomiga tayangan joylar (`canManageAcademic`, `exams`, `SupportCases`, `Finance`) matritsaga mos,
  shuning uchun teginilmadi; yangi ekranlar `usePermissions` ishlatadi.

### 1.20. Guruh detali `/groups/[id]` (foydalanuvchi xabari §3)
`GET /api/groups/{id}/overview` (`groups.view`, scope bilan): kurs/filial, o'qituvchilar, o'rin (n / sig'im),
davomat %, uy vazifa bajarilishi %, o'rtacha imtihon balli, har bir talaba uchun davomat/kelmagan/vazifa/imtihon
va yaqin 4 ta dars. **Student/parent** guruhni ochadi, lekin `results = null` — sinfdoshlari natijasi shaxsiy.
Guruhlar ro'yxati kartasi endi havola, `studentCount` (`GroupDto`) va "Batafsil" bor; 6 tadan ko'p guruhda
qidiruv; kurs narxlari ro'yxati guruhlar ostiga tushirildi; yordamchi o'qituvchi ismlari (ilgari ID) ko'rinadi.
Universal Search guruh natijasi endi `/groups/{id}` ga olib boradi.

### 1.21. Dars jadvali: hafta kartalari va kun modali (foydalanuvchi xabari §4)
`/schedule` — 7 ta kun kartasi (mobil 2 ustun kichik, `sm` 3, `lg` 4, `xl` 7 ustun katta; kichik ekranda faqat
son, katta kartada 3 tagacha dars ko'rinadi). Karta bosilsa `Modal` (`components/ui/Modal.tsx`, telefonda pastdan
chiqadigan varaq): kundagi hamma dars — vaqt, guruh, xona, o'qituvchi (`LessonDto.TeacherName`), mavzu va
**"Davomat va vazifa"** (→ `/schedule/{id}`) hamda **"Guruh"** (→ `/groups/{id}`) tugmalari. Darsi yo'q kun
bosilmaydi.

### 1.22. Katta demo ma'lumot (foydalanuvchi xabari)
`DemoDataSeeder` (`Infrastructure/Seed/`): **180 talaba** (+ 4 eski), **19 guruh** (3 filial: Chilonzor,
Yunusobod, yangi Sergeli), 14 o'qituvchi (10 asosiy + 4 yordamchi) + filial administratorlari, 60 ota-ona
hisobi, 13 kurs, 8 haftalik jadval (**521 dars**, xona/o'qituvchi/guruh to'qnashuvi **0** — SQL bilan tekshirilgan),
**2551 davomat**, uy vazifa + topshiriqlar, imtihon natijalari, orqaga sanalangan enrollment bo'yicha
**573 hisob-faktura / 469 to'lov** (qarzdorlar bilan), 46 ta lid (qo'ng'iroq navbati bilan). Deterministik
(`Random(20260921)`), idempotent (marker: `IELTS-22` guruhi).
**Yoqilishi:** faqat `Seed:BulkDemoData=true` bilan — `dotnet-in-docker.sh api` buni o'zi beradi. Testlar ham
`Development` muhitida seed qiladi va qatorlarni sanaydi, shuning uchun bayroqsiz katta ma'lumot 158 testni buzar edi.
Demo parol hammasiga bir xil (`Passw0rd!`); o'quvchi emaillari `ism.familiya@demo-center.test`.

### 1.23. Developer imtiyozlari (foydalanuvchi xabari, TZ §3.8, §53)
Developer ilgari faqat bitta monitoring ekraniga ega edi. Endi TZ ro'yxatining qolgani ham bor
(`Observability/` — `system` nomi bilan to'qnashmasligi uchun shunday atalgan):
| Ruxsat | Nima ochadi |
|---|---|
| `system.metrics.view` | `/system` → so'rovlar/daqiqa, kechikish p50/p95/p99, 5xx ulushi, 30 daqiqalik grafik, eng sekin endpointlar (route shabloni bo'yicha), baza ulanishlari, sekin so'rovlar (`pg_stat_activity`; `pg_stat_statements` bo'lsa tarix), disk va `App_Data` hajmi |
| `system.jobs.view` | Hangfire: navbat/bajarilmoqda/xato, takrorlanuvchi vazifalar, oxirgi xatolar (Hangfire o'chiq muhitda `available: false`) |
| `logs.view` | oxirgi 200 ta server xatosi (5xx): so'rov, exception turi, traceId |
| `deployments.view` | versiya, muhit, runtime, qo'llangan/qo'llanmagan migratsiyalar |
| `production.data.request` | Request → Reason → Approval → Expiration: `/system/access` |
`production.data.approve` — Super Admin. Tasdiqlangan ruxsat `PermissionResolver` orqali **faqat bitta o'qish
ruxsati** (talabalar/guruhlar/davomat/imtihon/uy vazifa; moliya va hisoblar ro'yxatda yo'q) va faqat `ExpiresAt`
gacha ishlaydi; so'rov va qarorlar audit jurnaliga tushadi. Hisoblagichlar (`RequestMetrics`) xotirada va API
qayta ishga tushganda tozalanadi — ekranda "dan beri yig'ilmoqda" deb yozilgan. Migratsiya: `AddDataAccessRequests`.
**Yo'q (ataylab):** Redis holati (Redis ishlatilmaydi), texnik support ticketlari, so'rov paytida ishlatilgan
ma'lumotni kim ko'rgani (faqat so'rov/qaror auditlanadi), so'rov kelganda Super Admin'ga bildirishnoma.

**Test:** `RoleExperienceTests` (8), `DeveloperAccessTests` (16) — 158 (134 + 24).

### 1.25. iPad'da foydalanuvchi menyusi va sahifalash (foydalanuvchi xabari)
**Menyu.** Sidebar pastidagi akkaunt kartasi (profil / bildirishnoma sozlamalari / chiqish) faqat CSS `group-hover` va
`focus-within` bilan ochilardi. Sensorli ekranda hover yo'q, Safari esa bosilgan tugmaga fokus bermaydi — iPad'da menyu
(va chiqish) umuman ochilmasdi. Endi `components/UserMenu.tsx`: bosish bilan ochiladi/yopiladi, tashqariga bosish
(`pointerdown`) yoki Escape yopadi, havoladan keyin mobil sidebar ham yopiladi. Xuddi shu sabab chatdagi xabar amallari
(javob/reaksiya/o'chirish, `opacity-0` + hover) sensorli qurilmada yashirin edi — `pointer-coarse:opacity-100`.
**Sahifalash.** Barcha katta ro'yxatlar serverda `Page<T>` (`PagingExtensions.ToPageAsync`, `size` ≤ 200, standart 25),
qidiruv va filtr bazada (`ILIKE`, `%`/`_` escape qilinadi), frontendda `useDebouncedValue` (300 ms) + `keepPreviousData`:
| Ekran | Nima o'zgardi |
|---|---|
| Studentlar | `?search&status&groupId&page&size` — ilgari hammasi birdan |
| Imtihonlar | `?groupId&search&page`; natijalar drawer'i endi barcha studentlarni emas, `ExamResultDto.StudentName` ni ishlatadi |
| Moliya | `invoices` (`status/open/overdue/search`), `debtors`, `payments`, `refunds` — sahifalangan. **Qarzdorlar** ilgari barcha ochiq invoice'ni xotiraga yuklardi; endi `GROUP BY student` bazada. "Muddati o'tgan" filial vaqt zonasiga bog'liq — `FinanceQueries.OpenOverdueAsync` uni SQL ichida zona bo'yicha quradi. Yuqoridagi to'rt raqam `GET /api/finance/overview` dan (ilgari brauzerda ro'yxatlardan yig'ilardi) |
| Support | `statuses[]` + `search` + `page`; tab sonlari `size=1` so'rovlardan; qidiruv orqali ochilgan yozuv uchun `GET /api/support-cases/{id}` |
| CRM vazifalari | `bucket=overdue|upcoming` — har birining o'z soni va pager'i |
| Lidlar doskasi | har ustun alohida `useInfiniteQuery` (20 tadan, "Yana ko'rsatish"), `statuses[]`, manba filtri va qidiruv serverda; jami/konversiya ustun `total`laridan |
| Student timeline | eng so'nggi 200 (`?limit`, ≤ 500) — har davomat va vazifa hodisa qo'shadi |
**Ataylab sahifalanmadi** (cheklangan yoki kichik): guruhlar/kurslar/filiallar/o'qituvchilar (o'nlab, boshqa
ekranlarda tanlagich sifatida ishlatiladi), dars davomati va guruh ro'yxati (guruh hajmi bilan cheklangan), bitta
student profilidagi tablar, imtihon natijalari (bitta imtihon), bilim bazasi (`Take(200)`), bildirishnomalar (`limit ≤ 100`),
AI tarixi (`Take 50-100`), qidiruv (tur bo'yicha limit). **Keyingi qadam:** ~10 000+ foydalanuvchida `ILIKE '%…%'` indeks
ishlatmaydi — `pg_trgm` GIN indeksi kerak bo'ladi (users.FullName/Email/Phone, leads.FullName).
**Test:** `PagingTests` (13) — jami **173**. Brauzerda tekshirildi: talabalar (84 ta, 25 tadan, qidiruv), moliya
(46 qarzdor, 50 025 000 so'm), doska, iPad 1180×820 va 820×1180 da menyu.

### 1.24. Lid → trial → student oqimi (foydalanuvchi xabari: "trialdan keyin qanday student qilaman?")
**Muammo:** "Studentga aylantirish" tugmasi faqat lid **profili** ichida edi; doskadagi kartada faqat "Qo'ng'iroq"
bor, "Studentga aylandi" ustuni esa o'chirilgan edi — sudralganda jimgina hech narsa bo'lmasdi. Kartani
"Trial'da qatnashdi" ustuniga sudrash trialni emas, faqat yorliqni o'zgartirardi (trial `Scheduled` qolardi,
dev bazada shunday lidlar bor edi). Guruh tanlash oddiy ro'yxat edi: kim o'qitishi, bo'sh o'rin, dars vaqti
ko'rinmasdi, trial vaqti esa qo'lda yozilardi. Ruxsat muammosi **yo'q**: Administrator ham, Operator ham
`leads.edit`/`trial.book`/`students.create` ga ega (testlar tasdiqlaydi).

**Endi (`/leads` doskasi):** har bir kartada shu bosqichning keyingi amali — Yangi/Bog'lanildi: *Trialga yozish*;
Trial band: trial (guruh, sana) + *Keldi / Kelmadi*; Trial'da qatnashdi: asosiy tugma *Studentga aylantirish*;
Studentga aylandi: *Student profili* havolasi. Kartani "Studentga aylandi" ga sudrash konvertatsiya oynasini ochadi;
"Trial band" ga sudrash trial band qilish oynasini ochadi; "Trial'da qatnashdi" ga sudrash `Scheduled` trialni
`Attended` qiladi (lid va trial birga yangilanadi).
**Trial oynasi:** guruhlar karta ko'rinishida — o'qituvchi, bo'sh o'rin, xona, nega mos (`GroupRecommendationDto.
TeacherName/NextLessons`); dars vaqti guruhning haqiqiy yaqin darslaridan tanlanadi (yoki "boshqa vaqt");
"Keldi" bosilgach shu oynaning o'zida *Studentga aylantirish* chiqadi va trial guruhi oldindan tanlanadi.
**Konvertatsiya oynasi:** nima sodir bo'lishini aytadi (login, guruhga yozish, birinchi hisob-faktura),
tugagach login/parol eslatmasi va *Student profilini ochish*. **Lid profili:** "Keyingi qadam" bloki har bosqichda
nima qilish kerakligini yozadi va aynan shu tugmani beradi; yopilgan lidni *Qayta ochish* mumkin.
Backend: `LeadDto.LatestTrial` (ro'yxat va bitta lid), `GroupRecommendationDto.TeacherName/NextLessons`.
Demo seed'ga kelgan/kelmagan trial'li lidlar qo'shildi. **Test:** `LeadTrialFlowTests` (3) — jami **161**.
Brauzerda tekshirildi (administrator): trialga yozish → Keldi → Studentga aylantirish → student yaratildi.
Sudrash (drag) yo'llari kod bilan yozilgan, lekin Playwright bilan qo'lda sinalmagan.

### 1.26. Studentni to'g'ridan-to'g'ri yaratish va tahrirlash (audit K1; reja `docs/11` 1.1)
**Muammo:** student faqat lid konvertatsiyasining oxirida paydo bo'lardi (`new Student(...)` butun kodda shu yagona
API yo'lida). `POST /api/users` STUDENT roli bilan `User` yaratardi, lekin `Student` qatorini emas — bunday odam
kira olardi, lekin ro'yxatda ko'rinmasdi. Boshqa tizimdan ko'chib kelayotgan markaz yuzlab talabani lid qilib
kirita olmasdi.
**Endi:**
| Endpoint | Ruxsat | Nima qiladi |
|---|---|---|
| `POST /api/students` | `students.create` | `User` + `Student` + (guruh berilsa) `Enrollment`, timeline `ENROLLED` va birinchi oylik invoice — bitta saqlashda |
| `PATCH /api/students/{id}` | `students.edit` *(yangi)* | ism, telefon, daraja, qo'shilgan sana, student raqami |
| `PATCH /api/students/{id}/contact` | `students.contact.edit` *(yangi)* | faqat ism va telefon |
Qarorlar va rejadan farqlar:
- **Filial xodimi guruhsiz student yarata olmaydi** (400). `MyBranches` scope studentni faqat filial guruhiga yozilgan
  bo'lsa ko'radi (`StudentAccess`), shuning uchun guruhsiz student o'zini yaratgan administratorga ham ko'rinmay
  qolardi. Guruhsiz ro'yxatga olish faqat `AllOrganization` (Super Admin) uchun.
- **Operator uchun ikki ruxsat.** Matritsada `students.edit` — Operator uchun "limited (contact info)", scope buni
  ifodalay olmaydi (`finance.debt.view` bilan bir xil holat), shuning uchun `students.edit` (SA, AD — hamma maydon) va
  `students.contact.edit` (SA, AD, OP — ism/telefon). Rejada operatorga to'liq `students.edit` berilgan edi.
- **Student raqami** ixtiyoriy berilishi mumkin (o'z raqamlash tizimini saqlagan markaz uchun), takrorlansa 409.
  Bo'sh bo'lsa `STU-yyyyMM-NNNN` (`StudentNumbers.NextAsync` — ilgari `LeadsController` ichida yopiq metod edi).
- **Guruh tekshiruvi umumiy:** `EnrollmentRules.CheckAsync` (mavjudmi, filial scope'ida, yopilmagan, o'rni bor,
  allaqachon yozilmaganmi). `Transfer` ham shunga o'tdi, statuslari o'zgarmadi (404/403/400). Yangi endpoint to'lgan/yopiq
  guruhga 409 qaytaradi — so'rov to'g'ri, rad etayotgan guruh holati.
- `PasswordPolicy.Validate` — parol uzunligi tekshiruvi uch joyda takrorlanmasin (`UsersController`, lid konvertatsiyasi, yangi endpoint).
- `StudentDto` ga `Phone` qo'shildi (profil sarlavhasi va tahrirlash oynasi uchun; TZ §7 sarlavhasida bor edi).
- Email bu yerdan o'zgarmaydi — u login identifikatori, alohida endpoint kerak (audit K8).
- Rejadagi "student + ota-onaga bildirishnoma" **qilinmadi**: yangi studentda hali ota-ona yo'q (1.2), o'zining yangi
  hisobiga "xush kelibsiz" xabari esa foyda bermaydi.
**Ma'lum holat:** guruh berilganda joriy oy invoice'i darhol chiqadi (lid konvertatsiyasidagi kabi). Boshqa tizimdan
ko'chirilayotgan va bu oy allaqachon to'lagan student uchun u ortiqcha qarz bo'ladi — Moliya'da bekor qilinadi.
Oynada shu haqida yozilgan; agar ommaviy ko'chirish ko'p bo'lsa, `issueFirstInvoice` bayrog'i qo'shilishi mumkin.
**Frontend:** `StudentCreateDrawer.tsx` (`/students` sarlavhasida "Yangi student"; parol generatori; saqlagach login/parol
ko'rsatiladi; "Yana bitta qo'shish" guruh, daraja va sanani saqlaydi — sinfni ketma-ket kiritish uchun),
`StudentEdit.tsx` (profil sarlavhasida "Tahrirlash"; ruxsatga qarab hamma maydon yoki faqat ism/telefon).
**Test:** `StudentAdminTests` (13) — jami **186**. Brauzerda tekshirildi (administrator, operator, teacher, super admin;
22 ta tekshiruv): yaratish → login/parol → "yana bitta" → profil (telefon, daraja, balans) → tahrirlash → xato xabari;
operator faqat ism/telefon ko'radi va o'zgartiradi; teacher'da tugma yo'q; Super Admin'da "Guruhsiz" tanlovi bor.
**Topilma (bu ishga aloqasiz):** operator student profilini ochganda `GET /api/students/{id}/finance` 403 beradi —
sahifa moliya kartasini rol nomi (`canSeeFinance`) bo'yicha so'raydi, operatorda esa `payments.view` yo'q
(faqat `finance.debt.view`). Ekranda ko'rinmaydi, lekin 1.19 dagi "matritsaga mos" degan izoh bu joy uchun to'g'ri emas.

### 1.27. Ota-ona va farzand bog'lami (audit K2; reja `docs/11` 1.2; `docs/21.09-audit-jurnali/S01.02`)
**Muammo:** `Parent`/`StudentParent` jadvallari bor edi, lekin ularni faqat `DbSeeder`/`DemoDataSeeder` yozardi — hech
qanday endpoint ota-onani ro'yxatga olib farzandga bog'lamasdi. Ota-ona kirsa `/api/dashboard/parent` bo'sh ro'yxat
qaytarardi (TZ §24 Parent Portal ishlamas edi). Yana bir teshik: `POST /api/users` `roleCode=STUDENT` yoki `PARENT`
bilan faqat `User` yaratardi — `Student`/`Parent` qatorisiz "yetim" hisob.
**Endi:**
| Endpoint | Ruxsat | Nima qiladi |
|---|---|---|
| `GET /api/students/{id}/parents` | `students.view` | vasiylar ro'yxati: ism, telefon, email, munosabat |
| `POST /api/students/{id}/parents` | `parents.manage` *(yangi)* | ikki rejim: mavjud PARENT hisobni biriktirish yoki yangisini yaratib biriktirish |
| `DELETE /api/students/{id}/parents/{parentId}` | `parents.manage` | bog'lamni uzadi; `Parent` qatori qoladi (boshqa farzandi bo'lishi mumkin) |
- `StudentParent.Relationship` (`ParentRelationship`: Father/Mother/Guardian/Other) — migratsiya `AddParentRelationship`,
  eski qatorlarga backfill `Guardian` (bo'sh satr emas — aks holda enum deserializatsiyasi buziladi).
- Yangi hisob rejimida filial: studentning faol enrollmentlari guruhlari filiallari beriladi (`user.AssignBranch`).
- `POST /api/users` va `POST /api/users/{id}/roles`: `roleCode=STUDENT` endi **400** ("Student `/students` sahifasidan
  yaratiladi"); `PARENT` endi `User` bilan birga `Parent` qatorini ham yozadi. `UserDirectory.tsx` rol tanlagichidan
  STUDENT olib tashlandi, o'rniga `/students`ga havola.
- `IsValidEmail` (`StudentAdminController`) umumiy `Api/Auth/EmailPolicy.cs`ga chiqarildi (S01.01 qoldirgan ish).
- **Xato topilib tuzatildi:** yangi ota-ona hisobi yaratilganda bildirishnoma jo'natilmayotgan edi —
  `NotificationDispatcher.NotifyAsync` qabul qiluvchini bazadan qidiradi, shu so'rovning o'zi yozgan `User`
  qatori esa hali saqlanmagan edi. Tuzatish: link yozuvlarini avval saqlab, keyin bildirishnoma yuborish.
**Frontend:** `StudentParents.tsx` — student profilining "Umumiy" tabida karta (ism, munosabat, telefon, uzish);
"Qo'shish" → `ParentLinkDrawer` (tab: "Yangi hisob" / "Mavjud hisob", oxirgisi `GET /api/users?role=PARENT&search=`
bilan qidiradi). Sarlavhaga birinchi ota-onaning nomi/telefoni qo'shildi.
**Test:** `StudentParentsTests` (7) — jami **193**. Brauzerda tekshirildi (administrator): mavjud demo studentda
seed qilingan ota-ona to'g'ri ko'rinadi ("Vasiy" backfill), yangi ota-ona yaratish → parol paneli → darhol ro'yxatda
→ ota-ona sifatida kirib "Farzandlarim"da farzandni va bildirishnomani ko'radi → uzish → karta yo'qoladi.
`/accounts` yaratish oynasida Student endi yo'q, o'rniga `/students`ga havola.
**Dev bazada qoldiq:** "Sinov Ona" (`sinov.ona.parent@demo-center.test`) — tekshiruv uchun yaratilgan ota-ona
hisobi, hech qanday farzandga bog'lanmagan (test paytida uzildi). `Parent` qatori borligi uchun UI orqali
o'chirib bo'lmaydi (faqat bloklanadi) — foydalanuvchi so'ramaguncha tegmang.

### 1.28. Guruhga yozish, chiqarish, bitirish (audit K3; reja `docs/11` 1.3; `docs/21.09-audit-jurnali/S01.03`)
**Muammo:** mavjud studentni ikkinchi guruhga yozib bo'lmasdi, guruhdan chiqarib bo'lmasdi, `EnrollmentStatus.Completed`
umuman yo'q edi, `StudentStatus.Graduated`/`Dropped` hech qachon yozilmasdi (TZ §10 ning oxirgi holatlariga erishib
bo'lmasdi). Yana ikki teshik: `LeadsController.Convert`/`BookTrial` guruh to'lganmi/yopiqmi tekshirmasdi.
**Endi:**
| Endpoint | Ruxsat | Nima qiladi |
|---|---|---|
| `POST /api/students/{id}/enrollments` | `student.enroll` *(yangi)* | qo'shimcha guruhga yozadi; `Dropped`/`Graduated` student `Active`ga qaytadi |
| `POST /api/students/{id}/enrollments/{enrollmentId}/drop` | `student.enroll` | sabab majburiy → `Dropped` + `EndedAt`/`EndReason` |
| `POST /api/students/{id}/graduate` | `student.enroll` | faol+muzlatilgan enrollmentlarni `Completed`ga, student `Graduated` |
- **`StudentStatusRules.Recompute`** (`Api/Students/`) — bitta qoida: faol enrollment bor → (`Debt` bo'lsa `Debt` qoladi,
  aks holda `Active`); faqat muzlatilgan → `Frozen`; yo'q va oxirgisi `Completed` → `Graduated`; oxirgisi `Dropped` →
  `Dropped`. `Transfer`/`Freeze`/`Resume` ham shu qoidadan foydalanadi endi (ilgari statusni qo'lda qo'yishardi) —
  mavjud 9 ta lifecycle test o'zgarishsiz o'tdi.
- `Enrollment.End(status, reason)` — `Dropped`/`Completed`/**faqat** `Transferred` uchun `EndedAt` qo'yadi (freeze
  vaqtinchalik, `EndedAt` olmaydi). Migratsiya **`AddEnrollmentEnd`** (`EndedAt`, `EndReason`, nullable — backfill shart emas).
- `EnrollmentStatus.Completed` qo'shildi (matn sifatida saqlanadi — migratsiya kerak emas).
- `LeadsController.Convert`/`BookTrial`: guruh tekshiruvi endi `EnrollmentRules.CheckAsync` orqali — to'lgan/yopiq
  guruhga endi **409** (ilgari faqat scope tekshirilardi, `EnsureGroupInScopeAsync` o'chirildi).
- **Xato topilib tuzatildi:** `Enroll`da yangi yozilgan enrollment `StudentStatusRules.Recompute`ga berilayotganda
  saqlanmagan (`Add`) entity requery bilan ko'rinmasligi mumkin edi — oldindan so'ralgan ro'yxatga qo'lda qo'shib
  yuborildi (`[.. priorEnrollments, enrollment]`), xuddi shu naqsh `Transfer`da ham qo'llandi.
- `EnrollmentSummaryDto`ga `Id` qo'shildi (frontendga qaysi enrollmentni chiqarish kerakligini bildirish uchun) —
  bitta joyda qo'shilgan maydon, eski qabul qiluvchilar buzilmadi.
**Frontend:** `StudentLifecycle.tsx` — "Guruhga yozish" va "Bitirdi" header tugmalari, har bir faol enrollment
qatorida "Chiqarish" (`DropEnrollmentButton`); barchasi endi `usePermissions()` orqali o'z ruxsatiga qarab
ko'rinadi (`canManageAcademic(role)` olib tashlandi — Operator `student.enroll` tutadi, lekin transfer/freeze'ni
tutmaydi). `groups/[id]/page.tsx`: "Student qo'shish" — qidiruv (`listStudents`) + biriktirish modali.
**Ilgari qoldirilgan ish tugallandi:** S01.02 `EVENT_CONFIG`ga `PARENT_LINKED`/`PARENT_UNLINKED` qo'shilishi kerak
edi, o'sha safar unutilgan edi — shu safar qo'shildi (`ENROLLMENT_DROPPED`/`GRADUATED` bilan birga).
**Test:** `EnrollmentManagementTests` (10) — jami **203**. Guruhni yopish/bekor qilish uchun hali API yo'q (S02.01) —
"yopiq guruh" holatini test to'g'ridan-to'g'ri `AppDbContext` orqali yozadi (boshqa testlarda ham shu naqsh bor).
Brauzerda tekshirildi (administrator): mavjud studentda ikkita faol guruhdan birini chiqarish → status "Dropped"
bo'lib qoladi, ikkinchisi "Active" qoladi → Tarixda "Guruhdan chiqarildi" ko'rinadi; `/groups/[id]`da "Student
qo'shish" → qidiruv → biriktirish → talabalar soni va ro'yxat darhol yangilanadi.
**Dev bazada qoldiq:** ikkita test uchun ochib-yopilgan enrollment — Abdulla Abdullayevning IELTS-Found-1 va
Dilshod Nurmatovning IELTS-Adv-1 guruhlaridan chiqarilgan yozuvlari "Dropped" holida qoladi, sababi maydonida
"Sinov: ..." deb yozilgan (audit izi sifatida saqlash tabiiyroq ko'rindi, chunki chiqarish orqaga qaytarilmaydi).
Foydalanuvchi xohlasa, ularni yana o'sha guruhlarga qayta yozish mumkin ("Guruhga yozish" tugmasi).
**Foydalanuvchi xabari (shu ishga aloqasiz):** `/students/[id]` sarlavhasidagi amal tugmalari (Tahrirlash, Guruhga
yozish, Ko'chirish, Muzlatish, Bitirdi...) tor ekranda (1280px atrofida) ism-familiya ustuniga tiqilib, matn 4
qatorga bo'linib ketardi. Sarlavha qatori `flex-wrap` ga o'tkazildi (`min-w-0`/`shrink-0` bilan), amallar endi
o'z navbatida pastga o'raladi — `StudentLifecycle.tsx`dagi tugmalar qatoriga ham `flex-wrap` qo'shildi.

### 1.29. Bloklangan foydalanuvchi: darhol chiqarish, ekran, login xabari (foydalanuvchi talabi 2026-09-21; `docs/21.09-audit-jurnali/S01.04`)
**Muammo (🔴 xavfsizlik teshigi):** login qilib bo'lgan foydalanuvchi keyin bloklansa, access token muddati
tugaguncha (15 daqiqa) bemalol ishlayverardi. Oldindan bloklangan foydalanuvchi to'g'ri parol bilan kirsa ham
"Email yoki parol noto'g'ri" degan umumiy xato olardi. **Yana bir teshik topildi:** `UsersController.CanTouchUser`
rolni tekshirmasdi — Chilonzor administratori (`admin@`) umumiy filialdagi Super Admin'ni (`superadmin@`) bloklay,
tahrirlay va o'chira olardi.
**Endi:**
- **`AccountStatusMiddleware`** (`UseAuthentication()` dan keyin, `UseRateLimiter()` dan oldin) — har bir
  autentifikatsiya qilingan so'rovda (`/api/auth/*` dan tashqari, shu jumladan `/hubs/chat/negotiate`) holatni
  **`UserStatusCache`** (30 soniyalik TTL, `IMemoryCache`) dan o'qiydi: `Active` emas → **403** `{ error, code:
  "ACCOUNT_BLOCKED" }`; foydalanuvchi o'chirilgan bo'lsa → 401.
- **`AccountBlocker`** (scoped) — `UsersController.Deactivate/Activate/Delete` saqlagandan keyin chaqiradi: keshni
  darhol tozalaydi (30 soniya kutilmaydi) va agar yangi holat bloklangan bo'lsa `ChatHub.DropUserAsync` orqali
  ochiq chat ulanishiga `accountBlocked` hodisasini yuboradi hamda uni barcha chat guruhlaridan chiqaradi.
- `AuthController.Login`: parol **to'g'ri tekshirilgandan keyin** holat tekshiriladi (aks holda bloklangan-
  bloklanmaganini email bo'yicha oshkor qilib qo'yardi) → 403 `ACCOUNT_BLOCKED`. Noto'g'ri parol/mavjud bo'lmagan
  email — o'zgarishsiz umumiy 401.
- `AuthController.Refresh`: token topilmadi → 401; foydalanuvchi yo'q → 401; **bloklangan → 403 `ACCOUNT_BLOCKED`**
  (shuning uchun `Deactivate` endi tokenlarni o'chirmaydi, faqat **bekor qiladi** — `Refresh` "kim bloklangan"ini
  bilishi kerak); token muddati o'tgan/bekor qilingan → 401.
- `ChatHub`: `SyncChats`/`Typing` boshida holat tekshiradi (`HubException("ACCOUNT_BLOCKED")`); yangi ulanish esa
  `AccountStatusMiddleware` orqali negotiate bosqichida 403 bilan rad etiladi.
- **Rol himoyasi (🔴 teshik yopildi):** `CanTouchUserAsync` — `MyBranches` scope endi filial mosligidan tashqari,
  nishon foydalanuvchi `SUPER_ADMIN`/`ADMINISTRATOR`/`DEVELOPER` rolida emasligini ham tekshiradi (`db.Roles`
  so'rovi). Qo'llanilgan joylar: `Update`, `AssignRole`, `Deactivate`, `Activate`, `Delete`.
**Frontend:** `lib/account-status.ts` (kichik do'kon: `markAccountBlocked`/`clearAccountBlocked`/`useAccountBlocked`);
`api-client.ts` (403+`ACCOUNT_BLOCKED` da tokenlarni **tozalamaydi** — ekrandagi tugma tozalaydi); `chat-hub.ts`
(`accountBlocked` hodisasi + invoke xatosi); **`BlockedScreen.tsx`** — to'liq ekranni egallovchi, faqat "Tizimdan
chiqish" tugmasi, Escape/orqa fon yopilmaydi; `(admin)/layout.tsx` — bloklanganda sidebar/sahifa/bell umuman
render qilinmaydi (fon so'rovlari to'xtaydi); login sahifasida bloklangan xato **ogohlantirish** (amber) uslubida,
boshqalari qizil.
**Test:** `AccountBlockingTests` (8) — jami **211**. SignalR testida LongPolling transport middleware'ning
o'zi tomonidan 403 bilan to'xtatiladi (`HttpRequestException`), `SyncChats`ning o'z `HubException`i esa faqat
WebSocket kabi uzoq umr ko'radigan ulanishda amalda ishlaydi — test ikkalasini ham qabul qiladi.
Brauzerda tekshirildi ("Sinov Bloklanadi" nomli test student bilan, ikkinchi so'rov orqali — localStorage bitta
kontekstda umumiy bo'lgani uchun ikkita alohida tab o'rniga `fetch` bilan admin tomonidan bloklandi): ishlab
turgan sahifada keyingi navigatsiyada to'liq ekran "Hisobingiz bloklandi" chiqdi (shu jumladan SignalR negotiate
403 bilan to'xtadi, konsolda ko'rindi) → "Tizimdan chiqish" → `/login` → qayta kirishda amber ogohlantirish →
blokdan chiqarilgach oddiy kirish ishladi.
**Dev bazada qoldiq:** "Sinov Bloklanadi" (`sinov.bloklanadi@demo-center.test`, IELTS-21 guruhida) — hozir **Faol**
holatda qoldirildi (test oxirida blokdan chiqarildi).

### 1.30. Parolni o'zgartirish, administrator tiklashi, majburiy almashtirish (audit K8a; reja `docs/11` 1.4; `docs/21.09-audit-jurnali/S01.05`)
**Muammo:** hech kim o'z parolini o'zgartira olmasdi, administrator ham boshqaning parolini tiklay olmasdi
(`AuthController`: faqat `login`/`refresh`/`logout`). Student, ota-ona va yangi xodimga administrator o'zi tanlagan
parol berilardi va uni almashtirishga hech narsa majburlamasdi.
**Endi:**
| Endpoint | Ruxsat | Nima qiladi |
|---|---|---|
| `POST /api/auth/change-password` | `[Authorize]`, `LoginBruteForce` | `{ currentPassword, newPassword }`; joriy xato / qisqa / joriyga teng → 400. Boshqa **barcha** refresh tokenlar bekor qilinadi, javobda **yangi juftlik** qaytadi — joriy sessiya davom etadi |
| `POST /api/users/{id}/reset-password` | `users.password.reset` *(yangi: SA `AllOrganization`, AD `MyBranches`)* | `{ newPassword? }`; bo'sh bo'lsa server 12 belgili parol yaratadi (`PasswordGenerator`). `MustChangePassword=true`, barcha sessiyalar bekor, kesh yangilanadi. Parol javobda **faqat shu safar** |
- **Domen:** `User.PasswordChangedAt`, `User.MustChangePassword`, `ChangePassword(hash)` / `RequirePasswordChange()`;
  migratsiya `AddPasswordChangeTracking`. `TokenResponse.MustChangePassword` (login/refresh/change-password to'ldiradi).
- **Serverda majburlash:** `UserStatusCache` endi `AccountState(Status, MustChangePassword)` saqlaydi;
  `AccountStatusMiddleware` `MustChangePassword` bo'lgan hisobga `/api/auth/*` dan tashqari hamma so'rovga **403
  `PASSWORD_CHANGE_REQUIRED`** beradi (chat `negotiate` ham). Frontend'ni chetlab o'tib bo'lmaydi.
- **Kim vaqtinchalik parol oladi (`MustChangePassword=true`):** `POST /api/users`, `POST /api/students`, yangi ota-ona
  hisobi (`POST /students/{id}/parents`) va `reset-password`. Seed hisoblari va lid konvertatsiyasi (`LeadsController.Convert`)
  spetsifikatsiya bo'yicha **tegilmadi** — ular ochiq, pastga qarang.
- `reset-password` `CanTouchUserAsync` dan o'tadi (S01.04): filial administratori Super Admin/Administrator/Developer
  parolini tiklay olmaydi (403); o'zini tiklay olmaydi (400 — "Parolni o'zgartirish"dan foydalaning).
- **Xato topilib tuzatildi (S01.04 bilan bog'liq):** `/api/auth/*` `AccountStatusMiddleware` dan ozod, shuning uchun
  bloklangan hisob hali amal qiladigan access token bilan `change-password` ni chaqirib **yangi token juftligini** olardi.
  Endi `change-password` bloklangan hisobni o'zi rad etadi (403 `ACCOUNT_BLOCKED`, parol o'zgarmaydi). Shu bilan birga
  "Hisobingiz bloklangan…" matni to'rt joyda takrorlanardi — `ApiErrorCodes.AccountBlockedMessage` ga chiqarildi.
- **Rejadan farq:** spetsifikatsiya "audit jurnalida parol xeshi yo'qligi — mavjud regressiya testi" deydi, lekin bunday
  test aslida yo'q edi (`PasswordHash` faqat `RedactedProperties` da). Yangi test yozildi.
**Frontend:** `ChangePasswordForm` (bitta forma — ikkala yo'l uchun; kamida 8 belgi, takrorlash, joriyga teng emas),
`app/(auth)/change-password` (majburiy ekran, `(admin)` qobig'idan tashqarida; "Boshqa hisob bilan kirish" chiqish yo'li bor),
`app/(admin)/settings/security` (kundalik o'zgartirish; `UserMenu` da "Parolni o'zgartirish"), `users/[id]` da
"Parolni tiklash" (`can("users.password.reset")`, `ConfirmDialog` → vaqtinchalik parol oynasi, "Nusxa olish").
Login `mustChangePassword` da `/change-password` ga yo'naltiradi. Ochiq sessiyada admin parolni tiklasa, keyingi so'rov
403 `PASSWORD_CHANGE_REQUIRED` oladi → `lib/account-status.ts` da yangi reaktiv bayroq → `(admin)` layout qobiqni to'xtatib
`/change-password` ga o'tkazadi (avval `window.location.assign` edi — `@next/next/no-location-assign-relative-destination`
ogohlantirishi va `--max-warnings=0` sabab do'kon naqshiga o'tkazildi; forma muvaffaqiyatli bo'lganda bayroqni tozalaydi).
**Test:** `PasswordTests` (11) + to'rtta mavjud test tuzatildi (`StudentParentsTests` 2, `AccountBlockingTests` 2: yangi
yaratilgan hisobning birinchi sessiyasi endi majburiy almashtirishdan boshlanadi — bu xatti-harakat endi kutilgani, testlar
o'shani bajaradi) — jami **222**. Testlar umumiy bazada ishlaydi va boshqa testlar seed hisoblarni `DemoPassword` bilan
ishlatadi, shuning uchun parolni o'zgartirish/tiklash faqat testning o'zi yaratgan hisobda.
Brauzerda tekshirildi ("Sinov Parol" hisobi, 15 ta tekshiruv): vaqtinchalik parol bilan login → `/change-password`;
takrorlash mos kelmasa xabar va o'chirilgan tugma; joriy parol xato → server xabari; muvaffaqiyat → `/dashboard`;
`UserMenu` → xavfsizlik sahifasi; Super Admin parolni tiklaydi → parol oynasi (12 belgi) va nusxa olish; ochiq sessiya
`/change-password` ga o'tadi va yangi parol bilan `/dashboard` ga qaytadi; 390px da gorizontal siljish yo'q; chiqish tugmasi.
**Ma'lum cheklovlar (yangi emas, S09.05 ga tegishli):** parol o'zgartirilganda boshqa qurilmalarning **refresh** tokenlari
bekor bo'ladi, lekin ularning **access** tokeni muddati tugaguncha (≤ 15 daqiqa) ishlayveradi — sessiyani zumda o'ldirish
token versiyasini talab qiladi. `reset-password` da esa bu muammo yo'q: `MustChangePassword` bayrog'i eski access tokenni
zumda to'sadi. `change-password` `login` bilan bitta `LoginBruteForce` cheklovini ulashadi (10/daqiqa/IP).
**Ochiq:** lid konvertatsiyasida yaratilgan student vaqtinchalik parol bilan kiradi, lekin uni almashtirishga majburlanmaydi
(spetsifikatsiya `LeadsController.Convert` va seederlarga tegmaslikni so'ragan — testlar buzilmasligi uchun). Alohida
kichik ish: `Convert` da `RequirePasswordChange()` chaqirish va `LeadConversionTests` da login qiladigan joylarni moslash.
**Dev bazada qoldiq:** yo'q (test hisobi o'chirildi).

### 1.31. Guruhni tahrirlash, yopish, o'chirish (audit K4; reja `docs/11` 2.1; `docs/21.09-audit-jurnali/S02.01`)
**Muammo:** guruh yaratilgach hech narsa qilib bo'lmasdi — nom/sig'imni o'zgartirish, holat yuritish, o'qituvchini olib
tashlash, o'chirish yo'q edi; API orqali yaratilgan guruh abadiy `Forming` da qolardi; `groups.delete` ruxsatini hech qaysi
endpoint ishlatmasdi.
**Endi:**
| Endpoint | Ruxsat | Nima qiladi |
|---|---|---|
| `PATCH /api/groups/{id}` | `groups.edit` | `{ name?, capacity? }`; nom bo'sh/>200 va sig'im <1 → 400; sig'im faol studentlar sonidan past (xabarda son) yoki yopiq guruh → 409 |
| `POST /api/groups/{id}/status` | `groups.edit` | `{ status }`: `Forming→Active→Completed`, `Forming\|Active→Cancelled`; qolgani 409. `Completed` → faol/muzlatilgan enrollmentlar `Completed`; `Cancelled` → `Dropped` ("Guruh bekor qilindi"). Student holati `StudentStatusRules.Recompute` dan; timeline'ga `GRADUATED` yozilmaydi. Studentlar va guruh xodimlariga `GROUP_CLOSED` xabari |
| `DELETE /api/groups/{id}/staff/{userId}` | `groups.edit` | oxirgi `MainTeacher` va shu guruhda kelajakda darsi bor o'qituvchi → 409; guruhda yo'q → 404 |
| `DELETE /api/groups/{id}` | `groups.delete` | faqat bog'liq yozuvi yo'q guruh; aks holda **409 + `blockers` + `canCancel`**. `GroupStaff` va xabarsiz chat xonasi (a'zolik kursorlari bilan) o'chadi |
- **Domen (migratsiyasiz):** `Group.Update/Complete/Cancel/RemoveStaff/IsClosed/IsLastMainTeacher`; `Activate()` endi faqat
  `Forming` dan (seederlar yangi guruhda chaqiradi — buzilmadi). `Group` `AuditedTypes` ga qo'shildi (yopish/o'chirish auditda).
- Hammasi scope bilan: `CanAccessGroupAsync` (boshqa filial guruhi → 403). Operator/Teacher `groups.edit`/`groups.delete` siz → 403.
- **Rejadan farq:** (1) blocker ro'yxatiga `knowledge_documents` (`GroupId`) qo'shildi — u ham guruh id'siga bog'liq, DB kaliti
  yo'q, o'chirsak sarson qolardi; (2) `canCancel` doim `true` emas: guruh allaqachon yopiq bo'lsa `false` (bekor qilinadigan
  narsa yo'q); (3) bekor qilishda studentning timeline'iga voqea yozilmaydi (faqat `Enrollment` auditi) — spetsifikatsiya so'ramagan.
**Frontend:** `lib/api/academic.ts` (`updateGroup`, `setGroupStatus`, `removeGroupStaff`, `deleteGroup`, `groupDeleteRefusal`);
`components/GroupAdminActions.tsx` — `groups/[id]` sarlavhasida "Tahrirlash" (modal), "Faollashtirish" / "Yopish" / "Bekor
qilish" (`ConfirmDialog`, oqibati: "N ta faol talaba…") va xodim belgisida ✕ (`can("groups.edit")`; yopiq guruhda amallar
ko'rinmaydi); `components/GroupDeleteDialog.tsx` — `groups` kartasida "O'chirish" (`can("groups.delete")` va `studentCount === 0`),
409 da `blockers` ro'yxati va "Guruhni bekor qilish" taklifi. Hover'ga bog'liq UI yo'q.
**Test:** `GroupManagementTests` (9): tahrirlash+sig'im, holat o'tishlari (teskari → 409), `Completed` (enrollment, student
`Graduated`, timeline'da `GRADUATED` yo'q, chat yo'qoladi), `Cancelled` (boshqa guruhdagi student `Active` qoladi), xodim
olib tashlash, o'chirish (bo'sh / blockers / `canCancel`), 403 (teacher, operator, boshqa filial administratori), audit — jami
**274** integratsion test yashil. `tsc` va `eslint --max-warnings=0` toza.
**Ma'lum cheklovlar:** (1) `AssignStaff` guruhga faqat bitta `MainTeacher` ruxsat beradi, `RemoveStaff` esa oxirgisini
olib tashlashni rad etadi — demak asosiy o'qituvchini **almashtirib bo'lmaydi** (na olib tashlab, na qayta biriktirib); alohida
qaror kerak (masalan, `PUT` bilan almashtirish). (2) Yopilgan guruhning kelajakdagi darslari jadvalda qoladi — S02.02 shu status
endpointiga ulanadi (**S02.02 da ulandi**, 1.32). (3) Ochiq invoice'lar tegilmaydi (o'tgan davr uchun).

### 1.32. Darsni tahrirlash, ko'chirish, bekor qilish (audit K5; reja `docs/11` 2.2; `docs/21.09-audit-jurnali/S02.02`)
**Muammo:** yaratilgan darsni ko'chirib, xonasini almashtirib yoki bekor qilib bo'lmasdi; `ScheduleChanged` turini yuboradigan
o'zgarish yo'q edi.
**Endi:**
| Endpoint | Ruxsat | Nima qiladi |
|---|---|---|
| `PATCH /api/schedule/lessons/{id}` | `schedule.edit` | `{ startsAt?, endsAt?, roomName?, teacherId?, topic? }`; **o'zi bilan to'qnashmaydi**; konflikt → 409 `{ conflicts }`; o'tib ketgan / o'tmishga ko'chirish → 400; bekor qilingan → 409 |
| `POST /api/schedule/lessons/{id}/cancel` | `schedule.edit` | `{ reason }` majburiy (≤500); davomat/homework/resurs saqlanadi; qayta bekor → 409 |
| `POST /api/schedule/lessons/bulk-cancel` | `schedule.edit` | `{ lessonIds[], reason }`; **hammasi yoki hech biri** (bittasi scope'dan tashqari → 403); allaqachon bekor qilinganlari o'tkazib yuboriladi; har guruhga bitta xabar |
| `DELETE /api/schedule/lessons/{id}` | `schedule.edit` | davomat / uy vazifasi / material yo'q darsni o'chiradi; aks holda 409 + `blockers`, `canCancel` |
- **Domen + migratsiya `AddLessonStatus`:** `LessonStatus { Scheduled, Cancelled }`, `CancelReason` (500), `CancelledAt`; ustun `text` +
  `DEFAULT 'Scheduled'` (mavjud qatorlar `Scheduled`). `Reschedule`, `Cancel`; bekor qilingan darsda `SetTopic/Reschedule/ReplaceTeacher`
  `InvalidOperationException` beradi. `ChangeTeacher` **qo'shilmadi** — `ReplaceTeacher` shu vazifani bajaradi (spetsifikatsiya "birlashtiring" degan).
- `Api/Academic/LessonQueries.Held()`, `ScheduleConflicts.FindAsync(db, candidate, excludeLessonId)` (bekor qilingan **o'tkazib yuboriladi**).
- **Bildirishnoma:** `ScheduleChanged` → guruh studentlari + ota-onalar (`NotificationRecipients.GroupStudentsAndParentsAsync`, ikki so'rov) +
  o'qituvchi(lar); matnda eski → yangi vaqt/xona/o'qituvchi yoki "bekor qilindi: sabab". Mavzu-only tahrir xabar bermaydi. Katalogga `Parent` qo'shildi.
- **Guruh yopilganda** (`POST groups/{id}/status` → `Completed/Cancelled`) kelajakdagi darslar "Guruh yopildi" sababi bilan avtomatik bekor bo'ladi
  (alohida xabarsiz — `GroupClosed` o'zi yetadi). `DELETE groups/{id}/staff` da kelajak darsi hisobi `Held()` bilan.
- Bekor qilingan darsga davomat belgilash va homework berish → **409**.
**46 ta `db.Lessons` joyi — ko'rib chiqildi:**
- **`Held()` qo'llandi (o'zgartirildi):** `DashboardController` 126 (filial darslari), 172 (davomat), 278 (o'qituvchi darslari), 299 (**belgilanmagan davomat**), 309
  (davomat foizi), 444 (student darslari); `NotificationRules` 152 (**dars eslatmasi**); `FollowUpRules` 132 (**ketma-ket yo'qlik**); `TeachersController` 151;
  `StudentLearningController` 39; `LeadsController` 338 (tavsiya `NextLessons`); `GroupsController` 268 (xodim olib tashlash), 365 (yaqin darslar), 384
  (`lessonsHeld`), 388 (davomat); `ScheduleController` — konfliktlar, `substitutes`, `replace-teacher` to'qnashuvi.
- **Ataylab qoldirildi (izoh):** `ScheduleController` ro'yxat (bekor qilingan **ko'rinadi**, belgilanib), id bo'yicha o'qishlar, `Rooms` (xona baribir mavjud);
  homework bilan bog'liq hisoblar — `DashboardController` 187/288/455, `TeachersController` 164, `StudentsController` 147, `StudentLearningController` 57,
  `NotificationRules` 109 / `FollowUpRules` 172 (muddat eslatmalari), `HomeworkController` 42/117/150/201, `WritingEvaluator`/`WritingController` — homework
  bekor qilingan darsga tegishli bo'lsa ham **saqlanadi** (topshirilgan ishni baholash kerak, muddat amal qiladi); `ResourcesController` 43/152 (materiallar saqlanadi);
  delete-blockerlar: `UsersController` 371, `BranchesController` 107, `GroupsController` 298 (bekor qilingan dars ham baribir qator); seederlar (2).
**Frontend:** `lib/api/academic.ts` (`updateLesson`, `cancelLesson`, `bulkCancelLessons`, `deleteLesson`; `Lesson.status/cancelReason`);
`components/LessonActions.tsx` (`EditLessonModal` — vaqt, xona `datalist`, `listSubstitutes`, konflikt ogohlantirishi; `CancelLessonModal` — bitta yoki ko'p;
`ConflictNotice` endi yaratish oynasi bilan umumiy); `schedule/page.tsx` kun modalida "Tahrirlash"/"Bekor qilish", tanlab "Tanlanganlarni bekor qilish",
bekor qilingan dars `line-through` + sabab, **kun kartasidagi son bekor qilinganlarni hisoblamaydi**; `schedule/[lessonId]` da xuddi shu amallar, bekor
qilingan darsda davomat/homework paneli faqat o'qish.
**Test:** `ScheduleEditTests` (11): ko'chirish (o'zi bilan to'qnashmaslik), xona/o'qituvchi konflikti, o'tib ketgan dars, bekor qilish (sabab, 409lar), bekor
qilingan dars konfliktda qatnashmaydi, o'chirish, bildirishnoma (student/ota-ona/o'qituvchi), 403, `bulk-cancel` (hammasi yoki hech biri), **hisoblar**
(guruh `lessonsHeld` va davomat foizi, o'qituvchi `UnmarkedLessons`, yaqin darslar, dars eslatmasi), guruh yopilganda kelajak darslari — jami **285** test yashil.
**Test izolyatsiyasi haqida:** testlar umumiy bazada ishlaydi va boshqa testlar demo o'qituvchini `GET /api/users?role=TEACHER` ning birinchi sahifasidan
qidiradi — shuning uchun yangi testlar o'qituvchi hisobini `SUPPORT_TEACHER` roli bilan yaratadi (o'qituvchi dashboardi kerak bo'lgan bitta test bundan mustasno).

### 1.33. Imtihon, natija, homework va kursni tahrirlash / o'chirish (audit M9; reja `docs/11` 2.3, 2.4; `docs/21.09-audit-jurnali/S02.03`)
**Muammo:** noto'g'ri sana/nom bilan yaratilgan imtihon, xato kiritilgan natija, muddati xato homework va keraksiz kursni tuzatib/o'chirib bo'lmasdi.
**Endi:**
| Endpoint | Ruxsat | Qoida |
|---|---|---|
| `PATCH /api/exams/{id}` | `exams.create` | nom (≤200), tur, sana; natijalar kiritilgandan keyin ham mumkin; guruh o'zgarmaydi |
| `DELETE /api/exams/{id}` | `exams.create` | faqat natijasiz; aks holda 409 + `blockers` + `resultCount` |
| `DELETE /api/exams/{id}/results/{resultId}` | `exams.score.edit` | natijani o'chiradi; timeline `EXAM_RESULT_REMOVED` (yangi); studentga **xabar yuborilmaydi** |
| `PATCH /api/homework/{id}` | `homework.create` | sarlavha (≤200), tavsif (≤2000, bo'sh = tozalash), tur, muddat, maksimal ball; ball berilgan baholardan past → 409; muddat o'zgarsa guruh studentlariga `HOMEWORK_CHANGED` (yangi) |
| `DELETE /api/homework/{id}` | `homework.create` | faqat topshiriqsiz (va AI writing tahlilisiz); aks holda 409 + `blockers` |
| `DELETE /api/courses/{id}` | `courses.manage` | guruhi (**har qanday holatda**) yo'q; aks holda 409 + `groupCount` |
- Scope imtihon/homework guruhi orqali (`CanAccessGroupAsync`): teacher faqat o'z guruhida, boshqasida 403. `Exam`, `ExamResult`, `Homework` `AuditedTypes` da.
- **Rejadan farq:** (1) `ExamResult.Update(...)` **qo'shilmadi** — hech qaysi endpoint uni chaqirmaydi (spetsifikatsiyada natijani tahrirlash yo'q, faqat o'chirish); S04.01 yangi
  ustunlar qo'shganda kerak bo'lsa yoziladi. (2) Homework o'chirish blockeriga `writing_evaluations.HomeworkId` ham qo'shildi (topshiriqsiz ham AI tahlili bo'lishi mumkin).
  (3) Kurs o'chirish blockeriga `knowledge_documents.CourseId` qo'shildi (kalit yo'q, sarson qolardi). (4) Timeline `EVENT_CONFIG`ga `EXAM_RESULT_REMOVED`, bell'ga
  `HOMEWORK_CHANGED` va `GROUP_CLOSED` (S02.01) ikonkalari qo'shildi.
**Frontend:** `lib/api/lms.ts` (`updateExam`, `deleteExam`, `deleteExamResult`, `updateHomework`, `deleteHomework`), `lib/api/academic.ts` (`deleteCourse`);
`components/ExamActions.tsx` — imtihon qatorida "Tahrirlash"/"O'chirish", natija kartasida ✕ (`ConfirmDialog`, server xabari ko'rsatiladi);
`components/HomeworkActions.tsx` — dars sahifasida homework kartasida "Tahrirlash"/"O'chirish"; `CourseFees` — "O'chirish" (`can("courses.manage")`), guruhi bor kursda
"N ta guruh bor" (ko'rinadigan guruhlar bo'yicha; ko'rinmaganini server rad etadi).
**Test:** `LmsEditTests` (9): imtihon tahrirlash (natijadan keyin ham), natijali imtihonni o'chirish 409 → natijani olib tashlab o'chirish 204, timeline `EXAM_RESULT_REMOVED` va xabar
yo'qligi, homework tahrirlash (faqat muddat o'zgarsa xabar), maksimal ball baholardan past → 409, topshiriqli homework 409, kurs (guruhsiz 204, guruhli va yopiq guruhli 409), 403
(boshqa o'qituvchi guruhi, student), audit — jami **294** integratsion test yashil. `tsc` va `eslint --max-warnings=0` toza.
**Brauzerda tekshirilmadi:** S02.01–S02.03 ning UI qismi faqat `tsc`/`eslint` bilan tekshirilgan; ochiq qolgan ish — qo'lda ko'rib chiqish (kun modali tanlovi, tahrirlash oynalari, 409 xabarlari).

### 1.34. Dars mavzusi va izohi — elektron jurnal, 1-qism (audit K6; reja `docs/11` 3.1; `docs/21.09-audit-jurnali/S03.01`)
**Muammo:** TZ §15 "teacher bir dars ichida topic, attendance, homework, notes, resources, scores kiritadi" — `Lesson.SetTopic` hech qayerda chaqirilmasdi, dars izohi ustuni umuman yo'q edi.
**Endi:** `Lesson.Notes` (≤2000, migratsiya `AddLessonNotes`) va yangi `LessonJournalController`:
| Endpoint | Ruxsat | Qoida |
|---|---|---|
| `GET /api/lessons/{id}/journal` | `attendance.mark` | `{ topic, notes, maxLessonScore }` |
| `PATCH /api/lessons/{id}/journal` | `attendance.mark` | `null` = tegmaydi, bo'sh matn = tozalaydi; mavzu ≤200, izoh ≤2000 (400); hech narsa yuborilmasa 400; **bekor qilingan dars 409**; o'tib ketgan dars mumkin |
- Nega alohida endpoint: mavzu pedagogik yozuv, `schedule.edit` o'qituvchida yo'q, `attendance.mark` da bor. Scope — `CanAccessGroupAsync` (o'qituvchi faqat o'z guruhida; yordamchi o'qituvchi, operator, student, ota-ona — 403).
- Mavzu o'zgarganda bildirishnoma **yuborilmaydi**. Izoh `LessonDto`da **yo'q** — student/ota-ona faqat mavzuni (jadval orqali) ko'radi, izohni hech qaysi endpointda emas.
**Frontend:** `components/LessonJournal.tsx` (dars sahifasi tepasida "Dars jurnali": Mavzu, Dars izohi, Dars balli; davomatdan mustaqil "Saqlash"; faqat `can("attendance.mark")` va bekor qilinmagan darsda), `lib/api/lms.ts` (`getLessonJournal`, `saveLessonJournal`).

### 1.35. Davomat izohi va dars balli — elektron jurnal, 2-qism (audit K6; reja `docs/11` 3.2; `docs/21.09-audit-jurnali/S03.02`)
**Muammo:** `Attendance` faqat `Status` saqlardi — "kech qoldi, sababi…" izohi ham, dars ichidagi ball ham yo'q edi. Qo'shimcha xato: `Mark` holat o'zgarmasa `continue` qilardi — bir xil holat bilan yuborilgan yangi izoh/ball yo'qolardi.
**Endi:** `Attendance.Note` (≤500), `Attendance.Score`, `Update(status, note, score)` (`SetStatus` o'rniga), `Lesson.MaxLessonScore` (1–100) — migratsiya `AddAttendanceNoteAndScore`.
- `MarkAttendanceItem(StudentId, Status, Note?, Score?)`; o'zgarish sharti "holat **yoki** izoh **yoki** ball farq qiladi". Timeline, ota-onaga "kelmadi" xabari va "2 ketma-ket yo'qlik" tekshiruvi **faqat holat o'zgarganda**: izohni tuzatish yangi hodisa yaratmaydi.
- **Qarorlar:** (1) `MaxLessonScore` yo'q darsga ball → **400** ("chegarasini belgilang"), jim e'tiborsiz qoldirilmaydi; ball `0..MaxLessonScore` dan tashqarida → 400. (2) **`Note` ichki**: `GET attendance` da faqat `attendance.mark` egasiga qaytadi (`PermissionResolver`); `Score` student/ota-onaga ham (o'zinikini). (3) Item — talabaning **to'liq yozuvi**: `Note`/`Score` yuborilmasa "yo'q" degani (ekran doim butun ro'yxatni qaytaradi) — eski, faqat statusli so'rov mavjud izoh/ballni tozalaydi. (4) Chegara: `PATCH …/journal` ga `maxLessonScore` (`0` = "ball yo'q"); qo'yilgan ballning eng kattasidan pastga tushirish yoki ball bor darsda olib tashlash — **409**.
- `LessonDto.MaxLessonScore` qo'shildi (student "9 / 10" ko'radi, davomat jadvali ball ustunini shundan biladi); `ReplaceTeacher` javobi `ToDto` ga o'tkazildi.
**Frontend:** davomat jadvalida har qatorda tugma (bosilganda ball + izoh maydoni; tap, hover emas — telefonda ham shu), ball faqat chegara bor darsda va `0..max` tekshiruvi bilan; student o'z ballini "9 / 10" ko'rinishida ko'radi.
**TZ §15 to'liq yopildi:** topic, attendance, homework, notes, resources, scores.
**Test:** `LessonJournalTests` (S03.01 + S03.02, 17 ta holat): mavzu+izoh → jadvalda mavzu; bo'sh/null; uzun matn; student/ota-ona/yordamchi/operator 403; boshqa guruh o'qituvchisi 403; bekor qilingan 409, o'tib ketgan dars mumkin; izoh student/ota-onaga chiqmaydi; izoh+ball qaytadi; **bir xil holat, boshqa izoh → saqlanadi** (regressiya); ball chegaralari; timeline shovqini yo'q; ichki izoh (student/operator ko'rmaydi); chegara 409.

### 1.36. Homeworkka fayl yuklash (audit K7; reja `docs/11` 3.3; `docs/21.09-audit-jurnali/S03.03`)
**Muammo:** `HomeworkType.FileUpload` bor edi, lekin fayl yuklash yo'q: student faqat matn/havola yubora olardi, o'qituvchi worksheet/audio biriktira olmasdi.
**Endi:** `Homework` (o'qituvchi fayli) va `HomeworkSubmission` (student javobi) ga `AttachmentKey/Name/ContentType/Size` — migratsiya `AddHomeworkAttachments`; `HomeworkFileStorage` (`App_Data/homework`, **20 MB**, pdf/doc(x)/ppt(x)/xls(x)/png/jpg/webp/mp3/m4a/wav/txt; `Program.cs`, sozlama bo'limi `Homework`).
| Endpoint | Ruxsat | Nima qiladi |
|---|---|---|
| `POST /api/homework/upload` | `homework.create` | multipart (`lessonId,title,description?,type,deadline,maxScore,file`); JSON `POST` faylsiz qoladi |
| `GET /api/homework/{id}/attachment` | `homework.view` | guruh scope'i (`CanAccessGroupAsync`) |
| `POST /api/homework/{id}/submissions/upload` | `homework.submit` | `file` + ixtiyoriy `contentText`; qoidalar JSON endpoint bilan **bir xil** |
| `GET /api/homework/submissions/{id}/attachment` | `homework.view` | student **faqat o'zinikini**, ota-ona farzandiniki, xodim o'z scope'idagini |
- **Qarorlar:** (1) Qayta topshirish JSON'dagi qoida bo'yicha — **409**, "almashtirish" yo'q; rad etilgan topshiriqning fayli diskda qolmaydi (fayl barcha rad etishlardan keyin yoziladi va saqlash xato bersa o'chiriladi — testda diskdagi fayllar soni tekshiriladi). (2) Homework o'chirilsa fayli ham o'chadi. (3) `HomeworkDto.Attachment`, `SubmissionDto.File` faqat nom/turi/hajm — saqlash kaliti hech qachon chiqmaydi. (4) JSON va multipart yaratish/topshirish bitta yadro (`CreateCoreAsync`, `SubmitCoreAsync`) orqali — natijada sarlavha/tavsif uzunligi, tur, muddat va javob matni (≤10000) endi **400** beradi (avval bazadan 500). (5) AI Writing: `QueueForHomeworkAsync` matn bo'sh bo'lsa baholashni o'zi o'tkazib yuboradi — kod o'zgarmadi, "faqat fayl" topshiriq testi yozildi. (6) Homework faylini keyin almashtirish/olib tashlash endpointi spetsifikatsiyada yo'q — yozilmadi (xato fayl bo'lsa homework qayta yaratiladi; ochiq).
- Student profili: `StudentHomeworkRow` ga `AttachmentName`, `SubmissionId`, `SubmissionFileName`.
- **S08.03 uchun:** `HomeworkFileStorage` fayl skaneri (ClamAV) qamrovi ro'yxatida bo'lsin; S09.06 da S3/MinIO ga ko'chadi.
**Frontend:** `components/HomeworkFiles.tsx` (`HomeworkFilePicker` — hajm/tur oldindan tekshiriladi, `DownloadFileButton` — bearer bilan blob yuklab olish, asl nom bilan), homework yaratish formasiga fayl, student topshirish oynasiga "Fayl yuklash" (matn bilan birga; fayl tanlansa havola yuborilmaydi), o'qituvchi tekshirish ro'yxatida va studentning o'z topshirig'ida fayl, student profili homework tabida ikkala fayl. Telefonda homework kartasi ustun bo'lib joylashadi (avval fayl nomi ko'rinmay qolardi).

### 1.37. Individual topshiriq — bitta yoki bir necha studentga (audit K7; reja `docs/11` 3.4; `docs/21.09-audit-jurnali/S03.04`)
**Muammo:** homework faqat `LessonId` ga bog'liq → har doim butun guruhga; "guruhdagi barcha homework" deb hisoblovchi joylar esa individual topshiriq kiritilsa boshqa studentlarning foizini tushirib, ularga noto'g'ri eslatma yuborardi.
**Endi:** `HomeworkTarget(HomeworkId, StudentId)` (unique) — migratsiya `AddHomeworkTargets`; **qator yo'q = butun guruh** (mavjud homeworklar o'zgarishsiz). `CreateHomeworkRequest`/multipart `StudentIds`: har biri shu guruhning **faol** studenti bo'lishi shart (aks holda 400 + ro'yxat); `HomeworkDto.Targeted` (hamma ko'radi) va `StudentIds` (faqat `homework.create` egasiga). Maqsadli bo'lmagan student ro'yxatda ko'rmaydi, topshira olmaydi (403), faylini ham ololmaydi; ota-ona — farzandlaridan birortasiga berilganini ko'radi.
- **`Api/Lms/HomeworkQueries.cs`** — bitta manba: `AssignedTo(db, studentId)`, `AssignedToAny(db, studentIds)`, `AssignedStudents(db, homeworkId)`, `CountAssignmentsAsync(...)` (foiz maxraji: berilgan **(homework, student) juftliklari**), `LoadTargetingAsync` → `HomeworkTargeting.IsAssigned` (xotirada yuruvchi kod uchun).
- **Qo'llangan joylar (7 ta):** `StudentsController.GetOverview` (`assignedHomeworkCount`), `StudentLearningController.Homework` (profil ro'yxati), `DashboardController` student "topshirilmagan" ro'yxati, `NotificationRules.HomeworkDeadlinesAsync` (eslatma faqat maqsadlilarga), `FollowUpRules.RunHomeworkSignalsAsync` ("oxirgi N ta homework" faqat berilganlardan), `TeachersController.Performance` va `GroupsController.BuildResultsAsync` (foiz maxraji `CountAssignmentsAsync` orqali; o'qituvchi formulasi endi **aniq** — har homework o'z guruhi bo'yicha, avvalgi "homework × barcha studentlar" yaqinlashuvi o'rniga), `HomeworkController` (`GET` ro'yxat, `POST` bildirishnoma, `POST …/submissions`, muddat o'zgargani xabari, faylni olish).
- **Ataylab o'zgartirilmadi:** `DashboardController` 187 (`pendingReviews`) va 288 (`pendingHomework`) — topshiriq (submission) asosida sanaydi, maqsadli bo'lmagan student topshira olmaydi; `TutorService` (baholangan topshiriqlar o'rtachasi) va `WritingController` (id bo'yicha) ham topshiriq asosida.
- **Bildirishnoma:** individual homework `HOMEWORK_CREATED` va muddat o'zgargani (`HOMEWORK_CHANGED`) — faqat maqsadli studentlarga **va ularning ota-onalariga**; butun guruhga berilgani avvalgidek guruh studentlariga.
- Homework o'chirilsa `HomeworkTarget` qatorlari ham o'chadi (FK yo'q, qo'lda).
**Frontend:** homework yaratish formasida "Kimga: Butun guruhga / Tanlangan studentlarga" (guruh ro'yxatidan ko'p tanlov, kamida bittasi), ro'yxatda "Individual · N student" belgisi (`Badge`), `lib/api/lms.ts` (`studentIds`, `targeted`).
**Test:** `HomeworkTargetTests` (7): faqat maqsadli student/ota-ona/o'qituvchi ko'radi, sinfdosh ko'rmaydi va topshira olmaydi (403); **sinfdoshning foizi tushmaydi** (overview 1/1 = 100%, dashboard "topshirilmagan" ro'yxati, guruh foizi 2/3 = 66.7 — eski formula 50 berardi); bildirishnoma faqat maqsadliga (+ota-ona), muddat o'zgarishi ham; guruhda bo'lmagan id → 400; bo'sh/berilmagan `StudentIds` = butun guruh; **support signal** maqsadli bo'lmagan homeworkni hisoblamaydi; individual homework fayli sinfdoshga 403, o'chirilsa maqsadlar ham o'chadi. Test yordamchisi: `LmsWorld` (yangi guruh + o'qituvchi + studentlar + dars).

**S03 yakuni:** jami **326** integratsion test yashil (S02.03 dan +32), `tsc` va `eslint --max-warnings=0` toza. Migratsiyalar: `AddLessonNotes`, `AddAttendanceNoteAndScore`, `AddHomeworkAttachments`, `AddHomeworkTargets` (dev bazaga `update-db` bilan qo'llangan).
**Brauzerda tekshirildi** (Playwright, teacher, 1280 va 390 px): dars jurnali saqlanadi, davomatda izoh+ball, individual homework fayl bilan yaratiladi va fayl asl nomi bilan yuklab olinadi. **Dev bazada qoldiq:** dars `8104ce98-f690-465a-921e-ac17d6ccfe34` (guruh "IELTS…", o'qituvchi demo) — mavzu "Sinov mavzu: Present Perfect", izoh, ball chegarasi 10, davomat (saqlash butun ro'yxatni "Keldi" deb yozgan — 12 talaba, birinchisiga ball 9 va izoh "Sinov: …"), "Sinov homework (individual)" (fayl `sinov-worksheet.txt`, bitta studentga). Kerak bo'lmasa o'chirish mumkin (homework — dars sahifasidan; topshirig'i yo'q).

### 1.38. ExamResult: Grammar va Vocabulary foizi (audit M8; reja `docs/11` 4.1; `docs/21.09-audit-jurnali/S04.01`)
**Muammo:** TZ §5.2/§7 "Grammar 71%", "Vocabulary 84%" ni talab qiladi, `ExamResult` da faqat IELTS bo'limlari bor edi — Learning Profile, filial dashboardi va Teacher KPI hisoblanmasdi.
**Endi:** `ExamResult.Grammar` va `Vocabulary` — `decimal?`, **foiz 0–100** (IELTS bandlari 0–9 bo'lib qoladi), migratsiya `AddExamGrammarVocabulary` (ikki nullable ustun — eski natijalarda `null`, ular o'lchanmagan). `POST /api/exams/{id}/results`: foiz 0–100 dan tashqarida → 400; "kamida bitta ball" tekshiruvi endi foizni ham hisoblaydi (Unit testda faqat Grammar ham natija). `ExamResultDto`, `RecordExamResultRequest`, `StudentExamRow` oxiriga ixtiyoriy maydonlar — mavjud klient/testlar buzilmaydi. `ExamResult` konstruktori: yangi parametrlar ixtiyoriy (`DbSeeder` o'zgarmadi).
- **Natija tahriri yo'q:** hujjatdagi "upsert" noto'g'ri edi — takror natija avvalgidek **409**, tuzatish yo'li "natijani o'chirib qayta kiritish" (S02.03). Test shuni tekshiradi.
- **Demo seeder:** `DemoDataSeeder` `Unit`/`Monthly` imtihonlarga Grammar/Vocabulary generatsiya qiladi (student qobiliyatidan, 30–100%); Mock IELTS'ga yo'q — ikkala holat ham ko'rinadi. `Seed:BulkDemoData` ortida.
**Frontend:** imtihon natijalari drawer'i va student "Imtihonlar" tabida "Grammatika %" / "Lug'at %" ustunlari (`null` → "—"), kiritish formasida ikki maydon (0–100).
**Test:** `LmsEditTests` +4: foizlar ikkala o'qish endpointida qaytadi; 101/−1 → 400 va 9.5 Listening'da hamon 400, chegaralar (0, 100) o'tadi; bo'sh foiz = eski xatti-harakat, faqat Grammar ham yetarli; takror 409 → o'chirib qayta kiritish.

### 1.39. Student Learning Profile — TZ §7 ning sakkiz raqami (audit M2; reja `docs/11` 4.2; `docs/21.09-audit-jurnali/S04.02`)
**Muammo:** `GET /students/{id}/overview` faqat Davomat va Homework'ni berardi; Listening/Reading/Writing/Speaking/Grammar/Vocabulary yo'q edi.
**Endi:** `GET /api/students/{id}/learning-profile` (`students.view` + `CanAccessStudentAsync` + tashkilot, **yangi jadval yo'q**). Har raqam `{ value, sampleSize, trend }`; ma'lumot yo'q joyda `value = null` (nol emas), `sampleSize = 0`.
- **`Api/Students/LearningProfileCalculator.cs`** — sof funksiyalar (soat va so'rovsiz): `WeightedAverage` (oxirgi **5** ta natija, og'irlik 5-4-3-2-1, yangisi kuchliroq), `Trend`, `Band`/`Percent`/`Attendance`/`HomeworkCompletion`, davomat qoidasi (`CountsForAttendance`, `Attended`), `Rate`. S04.05 va S05.03 shuni qayta ishlatadi.
- **Manbalar:** Listening/Reading/Speaking — `ExamResult` (Speaking **faqat imtihondan**); Writing — `ExamResult.Writing` **va** tugallangan `WritingEvaluation` (`TeacherBand ?? EstimatedBand`, `Failed/Rejected` hisobga olinmaydi); Grammar/Vocabulary — `ExamResult` **va** `HomeworkType.Grammar/Vocabulary` ning baholangan topshiriqlari (`Score / MaxScore × 100`); Davomat/Homework — mavjud formula.
- **Trend:** eng yangi 3 ta natija o'rtachasi va undan oldingi 3 tasiniki farqi (±0.25 band / ±3 foiz dan kichik → 0). **Tanlangan soddalashtirish:** 6 tadan kam natijada tomonlar imkon qadar teng bo'linadi (yangi tomon toq bittasini oladi; 2 ta natija = "oxirgi ↔ oldingisi"), aks holda ko'pchilik studentda (1–3 imtihon) trend hech qachon chiqmasdi; 1 ta natijada `null`. Davomat va homework (har dars ha/yo'q) 5 ga 5 solishtiriladi va 6 tadan kamda `null` — 3 ta dars uchun 33% sakrash bo'lardi.
- **Davomat formulasi bitta:** `Present/Late/Online` = keldi, `Excused` hisobga olinmaydi, bekor qilingan dars (`Held()`) hisobga olinmaydi — guruh/o'qituvchi formulasi bilan bir xil. **`GetOverview` shu qoidaga o'tkazildi**: avval faqat `Present` ni sanardi va bekor qilingan dars/sababli yo'qlikni ham maxrajga qo'shardi (bir student profilida ikki xil davomat chiqardi). `lessonsAttended` endi Late/Online'ni ham o'z ichiga oladi.
- **Ko'rinish chegarasi:** profil `Attendance/Homework/Exams` tablari bilan **bir xil guruh ko'rinishi** (`ScopedGroupsAsync`) — o'qituvchiga boshqa o'qituvchining guruhidagi natijalar o'rtachaga kirmaydi. Ya'ni profil faqat chaqiruvchi tablarda allaqachon ocha oladigan narsalarni jamlaydi. Shu sababli o'qituvchi uchun (ikki guruhda o'qiydigan studentda) profildagi davomat `overview` dagidan farq qilishi mumkin: `overview` hamon barcha guruhlarni sanaydi (bu ushbu qadamdan oldingi xatti-harakat, tegilmadi).
**Frontend:** `components/StudentLearningProfile.tsx` — sakkiz karta: qiymat, polosa (`bg-brand-solid`; past qiymat `warning-500`/`danger-500`: band 5.5/4.5, foiz 60/40 — konstantalar), trend o'qi (↑ ↓ →, ekran o'quvchi uchun matn), "N ta natijadan/darsdan/vazifadan", `null` → "Hali ma'lumot yo'q"; Writing va Speaking kartasida manba izohi.
**Test:** `LearningProfileTests` — integratsiya 7: 3 imtihondan og'irlikli o'rtacha (6.2 / 71.7%, trend +1, 1 ta natijada trend null); ma'lumot yo'q → hammasi null; o'qituvchi tuzatgani AI'dan ustun va `Failed/Rejected` insho hisobga olinmaydi; **AI Speaking mashqi Speaking'ga kirmaydi** (sessiya 9.0 → profilda null); Grammar homework foizi + Homework% overview bilan teng; **Davomat** (Present/Late/Absent/Excused + bekor qilingan dars → 66.7% va overview bilan bir xil); ruxsat (o'qituvchi/o'z studenti 200, begona 403, sinfdosh 403, anonim 401). Sof funksiyalar: `WeightedAverage`, `Trend` (chegara: aynan 0.25 → o'sish, 0.24 → 0; 7 ta ma'lumotdan eskilari e'tiborsiz), `Band` (kelish tartibi ahamiyatsiz), `HomeworkCompletion` (muddati kelmagan topshiriq trendga kirmaydi).

### 1.40. Student 360°: Learning, Writing va Speaking tablari (audit M1; reja `docs/11` 4.3; `docs/21.09-audit-jurnali/S04.03`)
**Muammo:** TZ §7 dagi 12 tabdan Learning, Writing, Speaking yo'q edi.
**Endi:** tab tartibi — Umumiy, **Learning**, Davomat, Homework, Imtihonlar, **Writing**, **Speaking**, To'lovlar… Har biri `usePermissions().can(...)` bilan: Learning — `students.view`; Writing — `ai.writing.review` (xodim) yoki `ai.writing.use` (student o'zinikini); Speaking — `ai.speaking.review`.
- **Learning:** `StudentLearningProfile` (1.39).
- **Writing:** yangi endpoint yo'q — xodimga mavjud `GET /api/ai/writing/reviews?studentId=`, studentga `GET /api/ai/writing/evaluations`. Sana, mavzu, band, o'qituvchi tuzatgan bo'lsa "Tuzatilgan" belgisi, holat; "Ko'rish" → `WritingEvaluationView` (compact) drawer'da; oxirgi 8 band oddiy raqamlar qatori (`recharts` kiritilmagan — S05.03). Bo'sh: "Hali insho yozilmagan".
- **Speaking — mahsulot qarori (foydalanuvchidan so'raldi): variant B, "hammasi"** — xodim yakunlangan mashqning bahosini, transkriptini va ovoz yozuvini ko'radi. Speaking avval ataylab studentning shaxsiy maydoni edi ("nothing here is shared with staff"), shuning uchun:
  - **Yangi ruxsat `ai.speaking.review`** — SA `ALL_ORG`, AD `MY_BRANCHES`, TC `MY_GROUPS`, ST `ASSIGNED_GROUPS` (`ai.writing.review` bilan bir xil doira). Operator, Parent, Student'da yo'q. `docs/01` ga qator va izoh.
  - **`Api/Ai/Speaking/StudentSpeakingController`** — `GET /api/students/{id}/speaking/sessions` (ro'yxat: sana, rejim, 4 ta band, davomiyligi), `…/sessions/{sessionId}` (savollar, transkript, nutq tezligi/pauza, baho) va `…/turns/{turnId}/audio`. Ruxsat + `CanAccessStudentAsync` + tashkilot; boshqa studentning sessiyasi 404. `SpeakingSessionView` — student va xodim ko'rinishi uchun bitta quruvchi (`SpeakingController.BuildAsync` shunga ko'chdi).
  - **Faqat "yakunlangan" va "student bilgan" mashqlar:** `SpeakingSession.SharedWithStaff` (migratsiya `AddSpeakingSessionSharing`) — yangi sessiyada `true`, **mavjud barcha sessiyalarda `false`**: ular "xodimga ko'rinmaydi" va'dasi bilan yozilgan, orqaga qarab ochilmaydi. Davom etayotgan va tashlab ketilgan sessiya ham ko'rinmaydi (404). Eski mashqlarni ham ochish kerak bo'lsa — bir qatorlik SQL (`UPDATE speaking_sessions SET "SharedWithStaff" = true`), lekin studentlar buni oldindan bilmagan.
  - **Iz qoldiriladi:** sessiyani ochish va yozuvni tinglash `AuditLog` ga `Viewed` deb yoziladi (kim, qaysi student, sessiya/yozuv).
  - **Student ogohlantiriladi:** `ai/speaking/page.tsx` tepasida doim ko'rinadigan xabar — "Yakunlangan mashqlaringiz — bahosi, javoblaringiz matni va ovoz yozuvingiz — o'qituvchilaringiz va markaz administratorlariga ko'rinadi".
  - **O'qituvchi bahosi (tuzatish)** — 1.43. **Learning profildagi Speaking o'zgarmadi:** faqat imtihon. AI mashq bandi transkriptdan olingan taxmin (talaffuz baholanmaydi), shuning uchun tabda ko'rinadi, lekin o'rtachaga qo'shilmaydi (tabda ham shunday izoh).
  - Frontend: `components/ai/SpeakingSessionView.tsx` (student sahifasidagi `TranscriptCard`/`ReportCard`/`TurnAudio` shu yerga chiqarildi, audio yo'li parametr), `components/StudentSpeakingTab.tsx`.
**Test:** `SpeakingReviewTests` (3): o'qituvchi tugallangan mashqni ko'radi — band, transkript, yozuv (2048 bayt, `audio/webm`) va **ikki `Viewed` audit yozuvi**; faqat tugallangan+ulashilgan sessiya ro'yxatda (eski/tashlab ketilgan/davom etayotgan → ro'yxatda yo'q va 404, eski sessiyaning audiosi ham 404, student o'zinikini hamon ko'radi); ruxsat (o'qituvchi/admin/superadmin 200; begona guruh o'qituvchisi, student, ota-ona, operator 403; anonim 401; boshqa student nomi ostidagi sessiya 404). Writing va Learning uchun yangi endpoint yo'q — mavjud testlar va `LearningProfileTests` qoplaydi.

**S04.01–S04.03 yakuni:** integratsion test soni **351** (S03 dan +25: S04.01 +4, S04.02 +18 — sof funksiya testlari bilan, S04.03 +3), `tsc` va `eslint --max-warnings=0` toza. Migratsiyalar: `AddExamGrammarVocabulary`, `AddSpeakingSessionSharing`. Yon ta'sir: `LmsWorld.CreateAsync` dars vaqti o'qituvchining boshqa darsi bilan to'qnashsa (409) yangi vaqt tanlab qayta uriniladi — yangi testlar `LmsWorld` ni ko'proq ishlatgani uchun bu kamdan-kam uchraydigan yiqilish (`LessonJournalTests`) ehtimolini oshirgan edi.

### 1.41. Student sarlavhasi va foto (audit M3; reja `docs/11` 4.4; `docs/21.09-audit-jurnali/S04.04`)
**Muammo:** TZ §7 sarlavhasi foto, filial, kurs, guruh, asosiy/yordamchi o'qituvchilar va ota-ona telefonini talab qiladi; profilda faqat ism, holat, email, telefon, ID va daraja bor edi, `Avatar` esa faqat bosh harflarni ko'rsatardi.
**Sarlavha (backend):** `GET /api/students/{id}/overview` javobiga `Header` (`StudentOverviewDto` **oxiriga**; `StudentDto` kengaytirilmadi — u ro'yxatda ham ishlatiladi va har qator uchun guruh/o'qituvchi qidiruvi bo'lib qolardi): `HasPhoto`, `PhotoVersion`, `Groups[]` (faol guruhlar, **birinchi qo'shilgani boshda**: `GroupName`, `BranchName`, `CourseName`, `MainTeacherNames[]`, `SupportTeacherNames[]`), `Parents[]` (ism, telefon, munosabat). Guruhsiz student — bo'sh ro'yxatlar, xatosiz. Ko'rinish `students.view` scope'i bilan bir xil (student va ota-ona o'z sarlavhasini ko'radi). Hujjatdagi `PhotoUrl` o'rniga `PhotoVersion`: rasm bearer token bilan olinadi, shuning uchun tayyor havola foyda bermaydi.
**Foto (backend):**
- `User.PhotoKey` (`string?`, 64) + `SetPhoto` — migratsiya `AddUserPhoto`. `Api/Users/UserPhotoStorage.cs` (`StudentDocumentFileStorage` naqshi): `App_Data/user-photos`, **2 MB**, faqat `.png/.jpg/.jpeg/.webp` (**SVG yo'q** — skript tashishi mumkin va ilova o'z origin'idan beriladi), tasodifiy kalit, eski foto almashganda o'chiriladi; `Program.cs` da `UserPhotos` bo'limi.
- `POST/DELETE/GET /api/users/{id}/photo` (`UserPhotosController`). O'zgartirish: `users.edit` **va** `UserAccess.CanTouchAsync` — S01.04 qoidasi (`UsersController.CanTouchUserAsync` shu yerga chiqarildi, endi ikkalasi bitta manba: filial administratori filial odamlariga tegadi, **Super Admin'ga tegmaydi**; o'qituvchi/student/ota-ona faqat o'zinikiga — `users.edit` ularda `Self`). Ko'rish (`users.view`): o'zi; org-miqyosida hamma; student bo'lsa — profilini ocha oladigan kishi (`CanAccessStudentAsync`); ota-ona bo'lsa — farzandlaridan birining profilini ocha oladigan; xodim bo'lsa — filialdosh yoki chaqiruvchi ko'ra oladigan guruhda dars beradigan (student o'z o'qituvchisini, ota-ona farzandining o'qituvchisini ko'radi). Javob: `Cache-Control: private, max-age=300`, `X-Content-Type-Options: nosniff`; manzilga `?v=<PhotoVersion>` qo'shiladi, shuning uchun yangi foto keshdan keyin ham darhol ko'rinadi. Skaner (S08.03) shu yo'lga keyin ulanadi.
**Frontend:** `Avatar` `photo={{userId, version}}` propini oladi (faqat berilganda `apiFetchBlob` → object URL, `useQuery` bilan versiya bo'yicha keshlanadi; rasm kelguncha yoki xato bo'lsa bosh harflar) — **ro'yxatlarda `photo` berilmaydi** (bir sahifada 50 ta so'rov bo'lmasligi uchun). `components/PhotoUpload.tsx` — "Foto yuklash / almashtirish / olib tashlash", fayl turi va 2 MB oldindan tekshiriladi. Tugma: o'z profilida **yoki** `users.edit` scope'i `AllOrganization/MyBranches` bo'lsa (o'qituvchi student profilini ochganda `users.edit` bor, lekin faqat o'zi uchun — shuning uchun ruxsatning o'zi emas, scope tekshiriladi). Sarlavha: foto, ism+holat; email · telefon · ID · daraja · qo'shilgan sana; **Filial · Kurs · Guruh (+N guruh)**; **O'qituvchi: … · Yordamchi: …**; **Ota-ona: ism (munosabat) telefon**. Mobilda qatorlar o'raladi (390 px da gorizontal scroll yo'q). Ota-ona bog'lanishi/uzilishi endi `overview` ni ham yangilaydi.
**Ataylab qilinmadi:** ro'yxatlarda foto (yuqoridagi sabab); `users/[id]` va o'qituvchi sahifasi sarlavhasi (S05.01/S05.02 shu mexanizmdan foydalanadi); fayl mazmunini tekshirish (magic bytes/skaner) — S08.03; Telegram (bot bloklangan).
**Test:** `StudentHeaderTests` (5): filial/kurs/guruh/asosiy va yordamchi o'qituvchi; ota-ona (ism, telefon, munosabat; student o'z sarlavhasida ham ko'radi); guruhsiz student — bo'sh sarlavha; ikki guruh — birinchi qo'shilgani boshda va tark etilgani ro'yxatda yo'q; boshqa guruh studentiga 403. `UserPhotoTests` (5): yuklash/almashtirish/o'chirish (tur, baytlar, `nosniff`, `max-age=300`, `HasPhoto`/`PhotoVersion`); **2 MB+1 bayt, `.exe`, `.svg`, `.html`, `photo.png.exe`, kengaytmasiz, bo'sh fayl, faylsiz → 400** va qabul qilingan foto o'zgarmaydi; kim o'zgartira oladi (sinfdosh va o'z o'qituvchisi 403; filial administratori filial studentiga 200, **Super Admin'ga 403**; o'qituvchi o'zinikiga 200; yo'q id 404; anonim 401); kim ko'ra oladi (student, o'qituvchi, admin, operator, superadmin 200; sinfdosh, boshqa guruh studenti 403; ota-ona faqat farzandinikini); o'qituvchi rasmini o'z guruh studenti ko'radi, begona guruh studenti ko'rmaydi (403).

### 1.42. Qolgan Learning Signal'lar — Writing grammatikasi, Listening, Reading (audit M5; reja `docs/11` 4.5; `docs/21.09-audit-jurnali/S04.05`)
**Muammo:** TZ §7 "Learning Signals" 6 ta; ishlaydiganlari 2 ta (yo'qlik, homework). Qolgan to'rttasining ma'lumoti bor edi, qoidasi yo'q edi.
**Endi:** uchta qoida Automation Engine katalogida (`AutomationRuleCode`: `WRITING_GRAMMAR_SIGNAL`, `LISTENING_LOW_SIGNAL`, `READING_TIME_SIGNAL`); har biri yoqiladi/o'chiriladi, chegaralari `/automation` da sozlanadi, amallari: `raiseSupport` ("Support tavsiyasi (administratorga xabar bilan)") va `notifyTeacher` ("Asosiy o'qituvchiga bildirishnoma"), ikkalasi standart yoqiq. `FollowUpRules` `partial` bo'ldi (`FollowUpRules.LearningSignals.cs`), `RunSweepAsync` ularni ham ishga tushiradi (`SweepResult.LearningSignals`, `POST /api/automation/run` javobida `learningSignals`, "So'nggi faoliyat"da qoida nomi bilan).
| Qoida | Shart | Parametrlar (min–max, standart) |
|---|---|---|
| Writing: grammatika zaif (`WRITING_GRAMMAR_WEAK`) | oxirgi **N** ta **yakunlangan** insho grammatika bandi (o'qituvchi tuzatgani `TeacherGrammarBand` ustun) **hammasi** M dan past | N 2–10 (3), M 3.0–7.0 (5.5) |
| Listening: past natijalar (`LISTENING_LOW`) | oxirgi **N** ta imtihonda (Listening baholangan) Listening hammasi M dan past | N 2–6 (2), M 3.0–7.0 (5.0) |
| Reading: vaqtni boshqarish (`READING_TIME`) | Reading bandi **Reading baholangan oxirgi imtihon**dagi boshqa bo'limlar (Listening, Writing, Speaking — **kamida ikkita**) o'rtachasidan **D** dan ko'p past (farq ≥ D) | D 0.5–3.0 (1.0) |
| Speaking: fluency past (`SPEAKING_FLUENCY_LOW`) — 1.43 | oxirgi **N** ta **yakunlangan, xodimga ulashilgan** mashqning fluency bandi (o'qituvchi tuzatgani ustun) hammasi M dan past | N 2–6 (3), M 3.0–7.0 (5.0) |
- **"Past" = qat'iy past:** aynan chegaradagi band signal bermaydi. Hisoblash `LearningProfileCalculator` da (`AllBelow`, `ReadingGap`) — sof funksiyalar, unit-testlangan.
- **Kasr parametrlar:** katalog parametrlari butun son edi, band esa 5.5. `AutomationParameterDefinition.Scale` qo'shildi (band — 10: 55 saqlanadi, ekran 10 ga bo'ladi); `Min/Max/Default/Value` saqlash birligida, DTO'da `Scale`. Xato xabari ("3.0 dan 7.0 gacha") o'qiladigan birlikda. Mavjud qoidalar `Scale = 1` — o'zgarishsiz.
- **Sabab matni aniq raqamli:** "Oxirgi 3 ta insho: grammatika 4.5, 5.0, 5.0 (chegara 5.5)" (yangisi birinchi); "Oxirgi 2 ta imtihon: Listening 4.0, 4.5 (chegara 5.0)"; "Oxirgi imtihonda Reading 5.5, boshqa bo'limlar o'rtachasi 6.8 (farq 1.3, chegara 1.0)". `/support` da ko'rinadi; `SIGNAL_LABEL` ga uch yozuv.
- **Idempotentlik — mavjud naqshdagidek** (`RaiseSupportSignalAsync`): trigger vaqti = signalga asos bo'lgan **eng yangi natija yozilgan vaqt** (insho `CompletedAt`, imtihon natijasi `CreatedAt`); shu vaqt yoki undan keyin shu kodli case yaratilgan bo'lsa (rad etilgan bo'lsa ham) jim; guruhda ochiq case bo'lsa ham yangisi yaratilmaydi. O'qituvchi bildirishnomasi `dedupeKey` (`learning-signal:{kod}:{student}:{guruh}:{trigger ticks}`) bilan bir marta; yangi past natija — yangi trigger.
- **Guruh tanlash:** imtihon signali — imtihon o'tgan guruh (student hamon faol bo'lsa), aks holda birinchi qo'shilgan faol guruh; insho signali — birinchi faol guruh. **Faol enrollmenti yo'q student** (chiqib ketgan) belgilanmaydi. Yangi `NotificationType.LearningSignal` (`NotificationCatalog` — Teacher; sozlamalarda o'chirish mumkin), qo'ng'iroq belgisida ikonka.
- **Speaking signali dastlab yozilmagan edi** (fayl: "qaror B/C bo'lsa yozmang"); foydalanuvchi "xodimlar speaking'ni ko'ra olishi kerak, muammo yo'q" deb tasdiqlagach qo'shildi — 1.43.
**Frontend:** `/automation` sahifasi katalogdan qurilgan, lekin kasr parametr uchun `scale` qo'llab-quvvatlandi (`5.5` yoziladi va ko'rsatiladi, `step=0.1`, oraliq "3–7 · standart 5.5", saqlanganda 55); "run now" natijasiga "ta o'zlashtirish bo'yicha tavsiya".
**Test:** `LearningSignalTests` (14): har signal uchun **chiqadi** (raqamli sabab, o'qituvchiga xabar, bir marta) va **chiqmaydi** (namuna kam; biri chegaradan past emas; aynan chegara; o'qituvchi tuzatgan band; tugallanmagan/`Failed` insho; Listening baholanmagan imtihon; kichik farq; faqat bitta boshqa bo'lim; Reading oxirgi imtihonda tiklangan); ikki marta sweep — bitta case; rad etilgandan keyin jim, yangi past imtihondan keyin yangi case; tark etgan student; sozlama (N va M o'zgartirilsa hisobga olinadi); amallar alohida o'chiriladi va o'chirilgan qoida hech narsa qilmaydi (qoida sozlamalari test oxirida standartga qaytariladi); katalog (`scale=10`, 55, chegaradan tashqari 75 → 400 "3.0 dan 7.0", Speaking qoidasi yo'q); run-now `learningSignals` va faoliyat jurnali. Sof: `AllBelow`, `ReadingGap`. `AutomationTests` dagi qoidalar soni 7 → 10.

**S04.04–S04.05 yakuni:** integratsion test soni **375** (S04.03 dan +24: S04.04 +10, S04.05 +14), `tsc` va `eslint --max-warnings=0` toza. Migratsiya: `AddUserPhoto` (dev bazaga `update-db` bilan qo'llangan). **Brauzerda tekshirildi** (Playwright, 1280 va 390 px): o'qituvchi ko'rinishida filial/kurs/guruh/o'qituvchi sarlavhasi; o'qituvchida student profilida foto tugmasi yo'q; student o'z fotosini yuklaydi → doira ichida ko'rinadi, SVG tanlansa server so'ramasdan rad etiladi, olib tashlanadi (dev bazada qoldiq qolmadi); `/automation` da "Writing: grammatika zaif" kartasi 5.5 bilan. Signal `/support` da brauzerda ko'rilmadi (dev bazada past natijali student yo'q; testlar API orqali tekshiradi).

### 1.43. Speaking: o'qituvchi eshitib bahoni tuzatadi + "Speaking tekshiruvi" + fluency signali (foydalanuvchi talabi; `docs/21.09-audit-jurnali/S04.03`, `S04.05`)
**Talab:** xodim studentning Speaking topshirig'ini eshita olsin (S04.03 dagi B qarori) — sababi AI yaxshi baholamagan bo'lsa **o'qituvchi bahoni tuzata olsin**; o'qituvchi oynasida "Speaking baholash" joyi ko'rinmayotgan edi; S04.05 da yozilmay qolgan Speaking signali ham shu bilan.
**Backend:**
- `SpeakingSession` ga o'qituvchi bahosi: `TeacherBand`, `TeacherFluencyBand/LexicalBand/GrammarBand`, `TeacherComment` (≤ 2000), `OverriddenBy/At` + `Override(...)` — migratsiya `AddSpeakingTeacherReview`. **AI bahosi o'zgarmaydi**, o'qituvchiniki uning yonida (Writing override naqshi).
- **Ruxsat `ai.speaking.override`** — SA `ALL_ORG`, AD `MY_BRANCHES`, TC `MY_GROUPS`; **yordamchi o'qituvchida yo'q** (u o'qiydi, baho qo'ymaydi — `ai.writing.override` bilan bir xil). `docs/01` ga qator.
- `POST /api/students/{id}/speaking/sessions/{sessionId}/override` (`StudentSpeakingController`): band majburiy, uchta mezon ixtiyoriy, hammasi 0.5 qadam bilan 0–9 (`WritingRubric.IsHalfBand`), izoh ≤ 2000, aks holda 400. Faqat xodimga ochiq (yakunlangan + `SharedWithStaff`) sessiya — davom etayotgan, tashlab ketilgan, **eski (shaxsiy)** va begona student sessiyasi 404. Hammasi bir amal: `AuditLog` ("Modified", faqat baholar — student so'zlari emas), studentning tarixida `SPEAKING_REVIEWED`, studentga `SPEAKING_REVIEWED` bildirishnomasi (`NotificationCatalog` — Student, o'chirish mumkin).
- Sessiya DTO'si (`SpeakingSessionDto`) oxiriga `TeacherReview` (band, mezonlar, izoh, kim, qachon) va `CanOverride` — student o'z mashqida o'qituvchi bahosini ko'radi; `CanOverride` faqat xodim endpointida va faqat `ai.speaking.override` egasiga `true` (tugma ruxsatga qarab, rolga emas). Ro'yxatlarga `TeacherBand`.
- **Kirish qutisi:** `GET /api/ai/speaking/reviews?pendingOnly=&studentId=` (`SpeakingReviewsController`, `ai.speaking.review`) — o'qituvchi scope'idagi studentlarning tugallangan, ulashilgan mashqlari; `pendingOnly` — hali tuzatilmaganlari. O'qituvchi bosh sahifasida "Speaking tekshiruvi" soni (`TeacherDashboardDto.PendingSpeakingReviews`).
- **Speaking fluency signali** (`SPEAKING_FLUENCY_SIGNAL` / `SPEAKING_FLUENCY_LOW`): 1.42 jadvalidagi to'rtinchi qoida. Faqat `SharedWithStaff` + `Completed` sessiyalar; **`Reason` da faqat band raqamlari** ("Oxirgi 3 ta Speaking mashqi: fluency 4.5, 4.0, 4.5 (chegara 5.0)") — student aytgan bir so'z ham emas; o'qituvchi tuzatgan fluency AI'nikidan ustun, shuning uchun o'qituvchi "yaxshi" deb tasdiqlagan mashq signalni takrorlamaydi.
**Frontend:**
- **Menyu:** "Speaking tekshiruvi" (`/ai/speaking/reviews`) — Writing tekshiruvi yonida (SA, AD, TC, ST). Sahifa: chapda ro'yxat ("Tekshirilmagan / Hammasi", band va "AI bahosi"/"tasdiqlangan"), o'ngda `SpeakingReviewPanel`.
- `components/ai/SpeakingReviewPanel.tsx` — AI hisobot, suhbat matni va har javobning yozuvi ("Yozuvni eshitish"), pastda o'qituvchi bahosi formasi (umumiy band, 3 mezon, izoh). Student profilidagi Speaking tabi ham shu panelni ochadi (bitta komponent, ikki joy). Saqlagach javob keshga qo'yiladi (har yuklash audit yozuvi — qayta yuklanmaydi).
- Student o'z sahifasida o'qituvchi bahosini yashil blokda ("O'qituvchi bahosi: 6.5 · Ism · sana · AI bahosi o'zgarmagan") va tarixda "o'qituvchi bahosi" belgisi bilan ko'radi. Yon tuzatish: hisobot kartasi tor ustunda mezonlarni uch ustunga sig'dirolmasdi (raqam kartadan chiqib ketardi) — `compact` rejim.
- `/automation` da yangi qoida, `/support` da "Speaking: fluency past", tarixda va qo'ng'iroq belgisida yangi tur.
**Test (+7, jami 382):** `SpeakingReviewTests` — tuzatish (student o'z mashqida ko'radi, AI bandlari o'zgarmagan, bildirishnoma, tarix, audit faqat baholar, `CanOverride`); validatsiya (6.3, 9.5, −0.5, mezon 6.25, izoh 2001 → 400; davom etayotgan/yo'q/eski sessiya → 404, hech narsa saqlanmagan); **ruxsat** (yordamchi o'qituvchi ochadi, `CanOverride=false`, tuzatsa 403; student/operator/ota-ona/begona guruh o'qituvchisi 403; anonim 401; admin va superadmin 200, oxirgisi qoladi); kirish qutisi (o'z guruhi studentlari bor, begona yo'q; `pendingOnly` tuzatilganini chiqarib tashlaydi; `studentId` filtri; ruxsatsizlarga 403); dashboard soni +1 → tuzatgach −1. `LearningSignalTests` +2: fluency signali (raqamli sabab, o'qituvchiga xabar) va jim qolish (2 ta mashq, **shaxsiy mashq**, o'qituvchi tuzatgan, tugallanmagan); katalog testi to'rtta qoidani kutadi, `AutomationTests` 10 → 11.
**Ataylab o'zgarmadi:** Learning profildagi Speaking — faqat imtihon (S04.02). O'qituvchi tasdiqlagan Speaking bandini ham qo'shish mumkin (Writing'dagidek `TeacherBand ?? AI`), lekin AI taxmini profilga kirmasligi qarori shu — o'zgartirish `LearningProfile` da bitta so'rov.

**S04.01–S04.05 yakuniy tekshiruv (2026-09-24):** integratsion test soni **383**, `tsc` va `eslint --max-warnings=0` toza. Qo'shimcha topilganlar: (1) **foydalanuvchi o'chirilganda foto fayli diskda yetim qolardi** — `UsersController.Delete` endi faylni ham o'chiradi (`UserPhotoTests` +1); (2) Learning profilga ota-ona va operator kirishi testlanmagan edi — `LearningProfileTests` ga qo'shildi; (3) `LoginThrottleTests` (oldingi sessiyadan, S04 ga aloqasiz) vaqtga bog'liq edi: `ManualClock` qat'iy `2026-09-24 12:00 UTC` dan boshlanardi, real soat undan o'tgach kesh yozuvi darhol eskirib 2 test yiqilardi — soat endi haqiqiy "hozir"dan boshlanadi. Brauzerda qo'shimcha ko'rildi: imtihon natijasi formasida Grammatika %/Lug'at % maydonlari, haqiqiy insholar bilan Writing tabi, `/automation` da 5.0 yozilib saqlangan (50 bo'lib saqlanadi), 5.55 ekranning o'zida rad etiladi. **Ochiq qolganlar:** quyidagi "ataylab" ro'yxat (foto mazmuni tekshiruvi — S08.03; o'qituvchi/xodim foto tugmasi — S05.01/S05.02; Learning profilda faqat chaqiruvchi ko'ra oladigan guruhlar; signal `/support` da brauzerda ko'rilmadi; prod nusxa yangilanmagan — `deploy.sh` kerak, u migratsiya va ruxsatlarni o'zi qo'llaydi).

### 1.44. Teacher 360°: Umumiy, Davomat, Homework, Imtihonlar, Yordamchilar, Faoliyat tarixi (audit M4; reja `docs/11` 5.1; `docs/21.09-audit-jurnali/S05.01`)
**Muammo:** TZ §6 da Teacher 360° 13 tab; sahifada 4 tasi (Guruhlar, Studentlar, Jadval, Natijadorlik) bor edi. Feedback va KPI — Faza 6 (`S10.02`, `S10.01`); Portfolio — 1.45.
**Backend:**
- **Kirish qoidasi bitta joyda:** `Api/Users/TeacherAccess.cs` (`CheckTeacherAccessAsync`) — o'zi yoki keng scope (`AllOrganization`/`MyBranches` va filial ustma-ust tushishi); hamkasb ro'yxatda ko'radi, sahifasini ochmaydi. `GetById`, `Performance` va yangi endpointlar shundan foydalanadi (avval shu qoida uch joyda nusxalangan edi).
- **Hisoblar bitta joyda:** `Api/Users/TeacherWorkloadQueries.cs`. `Performance` endi shu yerdan hisoblaydi (mavjud `TeacherPerformanceTests` o'zgarishsiz yashil) — bitta o'qituvchi uchun ikki xil davomat foizi bo'lmaydi. **Davomat** — bekor qilingan dars kirmaydi (`Held()`), `Excused` maxrajdan chiqadi; **umumiy foiz — barcha guruh belgilarining nisbati**, guruh foizlarining o'rtachasi emas (2 talabali guruh 15 talabalinikidek og'irlik qilmasin). Belgilar bazada **dars bo'yicha** sanaladi (`GROUP BY LessonId`), xotiraga har belgi bitta qator bo'lib kelmaydi.
- `GET /api/teachers/{id}/workload?from=&to=` (`users.view` + `TeacherAccess`): guruhlar bo'yicha davomat (o'tkazilgan/bekor qilingan darslar, kelgan/belgilangan, %), homework (berilgan, tekshirilmagan, tekshirish o'rtacha vaqti — `ReviewedAt − SubmittedAt`, soat), imtihon (guruh bo'yicha Overall o'rtachasi + oylik trend). **Davr:** standart oxirgi 30 kun; `to − from ≤ 366` (dashboarddagi qoida), aks holda 400. Darslar va homework davrga **dars sanasi** bo'yicha, imtihon o'z sanasi bo'yicha kiradi; kunlar **filial vaqt zonasida** kesiladi (`BranchClock`, bir kun zaxira bilan olinib filtrlanadi).
- `GET {id}/summary` — Umumiy tab: telefon, filial(lar) (administrator faqat o'zi bilan umumiy filiallarni ko'radi), ish boshlagan sana (`User.CreatedAt`), guruh/faol student soni. `GET {id}/support-teachers` — uning guruhlaridagi yordamchilar **va** aksincha (u yordamchi bo'lsa — kimning guruhida).
- **Faoliyat tarixi:** `GET /api/audit-logs?userId={id}` — filtr allaqachon bor edi (`userId` = jurnalni yozgan **aktyor**), yangisi qo'shilmadi; `audit.log.view` faqat SA/AD, o'qituvchi 403.
**Frontend:** `components/teachers/` — `TeacherOverviewTab`, `TeacherWorkloadTabs` (Davomat/Homework/Imtihonlar **bitta so'rov**, davr bitta sahifa holatida — uch tab bir xil haftalarni tasvirlaydi), `TeacherSupportTab`, `TeacherActivityTab` (sahifalangan). Tab tartibi: Umumiy · Guruhlar · Studentlar · Jadval · Davomat · Homework · Imtihonlar · Yordamchilar · Natijadorlik · (Portfolio) · Faoliyat tarixi (faqat `can("audit.log.view")`). Grafik yo'q — kichik jadvallar, dinamika strelka bilan. `formatYearMonth` (`lib/format-date.ts`).
**Test:** `TeacherWorkloadTests` (12): aniq raqamlar guruhma-guruh (davomat 50%/100%, umumiy 2/3 = 66.7); tekshirish vaqti (5 soat va 2 soat → guruhda 5.0/2.0, jami 3.5); imtihon o'rtachasi va oylik trend; bekor qilingan darsning belgilari kirmaydi; bo'sh davr — nol va `null`, xato emas; standart 30 kun, 366 kun chegarasi, 400; o'zi 200/hamkasb 403; administrator o'z filiali 200/boshqa filial 403; student 403, anonim 401, o'qituvchi bo'lmagan 404; `summary`; `support-teachers` ikki yo'nalishda; faoliyat tarixi (`userId` filtri; o'qituvchi 403).
**Sinov muhiti haqida:** yangi testlar o'z **filialida** ishlaydi (`ZZ Workload …`, birinchi ishlatilganda yaratiladi) va o'qituvchi/talabalari ro'yxatda **oxirida** turadigan nom bilan yaratiladi — boshqa testlar demo o'qituvchini/talabani `?size=25` ning birinchi sahifasidan qidiradi, ko'p qo'shilgan hisoblar ularni sahifadan surib chiqarardi (birinchi urinishda 91 test shunday yiqildi). `GroupRecommendationTests` ham guruhlar demo filialdagi eng yaxshi beshlikni egallab olganda yiqilardi. Qo'shimcha: `EnrollmentLifecycleTests` da "Nigora" talaba `?size=200` ning ichidan emas, qidiruv bilan topiladi.

### 1.45. O'qituvchi portfoliosi — IELTS, sertifikatlar, tajriba (foydalanuvchi talabi 2026-09-21; `docs/21.09-audit-jurnali/S05.02`)
**Talab:** Teacher 360° da o'qituvchining shaxsiy portfoliosi — IELTS nechi, sertifikatlar va shunga o'xshash. TZ §6 da yo'q edi.
**Ma'lumotlar (migratsiya `AddTeacherPortfolio`, 3 jadval; `Modules.Identity/Domain/TeacherPortfolio.cs`):** `TeacherProfile` (bio, ixtisoslik, tajriba yili 0–60, ta'lim, tillar, o'qita boshlagan sana; `UserId` unique), `TeacherCredential` (tur: `ExamScore/Certificate/Degree/Course/Award/Other`, nom, beruvchi, **ball matn** — "8.0 (L 8.5, R 8.0…)", berilgan/amal sanasi, tekshirish havolasi/raqami, izoh, ixtiyoriy fayl, `IsVerified/VerifiedBy/VerifiedAt`), `TeacherExperience`. Foydalanuvchi o'chirilsa qatorlar (FK cascade) **va fayllar** (`UsersController.Delete`) ketadi. `TeacherProfile` va `TeacherCredential` auditga (`AuditedTypes`) tushadi.
**Qoidalar:**
- **Tasdiqlash.** O'qituvchi qo'shgani **Tasdiqlanmagan**. Administrator hujjatni (fayl yoki `Reference`) ko'rib tasdiqlaydi. **Tasdiqlangan ma'lumotning tur/nom/beruvchi/ball/sanalar/`Reference`/fayli o'zgarsa tasdiq avtomatik bekor bo'ladi** (javobda `verificationReset: true`, ekran aytadi); faqat **izoh** (`Note`) tasdiqni saqlaydi. *Hujjatdan chetga chiqilgan joy:* `Note` maydoni hujjatda yo'q edi, lekin testlar ro'yxati "izohni o'zgartirsa saqlanadi" deydi — shu uchun qo'shildi; `Reference` ham tasdiqlangan narsaning bir qismi deb hisoblandi.
- **O'z hujjatini o'zi tasdiqlamaydi:** `verify/unverify` — `teacher.portfolio.verify` (SA, AD) **va** `id == o'zi` bo'lsa faqat `AllOrganization` scope'ga ruxsat (yuqorisida so'raydigan odam yo'q). Administrator o'zi kiritgan yozuvni `verified: true` bilan darhol tasdiqlangan qo'shishi mumkin (faqat tasdiqlash ruxsati egasiga, aks holda 403). Ekranda bu yo'l "Hujjatni ko'rdim — darhol tasdiqlansin" — fayl avval yuklanadi, keyin tasdiqlanadi (fayl almashtirish tasdiqni bekor qiladi).
- **Kim nimani qiladi.** Ruxsatlar: `teacher.portfolio.view` (SA/AD/o'qituvchi/yordamchi — o'zi), `.edit` (xuddi shunday), `.verify` (SA/AD). Yozishda `TeacherAccess` **va** `UserAccess.CanTouchAsync` (S01.04: filial administratori Super Admin/Developer'ga tegmaydi, o'qituvchi faqat o'zi). Operator portfoliosini ko'rmaydi. `docs/01` ga 3 qator.
- **Fayl:** `TeacherCredentialFileStorage` (`App_Data/teacher-credentials`, **10 MB**, faqat `.pdf/.png/.jpg/.jpeg/.webp` — SVG/Office yo'q), tasodifiy kalit; `GET …/file` **faqat `.view` egasiga** (o'qituvchi + xodim), `Cache-Control: no-store`, `nosniff`; javobda fayl **kaliti chiqmaydi**. Fayl mazmunini tekshirish (magic bytes/skaner) — `S08.03`.
- **Limit:** ≤ 50 credential, ≤ 30 tajriba yozuvi (409). Tekshiruvlar: uzunliklar, `ExpiresOn ≥ IssuedOn`, `ExamScore` uchun ball majburiy, tajriba yillari 1950…hozir.
- **Bildirishnoma:** o'qituvchi **o'zi** qo'shsa yoki tasdiqlangan ma'lumotni o'zgartirsa — uning filiallari administratorlariga `CREDENTIAL_SUBMITTED` (administrator o'zi kiritsa xabar yo'q); tasdiqlansa — o'qituvchiga `CREDENTIAL_VERIFIED` (`NotificationCatalog`, sozlamada o'chiriladi).
- **Sarlavha chip'lari:** eng yangi **3 ta tasdiqlangan, muddati o'tmagan** `ExamScore` ("IELTS Academic 8.0 ✓"); muddati tugagan yozuv ro'yxatda "Muddati tugagan" belgisi bilan turadi, lekin chipga kirmaydi (hujjatda bu aytilmagan — chip yolg'on maqtanmasin deb). `GET /api/teachers` va `summary` `Specialization`+`Headline` beradi — faqat portfolio o'qiy oladigan chaqiruvchiga va u ocha oladigan qatorlarga (hamkasbning sertifikati ko'rinmaydi); chiplar **bitta so'rovda** yig'iladi (N+1 yo'q).
**Frontend:** `TeacherPortfolioTab` (Haqida · Malaka va sertifikatlar · Ish tajribasi), `TeacherCredentials` (kartalar, tasdiq belgisi kim tasdiqlaganini ko'rsatadi, tasdiqlashdan oldin "Hujjatni ochish", tahrirlashda "tasdiq bekor bo'ladi" ogohlantirishi), sarlavhada chip'lar (`TeacherHeadline`), `/teachers` ro'yxatida ixtisoslik + 2 chip. Tugmalar ruxsat kodiga qarab (`can`), rol nomiga emas; hover'ga bog'liq narsa yo'q.
**Keyin qo'shildi (foydalanuvchi "hammasini bajar" degach):**
- **3.5 "Mening o'qituvchim"** — yangi ruxsat `teacher.portfolio.view.public` (Student `Self`, Parent `MyChildren`; `docs/01` da) va `GET /api/teachers/{id}/portfolio/public`: o'qituvchi shu student(lar)ning **faol** guruhida `GroupStaff` bo'lsa (aks holda 403; xodim va o'qituvchida bu ruxsat yo'q). Javobda **faqat tasdiqlangan** yozuvlar; `Reference`, izoh, fayl (nomi ham), kim tasdiqlagani yo'q. Frontend: student/ota-ona `/teachers/{id}` ni ochsa 360° emas, `PublicTeacherPortfolio` ko'rinadi; guruh sahifasidagi o'qituvchi ismi shu sahifaga havola (faqat shu ruxsat egasiga). O'qituvchining fotosini student ko'ra olishi 1.41 da bor edi.
- **O'qituvchi sarlavhasida foto** (1.41 "S05.01/S05.02 shu mexanizmdan foydalanadi" degan edi, qilinmagan edi): `TeacherOverviewDto.PhotoVersion`, `Avatar` + `PhotoUpload` (o'z sahifasida yoki `users.edit` scope'i `AllOrganization/MyBranches` bo'lsa).
- **Hujjatni tizim ichida ko'rish (foydalanuvchi talabi):** "Hujjatni ochish" faylni yuklab olardi; endi `CredentialFileViewer` oynasida rasm yoki PDF ko'rinadi (fayl blob sifatida olinadi, oyna yopilganda object URL bekor qilinadi); nusxa saqlash tugmasi faqat o'qituvchi va xodimda. **Student va ota-ona ham ko'radi:** `GET /api/teachers/{id}/portfolio/public/credentials/{cid}/file` — o'sha `teacher.portfolio.view.public` va o'sha kirish qoidasi, faqat **tasdiqlangan** yozuv skani (tasdiqlanmagani 404), `no-store`; ochiq javobda faqat `Id` va `FileContentType` (fayl nomi/hajmi/kaliti yo'q), saqlash tugmasi yo'q. *Ehtiyot:* sertifikat skani shaxsiy hujjat bo'lishi mumkin (pasport bilan aralash) — shuning uchun administrator tasdiqlashdan oldin hujjatni ko'radi, tasdiqlangandan keyin fayl almashtirilsa tasdiq bekor bo'ladi va sken talabalarga yopiladi.
- **O'qituvchining o'z profilida** ("Mening profilim" → `/users/{id}`) ham Portfolio bo'limi: qo'shish, tahrirlash, ko'rish, o'chirish (`TeacherPortfolioTab`, faqat `teacher.portfolio.view` scope'i `Self` bo'lgan o'zi uchun).
- Yon tuzatish: student teacher sahifasini ochganda ruxsatlar yuklanmasdan 360° so'rovi ketib 403 berardi — endi `ready` kutiladi.
- **O'qituvchi bosh sahifasida** portfolio bo'sh ekan "Portfolioingizni to'ldiring" kartasi (`PortfolioReminder`) — biror narsa qo'shilishi bilan yo'qoladi.
**Test:** `TeacherPortfolioTests` (17; +2 — "Mening o'qituvchim": student va ota-ona faqat tasdiqlanganini ko'radi, javobda `Reference`/izoh/fayl yo'q; boshqa guruh o'qituvchisi, xodim va to'liq portfolio studentga yopiq): o'zi CRUD; hamkasb 403 (GET/PUT/POST); administrator o'z filiali 200/boshqa 403, operator 403; o'qituvchi bo'lmagan 404, anonim 401; faqat SA/AD tasdiqlaydi, o'zinikini hech kim (o'qituvchi 403; filial administratori 403); administrator darhol tasdiqlangan qo'shadi; **ball o'zgarsa `IsVerified=false`, izoh o'zgarsa saqlanadi**; fayl yuklash/almashtirish (tasdiq bekor)/o'chirish, 10 MB+1/`.exe`/`.svg`/bo'sh → 400, student/ota-ona/hamkasb/operator 403, `no-store`; chip'lar (tasdiqlangan, muddati o'tmagan, faqat `ExamScore`) va ro'yxatda kimga ko'rinishi; bildirishnomalar; audit jurnalida tasdiqlash (`IsVerified":true`); limitlar (51-credential va 31-tajriba → 409); noto'g'ri kiritishlar 400; hisob o'chirilsa qatorlar va fayl ketadi.

### 1.46. Filial dashboardi: O'quv, O'qituvchilar, CRM, Moliya (audit M6; reja `docs/11` 5.2; `docs/21.09-audit-jurnali/S05.03`)
**Muammo:** `/branches/{id}` umumiy dashboardning shu filialga qulflangan nusxasi edi: Finance to'liq, lekin TZ §5.2 ning Academic, Teachers, CRM bo'limlari yo'q.
**Backend — bitta so'rov:** `GET /api/dashboard/branch/{id}?from=&to=` (`dashboard.view`; scope `AllOrganization/MyBranches`, aks holda 403; filial yo'q → 404; boshqa filial 403; `to − from ≤ 366`).
- **Umumiy va Moliya** — mavjud tashkilot dashboardining shu filialga kesimi: `DashboardController.Organization` tanasi `BuildOrganizationAsync` ga chiqarildi (davr qoidasi — `ResolvePeriod`), endi ikki endpoint bir xil sanaydi. `OrganizationDashboardDto` oxiriga `RefundedAmount`, `DiscountAmount` (Moliya tabi uchun) va `AiRequests/AiCostUsd/AiActiveStudents` (1.47) qo'shildi — ixtiyoriy, mavjud testlar buzilmagan.
- **O'quv** (`Dashboard/BranchQueries.AcademicAsync`, hammasi bazada `GROUP BY`): Listening/Reading/Writing/Speaking/Overall (0–9) va Grammar/Vocabulary (%) o'rtachalari — **baholanmagani hisobga olinmaydi, hech narsa yo'q bo'lsa `null` (nol emas)**; oylik Overall trendi (**davr oxirigacha oxirgi 6 kalendar oy**); davomat (bekor qilingan dars kirmaydi); o'tkazilgan/bekor qilingan darslar. Davr filialning **o'z kalendarida** instant oralig'i sifatida kesiladi (`BranchClock.StartOfLocalDay(tz, sana)` — yangi).
- **O'qituvchilar** (`BranchQueries.TeachersAsync`): har o'qituvchi/yordamchi uchun guruhlari bo'yicha **qo'shilgan** (o'rtacha o'rtachasi emas) darslar, davomat (`TeacherWorkloadQueries` — Teacher 360° dagi bilan bir xil raqam), homework tekshirish vaqti va tekshirilmagani, **retention** (davr boshida guruhda bo'lgan, kursni bitirmagan studentlarning hali `Active` qismi; hech kim bo'lmasa `null`), support case'lar (ochiq; davrda yopilgan), **reyting** davomat bo'yicha (belgisi bor guruhlar birinchi). Bo'lish uchun `TeacherWorkloadQueries` ga `HomeworkRawAsync` (yig'indilar) chiqarildi.
- **CRM** (`Api/Crm/LeadFunnelQueries.cs` — S06.02 shuni qayta ishlatadi): **kohorta** — davrda kelgan lidlar va ular hozir qayerda: lidlar, bog'lanilgan (`Lead` holatidan chiqqan — lidda holat tarixi yo'q), trial band, trial keldi, studentga aylandi, **trial → student**, **trial → to'lov** (o'sha student bekor qilinmagan to'lov qilgan), yo'qotilgan (`Lost`+`NotInterested`), **manba bo'yicha** taqsimot.
- **Ruxsatlar:** to'lov raqamlari `payments.view`siz nolga tushadi (`FinanceVisible=false`, "Moliya" tabi yo'q); AI raqamlari `ai.usage.view`siz `null`; **o'qituvchilar jadvali yangi `dashboard.teachers.view`** (SA/AD) bilan — hujjat `users.view` deydi, lekin operatorda `users.view` bor (`MyBranches`), testlar ro'yxati esa operatorga bu blok ko'rinmasligini talab qiladi; shu uchun alohida kod (`docs/01` ga qator). Operator: Umumiy, O'quv, CRM.
- **Kesh:** `BranchDashboardCache` (`IMemoryCache`, `Dashboard:BranchCacheSeconds`, standart **300 s**; 0 — o'chiq). Kalit — tashkilot+filial+davr; saqlanadigani **to'liq** javob, chaqiruvchining ruxsatlari **keshdan keyin** qo'llanadi (kesh ruxsatsizga pul yoki o'qituvchilarni bermaydi). Faqat muvaffaqiyatli yig'ilgani saqlanadi. Bir nusxa uchun xotirada (S09.07 Redis'ga ko'chiradi). Test muhitida o'chiq (`Dashboard__BranchCacheSeconds=0`), kesh testi o'zi yoqadi.
- **Unumdorlik:** dev bazada (87 talaba/146 dars) yig'ish ~30 ms, keshdan ~7–20 ms. **5000 talaba bilan `EXPLAIN ANALYZE` o'lchanmadi** (dev bazada 180 talaba — kam; sun'iy ma'lumot yaratilmadi). Dizayn shunga qarab: belgilar/imtihon natijalari bazada guruhlanadi; xotiraga darslar (bir necha ming qator) va homework topshiriqlari keladi; 366 kunlik davrda katta filialda so'nggi ikkitasi eng og'ir joy — kerak bo'lsa keyin shu ikkisi ham SQL'ga o'tadi.
**Frontend:** `npm install recharts`; `components/dashboard/BranchDashboard.tsx` (bitta `useQuery`, tab almashganda qayta yuklanmaydi; davr tanlash): **Umumiy** (`OrganizationCards` — tashkilot dashboardidan ajratib olingan bir xil kartalar), **O'quv** (o'rtacha ball polosalari — matn bilan, rang yolg'iz belgi emas; Overall trendi — `LineChart`), **O'qituvchilar** (reyting jadvali), **CRM** (voronka — `BarChart`, manba jadvali), **Moliya**. Grafik ranglari dizayn tokenlari (`var(--color-brand-solid)` …) — tungi rejimda ham o'qiladi. Mobilda (390 px) kartalar ustma-ust, jadval ustunlari yashirinadi.
**Test:** `BranchDashboardTests` (8) — alohida filialda aniq raqamlar (o'quv: ballar/Grammar/Vocabulary/trend/bekor qilingan dars; o'qituvchilar: reyting, tekshirish vaqti, retention 1/2, ochiq support; CRM: 6→5→3→2→1, trial→student 1, trial→to'lov 1, manba jadvali; Umumiy = tashkilot dashboardi shu filial uchun, `Collected=200000`); administrator o'z filiali 200/boshqasi 403/yo'q 404, teacher/student/parent 403, anonim 401; operatorga pul va o'qituvchilar yo'q, administratorga bor; 366 kundan uzun/teskari davr 400; kesh (TTL ichida eski, o'chirilgach yangi). Mavjud `DashboardTests` yashil.

### 1.47. Kichik ishlar: AI ishlatilishi kartasi, lid doskasi sinovi, operator 403 (audit M7; reja `docs/11` 5.3, 5.4; `docs/21.09-audit-jurnali/S05.04`)
**A. AI kartasi.** `Api/Ai/AiUsageQueries.cs` — qaysi so'rovlar hisobotga kirishi (`Requests`: tashkilot/filial scope'i, UTC kun davri) va qanday sanalishi (`Totals`, `BriefAsync`) shu yerga chiqarildi; `AiUsageController` va dashboard **bir xil** kodni ishlatadi (test hisobot bilan solishtiradi). `OrganizationDashboardDto` ga `AiRequests`, `AiCostUsd`, `AiActiveStudents` — faqat `ai.usage.view` egasiga (aks holda `null`; filial dashboardida ham keshdan keyin kesiladi). `OrganizationDashboard` da "AI ishlatilishi" kartasi `/ai/usage` ga havola. Test (+2): raqam `/api/ai/usage/summary` bilan mos (SA va AD); operatorda maydon yo'q.
**B. Lid doskasi sudrash yo'llari — Playwright bilan sinaldi** (haqiqiy `mouse` hodisalari, operator, dev baza). **Topilgan xato tuzatildi:** karta `DragOverlay` da ustun kengligidan (`w-72` ≈ 288 px, ustun ≈ 205 px) keng ko'rinadi, dnd-kit esa standart `rectIntersection` bilan **overlay to'g'ri to'rtburchagini** solishtiradi — kursor "Bog'lanildi" ustunida turganda karta **keyingi** ustunga (Trial band) tushardi (yangi lidni Bog'lanildi'ga sudrash Trial oynasini ochardi). Endi `pointerWithin` (kursor qaysi ustun ustida bo'lsa shu), kursor hech qaysi ustunda bo'lmasa `rectIntersection`. Natija: Yangi → **Bog'lanildi** holatni o'zgartiradi ✓; → **Trial band** trial oynasini ochadi, holat o'zgarmaydi ✓; Trial band → **Trial'da qatnashdi** — `Scheduled` trial `Attended`, lid `TrialAttended` ✓; → **Studentga aylandi** konvertatsiya oynasini ochadi, o'zi aylantirmaydi ✓; → **Yopilgan** yopish oynasini ochadi (Trial/Qiziqmadi/Yo'qotildi), jim hech narsa qilmaydi ✓. Sensorli qurilma tugmalari (iPad kengligi): **Keldi** (`TrialAttended`/`Attended`) va **Kelmadi** (`TrialMissed`/`Missed`) ✓. Yangi ustun qo'shilmadi.
**C. Operator student profilini ochsa 403 — tuzatildi.** `students/[id]/page.tsx` dagi to'lov tile'i va "To'lovlar" tabi `canSeeFinance(role)` (rol nomi, operatorni kiritardi) edi, `GET /api/students/{id}/finance` esa `payments.view` talab qiladi (operatorda faqat `finance.debt.view`). Endi `can("payments.view")`; `canSeeFinance` o'chirildi. Brauzerda operator profili konsolsiz (tab: faqat Imtihonlar, Hujjatlar), administrator "To'lovlar"ni ko'radi, o'qituvchi o'zgarishsiz. **Rol nomiga tayangan qolgan joylar ro'yxati** (`grep`): `canViewSupport` (SA/AD/TC/ST) = `support.recommend.view` ✓; `canViewStudentDocuments`/`canManageStudentDocuments` = `student.documents.view/manage` ✓; `canSeeHomework = role !== "OPERATOR"` — hozir `homework.view` bilan bir xil natija ✓ (o'zgartirilmadi); `canManageAcademic`/`isTeachingStaff` (`lib/nav.ts`, schedule/groups sahifalari) — **keyingi tozalash**. Bittasi ruxsatdan **farq qilardi** va tuzatildi: `branches/page.tsx` `canManageAcademic` (SA+AD) bilan "Yangi filial" va o'chirish tugmalarini administratorga ham ko'rsatardi, holbuki `branch.create`/`branch.delete` faqat SA'da — endi tugmalar `branch.create/edit/delete` ga qarab (administratorga faqat o'z filialini tahrirlash).
**D. Dev bazadagi test qoldiqlari.** Eskilariga ("Sinov Talaba …" 2 ta, "BFLA …" — yuqoridagi §5) **tegilmadi**: ular dev bazani qayta yaratishni talab qiladi, bu esa foydalanuvchi ruxsatini kutadi. Doska sinovidan **o'zim qo'shgan** "Sinov Lid A…E" (5 ta lid va 3 ta trial) esa lid o'chirish endpointi bo'lmagani uchun aniq shu qatorlarni tanlab (`FullName like 'Sinov Lid %'`, bitta tranzaksiya) o'chirildi — dev bazada qoldiq qolmadi.

**S05.01–S05.04 yakuni:** integratsion test soni **422** (383 dan +39: workload 12, portfolio 17 (skan ko'rish testlari ham shu ichida), filial dashboardi 8, dashboard +2), `tsc` va `eslint --max-warnings=0` toza. Migratsiya: `AddTeacherPortfolio` (dev bazaga `update-db` bilan qo'llangan). Yangi ruxsatlar: `teacher.portfolio.view/edit/verify/view.public`, `dashboard.teachers.view` (`docs/01` da). **Brauzerda tekshirildi** (Playwright, 1280 va 390 px): Teacher 360° barcha tablar; portfolio — qo'shish → hujjat yuklash → tasdiqlash → sarlavhada chip → tahrirlash → o'chirish (dev bazada qoldiq qolmadi); filial dashboardi 5 tab, davr almashuvi; lid doskasi (1.47 B); operator/administrator/teacher profillari. **Ochiq qolganlar:** 5000 talabali `EXPLAIN ANALYZE` (ajratilgan sinov bazasi kerak); `canManageAcademic`/`isTeachingStaff` ni ruxsatga o'tkazish; prod nusxa yangilanmagan (`deploy.sh` migratsiya va ruxsatlarni o'zi qo'llaydi); AD org-miqyosli audit jurnalini o'qiy oladi (filial bo'yicha kesilmagan — S05'dan oldingi holat; portfolio qatorlari ham shu jurnalga tushadi).

### 1.48. Lidda qulay vaqt va daraja + guruh tavsiyasining olti omili (audit M10; reja `docs/11` 6.1; `docs/21.09-audit-jurnali/S06.01`)
**Domen:** `Lead.PreferredTime` (matn ≤100, kodlar vergul bilan: `Morning` 08–12, `Afternoon` 12–17, `Evening` 17–21, `Weekend`; bo'sh = farqi yo'q — massiv ustuni emas, EF quirk'idan qochish uchun) va `Lead.CurrentLevel` (≤50), `UpdatePreferences(...)`. `PreferredTimeCodes.TryNormalize` — kodni belgilangan tartibga soladi, takrorni tashlaydi, noma'lum kod `false` (→ 400). Migratsiya **`AddLeadPreferences`**.
**Backend:** `CreateLeadRequest`/`LeadDto` ga ikki maydon; `PATCH /api/leads/{id}` (`leads.edit`; `UpdateLeadPreferencesRequest`: maydon berilmasa tegilmaydi, `""` — tozalash; boshqa filial lidi 403, konvertatsiya qilingan lid 409). Mavjud `PATCH {id}/status` ga tegilmadi. Daraja testi natijasi `CurrentLevel` ni **o'zi yangilamaydi** (operator tasdiqlaydi — mavjud qaror): lid kartasida "Oxirgi test: B1" ko'rinadi, ixtiyoriy "qo'llash" tugmasi bilan.
**Ballash** endi `Api/Crm/GroupRecommendationScorer.cs` da (controllerdan chiqarilgan, sof funksiya): kurs 35 · **daraja 20** · filial 15 · **qulay vaqt 15** · bo'sh o'rin 10 · faollik 5. `LevelScale` (`A1 … IELTS Advanced`) darajani guruh/kurs **nomidagi** belgidan topadi (kursda daraja maydoni yo'q; butun-belgi moslashuvi: "B1A1x" ichidan "A1" topilmaydi); farq 0 → 100%, 1 → 60%, ≥2 → 0. `LessonTimeBuckets` guruhning yaqin darslarini **filial soat mintaqasida** paqirlarga soladi (dam olish kuni — `Weekend`); hamma yaqin dars qulay oynada bo'lsa 100%, aralash bo'lsa ulushiga qarab. **Ma'lumoti yo'q omil hisobga kirmaydi** (lid darajasi/vaqti yo'q, guruh nomida daraja yo'q, guruhda yaqin dars yo'q) va qolganlar yig'indisi 100% ga qayta normallashtiriladi — soxta ball yo'q. Ball yaxlitlanmaydi (ro'yxat shunga qarab tartiblanadi: ikki guruh ikkalasi 92 ga yaxlitlansa ham teng emas), 0–100 ga qisilgan. Yangi sabablar: "Daraja mos (B1)" / "Daraja yaqin (…)", "Qulay vaqtda (kechqurun)" / "Ba'zi darslari qulay vaqtda".
**Frontend:** `components/crm/LeadPreferencesFields.tsx` (yaratish oynasi va profil uchun bir xil "Qulay vaqt" checkbox'lari + "Daraja"), `LeadPreferencesDrawer.tsx` (profilda tahrirlash; saqlanganda trial/konvertatsiya oynalaridagi tavsiyalar yangilanadi). `TrialDrawer`/`ConvertLeadDrawer` yangi sabablarni o'zi ko'rsatadi.
**Test:** `GroupRecommendationTests` 3 → **11** (+8): daraja (bir xil > 1 pog'ona > 2 pog'ona), qulay vaqt, daraja/vaqt bo'sh → "hisoblanmagan omil nol emas", ball 0–100, `PATCH` saqlaydi/normallashtiradi/tozalaydi va lid filialida qoladi, yaratishda noma'lum vaqt kodi 400.

### 1.49. Manba bo'yicha marketing voronkasi (audit M11; reja `docs/11` 6.2; `docs/21.09-audit-jurnali/S06.02`)
`GET /api/leads/funnel?from&to&branchId&source` (`leads.view`, scope `AllowedBranchIds`; branchId scope'dan tashqarida 403) → `FunnelDto(Stages, BySource)`. Oltita bosqich: **Lidlar → Bog'lanilgan → Trial band → Trial keldi → Student bo'ldi → To'lov qildi**; har birida soni, oldingisiga nisbatan % (`ConversionFromPrevious`) va lidlar soniga nisbatan % (`ShareOfLeads`). **Kohorta**: davrda *kelgan* lidlar (lid filialining kalendar kuni bo'yicha), hozir qayerga yetgani. Aniqlashlar: "Bog'lanilgan" = holati `Lead`dan chiqqan **yoki** javob berilgan qo'ng'iroq (`NoAnswer` hisoblanmaydi) yoki trialga yozilgan (lidda holat tarixi yo'q); "To'lov qildi" = konvertatsiya qilingan studentda `IsVoided=false` to'lov bor. Manba normallashadi (`Trim`, katta-kichik harf — "Instagram"/"instagram " bitta; ko'rsatiladigan yozuv eng ko'p ishlatilgani); bo'sh manba "Ko'rsatilmagan". Davr standart oxirgi 90 kun, ≤ 366 kun (aks holda 400), teskari davr 400.
**Bitta formula:** `Api/Crm/LeadFunnelQueries.cs` (S05.03 da yozilgan) — `JourneysAsync` + `Stages`; filial dashboardining CRM bloki ham shundan sanaydi, shuning uchun ikki ekranda ikki xil raqam chiqmaydi (test buni tekshiradi).
**Frontend:** `/leads` da **Doska / Voronka** almashtirgichi; `components/crm/LeadFunnelView.tsx` (davr, filial va manba filtri; manbalar jadvali gorizontal skroll qilinadi, sahifa emas) va `FunnelChart.tsx` (`recharts`, ranglar dizayn tokenlari — tungi rejimda ham o'qiladi).
**Test:** `LeadFunnelTests` (8): bosqichlar va foizlar umumiy va manba bo'yicha, manba filtri (katta-kichik harfdan qat'i nazar), bekor qilingan to'lov "to'lov qildi" ga kirmaydi, davr = lid kelgan kun, **dashboard bilan bir xil son**, administrator faqat o'z filialini ko'radi (operator ham ko'ra oladi), o'qituvchi/student/anonim 403/401, 366 kundan uzun yoki teskari davr 400.

### 1.50. Email va SMS kanallari (audit M12; reja `docs/11` 6.3; `docs/21.09-audit-jurnali/S06.03`)
**Domen** (migratsiya **`AddNotificationDelivery`**): `NotificationChannel` ga `Email`, `Sms`; yangi `NotificationDelivery` (har kanal uchun alohida qator: `Status` Pending/Sent/Failed/Skipped, `Attempts`, `Error` ≤200, `SentAt`, `DedupeKey` — `(UserId, Channel, DedupeKey)` bo'yicha yagona indeks). `Notification` jadvaliga ustun qo'shilmadi.
**Arxitektura** (`Api/Notifications/Delivery/`): `INotificationSender` (`Channel`, `IsEnabled`, `SendAsync(NotificationTarget, NotificationEnvelope)` → `SendResult`), `SmtpEmailSender` (MailKit; `Notifications:Email` — Host/Port/StartTLS/User/Password/From), `SmsSender` (Eskiz.uz — token olish + yuborish, `HttpClient` timeout, xato matni 200 belgigacha; raqam 998XXXXXXXXX ko'rinishiga keltiriladi). Telegram uchun hech narsa yozilmadi (bloklangan). `NotificationDispatcher` inbox qatorini yozadi va **yoqilgan** va **sozlangan** kanal uchun `NotificationDelivery(Pending)` qo'shadi (sozlanmagan kanalga qator ham, navbat ham, SMS hisobi ham yo'q). Chaqiruvchining `SaveChanges`i qatorlarni bitta tranzaksiyada yozadi; **`NotificationDeliveryInterceptor` saqlash muvaffaqiyatli o'tgandan keyin** yetkazishni navbatga qo'yadi (saqlanmagan yozuv haqida xabar ketmaydi). `INotificationDeliveryQueue` — `HangfireDeliveryQueue` / `InlineDeliveryQueue` (testda `Automation__Enabled=false`: darhol, HTTP so'rovga xato tashlamaydi); `NotificationDeliveryJob` — 3 urinish (Hangfire'da o'sib boruvchi kechikish), keyin `Failed`; **inbox qatori har doim qoladi**. SMS **kunlik limit** `Notifications:Sms:DailyLimit` (standart 200, UTC kuni) oshsa `Skipped`; SMS standart **o'chiq**.
**Sezgir ma'lumot chiqmaydi (TZ §23):** tashqi matn — bildirishnoma sarlavhasi + "Batafsil: {App:PublicUrl}{link}"; summa, ism, ball faqat ilovada. `NotificationMessage.ExternalBody` faqat xabar qulflangan ekranda o'qilsa xavfsiz bo'lgandagina beriladi (parol tiklash kodi — yagona misol, S06.04).
**Standartlar** (`NotificationChannelDefaults`): inbox — hammasiga yoqiq; Email — `PAYMENT_DUE`, `PAYMENT_OVERDUE`, `ABSENCE_STREAK`, `EXAM_RESULT`; SMS — hech narsaga. Foydalanuvchi sozlamasi standartni bosib o'tadi. Tashkilot standartlari S07.01 da.
**Sozlamalar:** `GET /api/notifications/preferences` har tur uchun `channels { inApp, email, sms }` qaytaradi (eski `inAppEnabled` ham saqlangan); `PUT preferences/{type}` `{ inApp?, email?, sms? }` qabul qiladi (eski `inAppEnabled` ham). Kanalning shu odamga **yetib borishi** alohida — `GET /api/notifications/channels`: server kanali sozlanganmi va profilda email/telefon bormi (yo'q bo'lsa sababi bilan). `settings/notifications/page.tsx` — jadvalda **Email** va **SMS** ustunlari (yetib bo'lmasa o'chirilgan, sababi yonida; "SMS pulli bo'lishi mumkin" izohi). `appsettings.Local.example.json` ga bo'sh namuna; kalitlar commit qilinmaydi.
**Test:** `NotificationDeliveryTests` (9), `EduCenterWebApplicationFactory` da soxta `INotificationSender` (`FakeNotificationSender` — haqiqiy SMTP/SMS'ga so'rov ketmaydi): yoqilgan kanal → `Sent`; o'chiq kanalga yuborilmaydi, SMS hamma tur uchun standart o'chiq; **sender xato bersa inbox qoladi**, 3 urinishdan keyin `Failed`; sender **istisno tashlasa** ham shunday; bir xil `DedupeKey` har kanalda bir marta; SMS limit → `Skipped`; tashqi matnda summa/ism/ball yo'q; kanal sozlamasi alohida (inbox'ni o'chirish pochtani o'chirmaydi); sozlamalar ekrani kanalning yetib borishini aytadi. Mavjud bildirishnoma testlari o'zgarishsiz yashil.

### 1.51. "Parolni unutdim" — OTP (audit K8; reja `docs/11` 6.4; `docs/21.09-audit-jurnali/S06.04`)
**Domen** (migratsiya **`AddPasswordResetTokens`**): `PasswordResetToken(UserId, CodeHash, ExpiresAt, UsedAt, Attempts)` — kod parol kabi **xeshlanadi**, ochiq saqlanmaydi va hech qayerga loglanmaydi.
**Endpointlar** (`AuthController`, `[AllowAnonymous]`, `LoginBruteForce` siyosati bilan; mantiq `Api/Auth/PasswordResetService.cs` da):
- `POST /api/auth/forgot-password { identifier }` — **har doim 204**. Email bo'yicha, yoki telefon **bitta** faol hisobga tegishli bo'lsa (telefon unique emas) 6 raqamli kod (`RandomNumberGenerator`), 15 daqiqa, Email/SMS bilan yuboriladi (`ExternalBody` — kodning o'zi tashqariga chiqadigan **yagona** joy, shuning uchun in-app inbox qatori yozilmaydi). Hisob yo'q, bloklangan, soatiga 3 tadan ortiq so'rov, yoki kanal sozlanmagan bo'lsa — jim (xuddi shu ish hajmi bajariladi, vaqt farqi hisob bor-yo'qligini bildirmasin; kanal sozlanmaganda faqat logga ogohlantirish). Yangi kod eskisini bekor qiladi.
- `POST /api/auth/reset-password { identifier, code, newPassword }` — kod to'g'ri, muddati o'tmagan, urinish < 5 → `PasswordPolicy`, yangi xesh, `MustChangePassword=false`, **barcha refresh tokenlar bekor**, kod `UsedAt`, bloklash keshi (`AccountBlocker`) yangilanadi va hisobga qo'yilgan urinish qulfi olinadi. So'rov anonim, avtomatik audit esa kirgan foydalanuvchini talab qiladi — shuning uchun `AuditLog("PasswordReset")` qo'lda yoziladi (foydalanuvchining o'zi aktyor). Noto'g'ri kod `Attempts++`, 5 ta xatodan keyin to'g'ri kod ham ishlamaydi; **zaif parol kodni sarflamaydi**. Har qanday xato bir xil matn ("Kod noto'g'ri yoki muddati o'tgan").
**Frontend:** `app/(auth)/forgot-password/page.tsx` (ikki qadam: email/telefon → kod + yangi parol; matn "Agar hisob mavjud bo'lsa, kod yuborildi." — bloklangan yoki mavjud bo'lmagan hisob farqlanmaydi), loginda "Parolni unutdingizmi?" havolasi, muvaffaqiyatda `/login` ga.
**Test:** `PasswordResetTests` (10): to'g'ri oqim (kod soxta sender'dan olinadi → yangi parol → login ishlaydi, eski parol va refresh token ishlamaydi), noma'lum email 204 va hech narsa yuborilmaydi, 5 xato → kod o'ladi, muddati o'tgan, ikkinchi so'rov birinchisini bekor qiladi, bloklangan hisobga yuborilmaydi, soatiga 3 tadan ortiq → jim, telefon bilan SMS (faqat bitta hisobga tegishli bo'lsa), zaif parol kodni sarflamaydi, pochta serveri sozlanmagan → hech kim hech narsa bilmaydi.

### 1.52. E'lonlar (audit M14; reja `docs/11` 6.5; `docs/21.09-audit-jurnali/S06.05`)
**Domen** (migratsiya **`AddAnnouncements`**): `Announcement` (`BranchId?`, `GroupId?` — ikkalasi bo'sh = butun tashkilot, guruhda filial guruhdan olinadi; `Title` ≤200, `Body` ≤4000, `Audience` All/Students/Parents/Staff, `AuthorId`, `PublishedAt`, `ExpiresAt?`); `NotificationType.Announcement`. Chiqarilgach faqat matn, auditoriya va muddat o'zgaradi (qayerga qaratilgani — yo'q: odamlarga allaqachon aytilgan; o'chirib qayta chiqariladi).
**Ruxsatlar:** `announcements.view` — hamma rol (SA `ALL_ORG`, AD/OP `MY_BRANCHES`, TC `MY_GROUPS`, SD `ASSIGNED_GROUPS`, ST `SELF`, PR `MY_CHILDREN`); `announcements.manage` — SA `ALL_ORG`, AD `MY_BRANCHES` (`docs/01` §3.8 da). Administrator butun tashkilotga e'lon bera olmaydi (403), faqat o'z filiali/guruhiga.
**Ko'rinish** (`AnnouncementQueries.Visible`, rol nomisiz): auditoriya `announcements.view` **scope turidan** olinadi (`Self` → studentlar, `MyChildren` → ota-onalar, qolgani → xodimlar); student — o'z faol guruhlari/filiali/tashkilot e'lonlari, ota-ona — farzandlarining, xodim — o'z filiali; muddati o'tgani hammadan yashirin; `manage` egasi boshqa auditoriyaga qaratilganini ham ko'radi (topib tahrirlay olishi uchun). `CanManage` bayrog'i har e'lon bilan keladi (tugmalar shundan chiziladi).
**Endpointlar:** `GET /api/announcements` (sahifalangan, yangisi birinchi), `POST`, `PATCH {id}`, `DELETE {id}`; o'tgan sanani muddat qilib bo'lmaydi (400).
**Bildirishnoma:** chiqarilganda `AnnouncementNotifier` auditoriyaga **partiyalab** (har partiyadan keyin saqlanadi — katta auditoriya bitta ulkan tranzaksiya emas), `dedupeKey = "announcement:{id}"` — qayta urinish hech kimga ikki marta yubormaydi, muallifga o'z e'loni haqida xabar bormaydi. Hangfire'da fon ishi (`IAnnouncementJobQueue`), testda darhol. Email/SMS S06.03 orqali sozlamaga qarab.
**Frontend:** `/announcements` (ro'yxat + "Yangi e'lon"/tahrirlash `AnnouncementDrawer`, o'chirish `ConfirmDialog`), student/ota-ona/o'qituvchi dashboardida "So'nggi e'lonlar" (`AnnouncementsCard`, 3 ta, e'lon bo'lmasa umuman ko'rinmaydi), `lib/nav.ts` da "E'lonlar" ("Muloqot" guruhi).
**Test:** `AnnouncementTests` (9): filial e'loni o'z filialiga, guruh e'loni faqat guruhiga, auditoriya, muddati o'tgan yashirin va o'tmish muddat 400, kim chiqara oladi (teacher/student/parent/operator 403; administrator boshqa filial/guruh/butun tashkilotga 403), bildirishnoma bir marta, sahifalash, tahrirlash/o'chirish, validatsiya.

### 1.53. Universal Search: to'lov va amallar (audit M16; reja `docs/11` 6.6; `docs/21.09-audit-jurnali/S06.06`)
**`payment` turi** (`SearchController.AddPaymentsAsync`): `payments.view` **va** scope `AllOrganization`/`MyBranches` bo'lganda (o'quvchi/ota-onaning o'z to'lovlari profilida — `/finance` sahifasi bo'lmagan odamga natija boshi berk ko'cha bo'lardi), moliya ekrani bilan bir xil filial kesimi (`FinanceQueries.ScopedPaymentsAsync`). Qidiruv: **summa** ("500000", "500 000", "500,000" — bir son; `UzbekText.ParseAmount`), student ismi, izoh. Natija `"500 000 so'm — Ism"`, `"18-sentabr · Naqd"` yoki "Bekor qilingan" (bekor qilingani ham topiladi, belgilangan holda); `Href = /finance?highlight={id}`. `GET /api/finance/payments?id=` (yangi `id` parametri) — bitta to'lovni sahifadagi o'rnidan qat'i nazar olish; `finance/page.tsx` `highlight` bo'lsa "To'lovlar" tabini shu bitta to'lov bilan ochadi. `UzbekText` — pul va sana serverda ham o'zbekcha yoziladi (ICU'da uz-UZ oy nomlari yo'q, `CLAUDE.md` qoidasi 3).
**`action` turi — faqat frontend** (`CommandPalette.tsx`): ruxsat bilan shartlangan `ACTIONS`: "Yangi student" (`students.create`), "Yangi lid" (`leads.create`), "Dars qo'shish" (`schedule.edit`), "Yangi guruh" (`groups.create`) — `?new=1` bilan o'sha sahifaning yaratish oynasini ochadi; "To'lov qabul qilish" (`payments.create`) `/finance` ga olib boradi. `?new=1` konventsiyasi — `lib/use-new-param.ts`: sahifa manzilda so'ralganda o'z yaratish oynasini ochadi, yopilganda manzildan olib tashlaydi (yangi backend kerak bo'lmadi; shu konventsiyadan S06.07 dagi "Shikoyat yuborish" ham foydalanadi). Tur yorlig'i "To'lov" / "Amal".
**Test:** `SearchTests` 7 → **11** (+4): summa bo'yicha (bo'shliqsiz va bo'shliqli), ism, izoh; administrator boshqa filial to'lovini topmaydi, operator/teacher/student/parent uchun `payment` turi umuman yo'q; bekor qilingan to'lov belgilangan holda; `?id=` bitta to'lovni qaytaradi.

### 1.54. Shikoyatlar oqimi (audit M17; reja `docs/11` 6.7; `docs/21.09-audit-jurnali/S06.07`)
**Domen** (migratsiya **`AddComplaints`** — faqat `complaints` jadvali): `Complaint : BranchScopedEntity` (`Modules.Crm/Domain/Complaint.cs`) — `StudentId?`, `AuthorUserId`, `Subject` ≤200, `Body` ≤4000, `Status` **New → Assigned → InProgress → Resolved → Closed** (enum tartibi = yo'l: holat faqat oldinga), `AssignedToUserId?`, `Resolution` ≤2000, `ResolvedAt`, `SatisfactionScore` 0–10, `FeedbackComment` ≤1000. Holat o'tishlari entity metodlarida (`Assign` — ochiq paytda qayta biriktirish mumkin, holat o'zgarmaydi; `Start`; `Resolve` — Assigned'dan ham; `GiveFeedback`; `Close`). `AuditedTypes` ga qo'shildi (kim oldi, yechimga nima yozdi, qanday baholandi). **`SupportCase` bilan aralashmagan** — u o'qishda qiynalayotgan student signali.
**Ruxsatlar** (`docs/01` §3.9 dagi eski, kodga tushmagan `complaint.view/resolve` o'rniga): `complaints.create` — ST `SELF`, PR `MY_CHILDREN`, TC `MY_GROUPS`, OP/AD `MY_BRANCHES` (Super Admin o'zi shikoyat yozmaydi — u butun doskani boshqaradi); `complaints.view` — SA `ALL_ORG`, AD/OP `MY_BRANCHES`; `complaints.manage` — SA `ALL_ORG`, AD `MY_BRANCHES`. **Muallif o'zinikini `view`siz ham har doim ko'radi** (student/ota-ona/o'qituvchida `complaints.view` yo'q), shuning uchun ro'yxat va `GET {id}` bitta policy'ga emas, `PermissionResolver` orqali "view yoki create" ga tayanadi (`ComplaintQueries.Visible`: o'zi yozgan ∪ o'ziga biriktirilgan ∪ view scope'idagi filiallar). Ikkalasi ham yo'q (masalan support teacher) → 403.
**Endpointlar** (`Api/Complaints/ComplaintsController.cs`): `GET /api/complaints` (`statuses[]`, `search` — mavzu/matn/muallif/student ismi, sahifalangan), `GET {id}` (bildirishnoma havolasi sahifalangan ro'yxatning birinchi sahifasida bo'lmasligi mumkin), `POST` (`{ subject, body, studentId?, branchId? }`), `GET {id}/candidates`, `POST {id}/assign { userId }`, `POST {id}/status { status }`, `POST {id}/resolve { resolution }` (majburiy), `POST {id}/feedback { score, comment? }` (faqat muallif, faqat `Resolved` da).
- **Filial/student aniqlash** (`ResolveTargetAsync`): student o'zi haqida (studentId o'zi topiladi), ota-ona farzandi haqida (bitta bo'lsa o'zi, bir nechta bo'lsa tanlaydi — 400), xodim o'ziga ochiq studentni nomlaydi (`CanAccessStudentAsync`); filial — studentning faol guruhi filiali (bir nechta bo'lsa `branchId` ko'rsatiladi), studentsiz shikoyatda — foydalanuvchining o'z filiallaridan (ko'p bo'lsa `branchId` majburiy). Administrator faqat o'z filialiga yoza oladi.
- **Kim biriktiriladi** (`CandidatesAsync`, rol nomisiz): `complaints.view` beradigan rolga ega, faol, va shu filialga ta'sir qiladigan xodimlar — har biri **haqiqiy scope'i bilan** (`PermissionResolver`, override hisobga olinadi) tekshiriladi. Tashqaridagini biriktirish 400.
- **Kim harakat qiladi:** `assign`/`candidates` — `complaints.manage` (o'z filiali, boshqasi 403); `status` (`InProgress`) va `resolve` — `manage` egasi **yoki shikoyat biriktirilgan xodim** (masalan operator: `manage`siz, lekin unga berilganini yecha oladi, boshqasiga tegolmaydi); baho qo'ymay yopish (`Closed`) — faqat `manage`. `status` faqat oldinga va bir qadamda: orqaga/bir xil holat 409, `Assigned` (xodimsiz) va `Resolved` (yechimsiz) 400, biriktirilmagan shikoyatni boshlash 409.
- **Bildirishnoma** (`NotificationType.ComplaintCreated/Assigned/Resolved`, katalogda): yangi shikoyat → filial administratorlariga (muallifning o'ziga emas), biriktirilganda ijrochiga (o'ziga biriktirsa — yo'q), yechilganda muallifga ("baho kerak"); havola `/complaints?highlight={id}`. Sarlavhalar umumiy ("Yangi shikoyat"), mavzu faqat inbox matnida — Email yoqilsa ham tashqariga faqat sarlavha + havola chiqadi.
- **Timeline:** `studentId` bor bo'lsa `COMPLAINT_CREATED`, `COMPLAINT_RESOLVED` (payload'da faqat `complaintId`, matn yo'q); `EVENT_CONFIG` ga qo'shildi. ⚠️ **Maxfiylik:** student timeline'ini o'qituvchi, student va ota-ona ham o'qiydi (`students.view`) — shikoyat esa ko'pincha ulardan biri haqida bo'ladi. Shuning uchun `GET /api/students/{id}/timeline` bu ikki hodisani **faqat `complaints.view` egasiga** ko'rsatadi (`TimelineEventType.ComplaintEvents`; `StudentsController` ga `PermissionResolver` qo'shildi).
**Frontend:** `/complaints` — bitta ekran ikki tomon uchun: `complaints.view` egasi "Shikoyatlar" (tab'lar **Yangi / Jarayonda / Yopilgan** sonlari bilan, qidiruv, sahifalash; birinchi bo'sh bo'lmagan tab ochiladi), boshqalar "Mening shikoyatlarim" (o'zinikilar). `ComplaintDrawer` — tafsilot va **serverdan kelgan bayroqlar** (`canManage`, `canAct`, `canRate`) bo'yicha chiziladigan amallar: biriktirish (ro'yxat `candidates` dan), "Jarayonga olish", yechim yozish, "Baho kutmasdan yopish", muallif uchun 0–10 baho (`radiogroup`) + izoh; `?highlight=<id>` (bildirishnoma havolasi) shu shikoyatni to'g'ridan-to'g'ri ochadi. `ComplaintCreateDrawer` — ota-onada farzand tanlovi (bir nechta bo'lsa), o'qituvchi/xodimda filial tanlovi (bir nechta bo'lsa) — bittadan ortiq bo'lmasa so'ralmaydi. Kirish joylari: **dashboard**'larda (student/ota-ona/o'qituvchi) "Shikoyat yuborish" (`/complaints?new=1` — forma ochiq), **student profilida** "Shikoyat yozish" (xodim mijoz nomidan, student va filial tayyor), `lib/nav.ts` "Muloqot" guruhida "Shikoyatlar" (SA/AD/OP/TC/ST/PR; support teacher va developer'da yo'q). `buttonVariants` eksport qilindi (dashboard havolasi tugma ko'rinishida).
**Test:** `ComplaintTests` (10): ota-ona shikoyati → o'z filiali administratori ko'radi, boshqa filial administratori ko'rmaydi (ro'yxatda ham, id bo'yicha 404 ham), muallif faqat o'zinikini ko'radi va xodim mijoz nomidan yozadi, biriktirish (nomzodlar, tashqi filial xodimi 400, ijrochiga xabar, qayta biriktirish), holat orqaga qaytmaydi (409/400 matritsasi), yechim majburiy va biriktirilgan xodim yecha oladi/boshqasi 403 va muallifga xabar, baho (muallif, faqat `Resolved` da 409, 0–10 dan tashqari 400, nol ham baho), ruxsatlar (teacher/student/parent `manage` 403, boshqa filial 404/403, support teacher va anonim), validatsiya va joylashuv, sahifalash/status/qidiruv, timeline hodisalari faqat xodimga ko'rinadi.
**Brauzerda tekshirildi** (Playwright, dev baza, 1280 va 390 px): ota-ona dashboarddagi tugma → forma (`?new=1`) → yuborish → administrator ro'yxatda ko'radi → xodimga biriktirish → "Jarayonga olish" → yechim → ota-onada "Baho kutilmoqda" → 8/10 → "Yopilgan"; 390 px da sahifa gorizontal skroll qilmaydi, konsol xatolarisiz. Sinov qatorlari (`Sinov shikoyat …` — 1 shikoyat, 3 bildirishnoma, 2 timeline hodisasi) **aniq shu qatorlarni tanlab o'chirildi**, dev bazada qoldiq yo'q (faqat `audit_logs` dagi yozuvlar qoldi — jurnal qo'shimcha-faqat).

**S06.01–S06.07 yakuni:** integratsion test soni **488, hammasi yashil** (`ComplaintTests` +10 shu qadamda; S06.01–S06.06 kodi va testlari sessiyadan oldin yozilgan edi, bu sessiyada `build`, to'liq `test`, `tsc`, `eslint` bilan tekshirildi). Migratsiyalar: `AddLeadPreferences`, `AddNotificationDelivery`, `AddPasswordResetTokens`, `AddAnnouncements`, `AddComplaints` (dev bazaga `update-db` bilan qo'llangan). Yangi ruxsatlar: `announcements.view/manage`, `complaints.create/view/manage` (`docs/01` da). **Ochiq qolganlar:** S06.01–S06.06 brauzerda bu sessiyada qayta ko'rilmadi (faqat testlar va tip tekshiruvi); Email/SMS haqiqiy provayderlarga ulanmagan (kalitlar `appsettings.Local.json` da kutilmoqda — testda soxta sender); Eskiz faqat tasdiqlangan shablon matnini yetkazadi ("sarlavha + havola" matnini ular bilan ro'yxatdan o'tkazish kerak); shikoyat bahosi (0–10) S10.02 (NPS) bilan bog'lanmagan; prod nusxa yangilanmagan (`deploy.sh` migratsiya va ruxsatlarni o'zi qo'llaydi).

### 1.55. Tashkilot sozlamalari (audit M13; reja `docs/11` 7.1; `docs/21.09-audit-jurnali/S07.01`)
**Saqlash** (migratsiya **`AddOrganizationSettings`** — faqat `organizations.SettingsJson`): `Organization.SettingsJson` (valyuta, ish vaqti) va mavjud, hech qayerda ishlatilmagan `BrandingConfigJson` (brend rangi, logo kaliti). Naqsh `AiLimits` bilan bir xil: `Api/Organizations/OrganizationSettings.cs` — `OrganizationSettingsCatalog` (standartlar, ruxsat etilgan qiymatlar: `UZS/USD/EUR/RUB`, ish vaqti `HH:mm`, `end > start`) va `Resolve(json)` (qo'lda buzilgan qator sahifani buzmaydi — standartga qaytadi).
**Endpointlar** (`OrganizationsController`): `GET /api/organizations/current` (`[Authorize]` — student/ota-ona ham; faqat ommaviy maydonlar: `id, name, currency, workdayStart/End, brandColor, logoVersion` — slug, filial soni, AI limit **yo'q**), `PATCH {id}` (`org.settings.edit`, faqat o'z tashkiloti — boshqasi 403; berilmagan maydon o'zgarmaydi, `brandColor: ""` — standart rang), `POST/DELETE {id}/logo`, `GET {id}/logo` (bearer, `private, max-age=3600`, `nosniff`). **Brend rangi kontrast qoidasi:** oq matn 4.5:1 dan yomon bo'lsa 400 (sariq/oq rang tugma matnini o'qib bo'lmas qiladi). Logo: png/jpg/webp ≤ 1 MB, **SVG yo'q** (`OrganizationLogoStorage`, `App_Data/org-logo`, tasodifiy kalit; yuklash xato bo'lsa fayl o'chiriladi, almashtirilganda eskisi o'chadi).
**Frontend:** `settings/organization` (nom, valyuta, ish vaqti, `<input type=color>` + HEX + jonli kontrast + "Tugma namunasi", logo), nav "Tashkilot" guruhida (faqat SA). **Brend rangi mexanizmi** — `globals.css` dagi `--color-brand-*` endi `var(--org-l-*, <standart teal>)` (kun) / `var(--org-d-*, …)` (tun); `lib/brand.ts` bitta HEX dan 10 pog'onali rampni (kun va tun, tunda teskari) hisoblab `<html>` ga qo'yadi, `--org-*` olib tashlansa asl teal qaytadi. Oldingi tashrifdagi rang `lib/brand-init.ts` (head'dagi inline skript, `THEME_INIT_SCRIPT` yonida) bilan birinchi chizishdan oldin qo'llanadi — yashil miltillash yo'q. CSP tufayli `<style>` emas, `style.setProperty` (CSSOM) ishlatildi. **Logo va login:** logo bearer bilan olinadi, shuning uchun kirmagan foydalanuvchiga uni faqat brauzer eslab qolgan nusxadan (`localStorage`, data URL ≤ ~300 KB) ko'rsatish mumkin — birinchi marta kelgan qurilma standart belgini ko'radi (anonim endpoint qo'shilmadi: qaysi tashkilot ekanini kirishdan oldin aniqlab bo'lmaydi — S10.09). Sidebar (`(admin)/layout.tsx`) va login `BrandMark` ni ishlatadi. **Valyuta:** `formatMoney` standart valyutasi endi `lib/currency.ts` do'konidan (tashkilotniki); u o'zgarsa shell sahifani qayta mount qiladi. Invoice o'z valyutasini olib yuradi (`Invoice.Currency`) — eskilari o'zgarmaydi. Ish vaqti: "Yangi dars" formasi boshlanishni ertaga shu soatga qo'yadi.
**Test:** `OrganizationSettingsTests` (7): SA o'zgartiradi, hamma o'qiydi (trim, kichik harf HEX, qisman o'zgarish, bo'sh rang → standart); faqat SA (admin/operator/teacher/student/developer 403, anonim 401); noto'g'ri HEX/valyuta/vaqt/nom va **juda och rang** → 400 va hech narsa o'zgarmaydi; logo (yuklash/olish/almashtirish/o'chirish, `.svg`, >1 MB, bo'sh, `nosniff`, kesh); student faqat ommaviy maydonlarni ko'radi; boshqa tashkilot id → 403/404; buzilgan JSON → standart.

### 1.56. Ruxsatlar matritsasi muharriri (audit M13; reja `docs/11` 7.2; `docs/21.09-audit-jurnali/S07.02`)
⚠️ **`docs/11` 7.2 dagi taxmin tuzatildi:** "`PermissionResolver` override'ni allaqachon o'qiydi" faqat **foydalanuvchi** darajasi (`user_permissions`) uchun to'g'ri edi. Rol darajasidagi grantlarni (`role_permissions`) `DbSeeder` har startda kod defoltlari bilan **qayta yozadi**, shuning uchun ularni to'g'ridan-to'g'ri tahrirlash keyingi startda bekor bo'lardi.
**Dizayn — kod defoltlari ustiga qatlam** (migratsiya **`AddRolePermissionOverrides`**): `role_permission_overrides(OrganizationId, RoleId, PermissionId, RoleCode, PermissionCode, ScopeOverride?, Revoked, UpdatedBy, UpdatedAt)`, `(OrganizationId, RoleId, PermissionId)` unique. `PermissionSeedData`/`DbSeeder` **o'zgarmadi**. Hujjatdan **ikki chetlanish:** (1) jadvalda `OrganizationId` bor — `roles` global, ko'p tenantli (S10.09) rejimda bir markaz o'zgarishi boshqasiga tegmasin; (2) `RoleCode`/`PermissionCode` ham saqlanadi — audit qatori ikki GUID emas, "OPERATOR · finance.debt.view" deb o'qiladi. Hal qilish tartibi (`PermissionResolver`): **foydalanuvchi override > rol override > kod defolti**; `Revoked` — ruxsat yo'q (`ScopeType.None` ishlatilmaydi: resolver uni `Succeed` qiladi); override'da defoltda yo'q ruxsat **berish** ham mumkin. Kesh yo'q — o'zgarish keyingi so'rovdayoq kuchga kiradi. `ChatAccess.ColleaguesAsync` va `ComplaintsController.CandidatesAsync` `role_permissions` ni to'g'ridan-to'g'ri o'qir edi — endi `PermissionResolver.RolesHolding` (defolt − olib tashlangan + qo'shilgan) orqali matritsaga bo'ysunadi.
**Endpointlar** (`Api/Authorization/PermissionMatrixController.cs`, `permission.policy.edit`, faqat `AllOrganization`): `GET /api/permissions/matrix` (kod × 8 rol: defolt, override, samarali scope, ruxsat etilgan scope'lar), `PUT /api/permissions/overrides {roleCode, permissionCode, scope?, revoked}` (upsert; `scope=null && !revoked` yoki defoltga teng scope = override'ni o'chirish), `DELETE /api/permissions/overrides/{id}`. **Xavfsizlik qoidalari:** SA o'zining `permission.policy.edit/users.view/users.deactivate/role.assign` katagini olib tashlay olmaydi **va toraytira olmaydi** (409 — tizimni ichidan qulflash); `permission.policy.edit` boshqa rolga berilmaydi (400); Developer'ga moliya/hisoblar/ruxsatlar tizimi resurslari berilmaydi (400 — `DataAccessRequest.GrantableCodes` chegarasi); scope faqat shu ruxsatning kod defoltlarida uchraydigan scope'lardan (`payments.create` ga `Self` — 400), `None` — 400; olib tashlanadigan defolti yo'q katak — 400. `RolePermissionOverride` `AuditedTypes` da.
**Frontend:** `settings/permissions` — resurs bo'yicha yig'iladigan jadval (qidiruv yoki "Faqat o'zgartirilganlar" bo'lsa hammasi ochiq), katak = samarali scope `Badge`, o'zgartirilgani sariq halqa, bosilsa `Drawer` (scope tanlash / "Ruxsatni olib tashlash" / "Standartga qaytarish"); katak javobi keshga joyida yoziladi. `usePermissions` keshi 5 daqiqa — tugmalar ro'yxati shunda yangilanadi, server esa darhol tekshiradi (drawerda shu aytilgan).
**Test:** `PermissionMatrixTests` (8): override scope'ni o'zgartiradi va **shu tokenning keyingi so'rovidayoq** kuchga kiradi, o'chirilsa defolt qaytadi; `Revoked` → endpoint 403 va `me/permissions` dan yo'qoladi, bo'sh katakka scope berilsa ruxsat paydo bo'ladi; **`DbSeeder.SeedReferenceDataAsync` qayta ishlasa override saqlanadi**; SA o'zini qulflay olmaydi (4 kod + toraytirish → 409); faqat SA (5 rol 403, anonim 401); noto'g'ri tahrirlar (Developer+moliya, scope mantiqsiz, `None`, revoked+scope, yo'q rol/ruxsat); matritsa 8 rol × barcha ruxsat; audit (Added → Modified → Deleted, kodlar bilan).

### 1.57. Xona (Room) entity'si (`docs/09` 1.10 "to'liq Room — keyingi bosqich"; reja `docs/11` 7.3; `docs/21.09-audit-jurnali/S07.03`)
**Domen** (migratsiya **`AddRooms`**): `Room : BranchScopedEntity` (`Name` ≤ **50** — `lessons.RoomName` shu uzunlikda, ko'zgu bo'lib qoladi; hujjatdagi 100 emas), `Capacity?` (1–1000), `Equipment?` ≤ 500, `IsActive`; `(BranchId, Name)` unique + controllerda **kichik-katta harfsiz** tekshiruv ("A-12" = "a-12"). `Lesson.RoomId?` (FK `Restrict`); **`RoomName` qoladi** — xonaning nomi bo'lib (xona qayta nomlansa `ExecuteUpdate` bilan darslardagi nom ham yangilanadi). **Ma'lumot ko'chirish** migratsiya ichida (raw SQL): har filialdagi noyob `RoomName` (kichik-katta harfsiz, `DISTINCT ON`) → `rooms`, so'ng darslar `RoomId` ga bog'lanadi — **dev bazada tekshirildi: 521 darsning hammasi 10 xonaga bog'landi**. Yangi seed uchun `DbSeeder.BackfillRoomsAsync` (idempotent; `SeedAsync` va `DemoDataSeeder` oxirida) xuddi shuni qiladi.
**Konflikt** (`ScheduleConflicts`): `RoomId` bo'yicha; **`RoomId == null` eski dars — nom bo'yicha** (xonadagi dars eski nomli dars bilan ham to'qnashadi); nomsiz-idsiz kandidat — nom bo'yicha. Nofaol xona **yangi** darsga tanlanmaydi (400), lekin unda turgan darsni tahrirlash (mavzu, vaqt) mumkin. `LessonCandidate/CreateLesson/PATCH lesson` `roomId` qabul qiladi (`roomName` alohida ham ishlaydi — orqaga moslik), `LessonDto` `roomId` + `warnings[]` qaytaradi: xona sig'imi guruh sig'imidan kichik bo'lsa **ogohlantirish** (xato emas).
**Endpointlar** (`Api/Academic/RoomsController.cs`): `GET /api/rooms?branchId&includeInactive&search` (`schedule.view`, sahifalangan), `POST/PATCH/DELETE` (**yangi `rooms.manage`**: SA `AllOrganization`, AD `MyBranches` — `docs/01` ga qo'shildi; `PATCH` da `capacity: 0` — tozalash), **darsi bor xonani o'chirib bo'lmaydi** (bekor qilingan dars ham, guruh shablonida ishlatilayotgani ham — 409 "nofaol qiling"), `GET {id}/availability?date=` (filial kalendari kuni bo'yicha band oraliqlar: dars, guruh, o'qituvchi; bekor qilingan dars band qilmaydi). Eski `GET /api/schedule/rooms` saqlandi (mavjud test va eski mijozlar), frontend yangisiga o'tdi.
**Frontend:** `/rooms` (jadval: nom, filial, sig'im, jihoz, darslar, holat; qidiruv, filial filtri, nofaollarni ko'rsatish; drawer: forma + "Band vaqtlar" kun tanlab; faollashtirish/nofaol qilish; o'chirish `ConfirmDialog` da serverning 409 matni bilan), nav "Ta'lim" guruhida (SA/AD/OP; tahrir tugmalari `can("rooms.manage")`). `schedule/page.tsx` va `LessonActions` dagi `<datalist>` o'rniga `RoomSelect` (sig'imi bilan; eski nomli darsda "xonalar ro'yxatida yo'q" varianti saqlanadi), ogohlantirishlar ko'rsatiladi.
**Test:** `RoomTests` (10): CRUD va ruxsatlar (AD o'z filialida, boshqa filial/teacher/operator/student/parent 403); validatsiya va nom takrori (kichik-katta harfsiz); **xona bo'yicha konflikt** (+ `conflicts` preview); dars xona nomini va id'sini oladi, xona qayta nomlansa darslar ham; nofaol xona; darsi bor xonani o'chirish 409; sig'im ogohlantirishi; availability (kun chegarasi filial vaqtida, bekor qilingan dars yo'q); **eski nomli dars xona bilan to'qnashadi va `BackfillRoomsAsync` unga xona beradi**; boshqa filial xonasi.

### 1.58. Guruh haftalik jadval shabloni (reja `docs/11` 7.4; `docs/21.09-audit-jurnali/S07.04`)
**Domen** (migratsiya **`AddGroupScheduleSlots`**): `GroupScheduleSlot(GroupId, DayOfWeek, StartTime: TimeOnly, DurationMinutes, RoomId?, TeacherId?)` (`TeacherId` bo'sh — guruhning asosiy o'qituvchisi). Vaqt **filial mahalliy soati**da yoziladi; `BranchClock.LocalToInstant` uni UTC'ga o'giradi (darslar instant sifatida saqlanadi — `Asia/Tashkent` da 18:00 → 13:00Z).
**Endpointlar** (`Api/Academic/GroupScheduleController.cs`): `GET/PUT /api/groups/{id}/schedule-template` (`groups.view` / `groups.edit`; PUT to'liq almashtiradi; bir kunda slotlar to'qnashmaydi, yarim tundan oshmaydi, davomiylik 15–360, ≤ 21 slot; xona shu filialniki va faol, o'qituvchi faol), `POST /api/groups/{id}/generate-lessons {from,to,skipDates}` (`schedule.edit`, `?preview=true`). **Hammasi yoki hech narsa:** avval har kandidat `ScheduleConflicts.FindAsync` bilan tekshiriladi — bittasi to'qnashsa 409 `{conflicts:[{date,type,…}]}` va **hech narsa yaratilmaydi**; preview 200 da ro'yxat + to'qnashuvlarni beradi. Davr ≤ 6 oy, o'tgan sanaga yo'q, yopiq guruh 409, shablonsiz / xonasiz slot / asosiy o'qituvchisiz va o'qituvchi ko'rsatilmagan slot 400. **Bir xil vaqtli dars (bekor qilingani ham) `skipped`** — takror yaratilmaydi (bekor qilingan dars qayta tirilmaydi). Bir tranzaksiya, **bitta** `ScheduleChanged` bildirishnoma. Sig'im ogohlantirishi `warnings` da.
**S06.01 bilan bog'lanish:** guruh shablonli bo'lsa "qulay vaqt" omili shablon slotlariga qaraydi (dars bo'lmasa ham ishlaydi); shablonsiz — avvalgidek `NextLessons`. `GroupOverviewDto` ga `BranchId` qo'shildi.
**Frontend:** `groups/[id]` — "Umumiy" va "Haftalik jadval" tablari (jadval tabi faqat `groups.edit`/`schedule.edit` egasiga; `Tabs` bitta tab bo'lsa sarlavhasiz chizadi): Du–Ya grid (mobilda bo'sh kunlar yashirin), slot modali (kun, vaqt, davomiylik, xona, o'qituvchi), saqlanmagan o'zgarish ko'rsatkichi; pastda "Darslarni yaratish": davr + tayyor tugmalar (4 hafta / 3 oy), bayram kunlari, **Oldindan ko'rish** (to'qnashgan kunlar ajratiladi) → tasdiq. Jadval saqlanmaguncha yaratish o'chiq.
**Test:** `GroupScheduleTests` (9): 3 slot → 4 haftada 12 dars, vaqtlar filial soatida (13:00Z / 04:30Z); bitta konflikt → 409 va **8 tasidan birortasi ham yaratilmagan**, preview, `skipDates`; ikkinchi chaqiruv `skipped` (bekor qilingan ham); bitta xabar; davr/holat 400 lari; shablon validatsiyasi va almashtirish, shablondagi xonani o'chirib bo'lmaydi; yopiq guruh, teacher/operator/student 403; asosiy o'qituvchisiz guruh; **tavsiya shablonga qaraydi**.

### 1.59. To'lov jadvali — bo'lib to'lash (reja `docs/11` 7.5; `docs/21.09-audit-jurnali/S07.05`)
**Domen** (migratsiya **`AddInvoiceInstallments`**): `Invoice.ParentInvoiceId?`, `InstallmentNumber?`, `InstallmentCount?`; `InvoiceStatus.Split` (matn sifatida saqlanadi). `Invoice.SplitInto` — asos invoice `Pending` va `PaidAmount == 0`, qismlar ≥ 2 (API ≤ 12, ekran 2–6), `Σ == NetAmount` **tiyingacha**, muddatlar o'sib boradi va `≥ PeriodStart`; qismlar asosning talaba/guruh/**davrini** oladi, `Description = "… (2/3)"`; qismni qayta bo'lib bo'lmaydi. ⚠️ **Noyob indeks o'zgardi:** `(EnrollmentId, PeriodStart)` endi `WHERE ParentInvoiceId IS NULL` filtrli — qismlar asosning davrini baham ko'radi, billing'ning bir davr = bitta invoice kafolati (parallel yaratishdan himoya) esa saqlanib qoldi.
**Endpoint:** `POST /api/finance/invoices/{id}/split` (`payments.edit` — SA/AD, boshqa filial 403; operator/teacher 403): 409 (qisman to'langan / to'langan / bekor qilingan / allaqachon bo'lingan / qismni bo'lish), 400 (yig'indi, tartib, sana, tiyin). Asosga to'lov (409 "qismlariga to'lang"), chegirma (409), bekor qilish (409) yo'q; qismlar oddiy invoice — alohida to'lanadi, chegirma oladi, bekor qilinadi. Timeline `INVOICE_SPLIT`, audit avtomatik.
**Ikki marta sanash — majburiy tekshiruv** (har bir `Invoice` so'rovi ko'rib chiqildi): `Pending/PartiallyPaid` bilan filtrlaydiganlar (qarzdorlar, `overview`, `OpenOverdue`, eslatmalar, `SyncDebtStatus`, student `finance`) `Split` ni o'zi chiqarib tashlaydi; **`!= Cancelled` bilan filtrlaydiganlarda ochiq qo'shildi** (`&& != Split`): `FinanceReportsController.Summary` ("billed" endi qismlarni sanaydi, asosni emas), `DashboardController` filial moliyasi va student "to'lanadigan summa". `BillingRules` asosni "davr chiqarilgan" deb ko'radi (ikkinchi invoice yaratilmaydi), har qism o'z `DueDate`iga qarab eslatma oladi va o'z muddati o'tgach qarzga aylanadi.
**Frontend** (`components/Finance.tsx`): invoice qatorida "Bo'lish" (`can("payments.edit")` va to'lanmagan) → drawer: qismlar soni 2–6, teng bo'lish (oxirgi qism farqni oladi), har qism summa va sana (oyma-oy), jonli yig'indi/xato; jadvalda asos "Qismlarga bo'lingan" (qoldiq "—"), qismlar uning ostida "2/3" belgisi bilan; `INVOICE_SPLIT` timeline'da.
**Test:** `InvoiceSplitTests` (8): 600 000 → 3 qism, yig'indi aniq, asos `Split`, davr/tavsif; 11 xil noto'g'ri qism (400, hech narsa yaratilmaydi); faqat to'lanmagan invoice (qisman/to'liq to'langan/bekor qilingan/ikki marta/qismni bo'lish → 409); qismlar oddiy invoice, asos to'lov/chegirma/bekor qilishni qabul qilmaydi; **hech bir raqam ikki marta sanalmaydi** (hisobot `Billed/Expected/Outstanding` bo'linishdan oldin va keyin bir xil, `overview.OpenInvoices` +1, student dashboardi va `finance.TotalBalance` 600 000, qism to'langanda hammasi aynan shuncha kamayadi); `BillingRules` ikkinchi invoice yaratmaydi va qarz vazifasi faqat muddati o'tgan qismni aytadi; har qism o'z sanasida o'z summasi bilan eslatma oladi; ruxsatlar va boshqa filial.

**S07.01–S07.05 yakuni:** integratsion test soni **530, hammasi yashil** (yangi: `OrganizationSettingsTests` 7, `PermissionMatrixTests` 8, `RoomTests` 10, `GroupScheduleTests` 9, `InvoiceSplitTests` 8), `tsc` va `eslint` toza. Migratsiyalar (dev bazaga qo'llandi): `AddOrganizationSettings`, `AddRolePermissionOverrides`, `AddRooms` (+ ma'lumot ko'chirish), `AddGroupScheduleSlots`, `AddInvoiceInstallments`. **Brauzerda tekshirildi** (Playwright, dev baza, 1280 px): SA brend rangi va logo yuklaydi → sidebar va **kirish sahifasi** (eslab qolingan nusxadan) yangi rangda; ruxsatlar matritsasi va katak drawer'i; `/rooms`; guruh "Haftalik jadval" tabi (slot qo'shish → saqlash → oldindan ko'rish → olib tashlash); moliyada "Bo'lish" drawer'i (3 qism, yig'indi 550 000 = 550 000). Sinov o'zgarishlari qaytarildi (tashkilot nomi/rangi/logosi, guruh shabloni); dev bazada faqat audit yozuvlari qoldi. `backend/scripts/dotnet-in-docker.sh test` endi `TEST_FILTER="FullyQualifiedName~RoomTests"` bilan tanlangan testlarni ishga tushiradi.

### 1.60. Speaking: TTS — imtihonchi savolining ovozi (audit M15; reja `docs/11` 8.1; `docs/21.09-audit-jurnali/S08.01`)
**Arxitektura (TZ §34 `ITTSProvider`):** `ITextToSpeechProvider.SynthesizeAsync(text, voice, language)` → `SpeechResult(Audio, ContentType, Voice)`; `AiProviderType.OpenAiCompatibleTts` (`POST {BaseUrl}/audio/speech`, `response_format: mp3`; `BaseUrl` chat provayderidagidek `/v1` bilan tugaydi); `FakeTextToSpeechProvider` (test: soniya uzunligi matnga bog'liq haqiqiy kichik WAV; `AlwaysFail` rejimini ham tushunadi). `AiProviderRegistry.SynthesisRoute()` + `AiGateway.SynthesizeAsync` — `TranscribeAsync` bilan bir xil qatlamlar: kunlik Speaking limiti, fallback zanjiri, `AiRequest`/`AiUsage` jurnali. **Jurnalga ovoz hajmi emas, belgi soni yoziladi:** yangi `AiRequest.Characters` (migratsiya **`AddAiRequestCharacters`**) — `InputTokens` ga aralashtirilmadi (token yig'indilari modellar haqida qolsin). Narx: `Models` da TTS modeli uchun `InputPricePerMillion` = 1 mln **belgi** narxi.
**Sozlama — standart o'chiq:** `Ai:Providers:tts` (`Type`, `BaseUrl`, `SpeechModel` — standart `tts-1`, `SpeechVoice` — standart `alloy`, `Enabled=false`) va marshrut **`SpeechSynthesis`**. ⚠️ Boshqa vazifalardan farqli bu marshrut **`Default` ga qaytmaydi** (u yerda chat modellari): ovoz markaz uni nomlab yoqadigan narsa; marshrut bo'sh bo'lsa savol matn bo'lib qoladi (xato emas). Dev'da `appsettings.Local.json` da `tts` ni `Enabled: true, BaseUrl: http://educenter-tts:8000/v1` qiling (`appsettings.Local.example.json` da tayyor). `docker-compose.yml`: `tts` servisi — `ghcr.io/matatonic/openedai-speech-min` (Piper, CPU; OpenAI `/v1/audio/speech` protokoli), `container_name: educenter-tts`, host porti **18000** (9000 — boshqa loyihaning MinIO'si; rasm nomi va tegi `docker manifest inspect` bilan tekshirildi). Pullik provayderga o'tish — faqat `BaseUrl` + kalit (kalit faqat `appsettings.Local.json` yoki muhit o'zgaruvchisida).
**Endpoint:** `GET /api/ai/speaking/sessions/{id}/turns/{turnId}/question-audio` (`ai.speaking.use`, faqat sessiya egasi, `ai` rate-limit): **kesh** — `SpeakingAudioStorage` da `q-<sha256(ovoz + savol)>.mp3|wav|ogg`, zanjirdagi har bir ovoz uchun tekshiriladi (fallback yasagan yozuv ham); kesh **sessiyaga emas**, so'z va ovozga bog'liq — boshqa talabaga o'sha savol berilsa provayder chaqirilmaydi, limit sarflanmaydi. Vaqtincha fayl nomi bilan yoziladi va `Move` qilinadi (yarim yozuv o'qilmaydi). Provayder yo'q/xato → **503 `{error}`**, limit → 429, boshqaning sessiyasi/o'z sessiyasining boshqa turni → 404, o'qituvchi → 403. `AiInputValidator` uzunlik chegarasi 1000 (= `SpeakingTurn.MaxQuestionLength`); prompt-injection tekshiruvi yo'q (ovoz ko'rsatmaga amal qilmaydi). `/api/ai/providers` ro'yxatida `SpeechSynthesis` qatori endi ovozlarni ko'rsatadi.
**Frontend** (`ai/speaking/page.tsx`, `lib/use-speaking-voice.ts`): savol yonida ▶/■ tugmasi — avval server yozuvi (turn bo'yicha bir marta olinadi va tabda saqlanadi), 503/xato bo'lsa **brauzer `speechSynthesis`** (fallback); "Savolni avtomatik o'qish" sozlamasi (`localStorage`, `useSyncExternalStore`, `try/catch`; standart o'chiq); ijro tugamaguncha **yozuv tugmasi o'chiq** (ovoz mikrofonga tushmasin); brauzer autoplay'ni rad etsa jimgina matn. `SessionDto.currentTurnId` qo'shildi.
**Test** (`SpeakingPracticeTests`, +6): savol audiosi qaytadi, ikkinchi so'rov va boshqa talabaning o'sha savoli **keshdan** (jurnalda 1 ta yozuv); egasi/o'qituvchi/boshqa sessiya; marshrutsiz host → 503 va matn ishlayveradi (`WithWebHostBuilder`); jurnal **belgilarda** (`Characters`, token/soniya 0) va limit tugaganda 429, lekin keshdagisi ochiq; `SynthesisRoute` `Default` ga qaytmaydi; `OpenAiCompatibleTtsProvider` (URL, `Bearer`, `model/voice/input`, audio bo'lmagan javob / bo'sh javob / HTTP 500 rad etiladi).

### 1.61. Speaking: Full Mock va Answer Relevance (audit M15; reja `docs/11` 8.2–8.3; `docs/21.09-audit-jurnali/S08.02`)
**Domen** (migratsiya **`AddSpeakingFullMock`**): `SpeakingMode.FullMock` (matn sifatida saqlanadi — enum o'rtasiga qo'shildi, eski qatorlarga ta'sir yo'q), `SpeakingTurn.Part` (`int?`), `SpeakingSession.RelevanceBand` (`numeric(3,1)?`), `SpeakingSession.MaxTurnsFor(mode)` (Full Mock 15, qolganlar 12; `AnswerAsync` da haqiqiy to'siq sifatida ishlatiladi). **Tuzilma:** Part 1 — 5 savol, Part 2 — bitta kartochka, Part 3 — 5 savol (`TurnsFor` = 11; 15 — yuqori chegara). `SpeakingScript.PartFor(mode, ordinal)`; `Part` yozilmagan eski turn'lar uchun `PartOf` rejim va tartibdan hisoblaydi (IeltsPart1/2/3 → 1/2/3).
**Prompt versiyalari:** `speaking.examiner.v1 → v2` (Full Mock: qismga qarab ko'rsatma; Part 2 — kartochka va "mavzu Part 1 dan boshqa"; Part 3 — **kartochka `part2_cue_card` nomli kirish sifatida beriladi**, model uni qidirib yurmaydi). Boshqa rejimlarning matni o'zgarmadi — v1 bilan boshlangan faol sessiya v2 da xuddi shunday davom etadi. `speaking.assessment.v2 → v3` (⚠️ hujjatdagi "v2" allaqachon band edi — o'zbekcha izohli versiya): `relevance {band, comment}` va Full Mock uchun `parts[]` (har qism uchun qisqa izoh). Sxema validatsiyasi: `relevance` majburiy; Full Mock'da 1–3 qismning har biriga izoh majburiy — noto'g'ri javob mavjud retry (1 marta) va `Failed` yo'liga tushadi, transkript saqlanadi. Eski (v1/v2) baholar o'qiladi: `RelevanceBand`, `relevanceComment`, `parts` null — ekran ularni ko'rsatmaydi.
**Qaror — Relevance `EstimatedBand` ga kirmaydi** (spetsifikatsiyadagi standart tavsiya): TZ/IELTS'da relevance alohida mezon emas (u Fluency & Coherence ichida), to'rtinchi mezon o'rtachani haqiqiy shkaladan uzoqlashtiradi. `RelevanceBand` alohida ko'rsatiladi ("Taxminiy bandga kiritilmaydi" izohi bilan). Fake provayder: (6.0, 6.5, 6.0) o'rtachasi 6.0; relevance 7.5 kiritilsa 6.5 bo'lardi — test shuni farqlaydi.
**Limit:** Full Mock ~14 daqiqa (`FullMockMinutes`) — kunlik Speaking limitidan shuncha qolmagan bo'lsa **boshlanmaydi**, 429 va sabab ("… bugungi limitdan N daqiqa qoldi. Qisqaroq mashqni tanlang…"), eski sessiya yopilmaydi.
**Frontend:** rejim kartasida "Full Mock (11–14 daqiqa)"; sessiyada qism ko'rsatkichi (Part 1/2/3 — o'tgani yashil, joriysi ko'k; Part 2 da "— tayyorgarlik 0:45"), Part 2 kartochkasi ostida **tayyorgarlik paneli** (1:00 taymer devor soati bo'yicha, progress, "darhol yozish" imkoni; tugagach yozuv **o'zi boshlanmaydi** — "ixtiyoriy" bandi shunday hal qilindi); natijada to'rtinchi karta "Javobning mosligi", Full Mock'da "Qismlar bo'yicha" izohlari, transkriptda "Part N" belgisi. **Yozuv fayl nomi endi blob turiga qarab** (`answer.webm|ogg|m4a|…`): S08.03 fayl mazmunini tekshirgani uchun Firefox (Ogg) va Safari (MP4) yozuvlari `.webm` deb yuborilsa rad etilardi.
**Test** (`SpeakingPracticeTests`, +6): Full Mock 11 javob — qismlar `[1×5, 2, 3×5]`, kartochka "one minute to prepare", Part 3 savoli **Part 2 mavzusini** o'z ichiga oladi, `Assessment.Parts` = 1,2,3, `MaxTurnsFor` 15/12; limit yetmasa (10/20 daqiqa ishlatilgan) boshlanmaydi va qisqa rejim ochiq; Relevance 7.5 saqlanadi va bahoga kirmaydi; `relevance`siz javob (yangi `FakeFailureMode.IncompleteAssessment`) → retry (2 urinish jurnalda) → `Failed`, transkript saqlangan; sxema validatori (relevance/parts); eski (v2) baho + turn'lari `Part`siz sessiya o'qiladi. **Pronunciation/Stress/Rhythm — S10.10** (bu qadamda qilinmadi).

### 1.62. Fayl xavfsizligi: skaner, magic bytes, yangi formatlar (audit T8; reja `docs/11` 8.4; `docs/21.09-audit-jurnali/S08.03`)
**`Api/Files/`:** `IFileScanner.ScanAsync` → `ScanResult` (`Clean` / `Infected(signature)` / `Unavailable`); `ClamAvScanner` — clamd TCP **`zINSTREAM`** (64 KB uzunlik-prefiksli bo'laklar, oxirida 0; javob `stream: OK|<imzo> FOUND`; ulanish/timeout/`ERROR`/bo'sh javob = `Unavailable`, hech qachon "toza" emas; so'rovni bekor qilish esa bekor qilish bo'lib qoladi), `NoopFileScanner` (doim `Unavailable`). **`IFileGuard.CheckAsync(stream, fileName)`** — hamma saqlagich uchun umumiy: (1) **magic bytes** (`FileSignatures`: pdf `%PDF`, png, jpg, gif, webp/wav `RIFF…`, doc/xls/ppt OLE2, docx/xlsx/pptx/zip `PK` + **`[Content_Types].xml`**, mp3 `ID3`/kadr sinxroni, m4a/mp4 `ftyp`, webm EBML, ogg, txt/md/csv — **UTF-8 matn**, NUL/boshqaruv belgisiz); signaturasi noma'lum kengaytma **hech qachon o'tmaydi**; (2) **zip-bomba** (`ZipLimits`: ≤ 1000 fayl, ochilgach ≤ 50 MB — e'lon qilingan o'lcham bo'yicha, hech narsa ochilmasdan); (3) skaner. Zararli → "Fayl xavfli deb topildi va qabul qilinmadi."; mazmun mos emas → "Fayl mazmuni kengaytmasiga mos emas."; **skaner javob bermasa va `Required`** → "Fayllarni xavfsizlik tekshiruvidan o'tkazib bo'lmadi…" (fail-closed). Rad etilgan yuklash `LogWarning` (fayl nomi bir qatorda va ≤ 100 belgi, foydalanuvchi, imzo; **mazmun yo'q**).
**Hamma saqlagich qamrab olindi (9 ta):** `Chat`, `Resource`, `StudentDocument`, `Homework`, `UserPhoto`, `OrganizationLogo`, `TeacherCredential`, `KnowledgeFile`, `SpeakingAudio` — `Validate` → **`ValidateAsync`** (o'lcham, kengaytma, keyin `guard`); 10 ta chaqiruv joyi (`await`). **Disk yozilishidan oldin** tekshiriladi — rad etilgan fayl saqlanmaydi. Test (`EveryFileStorage_ChecksWhatItStores`) `*Storage` sinflarini reflection bilan topib `ValidateAsync` va `IFileGuard` borligini tekshiradi — o'ninchi saqlagich guard'ni unutsa qizil bo'ladi.
**Bilim bazasi:** skaner "Uploaded → Processing" dan oldin **qayta** ishlaydi (imzolar yangilanadi, skanersiz kirib qolgan fayl keyingi indekslashda tutiladi): zararli → hujjat **`Quarantined`**, sabab "Fayl zararli dastur (malware) deb topildi: <imzo>", **tasdiqlab bo'lmaydi** (`approve` → 409, ekranda tugma yo'q — DTO `malware`); skaner `Unavailable`+`Required` → `Failed` (keyin qayta uriniladi). `DocumentTextExtractor`: **`.pptx`** (slaydlar matni, tartib bo'yicha — sarlavha, matn, jadval katakchalari; `.txt/.md/.csv` allaqachon bor edi) va docx/pptx uchun `ZipLimits` parslashdan oldin.
**Konfiguratsiya:** `Files:Scanner` (`Provider: none|clamav`, `Required`, `Host`, `Port`, `TimeoutSeconds`); `Required` **standart true**, `appsettings.Development.json` da `false` (dev'da skanersiz ishlayveradi). `ProductionSettingsGuard`: production'da `Required=false`, noma'lum provayder yoki `clamav` `Host`siz — **ishga tushirmaydi**; skanersiz production ishga tushadi, lekin yuklashlarni rad etadi va startupda ogohlantirish yozadi. `docker-compose.yml` (dev): `clamav` (`clamav/clamav:stable`, `educenter-clamav`, host **13310**, `clamdcheck.sh` healthcheck). **`deploy/docker-compose.yml`: `clamav` servisi va `Files__Scanner__*` qo'shildi** — ⚠️ keyingi `deploy.sh` da prod birinchi marta ~1–2 daqiqa signatura bazasini yuklaydi (~3 GB RAM chegarasi); u tayyor bo'lguncha prod yuklashlarni rad etadi.
**Mavjud testlar:** yolg'on mazmunli fixture'lar haqiqiy sarlavhalarga almashtirildi (`TestFiles`: `Png/Jpeg/Pdf/Webm/Mp3/Webp/Docx/Pptx/Archive`) — `TeacherPortfolio`, `UserPhoto`, `OrganizationSettings`, `HomeworkFiles`, `Speaking*`.
**Test** (`FileSecurityTests`, 26 holat: 13 test + 13 theory qatori; biri haqiqiy ClamAV bilan, `EDUCENTER_CLAMAV_HOST=educenter-clamav` berilsa ishlaydi, aks holda "skipped"): EICAR har yuklash yo'lida (resurs, student hujjati, chat, bilim bazasi, foto, uy vazifasi, speaking javobi) rad etiladi, **diskdagi fayllar soni o'zgarmaydi** va skaner chaqirilgan; toza pdf/png/docx/txt o'tadi; `.exe` → har yo'lda "mos emas"; bir tur boshqasiga o'tolmaydi (JPEG `.png` nomida, PDF `.docx` nomida, `[Content_Types].xml`siz zip); har turning signaturasi (theory); 1001 fayl va 60 MB ochiladigan kichik arxiv rad, 1000 fayl o'tadi; skaner `Unavailable` + `Required=true` → rad, `false` → o'tadi; **skanersiz kirgan fayl keyingi indekslashda karantinga tushadi va tasdiqlanmaydi**; `.pptx` (slayd tartibi), `.txt`, `.md` bilim bazasida `Ready`; production sozlamalari; **`ClamAvScanner` protokoli** ichki soxta clamd bilan (bo'laklash, `FOUND` imzosi, bo'sh oqim, ulanish rad, `ERROR`, jim server — timeout, chaqiruvchi bekor qilsa `OperationCanceledException`).

**S08.01–S08.03 yakuni:** integratsion test soni **568** (567 yashil + 1 ixtiyoriy: haqiqiy ClamAV bilan — u `EDUCENTER_CLAMAV_HOST=educenter-clamav bash backend/scripts/dotnet-in-docker.sh test` bilan alohida yashil o'tdi), `tsc` va `eslint` toza. Migratsiyalar (dev bazaga qo'llandi): `AddAiRequestCharacters`, `AddSpeakingFullMock`. **Haqiqiy xizmatlar bilan tekshirildi:** (1) `educenter-clamav` (`clamav/clamav:stable`, 3.6 mln imzo) — `ClamAvScanner` toza 5 MB oqim, EICAR (`Eicar-Test-Signature`) va guard orqali to'liq yo'lni to'g'ri hal qiladi; dev API'da EICAR `.txt` → 400 "Fayl xavfli deb topildi…" va logda `refused: malware Eicar-Test-Signature`, `.exe` `.pdf` nomida → "mos emas", toza fayl 201 (sinov hujjati o'chirildi). ⚠️ ClamAV **EICAR faylini** (satrning o'zi) taniydi, boshqa matn ichiga qo'yilgan satrni emas — suite'dagi `FakeFileScanner` esa istalgan joyda taniydi. (2) `educenter-tts` (`openedai-speech-min`, Piper, ~60 MB ovoz birinchi ishga tushirishda yuklandi) — `POST /v1/audio/speech` biz yuboradigan aynan shu tana bilan `200 audio/mpeg`. (3) **Brauzerda** (Playwright, Chrome, dev API, talaba): rejim kartasida "Full Mock" va "Savolni avtomatik o'qish"; Part 2 kartochkasi (haqiqiy DeepSeek yaratdi) → "Part 2 — tayyorgarlik 0:57" taymer; ▶ → `question-audio` 200 `audio/mpeg`, ijro paytida ■ tugmasi ko'rinadi va **"Yozishni boshlash" o'chiq**, tugagach yoqiladi; Full Mock → "Part 1 · Part 2 · Part 3" va "0 / 11 javob"; konsolda xato yo'q. **Dev muhit o'zgarishi:** `appsettings.Local.json` ga `tts` (yoqilgan) va `Files:Scanner` (`clamav`, dev'da `Required=false`) qo'shildi; `educenter-tts` va `educenter-clamav` konteynerlari ishga tushirilgan (`docker compose up -d clamav tts`; ~1.5 GB RAM clamd). Sinov qoldig'i: talabaning Speaking tarixida 2 ta "To'xtatilgan" mashq (Part 2 "A favourite meal", Full Mock "Hometown") — ular xodimlarga ko'rinmaydi.

### 2.1. 🔴 Telegram bot (audit §3.13, §5.4)
Backend `NotificationChannel.Telegram` bor, dispatcher tayyor.
**To'siq:** bot token yo'q. Foydalanuvchi keyinga qoldirishni so'radi (2026-09-18) —
token berilmaguncha bu band ko'tarilmaydi.

### 2.2. Teacher KPI va Feedback (audit §3.6, §5.13) — Phase 6
`/teachers/[id]/Natijadorlik` tabida Performance qismi bor (1.13 ga qarang).
Yo'q: TZ'dagi to'liq KPI modeli (Student Progress/Attendance/Homework/
Punctuality/Feedback Speed/Retention/Feedback — sozlanadigan og'irliklar bilan)
va Student/Parent Feedback yig'ish (yangi baholash formasi + entity kerak).
Ikkalasi ham kodda "Phase 6" deb belgilangan.

### 2.3. Gamification (audit §3.14) — Phase 6, rejada
### 2.4. Certificates (audit §3.15) — Phase 6, rejada
### 2.5. Flutter mobil ilova — Phase 5, rejada (`docs/07_mobile_design_brief.md` tayyor)
### 2.6. Texnik qarzlar (audit §6): S3/MinIO, Redis backplane, Redis, qayta indekslash, streaming — hammasi ochiq

---

## 3. ⚪ HISOBOTDAGI NOTO'G'RI BANDLAR

Bularni kod bilan solishtirib tekshirdim — hisobot xato yoki eskirgan:

| Band | Hisobot nima deydi | Aslida |
|---|---|---|
| §3.11 | "Xona tushunchasi bazada yo'q" | `Lesson.RoomName` bor va konflikt detektor aynan shunga tayanadi. Yo'q narsa — alohida `Room` jadvali va nomlar ro'yxati |
| §3.17 | "`/ai/writing/reviews` ichida `page.tsx` yo'q" | `c0e2c20` commitda yaratilgan, ishlaydi |
| §3.19 | "`/accounts` faqat 683 bayt — chala" | Wrapper fayl. Asosiy kod `UserDirectory.tsx` da, to'liq ishlaydi |
| §3.5 | "Freeze funksiyasi yo'q" | Domen darajasida bor edi: `EnrollmentStatus.Frozen`, `StudentStatus.Frozen`. Yo'q bo'lgani — API va UI |
| §3.4 | "Parent hech narsa ko'rmaydi" | Yarim to'g'ri: parent `MY_CHILDREN` scope bilan `/students`, `/schedule`, `/exams`, `/chat` ga kira olardi. Lekin bu xodim uchun yasalgan ekranlar edi — shuning uchun baribir to'g'rilandi |

---

## 4. KEYINGI QADAM (tavsiya qilingan tartib)

Phase 1-4 doirasidagi barcha band yopildi (2026-09-18, foydalanuvchi so'ragan
tartib bilan). Qolgani ataylab keyinga qoldirilgan:

1. **Phase 5/6** — Teacher KPI/Feedback, Gamification, Certificates, Flutter
   mobil ilova. Rejalashtirilgan, hozircha navbatda emas.
2. **Texnik qarzlar** (audit §6) — S3/MinIO, Redis backplane, qayta indekslash,
   streaming. Ishlab chiqarishga chiqishdan oldin kerak, hozircha dev muhitda
   shart emas.
3. **Telegram** — foydalanuvchi token bersa boshlanadi, hozircha kutilmoqda.

---

## 5. ESLATMALAR

- **Bilimlar bazasi:** App_Data volume tuzatilgunga qadar yuklangan hujjatlarning
  fayllari yo'qolgan; matni indeksdan ko'rinadi (1.18), faylni almashtirish uchun qayta yuklash kerak.
- **Dev bazada test qoldiqlari bor:** "BFLA Branch" (2 ta), "BFLA Course" (4 ta), "BFLA Teacher" — xavfsizlik
  testlaridan qolgan. `DemoDataSeeder` dublikat nomlarga chidamli; tozalash kerak bo'lsa dev bazani so'rab qayta yarating.
- **GitLab (`192.168.10.105`):** hali ham bloklangan. Serverda 80 va 445 portlari
  ochiq, SSH yo'q, HTTPS yo'q. Uchta yo'l: serverda SSH ochish, sertifikat qo'shish,
  yoki `git config credential.http://192.168.10.105.allowUnsafeRemotes true` ni
  o'zingiz ishga tushirish.
- **Graphify:** graf faqat koddan qurilgan (4306 tugun), jamoalar nomsiz —
  nomlash uchun LLM backend kerak.
