# Graphify + Obsidian ni loyihaga o'rnatish

Claude Code, quyidagi qadamlarni ushbu loyihaning terminalida bajar. Har bir bosqichdan keyin natijani tekshir, xato chiqsa keyingi qadamga o'tishdan oldin hal qil.

## 0. Muhim eslatma (avval o'qi)
"Graphify" nomi ostida bir nechta bir-biriga o'xshash, lekin turli mualliflarga tegishli loyihalar mavjud (masalan `safishamsi/graphify`, `Graphify-Labs/graphify`, `rhanka/graphify`, `sharkkyyy10/graphify-` va boshqalar), va PyPI'dagi asl `graphify` nomi hali "band" bo'lgani uchun paket vaqtincha `graphifyy` (ikkita "y" bilan) nomi ostida chiqarilgan. O'rnatishdan oldin:
- `pip install graphifyy` buyrug'ini ishlatishdan oldin paket sahifasini (pypi.org/project/graphifyy) va bog'langan GitHub repo'sini ko'rib chiq — muallif, yulduzlar soni, oxirgi yangilanish sanasini tekshir.
- Agar paket shubhali yoki tashlab qo'yilgandek ko'rinsa, o'rnatishni to'xtat va foydalanuvchiga xabar ber.

## 1. Talablar
- Python 3.10 yoki undan yuqori versiya o'rnatilganini tekshir: `python3 --version`
- Claude Code allaqachon ishlayapti (bu terminalda ishlayapsan degani — shart bajarilgan).

## 2. Graphify'ni o'rnatish
```bash
pip install graphifyy
```
Muvaffaqiyatli o'rnatilganini tekshir:
```bash
graphify --version
graphify --help
```
Agar `graphify: command not found` xatosi chiqsa, `pip` global bin papkasi PATH'da yo'qligi mumkin — `python3 -m pip show graphifyy` orqali qayerga o'rnatilganini top va PATH'ga qo'sh, yoki `pipx install graphifyy` bilan qayta urinib ko'r.

## 3. Claude Code integratsiyasi
Loyiha papkasida (joriy papkada) quyidagini ishga tushir:
```bash
graphify install
```
Bu `.claude/` ichiga kerakli skill va hook fayllarini yozadi, shunda Claude Code `/graphify` buyrug'ini tanay oladi.

Windows uchun (agar kerak bo'lsa):
```bash
graphify install --platform windows
```

## 4. Git hook'larni o'rnatish (avtomatik yangilanish uchun)
```bash
graphify hook install
```
Bu har bir commit/checkout'dan so'ng grafni avtomatik qayta quradi.

## 5. Bilim grafini qurish
Claude Code chatida (terminal emas, Claude Code ichida) yoz:
```
/graphify .
```
Bu joriy papkani (kod, docs, PDF, rasmlar va h.k.) tahlil qilib, quyidagi natijani beradi:
```
graphify-out/
├── graph.html        # interaktiv graf (brauzerda ochiladi)
├── obsidian/         # Obsidian vault sifatida ochish mumkin bo'lgan papka
├── GRAPH_REPORT.md   # asosiy tugunlar va qiziq bog'lanishlar haqida hisobot
├── graph.json        # doimiy saqlanadigan graf (keyingi sessiyalarda qayta o'qiladi)
└── cache/            # o'zgargan fayllarni kuzatish uchun kesh
```

## 6. Obsidian'ni o'rnatish va vault'ni ulash
Claude Code buni o'zi qila olmaydi (Obsidian — desktop ilova), shuning uchun bu qadamni foydalanuvchi qo'lda bajaradi:
1. https://obsidian.md saytidan Obsidian'ni yuklab, o'rnat.
2. Obsidian'ni ochib, "Open folder as vault" tugmasini bos.
3. Loyihadagi `graphify-out/obsidian/` papkasini tanla.
4. Grafni ko'rish uchun Obsidian ichidagi "Graph view" (yon panelda tarmoq belgisi) ni och.

## 7. Grafni yangilash
Kod yoki fayllar o'zgarganda, faqat o'zgargan qismlarni qayta ishlash uchun:
```
/graphify . --update
```
Katta refaktordan keyin (fayllar o'chirilgan bo'lsa) eski, noto'g'ri graf qolib ketmasligi uchun:
```
/graphify . --force
```

## 8. Tekshirish (hammasi ishlaganini tasdiqlash)
```bash
graphify hook status
ls graphify-out/
```
Agar `graphify-out/graph.json` va `graphify-out/obsidian/` mavjud bo'lsa — o'rnatish muvaffaqiyatli.
