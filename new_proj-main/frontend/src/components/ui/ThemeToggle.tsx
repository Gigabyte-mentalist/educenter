"use client";

import { Monitor, Moon, Sun } from "lucide-react";
import { useTheme, type Theme } from "@/lib/theme";
import { cn } from "@/lib/cn";

const OPTIONS: { value: Theme; label: string; icon: typeof Sun }[] = [
  { value: "light", label: "Kunduzgi", icon: Sun },
  { value: "dark", label: "Tungi", icon: Moon },
  { value: "system", label: "Tizim", icon: Monitor },
];

/**
 * Three states rather than a switch: "system" is the one most people want and a two-way toggle cannot express
 * it — once you flip a binary switch the app stops following the phone, and there is no way back.
 */
export function ThemeToggle({ className }: { className?: string }) {
  const { theme, setTheme, hydrated } = useTheme();

  return (
    <div
      className={cn("flex items-center gap-0.5 rounded-lg border border-border bg-canvas p-0.5", className)}
      role="radiogroup"
      aria-label="Ko'rinish rejimi"
    >
      {OPTIONS.map(({ value, label, icon: Icon }) => (
        <button
          key={value}
          type="button"
          role="radio"
          // Before hydration the stored choice is unknown, so nothing is marked selected rather than
          // marking the wrong one and flickering.
          aria-checked={hydrated && theme === value}
          title={label}
          onClick={() => setTheme(value)}
          className={cn(
            "flex flex-1 items-center justify-center rounded-md py-1.5 text-ink-500 transition-colors hover:text-ink-900",
            hydrated && theme === value && "bg-surface text-ink-900 shadow-sm",
          )}
        >
          <Icon className="size-3.5" aria-hidden />
          <span className="sr-only">{label}</span>
        </button>
      ))}
    </div>
  );
}
