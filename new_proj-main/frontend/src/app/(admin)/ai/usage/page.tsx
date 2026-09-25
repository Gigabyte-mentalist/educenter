"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Activity, AlertTriangle, Clock, Coins, Mic, RotateCcw, ShieldAlert, Users } from "lucide-react";
import {
  getAiLimits, getAiProviders, getAiUsageSummary, getWritingBenchmark, resetAiLimits, updateAiLimits,
  type AiUsageDay, type AiUsageGroup, type AiUsageSummary, type WritingBenchmark,
} from "@/lib/api/ai";
import { listBranches } from "@/lib/api/branches";
import { ApiError } from "@/lib/api-client";
import { useAuthSession } from "@/lib/use-auth-session";
import { PageHeader } from "@/components/ui/PageHeader";
import { Alert } from "@/components/ui/Alert";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { Card, CardBody, CardHeader, CardTitle } from "@/components/ui/Card";
import { EmptyState } from "@/components/ui/EmptyState";
import { Input, Label, Select } from "@/components/ui/Field";
import { Skeleton } from "@/components/ui/Skeleton";
import { StatTile } from "@/components/ui/StatTile";
import { Table, TBody, TD, TH, THead, TRow } from "@/components/ui/Table";
import { Tabs } from "@/components/ui/Tabs";
import { formatDayMonth, formatShortDateTime } from "@/lib/format-date";

const PERIODS = [7, 30, 90] as const;

const errorText = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);
const formatUsd = (value: number) => (value >= 1 ? `$${value.toFixed(2)}` : `$${value.toFixed(4)}`);
const formatDay = formatDayMonth;
const formatNumber = (value: number) => value.toLocaleString("uz-UZ");

function periodRange(days: number) {
  const to = new Date();
  const from = new Date();
  from.setDate(to.getDate() - (days - 1));
  const iso = (date: Date) => date.toISOString().slice(0, 10);
  return { from: iso(from), to: iso(to) };
}

export default function AiUsagePage() {
  const [days, setDays] = useState<number>(30);
  const [branchId, setBranchId] = useState("");
  const isSuperAdmin = useAuthSession().user?.role === "SUPER_ADMIN";
  const range = periodRange(days);

  const branchesQuery = useQuery({ queryKey: ["branches"], queryFn: listBranches });
  const summaryQuery = useQuery({
    queryKey: ["ai", "usage", range.from, range.to, branchId],
    queryFn: () => getAiUsageSummary({ ...range, branchId: branchId || undefined }),
  });

  return (
    <main className="p-4 sm:p-6 lg:p-8">
      <PageHeader
        title="AI monitoring"
        description="Gateway jurnalidan olingan raqamlar: so'rovlar, tokenlar, xarajat, kechikish va rad etilganlar. Har bir AI funksiyasi shu yerda avtomatik hisobga olinadi."
      />

      {/* Filters sit in one row above the numbers they change. */}
      <div className="mb-5 flex flex-wrap items-end gap-3">
        <div>
          <Label htmlFor="usage-period">Davr</Label>
          <Select id="usage-period" value={days} onChange={(e) => setDays(Number(e.target.value))} className="w-44">
            {PERIODS.map((option) => <option key={option} value={option}>So&apos;nggi {option} kun</option>)}
          </Select>
        </div>
        {branchesQuery.data && branchesQuery.data.length > 1 && (
          <div>
            <Label htmlFor="usage-branch">Filial</Label>
            <Select id="usage-branch" value={branchId} onChange={(e) => setBranchId(e.target.value)} className="w-56">
              <option value="">Barcha filiallar</option>
              {branchesQuery.data.map((branch) => <option key={branch.id} value={branch.id}>{branch.name}</option>)}
            </Select>
          </div>
        )}
      </div>

      <Tabs
        tabs={[
          { id: "usage", label: "Foydalanish", content: <UsageTab query={summaryQuery} /> },
          { id: "limits", label: "Limitlar", content: <LimitsTab canEdit={isSuperAdmin} /> },
          { id: "benchmark", label: "Writing benchmark", content: <BenchmarkTab /> },
        ]}
      />
    </main>
  );
}

