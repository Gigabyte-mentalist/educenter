# DATABASE ERD
## Barcha entity va relationshiplar

**Hujjat turi:** Technical Specification (TZ hujjatining davomi)
**Versiya:** 1.0
**Bog'liq hujjatlar:** `00_TZ.md` (§47), `01_role_permission_matrix.md`

Diagramma modul bo'yicha bo'lingan (bitta katta ERD o'qish qiyin bo'lgani uchun). Har bir entity'da `organization_id` va (kerak bo'lsa) `branch_id` bo'ladi — bu ustunlar diagrammaning soddaligi uchun ko'rsatilmagan, lekin barcha jadvallarga tegishli (§48, Multi-tenant).

---

## 1. Identity & Access (Foundation)

```mermaid
erDiagram
    ORGANIZATION ||--o{ BRANCH : has
    ORGANIZATION ||--o{ USER : employs
    BRANCH ||--o{ USER : "assigned to"
    USER ||--o{ USER_ROLE : has
    ROLE ||--o{ USER_ROLE : "granted via"
    ROLE ||--o{ ROLE_PERMISSION : includes
    PERMISSION ||--o{ ROLE_PERMISSION : "granted by"
    USER ||--o{ USER_PERMISSION : "override"
    PERMISSION ||--o{ USER_PERMISSION : "override target"
    USER ||--o{ AUDIT_LOG : performs

    ORGANIZATION {
        uuid id PK
        string name
        string tenant_slug
        jsonb branding_config
        jsonb ai_limits
    }
    BRANCH {
        uuid id PK
        uuid organization_id FK
        string name
        string address
        string timezone
    }
    USER {
        uuid id PK
        uuid organization_id FK
        uuid branch_id FK "nullable, multi-branch via USER_BRANCH"
        string full_name
        string phone
        string email
        string password_hash
        string status
        timestamp created_at
    }
    ROLE {
        uuid id PK
        string code "SUPER_ADMIN|ADMIN|OPERATOR|TEACHER|SUPPORT_TEACHER|STUDENT|PARENT|DEVELOPER"
        string name
    }
    USER_ROLE {
        uuid user_id FK
        uuid role_id FK
    }
    PERMISSION {
        uuid id PK
        string code "students.view"
        string resource
        string action
    }
    ROLE_PERMISSION {
        uuid role_id FK
        uuid permission_id FK
        string default_scope
    }
    USER_PERMISSION {
        uuid user_id FK
        uuid permission_id FK
        string scope_override
    }
    AUDIT_LOG {
        uuid id PK
        uuid user_id FK
        string action
        string resource
        uuid resource_id
        jsonb old_value
        jsonb new_value
        string reason
        timestamp created_at
    }
```

---

## 2. People (Students, Parents, Teachers)

```mermaid
erDiagram
    USER ||--o| STUDENT : "is a"
    USER ||--o| PARENT : "is a"
    USER ||--o| TEACHER_PROFILE : "is a"
    PARENT ||--o{ STUDENT : "guardian of"
    STUDENT ||--o{ STUDENT_TIMELINE : has
    STUDENT ||--o{ SUPPORT_ASSIGNMENT : "receives"
    TEACHER_PROFILE ||--o{ STAFF_ASSIGNMENT : "assigned via"

    STUDENT {
        uuid id PK
        uuid user_id FK
        string student_number
        date joined_date
        string current_level
        string status "LEAD..ACTIVE..GRADUATED"
    }
    PARENT {
        uuid id PK
        uuid user_id FK
    }
    STUDENT_PARENT {
        uuid student_id FK
        uuid parent_id FK
    }
    TEACHER_PROFILE {
        uuid id PK
        uuid user_id FK
        string specialization
        jsonb kpi_weights_override
    }
    STAFF_ASSIGNMENT {
        uuid id PK
        uuid teacher_profile_id FK
        uuid branch_id FK
    }
    STUDENT_TIMELINE {
        uuid id PK
        uuid student_id FK
        string event_type
        jsonb payload
        timestamp occurred_at
    }
    SUPPORT_ASSIGNMENT {
        uuid id PK
        uuid student_id FK
        uuid support_teacher_id FK
        string reason
        string status
    }
```

