# EduCenter

Til maktablari uchun ERP/CRM/LMS/AI platforma (O'zbekiston). Modulli monolit.

**Javoblar o'zbek tilida.** Kod, kommentariya va commit xabarlari ingliz tilida.

---

## Ishga tushirish

Host'da `dotnet` **ishlamaydi** (Smart App Control imzosiz DLL'ni bloklaydi). Hamma narsa Docker orqali:

```bash
bash backend/scripts/dotnet-in-docker.sh build          # faqat kompilyatsiya, eng tez tekshiruv
bash backend/scripts/dotnet-in-docker.sh test           # ~530 test, alohida educenter_test bazasida
TEST_FILTER="FullyQualifiedName~RoomTests" bash backend/scripts/dotnet-in-docker.sh test   # faqat tanlangan testlar (tez)
bash backend/scripts/dotnet-in-docker.sh migration <Nom> # migration qo'shadi va repoga ko'chiradi
bash backend/scripts/dotnet-in-docker.sh update-db      # dev bazaga qo'llaydi
bash backend/scripts/dotnet-in-docker.sh api            # API :5167 (detached)
bash backend/scripts/dotnet-in-docker.sh api-stop
```

Frontend: `cd frontend && npm run dev -- -p 3002 -H 0.0.0.0`

| Xizmat | Port |
|---|---|
| Frontend | **3002** (3000/3001 band) |
| API | 5167 |
| Postgres | 5433 (host) / educenter-postgres:5432 (docker) |
| Ollama | 11434 |
| TTS (`educenter-tts`, savol ovozi) | 18000 (host) / educenter-tts:8000 — `docker compose up -d tts`, `appsettings.Local.json` da `tts` yoqiladi |
| ClamAV (`educenter-clamav`, fayl skaneri) | 13310 (host) / educenter-clamav:3310 — `docker compose up -d clamav`, `Files:Scanner`; skanersiz prod yuklashni rad etadi |

**Demo login:** `superadmin@demo-center.test` / `Passw0rd!` (`DbSeeder.DemoPassword`).
Boshqa rollar: `admin@`, `operator@`, `teacher@`, `support@`, `student@`, `parent@`, `developer@demo-center.test` — parol bir xil.

`DbSeeder` idempotent va startupda ishlaydi → yangi `PermissionSeedData` granti uchun migration **kerak emas**, faqat API restart.
`api` buyrug'i `Seed__BulkDemoData=true` beradi → `DemoDataSeeder` ~180 talaba, 19 guruh, 521 dars va h.k. qo'shadi
(faqat bayroq bilan: testlar ham `Development` da seed qiladi va qatorlarni sanaydi).

**Frontend'da tugma/havola ko'rsatish:** rol nomi emas, `usePermissions().can("permission.code")` (`lib/use-permissions.ts`).

**Ro'yxat endpointi = sahifalangan.** Yangi `[HttpGet]` ro'yxati `Page<T>` qaytaradi (`query.ToPageAsync(paging, ct)`,
qidiruv `PagingExtensions.ContainsPattern`), frontendda `useDebouncedValue` + `keepPreviousData` + `<Pagination>`.
EF quirklari: matn sifatida saqlanadigan enum massivi uchun `WhereIn(...)` (Contains 500 beradi); DTO konstruktori ustida
`OrderBy`/`Where` ishlamaydi — avval entity'ni saralab sahifalang, keyin project qiling. Hover'ga bog'liq UI yozmang (iPad).

---

## Arxitektura

```
backend/src/
  EduCenter.Api/              controllerlar, Authorization/, Notifications/, Automation/, Ai/, Dashboard/
  EduCenter.Infrastructure/   AppDbContext, Migrations/, Seed/, Auditing/
  EduCenter.Modules.*/Domain/ entity'lar (Academic, Students, Lms, Finance, Crm, Ai, Chat, Identity, …)
  EduCenter.SharedKernel/     TenantEntity, BranchScopedEntity, Page<T>, Authorization/ScopeType
frontend/src/
  app/(admin)/                sahifalar    app/(auth)/login
  components/  components/ui/ dizayn primitivlari
  lib/api/                    API klientlar    lib/nav.ts    lib/format-date.ts    lib/theme.ts
```

