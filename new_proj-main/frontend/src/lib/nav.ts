import {
  Activity,
  LayoutDashboard,
  ListChecks,
  Users2,
  Building2,
  UsersRound,
  GraduationCap,
  CalendarClock,
  ScrollText,
  Award,
  Contact,
  DoorOpen,
  Gauge,
  KeyRound,
  LifeBuoy,
  Library,
  Megaphone,
  MessageSquareWarning,
  MessagesSquare,
  Mic,
  PenLine,
  Settings,
  ShieldCheck,
  Sparkles,
  Wallet,
  Workflow,
  type LucideIcon,
} from "lucide-react";

export type RoleCode =
  | "SUPER_ADMIN"
  | "ADMINISTRATOR"
  | "OPERATOR"
  | "TEACHER"
  | "SUPPORT_TEACHER"
  | "STUDENT"
  | "PARENT"
  | "DEVELOPER";

export interface NavItem {
  href: string;
  label: string;
  icon: LucideIcon;
  keywords?: string[];
  /** Roles that get this entry. Mirrors docs/01_role_permission_matrix.md — the backend still enforces
   * every permission; this only keeps the sidebar honest so a teacher isn't shown "Filiallar" and a 403. */
  roles: RoleCode[];
}

export interface NavGroup {
  label: string;
  items: NavItem[];
}

const MANAGEMENT: RoleCode[] = ["SUPER_ADMIN", "ADMINISTRATOR", "OPERATOR"];
const ACADEMIC: RoleCode[] = ["SUPER_ADMIN", "ADMINISTRATOR", "OPERATOR", "TEACHER", "SUPPORT_TEACHER", "STUDENT", "PARENT"];

const EVERYONE: RoleCode[] = [
  "SUPER_ADMIN", "ADMINISTRATOR", "OPERATOR", "TEACHER", "SUPPORT_TEACHER", "STUDENT", "PARENT", "DEVELOPER",
];

