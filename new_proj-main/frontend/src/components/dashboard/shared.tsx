"use client";

import { useSyncExternalStore } from "react";
import Link from "next/link";
import { CalendarClock, MapPin } from "lucide-react";
import type { DashboardLesson } from "@/lib/api/dashboard";
import { EmptyState } from "@/components/ui/EmptyState";
import { cn } from "@/lib/cn";
import { formatDayMonth, formatTime } from "@/lib/format-date";

/**
 * The wall clock, in 30-second steps. A dashboard is a page people leave open: reading Date.now() during
 * render freezes "hozir" at whenever the component last happened to re-render, and React rightly objects to
 * an impure read. Bucketing keeps the snapshot stable between ticks, which is what useSyncExternalStore needs.
 */
const TICK_MS = 30_000;

function subscribeToClock(onChange: () => void): () => void {
  const id = setInterval(onChange, TICK_MS);
  return () => clearInterval(id);
}

export function useNow(): number {
  // The server snapshot is 0 — no timestamp compares true against it, so server HTML and the hydrating render
  // agree on "nothing is live yet", and the real time arrives on the render straight after hydration.
  return useSyncExternalStore(subscribeToClock, () => Math.floor(Date.now() / TICK_MS) * TICK_MS, () => 0);
}

export const percent = (value: number) => `${Math.round(value * 100)}%`;

export const timeRange = (startsAt: string, endsAt: string) =>
  `${formatTime(startsAt)}–${formatTime(endsAt)}`;

export const dayMonth = formatDayMonth;

/** Colours a rate the way a person reads it, so the eye finds the bad branch without reading every number. */
export function rateTone(rate: number): string {
  if (rate >= 0.85) return "text-success-600";
  if (rate >= 0.7) return "text-warning-600";
  return "text-danger-600";
}

/**
 * The lesson strip every dashboard shows. A lesson in progress is marked, because "which one is now" is the
 * first thing anyone looks for and working it out from four timestamps is not reading, it is arithmetic.
 */
export function LessonList({
  lessons,
  emptyTitle = "Bugun dars yo'q",
  emptyDescription,
  showTeacher = true,
}: {
  lessons: DashboardLesson[];
  emptyTitle?: string;
  emptyDescription?: string;
  showTeacher?: boolean;
}) {
  const now = useNow();

  if (lessons.length === 0)
    return <EmptyState icon={CalendarClock} title={emptyTitle} description={emptyDescription} />;

  return (
    <ul className="divide-y divide-border">
      {lessons.map((lesson) => {
        const start = new Date(lesson.startsAt).getTime();
        const end = new Date(lesson.endsAt).getTime();
        const live = start <= now && now < end;
        const done = end <= now;

        return (
          <li
            key={lesson.id}
            className={cn("flex items-start gap-3 px-5 py-3 first:pt-4 last:pb-4", done && "opacity-60")}
          >
            <span
              className={cn(
                "mt-0.5 w-[74px] shrink-0 font-tabular text-[12.5px] font-medium",
                live ? "text-brand-700" : "text-ink-500",
              )}
            >
              {timeRange(lesson.startsAt, lesson.endsAt)}
            </span>
            <div className="min-w-0 flex-1">
              <p className="truncate text-[13.5px] font-medium text-ink-900">
                <Link href={`/schedule/${lesson.id}`} className="hover:underline">
                  {lesson.groupName}
                </Link>
                {live && (
                  <span className="ml-2 rounded-full bg-brand-50 px-1.5 py-0.5 text-[10.5px] font-semibold text-brand-700">
                    hozir
                  </span>
                )}
              </p>
              <p className="mt-0.5 flex flex-wrap items-center gap-x-3 gap-y-0.5 text-[12px] text-ink-500">
                <span className="inline-flex items-center gap-1">
                  <MapPin className="size-3 shrink-0" aria-hidden />
                  {lesson.roomName}
                </span>
                {showTeacher && <span className="truncate">{lesson.teacherName}</span>}
                {lesson.topic && <span className="truncate text-ink-400">{lesson.topic}</span>}
              </p>
            </div>
          </li>
        );
      })}
    </ul>
  );
}

/** A labelled number that carries its own link — dashboards exist to be left, not read. */
export function CountLink({
  href,
  label,
  value,
  tone = "neutral",
}: {
  href: string;
  label: string;
  value: number;
  tone?: "neutral" | "warning" | "danger";
}) {
  return (
    <Link
      href={href}
      className="flex items-center justify-between gap-3 rounded-lg px-3 py-2.5 hover:bg-ink-50"
    >
      <span className="text-[13px] text-ink-600">{label}</span>
      <span
        className={cn(
          "font-tabular text-[15px] font-semibold",
          tone === "danger" && value > 0 ? "text-danger-600"
            : tone === "warning" && value > 0 ? "text-warning-600"
            : "text-ink-900",
        )}
      >
        {value}
      </span>
    </Link>
  );
}

