# PHASE 4 — AI
## Sprint darajasidagi texnik reja

**Hujjat turi:** Technical Specification (TZ hujjatining davomi)
**Versiya:** 1.0
**Bog'liq hujjatlar:** `00_TZ.md` (§58.Phase4, §28–§38, §44, §54–§55), `01_role_permission_matrix.md` (§3.11), `02_database_erd.md` (§6 AI jadvallari), `05_phase3_technical_plan.md`

**Fazaning maqsadi:** AI'ni alohida, xavfsiz subsystem sifatida qurish: provider'ga bog'lanmagan AI Gateway (router, xavfsizlik qatlamlari, xarajat va limitlar), undan keyin uchta student funksiyasi — Writing AI (taxminiy IELTS band + teacher override), RAG asosidagi AI Tutor va Speaking MVP — va Super Admin uchun AI usage monitoring hamda sifat benchmark'i.

**Tartib sababi:** Gateway birinchi — barcha AI funksiyalari faqat u orqali provider'ga chiqadi, xavfsizlik, limit va xarajat hisobi bir joyda bo'ladi. Writing keyin — RAG'siz ishlaydi va mavjud Homework oqimiga (`HomeworkType.Writing`) darhol ulanadi. RAG + Tutor uchinchi — pgvector va hujjat ingestion'i kerak. Speaking oxirida — STT/TTS va audio saqlash qo'shimcha infratuzilma talab qiladi. Monitoring UI va benchmark usage/evaluation ma'lumotlari to'plangandan keyin.

---

## 1. Qabul qilingan arxitektura qarorlari

