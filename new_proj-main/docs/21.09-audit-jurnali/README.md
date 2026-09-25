# 21.09 AUDIT JURNALI — bosqichma-bosqich bajariladigan hujjatlar

**Sana:** 2026-09-21
**Manba:** `docs/10_full_audit_2026-09-21.md` (audit) + `docs/11_implementation_plan.md` (reja) + foydalanuvchining ikki
yangi talabi (bloklangan foydalanuvchi, o'qituvchi portfoliosi) + auditdan keyin kodni o'qib topilgan uchta teshik.

**Bu papka nima uchun.** Har bir fayl — **bitta Claude sessiyasi uchun mustaqil vazifa**. Fayl ichida maqsad, kodning hozirgi
holati (tekshirilgan), nima yozish kerakligi, testlar va "tayyor" mezoni bor. Yangi sessiya boshqa hujjatni o'qimasdan
faqat shu faylni o'qib ishlay oladi. Sessiya limiti tez tugamasligi uchun: **bitta fayl = bitta yangi chat**.

---

## 1. Qanday ishlatiladi

1. Holat jadvalidan keyingi ⬜ qadamni tanlang (tartib bilan — bog'liqliklar pastda).
2. **Yangi chat** oching (eskisini davom ettirmang — kontekst har so'rovda qayta yuboriladi, uzun chat qimmat).
3. Quyidagi promptni yozing (fayl nomini almashtirib).
4. Claude tugatgach: testlar yashil, `tsc`/`eslint` toza ekanini ko'ring, so'ng **"commit qil"** deng
   (loyiha qoidasi: commit faqat so'ralganda).
5. Shu README dagi jadvalda qadamni ✅ qiling (yoki Claude'ga ayting).

### Boshlash prompti (nusxa oling)

```
docs/21.09-audit-jurnali/S01.02-ota-ona.md faylini o'qi va faqat shu qadamni bajar.
Boshqa qadamlarga tegma. Fayldagi "Boshlashdan oldin o'qing" ro'yxatidan tashqari
katta fayllarni (ayniqsa docs/00_TZ.md ni) to'liq o'qima. Tugagach testlar, tsc va eslint
ni ishga tushir, docs/09_audit_progress.md ga jurnal bandi yoz. Commit qilma — men aytaman.
```

---

## 2. Holat jadvali

**Ustunlar:** *docs/11* — o'sha qadamning `docs/11_implementation_plan.md` dagi raqami (agar farq qilsa).
Hajm — bitta dasturchi uchun taxminiy kun (backend + frontend + test + brauzerda tekshirish).

| ✔ | Fayl | Nima | Hajm | docs/11 | Bog'liq |
|---|---|---|---|---|---|
| ✅ | [S00](S00-repoga-yozish.md) | Repoga yozish (124 fayl, 3 commit) | 0.5 | 0 | — |
| ✅* | [S01.01](S01.01-student-yaratish.md) | Student yaratish/tahrirlash (*S01.01–S01.05 commit kutilmoqda*) | 1.5 | 1.1 | S00 |
| ✅ | [S01.02](S01.02-ota-ona.md) | Ota-ona + farzand bog'lami, `/users` orqali yetim hisob teshigini yopish | 1.5 | 1.2 | S01.01 |
| ✅ | [S01.03](S01.03-guruhga-yozish-chiqarish.md) | Guruhga yozish / chiqarish / bitirish | 1.5 | 1.3 | S01.01 |
| ✅ | [S01.04](S01.04-bloklangan-foydalanuvchi.md) | **YANGI** — bloklangan foydalanuvchi: darhol chiqarish, ekran, login xabari + rol himoyasi | 2.5 | — | — |
| ✅ | [S01.05](S01.05-parol-ozgartirish.md) | Parol o'zgartirish + admin tiklashi | 1.5 | 1.4 | **S01.04** |
| ✅ | [S02.01](S02.01-guruhni-boshqarish.md) | Guruhni tahrirlash / yopish / o'chirish | 1.5 | 2.1 | S01.03 |
| ✅ | [S02.02](S02.02-darsni-tahrirlash-bekor-qilish.md) | Darsni tahrirlash / ko'chirish / bekor qilish | 2 | 2.2 | S02.01 |
| ✅ | [S02.03](S02.03-imtihon-homework-kurs-tahrirlash.md) | Imtihon, homework, kursni tahrirlash/o'chirish | 1.5 | 2.3+2.4 | — |
| ✅ | [S03.01](S03.01-dars-mavzusi-izohi.md) | Dars mavzusi va izohi (elektron jurnal) | 1 | 3.1 | S02.02 |
| ✅ | [S03.02](S03.02-davomat-izohi-ball.md) | Davomat izohi va dars balli | 1 | 3.2 | S03.01 |
| ✅ | [S03.03](S03.03-homework-fayl.md) | Homeworkga fayl yuklash | 2 | 3.3 | — |
| ✅ | [S03.04](S03.04-individual-topshiriq.md) | Individual topshiriq (bitta studentga) | 1 | 3.4 | S03.03 |
| ✅ | [S04.01](S04.01-exam-grammar-vocabulary.md) | ExamResult: Grammar va Vocabulary | 0.5 | 4.1 | S02.03 |
| ✅ | [S04.02](S04.02-learning-profile.md) | Student Learning Profile (TZ §7) | 1.5 | 4.2 | S04.01 |
| ✅ | [S04.03](S04.03-student-tablar-learning-writing-speaking.md) | Student 360°: Learning, Writing, Speaking tablari | 1.5 | 4.3 | S04.02 |
| ✅ | [S04.04](S04.04-student-sarlavha-foto.md) | Student sarlavhasi + foto | 1 | 4.4 | S01.02 |
| ✅ | [S04.05](S04.05-learning-signals.md) | Qolgan 4 ta Learning Signal | 2 | 4.5 | S04.02 |
| ✅ | [S05.01](S05.01-teacher-360-tablar.md) | Teacher 360° qolgan tablari | 2 | 5.1 | S04.04 |
| ✅ | [S05.02](S05.02-teacher-portfolio.md) | **YANGI** — o'qituvchi portfoliosi (IELTS, sertifikat, tajriba) | 3 | — | S05.01, S04.04 |
| ✅ | [S05.03](S05.03-branch-dashboard.md) | Filial dashboardi: Academic, Teachers, CRM | 3 | 5.2 | S04.01 |
| ✅ | [S05.04](S05.04-kichik-ishlar.md) | Kichik ishlar: AI usage kartasi, doska sinovi, operator 403 | 1 | 5.3+5.4 | — |
| ✅ | [S06.01](S06.01-lead-vaqt-daraja.md) | Lidda qulay vaqt va daraja + tavsiya formulasi | 1 | 6.1 | — |
| ✅ | [S06.02](S06.02-manba-voronka.md) | Manba bo'yicha marketing voronkasi | 1 | 6.2 | S05.03 |
| ✅ | [S06.03](S06.03-email-sms.md) | Email va SMS kanallari | 2 | 6.3 | — |
| ✅ | [S06.04](S06.04-parol-tiklash-otp.md) | "Parolni unutdim" (OTP) | 1 | 6.4 | S01.05, S06.03 |
| ✅ | [S06.05](S06.05-elonlar.md) | E'lonlar | 1.5 | 6.5 | S06.03 (ixtiyoriy) |
| ✅ | [S06.06](S06.06-qidiruv-tolov.md) | Universal Search: to'lov va amallar | 0.5 | 6.6 | — |
| ✅ | [S06.07](S06.07-shikoyatlar.md) | Shikoyatlar oqimi | 1 | 6.7 | — |
| ✅ | [S07.01](S07.01-tashkilot-sozlamalari.md) | Tashkilot sozlamalari | 1.5 | 7.1 | — |
| ✅ | [S07.02](S07.02-ruxsat-matritsa-muharriri.md) | Ruxsat matritsasi muharriri | 2 | 7.2 | — |
| ✅ | [S07.03](S07.03-xona.md) | Xona (Room) entity'si | 1.5 | 7.3 | — |
| ✅ | [S07.04](S07.04-jadval-shabloni.md) | Guruh haftalik jadval shabloni | 1.5 | 7.4 | S07.03, S02.02 |
| ✅ | [S07.05](S07.05-tolov-jadvali.md) | To'lov jadvali (bo'lib to'lash) | 1 | 7.5 | — |
| ✅ | [S08.01](S08.01-tts.md) | Speaking: TTS (savol ovozi) | 2 | 8.1 | — |
| ✅ | [S08.02](S08.02-full-mock-relevance.md) | Speaking: Full Mock + Answer Relevance | 1.5 | 8.2+8.3 | — |
| ✅ | [S08.03](S08.03-fayl-xavfsizligi.md) | Fayl xavfsizligi: ClamAV, magic bytes, yangi formatlar | 1.5 | 8.4 | S03.03, S04.04 |
| ⬜ | [S09.01](S09.01-production-bootstrap.md) | **YANGI** — production bootstrap (migratsiya, rol/ruxsat sinxroni, birinchi Super Admin) | 2 | — | — |
| ⬜ | [S09.02](S09.02-tenant-query-filter.md) | Tenant izolyatsiyasi: global query filter | 2 | 9.1 | S09.01 |
| ⬜ | [S09.03](S09.03-cors-csp-hsts.md) | CORS, CSP, HSTS | 1 | 9.2 | S09.01 |
| ⬜ | [S09.04](S09.04-mfa.md) | MFA (TOTP) | 2 | 9.3 | S01.05 |
| ⬜ | [S09.05](S09.05-sessiya-qurilma.md) | Sessiya va qurilma boshqaruvi | 1 | 9.4 | S01.04 |
| ⬜ | [S09.06](S09.06-s3-minio.md) | Obyekt saqlash: S3/MinIO | 1.5 | 9.5 | S08.03 |
| ⬜ | [S09.07](S09.07-redis.md) | Redis: SignalR backplane + kesh | 1 | 9.6 | — |
| ⬜ | [S09.08](S09.08-qidiruv-indekslari.md) | Qidiruv indekslari (pg_trgm) | 0.5 | 9.7 | — |
| ⬜ | [S09.09](S09.09-backup-dr.md) | 🔴 Backup, PITR, restore testi, DR hujjati | 1.5 | 9.8 | S09.01 |
| ⬜ | [S09.10](S09.10-ci-readme-hangfire.md) | CI, README, Hangfire dashboard himoyasi | 1 | 9.9+9.11+9.12 | — |
| ⬜ | [S09.11](S09.11-frontend-e2e.md) | Frontend e2e testlari (Playwright) | 1.5 | 9.10 | S01.01 |
| ⬜ | [S10.01](S10.01-teacher-kpi.md) | Teacher KPI (sozlanadigan og'irliklar) | 4 | 10.1 | S04.01, S05.01 |
| ⬜ | [S10.02](S10.02-feedback-nps.md) | Feedback / NPS | 3 | 10.2 | S06.03 |
| ⬜ | [S10.03](S10.03-sertifikat.md) | Sertifikatlar + QR tasdiqlash | 3 | 10.3 | — |
| ⬜ | [S10.04](S10.04-gamification-asos.md) | Gamification: XP, daraja, streak, badge | 3 | 10.4a | — |
| ⬜ | [S10.05](S10.05-gamification-challenge.md) | Gamification: challenge va leaderboard | 2 | 10.4b | S10.04 |
| ⬜ | [S10.06](S10.06-interaktiv-oyinlar.md) | Interaktiv o'yinlar + Spaced Repetition | 3 | 10.4c | S10.04 |
| ⬜ | [S10.07](S10.07-hr.md) | HR moduli | 5 | 10.5 | — |
| ⬜ | [S10.08](S10.08-xarajat-cashflow.md) | Xarajatlar va cash flow | 3 | 10.6 | — |
| ⬜ | [S10.09](S10.09-multi-tenant-saas.md) | Multi-tenant SaaS | 5 | 10.7 | S09.02, S07.01 |
| ⬜ | [S10.10](S10.10-advanced-speaking.md) | Advanced Speaking (talaffuz, urg'u, ritm) | 4 | 10.8 | S08.02 |

**Jami S01–S09: ~72 kun. S10 (Faza 6): ~35 kun.** Mobil (Faza 5) va Telegram bot bu jurnaldan tashqari.

---

## 3. Tartib va majburiy bog'liqliklar

```
S00 → S01.01 → S01.02 ─┐
              → S01.03 ─┴→ S02.01 → S02.02 → S03.01 → S03.02
S01.04 (guard) → S01.05 (parol) → S06.04 (OTP) ; S01.04 → S07.02, S09.05 (sessiyalar) ; S01.05 → S09.04 (MFA)
S02.02 → S07.03 (xona) → S07.04 (jadval shabloni)
S02.03 → S04.01 → S04.02 → S04.03 ; S04.02 → S04.05
S01.02 → S04.04 (foto, ota-ona kontakti) → S05.01 → S05.02 (portfolio) → S08.03 (fayl skaneri qamrovi)
S04.01 → S05.03 (filial dashboardi) → S06.02 (voronka shu yerdagi helperni qayta ishlatadi)
S06.03 (Email/SMS) → S06.04, S06.05 (ixtiyoriy), S10.02
S08.02 → S10.10 (advanced speaking) ; S08.03 → S09.06 (S3/MinIO)
S01.01 → S09.11 (frontend e2e)
S09.01 (production bootstrap) — real markazga chiqishdan oldin MAJBURIY; S09.02, S09.03, S09.09 dan oldin
S09.02 → S10.09 (multi-tenant, + S07.01)
S10.01 (KPI) → S10.07 (HR punctuality) ; S10.04 (gamification asos) → S10.05, S10.06
```

Ichida bog'liq bo'lmagan qadamlarni (masalan S02.03, S03.03, S06.01, S06.03, S06.06, S06.07, S07.01, S07.05, S08.01, S09.07, S09.08, S09.10, S10.03, S10.04) istalgan vaqt, alohida sessiyada qilish mumkin.

**Tavsiya etilgan navbat:** S01.04 (bloklash) ni **birinchi** qiling — xavfsizlik teshigi, kichik, qolganlarga tegmaydi.
Undan keyin S01.02 → S01.03 → S01.05, so'ng S02. Real markazga chiqishdan oldin **S09.01 va S09.09 majburiy**.

---

## 4. Sessiya limitini tejash

- **Bitta fayl = bitta yangi chat.** Tugagach `/clear` yoki yangi chat; "compact" qilib davom etmang.
- Har fayldagi **"Boshlashdan oldin o'qing"** ro'yxati — Claude faqat shularni o'qisin (`offset`/`limit` bilan). `docs/00_TZ.md`
  39 KB — hech qachon butunlay o'qitmang; fayllarda kerakli TZ bo'limlari qator oralig'i bilan berilgan.
- Claude'dan "hamma narsani tekshirib chiq" demang; aniq qadamni ayting. Auditni allaqachon qildik.
- Testlar Docker'da ~35 soniya + build. Kod yozib bo'lmasdan turib qayta-qayta ishga tushirmang: avval `build`
  (`bash backend/scripts/dotnet-in-docker.sh build`), keyin bir marta to'liq `test`.
- Brauzerda tekshirish uchun Claude Playwright ishlatadi; bu ham token yeydi — faqat asosiy oqimni tekshirtiring.

---

## 5. Har qadam oxirida (hamma faylga umumiy)

```bash
bash backend/scripts/dotnet-in-docker.sh test                        # hammasi yashil
cd frontend && npx tsc --noEmit && npx eslint src --max-warnings=0   # toza
```
Backend o'zgargan bo'lsa brauzerda ko'rish uchun API'ni qayta ishga tushiring:
`bash backend/scripts/dotnet-in-docker.sh api-stop && bash backend/scripts/dotnet-in-docker.sh api`.

Keyin: (1) `docs/09_audit_progress.md` ga jurnal bandi (1.27, 1.28 …), (2) yangi ruxsat bo'lsa `docs/01_role_permission_matrix.md`
ga qator, (3) shu README dagi jadvalda ✅, (4) **commit — faqat foydalanuvchi so'raganda**, `origin` ga tegmang.

Naqshlar (ruxsat qo'shish, migratsiya, sahifalash, timeline, bildirishnoma, fayl yuklash, test yozish) — [`_naqshlar.md`](_naqshlar.md).

---

## 6. Auditdan keyin qo'shilgan narsalar

| Nima | Qayerdan | Fayl |
|---|---|---|
| Bloklangan foydalanuvchi darhol chiqarilsin, ekran, login xabari | foydalanuvchi talabi | S01.04 |
| O'qituvchi portfoliosi (IELTS, sertifikat, tajriba) | foydalanuvchi talabi | S05.02 |
| **Production'da rollar/ruxsatlar yaratilmaydi**: `DbSeeder` faqat `IsDevelopment()` da ishlaydi (`Program.cs`), migratsiya avtomatik qo'llanmaydi, birinchi Super Admin'ni yaratish yo'li yo'q, `Jwt:SigningKey` dev kaliti bilan ishga tushaveradi | kodni o'qishda topildi | S09.01 |
| `/users` orqali STUDENT roli bilan **yetim hisob** yaratish hali ochiq (S01.01 faqat `/students` yo'lini qo'shdi) | S01.01 dan keyin | S01.02 |
| Lid konvertatsiyasi guruh to'lgan/yopiqligini tekshirmaydi | S01.01 da topildi | S01.03 |
| `docs/11` 7.2 dagi "override allaqachon o'qiladi" faqat **foydalanuvchi** darajasi uchun to'g'ri; rol darajasini `DbSeeder` har startda qayta yozadi | kodni o'qishda topildi | S07.02 |
| Operator student profilini ochsa `GET /students/{id}/finance` 403 | S01.01 brauzer sinovida | S05.04 |
| 🔴 **Filial administratori umumiy filialdagi Super Admin'ni bloklay/tahrirlay/o'chira oladi** (`UsersController.CanTouchUser` rolni tekshirmaydi; `deactivate` testsiz) | S01.04 uchun kodni o'qishda topildi | S01.04 (§3.1 f2) |
