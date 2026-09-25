"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import {
  MessagesSquare, GraduationCap, CalendarClock, UsersRound, LayoutDashboard,
  ListChecks, Contact, Wallet, Sparkles, Mic, PenLine, MoreHorizontal,
} from "lucide-react";
import { useAuthSession } from "@/lib/use-auth-session";
import { ChatUnreadBadge } from "@/components/chat/ChatUnreadBadge";
import { cn } from "@/lib/cn";
import type { RoleCode } from "@/lib/nav";

interface BottomItem {
  href: string;
  label: string;
  icon: React.ElementType;
  badge?: React.ReactNode;
  roles: RoleCode[];
}

const BOTTOM_ITEMS: BottomItem[] = [
  // First tab for everyone — the role's own dashboard.
  {
    href: "/dashboard", label: "Bosh", icon: LayoutDashboard,
    roles: ["SUPER_ADMIN", "ADMINISTRATOR", "OPERATOR", "TEACHER", "SUPPORT_TEACHER", "STUDENT", "PARENT", "DEVELOPER"],
  },

  // Student
  { href: "/ai/tutor",   label: "Tutor",    icon: Sparkles,      roles: ["STUDENT"] },
  { href: "/ai/writing", label: "Writing",  icon: PenLine,       roles: ["STUDENT"] },
  { href: "/ai/speaking",label: "Speaking", icon: Mic,           roles: ["STUDENT"] },
    { href: "/schedule",   label: "Jadval",   icon: CalendarClock, roles: ["STUDENT", "PARENT", "TEACHER", "SUPPORT_TEACHER"] },

  // Teacher / Support
  { href: "/groups",     label: "Guruhlar", icon: UsersRound,    roles: ["TEACHER", "SUPPORT_TEACHER"] },

  // Management
  { href: "/crm/tasks",  label: "Vazifalar",icon: ListChecks,    roles: ["SUPER_ADMIN", "ADMINISTRATOR", "OPERATOR"] },
  { href: "/leads",      label: "Lidlar",   icon: Contact,       roles: ["SUPER_ADMIN", "ADMINISTRATOR", "OPERATOR"] },
  { href: "/students",   label: "Studentlar",icon: GraduationCap,roles: ["SUPER_ADMIN", "ADMINISTRATOR", "OPERATOR"] },
  { href: "/finance",    label: "Moliya",   icon: Wallet,        roles: ["SUPER_ADMIN", "ADMINISTRATOR"] },

  // Shared
  {
    href: "/chat", label: "Xabarlar", icon: MessagesSquare,
    badge: <ChatUnreadBadge />,
    roles: ["SUPER_ADMIN", "ADMINISTRATOR", "OPERATOR", "TEACHER", "SUPPORT_TEACHER", "STUDENT", "PARENT"],
  },
];

export function MobileNav() {
  const pathname = usePathname();
  const { user } = useAuthSession();
  const role = user?.role as RoleCode | undefined;

  const items = BOTTOM_ITEMS.filter((i) => role && i.roles.includes(role));

  // Deduplicate (same href may appear for multiple roles) and cap at 5 visible tabs.
  const seen = new Set<string>();
  const visible: BottomItem[] = [];
  for (const item of items) {
    if (!seen.has(item.href) && visible.length < 5) {
      seen.add(item.href);
      visible.push(item);
    }
  }

  if (visible.length === 0) return null;

  return (
    <nav
      className="safe-bottom fixed inset-x-0 bottom-0 z-30 flex items-stretch border-t border-border bg-surface lg:hidden"
      aria-label="Asosiy navigatsiya"
    >
      {visible.map((item) => {
        const active = pathname === item.href || pathname.startsWith(item.href + "/");
        const Icon = item.icon;
        return (
          <Link
            key={item.href}
            href={item.href}
            className={cn(
              "relative flex flex-1 flex-col items-center justify-center gap-0.5 py-2 text-[10.5px] font-medium transition-colors",
              active ? "text-brand-700" : "text-ink-500 hover:text-ink-900",
            )}
            aria-current={active ? "page" : undefined}
          >
            <span className="relative">
              <Icon className="size-5" aria-hidden />
              {item.badge}
            </span>
            <span>{item.label}</span>
          </Link>
        );
      })}

      {/* If there are more items than fit, show "Ko'proq" pointing to the hamburger menu */}
      {items.filter((i) => !seen.has(i.href)).length > 0 && visible.length >= 5 && (
        <button
          type="button"
          className="flex flex-1 flex-col items-center justify-center gap-0.5 py-2 text-[10.5px] font-medium text-ink-500"
          onClick={() => window.dispatchEvent(new CustomEvent("open-mobile-nav"))}
          aria-label="Ko'proq"
        >
          <MoreHorizontal className="size-5" aria-hidden />
          <span>Ko&apos;proq</span>
        </button>
      )}
    </nav>
  );
}
