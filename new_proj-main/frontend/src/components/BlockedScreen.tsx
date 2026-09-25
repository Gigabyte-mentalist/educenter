"use client";

import { ShieldAlert } from "lucide-react";
import { Button } from "@/components/ui/Button";

/**
 * Replaces the entire admin shell the moment this session's account is found blocked (S01.04) — not a modal over
 * the page, because the page underneath (its data, its background polling) is exactly what must stop. No close
 * button, no Escape, no backdrop click: the only way out is the one button, which logs the session out.
 */
export function BlockedScreen({ onLogout }: { onLogout: () => void }) {
  return (
    <div
      className="fixed inset-0 z-[100] flex items-center justify-center bg-canvas p-6"
      role="alertdialog"
      aria-modal="true"
      aria-labelledby="blocked-screen-title"
      aria-describedby="blocked-screen-description"
    >
      <div className="w-full max-w-sm rounded-2xl border border-border bg-surface p-6 text-center shadow-xl">
        <span className="mx-auto flex size-12 items-center justify-center rounded-full bg-danger-50 text-danger-600">
          <ShieldAlert className="size-6" aria-hidden />
        </span>
        <h1 id="blocked-screen-title" className="mt-4 text-[17px] font-semibold text-ink-900">
          Hisobingiz bloklandi
        </h1>
        <p id="blocked-screen-description" className="mt-2 text-[13.5px] leading-relaxed text-ink-500">
          Administrator hisobingizni bloklagan, shuning uchun tizimdan foydalanib bo&apos;lmaydi. Savollaringiz
          bo&apos;lsa markaz administratoriga murojaat qiling.
        </p>
        <Button autoFocus onClick={onLogout} className="mt-6 w-full">
          Tizimdan chiqish
        </Button>
      </div>
    </div>
  );
}
