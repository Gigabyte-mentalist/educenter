"use client";

import { type ReactNode } from "react";
import Link from "next/link";
import { useQuery } from "@tanstack/react-query";
import { CalendarClock, ClipboardList, FileCheck2, Minus, TrendingDown, TrendingUp } from "lucide-react";
import { getTeacherWorkload, type TeacherWorkload } from "@/lib/api/teachers";
import { ApiError } from "@/lib/api-client";
import { formatYearMonth } from "@/lib/format-date";
import { Alert } from "@/components/ui/Alert";
import { Button } from "@/components/ui/Button";
import { EmptyState } from "@/components/ui/EmptyState";
import { Select } from "@/components/ui/Field";
import { Skeleton } from "@/components/ui/Skeleton";
import { StatTile } from "@/components/ui/StatTile";
import { Table, THead, TBody, TRow, TH, TD } from "@/components/ui/Table";

export const WORKLOAD_PERIODS = [
  { value: "30", label: "Oxirgi 30 kun" },
  { value: "90", label: "Oxirgi 90 kun" },
  { value: "365", label: "Oxirgi yil" },
] as const;

/** The server reads the dates as whole days, in each branch's own calendar; "to" is today and "from" is `days` before it. */
function rangeOf(days: string): { from: string; to: string } {
  const iso = (d: Date) => d.toISOString().slice(0, 10);
  const today = new Date();
  return { from: iso(new Date(today.getTime() - Number(days) * 86_400_000)), to: iso(today) };
}

type View = "attendance" | "homework" | "exams";

/**
 * The Davomat, Homework and Imtihonlar tabs. All three read one response, so switching between them (or back) does not
 * fetch again; the period lives in the page so the three tabs always describe the same weeks.
 */
export function TeacherWorkloadTab({
  teacherId, view, period, onPeriodChange,
}: { teacherId: string; view: View; period: string; onPeriodChange: (period: string) => void }) {
  const query = useQuery({
    queryKey: ["teachers", teacherId, "workload", period],
    queryFn: () => getTeacherWorkload(teacherId, rangeOf(period)),
  });

  return (
    <div className="space-y-4">
      <Select value={period} onChange={(e) => onPeriodChange(e.target.value)} aria-label="Davr" className="w-auto min-w-40">
        {WORKLOAD_PERIODS.map((p) => (
          <option key={p.value} value={p.value}>{p.label}</option>
        ))}
      </Select>
      {query.isPending && <Skeleton className="h-40" />}
      {query.isError && (
        <>
          <Alert>{query.error instanceof ApiError ? query.error.message : "Ma‘lumotni yuklab bo‘lmadi."}</Alert>
          <Button onClick={() => void query.refetch()}>Qayta urinish</Button>
        </>
      )}
      {query.data && (view === "attendance" ? <AttendanceView data={query.data} />
        : view === "homework" ? <HomeworkView data={query.data} />
        : <ExamsView data={query.data} />)}
    </div>
  );
}

const rateColor = (rate: number) => (rate >= 85 ? "text-success-600" : rate >= 70 ? "text-warning-600" : "text-danger-600");

function GroupName({ id, name }: { id: string; name: string }) {
  return <Link className="text-brand-700 hover:underline" href={`/groups/${id}`}>{name}</Link>;
}

function AttendanceView({ data }: { data: TeacherWorkload }) {
  return (
    <>
      <div className="grid gap-4 sm:grid-cols-3">
        <StatTile label="Davomat (studentlar)" value={data.lessonsHeld === 0 ? "—" : `${data.attendanceRate}%`}
          icon={CalendarClock} tone={data.lessonsHeld > 0 && data.attendanceRate < 70 ? "danger" : "brand"} />
        <StatTile label="O‘tkazilgan darslar" value={data.lessonsHeld} icon={CalendarClock} />
        <StatTile label="Bekor qilingan darslar" value={data.lessonsCancelled} icon={CalendarClock}
          hint="Davomat foiziga kirmaydi" />
      </div>
      {data.attendance.length === 0
        ? <EmptyState icon={CalendarClock} title="Guruh biriktirilmagan" />
        : <Table>
          <THead><tr><TH>Guruh</TH><TH className="text-right">Darslar</TH><TH className="text-right">Kelgan / belgilangan</TH><TH className="text-right">Davomat</TH></tr></THead>
          <TBody>{data.attendance.map((g) => (
            <TRow key={g.groupId}>
              <TD><GroupName id={g.groupId} name={g.groupName} /></TD>
              <TD className="text-right font-tabular">
                {g.lessonsHeld}
                {g.lessonsCancelled > 0 && <span className="block text-[11.5px] text-ink-500">{g.lessonsCancelled} ta bekor</span>}
              </TD>
              <TD className="text-right font-tabular">{g.attended} / {g.counted}</TD>
              <TD className={`text-right font-tabular ${g.counted === 0 ? "text-ink-400" : rateColor(g.attendanceRate)}`}>
                {g.counted === 0 ? "—" : `${g.attendanceRate}%`}
              </TD>
            </TRow>
          ))}</TBody>
        </Table>}
    </>
  );
}

