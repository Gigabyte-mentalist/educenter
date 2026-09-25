"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { Fragment, useEffect, useState, useSyncExternalStore } from "react";
import { Menu, Search, X } from "lucide-react";
import { navForRole } from "@/lib/nav";
import { getDefaultCurrency, subscribeDefaultCurrency } from "@/lib/currency";
import { logout as apiLogout } from "@/lib/api/auth";
import {
  clearAccountBlocked, clearPasswordChangeRequired, useAccountBlocked, usePasswordChangeRequired,
} from "@/lib/account-status";
import { clearTokens, getRefreshToken } from "@/lib/auth-storage";
import { useAuthSession } from "@/lib/use-auth-session";
import { BlockedScreen } from "@/components/BlockedScreen";
import { ThemeToggle } from "@/components/ui/ThemeToggle";
import { UserMenu } from "@/components/UserMenu";
import { CommandPalette } from "@/components/CommandPalette";
import { NotificationBell } from "@/components/NotificationBell";
import { BrandMark, OrganizationBrandSync, useBrand } from "@/components/OrganizationBrand";
import { ChatUnreadBadge } from "@/components/chat/ChatUnreadBadge";
import { MobileNav } from "@/components/MobileNav";
import { stopChatConnection } from "@/lib/chat-hub";
import { cn } from "@/lib/cn";

