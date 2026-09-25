"use client";

import Link from "next/link";
import { useEffect, useRef, useState } from "react";
import { BellRing, ChevronDown, KeyRound, LogOut } from "lucide-react";
import { roleLabel, type CurrentUser } from "@/lib/auth-storage";
import { Avatar } from "@/components/ui/Avatar";
import { cn } from "@/lib/cn";

/**
 * The account card at the foot of the sidebar and the menu it opens: profile, notification settings, password, sign out.
 *
 * It opens on tap or click, not on hover. The first version was pure CSS (`group-hover` / `focus-within`), which a mouse
 * satisfies and a finger does not: a tablet has no hover, and Safari does not give a tapped button focus, so on an iPad the
 * menu — and with it the only way to sign out — never appeared. It closes on Escape, on a tap anywhere outside, and when
 * a choice is made.
 */
export function UserMenu({
  user, onLogout, onNavigate,
}: {
  user: CurrentUser;
  onLogout: () => void;
  /** Called after a link is followed, so the caller can close the mobile navigation overlay too. */
  onNavigate?: () => void;
}) {
  const [open, setOpen] = useState(false);
  const container = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;

    // pointerdown covers mouse, pen and touch alike; "click" would fire after the target had already been acted on.
    function onPointerDown(e: PointerEvent) {
      if (container.current && !container.current.contains(e.target as Node)) setOpen(false);
    }
    function onKey(e: KeyboardEvent) {
      if (e.key === "Escape") setOpen(false);
    }
    document.addEventListener("pointerdown", onPointerDown);
    document.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("pointerdown", onPointerDown);
      document.removeEventListener("keydown", onKey);
    };
  }, [open]);

  const choose = () => {
    setOpen(false);
    onNavigate?.();
  };

  const item =
    "flex w-full items-center gap-2 rounded-md px-2.5 py-2.5 text-left text-[13px] text-ink-700 hover:bg-ink-50 hover:text-ink-900";

  return (
    <div ref={container} className="relative">
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        aria-haspopup="menu"
        aria-expanded={open}
        className="flex w-full items-center gap-2.5 rounded-lg px-2 py-2 text-left hover:bg-ink-50"
      >
        <Avatar name={user.email} />
        <span className="min-w-0 flex-1">
          <span className="block truncate text-[13px] font-medium text-ink-900">{roleLabel(user.role)}</span>
          <span className="block truncate text-[11.5px] text-ink-500">{user.email}</span>
        </span>
        <ChevronDown className={cn("size-3.5 text-ink-400 transition-transform", open && "rotate-180")} aria-hidden />
      </button>

      {open && (
        <div role="menu" className="absolute bottom-full left-0 z-10 mb-1 w-full rounded-lg border border-border bg-surface p-1 shadow-lg">
          {user.id && (
            <Link href={`/users/${user.id}`} role="menuitem" onClick={choose} className={item}>
              <Avatar name={user.email} className="size-4 text-[9px]" />
              Mening profilim
            </Link>
          )}
          <Link href="/settings/notifications" role="menuitem" onClick={choose} className={item}>
            <BellRing className="size-3.5" aria-hidden />
            Bildirishnoma sozlamalari
          </Link>
          <Link href="/settings/security" role="menuitem" onClick={choose} className={item}>
            <KeyRound className="size-3.5" aria-hidden />
            Parolni o&apos;zgartirish
          </Link>
          <button
            type="button"
            role="menuitem"
            onClick={() => { setOpen(false); onLogout(); }}
            className={cn(item, "hover:bg-danger-50 hover:text-danger-600")}
          >
            <LogOut className="size-3.5" aria-hidden />
            Chiqish
          </button>
        </div>
      )}
    </div>
  );
}
