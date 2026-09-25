# EduCenter — Mobil ilova uchun to'liq dizayn brifi

**Hujjat turi:** dizayn topshirig'i (AI dizayn vositasiga beriladigan manba hujjat)
**Versiya:** 1.0 · 2026-09-16
**Bog'liq hujjatlar:** `00_TZ.md` (asosiy TZ), `01_role_permission_matrix.md`, `02_database_erd.md`
**Maqsad:** mavjud web platformasining mobil (Flutter, Android + iOS) versiyasi uchun ekran dizaynlarini yaratish.

> **Dizayner uchun eng muhim 3 qoida**
> 1. Backend allaqachon yozilgan va o'zgarmaydi. Quyida sanab o'tilgan **ma'lumotlar mavjud** — boshqasi yo'q. Ekranga "haftalik reyting", "sertifikat", "XP" kabi narsalarni **qo'shmang** (§12ga qarang).
> 2. Interfeys tili — **o'zbekcha**. Quyidagi barcha yorliqlar aynan shu holatda ishlatilsin. Ingliz tilidagi atamalar (IELTS, Writing, Speaking, homework) o'zbek tilida ham shu ko'rinishda qoldiriladi — markaz xodimlari va o'quvchilari shunday gapiradi.
> 3. Bu **ma'lumotga boy ish ilovasi**, o'yin emas. Gradientlar, katta illyustratsiyalar, "wow" animatsiyalar kerak emas. Aniqlik, tezlik va bir qarashda o'qiladigan raqamlar kerak.

---

## 1. Loyiha nima

**EduCenter** — O'zbekistondagi til o'quv markazlari (asosan ingliz tili, IELTS tayyorgarlik) uchun yagona boshqaruv platformasi. U bitta tizimda birlashtiradi:

- **CRM** — kelgan mijozlar (lid), qo'ng'iroqlar, sinov darsi, studentga aylantirish;
- **O'quv jarayoni (LMS)** — guruhlar, dars jadvali, davomat, uy vazifasi, imtihonlar, resurslar;
- **Moliya** — oylik hisob-fakturalar, to'lovlar, qarzlar, chegirma va promo-kodlar;
- **Muloqot** — real vaqtli chat (shaxsiy, guruh, xodimlar kanali) va bildirishnomalar;
- **Sun'iy intellekt** — AI Tutor (savol-javob), AI Writing (insho baholash), AI Speaking (nutq mashqi), markaz materiallariga tayangan bilim bazasi;
- **Avtomatlashtirish** — yo'qlik, qarz, uy vazifasi bo'yicha avtomatik vazifa va eslatmalar;
- **Support Needed Engine** — qiynalayotgan studentni tizim o'zi aniqlab, unga yordamchi o'qituvchi tavsiya qiladi.

Platforma **ko'p filialli** va **ko'p tashkilotli**: bitta tizimda bir nechta o'quv markazi, har birida bir nechta filial bo'lishi mumkin. Foydalanuvchi faqat o'z doirasidagi ma'lumotni ko'radi (§3.3).

**Web versiyasi tayyor va ishlayapti.** Mobil ilova **aynan shu API'dan** foydalanadi — yangi backend yozilmaydi.

---

## 2. Texnik asos (dizaynga ta'sir qiladigan qismi)