function UsageTab({ query }: { query: { data?: AiUsageSummary; isLoading: boolean; isError: boolean; error: unknown } }) {
  const providersQuery = useQuery({ queryKey: ["ai", "providers"], queryFn: getAiProviders });

  if (query.isLoading) return <Skeleton className="h-96" />;
  if (query.isError) return <Alert>{errorText(query.error, "Hisobotni yuklab bo'lmadi.")}</Alert>;
  const summary = query.data!;
  const { totals } = summary;

  return (
    <div className="space-y-5">
      <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
        <StatTile label="So'rovlar" value={formatNumber(totals.requests)} icon={Activity}
          hint={`${formatNumber(totals.succeeded)} muvaffaqiyatli`} />
        <StatTile label="Xarajat" value={formatUsd(totals.costUsd)} icon={Coins} tone="brand"
          hint={`${formatNumber(totals.inputTokens + totals.outputTokens)} token`} />
        <StatTile label="O'rtacha kechikish" value={`${formatNumber(totals.averageLatencyMs)} ms`} icon={Clock} />
        <StatTile label="Xato va rad etilgan" value={formatNumber(totals.failed + totals.rejected)} icon={AlertTriangle}
          tone={totals.failed + totals.rejected > 0 ? "warning" : "neutral"}
          hint={`${formatNumber(totals.failed)} xato · ${formatNumber(totals.rejected)} rad etilgan`} />
      </div>

      <div className="grid grid-cols-2 gap-4 lg:grid-cols-3">
        <StatTile label="Writing tekshiruvlari" value={formatNumber(totals.writingEvaluations)} />
        <StatTile label="Tutor savollari" value={formatNumber(totals.tutorQuestions)} />
        <StatTile label="Speaking daqiqalari" value={formatNumber(totals.speakingMinutes)} icon={Mic} />
      </div>

      <Card>
        <CardHeader>
          <CardTitle>Kunlik xarajat</CardTitle>
          <span className="text-[12.5px] text-ink-500">{formatDay(summary.from)} — {formatDay(summary.to)}</span>
        </CardHeader>
        <CardBody>
          <DailyChart days={summary.daily} />
        </CardBody>
      </Card>

      <div className="grid grid-cols-1 gap-5 xl:grid-cols-2">
        <GroupCard title="Funksiya bo'yicha" rows={summary.byFeature} />
        <GroupCard title="Provider va model bo'yicha" rows={summary.byProvider} />
      </div>

      <div className="grid grid-cols-1 gap-5 xl:grid-cols-2">
        <Card>
          <CardHeader><CardTitle>Eng ko&apos;p sarflagan studentlar</CardTitle></CardHeader>
          {summary.topStudents.length === 0 && <EmptyState icon={Users} title="Ma'lumot yo'q" />}
          {summary.topStudents.length > 0 && (
            <Table>
              <THead><TRow><TH>Student</TH><TH>So&apos;rovlar</TH><TH>Speaking</TH><TH>Xarajat</TH></TRow></THead>
              <TBody>
                {summary.topStudents.map((student) => (
                  <TRow key={student.studentId ?? student.name}>
                    <TD>{student.name}</TD>
                    <TD className="font-tabular">{formatNumber(student.requests)}</TD>
                    <TD className="font-tabular">{student.speakingMinutes} daq</TD>
                    <TD className="font-tabular">{formatUsd(student.costUsd)}</TD>
                  </TRow>
                ))}
              </TBody>
            </Table>
          )}
        </Card>

        <Card>
          <CardHeader><CardTitle>Filiallar</CardTitle></CardHeader>
          {summary.byBranch.length === 0 && <EmptyState icon={Activity} title="Ma'lumot yo'q" />}
          {summary.byBranch.length > 0 && (
            <Table>
              <THead><TRow><TH>Filial</TH><TH>So&apos;rovlar</TH><TH>Xarajat</TH></TRow></THead>
              <TBody>
                {summary.byBranch.map((branch) => (
                  <TRow key={branch.branchId ?? branch.branchName}>
                    <TD>{branch.branchName}</TD>
                    <TD className="font-tabular">{formatNumber(branch.requests)}</TD>
                    <TD className="font-tabular">{formatUsd(branch.costUsd)}</TD>
                  </TRow>
                ))}
              </TBody>
            </Table>
          )}
        </Card>
      </div>

      {(summary.reasons.length > 0 || summary.recentFailures.length > 0) && (
        <Card>
          <CardHeader>
            <CardTitle>Rad etilgan va bajarilmagan so&apos;rovlar</CardTitle>
          </CardHeader>
          <CardBody className="space-y-4">
            <div className="flex flex-wrap gap-2">
              {summary.reasons.map((reason) => (
                <Badge key={reason.key} tone={reason.key === "PROMPT_INJECTION" ? "danger" : "warning"}>
                  {reason.key}: {formatNumber(reason.requests)}
                </Badge>
              ))}
            </div>
            {summary.recentFailures.length > 0 && (
              <ul className="divide-y divide-border text-[13px]">
                {summary.recentFailures.map((failure, i) => (
                  <li key={i} className="flex flex-wrap items-baseline gap-x-3 py-2">
                    <span className="text-ink-500">{formatShortDateTime(failure.at)}</span>
                    <span className="font-medium text-ink-800">{failure.feature} · {failure.taskKind}</span>
                    <span className="text-danger-600">{failure.reason}</span>
                    {failure.detail && <span className="truncate text-ink-500">{failure.detail}</span>}
                  </li>
                ))}
              </ul>
            )}
          </CardBody>
        </Card>
      )}

      <Card>
        <CardHeader><CardTitle>Provayderlar</CardTitle></CardHeader>
        {providersQuery.isLoading && <CardBody><Skeleton className="h-20" /></CardBody>}
        {providersQuery.data && (
          <Table>
            <THead><TRow><TH>Nomi</TH><TH>Turi</TH><TH>Holati</TH><TH>Model</TH></TRow></THead>
            <TBody>
              {providersQuery.data.providers.map((provider) => (
                <TRow key={provider.name}>
                  <TD>{provider.name}</TD>
                  <TD className="text-ink-500">{provider.type}</TD>
                  <TD>
                    {provider.active
                      ? <Badge tone="success" dot>Faol</Badge>
                      : <span className="text-[12.5px] text-ink-500">{provider.inactiveReason}</span>}
                  </TD>
                  <TD className="text-ink-600">
                    {[provider.chatModel, provider.embeddingModel].filter(Boolean).join(" · ") || "—"}
                  </TD>
                </TRow>
              ))}
            </TBody>
          </Table>
        )}
      </Card>
    </div>
  );
}

