using EduCenter.Modules.Identity.Domain;
using EduCenter.SharedKernel.Authorization;

namespace EduCenter.Infrastructure.Seed;

/// <summary>
/// Source of truth: docs/01_role_permission_matrix.md §3 (Phase 1 subset only — Organization/Branch,
/// Users, Groups/Courses/Schedule, Audit). CRM/Finance/AI rows are added when their modules land.
/// One row here = one cell in that matrix's tables.
/// </summary>
public static class PermissionSeedData
{
    public record PermissionGrant(string Code, string Resource, string Action, string RoleCode, ScopeType Scope);

    public static readonly (string Code, string Name)[] Roles =
    [
        (RoleCode.SuperAdmin, "Super Admin"),
        (RoleCode.Administrator, "Administrator"),
        (RoleCode.Operator, "Operator"),
        (RoleCode.Teacher, "Teacher"),
        (RoleCode.SupportTeacher, "Support Teacher"),
        (RoleCode.Student, "Student"),
        (RoleCode.Parent, "Parent"),
        (RoleCode.Developer, "Developer")
    ];

    public static readonly PermissionGrant[] Grants =
    [
        Grant("resources.view", "resources", "view", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("resources.view", "resources", "view", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("resources.view", "resources", "view", RoleCode.Operator, ScopeType.MyBranches),
        Grant("resources.view", "resources", "view", RoleCode.Teacher, ScopeType.MyGroups),
        Grant("resources.view", "resources", "view", RoleCode.SupportTeacher, ScopeType.AssignedGroups),
        Grant("resources.view", "resources", "view", RoleCode.Student, ScopeType.Self),
        Grant("resources.view", "resources", "view", RoleCode.Parent, ScopeType.MyChildren),
        Grant("resources.create", "resources", "create", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("resources.create", "resources", "create", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("resources.create", "resources", "create", RoleCode.Teacher, ScopeType.MyGroups),
        Grant("resources.create", "resources", "create", RoleCode.SupportTeacher, ScopeType.AssignedGroups),
        // A teacher may remove the material they attached to their own lesson, so this follows resources.create
        // rather than sitting with the organization-wide deletes.
        Grant("resources.delete", "resources", "delete", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("resources.delete", "resources", "delete", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("resources.delete", "resources", "delete", RoleCode.Teacher, ScopeType.MyGroups),
        Grant("resources.delete", "resources", "delete", RoleCode.SupportTeacher, ScopeType.AssignedGroups),
        // §3.7 Finance (docs/05_phase3_technical_plan.md §1). The matrix's "OP: payments.view (debt only)" is its own
        // permission, finance.debt.view — a scope can't express "debt but not payments", and role-name checks are banned.
        Grant("payments.view", "payments", "view", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("payments.view", "payments", "view", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("payments.view", "payments", "view", RoleCode.Student, ScopeType.Self),
        Grant("payments.view", "payments", "view", RoleCode.Parent, ScopeType.MyChildren),
        Grant("finance.debt.view", "finance", "debt.view", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("finance.debt.view", "finance", "debt.view", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("finance.debt.view", "finance", "debt.view", RoleCode.Operator, ScopeType.MyBranches),
        Grant("payments.create", "payments", "create", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("payments.create", "payments", "create", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("payments.edit", "payments", "edit", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("payments.edit", "payments", "edit", RoleCode.Administrator, ScopeType.MyBranches),
        // §3.7 Finance — Sprint 2. Branch-scoped holders of discount.apply / refund.approve are capped by FinanceOptions;
        // promo.manage (creating pre-approved codes) is organization-wide only.
        Grant("discount.apply", "discount", "apply", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("discount.apply", "discount", "apply", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("refund.approve", "refund", "approve", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("refund.approve", "refund", "approve", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("finance.reports.view", "finance", "reports.view", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("finance.reports.view", "finance", "reports.view", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("promo.manage", "promo", "manage", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        // §3.8 Communication / Chat (docs/00_TZ.md §22). Who can be messaged 1-to-1 follows the scope:
        // teachers — their students (+ parents) and colleagues, students — their teachers, parents — their children's
        // teachers and branch administrators.
        Grant("chat.1to1.use", "chat", "1to1", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("chat.1to1.use", "chat", "1to1", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("chat.1to1.use", "chat", "1to1", RoleCode.Operator, ScopeType.MyBranches),
        Grant("chat.1to1.use", "chat", "1to1", RoleCode.Teacher, ScopeType.MyGroups),
        Grant("chat.1to1.use", "chat", "1to1", RoleCode.SupportTeacher, ScopeType.AssignedGroups),
        Grant("chat.1to1.use", "chat", "1to1", RoleCode.Student, ScopeType.Self),
        Grant("chat.1to1.use", "chat", "1to1", RoleCode.Parent, ScopeType.MyChildren),
        Grant("chat.group.use", "chat", "group", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("chat.group.use", "chat", "group", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("chat.group.use", "chat", "group", RoleCode.Teacher, ScopeType.MyGroups),
        Grant("chat.group.use", "chat", "group", RoleCode.SupportTeacher, ScopeType.AssignedGroups),
        Grant("chat.group.use", "chat", "group", RoleCode.Student, ScopeType.Self),
        Grant("chat.moderate", "chat", "moderate", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("chat.moderate", "chat", "moderate", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("chat.moderate", "chat", "moderate", RoleCode.Teacher, ScopeType.MyGroups),
        // chat.moderate deletes one message; chat.delete removes the whole conversation and everything said in
        // it, which is why a Teacher who moderates their own group chat does not get it.
        Grant("chat.delete", "chat", "delete", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("chat.delete", "chat", "delete", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("staff.channels.access", "chat", "staff_channels", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("staff.channels.access", "chat", "staff_channels", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("staff.channels.access", "chat", "staff_channels", RoleCode.Operator, ScopeType.MyBranches),
        Grant("staff.channels.access", "chat", "staff_channels", RoleCode.Teacher, ScopeType.MyBranches),
        Grant("staff.channels.access", "chat", "staff_channels", RoleCode.SupportTeacher, ScopeType.MyBranches),
        // §3.13 Automation Engine (docs/00_TZ.md §40). Rules are organization-wide: administrators may look,
        // changing thresholds/actions is organization-level.
        Grant("automation.view", "automation", "view", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("automation.view", "automation", "view", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("automation.manage", "automation", "manage", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        // §3.11 AI Platform (docs/06_phase4_technical_plan.md §2). The matrix's ai.writing.use meant "submit" for a student
        // and "review" for teaching staff — one code can't carry both without a role-name check, so review is its own code.
        // Speaking and the Knowledge Base had no permission in the matrix and get their own.
        Grant("ai.tutor.use", "ai", "tutor.use", RoleCode.Student, ScopeType.Self),
        Grant("ai.writing.use", "ai", "writing.use", RoleCode.Student, ScopeType.Self),
        Grant("ai.writing.review", "ai", "writing.review", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("ai.writing.review", "ai", "writing.review", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("ai.writing.review", "ai", "writing.review", RoleCode.Teacher, ScopeType.MyGroups),
        Grant("ai.writing.review", "ai", "writing.review", RoleCode.SupportTeacher, ScopeType.AssignedGroups),
        Grant("ai.writing.override", "ai", "writing.override", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("ai.writing.override", "ai", "writing.override", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("ai.writing.override", "ai", "writing.override", RoleCode.Teacher, ScopeType.MyGroups),
        Grant("ai.speaking.use", "ai", "speaking.use", RoleCode.Student, ScopeType.Self),
        // Reading a student's finished practice — the assessment, the transcript and the recordings. The same reach as the Writing
        // review (the people who teach the student), and no one else: an operator has no business listening to a student's voice.
        Grant("ai.speaking.review", "ai", "speaking.review", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("ai.speaking.review", "ai", "speaking.review", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("ai.speaking.review", "ai", "speaking.review", RoleCode.Teacher, ScopeType.MyGroups),
        Grant("ai.speaking.review", "ai", "speaking.review", RoleCode.SupportTeacher, ScopeType.AssignedGroups),
        // Correcting the AI's bands after listening: the same people as ai.writing.override — the support teacher reads, the teacher decides.
        Grant("ai.speaking.override", "ai", "speaking.override", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("ai.speaking.override", "ai", "speaking.override", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("ai.speaking.override", "ai", "speaking.override", RoleCode.Teacher, ScopeType.MyGroups),
        Grant("ai.knowledge.manage", "ai", "knowledge.manage", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("ai.knowledge.manage", "ai", "knowledge.manage", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("ai.knowledge.manage", "ai", "knowledge.manage", RoleCode.Teacher, ScopeType.MyGroups),
        Grant("ai.usage.view", "ai", "usage.view", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("ai.usage.view", "ai", "usage.view", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("ai.limits.edit", "ai", "limits.edit", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        // §3.9 Support Needed Engine (docs/00_TZ.md §8). "Teacher ✓ (propose)" in the matrix is its own
        // permission so a teacher can raise a recommendation without being able to assign support.
        Grant("support.recommend.view", "support", "recommend.view", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("support.recommend.view", "support", "recommend.view", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("support.recommend.view", "support", "recommend.view", RoleCode.Teacher, ScopeType.MyGroups),
        Grant("support.recommend.view", "support", "recommend.view", RoleCode.SupportTeacher, ScopeType.AssignedGroups),
        Grant("support.propose", "support", "propose", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("support.propose", "support", "propose", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("support.propose", "support", "propose", RoleCode.Teacher, ScopeType.MyGroups),
        Grant("support.assign", "support", "assign", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("support.assign", "support", "assign", RoleCode.Administrator, ScopeType.MyBranches),
        // §3.1 Organization / Branch
        Grant("branch.view", "branch", "view", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("branch.view", "branch", "view", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("branch.view", "branch", "view", RoleCode.Operator, ScopeType.MyBranches),
        Grant("branch.view", "branch", "view", RoleCode.Teacher, ScopeType.MyBranches),
        Grant("branch.view", "branch", "view", RoleCode.SupportTeacher, ScopeType.AssignedGroups),
        Grant("branch.create", "branch", "create", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("branch.edit", "branch", "edit", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("branch.edit", "branch", "edit", RoleCode.Administrator, ScopeType.MyBranches),
        // Closing a branch removes the container every group, lesson and payment in it hangs off, so unlike
        // branch.edit it stays organization-wide: an Administrator scoped to a branch must not be able to
        // delete the very branch that bounds their own scope.
        Grant("branch.delete", "branch", "delete", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("org.settings.edit", "org", "settings.edit", RoleCode.SuperAdmin, ScopeType.AllOrganization),

        // §3.2 Users & Staff
        Grant("users.view", "users", "view", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("users.view", "users", "view", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("users.view", "users", "view", RoleCode.Operator, ScopeType.MyBranches),
        Grant("users.view", "users", "view", RoleCode.Teacher, ScopeType.MyGroups),
        Grant("users.view", "users", "view", RoleCode.SupportTeacher, ScopeType.AssignedGroups),
        Grant("users.view", "users", "view", RoleCode.Student, ScopeType.Self),
        Grant("users.view", "users", "view", RoleCode.Parent, ScopeType.MyChildren),
        Grant("users.create", "users", "create", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("users.create", "users", "create", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("users.edit", "users", "edit", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("users.edit", "users", "edit", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("users.edit", "users", "edit", RoleCode.Teacher, ScopeType.Self),
        Grant("users.edit", "users", "edit", RoleCode.SupportTeacher, ScopeType.Self),
        Grant("users.edit", "users", "edit", RoleCode.Student, ScopeType.Self),
        Grant("users.edit", "users", "edit", RoleCode.Parent, ScopeType.Self),
        Grant("users.deactivate", "users", "deactivate", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("users.deactivate", "users", "deactivate", RoleCode.Administrator, ScopeType.MyBranches),
        // Deactivation is the everyday tool and keeps the person's history readable; deletion erases the row
        // and is only offered when nothing references it, which is why it sits one level higher than
        // users.deactivate and is never granted to an Operator.
        Grant("users.delete", "users", "delete", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("users.delete", "users", "delete", RoleCode.Administrator, ScopeType.MyBranches),
        // A distinct code from users.edit on purpose: users.edit's Self scope is how Teacher/Student/Parent edit
        // their own profile, and re-using it here would let them reset their own password through this endpoint
        // instead of POST /api/auth/change-password (S01.05) — Operator holds neither.
        Grant("users.password.reset", "users", "password.reset", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("users.password.reset", "users", "password.reset", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("role.assign", "role", "assign", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("role.assign", "role", "assign", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("permission.policy.edit", "permission", "policy.edit", RoleCode.SuperAdmin, ScopeType.AllOrganization),

        // A teacher's portfolio (S05.02): IELTS, certificates, experience — personal and partly documents, so it is the teacher's own
        // to keep and the office's to read. The Operator never sees it. A teacher fills in their own; an administrator may edit a
        // branch teacher's (and only that — the controller also applies the S01.04 rule that keeps them off a Super Admin's).
        // Verifying is the administrators' alone: a teacher confirming their own certificate would confirm nothing.
        Grant("teacher.portfolio.view", "teacher.portfolio", "view", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("teacher.portfolio.view", "teacher.portfolio", "view", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("teacher.portfolio.view", "teacher.portfolio", "view", RoleCode.Teacher, ScopeType.Self),
        Grant("teacher.portfolio.view", "teacher.portfolio", "view", RoleCode.SupportTeacher, ScopeType.Self),
        Grant("teacher.portfolio.edit", "teacher.portfolio", "edit", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("teacher.portfolio.edit", "teacher.portfolio", "edit", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("teacher.portfolio.edit", "teacher.portfolio", "edit", RoleCode.Teacher, ScopeType.Self),
        Grant("teacher.portfolio.edit", "teacher.portfolio", "edit", RoleCode.SupportTeacher, ScopeType.Self),
        Grant("teacher.portfolio.verify", "teacher.portfolio", "verify", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("teacher.portfolio.verify", "teacher.portfolio", "verify", RoleCode.Administrator, ScopeType.MyBranches),
        // "Mening o'qituvchim": a student reads the confirmed part of the portfolio of whoever teaches their group, a parent of whoever
        // teaches their child's — no scan, no reference. Its own code, so the full portfolio (view) never reaches them.
        Grant("teacher.portfolio.view.public", "teacher.portfolio", "view.public", RoleCode.Student, ScopeType.Self),
        Grant("teacher.portfolio.view.public", "teacher.portfolio", "view.public", RoleCode.Parent, ScopeType.MyChildren),

        // §3.4 Groups / Courses / Schedule
        Grant("groups.view", "groups", "view", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("groups.view", "groups", "view", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("groups.view", "groups", "view", RoleCode.Operator, ScopeType.MyBranches),
        Grant("groups.view", "groups", "view", RoleCode.Teacher, ScopeType.MyGroups),
        Grant("groups.view", "groups", "view", RoleCode.SupportTeacher, ScopeType.AssignedGroups),
        Grant("groups.view", "groups", "view", RoleCode.Student, ScopeType.Self),
        Grant("groups.view", "groups", "view", RoleCode.Parent, ScopeType.MyChildren),
        Grant("groups.create", "groups", "create", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("groups.create", "groups", "create", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("groups.edit", "groups", "edit", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("groups.edit", "groups", "edit", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("groups.delete", "groups", "delete", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("groups.delete", "groups", "delete", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("courses.manage", "courses", "manage", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("courses.manage", "courses", "manage", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("schedule.view", "schedule", "view", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("schedule.view", "schedule", "view", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("schedule.view", "schedule", "view", RoleCode.Operator, ScopeType.MyBranches),
        Grant("schedule.view", "schedule", "view", RoleCode.Teacher, ScopeType.MyGroups),
        Grant("schedule.view", "schedule", "view", RoleCode.SupportTeacher, ScopeType.AssignedGroups),
        Grant("schedule.view", "schedule", "view", RoleCode.Student, ScopeType.Self),
        Grant("schedule.view", "schedule", "view", RoleCode.Parent, ScopeType.MyChildren),
        Grant("schedule.edit", "schedule", "edit", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("schedule.edit", "schedule", "edit", RoleCode.Administrator, ScopeType.MyBranches),
        // The classrooms themselves (S07.03): who may add, rename, resize or switch off a room. Seeing them and when they are taken follows
        // schedule.view; putting a lesson in one follows schedule.edit. An Administrator manages the rooms of their own branches only.
        Grant("rooms.manage", "rooms", "manage", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("rooms.manage", "rooms", "manage", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("teacher.replace", "teacher", "replace", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("teacher.replace", "teacher", "replace", RoleCode.Administrator, ScopeType.MyBranches),

        // §3.10 Audit (view only in Phase 1 — analytics/KPI land with later modules)
        Grant("audit.log.view", "audit", "log.view", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("audit.log.view", "audit", "log.view", RoleCode.Administrator, ScopeType.MyBranches),

        // §3.6 CRM (Leads, Trial, Calls) — Phase 2
        Grant("leads.view", "leads", "view", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("leads.view", "leads", "view", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("leads.view", "leads", "view", RoleCode.Operator, ScopeType.MyBranches),
        Grant("leads.create", "leads", "create", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("leads.create", "leads", "create", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("leads.create", "leads", "create", RoleCode.Operator, ScopeType.MyBranches),
        Grant("leads.edit", "leads", "edit", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("leads.edit", "leads", "edit", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("leads.edit", "leads", "edit", RoleCode.Operator, ScopeType.MyBranches),
        Grant("crm.task.view", "crm", "task.view", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("crm.task.view", "crm", "task.view", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("crm.task.view", "crm", "task.view", RoleCode.Operator, ScopeType.Self),
        // Matrix §3.6 lists crm.call.log for the Operator only; Super Admin / Administrator are added
        // so a manager covering the phones (or demoing the system) isn't locked out of logging a call.
        Grant("crm.call.log", "crm", "call.log", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("crm.call.log", "crm", "call.log", RoleCode.Administrator, ScopeType.MyBranches),
        // MyBranches (not Self): logging a call is a per-lead action, and a lead belongs to a branch —
        // "Self" would resolve to no branch at all and the operator could never log anything.
        Grant("crm.call.log", "crm", "call.log", RoleCode.Operator, ScopeType.MyBranches),
        Grant("trial.book", "trial", "book", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("trial.book", "trial", "book", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("trial.book", "trial", "book", RoleCode.Operator, ScopeType.MyBranches),
        // §13 Placement Test — same operator-administered shape as trial.book.
        Grant("placement.test.log", "placement", "test.log", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("placement.test.log", "placement", "test.log", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("placement.test.log", "placement", "test.log", RoleCode.Operator, ScopeType.MyBranches),

        // §3.3 Students — Phase 2 Sprint 2 (conversion + basic view)
        Grant("students.view", "students", "view", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("students.view", "students", "view", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("students.view", "students", "view", RoleCode.Operator, ScopeType.MyBranches),
        Grant("students.view", "students", "view", RoleCode.Teacher, ScopeType.MyGroups),
        Grant("students.view", "students", "view", RoleCode.SupportTeacher, ScopeType.AssignedGroups),
        Grant("students.view", "students", "view", RoleCode.Student, ScopeType.Self),
        Grant("students.view", "students", "view", RoleCode.Parent, ScopeType.MyChildren),
        Grant("students.create", "students", "create", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("students.create", "students", "create", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("students.create", "students", "create", RoleCode.Operator, ScopeType.MyBranches),
        // Correcting a registered student's profile. The matrix gives the Operator "limited (contact info)", which a
        // scope cannot express, so it is its own code (the same way finance.debt.view stands for "debt only"):
        // students.edit changes anything on the profile, students.contact.edit only the name and the phone number.
        Grant("students.edit", "students", "edit", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("students.edit", "students", "edit", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("students.contact.edit", "students", "contact.edit", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("students.contact.edit", "students", "contact.edit", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("students.contact.edit", "students", "contact.edit", RoleCode.Operator, ScopeType.MyBranches),

        // §24 Parent Portal — linking/unlinking a guardian account to a student (docs/21.09-audit-jurnali S01.02).
        Grant("parents.manage", "parents", "manage", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("parents.manage", "parents", "manage", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("parents.manage", "parents", "manage", RoleCode.Operator, ScopeType.MyBranches),

        // §3.5 Attendance / Homework — Phase 2 Sprint 3
        Grant("attendance.view", "attendance", "view", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("attendance.view", "attendance", "view", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("attendance.view", "attendance", "view", RoleCode.Operator, ScopeType.MyBranches),
        Grant("attendance.view", "attendance", "view", RoleCode.Teacher, ScopeType.MyGroups),
        Grant("attendance.view", "attendance", "view", RoleCode.SupportTeacher, ScopeType.AssignedGroups),
        Grant("attendance.view", "attendance", "view", RoleCode.Student, ScopeType.Self),
        Grant("attendance.view", "attendance", "view", RoleCode.Parent, ScopeType.MyChildren),
        Grant("attendance.mark", "attendance", "mark", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("attendance.mark", "attendance", "mark", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("attendance.mark", "attendance", "mark", RoleCode.Teacher, ScopeType.MyGroups),
        Grant("homework.view", "homework", "view", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("homework.view", "homework", "view", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("homework.view", "homework", "view", RoleCode.Teacher, ScopeType.MyGroups),
        Grant("homework.view", "homework", "view", RoleCode.SupportTeacher, ScopeType.AssignedGroups),
        Grant("homework.view", "homework", "view", RoleCode.Student, ScopeType.Self),
        Grant("homework.view", "homework", "view", RoleCode.Parent, ScopeType.MyChildren),
        Grant("homework.create", "homework", "create", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("homework.create", "homework", "create", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("homework.create", "homework", "create", RoleCode.Teacher, ScopeType.MyGroups),
        Grant("homework.create", "homework", "create", RoleCode.SupportTeacher, ScopeType.AssignedGroups),
        Grant("homework.review", "homework", "review", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("homework.review", "homework", "review", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("homework.review", "homework", "review", RoleCode.Teacher, ScopeType.MyGroups),
        Grant("homework.review", "homework", "review", RoleCode.SupportTeacher, ScopeType.AssignedGroups),
        Grant("homework.submit", "homework", "submit", RoleCode.Student, ScopeType.Self),
        Grant("exams.view", "exams", "view", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("exams.view", "exams", "view", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("exams.view", "exams", "view", RoleCode.Operator, ScopeType.MyBranches),
        Grant("exams.view", "exams", "view", RoleCode.Teacher, ScopeType.MyGroups),
        Grant("exams.view", "exams", "view", RoleCode.SupportTeacher, ScopeType.AssignedGroups),
        Grant("exams.view", "exams", "view", RoleCode.Student, ScopeType.Self),
        Grant("exams.view", "exams", "view", RoleCode.Parent, ScopeType.MyChildren),
        Grant("exams.create", "exams", "create", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("exams.create", "exams", "create", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("exams.create", "exams", "create", RoleCode.Teacher, ScopeType.MyGroups),
        Grant("exams.score.edit", "exams", "score.edit", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("exams.score.edit", "exams", "score.edit", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("exams.score.edit", "exams", "score.edit", RoleCode.Teacher, ScopeType.MyGroups),

        // §20-21 Student lifecycle. Moving a student between groups or stopping their billing is an office
        // decision, so it stops at the branch: a teacher who can see a student still cannot transfer them.
        Grant("student.transfer", "student", "transfer", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("student.transfer", "student", "transfer", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("student.freeze", "student", "freeze", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("student.freeze", "student", "freeze", RoleCode.Administrator, ScopeType.MyBranches),
        // Enrolling into an extra group, dropping one, or graduating — the matrix gives the Operator "initiate"
        // here (unlike transfer/freeze, which the office keeps to Administrator), so it is granted too.
        Grant("student.enroll", "student", "enroll", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("student.enroll", "student", "enroll", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("student.enroll", "student", "enroll", RoleCode.Operator, ScopeType.MyBranches),

        // Student 360° Documents tab (audit §3.7). Viewing follows the same people who can open the profile
        // at all, plus the student and their parent seeing their own paperwork; uploading/deleting stays with
        // the office staff who actually handle contracts and receipts — a teacher never manages these.
        Grant("student.documents.view", "student.documents", "view", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("student.documents.view", "student.documents", "view", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("student.documents.view", "student.documents", "view", RoleCode.Operator, ScopeType.MyBranches),
        Grant("student.documents.view", "student.documents", "view", RoleCode.Student, ScopeType.Self),
        Grant("student.documents.view", "student.documents", "view", RoleCode.Parent, ScopeType.MyChildren),
        Grant("student.documents.manage", "student.documents", "manage", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("student.documents.manage", "student.documents", "manage", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("student.documents.manage", "student.documents", "manage", RoleCode.Operator, ScopeType.MyBranches),

        // §5 Dashboards. Every role holds the same code; the scope is what makes the super admin's copy of the
        // screen the whole organization and a parent's copy their own children. The Developer's landing screen is
        // system.health.view instead — there is nothing academic on it.
        Grant("dashboard.view", "dashboard", "view", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("dashboard.view", "dashboard", "view", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("dashboard.view", "dashboard", "view", RoleCode.Operator, ScopeType.MyBranches),
        Grant("dashboard.view", "dashboard", "view", RoleCode.Teacher, ScopeType.MyGroups),
        Grant("dashboard.view", "dashboard", "view", RoleCode.SupportTeacher, ScopeType.AssignedGroups),
        Grant("dashboard.view", "dashboard", "view", RoleCode.Student, ScopeType.Self),
        Grant("dashboard.view", "dashboard", "view", RoleCode.Parent, ScopeType.MyChildren),
        // The league table of a branch's teachers on its dashboard (S05.03): attendance, marking speed and retention side by side, which
        // is a management view. The Operator holds dashboard.view (their branch's numbers) but not this; money on the same screen is
        // gated by payments.view in the same way.
        Grant("dashboard.teachers.view", "dashboard", "teachers.view", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("dashboard.teachers.view", "dashboard", "teachers.view", RoleCode.Administrator, ScopeType.MyBranches),

        // §3.12 Developer / Observability
        Grant("system.health.view", "system", "health.view", RoleCode.Developer, ScopeType.AllOrganization),
        Grant("logs.view", "logs", "view", RoleCode.Developer, ScopeType.AllOrganization),
        Grant("deployments.view", "deployments", "view", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("deployments.view", "deployments", "view", RoleCode.Developer, ScopeType.AllOrganization),
        // The rest of docs/00_TZ.md §3.8's list — request latency and error rate, slow queries, storage, background jobs.
        // Each is read-only and touches no student data, which is the whole reason a Developer may hold them by default.
        Grant("system.metrics.view", "system", "metrics.view", RoleCode.Developer, ScopeType.AllOrganization),
        Grant("system.jobs.view", "system", "jobs.view", RoleCode.Developer, ScopeType.AllOrganization),
        // Production data: the Developer asks, an organization-wide approver decides (matrix §3.12, §4 rule 4). What can be
        // asked for is listed in DataAccessRequest.GrantableCodes — read access to academic records, never finance.
        Grant("production.data.request", "production.data", "request", RoleCode.Developer, ScopeType.AllOrganization),
        Grant("production.data.approve", "production.data", "approve", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        // Announcements (S06.05, matrix §3.13). Everyone the centre talks to reads what concerns their branch, their groups or their children;
        // only the people who run a branch publish. An administrator's scope is their own branches, so they cannot address the whole organization.
        Grant("announcements.view", "announcements", "view", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("announcements.view", "announcements", "view", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("announcements.view", "announcements", "view", RoleCode.Operator, ScopeType.MyBranches),
        Grant("announcements.view", "announcements", "view", RoleCode.Teacher, ScopeType.MyGroups),
        Grant("announcements.view", "announcements", "view", RoleCode.SupportTeacher, ScopeType.AssignedGroups),
        Grant("announcements.view", "announcements", "view", RoleCode.Student, ScopeType.Self),
        Grant("announcements.view", "announcements", "view", RoleCode.Parent, ScopeType.MyChildren),
        Grant("announcements.manage", "announcements", "manage", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("announcements.manage", "announcements", "manage", RoleCode.Administrator, ScopeType.MyBranches),
        // Complaints (S06.07, matrix §3.14, TZ §41). Anyone the centre serves may file one — a student about their own studies, a parent about a child,
        // a teacher about what they see — and always sees their own; the office (operator, administrator) files on a caller's behalf and reads the
        // branch's; only the ones who run a branch take, move and settle them. A Super Admin files nothing of their own here: their copy of the
        // screen is the whole organization's board.
        Grant("complaints.create", "complaints", "create", RoleCode.Student, ScopeType.Self),
        Grant("complaints.create", "complaints", "create", RoleCode.Parent, ScopeType.MyChildren),
        Grant("complaints.create", "complaints", "create", RoleCode.Teacher, ScopeType.MyGroups),
        Grant("complaints.create", "complaints", "create", RoleCode.Operator, ScopeType.MyBranches),
        Grant("complaints.create", "complaints", "create", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("complaints.view", "complaints", "view", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("complaints.view", "complaints", "view", RoleCode.Administrator, ScopeType.MyBranches),
        Grant("complaints.view", "complaints", "view", RoleCode.Operator, ScopeType.MyBranches),
        Grant("complaints.manage", "complaints", "manage", RoleCode.SuperAdmin, ScopeType.AllOrganization),
        Grant("complaints.manage", "complaints", "manage", RoleCode.Administrator, ScopeType.MyBranches),
    ];

    private static PermissionGrant Grant(string code, string resource, string action, string roleCode, ScopeType scope) =>
        new(code, resource, action, roleCode, scope);
}