| Narsa | Qiymati |
|---|---|
| Backend | ASP.NET Core (.NET 10), REST API, `/api/...` |
| Mobil texnologiya | Flutter (Android + iOS) |
| Autentifikatsiya | JWT. `POST /api/auth/login` → `accessToken` + `refreshToken`. Token muddati tugasa avtomatik yangilanadi (foydalanuvchi buni sezmaydi) |
| Rol | JWT ichida keladi — ilova ochilishida rol asosida navigatsiya quriladi |
| Real vaqt | SignalR, `/hubs/chat` — yangi xabar, "yozmoqda…", o'qildi belgisi, reaksiya |
| Vaqt formati | Barcha sanalar API'dan **UTC ISO** keladi, ekranda **mahalliy vaqt** (UTC+5, Toshkent) ko'rsatiladi |
| Pul birligi | **so'm (UZS)**. Format: `1 200 000 so'm` (uch xonali guruh, probel bilan) |
| Fayllar | Chat ilovalari va bilim bazasi hujjatlari API orqali yuklanadi/olinadi |

**Tarmoq holati muhim:** o'quv markazlarida internet uziladi. Har bir ro'yxat ekranida "qayta urinish" holati bo'lishi shart (§11).

---

## 3. Foydalanuvchilar

### 3.1. 8 ta rol

| Kod | Nomi (o'zbekcha) | Kim |
|---|---|---|
| `SUPER_ADMIN` | Super Admin | Markaz egasi/bosh rahbar — hamma narsani ko'radi va o'zgartiradi |
| `ADMINISTRATOR` | Administrator | Filial boshlig'i — o'z filial(lar)i doirasida |
| `OPERATOR` | Operator | Qabul/call-markaz — lidlar, qo'ng'iroqlar, to'lov qabuli |
| `TEACHER` | O'qituvchi | O'zi asosiy o'qituvchi bo'lgan guruhlar |
| `SUPPORT_TEACHER` | Yordamchi o'qituvchi | Unga biriktirilgan guruhlar/studentlar |
| `STUDENT` | Student | Faqat o'zining ma'lumoti |
| `PARENT` | Ota-ona | Faqat farzand(lar)ining ma'lumoti |
| `DEVELOPER` | Dasturchi | Texnik monitoring (ekranlari hali yo'q) |

### 3.2. Mobil ilova kimga kerak — tavsiya etilgan qamrov

Mobil ilovaning **birinchi versiyasi (v1)** uch rolga qaratilishi kerak. Ular kun davomida telefonda ishlaydi:

| Rol | Ustuvorlik | Nega |
|---|---|---|
| **STUDENT** | 1 — asosiy | Jadval, uy vazifasi, AI mashqlar, chat — hammasi telefonda qilinadi |
| **PARENT** | 2 — asosiy | Farzand davomati, bahosi, qarzi — faqat ko'rish uchun, telefon eng qulay |
| **TEACHER / SUPPORT_TEACHER** | 3 — asosiy | Dars boshida davomat belgilash — sinfda telefonda qilinadi, noutbuk ochilmaydi |

**SUPER_ADMIN / ADMINISTRATOR / OPERATOR** — v1 da faqat **ko'rish** ekranlari (dashboard, bildirishnomalar, chat, qarzdorlar ro'yxati). Moliyaviy operatsiyalar, guruh yaratish, avtomatlashtirish sozlamalari — bular murakkab jadvalli ishlar, webda qoladi.

> **Dizayn vositasiga:** har bir rol uchun **alohida navigatsiya** kerak. Bitta universal menyu qilmang — student "Lidlar"ni hech qachon ko'rmaydi, operator "AI Speaking"ni ko'rmaydi.

### 3.3. Ko'rish doirasi (scope) — dizaynga ta'siri

Har bir foydalanuvchi bir xil ekranni ochadi, lekin **ichidagi ma'lumot hajmi turlicha**:

| Doira | Ma'nosi |
|---|---|
| `ALL_ORGANIZATION` | Tashkilotdagi barcha filiallar |
| `MY_BRANCHES` | Faqat o'z filial(lar)i |
| `MY_GROUPS` | O'zi asosiy o'qituvchi bo'lgan guruhlar |
| `ASSIGNED_GROUPS` | Yordamchi sifatida biriktirilgan guruhlar |
| `SELF` | Faqat o'zi |
| `MY_CHILDREN` | Faqat farzandlari |

**Amaliy natija:** "Studentlar" ro'yxati Super Admin uchun 800 qatorli izlanadigan jadval, o'qituvchi uchun 60 qatorli ro'yxat, student uchun esa **bitta karta** (o'zi). Shuning uchun student va ota-ona uchun ro'yxat emas, **to'g'ridan-to'g'ri profil/dashboard** ochilishi kerak.

---

## 4. Dizayn tizimi (web'dan olinadi — mobilda saqlanadi)

Bu ranglar va shriftlar allaqachon tanlangan va ishlatilmoqda. **Mobil ilovada aynan shular qoladi** — foydalanuvchi web va mobilni bitta mahsulot deb his qilishi kerak.

### 4.1. Ranglar

**Brend — to'q ko'kimtir-yashil (teal).** O'sish va ishonchni bildiradi, bugungi kunda hamma ishlatadigan binafsha-ko'k gradientdan qasddan uzoqlashtirilgan.

```
brand-50  #eff6f5    brand-500 #337d70
brand-100 #d7ebe7    brand-600 #26645a   ← asosiy tugma foni
brand-200 #b0d6cf    brand-700 #1e5049
brand-300 #82bab0    brand-800 #173d38
brand-400 #549b8f    brand-900 #102b27
```

**Urg'u — iliq oxra (ochre).** Faqat diqqat talab qiladigan joyga: streak, "e'tibor bering" belgilari. Ko'p ishlatilmaydi.

```
accent-300 #f3c37f   accent-500 #dc8b2a   accent-700 #925716
accent-400 #eaa851   accent-600 #b96f1c
```

**Neytral ranglar — iliq kulrang** (sof kulrang emas, ichida brend rangdan bir tomchi bor):

```
ink-50  #f7f6f3   ink-500 #767267
ink-100 #eeece7   ink-600 #5b584f
ink-200 #dedbd3   ink-700 #45433c
ink-300 #c2beb3   ink-800 #2e2c27   ← asosiy matn
ink-400 #9a958a   ink-900 #1c1b17   ← sarlavha
```

**Yuza va chegaralar:**

```
canvas         #f7f6f3   ← ekran foni
surface        #ffffff   ← karta foni
border         #e2dfd7
border-strong  #cac6ba
```

**Semantik (holat) ranglari** — brend va urg'u ranglaridan ataylab ajratilgan:

```
success  #1f8a56  (fon #eaf6ef)   ← to'langan, kelgan, topshirilgan
warning  #b7791b  (fon #fbf1e2)   ← muddati yaqin, qisman to'langan
danger   #c0362c  (fon #fbeae8)   ← qarz, kelmagan, xato
info     #2563a8  (fon #eaf1fb)   ← neytral xabar
```

### 4.2. Shriftlar

| Rol | Shrift | Qayerda |
|---|---|---|
| Sarlavha | **Sora** | h1–h4, ekran nomi, katta raqam |
| Matn | **IBM Plex Sans** | asosiy matn, tugma, yorliq |
| Raqamli ma'lumot | **IBM Plex Mono** | ID, vaqt, xona nomi, pul miqdori, band ball |

Sarlavhada harflar orasi biroz siqilgan (`letter-spacing: -0.01em`). Jadvaldagi raqamlar **tabular** (bir xil kenglikdagi raqam) bo'lishi shart — ustunlar tekis tursin.

### 4.3. Komponentlar (web'da bor, mobilda muqobili kerak)

`Button` · `Badge` · `Card` · `Input / Select / Textarea / Label` · `Table` · `Drawer` · `Tabs` · `Avatar` (ism bo'yicha rangli initsiallar) · `StatTile` (raqamli plitka) · `PageHeader` · `EmptyState` · `Skeleton` (yuklanish) · `Alert`.

**Mobilga moslashtirish:**

| Web | Mobil muqobil |
|---|---|
| `Table` (jadval) | Karta ro'yxati. Jadvalni telefonda gorizontal aylantirmang |
| `Drawer` (o'ngdan chiquvchi panel) | Pastdan chiquvchi **bottom sheet** |
| `Tabs` | Yuqoridagi segment tugmalar (2–4 ta) yoki aylanadigan chip qatori (5+) |
| Sidebar | Pastdagi **tab bar** (3–5 ta) + "Yana" ekrani |
| `Ctrl+K` universal qidiruv | Yuqoridagi qidiruv ikonkasi → to'liq ekranli qidiruv |

**O'lcham qoidalari:** bosiladigan element eng kamida 44×44 pt. Asosiy matn 15–16 pt (web'da 13.5 px — telefonda kichik). Ro'yxat qatori 64–72 pt balandlik.

---

## 5. STUDENT — ekranlar (asosiy rol)

### 5.1. Navigatsiya

Pastki tab bar, 5 ta:

```
[ Bosh sahifa ]  [ Jadval ]  [ AI ]  [ Xabarlar ]  [ Profil ]
```

"AI" tabi ichida uchta bo'lim: **Tutor**, **Writing**, **Speaking**.
Bildirishnoma qo'ng'irog'i — yuqori o'ng burchakda, o'qilmaganlar soni bilan.

### 5.2. Bosh sahifa (yangi ekran — webda to'liq ekvivalenti yo'q)

Student uchun eng muhim ekran. Yuqoridan pastga:

1. **Salomlashish + daraja** — "Salom, Aziz" · daraja belgisi (`B1`, `IELTS 6.0` kabi matn; `currentLevel` maydoni, bo'sh bo'lishi mumkin).
2. **Keyingi dars kartasi** — guruh nomi, mavzu, vaqt ("Bugun 15:00 – 16:30"), xona, o'qituvchi. Agar bugun dars bo'lmasa — "Bugun dars yo'q, keyingisi: seshanba 15:00".
3. **Diqqat talab qiladigan narsalar** (bo'lsa ko'rsatiladi, bo'lmasa yo'q):
   - muddati yaqin/o'tgan uy vazifasi — `danger`/`warning` rangda;
   - to'lanmagan hisob-faktura — summa va muddati;
   - yangi baholangan ish.
4. **Ikki raqamli plitka:** `Davomat 92%` · `Uy vazifasi 78%` (API: `attendanceRate`, `homeworkCompletionRate` — 0–1 oralig'ida keladi, foizga aylantiriladi).
5. **AI tezkor tugmalari** — "Savol bering" (Tutor), "Insho yuborish" (Writing), "Gapirish mashqi" (Speaking). Har birida bugungi qolgan limit kichik matnda.
6. **Guruhlarim** — student a'zo bo'lgan guruhlar ro'yxati (odatda 1–2 ta).

### 5.3. Dars jadvali

- **Ko'rinish:** hafta bo'yicha gorizontal kun tanlagich (Du–Ya) + tanlangan kunning darslari vertikal ro'yxatda. Oylik kalendar shart emas — studentda haftasiga 2–3 dars bo'ladi.
- **Dars kartasi:** vaqt oralig'i (mono shrift) · guruh nomi · mavzu · xona · o'qituvchi ismi. O'tgan darslar susaytirilgan, bugungisi brend rangli chap chiziq bilan ajratilgan.
- **Bosilganda:** dars sahifasi ochiladi.

### 5.4. Dars sahifasi

Ikki bo'lim (tab yoki ketma-ket):

**Davomat** — shu darsdagi o'z holati, bitta belgi:

| Holat | Yorlig'i | Rang |
|---|---|---|
| `Present` | Keldi | success |
| `Absent` | Kelmadi | danger |
| `Late` | Kechikdi | warning |
| `Excused` | Sababli | info |
| `Online` | Onlayn | info |

**Uy vazifasi** — shu darsga berilgan vazifalar ro'yxati. Har birida: sarlavha, tavsif, turi, muddati, maksimal ball, o'z topshirig'i holati.

Uy vazifasi turlari: `Writing` · `Speaking` · `Reading` · `Listening` · `Grammar` · `Vocabulary` · `FileUpload` · `Quiz` · `Custom`. Har biri uchun kichik ikonka kerak.

**Resurslar** — darsga biriktirilgan havolalar (sarlavha + tavsif + havola).

### 5.5. Uy vazifasini topshirish

- Kirish: matn maydoni (uzun insho ham yoziladi) **yoki** havola.
- Yuborilgandan keyin: "Topshirildi — 14-sentyabr 21:40" holati.
- Baholangandan keyin: **ball / maksimal ball** (katta, mono shrift) + o'qituvchi izohi.
- **Writing turidagi vazifada qo'shimcha:** AI baholagan taxminiy IELTS band va "AI tahlil" tugmasi — bosilganda to'liq tahlil ochiladi (§5.7 dagi bilan bir xil ko'rinish).

### 5.6. AI Tutor (chat)

Ingliz tili bo'yicha savol-javob. Javob **markazning o'z materiallariga** tayanadi va manbalarini ko'rsatadi.

- **Ko'rinish:** oddiy chat. Savol — o'ngda, brend rangli pufakcha, oq matn. Javob — chapda, ochiq kulrang pufakcha.
- **Javob formatlangan matn** (markdown): qalin matn, ro'yxatlar, kichik sarlavhalar. Bu muhim — AI ko'pincha "1. ... 2. ..." shaklida tushuntiradi.
- **Manbalar:** javob ostida kichik chiplar — ishlatilgan hujjat nomlari ("Grammar Book Unit 4"). Manba bo'lmasligi ham mumkin.
- **Yuborish:** matn maydoni + yuborish tugmasi. Yozilgan savol darhol chatga tushadi, javob tayyor bo'lguncha (odatda 3–8 soniya) pastda uchta nuqta animatsiyasi. **Muhim:** nuqtalar faqat 1.5 soniyadan uzoq kutilsa chiqadi — tez javobda ko'z oldida lip-lip qilmasin.
- **Suhbatlar tarixi:** yuqorida "Avvalgi suhbatlar" tugmasi → bottom sheet'da ro'yxat (sarlavha, savollar soni, oxirgi vaqt, o'chirish). Ilova ochilganda **oxirgi suhbat davom etadi**, bo'sh ekran emas.
- **Yangi suhbat** tugmasi alohida.
- **Mavzudan tashqari savol:** tutor faqat ingliz tili bo'yicha javob beradi. Futbol natijasi so'ralsa — muloyim rad javobi, xato emas. Uni oddiy AI xabari sifatida ko'rsating, qizil ogohlantirish qilmang.
- **Til:** student qaysi tilda so'rasa (o'zbek/rus/ingliz), javob o'sha tilda keladi.
- **Limit:** kuniga 50 savol. Qolgani sarlavhada: "Bugun qoldi: **43** / 50".

### 5.7. AI Writing (insho baholash)

**Yuborish ekrani:**
- Vazifa turi tanlash: `IELTS Task 1` · `IELTS Task 2` · `Umumiy writing`.
- Vazifa matni (prompt) — qisqa maydon.
- Insho matni — katta maydon, **so'zlar soni jonli ko'rsatiladi** (IELTS'da bu muhim: Task 2 uchun 250+).
- "Baholashga yuborish" tugmasi.

**Natija ekrani** — ilovaning eng ma'lumotga boy ekrani, diqqat bilan dizayn qilinsin:

1. **Taxminiy band** — katta raqam (masalan `6.5`), yonida kichik matn: "AI bahosi — rasmiy natija emas".
2. **4 ta mezon** (IELTS bandlari, har biri 0–9):
   - Task Response / Task Achievement
   - Coherence & Cohesion
   - Lexical Resource
   - Grammatical Range & Accuracy

   Har biri: nomi · ball · qisqa izoh. Gorizontal chiziqli diagramma (0–9 shkala) yaxshi ishlaydi.
3. **Kuchli tomonlar** — yashil belgi bilan ro'yxat.
4. **Zaif tomonlar** — sariq belgi bilan ro'yxat.
5. **Grammatik xatolar** va **lug'at xatolari** — har biri uchta qatorli karta:
   - *Sizda:* xato jumla parchasi (ingliz tilida, qizg'ish fon);
   - *To'g'risi:* tuzatilgan variant (yashil fon);
   - *Nega:* tushuntirish (o'zbek tilida).
6. **Tavsiyalar** — nima ustida ishlash kerak.
7. **O'qituvchi bahosi** (agar qo'ygan bo'lsa) — AI bahosi yonida alohida ko'rsatiladi, ustidan yozilmaydi. "O'qituvchi bahosi: 6.0 — AI 6.5 deb baholagan edi" + izoh.

> **Til qoidasi:** tushuntirish, tavsiya va izohlar — **o'zbekcha**. Insho parchalari, tuzatilgan jumlalar va mezon nomlari — **inglizcha** (ular ingliz tilining o'zi haqida).

**Tarix:** oldingi ishlar ro'yxati — sana, vazifa turi, so'zlar soni, band. Baholash 5–15 soniya davom etadi; shu vaqtda "Baholanmoqda…" holati bo'lishi kerak (ekran o'zi yangilanadi).

**Limit:** kuniga 5 ta insho.

### 5.8. AI Speaking (nutq mashqi)

Eng murakkab mobil ekran — mikrofon, ovoz va vaqt bilan ishlaydi. Telefonda webdan ancha qulay bo'ladi, shuning uchun alohida e'tibor bering.

**1-qadam — rejim tanlash.** Beshta karta:

| Rejim | Yorlig'i | Izohi |
|---|---|---|
| `IeltsPart1` | IELTS Part 1 | Qisqa, kundalik savollar — 5 ta savol |
| `IeltsPart2` | IELTS Part 2 (cue card) | Bitta kartochka: 1 daqiqa tayyorgarlik, 1–2 daqiqa gapirish |
| `IeltsPart3` | IELTS Part 3 | Fikr talab qiladigan chuqurroq savollar — 4 ta savol |
| `GeneralEnglish` | Umumiy ingliz tili | Kundalik mavzularda suhbat — 6 ta savol |
| `FreeConversation` | Erkin suhbat | Erkin suhbat — 6 ta savol |

**2-qadam — savol-javob sikli.** Ekranda:
- Savol matni (katta, o'qish oson) + **tinglash tugmasi** (telefon o'zi ovoz chiqarib o'qiydi);
- Qadam ko'rsatkichi: "Savol 2 / 5";
- Markazda katta **yozish tugmasi** (mikrofon). Bosilganda: to'lqin animatsiyasi + o'tgan vaqt sanoqchisi;
- To'xtatilgandan keyin: qayta eshitish · qayta yozish · "Javobni yuborish";
- Yuborilgandan keyin transkript chiqadi (AI nutqni matnga aylantiradi) va keyingi savolga o'tiladi;
- "Mashqni yakunlash" tugmasi har doim mavjud.

**IELTS Part 2 alohida:** kartochka ko'rsatilgach **1 daqiqalik tayyorgarlik taymeri** ishga tushadi (aylana ko'rinishida), keyin yozish boshlanadi.

**3-qadam — yakuniy natija:**
- Taxminiy umumiy band + uchta alohida band: **Fluency** · **Lexical** · **Grammar**;
- Nutq statistikasi: umumiy gapirilgan vaqt, **so'z/daqiqa** tezligi, uzun pauzalar soni;
- Har bir band bo'yicha izoh, kuchli/zaif tomonlar, tavsiyalar (o'zbekcha);
- Har bir savol bo'yicha: savol, transkript, gapirilgan vaqt, tezlik, **yozuvni qayta eshitish**.

**Tarix:** oldingi mashqlar — sana, rejim, mavzu, band, davomiyligi.

**Limit:** kuniga 20 daqiqa gapirish.

**Ruxsatlar:** mikrofon ruxsati so'raladigan ekran kerak — nima uchun kerakligi tushuntirilgan holda, tizim oynasi chiqishidan oldin.

### 5.9. Xabarlar (chat)

Real vaqtli. Uch turdagi suhbat:

| Turi | Kim bilan |
|---|---|
| `Direct` | Shaxsiy — o'qituvchi, yordamchi o'qituvchi, administrator bilan |
| `Group` | Guruh chati — guruhdoshlar va o'qituvchi |
| `StaffChannel` | Xodimlar kanali — **studentda ko'rinmaydi** |

**Ro'yxat ekrani:** avatar (ism bo'yicha rangli initsial) · nomi · oxirgi xabar parchasi · vaqt · o'qilmaganlar soni (brend rangli dumaloq).

**Suhbat ekrani imkoniyatlari (hammasi backendda bor):**
- xabarga **javob berish** (yuqorida iqtibos bilan);
- **reaksiya** — 👍 ❤️ 😂 😮 🙏 ✅ (uzoq bosilganda chiqadi);
- xabarni **o'chirish** (o'z xabarini);
- **pin** — muhim xabar tepada qotiriladi (faqat o'qituvchi/admin);
- **fayl va rasm** yuborish (rasm chatda ko'rinadi);
- **"yozmoqda…"** ko'rsatkichi;
- **o'qildi** belgisi;
- suhbat ichida **qidiruv**;
- eski xabarlar yuqoriga aylantirilganda yuklanadi.

### 5.10. To'lovlar

Faqat ko'rish (student to'lov qilmaydi — kassaga naqd yoki Click/Payme orqali to'laydi, operator tizimga kiritadi).

- **Joriy holat:** qarz bo'lsa — qizil karta: summa + necha kun kechikkan. Qarz bo'lmasa — yashil: "Qarz yo'q".
- **Hisob-fakturalar ro'yxati:** tavsif (masalan "IELTS Intensive — sentyabr"), davr, muddati, summa, to'langan, qoldiq, holat.

| Holat | Yorlig'i | Rang |
|---|---|---|
| `Pending` | To'lanmagan | warning (muddati o'tgan bo'lsa danger) |
| `PartiallyPaid` | Qisman to'langan | warning |
| `Paid` | To'langan | success |
| `Cancelled` | Bekor qilingan | ink-400 |

- **To'lovlar tarixi:** sana, summa, usuli (`Naqd` · `Karta` · `Bank o'tkazmasi` · `Click` · `Payme` · `Boshqa`), qabul qilgan xodim.
- **Chegirma** qo'llanilgan bo'lsa alohida ko'rsatiladi.