/** One measure (daily cost) over time. Requests ride along in the tooltip rather than on a second axis. */
function DailyChart({ days }: { days: AiUsageDay[] }) {
  if (days.length === 0) return <EmptyState icon={Activity} title="Bu davrda AI ishlatilmagan" />;

  const max = Math.max(...days.map((day) => day.costUsd));
  const total = days.reduce((sum, day) => sum + day.costUsd, 0);

  return (
    <div>
      <div
        className="flex h-40 items-end gap-0.5"
        role="img"
        aria-label={`Kunlik AI xarajati, ${days.length} kun, jami ${formatUsd(total)}`}
      >
        {days.map((day) => (
          <div key={day.date} className="group relative flex h-full flex-1 items-end">
            <div
              className="w-full rounded-t-[4px] bg-brand-solid transition-colors group-hover:bg-brand-solid-hover"
              style={{ height: `${max > 0 ? Math.max(2, (day.costUsd / max) * 100) : 2}%` }}
            />
            <span className="pointer-events-none absolute bottom-full left-1/2 z-10 mb-1.5 hidden -translate-x-1/2 whitespace-nowrap rounded-md bg-ink-900 px-2 py-1 text-[11px] text-white group-hover:block">
              {formatDay(day.date)} · {formatUsd(day.costUsd)} · {formatNumber(day.requests)} so&apos;rov
            </span>
          </div>
        ))}
      </div>
      <div className="mt-1.5 flex justify-between border-t border-border pt-1.5 text-[11.5px] text-ink-500">
        <span>{formatDay(days[0].date)}</span>
        <span className="font-tabular text-ink-700">Jami {formatUsd(total)}</span>
        <span>{formatDay(days[days.length - 1].date)}</span>
      </div>

      <details className="mt-3">
        <summary className="cursor-pointer text-[12.5px] text-ink-500">Jadval ko&apos;rinishi</summary>
        <Table>
          <THead><TRow><TH>Kun</TH><TH>So&apos;rovlar</TH><TH>Xarajat</TH></TRow></THead>
          <TBody>
            {days.map((day) => (
              <TRow key={day.date}>
                <TD>{formatDay(day.date)}</TD>
                <TD className="font-tabular">{formatNumber(day.requests)}</TD>
                <TD className="font-tabular">{formatUsd(day.costUsd)}</TD>
              </TRow>
            ))}
          </TBody>
        </Table>
      </details>
    </div>
  );
}

