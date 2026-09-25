# INTERNETGA CHIQARISH (Tailscale Funnel)

**Sana:** 2026-09-24
**Manzil:** `https://desktop-nu50l13.tail63dd24.ts.net/`
**Papka:** `deploy/` (compose, Dockerfile'lar, Caddyfile, skriptlar)

Bu hujjat: tizim qanday joylangan, nima qanday himoyalangan, kundalik ishlar (yangilash, zaxira, to'xtatish)
va qolgan xavflar. Hamma buyruq Git Bash'da, loyiha ildizidan.

---

## 1. Sxema

```
Internet ──TLS──▶ Tailscale Funnel (bu kompyuter)
                       │  http, faqat 127.0.0.1:8088
                       ▼
                  ┌─ gateway (Caddy) ─────────────────────────────┐
                  │  /api/*  /hubs/*  → api:8080   (ASP.NET Core)  │
                  │  boshqa hamma     → web:3000   (Next.js)       │
                  └────────────────────────────────────────────────┘
                       api ──▶ db (Postgres+pgvector, faqat ichki tarmoq)
                       api ──▶ clamav (yuklangan fayllar skaneri, :3310)
                       api ──▶ internet (DeepSeek) va bu kompyuterdagi Ollama/Whisper
```

- Frontend va API **bitta origin**da (gateway) — brauzer hech qachon boshqa originga so'rov yubormaydi, CORS umuman kerak emas.
- Tashqaridan ochiq **bitta port**: `127.0.0.1:8088` (faqat shu kompyuterning loopback'i). LAN va tailnet unga to'g'ridan-to'g'ri
  kira olmaydi, faqat Funnel orqali.
- `db` va `web` **internetga chiqish yo'li yo'q** tarmoqda (`internal: true`).

## 2. Nima qanday himoyalangan

| Qatlam | Nima qilingan |
|---|---|
| **Sirlar** | JWT kaliti, DB parollari, birinchi admin paroli — `deploy/.env` (git-ignored), `deploy.sh` tasodifiy yaratadi. Repodagi `dev-only…` kalit va `educenter_dev_only` paroli prod'da **ishlamaydi**: API shunday sozlama bilan umuman ishga tushmaydi (`ProductionSettingsGuard`). DeepSeek kaliti image ichiga tushmaydi (`.dockerignore` + build'dagi tekshiruv). |
| **Demo hisoblar** | Prod'da dev'dagi ochiq parol `Passw0rd!` **hech qachon ishlamaydi**: demo rejimda hamma demo hisob deploy'ga xos tasodifiy parolni oladi (API `Passw0rd!` bilan ishga tushmaydi ham — `ProductionSettingsGuard`). Aks holda internetdagi har kim `superadmin@demo-center.test` bo'lib kira olardi. Ikki rejim bor — 3-bo'limga qarang. |
| **Autentifikatsiya** | Har bir hisobga 5 ta noto'g'ri paroldan keyin 15 daqiqalik qulf (taqsimlangan taxminga qarshi); IP bo'yicha limit; mavjud bo'lmagan email uchun ham xuddi shuncha vaqt ketadi (email taxmin qilib bo'lmaydi); JWT faqat HS256, 30 soniya clock-skew; `/api/auth/*` tanasi ≤16 KB. |
| **Haqiqiy IP** | Tailscale mijoz `X-Forwarded-For`ini o'zi yozadi (soxtasi o'chiriladi — sinab ko'rilgan); gateway va API uni faqat docker tarmog'idan qabul qiladi. Shu tufayli tezlik limiti har bir tashrif buyuruvchiga alohida (aks holda hamma bitta IP bo'lardi). Limitlar: 1200/daq umumiy, 30/daq login (maktab — bitta NAT), 20/daq AI (foydalanuvchiga). |
| **Brauzer** | Nonce'li qat'iy CSP (`src/proxy.ts`): inline skript, `javascript:` havola, begona originga so'rov, framing — bloklanadi. HSTS, `nosniff`, `X-Frame-Options: DENY`, `Permissions-Policy` (faqat mikrofon), COOP/CORP. Tokenlar localStorage'da bo'lgani uchun CSP asosiy himoya. |
| **Yuklangan fayllar** | Kengaytma ro'yxati (HTML/SVG yo'q), tasodifiy nom, content-type serverdan; API javoblariga `CSP: sandbox` + `no-store`. Havolalar (Drive URL) faqat `http(s)` — **topilgan zaiflik yopildi**: uy vazifasi `contentUrl` avval `javascript:` qabul qilardi. **Har fayl saqlashdan oldin:** mazmuni kengaytmasiga mos kelishi (magic bytes: `.exe` ni `.pdf` deb nomlasa o'tmaydi), zip-bomba chegarasi (≤ 1000 fayl, ochilgach ≤ 50 MB) va **ClamAV** skaneri (`clamav` servisi, `Files__Scanner__*`). Skaner javob bermasa (hali signaturalarni yuklayotgan bo'lsa yoki to'xtagan bo'lsa) yuklash **rad etiladi** — fayl tekshirilmasdan saqlanmaydi (`Files:Scanner:Required=true`, prod'da o'chirib bo'lmaydi: `ProductionSettingsGuard`). |
| **Konteynerlar** | Hammasi root emas, `read_only`, `cap_drop: ALL`, `no-new-privileges`, xotira/pid limiti, log hajmi cheklangan. API'ning yagona yoziladigan joyi — `App_Data` volume'i. **Istisno — `clamav`:** yoziladigan fayl tizimi va o'z imzolar bazasi (`clamdb` volume'i, ~300 MB) kerak, va u begona fayllarni ochadigan yagona servis; `no-new-privileges`, 3 GB xotira va pid limiti bilan cheklangan. |
| **Baza** | Ilova superuser emas, faqat o'z bazasining egasi bo'lgan rol bilan ulanadi. Portsiz. |
| **Loglar** | SignalR tokeni (`?access_token=`), `Authorization`, Tailscale'ning foydalanuvchi email/ismi loglarga yozilmaydi. |
| **Ochiq yuza** | Anonim faqat `login/refresh/logout` (+ ichki `/healthz`, gateway uni chiqarmaydi). Bu `AnonymousSurfaceTests` testi bilan qotirilgan: yangi anonim endpoint qo'shilsa test yiqiladi. Swagger/OpenAPI/Hangfire dashboard prod'da o'chiq. |

## 3. Ikki rejim: demo va toza

Bo'sh bazaga nima yoziladi — `deploy/.env` dagi `BOOTSTRAP_MODE` hal qiladi. **Hozir: `Demo`.**

| Rejim | Ichida nima | Kirish |
|---|---|---|
| **Demo** | dev'dagi demo markaz: 8 rol hisobi + ~14 o'qituvchi, ~180 o'quvchi va ota-onalar, 19 guruh, ~520 dars (davomat, uy vazifa, imtihon), lidlar, ~570 hisob-faktura. Dev bazadan nusxa **emas** — ilovaning o'z seeder'i (sintetik ma'lumot) ishlaydi. | `deploy/secrets/demo-accounts.txt`: 8 ta email (`superadmin@demo-center.test`, `teacher@…`, `student@…`, `parent@…` …) va **hammasi uchun bitta tasodifiy parol**. |
| **Toza** | bitta tashkilot, bitta filial, bitta SuperAdmin (`admin@educenter.local`), boshqa hech narsa | `deploy/secrets/first-login.txt`: vaqtinchalik parol, birinchi kirishda almashtirishga majbur. |

Rejimni almashtirish (yoki demo ma'lumotni qayta yuklash) — bazani **o'chiradi**, shuning uchun avval o'zi zaxira oladi va `--yes`siz ishlamaydi:

```bash
bash deploy/scripts/deploy.sh reset --demo --yes    # demo markaz, yangi tasodifiy parol
bash deploy/scripts/deploy.sh reset --clean --yes   # toza, bitta egasi bilan (haqiqiy ishga o'tishda)
```

Demo parolni kimga berishni o'zingiz hal qilasiz: **parol + email = o'sha hisob.** SuperAdmin hamma narsani o'zgartira/o'chira oladi va
AI xarajatini ishlatadi — ko'pchilikka o'quvchi/o'qituvchi/ota-ona hisobini bering. Har bir hisob parolini o'zi almashtira oladi.
Ochiq (`Passw0rd!`) demo xohlasangiz — bu internetdagi har kimga SuperAdmin berish demak; men buni o'zboshimcha qilmadim.

> Demo tashkilotning sanalari yaratilgan paytga nisbatan (5 hafta oldin … 3 hafta keyin), vaqt o'tgach eskiradi — `reset --demo` yangilaydi.
> Toza rejimda zaxira SuperAdmin yarating: yagona SuperAdmin parolini unutsa, tiklashning oddiy yo'li yo'q.

## 4. Kundalik ishlar

```bash
bash deploy/scripts/deploy.sh            # yig'ish + ishga tushirish; kod o'zgargach ham shu (yangilash)
bash deploy/scripts/deploy.sh status     # konteynerlar + Funnel holati
bash deploy/scripts/deploy.sh logs api   # jonli log (api | web | gateway | db)
bash deploy/scripts/deploy.sh down       # to'xtatish (ma'lumot saqlanadi)
bash deploy/scripts/deploy.sh reset --demo|--clean --yes   # bazani o'chirib boshidan (avval zaxira oladi) — 3-bo'lim

bash deploy/scripts/funnel.sh on         # internetga chiqarish   (FORCE=1 — mavjud boshqa serve/funnel'ni almashtiradi)
bash deploy/scripts/funnel.sh off        # internetdan olish (stack ishlayveradi, faqat shu kompyuterdan ko'rinadi)
bash deploy/scripts/funnel.sh test-on    # faqat tailnet uchun https://…:8443 (nashrdan oldin sinash)

bash deploy/scripts/verify.sh [URL]      # qora quti xavfsizlik tekshiruvi (45 ta tekshiruv), 0 = hammasi joyida
bash deploy/scripts/backup.sh            # deploy/backups/ ga db-*.dump + files-*.tar.gz (14 kundan eskilari o'chadi)
```

**Zaxirani tiklash (baza):**
`docker compose --env-file .env -f docker-compose.yml exec -T db pg_restore -U postgres -d educenter --clean --if-exists < backups/db-<vaqt>.dump`
(`deploy/` ichidan). Dump'larda o'quvchilarning shaxsiy ma'lumotlari bor — bu kompyuterdan tashqariga shifrlamasdan chiqarmang.

**Avtomatik zaxira:** Windows Task Scheduler'da kuniga bir marta `bash deploy/scripts/backup.sh` (ishga tushirish buyrug'i
`"C:\Program Files\Git\bin\bash.exe" -lc "cd '/d/New proj' && bash deploy/scripts/backup.sh"`). Hozir avtomatik **emas** —
qo'lda ishga tushiriladi.

**Eski nashr (2026-09-24 da o'chirilgan).** Bu kompyuterda Funnel/Serve sozlamasi bitta jadval: `funnel.sh on` avvalgi
holatni `deploy/secrets/previous-serve-config.json` ga saqlab, o'rniga gateway'ni qo'ydi. Avval nashr etilgan edi:
`/` → `127.0.0.1:5173`, `/api` → `127.0.0.1:3001/api`, `/sozyodlash` → `127.0.0.1:3000` (o'sha node jarayonlari hali ishlab turibdi,
faqat internetdan ko'rinmaydi). Ikkalasini birga ushlab turib bo'lmaydi (`/` ni gateway egallaydi); qaytarish uchun:

```bash
bash deploy/scripts/funnel.sh off
tailscale funnel --bg --set-path=/ http://127.0.0.1:5173
tailscale funnel --bg --set-path=/api http://127.0.0.1:3001/api
tailscale funnel --bg --set-path=/sozyodlash http://127.0.0.1:3000
```

## 5. Sirlarni almashtirish

| Nima | Qanday |
|---|---|
| JWT kaliti (hamma tizimdan chiqadi) | `deploy/.env`da `JWT_SIGNING_KEY=` ni `openssl rand -hex 48` bilan almashtiring → `docker compose --env-file .env -f docker-compose.yml up -d api` |
| DB paroli | `docker compose … exec db psql -U postgres -c "ALTER ROLE educenter_app PASSWORD '<yangi>'"` → `.env`da `APP_DB_PASSWORD` → `up -d api` |
| DeepSeek kaliti | `.env`da `DEEPSEEK_API_KEY` → `up -d api` |

**`deploy/.env` ni xavfsiz joyga nusxalab qo'ying.** Undagi DB paroli baza volume'idan tiklanmaydi.

## 6. Qolgan xavflar (halol ro'yxat)

1. **Kompyuter o'chsa yoki uyquga ketsa, sayt o'chadi.** Hozir Windows uxlamaydi (AC uyqu = 0) va Docker Desktop
   `AutoStart` yoqilgan, konteynerlar `restart: unless-stopped`, Funnel sozlamasi (`--bg`) qayta ishga tushganda saqlanadi.
   Lekin Docker Desktop **foydalanuvchi tizimga kirganda** ishga tushadi — kompyuter o'zi qayta yuklansa (yangilanish, elektr),
   Windows avto-kirish sozlanmagan bo'lsa, kimdir kirmaguncha sayt o'chiq turadi.
2. **Bu kompyuterdagi boshqa xizmatlar LAN/tailnet'ga ochiq** (Funnel orqali emas, lekin xavfli): dev Postgres `:5433` (parol
   `educenter_dev_only`), dev API `:5167` (demo hisoblar, `Passw0rd!`), Ollama `:11434` (autentifikatsiyasiz), Whisper `:19000`,
   `lcsystem-*` Postgres/Redis/MinIO, Samba `:1445` va **DVWA `:8081`** (ataylab zaif ilova). Ularni `127.0.0.1:` ga bog'lash tavsiya
   etiladi. Prod stack ularga tegmaydi (faqat Ollama/Whisper'ga `host.docker.internal` orqali murojaat qiladi).
3. **Tokenlar localStorage'da.** CSP XSS'ni to'sadi, lekin HttpOnly cookie'dan kuchsizroq. Kelajakda cookie'ga o'tkazish mumkin.
4. **Parol siyosati** — kamida 8 belgi, murakkablik talabi yo'q (testlar shunga bog'liq). Hisob qulfi va IP limiti bilan qoplanadi.
5. **Bitta nusxa.** SignalR guruhlari xotirada; ko'p nusxaga o'tilsa backplane kerak. Funnel'ning o'z trafik limiti bor.
6. **`SuperAdmin` — bitta hisob.** Yuqoridagi zaxira admin tavsiyasiga qarang.
7. **Demo parol — bitta umumiy sir.** Kimga bersangiz, o'sha 267 hisobning istalganiga kira oladi. Tarqalsa: `reset --demo --yes`
   (yangi parol). Demo tashkilot va haqiqiy ma'lumotni bitta bazada **aralashtirmang** — tizimda tenant-filtr har so'rovda qo'lda
   tekshiriladi va ikki tashkilotli holat testlanmagan; shuning uchun demo va haqiqiy ish alohida rejimlarda.
8. **AI xarajati.** DeepSeek yoqilgan; foydalanuvchi boshiga 20 so'rov/daq va tashkilot limitlari bor, lekin oylik byudjet chegarasi yo'q.

## 7. Muammolar

| Belgi | Sabab / yechim |
|---|---|
| `deploy.sh`: `gateway did not become healthy` | `deploy.sh logs gateway`. Caddyfile sintaksisi: `docker run --rm -v "$(pwd -W)/deploy/Caddyfile:/etc/caddy/Caddyfile" caddy:2-alpine caddy validate --config /etc/caddy/Caddyfile` |
| API ishga tushmaydi: `Refusing to start with unsafe settings` | `.env`dagi qiymat yetishmayapti (JWT kaliti ≥32 belgi, `AllowedHosts` aniq nom). Xabar hammasini sanab beradi. |
| Sayt `421 Misdirected request` | Boshqa nom bilan kirilyapti. Faqat `PUBLIC_HOST` (`.env`) va `localhost` qabul qilinadi. |
| Hamma `429` oladi | Tezlik limiti; `RateLimiting__*` (docker-compose.yml) va gateway `client_ip` to'g'ri kelayotganini tekshiring (`deploy.sh logs gateway`). |
| Hisob qulflandi | 15 daqiqa kuting yoki API'ni qayta ishga tushiring (`up -d --force-recreate api`; qulflar xotirada). |
| AI javob bermaydi | Dev stackdagi `educenter-ollama` / `educenter-whisper` ishlayaptimi (`docker ps`); prod API ularga `host.docker.internal:11434/19000` orqali murojaat qiladi. |
| Fayl yuklanmaydi: "Fayllarni xavfsizlik tekshiruvidan o'tkazib bo'lmadi" | `clamav` servisi hali tayyor emas (birinchi ishga tushirishda ~1–2 daqiqa imzolar bazasini yuklaydi) yoki to'xtagan: `docker compose -p educenter-prod ps clamav` (healthy bo'lishi kerak), `deploy.sh logs clamav`. Tayyor bo'lguncha yuklashlar ataylab rad etiladi. Ovozli savol (TTS) prod'da standart **o'chiq** (`Ai__Providers__tts__Enabled`) — savollar matn bo'lib qoladi. |

## 8. Fayllar

```
deploy/
  docker-compose.yml     stack (db, api, web, gateway), tarmoqlar, cheklovlar
  Caddyfile              gateway: marshrutlar, sarlavhalar, limitlar, log filtri
  api.Dockerfile  web.Dockerfile
  db/init-app-role.sh    cheklangan DB rolini yaratadi (faqat birinchi marta)
  scripts/deploy.sh  funnel.sh  backup.sh  verify.sh
  .env  secrets/  backups/   ← git-ignored
backend/src/EduCenter.Api/Deployment/   ProductionSettingsGuard, ProductionBootstrapper, HealthProbe
backend/src/EduCenter.Api/Auth/         LoginThrottle, HttpUrlPolicy
frontend/src/proxy.ts                   CSP (nonce)
```
