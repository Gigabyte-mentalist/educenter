# MAVJUD NAQSHLAR — har qadam shunga tayanadi

Hammasi kodda tekshirilgan (2026-09-21). Yangi kod **shu naqshlarga o'xshasin** — o'rganilgan uslubni buzmang.
Fayl yo'llari `backend/src/...` ga nisbatan; qator raqamlari ataylab berilmagan (siljiydi) — belgi (metod/klass) nomi bilan qidiring.

---

## 1. Ruxsat qo'shish

1. `EduCenter.Infrastructure/Seed/PermissionSeedData.cs` → `Grants` massiviga qator:
   `Grant("students.edit", "students", "edit", RoleCode.SuperAdmin, ScopeType.AllOrganization),` (har rol uchun alohida qator).
   Izoh yozing — **nega** shu rolga shu scope.
2. `docs/01_role_permission_matrix.md` ga jadval qatori (mavjud qatorlar shaklida).
3. Endpoint: `[Authorize(Policy = "students.edit")]` + metod ichida `var scope = HttpContext.RequireResolvedScope();`.
4. **Migration kerak emas**: `DbSeeder` startda yangi grantni bazaga qo'shadi (faqat Development va testlarda — production
   uchun `S09.01` ga qarang). Dev'da ko'rish uchun API'ni qayta ishga tushiring.
