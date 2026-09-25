"use client";

import { useState } from "react";
import Link from "next/link";
import { useQuery } from "@tanstack/react-query";
import { ArrowLeft, Award, CalendarClock, ClipboardList, GraduationCap, Percent, TrendingUp, Users2, Wallet } from "lucide-react";
import { CartesianGrid, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import {
  fetchBranchDashboard, type AcademicBlock, type BranchDashboard as BranchDashboardData, type BranchTeacherRow, type LeadFunnel,
} from "@/lib/api/dashboard";
import { formatMoney } from "@/lib/api/finance";
import { ApiError } from "@/lib/api-client";
import { formatYearMonth } from "@/lib/format-date";
import { Alert } from "@/components/ui/Alert";
import { Button } from "@/components/ui/Button";
import { Card, CardBody, CardHeader, CardTitle } from "@/components/ui/Card";
import { EmptyState } from "@/components/ui/EmptyState";
import { Select } from "@/components/ui/Field";
import { PageHeader } from "@/components/ui/PageHeader";
import { Skeleton } from "@/components/ui/Skeleton";
import { StatTile } from "@/components/ui/StatTile";
import { Table, THead, TBody, TRow, TH, TD } from "@/components/ui/Table";
import { Tabs } from "@/components/ui/Tabs";
import { FunnelChart } from "@/components/crm/FunnelChart";
import { OrganizationCards, PERIODS, periodRange } from "./OrganizationDashboard";
import { percent, rateTone } from "./shared";

// Chart colours are the design tokens, so the charts follow the theme (the ramp turns over at night).
const BRAND = "var(--color-brand-solid)";
const GRID = "var(--color-border)";
const AXIS = "var(--color-ink-500)";
const CHART_TEXT = { fontSize: 12, fill: AXIS };

/**
 * A single branch's own dashboard (docs/00_TZ.md §5.2, S05.03): one request feeds every tab, so flipping between them costs nothing.
 * The server decides what the caller may see — no Finance tab without payments.view, no teachers' table without
 * dashboard.teachers.view — and this screen simply draws what came back.
 */
export function BranchDashboard({ branchId, back }: { branchId: string; back: { href: string; label: string } }) {
  const [period, setPeriod] = useState<string>("month");
  const query = useQuery({
    queryKey: ["dashboard", "branch", branchId, period],
    queryFn: () => fetchBranchDashboard(branchId, periodRange(period)),
  });
  const data = query.data;

  return (
    <main className="p-4 sm:p-6 lg:p-8">
      <Link href={back.href} className="mb-4 inline-flex items-center gap-1.5 text-[13px] text-ink-500 hover:text-ink-800">
        <ArrowLeft className="size-3.5" aria-hidden />
        {back.label}
      </Link>
      <PageHeader
        title={data?.branchName ?? "Filial"}
        description={data ? `${data.address} · ${data.timezone}` : undefined}
        actions={
          <Select value={period} onChange={(e) => setPeriod(e.target.value)} aria-label="Davr" className="w-auto min-w-36">
            {PERIODS.map((p) => (
              <option key={p.value} value={p.value}>{p.label}</option>
            ))}
          </Select>
        }
      />

      {query.isError && (
        <div className="space-y-3">
          <Alert>{query.error instanceof ApiError ? query.error.message : "Ko'rsatkichlarni yuklab bo'lmadi."}</Alert>
          <Button onClick={() => void query.refetch()}>Qayta urinish</Button>
        </div>
      )}
      {query.isPending && (
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          {Array.from({ length: 8 }, (_, i) => <Skeleton key={i} className="h-24" />)}
        </div>
      )}
      {data && <BranchTabs data={data} />}
    </main>
  );
}

function BranchTabs({ data }: { data: BranchDashboardData }) {
  return (
    <Tabs
      tabs={[
        { id: "overview", label: "Umumiy", content: <OrganizationCards data={data.overview} lockedBranch hideMoney={!data.financeVisible} /> },
        { id: "academic", label: "O‘quv", content: <AcademicTab academic={data.academic} /> },
        ...(data.teachers ? [{ id: "teachers", label: "O‘qituvchilar", content: <TeachersTab rows={data.teachers.teachers} /> }] : []),
        { id: "crm", label: "CRM", content: <CrmTab crm={data.crm} /> },
        ...(data.financeVisible ? [{ id: "finance", label: "Moliya", content: <FinanceTab data={data} /> }] : []),
      ]}
    />
  );
}

// ── Academic ──────────────────────────────────────────────────────────────────────────────────────

/** A labelled bar on a fixed scale — 0–9 for bands, 0–100 for the percentages. Text beside it, so colour is never the only signal. */
function ScoreBar({ label, value, max, suffix = "" }: { label: string; value: number | null; max: number; suffix?: string }) {
  return (
    <div className="grid grid-cols-[6.5rem_1fr_3.5rem] items-center gap-3 text-[13px]">
      <span className="text-ink-600">{label}</span>
      <div className="h-2.5 overflow-hidden rounded-full bg-ink-100" role="img" aria-label={`${label}: ${value ?? "ma‘lumot yo‘q"}`}>
        {value !== null && <div className="h-full rounded-full bg-brand-solid" style={{ width: `${Math.min(100, (value / max) * 100)}%` }} />}
      </div>
      <span className="text-right font-tabular font-medium text-ink-900">{value === null ? "—" : `${value}${suffix}`}</span>
    </div>
  );
}

function AcademicTab({ academic: a }: { academic: AcademicBlock }) {
  const trend = a.trend.map((m) => ({ ...m, label: formatYearMonth(m.month) }));
  return (
    <div className="space-y-4">
      <div className="grid gap-4 sm:grid-cols-3">
        <StatTile label="Davomat" value={a.lessonsHeld === 0 ? "—" : percent(a.attendanceRate)} icon={CalendarClock}
          tone={a.lessonsHeld > 0 && a.attendanceRate < 0.7 ? "danger" : "brand"} hint={`${a.lessonsHeld} ta dars o‘tkazildi`} />
        <StatTile label="Bekor qilingan darslar" value={a.lessonsCancelled} icon={CalendarClock} hint="Davomat foiziga kirmaydi" />
        <StatTile label="Imtihon natijalari" value={a.results} icon={Award} hint="Davrdagi imtihonlar" />
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        <Card>
          <CardHeader><CardTitle>O‘rtacha ballar</CardTitle></CardHeader>
          <CardBody className="space-y-3">
            {a.results === 0
              ? <EmptyState icon={Award} title="Bu davrda imtihon natijasi yo‘q" />
              : <>
                <ScoreBar label="Listening" value={a.listening} max={9} />
                <ScoreBar label="Reading" value={a.reading} max={9} />
                <ScoreBar label="Writing" value={a.writing} max={9} />
                <ScoreBar label="Speaking" value={a.speaking} max={9} />
                <ScoreBar label="Overall" value={a.overall} max={9} />
                <div className="my-1 border-t border-border" />
                <ScoreBar label="Grammar" value={a.grammar} max={100} suffix="%" />
                <ScoreBar label="Vocabulary" value={a.vocabulary} max={100} suffix="%" />
              </>}
          </CardBody>
        </Card>

        <Card>
          <CardHeader><CardTitle>Imtihon dinamikasi (Overall)</CardTitle><span className="text-[12px] text-ink-500">oxirgi 6 oy</span></CardHeader>
          <CardBody>
            {trend.length === 0
              ? <EmptyState icon={TrendingUp} title="Oylik natijalar yo‘q" />
              : <div className="h-56 w-full" role="img" aria-label={`Overall bo‘yicha oylik o‘rtacha: ${trend.map((m) => `${m.label} ${m.average}`).join(", ")}`}>
                <ResponsiveContainer width="100%" height="100%">
                  <LineChart data={trend} margin={{ top: 12, right: 16, bottom: 0, left: -20 }}>
                    <CartesianGrid stroke={GRID} vertical={false} />
                    <XAxis dataKey="label" tick={CHART_TEXT} tickLine={false} axisLine={{ stroke: GRID }} />
                    <YAxis domain={[0, 9]} ticks={[0, 3, 6, 9]} tick={CHART_TEXT} tickLine={false} axisLine={false} />
                    <Tooltip formatter={(value) => [value, "O‘rtacha"]} contentStyle={{ borderRadius: 8, border: `1px solid ${GRID}` }} />
                    <Line type="monotone" dataKey="average" stroke={BRAND} strokeWidth={2.5} dot={{ r: 4, fill: BRAND }} isAnimationActive={false} />
                  </LineChart>
                </ResponsiveContainer>
              </div>}
          </CardBody>
        </Card>
      </div>
    </div>
  );
}

// ── Teachers ──────────────────────────────────────────────────────────────────────────────────────

const hours = (value: number | null) => (value === null ? "—" : value < 1 ? `${Math.round(value * 60)} daq` : `${value} soat`);

function TeachersTab({ rows }: { rows: BranchTeacherRow[] }) {
  if (rows.length === 0) return <EmptyState icon={Users2} title="Filialda o‘qituvchi yo‘q" description="Guruhga o‘qituvchi biriktirilganda shu yerda ko‘rinadi." />;
  return (
    <Table>
      <THead>
        <tr>
          <TH className="w-10">#</TH>
          <TH>O‘qituvchi</TH>
          <TH className="hidden text-right sm:table-cell">Guruh</TH>
          <TH className="hidden text-right sm:table-cell">Dars</TH>
          <TH className="text-right">Davomat</TH>
          <TH className="hidden text-right md:table-cell">Tekshirish vaqti</TH>
          <TH className="hidden text-right md:table-cell">Tekshirilmagan</TH>
          <TH className="hidden text-right lg:table-cell">Ushlab qolish</TH>
          <TH className="hidden text-right lg:table-cell">Support</TH>
        </tr>
      </THead>
      <TBody>
        {rows.map((t) => (
          <TRow key={t.teacherId}>
            <TD className="font-tabular text-ink-500">{t.rank ?? "—"}</TD>
            <TD className="font-medium text-ink-900">
              <Link href={`/teachers/${t.teacherId}`} className="hover:underline">{t.fullName}</Link>
            </TD>
            <TD className="hidden text-right font-tabular sm:table-cell">{t.groups}</TD>
            <TD className="hidden text-right font-tabular sm:table-cell">{t.lessonsHeld}</TD>
            <TD className={`text-right font-tabular ${t.counted === 0 ? "text-ink-400" : rateTone(t.attendanceRate)}`}>
              {t.counted === 0 ? "—" : percent(t.attendanceRate)}
              <span className="block text-[11.5px] font-normal text-ink-500">{t.attended}/{t.counted}</span>
            </TD>
            <TD className="hidden text-right font-tabular md:table-cell">{hours(t.averageReviewHours)}</TD>
            <TD className={`hidden text-right font-tabular md:table-cell ${t.homeworkPendingReview > 0 ? "text-warning-600" : ""}`}>{t.homeworkPendingReview}</TD>
            <TD className="hidden text-right font-tabular lg:table-cell">
              {t.retentionRate === null ? "—" : percent(t.retentionRate)}
              {t.retentionRate !== null && <span className="block text-[11.5px] text-ink-500">{t.retained}/{t.retentionBase}</span>}
            </TD>
            <TD className="hidden text-right font-tabular lg:table-cell">{t.openCases} ochiq · {t.closedCases} yopilgan</TD>
          </TRow>
        ))}
      </TBody>
    </Table>
  );
}

// ── CRM ───────────────────────────────────────────────────────────────────────────────────────────

function CrmTab({ crm }: { crm: LeadFunnel }) {
  if (crm.leads === 0) return <EmptyState icon={Users2} title="Bu davrda yangi lid yo‘q" />;
  return (
    <div className="space-y-4">
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <StatTile label="Lid → student" value={percent(crm.leadConversionRate)} icon={Percent} tone="brand" hint={`${crm.converted} / ${crm.leads}`} />
        <StatTile label="Trial → student" value={percent(crm.trialToStudentRate)} icon={GraduationCap} hint={`${crm.trialToStudent} / ${crm.trialAttended} ta trial kelgan`} />
        <StatTile label="Trial → to‘lov" value={percent(crm.trialToPaymentRate)} icon={Wallet} hint={`${crm.trialToPayment} ta o‘quvchi to‘lov qilgan`} />
        <StatTile label="Yo‘qotilgan" value={crm.lost} icon={TrendingUp} tone={crm.lost > 0 ? "warning" : "neutral"} hint="Yo‘qotilgan + qiziqmagan" />
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        <Card>
          <CardHeader><CardTitle>Voronka</CardTitle><span className="text-[12px] text-ink-500">davrda kelgan lidlar</span></CardHeader>
          <CardBody>
            <FunnelChart stages={crm.stages} />
          </CardBody>
        </Card>

        <Card>
          <CardHeader><CardTitle>Manba bo‘yicha</CardTitle></CardHeader>
          <Table bare>
            <THead>
              <tr>
                <TH>Manba</TH>
                <TH className="text-right">Lid</TH>
                <TH className="hidden text-right 2xl:table-cell">Trial keldi</TH>
                <TH className="text-right">Student</TH>
                <TH className="hidden text-right 2xl:table-cell">Yo‘qotilgan</TH>
                <TH className="text-right">Konversiya</TH>
              </tr>
            </THead>
            <TBody>
              {crm.sources.map((s) => (
                <TRow key={s.source}>
                  <TD className="font-medium text-ink-900">{s.source}</TD>
                  <TD className="text-right font-tabular">{s.leads}</TD>
                  <TD className="hidden text-right font-tabular 2xl:table-cell">{s.trialAttended}</TD>
                  <TD className="text-right font-tabular">{s.converted}</TD>
                  <TD className="hidden text-right font-tabular 2xl:table-cell">{s.lost}</TD>
                  <TD className="text-right font-tabular">{percent(s.conversionRate)}</TD>
                </TRow>
              ))}
            </TBody>
          </Table>
        </Card>
      </div>
    </div>
  );
}

// ── Finance ───────────────────────────────────────────────────────────────────────────────────────

function FinanceTab({ data }: { data: BranchDashboardData }) {
  const o = data.overview;
  return (
    <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
      <StatTile label="Tushum" value={formatMoney(o.collected)} icon={Wallet} tone="brand" hint="To‘lovlar, qaytarilganini ayirib" />
      <StatTile label="Kutilayotgan" value={formatMoney(o.expectedRevenue)} icon={Wallet} hint="Davrdagi hisob-fakturalar" />
      <StatTile label="Qarz" value={formatMoney(o.outstanding)} icon={Wallet} tone={o.debtors > 0 ? "danger" : "neutral"} hint={`${o.debtors} ta qarzdor`} />
      <StatTile label="Chegirmalar" value={formatMoney(o.discountAmount)} icon={ClipboardList} hint="Davrdagi hisob-fakturalar bo‘yicha" />
      <StatTile label="Qaytarilgan" value={formatMoney(o.refundedAmount)} icon={ClipboardList} tone={o.refundedAmount > 0 ? "warning" : "neutral"} hint="Davrdagi to‘lovlardan" />
    </div>
  );
}