function GroupCard({ title, rows }: { title: string; rows: AiUsageGroup[] }) {
  return (
    <Card>
      <CardHeader><CardTitle>{title}</CardTitle></CardHeader>
      {rows.length === 0 && <EmptyState icon={Activity} title="Ma'lumot yo'q" />}
      {rows.length > 0 && (
        <Table>
          <THead><TRow><TH>Nomi</TH><TH>So&apos;rovlar</TH><TH>Kechikish</TH><TH>Xarajat</TH></TRow></THead>
          <TBody>
            {rows.map((row) => (
              <TRow key={row.key}>
                <TD>{row.key}</TD>
                <TD className="font-tabular">{formatNumber(row.requests)}<span className="text-ink-400"> / {formatNumber(row.succeeded)}</span></TD>
                <TD className="font-tabular">{formatNumber(row.averageLatencyMs)} ms</TD>
                <TD className="font-tabular">{formatUsd(row.costUsd)}</TD>
              </TRow>
            ))}
          </TBody>
        </Table>
      )}
    </Card>
  );
}

function LimitsTab({ canEdit }: { canEdit: boolean }) {
  const client = useQueryClient();
  const [edits, setEdits] = useState<Record<string, number>>({});
  const limitsQuery = useQuery({ queryKey: ["ai", "limits"], queryFn: getAiLimits });

  const save = useMutation({
    mutationFn: () => updateAiLimits(edits),
    onSuccess: () => { setEdits({}); client.invalidateQueries({ queryKey: ["ai"] }); },
  });
  const reset = useMutation({
    mutationFn: resetAiLimits,
    onSuccess: () => { setEdits({}); client.invalidateQueries({ queryKey: ["ai"] }); },
  });

  if (limitsQuery.isLoading) return <Skeleton className="h-64" />;
  if (limitsQuery.isError) return <Alert>{errorText(limitsQuery.error, "Limitlarni yuklab bo'lmadi.")}</Alert>;
  const limits = limitsQuery.data!;

  return (
    <Card>
      <CardHeader>
        <CardTitle>Kunlik limitlar</CardTitle>
        {limits.isCustomized
          ? <Badge tone="brand">O&apos;zgartirilgan</Badge>
          : <span className="text-[12.5px] text-ink-500">Standart qiymatlar</span>}
      </CardHeader>
      <CardBody className="space-y-4">
        <p className="text-[13px] text-ink-600">
          Limit bitta student uchun, kuniga. Hisob filial vaqt zonasi bo&apos;yicha yuritiladi va faqat muvaffaqiyatli
          so&apos;rovlar hisoblanadi.
        </p>

        <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
          {limits.limits.map((limit) => (
            <div key={limit.key}>
              <Label htmlFor={`limit-${limit.key}`}>{limit.label}</Label>
              <Input
                id={`limit-${limit.key}`}
                type="number"
                min={limit.min}
                max={limit.max}
                disabled={!canEdit}
                value={edits[limit.key] ?? limit.value}
                onChange={(e) => setEdits((prev) => ({ ...prev, [limit.key]: Number(e.target.value) }))}
              />
              <p className="mt-1 text-[12px] text-ink-500">
                {limit.unit} · {limit.min}–{limit.max} · standart {limit.default}
              </p>
            </div>
          ))}
        </div>

        {!canEdit && <Alert tone="info">Limitlarni faqat Super Admin o&apos;zgartira oladi.</Alert>}
        {save.isError && <Alert>{errorText(save.error, "Saqlab bo'lmadi.")}</Alert>}
        {save.isSuccess && <Alert tone="success">Limitlar saqlandi.</Alert>}

        {canEdit && (
          <div className="flex items-center gap-2">
            <Button loading={save.isPending} disabled={Object.keys(edits).length === 0} onClick={() => save.mutate()}>
              Saqlash
            </Button>
            <Button variant="secondary" loading={reset.isPending} onClick={() => reset.mutate()}>
              <RotateCcw className="size-4" aria-hidden />
              Standartga qaytarish
            </Button>
          </div>
        )}
      </CardBody>
    </Card>
  );
}