export default function AdminLayout({ children }: { children: React.ReactNode }) {
  const router = useRouter();
  const pathname = usePathname();
  const { hydrated, token, user } = useAuthSession();
  const blocked = useAccountBlocked();
  const mustChangePassword = usePasswordChangeRequired();
  const brand = useBrand();
  // Prices already on screen were written in the old currency; when the organization's turns out to differ from the remembered
  // one (or an office changes it), the page below is mounted afresh so every formatMoney call reads the new one.
  const currency = useSyncExternalStore(subscribeDefaultCurrency, getDefaultCurrency, () => "UZS");

  // The sidebar is a permanent column from lg up and an overlay below it. Phones and tablets are not the
  // target — the app for those is coming — but somebody opening this on a phone should still get a usable
  // page rather than a 240px navigation bar eating half the screen.
  const [navOpen, setNavOpen] = useState(false);

  useEffect(() => {
    if (hydrated && !token) router.replace("/login");
  }, [hydrated, token, router]);

  // An administrator reset this account's password while the session was open: every request now answers 403 until
  // the temporary one is replaced, so the shell gives way to the form that does it (S01.05).
  useEffect(() => {
    if (mustChangePassword) router.replace("/change-password");
  }, [mustChangePassword, router]);

  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      if (e.key === "Escape") setNavOpen(false);
    }
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, []);

  const handleLogout = async () => {
    const refreshToken = getRefreshToken();
    if (refreshToken) await apiLogout(refreshToken).catch(() => {});
    stopChatConnection();
    clearTokens();
    clearAccountBlocked();
    clearPasswordChangeRequired();
    router.replace("/login");
  };

  // Nothing renders while a password change is pending, so no page or poller keeps firing requests that would all 403.
  if (!hydrated || !token || mustChangePassword) return null;
  // Nothing else in the shell renders while blocked — no sidebar, no page, no notification bell — so the
  // background polling and requests that would otherwise keep confirming "still blocked" simply stop.
  if (blocked) return <BlockedScreen onLogout={handleLogout} />;

  const navGroups = navForRole(user?.role);

  return (
    <div className="flex h-dvh min-h-0 bg-canvas">
      {navOpen && (
        <div
          className="fixed inset-0 z-30 bg-ink-900/40 lg:hidden"
          onClick={() => setNavOpen(false)}
          aria-hidden
        />
      )}

      <aside
        className={cn(
          "fixed inset-y-0 left-0 z-40 flex h-screen w-60 shrink-0 flex-col border-r border-border bg-surface transition-transform duration-200",
          "lg:sticky lg:top-0 lg:z-auto lg:translate-x-0",
          navOpen ? "translate-x-0" : "-translate-x-full",
        )}
      >
        <div className="flex items-center gap-2 px-5 py-5">
          <BrandMark logo={brand.logo} className="size-8" />
          <span className="min-w-0 truncate font-display text-[15px] font-semibold text-ink-900">{brand.name}</span>
          <div className="ml-auto flex items-center gap-1">
            <NotificationBell />
            <button
              onClick={() => setNavOpen(false)}
              className="rounded-md p-1.5 text-ink-500 hover:bg-ink-100 lg:hidden"
              aria-label="Menyuni yopish"
            >
              <X className="size-4" aria-hidden />
            </button>
          </div>
        </div>

        <button
          onClick={() => window.dispatchEvent(new KeyboardEvent("keydown", { key: "k", ctrlKey: true }))}
          className="mx-3 mb-4 flex items-center gap-2 rounded-lg border border-border bg-ink-50 px-3 py-2 text-[13px] text-ink-500 hover:border-border-strong"
        >
          <Search className="size-3.5" aria-hidden />
          Qidirish
          <kbd className="ml-auto rounded border border-border bg-surface px-1.5 py-0.5 text-[10px] font-medium text-ink-400">
            Ctrl K
          </kbd>
        </button>

        <nav className="flex-1 space-y-5 overflow-y-auto px-3 pb-4">
          {navGroups.length === 0 && (
            <p className="px-2 text-[12.5px] leading-relaxed text-ink-500">
              Bu rol uchun hozircha sahifalar yo&apos;q. Administrator bilan bog&apos;laning.
            </p>
          )}
          {navGroups.map((group) => (
            <div key={group.label}>
              {group.label && (
                <p className="mb-1.5 px-2 text-[11px] font-semibold uppercase tracking-wide text-ink-400">
                  {group.label}
                </p>
              )}
              <div className="space-y-0.5">
                {group.items.map((item) => {
                  const active = pathname === item.href || pathname.startsWith(item.href + "/");
                  return (
                    <Link
                      key={item.href}
                      href={item.href}
                      // Closed on the tap rather than on a pathname effect: the effect fires a render after
                      // the new page has already painted behind the overlay.
                      onClick={() => setNavOpen(false)}
                      className={cn(
                        "flex items-center gap-2.5 rounded-lg px-2.5 py-2 text-[13.5px] font-medium transition-colors",
                        active ? "bg-brand-50 text-brand-800" : "text-ink-600 hover:bg-ink-50 hover:text-ink-900",
                      )}
                    >
                      <item.icon className={cn("size-4 shrink-0", active ? "text-brand-600" : "text-ink-400")} aria-hidden />
                      {item.label}
                      {item.href === "/chat" && <ChatUnreadBadge />}
                    </Link>
                  );
                })}
              </div>
            </div>
          ))}
        </nav>

        <div className="space-y-2 border-t border-border p-3">
          <ThemeToggle />
          {user && <UserMenu user={user} onLogout={handleLogout} onNavigate={() => setNavOpen(false)} />}
        </div>
      </aside>

      <div className="flex min-h-0 min-w-0 flex-1 flex-col overflow-y-auto pb-16 lg:pb-0">
        {/* Below lg the sidebar is hidden, so the page needs its own bar to reach navigation and alerts from. */}
        <header className="sticky top-0 z-20 flex items-center gap-2 border-b border-border bg-surface px-4 py-2.5 lg:hidden">
          <button
            onClick={() => setNavOpen(true)}
            className="rounded-md p-1.5 text-ink-600 hover:bg-ink-100"
            aria-label="Menyuni ochish"
            aria-expanded={navOpen}
          >
            <Menu className="size-5" aria-hidden />
          </button>
          <span className="min-w-0 truncate font-display text-[14px] font-semibold text-ink-900">{brand.name}</span>
          <div className="ml-auto flex items-center gap-1">
            <NotificationBell />
          </div>
        </header>

        <Fragment key={currency}>{children}</Fragment>
      </div>

      <OrganizationBrandSync />
      <CommandPalette />
      <MobileNav />
    </div>
  );
}