### 5.11. Imtihonlar

Ro'yxat: imtihon nomi, turi, sanasi, guruh, natijalar.

Imtihon turlari: `Placement` (daraja aniqlash) · `Unit` (bo'lim) · `Monthly` (oylik) · `MockIelts` (sinov IELTS) · `Final` (yakuniy) · `Custom`.

Natija — 5 ta ball: **Listening · Reading · Writing · Speaking · Overall**. Har biri 0–9 (yoki 0–100, imtihon turiga qarab). Mono shriftda, tekis ustunda.

**Progress:** agar bir nechta imtihon bo'lsa — vaqt bo'yicha o'sish grafigi (oddiy chiziqli, faqat Overall). Bir nechta ball bo'lgani uchun ikkinchi o'q qo'shmang — kerak bo'lsa alohida kichik grafiklar.

### 5.12. Profil

Ism, student raqami, email, telefon, daraja, qo'shilgan sanasi, holati.

Student holatlari: `Active` (Faol) · `Debt` (Qarzdor) · `Frozen` (Muzlatilgan) · `Transferred` (O'tkazilgan) · `Graduated` (Bitirgan) · `Dropped` (Tark etgan).

Shuningdek: **bildirishnoma sozlamalari** (har bir tur uchun yoqish/o'chirish), **til**, **tizimdan chiqish**.

---

## 6. PARENT (ota-ona) — ekranlar

Ota-ona **hech narsani o'zgartirmaydi** — faqat ko'radi va yozadi (chat). Interfeys sokin, ishonchli va juda sodda bo'lishi kerak; ota-ona texnikaga unchalik yaqin bo'lmasligi mumkin.

### 6.1. Navigatsiya

```
[ Farzandim ]  [ Jadval ]  [ To'lov ]  [ Xabarlar ]  [ Profil ]
```

Agar bir nechta farzand bo'lsa — yuqorida farzand tanlagich (avatar + ism, bosilganda almashadi). Bitta bo'lsa tanlagich ko'rsatilmaydi.

### 6.2. "Farzandim" ekrani

Bitta ekranda eng muhim javoblar:

1. Farzand kartasi: ism, guruh(lar), daraja, holati.
2. Ikki katta raqam: **Davomat %** va **Uy vazifasi %**.
3. **So'nggi davomat** — oxirgi 5–10 dars, har biri bir qatorda (sana, guruh, holat belgisi). Kelmagan darslar ajralib tursin.
4. **Uy vazifasi holati** — topshirilgan/topshirilmagan, ball.
5. **So'nggi imtihon natijasi** — 5 ta ball.
6. **O'qituvchi izohlari** — baholangan vazifalardagi izohlar ro'yxati.
7. **Yordam signali** (bo'lsa) — "Farzandingizga yordamchi o'qituvchi biriktirildi" kabi xabar (§7.6).

### 6.3. Qolgan ekranlar

- **Jadval** — student jadvalining aynan o'zi (faqat ko'rish).
- **To'lov** — §5.10 dagi ekran, faqat ko'rish. Ota-ona uchun bu eng tez-tez ochiladigan ekranlardan biri.
- **Xabarlar** — o'qituvchi va administratsiya bilan shaxsiy chat.

---

## 7. TEACHER / SUPPORT_TEACHER — ekranlar

O'qituvchi uchun asosiy stsenariy: **dars boshlanadi → telefonni oladi → davomat belgilaydi → uy vazifasi beradi**. Bu 60 soniyada bajarilishi kerak.

### 7.1. Navigatsiya

```
[ Bugun ]  [ Guruhlar ]  [ Studentlar ]  [ Xabarlar ]  [ Yana ]
```

"Yana" ichida: Imtihonlar, Support, Bilim bazasi, Profil, Sozlamalar.

### 7.2. "Bugun" ekrani

- Bugungi darslar ro'yxati, vaqt bo'yicha. Har birida: vaqt, guruh, mavzu, xona, studentlar soni.
- **Davomat belgilanmagan dars alohida ajralib tursin** — "Davomat belgilanmagan" yorlig'i bilan, birinchi o'rinda.
- Dars kartasida to'g'ridan-to'g'ri **"Davomat belgilash"** tugmasi.
- Pastda: baholanmagan uy vazifalari soni, javobsiz xabarlar.

### 7.3. Davomat belgilash — eng muhim ekran

Bitta ekranda guruhdagi hamma student. Har bir qator:

```
[Avatar]  Ism Familiya            [ Keldi ][ Kelmadi ][ Kechikdi ][ Sababli ]
```

- **Sukut bo'yicha hamma "Keldi"** — o'qituvchi faqat kelmaganlarni bosadi. Bu eng tez usul.
- Tanlangan holat rangli (success/danger/warning/info), qolganlari kulrang.
- Yuqorida sanoq: "18 keldi · 2 kelmadi".
- Pastda qotirilgan **"Saqlash"** tugmasi.
- `Online` holati ham bor, lekin kamdan-kam ishlatiladi — uni qo'shimcha menyuga oling.

### 7.4. Uy vazifasi

**Yaratish:** sarlavha, tavsif, turi (9 ta turdan), muddati (sana + vaqt), maksimal ball.

**Baholash:** topshiriqlar ro'yxati → bitta topshiriq → student matni/havolasi → ball (raqam) + izoh → saqlash.

**Writing turidagi topshiriqda:** AI allaqachon baholagan bo'ladi. O'qituvchiga ko'rsatiladi: AI bandi, to'liq tahlil ("AI tahlil" tugmasi) va o'z bahosini qo'yish imkoniyati. AI bahosi **o'chirilmaydi** — ikkalasi yonma-yon saqlanadi (bu keyinchalik AI sifatini o'lchash uchun ishlatiladi).

### 7.5. Guruhlar va studentlar

- **Guruhlar:** karta ro'yxati — guruh nomi, kurs, filial, sig'imi, studentlar soni, holati (`Forming` Shakllanmoqda · `Active` Faol · `Completed` Tugagan · `Cancelled` Bekor qilingan).
- **Guruh ichi:** studentlar ro'yxati, jadval, o'rtacha davomat.
- **Student 360°** (o'qituvchi ko'rinishi): profil, davomat, uy vazifasi, imtihonlar, tarix. Moliya bo'limi o'qituvchiga **ko'rinmaydi**.

### 7.6. Support (yordam signallari)

Tizim o'zi aniqlagan muammolar ro'yxati:

| Signal | Yorlig'i |
|---|---|
| `CONSECUTIVE_ABSENCES` | Ketma-ket yo'qlik |
| `HOMEWORK_MISSED` | Homework topshirilmayapti |
| `STAFF_PROPOSAL` | Xodim tavsiyasi |

Har bir holat: student, guruh, sabab, holati (`Recommended` Tavsiya etilgan · `Assigned` Biriktirilgan · `Dismissed` Rad etilgan · `Resolved` Yakunlangan).

Amallar: yordamchi o'qituvchi biriktirish · rad etish · yakunlash (izoh bilan).

---

## 8. ADMIN / OPERATOR — v1 da cheklangan qamrov

Bu rollar uchun mobilda **faqat kuzatuv** ekranlari. To'liq boshqaruv webda qoladi.

- **Dashboard:** bugungi tushum, yangi lidlar, qarzdorlar soni, bugungi darslar, yo'qlik foizi — `StatTile` plitkalar ko'rinishida.
- **Qarzdorlar ro'yxati:** ism, summa, necha kun, telefon → **bir bosishda qo'ng'iroq qilish** (telefonda bu juda foydali).
- **Bugungi vazifalar (Operator):** qo'ng'iroq navbati — muddati o'tgan va navbatdagi vazifalar, bir bosishda bajarish. Har bir vazifada sabab ko'rsatilgan ("Sinov darsidan keyin javob yo'q", "3 dars ketma-ket kelmadi").
- **Lidlar:** ro'yxat va qo'ng'iroq natijasini yozish. Lid holatlari: `Lead` · `Contacted` · `TrialBooked` · `TrialAttended` · `TrialMissed` · `NotInterested` · `Lost` · `Converted`.
- **Bildirishnomalar** va **Xabarlar** — hamma rol uchun bir xil.

Lid yaratish, studentga aylantirish, hisob-faktura chiqarish, guruh ochish, avtomatlashtirish qoidalari, audit jurnali, AI monitoringi — **v1 da mobilga kiritilmaydi**.

---

## 9. Bildirishnomalar

Ilova ichida qo'ng'iroq ikonkasi + o'qilmaganlar soni. Bosilganda ro'yxat; xabar bosilsa o'qilgan bo'ladi va tegishli ekran ochiladi.

**Mavjud bildirishnoma turlari** (backendda aynan shular bor):

| Kod | Kimga | Mazmuni |
|---|---|---|
| `PAYMENT_DUE` | Student, Ota-ona | To'lov muddati yaqinlashdi |
| `PAYMENT_OVERDUE` | Student, Ota-ona | To'lov muddati o'tdi |
| `ABSENT` | Student, Ota-ona | Darsga kelmadi |
| `ABSENCE_STREAK` | Ota-ona, O'qituvchi | Ketma-ket yo'qlik |
| `EXAM_RESULT` | Student, Ota-ona | Imtihon natijasi chiqdi |
| `HOMEWORK_CREATED` | Student | Yangi uy vazifasi |
| `HOMEWORK_DEADLINE` | Student | Muddati yaqinlashdi |
| `HOMEWORK_REVIEWED` | Student, Ota-ona | Vazifa baholandi |
| `LESSON_REMINDER` | Student | Dars eslatmasi |
| `SCHEDULE_CHANGED` | Student, Ota-ona | Jadval o'zgardi |
| `TEACHER_ASSIGNED` | Student | O'qituvchi biriktirildi |
| `SUPPORT_RECOMMENDED` | O'qituvchi, Admin | Yordam tavsiya etildi |
| `SUPPORT_ASSIGNED` | Student, Ota-ona | Yordamchi o'qituvchi biriktirildi |
| `TASK_ASSIGNED` | Operator | Yangi vazifa |
| `WRITING_EVALUATED` | Student | Insho baholandi |

Mobil uchun bularning har biri **push bildirishnoma** bo'lishi kerak (bosilganda tegishli ekran ochiladi). Sozlamalarda har bir turni alohida o'chirish mumkin.

---

## 10. Ma'lumot lug'ati — ekranda ishlatiladigan barcha holatlar

Dizaynda har bir holat uchun rang va yorliq kerak. To'liq ro'yxat:

**Davomat:** `Present` Keldi · `Absent` Kelmadi · `Late` Kechikdi · `Excused` Sababli · `Online` Onlayn

**Student:** `Active` Faol · `Debt` Qarzdor · `Frozen` Muzlatilgan · `Transferred` O'tkazilgan · `Graduated` Bitirgan · `Dropped` Tark etgan

**Guruh:** `Forming` Shakllanmoqda · `Active` Faol · `Completed` Tugagan · `Cancelled` Bekor qilingan

**Hisob-faktura:** `Pending` To'lanmagan · `PartiallyPaid` Qisman to'langan · `Paid` To'langan · `Cancelled` Bekor qilingan

**To'lov usuli:** `Cash` Naqd · `Card` Karta · `BankTransfer` Bank o'tkazmasi · `Click` · `Payme` · `Other` Boshqa

**Uy vazifasi turi:** `Writing` · `Speaking` · `Reading` · `Listening` · `Grammar` · `Vocabulary` · `FileUpload` Fayl · `Quiz` Test · `Custom` Boshqa

**Imtihon turi:** `Placement` Daraja aniqlash · `Unit` Bo'lim · `Monthly` Oylik · `MockIelts` Sinov IELTS · `Final` Yakuniy · `Custom` Boshqa

**Lid:** `Lead` Yangi · `Contacted` Bog'lanildi · `TrialBooked` Sinov darsi belgilandi · `TrialAttended` Sinovga keldi · `TrialMissed` Sinovga kelmadi · `NotInterested` Qiziqmadi · `Lost` Yo'qotildi · `Converted` Studentga aylandi

**Yordam holati:** `Recommended` Tavsiya etilgan · `Assigned` Biriktirilgan · `Dismissed` Rad etilgan · `Resolved` Yakunlangan

**AI Writing baholash:** `Queued` Navbatda · `Processing` Baholanmoqda · `Completed` Tayyor · `Failed` Xato · `Rejected` Rad etilgan

**AI Speaking mashqi:** `Active` Davom etmoqda · `Completed` Yakunlangan · `Failed` Xato · `Abandoned` Tashlab ketilgan

---

## 11. Har bir ekran uchun majburiy holatlar

AI dizayn vositalari odatda faqat "hamma narsa joyida" holatini chizadi. Bu ilovada quyidagilar **shart**:

1. **Yuklanmoqda** — skeleton (kulrang plitkalar), spinner emas. Ro'yxat shakli oldindan ko'rinib tursin.
2. **Bo'sh** — ikonka + sarlavha + bir jumlalik tushuntirish + (bo'lsa) harakat tugmasi. Masalan: "Hali insho yubormagansiz — birinchisini yuborib ko'ring".
3. **Xato / internet yo'q** — tushunarli o'zbekcha xabar + "Qayta urinish" tugmasi. Texnik kod ko'rsatilmasin.
4. **Ruxsat yo'q (403)** — "Bu bo'lim sizning rolingiz uchun ochiq emas". Bu holat kam uchraydi, chunki menyu roldan kelib chiqib quriladi, lekin dizayni bo'lsin.
5. **Limit tugadi (AI)** — "Bugungi limit tugadi. Ertaga yana 50 ta savol bera olasiz." Xato emas, sokin xabar.
6. **Kutish (AI)** — insho baholanmoqda, javob yozilmoqda, nutq tahlil qilinmoqda. Bular 5–20 soniya davom etadi, taxminiy kutish vaqti yozilsin.

---

## 12. Nimalar YO'Q — ekranga chizmang

TZ'da rejalashtirilgan, lekin **hali yozilmagan** funksiyalar. AI dizayn vositalari bularni o'zi "o'ylab topib" qo'shib yuborishi mumkin — kerak emas:

| Funksiya | Holati |
|---|---|
| Gamifikatsiya — XP, ball, daraja, streak, mini-o'yinlar, reyting jadvali | **Yo'q.** Keyingi bosqichda |
| Sertifikatlar | **Yo'q** |
| Placement test (onlayn daraja aniqlash testi) | **Yo'q** (imtihon turi sifatida bor, lekin test ilova ichida ishlamaydi) |
| Kursni muzlatish / qayta boshlash so'rovi | **Yo'q** (student holati sifatida bor, lekin so'rov jarayoni yo'q) |
| O'qituvchi almashtirish jarayoni | **Yo'q** |
| Studentni boshqa guruhga o'tkazish | **Yo'q** |
| NPS so'rovi, shikoyat (complaint) jarayoni | **Yo'q** |
| Telegram bot | **Yo'q** (bildirishnoma kanali sifatida rejalashtirilgan) |
| Ilova ichida to'lov qilish (Click/Payme integratsiyasi) | **Yo'q.** To'lov kassada qabul qilinadi, tizimga operator kiritadi |
| Video dars, jonli efir | **Yo'q** |
| Vocabulary/Grammar interaktiv mashqlar | **Yo'q** (uy vazifasi turi sifatida bor, lekin interaktiv mashq ekrani yo'q) |
| Reading/Listening interaktiv topshiriqlar | **Yo'q** (uy vazifasi turi sifatida bor) |
| AI javobini so'zma-so'z oqim (streaming) bilan yozish | **Yo'q.** Javob to'liq tayyor bo'lgach bir marta chiqadi |
| Talaffuz (pronunciation) bahosi | **Yo'q.** Speaking'da faqat ravonlik, lug'at va grammatika baholanadi |
| Qorong'i rejim (dark mode) | Webda yo'q. Mobilda **qo'shilsa yaxshi** — telefonda kutiladi |

