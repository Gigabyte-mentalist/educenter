"use client";

import { useQuery } from "@tanstack/react-query";
import { ArrowRight, TrendingDown, TrendingUp } from "lucide-react";
import { getLearningProfile, type LearningProfile, type ProfileFigure } from "@/lib/api/students";
import { ApiError } from "@/lib/api-client";
import { Alert } from "@/components/ui/Alert";
import { Button } from "@/components/ui/Button";
import { Skeleton } from "@/components/ui/Skeleton";
import { cn } from "@/lib/cn";

/** Below the first line a figure turns amber, below the second red — IELTS bands and percentages each have their own. */
const BAND = { max: 9, warn: 5.5, danger: 4.5 };
const PERCENT = { max: 100, warn: 60, danger: 40 };

interface Row {
  key: keyof LearningProfile;
  label: string;
  kind: typeof BAND;
  /** "1 ta natijadan" — what the sample size counts. */
  unit: string;
  isPercent: boolean;
  hint?: string;
}

const ROWS: Row[] = [
  { key: "listening", label: "Listening", kind: BAND, unit: "natijadan", isPercent: false },
  { key: "reading", label: "Reading", kind: BAND, unit: "natijadan", isPercent: false },
  { key: "writing", label: "Writing", kind: BAND, unit: "natijadan", isPercent: false, hint: "Imtihon va AI insholari; o‘qituvchi tuzatgani ustun" },
  { key: "speaking", label: "Speaking", kind: BAND, unit: "natijadan", isPercent: false, hint: "Faqat imtihon natijasidan" },
  { key: "grammar", label: "Grammatika", kind: PERCENT, unit: "natijadan", isPercent: true },
  { key: "vocabulary", label: "Lug‘at", kind: PERCENT, unit: "natijadan", isPercent: true },
  { key: "attendance", label: "Davomat", kind: PERCENT, unit: "darsdan", isPercent: true },
  { key: "homeworkCompletion", label: "Homework", kind: PERCENT, unit: "vazifadan", isPercent: true },
];

function toneOf(value: number, kind: typeof BAND): "ok" | "warn" | "danger" {
  return value < kind.danger ? "danger" : value < kind.warn ? "warn" : "ok";
}

const FILL = { ok: "bg-brand-solid", warn: "bg-warning-500", danger: "bg-danger-500" };
const TEXT = { ok: "text-ink-900", warn: "text-warning-600", danger: "text-danger-600" };

const TREND = {
  1: { Icon: TrendingUp, label: "Yaxshilanmoqda", className: "text-success-600" },
  0: { Icon: ArrowRight, label: "O‘zgarmagan", className: "text-ink-400" },
  [-1]: { Icon: TrendingDown, label: "Pasaymoqda", className: "text-danger-600" },
} as const;

function FigureCard({ row, figure }: { row: Row; figure: ProfileFigure }) {
  const { value, sampleSize, trend } = figure;
  const tone = value === null ? "ok" : toneOf(value, row.kind);
  const trendView = trend === null ? null : TREND[trend];

  return (
    <div className="rounded-xl border border-border bg-surface p-4">
      <div className="flex items-center justify-between gap-2">
        <span className="text-[13px] font-medium text-ink-500">{row.label}</span>
        {trendView && (
          <span className={cn("flex items-center", trendView.className)} title={trendView.label}>
            <trendView.Icon className="size-4" aria-hidden />
            <span className="sr-only">{trendView.label}</span>
          </span>
        )}
      </div>

      {value === null ? (
        <p className="mt-2 text-[13.5px] text-ink-400">Hali ma’lumot yo‘q</p>
      ) : (
        <p className={cn("mt-2 font-tabular text-2xl font-semibold", TEXT[tone])}>
          {row.isPercent ? `${value}%` : value.toFixed(1)}
        </p>
      )}

      <div
        className="mt-3 h-1.5 overflow-hidden rounded-full bg-ink-100"
        role="progressbar" aria-label={row.label} aria-valuemin={0} aria-valuemax={row.kind.max} aria-valuenow={value ?? undefined}
      >
        {value !== null && <div className={cn("h-full rounded-full", FILL[tone])} style={{ width: `${Math.min(100, (value / row.kind.max) * 100)}%` }} />}
      </div>

      {value !== null && <p className="mt-2 text-xs text-ink-500">{sampleSize} ta {row.unit}</p>}
      {row.hint && <p className="mt-1 text-[11px] leading-snug text-ink-400">{row.hint}</p>}
    </div>
  );
}

/** TZ §7: the eight numbers that say how a student is doing. What the caller may see here is what they may open in the
 *  Davomat, Homework and Imtihonlar tabs — the profile only summarises those. */
export function StudentLearningProfile({ studentId }: { studentId: string }) {
  const query = useQuery({ queryKey: ["students", studentId, "learning-profile"], queryFn: () => getLearningProfile(studentId) });

  if (query.isPending) {
    return <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">{ROWS.map((row) => <Skeleton key={row.key} className="h-28" />)}</div>;
  }
  if (query.isError) {
    return (
      <div className="space-y-3">
        <Alert>{query.error instanceof ApiError ? query.error.message : "Profilni yuklab bo‘lmadi."}</Alert>
        <Button variant="secondary" loading={query.isFetching} onClick={() => void query.refetch()}>Qayta urinish</Button>
      </div>
    );
  }

  return (
    <div className="space-y-3">
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        {ROWS.map((row) => <FigureCard key={row.key} row={row} figure={query.data[row.key]} />)}
      </div>
      <p className="text-xs text-ink-500">
        Ko‘nikmalar so‘nggi 5 ta natijaning o‘rtachasi (yangisi kuchliroq); strelka — oxirgi natijalar oldingilaridan farqi.
        Ma’lumot yetmasa raqam ko‘rsatilmaydi.
      </p>
    </div>
  );
}