5. Rol nomi bilan tekshirish **taqiqlangan** (`if (role == "ADMIN")` yo'q). Faqat ruxsat kodi va scope.
   Bitta ruxsat "qisman" bo'lishi kerak bo'lsa (masalan "operator faqat ism/telefon") — **alohida kod** (misol:
   `finance.debt.view`, `students.contact.edit`).

Scope'lar (`SharedKernel/Authorization/TenantContext.cs`): `AllOrganization`, `MyBranches`, `MyGroups`, `AssignedGroups`,
`Self`, `MyChildren`, `None`. `ScopeType.None` "ruxsat yo'q" degani (`MeController` uni ro'yxatdan chiqaradi).

Scope yordamchilari (`EduCenter.Api/Authorization/`):
| Yordamchi | Nima qiladi |
|---|---|
| `scope.CanAccessStudentAsync(db, studentId)` | student scope ichidami (`StudentAccess.cs`). **MyBranches uchun student faqat filial guruhiga yozilgan bo'lsa ko'rinadi** |
| `scope.ScopedStudentIds(db)` | scope ichidagi barcha student id lari (query) |
| `scope.CanAccessGroupAsync(...)` / `scope.VisibleGroupIdsAsync(db)` | guruh uchun (`GroupAccess.cs`) |
| `scope.AllowedBranchIds(orgBranchIds)` | AllOrg → hamma filial; MyBranches → o'zinikilar; boshqasi → `null` (`ScopeFilter.cs`) |
| `EnrollmentRules.CheckAsync(...)` | guruhga o'quvchi qo'shish mumkinmi: mavjud, scope, yopilmagan, o'rin, takror (`Students/EnrollmentRules.cs`) |
| `StudentNumbers.NextAsync(db)`, `PasswordPolicy.Validate(pw)` | student raqami; parol qoidasi |

## 2. Domen, migratsiya

- Entity: `Modules.<X>/Domain/`. Asos klass: `TenantEntity` (`OrganizationId`) yoki `BranchScopedEntity` (+`BranchId`) —
  `SharedKernel/BaseEntity.cs`. Setterlar `private`, o'zgarish metodlar orqali; `MarkUpdated()` chaqiring.
  Bo'sh `private Entity() { }` (EF uchun) va domen konstruktori.
- EF konfiguratsiya: `Modules.<X>/Infrastructure/<X>Configurations.cs` (`IEntityTypeConfiguration<T>`; `ApplyConfigurationsFromAssembly`
  o'zi topadi). Enum → `.HasConversion<string>().HasMaxLength(20)`; `HasMaxLength`, `HasIndex`. **Enum'ga yangi a'zo qo'shish
  migratsiya talab qilmaydi** (matn sifatida saqlanadi).
- `EduCenter.Infrastructure/AppDbContext.cs` ga `public DbSet<X> Xs => Set<X>();`.
- Migratsiya: `bash backend/scripts/dotnet-in-docker.sh migration <PascalCaseNom>` (fayllarni repoga ko'chiradi),
  keyin dev bazaga: `bash backend/scripts/dotnet-in-docker.sh update-db`. Testlar o'zi `educenter_test` bazasida `Migrate()` qiladi.
- Auditga tushishi kerak bo'lgan entity → `Infrastructure/Auditing/AuditSaveChangesInterceptor.cs` dagi `AuditedTypes` ga qo'shing
  (`PasswordHash` avtomatik yashiriladi).
- **Host'da `dotnet` ishlamaydi** (Smart App Control) — faqat `dotnet-in-docker.sh` (`build`, `test`, `migration`, `update-db`, `api`, `api-stop`).

## 3. Ro'yxat endpointi (sahifalash)

```csharp
public async Task<ActionResult<Page<XDto>>> List([FromQuery] string? search, [FromQuery] PageRequest? paging, CancellationToken ct = default)
{
    ...
    if (!string.IsNullOrWhiteSpace(search)) { var term = PagingExtensions.ContainsPattern(search); query = query.Where(x => EF.Functions.ILike(x.Name, term)); }
    return Ok(await query.OrderBy(...).ThenBy(x => x.Id).Select(x => new XDto(...)).ToPageAsync(paging, ct));
}
```
- `PageRequest`: `page`, `size` (≤ 200, standart 25). Matn sifatida saqlangan enum massivi uchun `WhereIn(...)` (`Contains` 500 beradi).
- DTO **konstruktori ustida** `OrderBy`/`Where` ishlamaydi — avval entity'ni saralab sahifalang, keyin `Select` qiling.
- Frontend: `lib/api/query.ts` (`buildQuery`, `DEFAULT_PAGE_SIZE`), `useDebouncedValue`, `useQuery({ placeholderData: keepPreviousData })`,
  `<Pagination>`. Namuna: `app/(admin)/students/page.tsx`.

## 4. Hodisa: timeline + bildirishnoma + audit

```csharp
db.StudentTimelineEvents.Add(new StudentTimelineEvent(scope.OrganizationId, studentId, TimelineEventType.Xxx, JsonSerializer.Serialize(new { ... })));
await notifications.NotifyAsync(scope.OrganizationId, await recipients.StudentAndParentsAsync(studentId),
    new NotificationMessage(NotificationType.Xxx, "Sarlavha", "Matn", "/dashboard"), ct);
await db.SaveChangesAsync(ct);   // bildirishnoma qatorlari SHU saqlash bilan birga yoziladi
```
- Yangi tur: `Modules.Timeline/Domain/StudentTimelineEvent.cs` (`TimelineEventType`) va
  `Modules.Notifications/Domain/Notification.cs` (`NotificationType`) ga `const string`. Frontend: `EVENT_CONFIG`
  (`app/(admin)/students/[id]/page.tsx`) ga yorliq va ikonka.
- `NotificationRecipients` (`Api/Notifications/`): `StudentUserAsync`, `ParentsAsync`, `StudentAndParentsAsync`, `GroupStudentsAsync`,
  `GroupTeachersAsync`, `BranchAdministratorsAsync`, `StudentNameAsync`, `UserNameAsync`.
- `NotifyAsync` foydalanuvchi o'chirib qo'ygan turni o'tkazib yuboradi va `dedupeKey` bo'yicha takrorni yubormaydi.

## 5. Fayl yuklash

Namuna: `Api/Students/StudentDocumentFileStorage.cs` + `StudentDocumentsController.Upload`.
`Validate(IFormFile)` → `SaveAsync` (tasodifiy kalit + asl kengaytma, `AllowedTypes` oq ro'yxati) → entity → `SaveChanges`,
xato bo'lsa `files.Delete(key)`. `[RequestSizeLimit]`, ko'rsatish endpointi bearer token bilan (`<img src>` ishlamaydi —
frontendda `apiFetchBlob`). Sozlama: `Program.cs` da `AddScoped<...Storage>()` + `Configure<...Options>(GetSection(...))`.
Frontend: `FormData` (`uploadStudentDocument` — `lib/api/students.ts`). Hozircha hamma fayl `App_Data/` (git-ignored, docker volume
`educenter-app-data`).

## 6. Frontend

- Tugma/havola ko'rsatish: `const { can, scopeOf } = usePermissions();` → `can("students.edit")` (`lib/use-permissions.ts`).
  Rol nomi faqat navigatsiya uchun (`lib/nav.ts`).
- Primitivlar (`components/ui/`): `Drawer`, `Modal`, `Button` (`variant`, `size`, `loading`), `Field` (`Input`, `Label`, `Select`, `Textarea`),
  `Alert`, `Badge`, `Table`, `Tabs`, `PageHeader` (`actions` prop), `EmptyState`, `Pagination`, `StatTile`, `ConfirmDialog`.
- Naqsh: forma `Drawer` ichida, `useMutation` → `onSuccess: queryClient.invalidateQueries({ queryKey: ["students"] })`. Xatoni
  `ApiError.message` bilan ko'rsating (server `{ error: "..." }` yuboradi). Namuna: `components/StudentCreateDrawer.tsx`, `StudentEdit.tsx`,
  `StudentLifecycle.tsx`.
- Sana: `lib/format-date.ts` (`toLocaleDateString("uz-UZ")` **ishlatilmaydi** — oy nomlari chiqmaydi). Pul: `formatMoney` (`lib/api/finance.ts`).
- Ranglar: `ink-*`, `brand-*`, **`bg-brand-solid`** (to'ldirish uchun; `bg-brand-600` emas), `success/warning/danger/info-*`.
- ESLint qattiq: `react-hooks/purity` (renderda `Date.now()`/`Math.random()`/`new Date()` yo'q — `useNow()`), `react-hooks/set-state-in-effect`,
  `react/no-unescaped-entities` (`'` → `&apos;`). Hover'ga bog'liq UI yozmang (iPad).
- API klient: `lib/api/<modul>.ts` — `apiFetch<T>(path, { method, body: JSON.stringify(...) })`; enum'lar **matn** ("Active").

## 7. Test yozish

Namuna: `backend/tests/EduCenter.Api.IntegrationTests/StudentAdminTests.cs`, `EnrollmentLifecycleTests.cs`.
- `[Collection("Api")] public class XTests(EduCenterWebApplicationFactory factory)`; har testda `AuthenticatedClientAsync(email)`
  (private yordamchi: login qilib `Authorization: Bearer` qo'yadi). Parol hammada `DbSeeder.DemoPassword` ("Passw0rd!").
- Demo foydalanuvchilar (`@demo-center.test`): `superadmin` (Chilonzor+Yunusobod), `admin` (faqat Chilonzor), `operator` (ikkalasi),
  `teacher` (ikkalasi), `support`, `student`, `parent`, `developer`.
- Boshqa filial holati: superadmin `GET /api/branches` dan admin ko'rmaydigan filialni topib, u yerda guruh yaratadi (`StudentAdminTests.GroupInOtherBranchAsync`).
- Test DTO'lar har fayl ichida `private record` (server JSON'ining qismi; ortiqcha maydonlar e'tiborsiz). Sahifalangan javob: `PagedResponse<T>` (`.Items`).
- Har yangi endpoint uchun: muvaffaqiyat, validatsiya (400), takror/holat (409), **ruxsatsiz rol (403)**, **boshqa filial (403)**.
- SignalR sinovi: `ChatTests.ConnectAsync` (LongPolling, `factory.Server.CreateHandler()`).
- Testlar `Development` muhitida seed qiladi — `Seed:BulkDemoData` ni **yoqmang** (qatorlarni sanaydigan testlar buziladi).
- Tashqi AI provayderlar testda o'chiq (`Ai__AllowFakeProvider`); haqiqiy DeepSeek'ni chaqirtirmang.

## 8. Brauzerda tekshirish (Playwright)

- Dev serverlar: frontend `:3002`, API `:5167`. Backend o'zgargan bo'lsa API'ni qayta ishga tushiring (`api-stop`, `api`).
- Playwright MCP boshqa oynada band bo'lishi mumkin ("Browser is already in use") — **uni to'xtatmang**. O'rniga alohida profil bilan:
  `require('C:/Users/Administrator/AppData/Local/npm-cache/_npx/9833c18b2d85bc59/node_modules/playwright-core')`,
  `chromium.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true })`.
  Skriptni scratchpad'ga yozing. Login: `#email`, `#password`, `button[type=submit]` → `/dashboard`.
- Ekran rasmini o'qish uchun `Read` (PNG) ishlaydi; viewport, `fullPage` emas.
- **Dev bazaga qoldiq qoldirmang yoki qoldirsangiz aniq nom bering** ("Sinov …") va foydalanuvchiga ayting. O'quvchi kartasi bor
  foydalanuvchini o'chirib bo'lmaydi (faqat bloklanadi) — SQL bilan aylanib o'tmang. Dev bazani reset qilishdan oldin so'rang.

## 9. Ma'lumotlar bazasi va muhit

- `educenter` — dev (API shuni ishlatadi, `Seed:BulkDemoData=true` bilan ~180 student); `educenter_test` — testlar. Postgres `:5433`.
- Sirlar (DeepSeek kaliti) faqat `appsettings.Local.json` da (git-ignored). Hech qachon commit qilinmaydi.
- `origin` (GitHub) va `gitlab` remote'lariga **tegilmaydi**, force-push yo'q. Commit xabari inglizcha, "nima va nega" nasr uslubida
  (`git log -5` ga qarang), oxirida harness bergan `Co-Authored-By` qatori.