| # | Qaror | Standart |
|---|---|---|
| 1 | Modul | `EduCenter.Modules.Ai` — domen va EF konfiguratsiya. Gateway, router, provider'lar va xavfsizlik qatlamlari `EduCenter.Api/Ai`da (Automation/Chat kabi). Boshqa modullarga faqat `Guid` orqali bog'lanadi |
| 2 | Provider abstraksiyasi (TZ §29, §34) | `IChatModelProvider` (matn/JSON javob), `IEmbeddingProvider`, `ISpeechToTextProvider`, `ITextToSpeechProvider`. Birinchi implementatsiyalar: **`OpenAiCompatibleProvider`** (DeepSeek API ham, local vLLM/Ollama/faster-whisper ham OpenAI-compatible endpoint beradi — bitta klient) va **`FakeAiProvider`** (deterministik, dev va testlar uchun) |
| 3 | Provider sozlamasi | `Ai:Providers` — nomi, turi, base URL, model, token narxi. Kalit faqat environment/user-secrets'da, repoga yozilmaydi. Kalit yoki URL yo'q provider o'chirilgan hisoblanadi. **Hozircha DeepSeek kaliti yo'q** → matn funksiyalari `Fake` bilan ishlaydi, kalit kelganda faqat konfiguratsiya o'zgaradi |
| 4 | AI Router (TZ §30) | Vazifa turi → provider/model zanjiri (`Ai:Routes`): `TutorAnswer`, `WritingEvaluation`, `TopicClassification`, `InjectionCheck`, `Embedding`, `SpeakingReply`, `Transcription`, `SpeechSynthesis`. Xato/timeout bo'lsa zanjirdagi keyingisiga o'tadi (masalan DeepSeek → local Qwen). Har urinish usage jurnalida |
| 5 | AI bazaga ulanmaydi (TZ §37) | Provider'ga faqat backend tayyorlagan kontekst ketadi: `Student → AI Gateway → Permission Guard → Safe Retrieval → DB`. Kontekst yig'uvchi servislar chaqiruvchining permission/scope'i bilan ishlaydi. AI javobi hech qanday yozuv amalini chaqirmaydi — faqat matn/JSON qaytaradi |
| 6 | Xavfsizlik qatlamlari (TZ §37) | 1) kirish validatsiyasi (uzunlik, til, bo'sh matn); 2) mavzu klassifikatori; 3) prompt injection detektori (qoidalar + model tekshiruvi); 4) foydalanuvchi permission konteksti; 5) RAG permission filtri (SQL darajasida, reranking'dan oldin); 6) strukturali prompt (tizim ko'rsatmasi va foydalanuvchi matni ajratilgan, foydalanuvchi matni hech qachon ko'rsatma sifatida birlashtirilmaydi); 7) JSON schema validatsiyasi (xato bo'lsa bir marta qayta so'rash); 8) chiqish xavfsizligi; 9) jurnal; 10) rate limiting (foydalanuvchi bo'yicha) |
| 7 | Prompt Manager | Promptlar kodda, versiyali (`writing.ielts.v1`); versiya har usage/evaluation qatorida saqlanadi — benchmark prompt o'zgarishini solishtiradi (TZ §55) |
| 8 | Xarajat (TZ §54) | Token soni provider javobidan; narx modelning 1M token narxidan (`Ai:Providers:*:InputPricePerMillion/OutputPricePerMillion`), **USD**, `numeric(18,6)`. Local modellar narxi 0. Speaking — daqiqalar ham hisoblanadi |
| 9 | Limitlar (TZ §54) | Student uchun kunlik: tutor so'rovlari (standart 50), writing tekshiruvlari (5), speaking daqiqalari (20). Tashkilot sozlamasi mavjud `organizations.AiLimitsConfigJson` ustunida (kod standartlari + oraliq, Automation katalogi kabi siqiladi). Kun filial vaqt zonasi bo'yicha (`BranchClock`). Limitdan oshsa 429 va tushunarli sabab. Plan-based limitlar — Phase 6 (multi-tenant SaaS) |
| 10 | Rasmiy baho o'zgarmaydi | AI natijasi doim **Estimated** deb belgilanadi; `ExamResult` va homework bahosiga AI yozmaydi. Teacher override alohida saqlanadi, AI natijasi o'chirilmaydi (benchmark uchun kerak) |
| 11 | Uzoq ishlar | Writing baholash, hujjat ingestion va benchmark — fon ishi (Hangfire, mavjud server). Testlarda `Automation__Enabled=false` bo'lgani uchun `IAiJobQueue` testda ishni darhol bajaradi |
| 12 | RAG saqlash (TZ §31, §44) | PostgreSQL + **pgvector**. Hozirgi `postgres:16-alpine` image'da pgvector yo'q → `pgvector/pgvector:pg16` image'ga o'tiladi (bir xil major versiya, data volume saqlanadi; alpine→debian sababli collation tekshiriladi va kerak bo'lsa indekslar qayta quriladi). **Dev bazaga tegishdan oldin foydalanuvchidan so'raladi.** Embedding o'lchami tanlangan modelga bog'liq (migratsiyada qat'iy) — local server modeli aniqlangach belgilanadi. Embedding provider mavjud bo'lmasa qidiruv PostgreSQL full-text'ga tushadi (hybrid retrieval) |
| 13 | Speaking MVP | Real-time oqim emas — **navbatma-navbat** (turn-based): brauzer bitta javobni yozadi → yuklanadi → local Whisper (STT) → LLM examiner javobi → TTS. TTS provider sozlanmagan bo'lsa brauzer `speechSynthesis` ishlatiladi. Baholash: transkript bo'yicha (grammatika, lug'at, javobning mosligi) + Whisper vaqt belgilaridan fluency metrikalari (nutq tezligi, pauzalar). Pronunciation/stress/rhythm — Phase 6 (TZ §58 "advanced Speaking Assessment", §59) |
| 14 | Audio va hujjat fayllari | Chat'dagi kabi lokal disk (`Ai:StoragePath`), tasodifiy kalit, kirish tekshiruvi orqali yuklab olish. S3/MinIO — keyingi ish (Chat bilan birga) |
| 16 | Javob tili (2026-09-16) | **Writing va Speaking** izohlari, kuchli/zaif tomonlari va tavsiyalari — **o'zbek tilida** (lotin). Studentning o'z matni, uning tuzatilgan varianti, inglizcha misollar va IELTS mezon nomlari tarjima qilinmaydi. **Speaking examiner savollari ingliz tilida** qoladi — mashqning mohiyati shu. **AI Tutor** savol qaysi tilda bo'lsa (o'zbek / rus / ingliz), javob ham o'sha tilda; inglizcha misollar va grammatik atamalar ingliz tilida saqlanadi. Til qoidalari `Api/Ai/AiLanguage.cs`da bir joyda; mavzu klassifikatori savol tilini ham qaytaradi (mavzudan tashqari rad javobi ham o'sha tilda). Prompt versiyalari yangilandi (`writing.*.v2`, `tutor.topic.v2`, `tutor.answer.v2`, `speaking.assessment.v2`) — benchmark eski va yangi natijalarni aralashtirmasligi uchun |
| 15 | Local modellar (TZ §36) | **2026-09-16'da qayta ko'rildi:** dasturlash jarayonida local modellar dev mashinaning o'zida (RTX 3060 12 GB) — compose'dagi `ollama` servisi (NVIDIA runtime, `educenter-ollama:11434/v1`): `qwen3:8b` (matn) va `bge-m3` (embedding, 1024 o'lchov). 2×RTX 4090 serveri rejadan chiqarildi (vLLM konteyneri KV cache yetishmasligidan qayta-qayta yiqilmoqda); undagi Whisper (`192.168.10.201:18001/asr`) Speaking uchun zaxira variant |

---

## 2. Permission o'zgarishlari (matritsa §3.11)

Matritsadagi `ai.writing.use` bitta kod ichida ikki xil ma'noni birlashtiradi ("TC review", "SD submit"). Matritsa §4 qoida 6'ga ko'ra rol nomi bilan ajratib bo'lmaydi, shuning uchun ikkiga bo'linadi. Matritsada Speaking va Knowledge Base uchun permission yo'q — qo'shiladi:

| Permission | SA | AD | OP | TC | ST | SD | PR | DV |
|---|---|---|---|---|---|---|---|---|
| `ai.tutor.use` | — | — | — | — | — | SELF | — | — |
| `ai.writing.use` | — | — | — | — | — | SELF (submit) | — | — |
| `ai.writing.review` *(yangi)* | ALL_ORG | MY_BRANCHES | — | MY_GROUPS | ASSIGNED_GROUPS | — | — | — |
| `ai.writing.override` | ALL_ORG | MY_BRANCHES | — | MY_GROUPS | — | — | — | — |
| `ai.speaking.use` *(yangi)* | — | — | — | — | — | SELF | — | — |
| `ai.knowledge.manage` *(yangi)* | ALL_ORG | MY_BRANCHES | — | MY_GROUPS | — | — | — | — |
| `ai.usage.view` | ALL_ORG | MY_BRANCHES | — | — | — | — | — | — |
| `ai.limits.edit` | ALL_ORG | — | — | — | — | — | — | — |

- `ai.writing.review` — o'z scope'idagi studentlarning AI baholash natijalarini ko'rish; `ai.writing.override` — natijani teacher bahosi bilan almashtirish.
- `ai.knowledge.manage` — Knowledge Base'ga material yuklash: SA tashkilot bo'yicha, AD filial bo'yicha, TC o'z guruhlari kurslari bo'yicha. Staff-only materiallar (teacher methodology) studentga retrieval'da hech qachon chiqmaydi.
- Benchmark'ni ishga tushirish `ai.limits.edit` bilan (Super Admin).
- Parent AI funksiyalaridan foydalanmaydi; farzandi homework'ining teacher tomonidan tasdiqlangan bahosini avvalgidek ko'radi.

---

## 3. Sprint bo'linishi

### Sprint 1 — AI Gateway: provider'lar, router, xavfsizlik, usage va limitlar
- `EduCenter.Modules.Ai`: `AiConversation` (student × modul `Tutor|Writing|Speaking`), `AiUsage` (tashkilot, filial, foydalanuvchi, student, funksiya, vazifa turi, provider, model, prompt versiyasi, kirish/chiqish tokenlari, xarajat, kechikish, holat, xato kodi, rad etish sababi). Migratsiya `AddAiGateway`
- `AiGateway` — yagona kirish nuqtasi: limit → kirish validatsiyasi → injection detektori → router → provider → schema validatsiyasi → usage jurnali
- Provider'lar: `FakeAiProvider`, `OpenAiCompatibleProvider` (chat completions JSON mode, embeddings, audio transcriptions); provider health tekshiruvi
- `PromptInjectionDetector` (qoidalar: "ignore previous instructions", rol almashtirish, tizim promptini so'rash, yashirin belgilar va h.k.; natija — ball va sabab), `AiInputValidator`, `AiOutputValidator`
- `AiLimits` — `AiLimitsConfigJson` + katalog standartlari; `AiUsageCounter` — kunlik hisob `ai_usages`dan (filial vaqt zonasi)
- Rate limiting: `ai` policy (foydalanuvchi bo'yicha daqiqalik)
- Permission seed (§2 jadvali), matritsa §3.11 yangilanadi
- API: `GET /api/ai/status` (chaqiruvchi uchun qaysi AI funksiyalari mavjud va bugungi qoldiq limitlar)
- Testlar: Fake provider bilan gateway oqimi, fallback, injection rad etilishi (provider chaqirilmaydi, jurnalga yoziladi), limit 429, schema xatosida qayta so'rash, xarajat hisobi

### Sprint 2 — Writing AI (TZ §33)
- `WritingEvaluation`: manba (`HomeworkSubmission` yoki mustaqil mashq), topshiriq turi (IELTS Task 1 / Task 2 / General), topshiriq matni, insho, holat (`Queued → Processing → Completed | Failed | Rejected`), **Estimated Overall Band** va 4 mezon (TR, CC, LR, GRA), strengths/weaknesses, grammar issues, vocabulary issues, suggested improvements (JSON), prompt versiyasi, model; teacher override (band, mezonlar, izoh, kim/qachon). Migratsiya `AddWritingEvaluations`
- Pipeline: validatsiya (so'z soni oralig'i, ingliz tili) → injection tekshiruvi → rubric konteksti → baholash → JSON schema → band 0–9 oralig'i, 0.5 qadamga yumaloqlash, overall = mezonlar o'rtachasi bilan muvofiqlik tekshiruvi
- `Writing` turidagi homework topshirilganda AI baholash navbatga qo'yiladi (limitga kiradi); student mustaqil mashq ham yuborishi mumkin
- Timeline: `WRITING_EVALUATED`, `WRITING_OVERRIDDEN`; bildirishnoma `WRITING_EVALUATED` (studentga); audit: `WritingEvaluation`
- API: `POST /api/ai/writing/evaluations`, `GET /api/ai/writing/evaluations` (o'ziniki / review scope), `GET /{id}`, `POST /{id}/override`, `POST /{id}/retry`
- UI: student **AI Writing** sahifasi (yangi insho, tarix, natija — "Estimated band, rasmiy IELTS bahosi emas"), homework topshirig'ida AI natijasi; teacher homework review'ida AI natija paneli va override formasi

### Sprint 3 — RAG + AI Tutor (TZ §31, §32, §38)
- Infratuzilma: `pgvector/pgvector:pg16` image, `CREATE EXTENSION vector` migratsiyasi (dev bazaga tegishdan oldin kelishiladi)
- `KnowledgeDocument` (nomi, turi, doira: tashkilot / filial / kurs / guruh, ko'rinish: `Students | StaffOnly`, holat `Uploaded → Scanning → Ready | Quarantined | Failed`, kim yuklagan), `KnowledgeChunk` (matn, tartib, embedding `vector(N)`, `tsvector`, doira ustunlari). Migratsiya `AddKnowledgeBase`
- Ingestion (TZ §38): fayl validatsiyasi (tur, hajm) → xavfsizlik tekshiruvi → parsing (txt, md, pdf, docx) → injection pattern scan (topilsa `Quarantined`, staff tasdiqlaydi) → access classification → chunking → embedding → saqlash
- Retrieval: doira/ko'rinish filtri SQL'da → vector + full-text hybrid → top-k; manba havolalari javobga qo'shiladi
- AI Tutor: kontekst (student darajasi, joriy kurs, so'nggi mavzular, homework/imtihon bo'yicha zaif tomonlar — Safe Retrieval orqali), mavzu klassifikatori (faqat English/IELTS/Grammar/Vocabulary/Speaking/Writing/Reading/Listening; tashqarisida muloyim rad), suhbat tarixi
- API: `GET/POST /api/ai/tutor/conversations`, `GET/POST /api/ai/tutor/conversations/{id}/messages`; `GET/POST /api/ai/knowledge/documents`, `DELETE /{id}`, `POST /{id}/approve` (karantindan chiqarish)
- UI: student **AI Tutor** (suhbatlar ro'yxati, chat, manbalar); staff **Knowledge Base** (yuklash, holat, doira, karantin)

### Sprint 4 — Speaking MVP (TZ §34, §35) *(reja)*
- `SpeakingSession` (student, rejim, holat, davomiylik, baholash JSON, Estimated band), `SpeakingTurn` (examiner savoli, student audio kaliti, transkript, so'z vaqt belgilari, davomiylik). Migratsiya `AddSpeakingSessions`
- Rejimlar (MVP): IELTS Part 1, Part 2 (cue card + 1 daqiqa tayyorgarlik), Part 3, General English, Free Conversation. Full Mock, Interview, Business, Travel — keyingi iteratsiya
- Oqim: sessiya boshlash → examiner savoli (LLM) → student audio (webm/opus, ≤2 daqiqa) → STT (local Whisper) → keyingi savol → sessiya yakuni → baholash (Fluency, Vocabulary, Grammar, Answer Relevance transkriptdan; nutq tezligi va pauzalar vaqt belgilaridan)
- Speaking daqiqalari limiti; audio fayllar kirish tekshiruvi orqali
- API: `POST /api/ai/speaking/sessions`, `POST /{id}/turns` (multipart audio), `POST /{id}/finish`, `GET /api/ai/speaking/sessions`, `GET /{id}`, `GET .../turns/{turnId}/audio`
- UI: student **AI Speaking** — rejim tanlash, mikrofon yozish, examiner ovozi (TTS yoki brauzer), transkript, yakuniy hisobot

### Sprint 5 — AI usage monitoring, limitlar va benchmark (TZ §54, §55)
- Monitoring (`ai.usage.view`): kunlik so'rovlar, tokenlar, writing baholashlar, speaking daqiqalari, student va filial bo'yicha xarajat, o'rtacha kechikish, xatolar, rad etilgan (injection/limit) so'rovlar; provider holati
- Limitlar sozlamasi (`ai.limits.edit`) — oraliq va standartlar ko'rsatilgan forma, audit jurnalida
- Writing benchmark: dataset — teacher override qilingan baholashlar (teacher bahosi, AI bahosi, delta, mezon bo'yicha metrikalar: MAE, ±0.5 ichidagi ulush); prompt/model o'zgarganda qayta ishga tushirish (fon ishi), natijalar tarixi versiyalar bo'yicha
- API: `GET /api/ai/usage/summary?from&to&branchId`, `GET/PUT /api/ai/limits`, `POST /api/ai/benchmarks/writing/run`, `GET /api/ai/benchmarks/writing`
- UI: `/ai/usage` (Tashkilot → «AI monitoring»), limitlar va benchmark tablari

---

## 4. Definition of Done (Phase 4 yakuni)

- [x] Barcha AI chaqiruvlari faqat AI Gateway orqali o'tadi; provider konfiguratsiya bilan almashtiriladi va xato bo'lsa fallback ishlaydi;
- [x] Prompt injection urinishlari provider'ga yetmasdan rad etiladi va jurnalga yoziladi; AI hech qanday yozuv amalini bajarmaydi;
- [x] Student writing yuboradi va Estimated IELTS band + 4 mezon + tavsiyalar oladi; teacher natijani override qiladi — hammasi audit va timeline'da;
- [x] AI Tutor faqat ta'lim domenida, student ko'ra oladigan materiallar asosida, manbalari bilan javob beradi;
- [x] Knowledge Base'ga yuklangan hujjat ingestion pipeline'dan o'tadi; shubhali hujjat karantinga tushadi;
- [x] Student IELTS Speaking Part 1–3 mashqini ovoz bilan o'tadi va transkript + taxminiy baholash oladi;
- [x] Kunlik limitlar ishlaydi va Super Admin ularni kod o'zgartirmasdan sozlaydi;
- [x] Super Admin AI usage, xarajat, kechikish va xatolarni filial/student kesimida ko'radi;
- [x] Writing benchmark teacher va AI baholari farqini prompt versiyalari bo'yicha ko'rsatadi;
- [x] Matritsa §3.11 permissionlari kodda ishlaydi.

---

## 5. Tashqi bog'liqliklar

| Resurs | Holat (2026-09-16) | Ta'siri |
|---|---|---|
| DeepSeek API kaliti | To'lov qilingan, kalit olinmoqda | Kalit `appsettings.Local.json`da (git-ignored, namuna: `appsettings.Local.example.json`); testlar uni majburan o'chiradi, shuning uchun test yurgizish pul sarflamaydi |
| Local modellar (dev mashina, RTX 3060 12 GB) | Compose'da `ollama` servisi, `qwen3:8b` + `bge-m3` | Embedding o'lchami **1024** (bge-m3) — pgvector ustuni shunga qarab yaratiladi; Qwen — DeepSeek yo'q paytda zaxira |
| 2×RTX 4090 serveri (`shumbola-server`) | Rejadan chiqarildi | vLLM konteyneri KV cache yetishmasligidan yiqilmoqda; faqat Whisper (`:18001/asr`) Speaking uchun zaxira |
| pgvector | Hozirgi image'da yo'q | Sprint 3 boshida image almashtiriladi (foydalanuvchi rozi, ruxsat so'rovi tasdiqlanishi kerak) |
| Telegram (Phase 3 Sprint 4) | Keyinga qoldirilgan | Phase 4'ga bog'liq emas |

---

## 6. Sprint jurnali

### Sprint 1 — AI Gateway (2026-09-15, bajarildi)

**Model** — yangi modul `EduCenter.Modules.Ai`, migratsiya `AddAiGateway`:
- `AiRequest` (`ai_requests`) — foydalanuvchining bitta AI so'rovi: tashkilot, filial, foydalanuvchi, student, funksiya (`Tutor`/`Writing`/`Speaking`/`System`), vazifa turi, prompt versiyasi, holat (`Succeeded`/`Failed`/`Rejected`), sabab kodi (`INVALID_INPUT`, `PROMPT_INJECTION`, `LIMIT_EXCEEDED`, `NO_PROVIDER`, `PROVIDER_ERROR`, `INVALID_OUTPUT`), javob bergan provider/model, tokenlar, xarajat (USD), kechikish, audio soniyalar. Limit va hisobotlar shu jadvaldan.
- `AiUsage` (`ai_usages`) — so'rov ortidagi har bir provider chaqiruvi (fallback va qayta so'rashlar alohida qator): provider, model, muvaffaqiyat, tokenlar, xarajat, kechikish, xato kodi.
- **Prompt va javob matni jurnalga yozilmaydi** — ularda student shaxsiy ma'lumoti bo'lishi mumkin; funksiyalar keraklisini o'z jadvalida saqlaydi. Jurnal chaqiruvchining unit of work'idan mustaqil kontekstda saqlanadi.
- `AiConversation` ERD'dagi kabi Sprint 3'da (Tutor bilan) qo'shiladi — Sprint 1'da uni ishlatadigan funksiya yo'q.

**Provider'lar va router** (`Api/Ai/Providers`):
- `IChatModelProvider`; `OpenAiCompatibleChatProvider` (`/chat/completions`, JSON mode, token hisobi javobdan) — DeepSeek va local vLLM uchun bitta klient; `FakeChatModelProvider` — deterministik javob, token taxmini, narx 0; testlar uchun `FailureMode=AlwaysFail`.
- **`OllamaChatProvider`** (2026-09-16, `Type: "Ollama"`) — Ollama'ning native `/api/chat` endpointi. Sabab: uning OpenAI-mos endpointida Qwen3 kabi hybrid-reasoning modellarda "thinking"ni o'chirib bo'lmaydi — Ollama prompt'ga o'zi `/think` qo'shadi, o'ylash matnini alohida `reasoning` maydoniga soladi va `content` bo'sh qoladi. Native endpointda `"think": false` va `"format": "json"` ishlaydi. OpenAI-mos provider'da ham `DisableReasoning` bor (`/no_think` + `chat_template_kwargs`, vLLM uchun), javobdagi `<think>…</think>` bloklari esa har doim kesib tashlanadi.
- **Kalitlar va mashinaga xos sozlamalar:** `appsettings.Local.json` (git-ignored, namuna `appsettings.Local.example.json`). Konfiguratsiya manbalari "oxirgisi yutadi" tartibida, shuning uchun `Program.cs` bu fayldan **keyin** `AddEnvironmentVariables()`ni qayta chaqiradi — aks holda fayl environment o'zgaruvchilaridan ustun turib, integration testlar haqiqiy (pullik) DeepSeek API'siga chiqib ketadi. Test host qo'shimcha ravishda `deepseek`/`local` provayderlarini o'chiradi va kalitni bo'shatadi.
- `AiProviderRegistry`: `Ai:Providers` dan quriladi. O'chirilgan, `BaseUrl`/`ChatModel`siz yoki development'dan tashqaridagi fake provider marshrutga kirmaydi (sababi `GET /api/ai/providers`da ko'rinadi). `Ai:Routes` — vazifa turi yoki `Default` → zanjir, element `provider` yoki `provider:model` (Ollama'ning `qwen2.5:32b` kabi nomlari ham ishlaydi). Narx — `Models[]` (1M token uchun USD); ro'yxatda bo'lmagan model bepul.
- Embedding / STT / TTS interfeyslari ular ishlatiladigan sprintda (3 va 4) qo'shiladi.
- Sozlama: `appsettings.json` — `deepseek` (`Enabled=false`, kalitsiz) va `local` (bo'sh shablon), `Default` marshrut `deepseek → local`. `appsettings.Development.json` — `AllowFakeProvider=true`, `fake` provider, marshrut `deepseek → local → fake`. Kalit faqat `Ai__Providers__deepseek__ApiKey` environment o'zgaruvchisi / user-secrets orqali. `deepseek-chat` narxi (0.28 / 0.42 USD) provayderning joriy narx ro'yxati bilan tekshirilishi kerak.

**Gateway** (`Api/Ai/AiGateway.cs`) — `CompleteTextAsync`, `CompleteJsonAsync<T>(validate)`, `CheckProviderAsync`. Tartib:
1. kirish validatsiyasi (bo'sh, uzunlik, boshqaruv belgilari) → `Rejected`;
2. kunlik limit → `LimitExceeded` (429 uchun);
3. prompt injection detektori → `Rejected`, provider chaqirilmaydi;
4. marshrut bo'sh → `Unavailable`;
5. strukturali prompt: ko'rsatmalar + xavfsizlik qoidalari system xabarida, foydalanuvchi matni faqat `<user_input>` ichida (ichidagi o'xshash teglar zararsizlantiriladi);
6. provider'lar navbat bilan, har biriga o'z timeout'i; xato/timeout → keyingisi;
7. javob tekshiruvi: JSON (```json fence tozalanadi) + funksiya validatori + ko'rsatmalar oshkor bo'lishi tekshiruvi; xato bo'lsa sabab modelga qaytarilib `Ai:OutputValidationRetries` (1) marta qayta so'raladi;
8. jurnal.
- `PromptInjectionDetector` — qoidalar: ko'rsatmalarni bekor qilish, system prompt'ni so'rash, "cheklovsiz" persona / developer mode, chat-template tokenlari, soxta `System:` qatori; rus va o'zbek variantlari. Ko'rinmas belgilar olib tashlanib tekshiriladi, bidi override belgilari o'zi shubhali. Qoidalar ataylab tor — "ignore the rules of society" kabi insho jumlalari o'tadi. **Model orqali ikkinchi tekshiruv** (`InjectionCheck` marshruti) real provider ulangach qo'shiladi.
- Mavzu klassifikatori (TZ §37 2-qatlam) — Tutor bilan Sprint 3'da.

**Limitlar** (`Api/Ai/AiLimits.cs`): katalog — tutor 50 ta, writing 5 ta, speaking 20 daqiqa (kuniga, 0–1000 / 0–100 / 0–240). Tashkilot qiymatlari `organizations.AiLimitsConfigJson`da, o'qilganda oraliqqa siqiladi. "Bugun" — studentning guruh filiali (bo'lmasa foydalanuvchi filiali) vaqt zonasida (`BranchClock.StartOfLocalDay`). Faqat `Succeeded` so'rovlar hisoblanadi — rad etilgan yoki provider xatosi limitni yemaydi. Limit API UI'dan oldin (Sprint 5 o'rniga) shu sprintda qilindi; `Organization` o'zgarishi audit jurnalida.

**Rate limiting:** `ai` policy — foydalanuvchi bo'yicha daqiqasiga `RateLimiting:AiPermitPerMinute` (20). Per-user partition ishlashi uchun `UseRateLimiter` `UseAuthentication`dan keyinga ko'chirildi (global va login limitlari IP bo'yicha — ta'sir yo'q).

**Permission:** §2 jadvali seed qilindi (`ai.tutor.use`, `ai.writing.use`, `ai.writing.review`, `ai.writing.override`, `ai.speaking.use`, `ai.knowledge.manage`, `ai.usage.view`, `ai.limits.edit`); matritsa §3.11 yangilandi.

**API** `/api/ai`: `GET status` (`[Authorize]` — chaqiruvchiga qaysi funksiya ruxsat etilgan, provider bormi, bugungi limit/foydalanilgan/qoldiq, `demoMode` — javobni fake provider berayotgan bo'lsa); `GET limits` (`ai.usage.view`); `PUT limits`, `DELETE limits` (`ai.limits.edit` — oraliq va noma'lum kalit → 400); `GET providers` (`ai.usage.view` — provider'lar, faol emaslik sababi, kalit bor-yo'qligi (kalitning o'zi emas), har vazifa uchun hal qilingan zanjir); `POST providers/{name}/check` (`ai.limits.edit`, `ai` rate limit — ulanishni tekshirish, noma'lum → 404).

**UI:** bu sprintda yo'q (reja bo'yicha) — student sahifalari Sprint 2–4'da, monitoring/limitlar Sprint 5'da.

**Testlar:** 66/66 (2 unit + 64 integration). `AiGatewayTests` (7): status student uchun limitlar bilan, xodim uchun hech narsa; fake orqali javob va jurnal (kontentsiz, `<user_input>` ajratgichi modelga yetadi); `broken → fake` fallback va hammasi yiqilganda `Failed`; injection va juda uzun matn provider chaqirilmasdan rad etiladi; noto'g'ri javob bir marta qayta so'raladi, keyin `Failed`; limit 2 bo'lsa uchinchisi `LimitExceeded` (rad etilgan so'rov hisoblanmaydi), AD o'zgartira olmaydi, oraliq/noma'lum kalit 400, reset; provider ro'yxatida kalit yo'q, check fake → ok, broken/o'chirilgan → xato, noma'lum 404, AD 403. `PromptInjectionDetectorTests` (18 holat): hujum namunalari (EN/RU/UZ, ko'rinmas belgi, bidi) va oddiy student matnlari. Dev bazaga `AddAiGateway` qo'llandi, dev API'da `status`/`providers`/`check` qo'lda tekshirildi.

### Sprint 2 — Writing AI (2026-09-15, bajarildi)

**Model** — `WritingEvaluation` (`writing_evaluations`), migratsiya `AddWritingEvaluations`: student, so'ragan foydalanuvchi (limit shu akkauntdan), ixtiyoriy homework / submission (submission bo'yicha unikal), topshiriq turi (`IeltsTask1` / `IeltsTask2` / `General`), topshiriq va insho matni, so'z soni, prompt versiyasi, holat (`Queued → Processing → Completed | Failed | Rejected`), xato, `AiRequestId`, model; AI bandlari (overall + 4 mezon, `numeric(3,1)`), izohlar/kuchli-zaif tomonlar/xatolar/tavsiyalar JSON'da; teacher override — alohida ustunlar (band, mezonlar, izoh, kim/qachon). AI natijasi override'da o'zgarmaydi.

**Qoidalar** (`Api/Ai/Writing/WritingRubric.cs`):
- Kirish: topshiriq ≤ 2000 belgi; insho 50–1200 so'z, ≤ 9000 belgi; lotin yozuvi va inglizcha yordamchi so'zlar ulushi (o'zbek/rus insholari pullik chaqiruvdan oldin rad etiladi).
- Prompt versiyalari `writing.ielts-task1.v1`, `writing.ielts-task2.v1`, `writing.general.v1`: IELTS band descriptor'lari (4 mezon), minimal hajm jarimasi (150 / 250 so'z — so'z sonini backend hisoblaydi va trusted ko'rsatmaga yozadi), Task 1'da grafik modelga ko'rinmasligi aytiladi, izohlar B1 darajasidagi inglizchada.
- Model faqat 4 mezon bandini beradi. **Overall band backendda IELTS qoidasi bilan hisoblanadi** (o'rtacha; .25 → .5 ga, .75 → keyingi butunga) — mezonlar bilan hech qachon zid bo'lmaydi. Bandlar 0–9 oralig'iga siqilib 0.5 ga yumaloqlanadi.
- Schema: 4 mezon bandi va `strengths`/`weaknesses`/`suggestions` massivlari majburiy (xato bo'lsa gateway bir marta qayta so'raydi). Grammatika/lug'at xatosining `excerpt`i inshoda so'zma-so'z topilmasa tashlab yuboriladi; ro'yxatlar va matn uzunliklari cheklanadi.
- Homework'da topshiriq turi yo'q: matnda "task 1"/"task 2" bo'lsa shu, aks holda IELTS kursi → Task 2, boshqasi → General.

**Oqim** (`WritingEvaluator`): yaratish → limit tekshiruvi (bugungi muvaffaqiyatli + navbatdagi/jarayondagi baholashlar, shunda navbat orqali limitdan oshib bo'lmaydi) → `Queued` → fon ishi. `IAiJobQueue`: Hangfire bor joyda `WritingEvaluationJob` (`AutomaticRetry(0)` — qayta urinishni student o'zi qiladi), testlarda (`Automation__Enabled=false`) shu so'rov ichida darhol. Ish idempotent (faqat `Queued`ni oladi); gateway natijasi `Completed` / `Rejected` (injection) / `Failed` (provider, noto'g'ri javob, limit). Kutilmagan xatoda ham `Failed`. `Failed` yoki 10 daqiqadan ko'p osilib qolgan baholashni egasi qayta yuboradi.
- `Writing` turidagi homework matn bilan topshirilganda avtomatik navbatga qo'yiladi (student `ai.writing.use`ga ega va limiti qolgan bo'lsa; aks holda jim o'tkazib yuboriladi — o'qituvchi odatdagidek baholaydi).
- Yakunlanganda: timeline `WRITING_EVALUATED` (taxminiy band), bildirishnoma `WRITING_EVALUATED` (studentga, havola `/ai/writing?evaluation=…`). Override'da: timeline `WRITING_OVERRIDDEN`, studentga bildirishnoma, audit — **qo'lda, faqat band maydonlari bilan** (entity audit ro'yxatiga qo'shilmadi, chunki insho matni audit jurnaliga ko'chmasligi kerak).

**API** `/api/ai/writing`: `POST evaluations` (`ai.writing.use`, `ai` rate limit; 400 validatsiya, 429 limit, 503 provider yo'q); `GET evaluations?homeworkId` (o'ziniki); `GET reviews?studentId|homeworkId` (`ai.writing.review`, scope bo'yicha; filtrsiz 400); `GET evaluations/{id}` (egasi yoki scope'i qamragan reviewer, aks holda 404; `canOverride`, `canRetry`); `POST evaluations/{id}/retry`; `POST evaluations/{id}/override` (`ai.writing.override` + scope; faqat `Completed`; bandlar 0–9, 0.5 qadam).

**UI:**
- `/ai/writing` (sidebar «AI» → «AI Writing», faqat Student): yangi insho formasi (tur, topshiriq, so'z hisoblagichi va tavsiya etilgan minimum, bugungi qoldiq limit), tarix, natija — «Taxminiy IELTS band — rasmiy natija emas», 4 mezon, kuchli/zaif tomonlar, grammatika va lug'at xatolari (asl → tuzatilgan), tavsiyalar, insho; kutish holatida avtomatik yangilanadi; demo rejim ogohlantirishi; xato bo'lsa «Qayta baholash».
- Dars sahifasi → Homework: student Writing topshirig'i yonida «AI band 6.0» havolasi; o'qituvchi «Topshiriqlar» oynasida har submission uchun AI band / holat va «AI tahlil» (to'liq tahlil + override formasi).
- Student 360° timeline va bildirishnomalar qo'ng'irog'ida yangi turlar.

**Testlar:** 70/70 (2 unit + 68 integration). `WritingEvaluationTests` (4): mashq inshosi baholanadi (4 mezon, overall IELTS qoidasi bilan, so'zma-so'z bo'lmagan xato tashlanadi, bildirishnoma, teacher yubora olmaydi); qisqa / o'zbekcha / topshiriqsiz → 400, injection → `Rejected` va band yo'q; boshqa student va operator 404, o'qituvchi override qila oladi, support teacher faqat ko'radi (override 403), noto'g'ri band 400, override AI bandini o'zgartirmaydi, audit yozuvida insho yo'q, timeline; Writing homework topshirilganda avtomatik `IeltsTask2` baholash, Grammar homework — yo'q, o'qituvchi `reviews?homeworkId`, limit 1 bo'lganda 429. Frontend: `tsc --noEmit` va eslint toza (`next build` ishlab turgan dev serverni buzmaslik uchun ishga tushirilmadi).

### Sprint 3 — RAG + AI Tutor (2026-09-16, bajarildi)

**Infratuzilma:** dev Postgres `pgvector/pgvector:pg16` image'iga o'tkazildi (zaxira → yangi volume'ga tiklash, qator sonlari solishtirildi, eski volume orqaga qaytish uchun saqlandi); `vector` 0.8.6. EF tomonda `Pgvector.EntityFrameworkCore` (`UseVector()`), domen `Pgvector` paketiga bog'landi.

**Model** — migratsiya `AddKnowledgeBase`:
- `KnowledgeDocument` (`knowledge_documents`): doira (`Organization`/`Branch`/`Course`/`Group` + nishon id), ko'rinish (`Students`/`StaffOnly`), fayl (nomi, turi, hajmi, saqlash kaliti — **asl fayl saqlanadi**, qayta indekslash uchun), holat (`Uploaded → Processing → Ready | Quarantined | Failed`), xato, bo'laklar soni, embedding modeli, `InjectionCheckWaived`.
- `KnowledgeChunk` (`knowledge_chunks`): matn (≤4000), tartib, **hujjatning doirasi va ko'rinishi nusxalanadi** (filtr bitta SQL'da bajarilishi uchun), `Embedding vector(1024)` (null bo'lishi mumkin), `EmbeddingModel`. Qo'lda SQL bilan: generated `SearchVector tsvector` (`to_tsvector('simple', "Text")` — ingliz/o'zbek/rus aralash bazada stemming qilmaslik uchun), GIN indeks va `hnsw (vector_cosine_ops)` indeks. `SearchVector` ataylab EF modelidan tashqarida — uni faqat qidiruv SQL'i o'qiydi.
- `AiConversation` / `AiConversationMessage`: Tutor suhbatlari, har xabarda manbalar JSON va `AiRequestId`.

**Embedding:** `IEmbeddingProvider` — `OllamaEmbeddingProvider` (`/api/embed`), `OpenAiCompatibleEmbeddingProvider` (`/embeddings`), `FakeEmbeddingProvider` (deterministik, so'z xeshlari asosida — testlar vektor yo'lini ham sinaydi). Marshrut `Ai:Routes:Embedding` (`local` → dev'da `fake`). **O'lcham 1024** — bge-m3'ning tabiiy kengligi; Qwen3-Embedding-0.6B ham 1024, OpenAI/Cohere/Voyage modellarini 1024 ga qisqartirish mumkin, shuning uchun model almashsa sxema emas, faqat qayta embedding kerak bo'ladi.

**Ingestion** (`KnowledgeIngestor`, TZ §38 bosqichlari): validatsiya (tur, hajm — yuklashda) → parsing (PdfPig, OpenXml, matn) → **prompt injection tekshiruvi** (bo'laklar bo'yicha; topilsa hujjat `Quarantined` va indekslanmaydi) → access classification (doira/ko'rinish har bo'lakka) → chunking (~1200 belgi, 150 belgi ustma-ustlik, paragraf va gap chegaralarida) → embedding (16 tadan, xato bo'lsa keyingi provider) → saqlash. Embedding umuman bo'lmasa hujjat baribir `Ready` bo'ladi va full-text orqali topiladi. Fon ishi — Hangfire (`KnowledgeIngestionJob`), testlarda darhol.

**Retrieval** (`KnowledgeRetriever`): bitta SQL — avval doira va ko'rinish filtri (`StaffOnly` studentga hech qachon chiqmaydi), keyin ikkita nomzod ro'yxati (vektor `<=>` va `ts_rank_cd`) va ularni **Reciprocal Rank Fusion** bilan birlashtirish. Embedding provider yo'q bo'lsa faqat full-text ishlaydi — RAG buzilmaydi, sifati pasayadi.

**AI Tutor** (`TutorService`, TZ §32): mavzu klassifikatori (`tutor.topic.v1`, `System` funksiyasi sifatida — studentning kunlik limitini yemaydi) → domen tashqarisidagi savol muloyim rad etiladi va suhbatga yoziladi; domen ichida — student profili (daraja, kurslar, oxirgi imtihon bandlari, homework o'rtachasi — **faqat o'ziniki**, safe retrieval), oxirgi 6 xabar, top-6 manba; hammasi `<user_input>` ichida, ya'ni zaharlangan hujjat ko'rsatma bera olmaydi. Javob `tutor.answer.v1` (B1 darajasidagi ingliz tilida, ≤250 so'z), manbalar hujjat sarlavhalari bilan qaytariladi.

**API:** `/api/ai/knowledge/documents` (`ai.knowledge.manage`): ro'yxat, yuklash (multipart), detal, `preview` (ajratilgan matn — karantinni ko'rib chiqish uchun), `approve` (karantindan chiqarish + qayta indekslash), `reingest`, `delete`, `file`. Doira tekshiruvi: `ALL_ORG` — hammasi, `MY_BRANCHES` — o'z filiallari, `MY_GROUPS` — o'z guruhlari va ularning kurslari; boshqasiga 400. `/api/ai/tutor` (`ai.tutor.use`): `conversations`, `conversations/{id}/messages`, `ask` (`ai` rate limit), `DELETE conversations/{id}` — faqat o'z suhbati.

**UI:** `/ai/tutor` (Student) — suhbatlar ro'yxati, chat, javob ostida manbalar, kunlik qoldiq limit, demo ogohlantirishi; `/ai/knowledge` (SA/AD/TC) — material yuklash (doira va ko'rinish tanlovi bilan), holat jadvali (karantin/xato sabablari), matn ko'rish, tasdiqlash, qayta indekslash, o'chirish.

**Testlar:** 74/74 (2 unit + 72 integration). `KnowledgeAndTutorTests` (4): hujjat parsing/chunk/embedding va operatorga 403; AI ko'rsatmasi bo'lgan hujjat `Quarantined` → tasdiqlangach `Ready`, takroriy tasdiqlash 409; o'qituvchi faqat o'z guruhiga yuklaydi (tashkilot doirasi va nishonsiz so'rov 400, `.exe` 400); tutor domen ichida javob beradi va **faqat studentga ruxsat etilgan** hujjatni keltiradi (`StaffOnly` keltirilmaydi), domen tashqarisida rad etadi, begona student suhbatni ko'ra olmaydi (404), o'qituvchiga 403, bo'sh savol 400.

### Sprint 5 — AI monitoring, limitlar va benchmark (2026-09-16, bajarildi)

**Manba:** hisobotlar gateway jurnalidan (`ai_requests`) o'qiydi — bu yerda har bir AI chaqiruvi (matn, embedding, transkripsiya) allaqachon yozilgan, shuning uchun **yangi AI funksiyasi monitoring kodiga tegmasdan hisobga tushadi**.

**API** (`ai.usage.view`):
- `GET /api/ai/usage/summary?from&to&branchId` — jami (so'rovlar, muvaffaqiyatli/xato/rad etilgan, tokenlar, **USD xarajat**, o'rtacha kechikish, speaking daqiqalari, writing tekshiruvlari, tutor savollari), kunlik qator (UTC kun — bu texnik o'lchov, filial kalendari emas), funksiya va provider/model kesimi, filial kesimi, eng ko'p sarflagan 10 student, sabablar (`PROMPT_INJECTION`, `LIMIT_EXCEEDED`, `PROVIDER_ERROR`, `INVALID_OUTPUT`…) va oxirgi 20 muvaffaqiyatsiz so'rov. Scope: SA butun tashkilot (filialsiz so'rovlar ham), AD faqat o'z filiallari; davr ≤ 92 kun.
- `GET /api/ai/benchmarks/writing?from&to` — **o'qituvchi tuzatgan** baholashlar bo'yicha: namunalar soni, AI va o'qituvchi o'rtachasi, **bias** (AI − o'qituvchi: musbat bo'lsa AI yuqori qo'yadi), o'rtacha mutlaq xato, ±0.5 band ichidagi ulush va har mezon bo'yicha xato; **prompt versiyasi va model bo'yicha ajratilgan**, shuning uchun prompt o'zgarganda eski va yangi natija solishtiriladi.
- Limitlar API'si Sprint 1'dan bor (`GET/PUT/DELETE /api/ai/limits`), endi UI'ga ulandi.

**Qaror:** benchmark insholarni modelga **qayta yubormaydi** — mavjud natijalardan hisoblanadi. Qayta baholash har ishga tushirilganda pul sarflaydi va uni tasodifan bosib yuborish oson; TZ §55'dagi "prompt o'zgarganda qayta ishga tushirish" talabi esa prompt versiyasi bo'yicha ajratish orqali qoplanadi.

**UI** `/ai/usage` (Tashkilot → «AI monitoring», SA/AD), uchta tab:
- **Foydalanish** — kartalar, kunlik xarajat grafigi (bitta o'lchov, ikkinchi o'q yo'q; so'rovlar soni tooltipda va `aria-label`da, ostida jadval ko'rinishi), funksiya/provider/filial jadvallari, top studentlar, sabablar va oxirgi xatolar, provayderlar holati;
- **Limitlar** — katalog oralig'i va standarti ko'rsatilgan tahrirlash formasi (faqat SA yozadi, AD ko'radi), «Standartga qaytarish»;
- **Writing benchmark** — yuqoridagi jadval va uni qanday o'qish kerakligi izohi.

**Testlar:** 80/80 (2 unit + 78 integration). `AiUsageReportTests` (3): gateway orqali o'tgan so'rov hisobotda ko'rinadi (funksiya, provider, kunlar, top studentlar), AD ko'radi, operator va student 403; teskari sana va 92 kundan uzun davr 400; benchmark faqat o'qituvchi tuzatgan baholashni namuna sifatida oladi va mezon bo'yicha xatoni hisoblaydi.

**Dev tekshiruvi:** 62 so'rov, $0.0093, 34 745 token, o'rtacha 2.4 s; provider kesimi `deepseek:deepseek-chat` 34, `local:qwen3:8b` 19, `whisper:asr` 4.

**Dev tekshiruvi:** hujjat yuklandi → `local:bge-m3`, 1024 o'lchov, HNSW/GIN indekslari o'rnida; student savoliga DeepSeek 15.5 soniyada manbaga tayangan javob berdi; o'zbekcha mavzudan tashqari savol rad etildi.

**Keyinga qolgani:** javobni oqim (streaming) bilan ko'rsatish, model orqali ikkinchi injection tekshiruvi, hujjatlar uchun obyekt saqlash (S3/MinIO), embedding modeli almashganda ommaviy qayta indekslash amali.

### Sprint 4 — Speaking MVP (2026-09-16, bajarildi)

**Infratuzilma:** compose'ga `whisper` servisi (`onerahmet/openai-whisper-asr-webservice:v1.4.1-gpu`, `ASR_MODEL=small`, `faster_whisper`, CUDA) — dev mashinaning RTX 3060'ida, Ollama modellari yonida ~1.5 GB VRAM. Host porti **19000** (9000 ni Docker Desktop egallagan); API konteyneri `http://educenter-whisper:9000` manzilidan foydalanadi.

**Model** — migratsiya `AddSpeakingSessions`:
- `SpeakingSession` (`speaking_sessions`): rejim (`IeltsPart1/2/3`, `GeneralEnglish`, `FreeConversation`), mavzu, holat (`Active → Completed | Failed | Abandoned`), javoblar soni, **gapirilgan soniyalar** (kunlik limit shuni sanaydi), uchta mezon bandi + umumiy band, baholash JSON.
- `SpeakingTurn` (`speaking_turns`): examiner savoli, audio kaliti, transkript, davomiylik, **so'z/daqiqa** va **1 soniyadan uzun pauzalar soni**.

**Provider:** `ISpeechToTextProvider` — `WhisperAsrProvider` (`POST /asr?task=transcribe&language=en&output=json`, segment vaqtlari bilan) va testlar uchun deterministik `FakeSpeechToTextProvider`. Yangi provider turi `WhisperAsr` (chat modeli talab qilinmaydi), marshrut `Ai:Routes:Transcription`.

**Gateway:** `TranscribeAsync` — matn chaqiruvlari bilan bir xil qoidalar: kunlik limit tekshiruvi, provider zanjiri, xatolar jurnali; `AiRequest`da **audio soniyalari** saqlanadi, shuning uchun speaking daqiqalari limiti va hisobotlar bir manbadan oziqlanadi.

**Oqim** (`SpeakingService`): mashq boshlanganda examiner birinchi savolni beradi (`speaking.examiner.v1`), eski tugallanmagan sessiya avtomatik yopiladi (bir vaqtda bitta mashq). Har javob: audio validatsiya (webm/ogg/m4a/mp3/wav, ≤15 MB) → diskka saqlash → gateway orqali transkripsiya → metrikalar (Whisper segment vaqtlaridan so'z/daqiqa va pauzalar) → keyingi savol. Ovoz eshitilmasa yozuv o'chiriladi va tushunarli xato qaytadi. Yakunlashda `speaking.assessment.v1` uchta mezonni baholaydi — **Fluency & Coherence, Lexical Resource, Grammatical Range & Accuracy** — umumiy band Writing bilan bir xil IELTS qoidasida hisoblanadi.
- **Talaffuz baholanmaydi:** transkriptda bu ma'lumot yo'q. TZ §58/§59 bo'yicha bu Phase 6'ga qoldirilgan; UI ham buni ochiq aytadi. Nutq tezligi va pauzalar esa o'lchanadi va modelga dalil sifatida beriladi (baho o'rniga emas).

**API** `/api/ai/speaking` (`ai.speaking.use`, `ai` rate limit): `GET sessions`, `POST sessions`, `GET sessions/{id}`, `POST sessions/{id}/turns` (multipart audio + davomiylik), `POST sessions/{id}/finish`, `POST sessions/{id}/abandon`, `GET sessions/{id}/turns/{turnId}/audio`. Hammasi faqat o'z sessiyasi — begonaga 404.

**UI:** `/ai/speaking` (Student) — rejim tanlash, savol (brauzer TTS bilan «tinglash»), mikrofonda yozish (MediaRecorder, sekundomer), javob yuborilgach transkript va metrikalar, yakunda band + uchta mezon + kuchli/zaif tomonlar va tavsiyalar, yozuvlarni qayta eshitish (token bilan yuklanadi), mashqlar tarixi.

**Yo'l-yo'lakay topilgan ikki xato:**
- *Yopilgan oqim:* HTTP so'rovga berilgan oqim jo'natilgandan keyin yopiladi, shuning uchun zanjirdagi ikkinchi provider yopiq oqimni olardi. Endi audio bir marta xotiraga o'qiladi va har urinishga yangi nusxa beriladi.
- *DeepSeek JSON rejimi:* provayder promptda "json" so'zi bo'lishini talab qiladi — faqat `{...}` namunasini ko'rsatish yetmaydi va so'rov HTTP 400 bilan rad etiladi (examiner savollari va Tutor mavzu tekshiruvi jimgina local modelga tushib ketayotgan edi). `AiGateway` endi JSON so'ralganda promptda bu so'z yo'qligini o'zi tekshirib qo'shadi, ya'ni kelajakdagi promptlar ham himoyalangan.
- *Whisper javob formati:* `faster_whisper` dvigateli segmentlarni obyekt emas, `[id, seek, start, end, text, …]` massivi sifatida qaytaradi; provider ikkala shaklni ham o'qiydi.

**Testlar (Sprint 4):** 77/77 (2 unit + 75 integration). `SpeakingPracticeTests` (3): mashq navbat bilan o'tadi (transkript, so'z/daqiqa, keyingi savol, audio yuklab olish) va yakunda IELTS qoidasi bo'yicha band chiqadi; `.exe` audio 400, begona student 404, o'qituvchi 403, javobsiz yakunlash 409, bekor qilingan sessiyaga javob 409; yangi mashq eskisini avtomatik yopadi.