---

## 3. Academic (Courses, Groups, Schedule, LMS)

```mermaid
erDiagram
    COURSE ||--o{ GROUP : "offered as"
    GROUP ||--o{ GROUP_STAFF : "staffed by"
    GROUP ||--o{ ENROLLMENT : contains
    STUDENT ||--o{ ENROLLMENT : "enrolled in"
    GROUP ||--o{ LESSON : schedules
    LESSON ||--o{ ATTENDANCE : records
    LESSON ||--o{ HOMEWORK : assigns
    HOMEWORK ||--o{ HOMEWORK_SUBMISSION : receives
    STUDENT ||--o{ HOMEWORK_SUBMISSION : submits
    GROUP ||--o{ EXAM : has
    EXAM ||--o{ EXAM_RESULT : produces
    STUDENT ||--o{ EXAM_RESULT : receives
    HOMEWORK ||--o{ RESOURCE : attaches

    COURSE {
        uuid id PK
        uuid organization_id FK
        string name
        string level_category
    }
    GROUP {
        uuid id PK
        uuid branch_id FK
        uuid course_id FK
        string name
        int capacity
        string status
    }
    GROUP_STAFF {
        uuid id PK
        uuid group_id FK
        uuid user_id FK
        string role "MAIN_TEACHER|SUPPORT_TEACHER"
    }
    ENROLLMENT {
        uuid id PK
        uuid student_id FK
        uuid group_id FK
        date enrolled_at
        string status "ACTIVE|FROZEN|TRANSFERRED|DROPPED"
    }
    LESSON {
        uuid id PK
        uuid group_id FK
        uuid room_id FK
        uuid teacher_id FK
        timestamp starts_at
        timestamp ends_at
        string topic
    }
    ATTENDANCE {
        uuid id PK
        uuid lesson_id FK
        uuid student_id FK
        string status "PRESENT|ABSENT|LATE|EXCUSED|ONLINE"
    }
    HOMEWORK {
        uuid id PK
        uuid lesson_id FK
        string title
        string type
        timestamp deadline
        int max_score
    }
    HOMEWORK_SUBMISSION {
        uuid id PK
        uuid homework_id FK
        uuid student_id FK
        string content_url
        int score
        uuid reviewed_by FK
        timestamp submitted_at
    }
    EXAM {
        uuid id PK
        uuid group_id FK
        string type "PLACEMENT|UNIT|MONTHLY|MOCK_IELTS|FINAL"
    }
    EXAM_RESULT {
        uuid id PK
        uuid exam_id FK
        uuid student_id FK
        decimal listening
        decimal reading
        decimal writing
        decimal speaking
        decimal overall
    }
    RESOURCE {
        uuid id PK
        string file_url
        string type
    }
```

---

## 4. CRM & Lifecycle

```mermaid
erDiagram
    LEAD ||--o{ CRM_CALL : "called via"
    LEAD ||--o{ CRM_TASK : generates
    LEAD ||--o| TRIAL_LESSON : books
    LEAD ||--o| STUDENT : "converts to"

    LEAD {
        uuid id PK
        uuid branch_id FK
        string full_name
        string phone
        string source
        string interested_course
        string status "LEAD..LOST"
        uuid operator_id FK
        string lost_reason
    }
    TRIAL_LESSON {
        uuid id PK
        uuid lead_id FK
        uuid group_id FK
        timestamp scheduled_at
        string attendance_status
    }
    CRM_CALL {
        uuid id PK
        uuid lead_id FK
        uuid operator_id FK
        string outcome "ANSWERED|NO_ANSWER|CALL_LATER|..."
        timestamp called_at
    }
    CRM_TASK {
        uuid id PK
        uuid lead_id FK "nullable"
        uuid student_id FK "nullable"
        uuid assigned_to FK
        string type "FOLLOW_UP|DEBT|ABSENCE|CALL_BACK"
        string status
        timestamp due_at
    }
```

---

## 5. Finance