---

## 13. Dizayn uchun yakuniy ko'rsatmalar

**Nimadan boshlash kerak.** Ustuvorlik shu tartibda:

1. Student: Bosh sahifa, Jadval, Dars sahifasi, Uy vazifasi topshirish
2. Student: AI Tutor, AI Writing natijasi, AI Speaking sikli
3. O'qituvchi: Bugun, Davomat belgilash
4. Ota-ona: Farzandim, To'lov
5. Chat (hamma rol uchun bir xil)
6. Bildirishnomalar, Profil, Kirish (login)

**Uslub.** Mavjud webdagi kabi: oq kartalar iliq kulrang fonda, yupqa chegaralar, yumshoq burchak (10–12 pt), soya deyarli yo'q (faqat suzuvchi elementlarda). Rang kam ishlatiladi — asosan holat belgilarida va asosiy tugmada. Ma'lumot zich joylashadi, lekin havo yetarli.

**Nimadan qochish kerak:**
- gradient fonlar va rangli kartalar devori;
- har bir ro'yxat elementida katta rasm/illyustratsiya;
- telefonda gorizontal aylanadigan jadvallar;
- bir ekranda 3 tadan ortiq asosiy harakat tugmasi;
- ikkita o'qli (dual-axis) grafiklar — hech qachon;
- mazmunsiz animatsiyalar. Animatsiya faqat ikki joyda: chatga xabar tushishi va yozuv paytidagi to'lqin.