.NET 10 · EF Core 10 + Npgsql (pgvector) · Hangfire · SignalR `/hubs/chat`
Next.js 16 App Router (Turbopack) · Tailwind v4 (CSS o'zgaruvchi tokenlar) · TanStack Query · lucide-react

---

## Qat'iy qoidalar

**1. Rol nomi bo'yicha tekshirish taqiqlanadi.** Hech qachon `if (role == "ADMIN")`.
Faqat `[Authorize(Policy = "permission.code")]` + `HttpContext.RequireResolvedScope()`.
Scope'lar: `AllOrganization` / `MyBranches` / `MyGroups` / `AssignedGroups` / `Self` / `MyChildren` / `None`.
Yangi ruxsat → `PermissionSeedData.Grants` ga qator.

**2. Frontend'da rol nomi faqat navigatsiya uchun** (`lib/nav.ts`) — serverda emas.

**3. Sanalar `lib/format-date.ts` orqali.** `toLocaleDateString("uz-UZ", …)` **ishlatilmaydi** —
ICU'da uz-UZ oy nomlari yo'q, `M09 17` chiqadi. Pul: `formatMoney` (`lib/api/finance.ts`).

**4. Rang tokenlari:** `ink-*`, `brand-*`, `brand-solid`, `success/warning/danger/info-*`.
To'ldirish uchun `bg-brand-600` emas, **`bg-brand-solid`** — tungi rejimda ramp teskari bo'ladi.

**5. Git:** `origin` = GitHub, **hech qachon tegilmaydi va force-push qilinmaydi**.
Commit faqat so'ralganda.

**6. Sirlar:** DeepSeek kaliti faqat `appsettings.Local.json` da (git-ignored). Hech qachon commit qilinmaydi.

---

## Tekshirish tartibi

```bash
cd frontend && npx tsc --noEmit && npx eslint src --max-warnings=0
bash backend/scripts/dotnet-in-docker.sh test
```

ESLint qattiq: `react-hooks/purity` (render ichida `Date.now()` mumkin emas — `useNow()` ishlating),
`react-hooks/set-state-in-effect`, `react/no-unescaped-entities` (`&apos;`).

---

## Holat

Faza 1–4 asosan tayyor. **Ish `docs/21.09-audit-jurnali/` dan olinadi** — har bosqich (S01.01, S01.02, …)
alohida faylda, bitta Claude sessiyasi uchun mustaqil vazifa qilib yozilgan (maqsad, kodning hozirgi holati,
nima yozish kerak, testlar, qabul mezoni). Yangi sessiyani o'sha papkadagi `README.md` bilan boshlang — u holat
jadvali va navbatni ko'rsatadi. `docs/11_implementation_plan.md` va `docs/10_full_audit_2026-09-21.md` — shu
jurnalning manbasi (asl audit va reja), endi ish undan emas, jurnaldan olinadi.

- `docs/00_TZ.md` — texnik topshiriq
- `docs/01_role_permission_matrix.md` — ruxsat matritsasi
- `docs/09_audit_progress.md` — bajarilgan ishlar jurnali (1.1–1.25)
- `docs/08_project_audit_report.md` — tashqi audit (bir nechta bandi eskirgan, 09 da izohlangan)

**Bloklangan:** Telegram bot (token yo'q) · GitLab `192.168.10.105` (SSH/HTTPS yo'q) · Flutter (Faza 5)

---

## Internetdagi nusxa (prod)

`https://desktop-nu50l13.tail63dd24.ts.net/` — Tailscale Funnel → Caddy gateway → api/web/db (`deploy/`, alohida
docker-compose, dev stackdan mustaqil). **To'liq qo'llanma: `docs/22_deploy_internet.md`.**

- Yangilash: `bash deploy/scripts/deploy.sh` · tekshirish: `bash deploy/scripts/verify.sh [URL]` · zaxira: `deploy/scripts/backup.sh`.
- Prod'da **`Passw0rd!` hech qachon ishlamaydi**. `Development` bo'lmagan muhitda `ProductionBootstrapper` (bo'sh bazada) ikki rejimdan
  birini bajaradi: `BOOTSTRAP_MODE=Demo` — dev'dagi demo markaz, lekin `DEMO_PASSWORD` (tasodifiy, `secrets/demo-accounts.txt`) bilan;
  `Clean` — bitta tashkilot + bitta SuperAdmin. Almashtirish: `deploy.sh reset --demo|--clean --yes` (bazani o'chiradi, avval zaxira oladi).
  Prod'ni `Development` muhitida ishga tushirmang; demo va haqiqiy ma'lumotni bitta bazada aralashtirmang (ko'p-tenant testlanmagan).
- `deploy/.env`, `deploy/secrets/`, `deploy/backups/` **hech qachon commit qilinmaydi** (git-ignored). Sirlar image'ga tushmaydi (`.dockerignore`).
- Yangi anonim endpoint qo'shish — xavfsizlik qarori: `AnonymousSurfaceTests` ro'yxatini ataylab o'zgartirish kerak.
- Foydalanuvchi kiritgan havola `href`ga tushsa: server `HttpUrlPolicy`, frontend `lib/safe-url.ts` (faqat http/https).
- `funnel.sh on/off` — internetga ochish/yopish; faqat foydalanuvchi so'raganda ishlating.

---

## Token tejash

Kontekst har so'rovda qayta yuboriladi, shuning uchun uzun sessiya qimmat:

- Faylni **`Edit` toolida** tahrirlang. `python`/`sed` bilan tahrirlash tizimni butun faylni
  kontekstga qayta chop etishga majbur qiladi.
- Faylning kerakli qismini o'qing (`offset`/`limit`), butunini emas.
- Screenshot `fullPage` emas, viewport.
- Bitta vazifa = bitta sessiya. Tugagach yangi chat, avvalgisini compact qilib davom etmang.
