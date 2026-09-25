# EduCenter Frontend

Next.js 16 (App Router) + TypeScript + Tailwind v4. Backend: `../backend` (ASP.NET Core / .NET 10).

## Ishga tushirish

```bash
cd backend && docker compose up -d   # local Postgres (port 5433)
cd backend && dotnet run --project src/EduCenter.Api   # http://localhost:5167

cd frontend && npm install
npm run dev   # http://localhost:3000 (yoki band bo'lsa keyingi bo'sh port)
```

Backend `Development` muhitida ishga tushganda demo ma'lumotlar avtomatik seed qilinadi (8 rol, demo Organization + 2 Branch, har roldan 1 demo user). Login uchun: `superadmin@demo-center.test` / `Passw0rd!` (boshqa demo emaillar `backend/src/EduCenter.Infrastructure/Seed/DbSeeder.cs`da).

## Dizayn tizimi

`docs/00_TZ.md` §27 talabiga muvofiq — professional, ma'lumotga boy admin interfeysi — mustaqil dizayn tizimi qurilgan (default Tailwind ko'rinishidan qasddan uzoqlashtirilgan):

- **Ranglar** (`src/app/globals.css`): teal asosiy rang (`brand-*`), ochra urg'u rangi (`accent-*`, faqat highlight uchun), issiq kulrang neytrallar (`ink-*`) — sof kulrang emas. Semantik ranglar (success/warning/danger/info) urg'u rangidan ataylab ajratilgan.
- **Tipografiya** (`next/font/google`, `src/app/layout.tsx`): sarlavhalar — **Sora**, matn — **IBM Plex Sans**, raqamli ma'lumotlar (ID, vaqt, xona) — **IBM Plex Mono**. Standart Inter/Geist ishlatilmagan.
- **Komponentlar kutubxonasi** (`src/components/ui/`): `Button`, `Badge`, `Card`, `Field` (Input/Select/Textarea/Label), `Table`, `Drawer`, `Tabs`, `Avatar` (ism bo'yicha rangli initsiallar), `StatTile`, `PageHeader`, `EmptyState`, `Skeleton`, `Alert` — barcha sahifalar shu asosda qurilgan, birorta ham xom `<div className="border p-4">` qolmagan.
- **Universal qidiruv** (`Ctrl/Cmd+K`, `src/components/CommandPalette.tsx`) — TZ §27.3 talabiga muvofiq, sahifalar bo'yicha tezkor navigatsiya.
- **Ikonlar**: `lucide-react`.

## Sahifalar

| Route | Vazifa |
|---|---|
| `/login` | Split-screen login, haqiqiy `/api/auth/login`ga ulangan |
| `/crm/tasks` | **Bugungi vazifalar** (Operator Call Queue) — muddati o'tgan/navbatdagi guruhlash, bir bosishda bajarish. Avtomatik vazifalar (trial follow-up, ketma-ket yo'qlik) sababi bilan; student vazifasi Student 360°ga havola |
| `/leads` | Lidlar — yaratish, qo'ng'iroq natijasini yozish, studentga aylantirish (drawer'lar orqali) |
| `/students` | Studentlar ro'yxati |
| `/students/[id]` | **Student 360°** — statistika (davomat/homework foizi), tablar: Umumiy / Davomat / Homework / Imtihonlar / **To'lovlar** (SA/AD/Student/Parent to'liq, Operator — hisob-fakturalar) / **Support** (SA/AD/Teacher/Support) / Tarix (akademik + lid bosqichi: qo'ng'iroqlar, trial) |
| `/branches` | Filiallar ro'yxati + yaratish |
| `/users` | Foydalanuvchilar ro'yxati + yaratish |
| `/groups` | Kurslar + guruhlar (karta ko'rinishi) + o'qituvchi biriktirish (faqat SA/AD); **kurslarning oylik to'lovi** (SA/AD tahrirlaydi); Teacher/Student — o'z guruhlari, faqat ko'rish |
| `/schedule` | Dars jadvali + Conflict Detector xabarlari (dars qo'shish faqat SA/AD) |
| `/schedule/[lessonId]` | Dars: **Davomat** + **Homework**. Teacher/Support — belgilash, homework yaratish, topshiriqlarni baholash (Writing homework'da AI band va «AI tahlil» — to'liq tahlil, o'qituvchi override'i); Student — o'z davomati, homework topshirish (matn/havola), ball va izohni, Writing'da AI bandini ko'rish; Parent — farzandi holati; Operator — faqat ko'rish |
| `/ai/tutor` | **AI Tutor** (Student) — ingliz tili bo'yicha savol-javob chati: javob markaz materiallariga tayanadi va manbalari ko'rsatiladi, mavzudan tashqari savol muloyim rad etiladi; suhbatlar ro'yxati, kunlik limit |
| `/ai/usage` | **AI monitoring** (SA/AD) — so'rovlar, tokenlar, USD xarajat, kechikish va xatolar; kunlik xarajat grafigi, funksiya/provider/filial/student kesimlari, provayderlar holati; **Limitlar** tabida kunlik limitlarni sozlash (SA), **Writing benchmark** tabida AI va o'qituvchi baholari farqi |
| `/ai/speaking` | **AI Speaking** (Student) — rejim tanlash (IELTS Part 1/2/3, umumiy, erkin suhbat), savolni tinglash (brauzer TTS), mikrofonda javob yozish, transkript va nutq tezligi, yakunda taxminiy band va tavsiyalar; yozuvlarni qayta eshitish, mashqlar tarixi |
| `/ai/knowledge` | **Bilim bazasi** (SA/AD/TC) — AI Tutor uchun material yuklash (TXT/MD/CSV/PDF/DOCX; doira: tashkilot/filial/kurs/guruh, ko'rinish: studentlar yoki faqat xodimlar), indekslash holati, karantindagi hujjat matnini ko'rib tasdiqlash, qayta indekslash, o'chirish |
| `/ai/writing` | **AI Writing** (Student) — insho yuborish (IELTS Task 1/2 yoki umumiy), taxminiy band, 4 mezon, xatolar va tavsiyalar, o'qituvchi bahosi, tarix, kunlik limit; `?evaluation=` bilan natija ochiladi, baholash tugaguncha avtomatik yangilanadi. Komponent: `src/components/ai/WritingEvaluationView.tsx` |
| `/exams` | Imtihonlar — guruh filtri, yaratish, natija kiritish (SA/AD/Teacher), boshqalar uchun ko'rish |
| `/teachers`, `/teachers/[id]` | Teacher 360° — guruhlar, studentlar, jadval |
| `/chat` | **Xabarlar** — shaxsiy suhbatlar, guruh chatlari va filial kanallari (real-time, SignalR): javob, reaksiya, pin, o'chirish, fayl/rasm, «yozmoqda…», o'qildi belgisi, qidiruv. Ulanish `src/lib/chat-hub.ts`da bitta, sidebar'dagi o'qilmaganlar belgisi bilan umumiy |
| `/finance` | **To'lovlar va qarzlar** — qarzdorlar, hisob-fakturalar (holat filtri; to'lov, chegirma/promo-kod), to'lovlar (refund, bekor qilish), **Hisobot** (davr/filial bo'yicha tushum, kutilgan tushum, chegirma, refund, qarz), **Promo-kodlar** (SA yaratadi); "Hisob-fakturalarni yangilash" |
| `/support` | **Support Needed Engine** — tizim signallari va xodim tavsiyalari: ko'rib chiqish, yordamchi o'qituvchi biriktirish, rad etish, yakunlash |
| `/settings/notifications` | **Bildirishnoma sozlamalari** — rolga tegishli har bir tur uchun yoqish/o'chirish |
| `/automation` | **Avtomatlashtirish** — qoidalar (trial, yo'qlik, homework, qarz, eslatmalar): yoqish/o'chirish, chegaralar, amallar, standartga qaytarish, hozir ishga tushirish, so'nggi faoliyat. SA tahrirlaydi, AD ko'radi |
| `/audit-logs` | Audit jurnali (faqat ko'rish) |

Sidebar'dagi **qo'ng'iroq** (`src/components/NotificationBell.tsx`) — o'qilmaganlar soni (har daqiqada), bildirishnomalar oynasi: bosilganda o'qilgan bo'ladi va havola ochiladi.

`(admin)/*` sahifalar route guard bilan himoyalangan — token bo'lmasa `/login`ga yo'naltiriladi (`src/app/(admin)/layout.tsx`). Sidebar guruhlangan navigatsiya (CRM / Ta'lim / Tashkilot) va foydalanuvchi kartasi (JWT'dan dekodlangan rol/email, faqat ko'rsatish uchun — xavfsizlik chegarasi emas) bilan.

**Rolga qarab UI** (`src/lib/nav.ts`): har `NavItem`da `roles` ro'yxati bor — sidebar va Ctrl+K qidiruv faqat shu rol uchun ma'noli sahifalarni ko'rsatadi (Teacher "Filiallar"ni ko'rmaydi, Student "Lidlar"ni ko'rmaydi). Login'dan keyin `homeForRole()`: SA/AD/OP → `/crm/tasks`, Teacher/Support → `/schedule`, Student/Parent → `/students`. `canManageAcademic / isTeachingStaff / isStudentOrParent` yordamchilari sahifalarda 403 bilan tugaydigan tugma/formalarni yashiradi — bu faqat qulaylik, haqiqiy chegara backend'dagi permission+scope.

## API integratsiyasi

- `src/lib/api-client.ts` — fetch wrapper, bearer token + 401'da bitta marta refresh (refresh vaqtida tarmoq uzilsa tokenlar o'chirilmaydi); server `{ error }` yubormasa status bo'yicha tushunarli o'zbekcha xabar (401/403/404/409/429/5xx);
- `src/lib/auth-storage.ts` — tokenlar `localStorage`da (client-only) + `userFromToken()` (JWT dekodlash, faqat UI uchun); o'zgarganda `educenter:auth-changed` hodisasini yuboradi;
- `src/lib/use-auth-session.ts` — `useAuthSession()` → `{ hydrated, token, user }` (`useSyncExternalStore`). Komponentlar rol/tokenni shu hook orqali o'qiydi — effect ichida `setState` yo'q, gidratsiya paytida noto'g'ri `/login` redirect bo'lmaydi;
- `src/lib/api/*.ts` — resurslar bo'yicha tiplangan funksiyalar (`auth`, `branches`, `users`, `academic`, `crm`, `students`, `audit-logs`);
- API bazaviy URL: sahifa qaysi hostdan ochilgan bo'lsa o'shandan avtomatik aniqlanadi (`window.location.hostname`) — Tailscale/LAN orqali ham ishlaydi; `NEXT_PUBLIC_API_URL` bilan override qilinadi.

## Ma'lum cheklov

Bu muhitda headless-browser vositasi yo'q, shuning uchun sahifalar vizual jihatdan skrinshot orqali tekshirilmadi — faqat `npm run build` (production build + TypeScript) va backend endpointlariga real HTTP so'rovlar orqali tasdiqlangan. `npm run dev` bilan brauzerda qo'lda ko'rib chiqish tavsiya etiladi.

## Original create-next-app hujjatlari

Next.js haqida ko'proq: [nextjs.org/docs](https://nextjs.org/docs).