**Til va matn:** hamma yorliq, tugma va xabar — o'zbek tilida, lotin alifbosida. Uzun matndan qoching: "Baholashga yuborish" ✓, "Inshongizni AI tomonidan baholanishi uchun yuborish" ✗.

**Telefon o'lchamlari:** asosiy maqsad — 390×844 pt (iPhone 14/15) va 360×800 pt (o'rta Android). Planshet v1 da kerak emas.

---

## 14. Ilova qilinadigan texnik ma'lumot

Bu bo'lim dizayn vositasi uchun emas, keyingi bosqich — Flutter ilovasini yozish uchun.

**Asosiy API endpointlari:**

```
POST   /api/auth/login                          kirish
POST   /api/auth/refresh                        token yangilash
POST   /api/auth/logout                         chiqish

GET    /api/students                            studentlar (doira bo'yicha filtrlanadi)
GET    /api/students/{id}/overview               statistika + guruhlar
GET    /api/students/{id}/attendance             davomat tarixi
GET    /api/students/{id}/homework                uy vazifalari
GET    /api/students/{id}/exams                   imtihon natijalari
GET    /api/students/{id}/timeline                tarix
GET    /api/students/{id}/finance                 moliyaviy holat

GET    /api/schedule                             dars jadvali
GET    /api/lessons/{lessonId}/attendance         darsning davomati
POST   /api/lessons/{lessonId}/attendance         davomat belgilash
GET    /api/homework?lessonId=                    darsning uy vazifalari
POST   /api/homework                              vazifa yaratish
POST   /api/homework/{id}/submissions             topshirish
GET    /api/homework/{id}/my-submissions          o'z topshirig'i
PATCH  /api/homework/submissions/{id}/review      baholash
GET    /api/exams · /api/exams/{id}/results       imtihonlar
GET    /api/resources?lessonId=                   dars resurslari
GET    /api/groups · /api/groups/{id}/students    guruhlar

GET    /api/ai/status                            limitlar va mavjudlik
POST   /api/ai/tutor/ask                         savol berish
GET    /api/ai/tutor/conversations                suhbatlar
GET    /api/ai/tutor/conversations/{id}/messages  yozishmalar
POST   /api/ai/writing/evaluations                insho yuborish
GET    /api/ai/writing/evaluations                insholar tarixi
POST   /api/ai/speaking/sessions                  mashq boshlash
POST   /api/ai/speaking/sessions/{id}/turns       ovozli javob yuborish
POST   /api/ai/speaking/sessions/{id}/finish      yakunlash

GET    /api/chats · /api/chats/{id}/messages      chat
POST   /api/chats/{id}/messages                   xabar yuborish
       /hubs/chat                                 SignalR — real vaqt

GET    /api/notifications                        bildirishnomalar
GET    /api/notifications/unread-count            o'qilmaganlar soni
GET    /api/notifications/preferences             sozlamalar

GET    /api/finance/invoices · /payments          moliya
GET    /api/support-cases                        yordam signallari
```

**Muhim eslatmalar:**
- Barcha so'rovlar `Authorization: Bearer <accessToken>` bilan yuboriladi.
- Ruxsat yo'qligi `403`, topilmaganlik `404`, limit tugashi `429` bilan qaytadi.
- AI so'rovlari sekin (3–20 soniya) — timeout kamida 60 soniya bo'lsin.
- Ovozli fayl hajmi 15 MB bilan cheklangan.