const hours = (value: number | null) => (value === null ? "—" : value < 1 ? `${Math.round(value * 60)} daq` : `${value} soat`);

function HomeworkView({ data }: { data: TeacherWorkload }) {
  return (
    <>
      <div className="grid gap-4 sm:grid-cols-3">
        <StatTile label="Berilgan homework" value={data.homeworkCreated} icon={ClipboardList} />
        <StatTile label="Tekshirilmagan" value={data.homeworkPendingReview} icon={ClipboardList}
          tone={data.homeworkPendingReview > 0 ? "warning" : "neutral"} />
        <StatTile label="Tekshirish vaqti (o‘rtacha)" value={hours(data.averageReviewHours)} icon={ClipboardList}
          hint="Topshirilgandan baholangungacha" />
      </div>
      {data.homework.length === 0
        ? <EmptyState icon={ClipboardList} title="Guruh biriktirilmagan" />
        : <Table>
          <THead><tr><TH>Guruh</TH><TH className="text-right">Berilgan</TH><TH className="text-right">Tekshirilmagan</TH><TH className="text-right">Tekshirish vaqti</TH></tr></THead>
          <TBody>{data.homework.map((g) => (
            <TRow key={g.groupId}>
              <TD><GroupName id={g.groupId} name={g.groupName} /></TD>
              <TD className="text-right font-tabular">{g.created}</TD>
              <TD className={`text-right font-tabular ${g.pendingReview > 0 ? "text-warning-600" : ""}`}>{g.pendingReview}</TD>
              <TD className="text-right font-tabular">{hours(g.averageReviewHours)}</TD>
            </TRow>
          ))}</TBody>
        </Table>}
    </>
  );
}

/** The last month against the one before it — an arrow, not a chart: the numbers are on the row. */
function Trend({ months }: { months: { month: string; average: number }[] }): ReactNode {
  if (months.length < 2) return <span className="text-ink-400">—</span>;
  const delta = Math.round((months[months.length - 1].average - months[months.length - 2].average) * 100) / 100;
  if (delta === 0) return <span className="inline-flex items-center gap-1 text-ink-500"><Minus className="size-3.5" aria-hidden />o‘zgarmagan</span>;
  return delta > 0
    ? <span className="inline-flex items-center gap-1 text-success-600"><TrendingUp className="size-3.5" aria-hidden />+{delta}</span>
    : <span className="inline-flex items-center gap-1 text-danger-600"><TrendingDown className="size-3.5" aria-hidden />{delta}</span>;
}

function ExamsView({ data }: { data: TeacherWorkload }) {
  const withResults = data.exams.filter((g) => g.averageOverall !== null);
  if (data.exams.length === 0) return <EmptyState icon={FileCheck2} title="Guruh biriktirilmagan" />;
  return (
    <>
      <div className="grid gap-4 sm:grid-cols-3">
        <StatTile label="Imtihonlar" value={data.exams.reduce((n, g) => n + g.exams, 0)} icon={FileCheck2} />
        <StatTile label="Natijalar" value={data.exams.reduce((n, g) => n + g.results, 0)} icon={FileCheck2} />
      </div>
      {withResults.length === 0
        ? <EmptyState icon={FileCheck2} title="Bu davrda imtihon natijasi yo‘q" />
        : <Table>
          <THead><tr><TH>Guruh</TH><TH className="text-right">Imtihon</TH><TH className="text-right">O‘rtacha (Overall)</TH><TH>Oylar bo‘yicha</TH><TH>Dinamika</TH></tr></THead>
          <TBody>{withResults.map((g) => (
            <TRow key={g.groupId}>
              <TD><GroupName id={g.groupId} name={g.groupName} /></TD>
              <TD className="text-right font-tabular">{g.exams} ({g.results} natija)</TD>
              <TD className="text-right font-tabular font-semibold">{g.averageOverall}</TD>
              <TD className="text-[12.5px] text-ink-600">
                {g.trend.map((m) => <span key={m.month} className="mr-3 inline-block whitespace-nowrap">{formatYearMonth(m.month)}: <b className="font-tabular">{m.average}</b></span>)}
              </TD>
              <TD><Trend months={g.trend} /></TD>
            </TRow>
          ))}</TBody>
        </Table>}
    </>
  );
}