```mermaid
erDiagram
    STUDENT ||--o{ INVOICE : billed
    INVOICE ||--o{ PAYMENT : "paid via"
    INVOICE ||--o| DISCOUNT : applies
    PAYMENT ||--o| REFUND : "may have"

    INVOICE {
        uuid id PK
        uuid student_id FK
        uuid enrollment_id FK
        decimal amount
        date due_date
        string status "PENDING|PAID|OVERDUE"
    }
    PAYMENT {
        uuid id PK
        uuid invoice_id FK
        decimal amount
        string method
        timestamp paid_at
    }
    DISCOUNT {
        uuid id PK
        uuid invoice_id FK
        string type "PERCENT|FIXED|PROMO_CODE"
        decimal value
        uuid approved_by FK
    }
    REFUND {
        uuid id PK
        uuid payment_id FK
        decimal amount
        string reason
        uuid approved_by FK
    }
```

---

## 6. Communication, Notification, Support, AI

```mermaid
erDiagram
    CHAT ||--o{ CHAT_MEMBER : includes
    CHAT ||--o{ MESSAGE : contains
    USER ||--o{ NOTIFICATION : receives
    STUDENT ||--o{ SUPPORT_TICKET : opens
    STUDENT ||--o{ AI_CONVERSATION : has
    AI_CONVERSATION ||--o{ AI_USAGE : logs
    STUDENT ||--o{ WRITING_EVALUATION : receives
    STUDENT ||--o{ SPEAKING_SESSION : records
    AUTOMATION_RULE ||--o{ CRM_TASK : "may trigger"

    CHAT {
        uuid id PK
        string type "DIRECT|GROUP|STAFF_CHANNEL"
        uuid group_id FK "nullable"
    }
    CHAT_MEMBER {
        uuid chat_id FK
        uuid user_id FK
    }
    MESSAGE {
        uuid id PK
        uuid chat_id FK
        uuid sender_id FK
        string content
        string attachment_url
        timestamp sent_at
    }
    NOTIFICATION {
        uuid id PK
        uuid user_id FK
        string event_type
        string channel "IN_APP|PUSH|TELEGRAM|EMAIL|SMS"
        jsonb payload
        boolean is_read
    }
    AUTOMATION_RULE {
        uuid id PK
        string trigger
        jsonb conditions
        jsonb actions
        boolean is_active
    }
    SUPPORT_TICKET {
        uuid id PK
        uuid student_id FK
        string type "COMPLAINT|SUPPORT_REQUEST"
        string status "ASSIGNED|IN_PROGRESS|RESOLVED"
    }
    AI_CONVERSATION {
        uuid id PK
        uuid student_id FK
        string module "TUTOR|WRITING|SPEAKING"
    }
    AI_USAGE {
        uuid id PK
        uuid ai_conversation_id FK
        int tokens
        decimal cost
        int latency_ms
    }
    WRITING_EVALUATION {
        uuid id PK
        uuid student_id FK
        decimal estimated_band
        jsonb criteria_scores
        uuid overridden_by FK "nullable"
    }
    SPEAKING_SESSION {
        uuid id PK
        uuid student_id FK
        string mode
        jsonb evaluation
        string audio_url
    }
```

---

## 7. Multi-tenant izohi

Har bir jadval (yuqoridagi diagrammalarda ko'rsatilmagan) quyidagi ustunlarga ega bo'ladi:

- `organization_id` — barcha business entitylarda majburiy;
- `branch_id` — filialga tegishli entitylarda (Group, Lesson, Lead, Invoice, va h.k.);
- PostgreSQL darajasida **Row-Level Security (RLS)** yoki EF Core **Global Query Filter** orqali tenant isolation ta'minlanadi.

## 8. Indekslash bo'yicha eslatma (Phase 1 uchun muhim)

- `(organization_id, branch_id)` — deyarli barcha jadvallarda composite index;
- `lead.status`, `enrollment.status`, `invoice.status` — CRM/Finance filtrlash uchun;
- `lesson(teacher_id, starts_at)` va `lesson(room_id, starts_at)` — Conflict Detector uchun kritik;
- `audit_log(resource, resource_id, created_at)`.
