# S00 — Repoga yozish

| | |
|---|---|
| Holat | ✅ **bajarilgan** (2026-09-21) — bu fayl faqat yozuv; yangi sessiya bu yerda hech narsa qilmaydi |
| Manba | audit T15 · docs/11 §2 |

## Nima qilindi
124 ta commit qilinmagan fayl uch commitga yozildi (qatlam bo'yicha — umumiy fayllarda bir nechta funksiya aralashgani uchun
funksiya bo'yicha bo'lib bo'lmadi, har bir commit alohida build bo'ladi):

| Commit | Nima | Fayl |
|---|---|---|
| `a5597fc` | backend: Transfer/Freeze/Resume, Teacher Replacement, Universal Search, Student Documents, Placement Test, Group Recommendation, Developer paneli, sahifalash, `/api/me/permissions`, demo seed | 67 |
| `a637bb4` | frontend: `usePermissions`, iPad menyusi, lid doskasi, trial/convert/placement oynalari, student/teacher/group sahifalari | 58 |
| `d004d74` | hujjatlar: `docs/09`, `docs/10`, `docs/11`, `kamchiliklar.docx`, `CLAUDE.md` | 6 |

Migratsiyalar: `AddStudentDocuments`, `AddPlacementAttempts`, `AddDataAccessRequests`. Tekshiruv: 173 test, `tsc`, `eslint` toza.
`origin` va `gitlab` ga push qilinmagan.

## Keyingi
[S01.01](S01.01-student-yaratish.md) (bajarilgan) → [S01.02](S01.02-ota-ona.md). Tavsiya: avval [S01.04](S01.04-bloklangan-foydalanuvchi.md).