function BenchmarkTab() {
  const benchmarkQuery = useQuery({ queryKey: ["ai", "benchmark", "writing"], queryFn: () => getWritingBenchmark() });

  if (benchmarkQuery.isLoading) return <Skeleton className="h-64" />;
  if (benchmarkQuery.isError) return <Alert>{errorText(benchmarkQuery.error, "Benchmarkni yuklab bo'lmadi.")}</Alert>;
  const benchmark = benchmarkQuery.data as WritingBenchmark;

  return (
    <Card>
      <CardHeader>
        <CardTitle>Writing sifati: AI va o&apos;qituvchi</CardTitle>
        <span className="text-[12.5px] text-ink-500">
          {benchmark.overridden} / {benchmark.completed} baholash o&apos;qituvchi tomonidan tuzatilgan
        </span>
      </CardHeader>
      <CardBody className="space-y-4">
        <p className="text-[13px] text-ink-600">
          Har bir tuzatilgan insho — bitta o&apos;lchov nuqtasi. <b>Bias</b> musbat bo&apos;lsa AI o&apos;qituvchidan
          yuqori baho qo&apos;yyapti, manfiy bo&apos;lsa past. <b>O&apos;rtacha xato</b> — band bo&apos;yicha o&apos;rtacha
          farq. Qatorlar prompt versiyasi bo&apos;yicha ajratilgan, shuning uchun promptni o&apos;zgartirgandan keyingi
          natijani oldingisi bilan solishtirish mumkin. Bu hisobot mavjud natijalardan tuziladi — modelga qayta
          so&apos;rov yuborilmaydi.
        </p>

        {benchmark.rows.length === 0 && (
          <EmptyState icon={ShieldAlert} title="Hali ma'lumot yo'q"
            description="O'qituvchi AI bahosini tuzatganda, shu yerda taqqoslash paydo bo'ladi." />
        )}

        {benchmark.rows.length > 0 && (
          <div className="overflow-x-auto">
            <Table>
              <THead>
                <TRow>
                  <TH>Prompt</TH><TH>Model</TH><TH>Namunalar</TH><TH>AI o&apos;rt.</TH><TH>O&apos;qituvchi o&apos;rt.</TH>
                  <TH>Bias</TH><TH>O&apos;rtacha xato</TH><TH>±0.5 ichida</TH>
                </TRow>
              </THead>
              <TBody>
                {benchmark.rows.map((row) => (
                  <TRow key={`${row.promptVersion}-${row.model}`}>
                    <TD className="font-mono text-[12px]">{row.promptVersion}</TD>
                    <TD className="text-ink-500">{row.model ?? "—"}</TD>
                    <TD className="font-tabular">{row.samples}</TD>
                    <TD className="font-tabular">{row.aiMean.toFixed(1)}</TD>
                    <TD className="font-tabular">{row.teacherMean.toFixed(1)}</TD>
                    <TD className="font-tabular">
                      <span className={row.bias > 0.25 ? "text-warning-600" : row.bias < -0.25 ? "text-info-600" : "text-ink-700"}>
                        {row.bias > 0 ? "+" : ""}{row.bias.toFixed(2)}
                      </span>
                    </TD>
                    <TD className="font-tabular">{row.meanAbsoluteError.toFixed(2)}</TD>
                    <TD className="font-tabular">{row.withinHalfBand.toFixed(0)}%</TD>
                  </TRow>
                ))}
              </TBody>
            </Table>
          </div>
        )}
      </CardBody>
    </Card>
  );
}
