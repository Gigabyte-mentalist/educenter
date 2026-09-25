"use client";

import { useEffect, useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import {
  Search, Loader2, GraduationCap, UsersRound, Users, BookOpen, Contact, Building2, LifeBuoy, Wallet, CalendarPlus, UserPlus, Zap,
  type LucideIcon,
} from "lucide-react";
import { navForRole } from "@/lib/nav";
import { search, type SearchResult, type SearchResultType } from "@/lib/api/search";
import { useAuthSession } from "@/lib/use-auth-session";
import { usePermissions } from "@/lib/use-permissions";
import { cn } from "@/lib/cn";

const RESULT_ICON: Record<SearchResultType, LucideIcon> = {
  student: GraduationCap,
  teacher: UsersRound,
  group: Users,
  course: BookOpen,
  lead: Contact,
  branch: Building2,
  ticket: LifeBuoy,
  payment: Wallet,
};
const RESULT_LABEL: Record<SearchResultType, string> = {
  student: "Student",
  teacher: "O'qituvchi",
  group: "Guruh",
  course: "Kurs",
  lead: "Lid",
  branch: "Filial",
  ticket: "Support",
  payment: "To'lov",
};

/**
 * The "action" type of Universal Search (docs/00_TZ.md §27.3): things to do rather than things to find. Only the front end knows them — each is
 * a page to go to and, for `?new=1`, the creation form to open there (the pages read it with `useNewParam`) — and each is shown only to a person who
 * holds the permission behind it, so the palette never offers a step that ends in "Bu amal uchun ruxsat yo'q".
 */
const ACTIONS: { label: string; keywords: string[]; href: string; permission: string; icon: LucideIcon }[] = [
  { label: "Yangi student", keywords: ["student", "o'quvchi", "qo'shish", "yangi", "ro'yxatga"], href: "/students?new=1", permission: "students.create", icon: UserPlus },
  { label: "Yangi lid", keywords: ["lid", "lead", "mijoz", "qo'shish", "yangi"], href: "/leads?new=1", permission: "leads.create", icon: Contact },
  { label: "Dars qo'shish", keywords: ["dars", "lesson", "jadval", "qo'shish", "yangi"], href: "/schedule?new=1", permission: "schedule.edit", icon: CalendarPlus },
  { label: "To'lov qabul qilish", keywords: ["to'lov", "payment", "pul", "qabul", "kassa"], href: "/finance", permission: "payments.create", icon: Wallet },
  { label: "Yangi guruh", keywords: ["guruh", "group", "qo'shish", "yangi"], href: "/groups?new=1", permission: "groups.create", icon: Users },
];

type Entry =
  | { kind: "nav"; href: string; label: string; sub?: string; icon: LucideIcon }
  | { kind: "action"; href: string; label: string; sub?: string; icon: LucideIcon }
  | { kind: "result"; href: string; label: string; sub?: string; icon: LucideIcon };

export function CommandPalette() {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const [debouncedQuery, setDebouncedQuery] = useState("");
  const [activeIndex, setActiveIndex] = useState(0);
  const router = useRouter();

  const { user } = useAuthSession();
  const navItems = useMemo(() => navForRole(user?.role).flatMap((g) => g.items), [user?.role]);
  const filteredNav = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return navItems;
    return navItems.filter((i) => i.label.toLowerCase().includes(q) || i.keywords?.some((k) => k.includes(q)));
  }, [navItems, query]);

  // Actions appear once something is typed, and only those the person may perform.
  const { can } = usePermissions();
  const matchingActions = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return [];
    return ACTIONS.filter((a) => can(a.permission) && (a.label.toLowerCase().includes(q) || a.keywords.some((k) => k.includes(q))));
  }, [can, query]);

  // Backend search fires on its own short delay so every keystroke doesn't turn into a request — the nav
  // filter above is instant either way since it's just an in-memory list.
  useEffect(() => {
    const trimmed = query.trim();
    const timer = setTimeout(() => setDebouncedQuery(trimmed), 250);
    return () => clearTimeout(timer);
  }, [query]);

  const searchQuery = useQuery({
    queryKey: ["command-palette-search", debouncedQuery],
    queryFn: () => search(debouncedQuery),
    enabled: open && debouncedQuery.length >= 2,
  });

  useEffect(() => {
    function onKeyDown(e: KeyboardEvent) {
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === "k") {
        e.preventDefault();
        setOpen((v) => !v);
        setActiveIndex(0);
      }
      if (e.key === "Escape") setOpen(false);
    }
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, []);

  if (!open) return null;

  const go = (href: string) => {
    setOpen(false);
    setQuery("");
    setDebouncedQuery("");
    router.push(href);
  };

  const navEntries: Entry[] = filteredNav.map((i) => ({ kind: "nav", href: i.href, label: i.label, icon: i.icon }));
  const actionEntries: Entry[] = matchingActions.map((a) => ({ kind: "action", href: a.href, label: a.label, sub: "Amal", icon: a.icon }));
  const resultEntries: Entry[] = (searchQuery.data ?? []).map((r: SearchResult) => ({
    kind: "result", href: r.href, label: r.title,
    sub: [RESULT_LABEL[r.type], r.subtitle].filter(Boolean).join(" · "),
    icon: RESULT_ICON[r.type],
  }));
  const entries = [...navEntries, ...actionEntries, ...resultEntries];

  return (
    <div className="fixed inset-0 z-50 flex items-start justify-center bg-ink-900/40 pt-[15vh]" onClick={() => setOpen(false)}>
      <div
        className="w-full max-w-lg overflow-hidden rounded-xl border border-border bg-surface shadow-xl"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center gap-2 border-b border-border px-4 py-3">
          {searchQuery.isFetching ? (
            <Loader2 className="size-4 shrink-0 animate-spin text-ink-400" aria-hidden />
          ) : (
            <Search className="size-4 shrink-0 text-ink-400" aria-hidden />
          )}
          <input
            autoFocus
            value={query}
            onChange={(e) => { setQuery(e.target.value); setActiveIndex(0); }}
            onKeyDown={(e) => {
              if (e.key === "ArrowDown") { e.preventDefault(); setActiveIndex((i) => Math.min(i + 1, entries.length - 1)); }
              if (e.key === "ArrowUp") { e.preventDefault(); setActiveIndex((i) => Math.max(i - 1, 0)); }
              if (e.key === "Enter" && entries[activeIndex]) go(entries[activeIndex].href);
            }}
            placeholder="Qidirish... (sahifalar, amallar, studentlar, guruhlar, lidlar, to'lovlar)"
            className="w-full bg-transparent text-sm text-ink-900 placeholder:text-ink-400 focus:outline-none"
          />
          <kbd className="rounded border border-border bg-ink-50 px-1.5 py-0.5 text-[11px] text-ink-500">Esc</kbd>
        </div>
        <div className="max-h-96 overflow-y-auto p-1.5">
          {entries.length === 0 && (
            <p className="px-3 py-6 text-center text-sm text-ink-500">Hech narsa topilmadi.</p>
          )}
          {navEntries.length > 0 && (
            <p className="px-3 pb-1 pt-2 text-[11px] font-medium uppercase tracking-wide text-ink-400">Sahifalar</p>
          )}
          {navEntries.map((entry, i) => (
            <EntryRow key={`nav-${entry.href}`} entry={entry} active={i === activeIndex} onClick={() => go(entry.href)} />
          ))}
          {actionEntries.length > 0 && (
            <p className="flex items-center gap-1.5 px-3 pb-1 pt-3 text-[11px] font-medium uppercase tracking-wide text-ink-400">
              <Zap className="size-3" aria-hidden />Amallar
            </p>
          )}
          {actionEntries.map((entry, i) => (
            <EntryRow key={`action-${entry.href}`} entry={entry} active={navEntries.length + i === activeIndex} onClick={() => go(entry.href)} />
          ))}
          {resultEntries.length > 0 && (
            <p className="px-3 pb-1 pt-3 text-[11px] font-medium uppercase tracking-wide text-ink-400">Natijalar</p>
          )}
          {resultEntries.map((entry, i) => (
            <EntryRow
              key={`result-${entry.href}-${entry.label}-${i}`}
              entry={entry}
              active={navEntries.length + actionEntries.length + i === activeIndex}
              onClick={() => go(entry.href)}
            />
          ))}
        </div>
      </div>
    </div>
  );
}

function EntryRow({ entry, active, onClick }: { entry: Entry; active: boolean; onClick: () => void }) {
  return (
    <button
      onClick={onClick}
      className={cn(
        "flex w-full items-center gap-3 rounded-lg px-3 py-2 text-left text-sm",
        active ? "bg-brand-50 text-brand-800" : "text-ink-700 hover:bg-ink-50",
      )}
    >
      <entry.icon className="size-4 shrink-0 text-ink-400" aria-hidden />
      <span className="min-w-0 flex-1 truncate">{entry.label}</span>
      {entry.sub && <span className="shrink-0 truncate text-[12px] text-ink-400">{entry.sub}</span>}
    </button>
  );
}