export const NAV_GROUPS: NavGroup[] = [
  {
    // No heading: this group holds one item whose own label already says what it is.
    label: "",
    items: [
      { href: "/dashboard", label: "Bosh sahifa", icon: LayoutDashboard, keywords: ["dashboard", "bosh", "asosiy", "kpi", "statistika", "monitoring"], roles: EVERYONE },
    ],
  },
  {
    label: "AI",
    items: [
      { href: "/ai/tutor", label: "AI Tutor", icon: Sparkles, keywords: ["ai", "tutor", "savol", "grammar", "yordam"], roles: ["STUDENT"] },
      { href: "/ai/writing", label: "AI Writing", icon: PenLine, keywords: ["ai", "writing", "essay", "insho", "ielts"], roles: ["STUDENT"] },
      { href: "/ai/writing/reviews", label: "Writing tekshiruvi", icon: PenLine, keywords: ["ai", "writing", "insho", "baho", "tekshir", "review"], roles: ["SUPER_ADMIN", "ADMINISTRATOR", "TEACHER", "SUPPORT_TEACHER"] },
      { href: "/ai/speaking/reviews", label: "Speaking tekshiruvi", icon: Mic, keywords: ["ai", "speaking", "gapirish", "ovoz", "baho", "tekshir", "review"], roles: ["SUPER_ADMIN", "ADMINISTRATOR", "TEACHER", "SUPPORT_TEACHER"] },
      { href: "/ai/speaking", label: "AI Speaking", icon: Mic, keywords: ["ai", "speaking", "gapirish", "ovoz", "ielts"], roles: ["STUDENT"] },
      { href: "/ai/knowledge", label: "Bilim bazasi", icon: Library, keywords: ["ai", "knowledge", "rag", "material", "baza"], roles: ["SUPER_ADMIN", "ADMINISTRATOR", "TEACHER"] },
      { href: "/ai/usage", label: "AI monitoring", icon: Activity, keywords: ["ai", "usage", "xarajat", "cost", "limit", "benchmark", "monitoring"], roles: ["SUPER_ADMIN", "ADMINISTRATOR"] },
    ],
  },
  {
    label: "Muloqot",
    items: [
      { href: "/chat", label: "Xabarlar", icon: MessagesSquare, keywords: ["chat", "xabar", "suhbat", "kanal", "message"], roles: ACADEMIC },
      { href: "/announcements", label: "E'lonlar", icon: Megaphone, keywords: ["announcement", "e'lon", "elon", "xabar", "yangilik", "bayram"], roles: ACADEMIC },
      // Everyone who may file one (and so reads their own) plus the office that works them; a support teacher and a developer have no part in it.
      { href: "/complaints", label: "Shikoyatlar", icon: MessageSquareWarning, keywords: ["complaint", "shikoyat", "norozilik", "murojaat", "taklif"], roles: [...MANAGEMENT, "TEACHER", "STUDENT", "PARENT"] },
    ],
  },
  {
    label: "CRM",
    items: [
      { href: "/crm/tasks", label: "Bugungi vazifalar", icon: ListChecks, keywords: ["call queue", "task", "vazifa"], roles: MANAGEMENT },
      { href: "/leads", label: "Lidlar", icon: Contact, keywords: ["lead", "mijoz"], roles: MANAGEMENT },
    ],
  },
  {
    label: "Moliya",
    items: [
      { href: "/finance", label: "To'lovlar va qarzlar", icon: Wallet, keywords: ["finance", "payment", "to'lov", "qarz", "invoice"], roles: MANAGEMENT },
    ],
  },
  {
    label: "Ta'lim",
    items: [
      { href: "/students", label: "Studentlar", icon: GraduationCap, keywords: ["student", "o'quvchi"], roles: ACADEMIC },
      { href: "/teachers", label: "O‘qituvchilar", icon: Users2, keywords: ["teacher", "ustoz"], roles: ["SUPER_ADMIN", "ADMINISTRATOR", "TEACHER", "SUPPORT_TEACHER"] },
      { href: "/groups", label: "Guruhlar", icon: UsersRound, keywords: ["group", "kurs"], roles: ACADEMIC },
      { href: "/schedule", label: "Dars jadvali", icon: CalendarClock, keywords: ["schedule", "lesson"], roles: ACADEMIC },
      // Anyone who reads a branch's schedule as staff may look at its rooms; only the Super Admin and the branch Administrator change them.
      { href: "/rooms", label: "Xonalar", icon: DoorOpen, keywords: ["room", "xona", "sig'im", "sigim", "jihoz", "band"], roles: MANAGEMENT },
      { href: "/exams", label: "Imtihonlar", icon: Award, keywords: ["exam", "baho", "natija"], roles: ACADEMIC },
      { href: "/support", label: "Support", icon: LifeBuoy, keywords: ["support", "yordam", "signal", "tavsiya"], roles: ["SUPER_ADMIN", "ADMINISTRATOR", "TEACHER", "SUPPORT_TEACHER"] },
    ],
  },
  {
    label: "Texnik",
    items: [
      { href: "/system", label: "Tizim", icon: Gauge, keywords: ["system", "monitoring", "metrika", "latency", "xato", "log", "job", "versiya", "deployment"], roles: ["DEVELOPER"] },
      { href: "/system/access", label: "Ma'lumotga kirish", icon: ShieldCheck, keywords: ["access", "production", "ruxsat", "so'rov", "vaqtinchalik", "developer"], roles: ["DEVELOPER", "SUPER_ADMIN"] },
    ],
  },
  {
    label: "Tashkilot",
    items: [
      { href: "/settings/organization", label: "Markaz sozlamalari", icon: Settings, keywords: ["organization", "tashkilot", "logo", "brend", "rang", "valyuta", "ish vaqti", "sozlama", "nom"], roles: ["SUPER_ADMIN"] },
      { href: "/settings/permissions", label: "Ruxsatlar", icon: ShieldCheck, keywords: ["permission", "ruxsat", "rol", "matritsa", "scope", "doira", "huquq"], roles: ["SUPER_ADMIN"] },
      { href: "/branches", label: "Filiallar", icon: Building2, keywords: ["branch"], roles: ["SUPER_ADMIN", "ADMINISTRATOR", "OPERATOR"] },
      { href: "/users", label: "Xodimlar", icon: Users2, keywords: ["user", "staff", "xodim"], roles: ["SUPER_ADMIN", "ADMINISTRATOR"] },
      { href: "/accounts", label: "Hisoblar", icon: KeyRound, keywords: ["account", "login", "hisob", "foydalanuvchi", "parol"], roles: ["SUPER_ADMIN"] },
      { href: "/automation", label: "Avtomatlashtirish", icon: Workflow, keywords: ["automation", "qoida", "rule", "avtomat"], roles: ["SUPER_ADMIN", "ADMINISTRATOR"] },
      { href: "/audit-logs", label: "Audit jurnali", icon: ScrollText, keywords: ["log", "audit"], roles: ["SUPER_ADMIN", "ADMINISTRATOR"] },
    ],
  },
];

/** The sidebar/command palette for one role — groups with no visible items are dropped entirely. */
export function navForRole(role: string | null | undefined): NavGroup[] {
  return NAV_GROUPS
    .map((group) => ({ ...group, items: group.items.filter((item) => item.roles.includes(role as RoleCode)) }))
    .filter((group) => group.items.length > 0);
}

/** Where a fresh login lands. Every role now has a dashboard of its own behind one URL, so this no longer
 * has to guess which list screen is least wrong for a student or a parent. */
export function homeForRole(role: string | null | undefined): string {
  return role ? "/dashboard" : "/login";
}

/** UI-side gate for "manage" affordances (create group, schedule lesson, assign staff, invite user).
 * The API rejects these for every other role anyway; hiding the buttons avoids dead-end forms. */
export function canManageAcademic(role: string | null | undefined): boolean {
  return role === "SUPER_ADMIN" || role === "ADMINISTRATOR";
}

/** Roles that can take attendance, create/review homework and record exam results. */
export function isTeachingStaff(role: string | null | undefined): boolean {
  return role === "SUPER_ADMIN" || role === "ADMINISTRATOR" || role === "TEACHER" || role === "SUPPORT_TEACHER";
}

export function isStudentOrParent(role: string | null | undefined): boolean {
  return role === "STUDENT" || role === "PARENT";
}
