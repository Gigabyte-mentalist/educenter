"use client";

import Link from "next/link";
import { useQuery } from "@tanstack/react-query";
import { Activity, AlertOctagon, Clock, Cpu, Database, GitBranch, HardDrive, Layers, ShieldCheck } from "lucide-react";
import {
  getDeployment, getServerErrors, getSystemJobs, getSystemMetrics,
  type Deployment, type ServerError, type SystemJobs, type SystemMetrics,
} from "@/lib/api/system";
import { ApiError } from "@/lib/api-client";
import { formatBytes } from "@/lib/format-bytes";
import { formatShortDateTime, formatTime } from "@/lib/format-date";
import { usePermissions } from "@/lib/use-permissions";
import { Alert } from "@/components/ui/Alert";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { Card, CardBody, CardHeader, CardTitle } from "@/components/ui/Card";
import { EmptyState } from "@/components/ui/EmptyState";
import { PageHeader } from "@/components/ui/PageHeader";
import { Skeleton } from "@/components/ui/Skeleton";
import { StatTile } from "@/components/ui/StatTile";
import { Table, TBody, TD, TH, THead, TRow } from "@/components/ui/Table";
import { Tabs } from "@/components/ui/Tabs";

const errorText = (error: unknown) => (error instanceof ApiError ? error.message : "Yuklab bo'lmadi.");
const percent = (value: number) => `${(value * 100).toFixed(1)}%`;

/**
 * The Developer's workbench (docs/00_TZ.md §3.8, §53): request latency and errors, the database and disk, background jobs,
 * server errors and what is deployed. Each tab needs its own permission, so a person who holds only some of them sees only those.
 */
export default function SystemPage() {
  const { can, ready } = usePermissions();

  const tabs: { id: string; label: string; content: React.ReactNode }[] = [];
  if (can("system.metrics.view")) tabs.push({ id: "metrics", label: "So'rovlar va baza", content: <MetricsPanel /> });
  if (can("system.jobs.view")) tabs.push({ id: "jobs", label: "Fon vazifalari", content: <JobsPanel /> });
  if (can("logs.view")) tabs.push({ id: "logs", label: "Xatolar jurnali", content: <LogsPanel /> });
  if (can("deployments.view")) tabs.push({ id: "deployment", label: "Versiya", content: <DeploymentPanel /> });

  return (
    <main className="p-4 sm:p-6 lg:p-8">
      <PageHeader
        title="Tizim"
        description="API so'rovlari, ma'lumotlar bazasi, fon vazifalari va xatolar. Talabalar ma'lumoti bu yerda ko'rinmaydi — u uchun ruxsat so'rang."
        actions={can("production.data.request") && (
          <Link href="/system/access">
            <Button variant="secondary"><ShieldCheck className="size-4" aria-hidden />Ma&apos;lumotga kirish</Button>
          </Link>
        )}
      />
      {!ready && <Skeleton className="h-40" />}
      {ready && tabs.length === 0 && <EmptyState icon={Activity} title="Sizga ko'rsatiladigan bo'lim yo'q" />}
      {ready && tabs.length > 0 && <Tabs tabs={tabs} />}
    </main>
  );
}

function MetricsPanel() {
  const query = useQuery({ queryKey: ["system", "metrics"], queryFn: getSystemMetrics, refetchInterval: 15_000 });
  if (query.isPending) return <Skeleton className="h-64" />;
  if (query.isError) return <Alert>{errorText(query.error)}</Alert>;

  const { requests: r, database: d, storage: s } = query.data;
  const diskUsed = s.diskTotalBytes != null && s.diskFreeBytes != null ? s.diskTotalBytes - s.diskFreeBytes : null;

  return (
    <div className="space-y-4">
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <StatTile
          label="So'rovlar / daqiqa"
          value={r.requestsPerMinute}
          icon={Activity}
          hint={`${r.requests} ta so'nggi ${r.windowMinutes} daqiqada`}
        />
        <StatTile
          label="Kechikish p50 / p95 / p99"
          value={`${r.p50Ms} / ${r.p95Ms} / ${r.p99Ms}`}
          icon={Clock}
          tone={r.p95Ms > 1000 ? "warning" : "brand"}
          hint="millisekund"
        />
        <StatTile
          label="Xato ulushi (5xx)"
          value={percent(r.errorRate)}
          icon={AlertOctagon}
          tone={r.serverErrors > 0 ? "danger" : "neutral"}
          hint={`${r.serverErrors} server, ${r.clientErrors} mijoz xatosi`}
        />
        <StatTile
          label="Baza ulanishlari"
          value={`${d.connections} / ${d.maxConnections}`}
          icon={Database}
          tone={d.connections > d.maxConnections * 0.8 ? "warning" : "neutral"}
          hint="faol ulanish / chegara"
        />
      </div>

      <Card>
        <CardHeader>
          <CardTitle>So&apos;nggi 30 daqiqa</CardTitle>
          <span className="text-[12px] text-ink-500">{formatShortDateTime(r.collectingSince)} dan beri yig&apos;ilmoqda</span>
        </CardHeader>
        <CardBody>
          <Timeline buckets={r.timeline} />
          <p className="mt-2 text-[12px] text-ink-500">
            Hisoblagich xotirada turadi va API qayta ishga tushganda tozalanadi — tarix emas, joriy holat.
          </p>
        </CardBody>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Eng sekin endpointlar</CardTitle>
          <span className="text-[12px] text-ink-500">p95 bo&apos;yicha</span>
        </CardHeader>
        {r.endpoints.length === 0 ? (
          <EmptyState icon={Activity} title="Hali so'rov yo'q" />
        ) : (
          <Table bare>
            <THead>
              <tr>
                <TH>Endpoint</TH>
                <TH className="text-right">So&apos;rov</TH>
                <TH className="text-right">Xato</TH>
                <TH className="text-right">p50</TH>
                <TH className="text-right">p95</TH>
                <TH className="text-right">p99</TH>
              </tr>
            </THead>
            <TBody>
              {r.endpoints.map((e) => (
                <TRow key={`${e.method} ${e.route}`}>
                  <TD>
                    <span className="mr-2 font-mono text-[11px] font-semibold text-ink-500">{e.method}</span>
                    <span className="font-mono text-[12.5px] text-ink-900">/{e.route.replace(/^\//, "")}</span>
                  </TD>
                  <TD className="text-right font-tabular">{e.requests}</TD>
                  <TD className={`text-right font-tabular ${e.errors > 0 ? "font-medium text-danger-600" : ""}`}>{e.errors}</TD>
                  <TD className="text-right font-tabular">{e.p50Ms} ms</TD>
                  <TD className={`text-right font-tabular ${e.p95Ms > 1000 ? "font-medium text-warning-600" : ""}`}>{e.p95Ms} ms</TD>
                  <TD className="text-right font-tabular">{e.p99Ms} ms</TD>
                </TRow>
              ))}
            </TBody>
          </Table>
        )}
      </Card>

      <div className="grid gap-4 lg:grid-cols-2">
        <Card>
          <CardHeader><CardTitle>Sekin so&apos;rovlar (baza)</CardTitle></CardHeader>
          <CardBody className="space-y-3">
            {d.activeQueries.length === 0 && <p className="text-[13px] text-ink-500">Hozir 0.5 soniyadan uzoq ishlayotgan so&apos;rov yo&apos;q.</p>}
            {d.activeQueries.map((q) => (
              <div key={q.pid}>
                <p className="text-[12px] font-medium text-warning-600">{q.runningSeconds} soniya · pid {q.pid}</p>
                <p className="mt-0.5 break-all font-mono text-[11.5px] text-ink-700">{q.query}</p>
              </div>
            ))}
            {d.statementStatsAvailable ? (
              d.slowStatements.map((q) => (
                <div key={q.query}>
                  <p className="font-tabular text-[12px] text-ink-500">o&apos;rtacha {q.meanMs} ms · {q.calls} marta</p>
                  <p className="mt-0.5 break-all font-mono text-[11.5px] text-ink-700">{q.query}</p>
                </div>
              ))
            ) : (
              <p className="text-[12px] text-ink-500">
                Tarixiy statistika uchun serverda <span className="font-mono">pg_stat_statements</span> yoqilmagan; faqat hozir ishlayotgan so&apos;rovlar ko&apos;rinadi.
              </p>
            )}
          </CardBody>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Saqlash joyi</CardTitle>
            <HardDrive className="size-4 text-ink-400" aria-hidden />
          </CardHeader>
          <CardBody className="space-y-3">
            <div>
              <div className="flex items-baseline justify-between text-[13px]">
                <span className="text-ink-600">Disk</span>
                <span className="font-tabular text-ink-900">{formatBytes(diskUsed)} / {formatBytes(s.diskTotalBytes)}</span>
              </div>
              {diskUsed != null && s.diskTotalBytes ? (
                <div className="mt-1.5 h-2 overflow-hidden rounded-full bg-ink-100">
                  <div className="h-full rounded-full bg-brand-solid" style={{ width: `${Math.min(100, (diskUsed / s.diskTotalBytes) * 100)}%` }} />
                </div>
              ) : null}
            </div>
            {s.folders.length === 0 && <p className="text-[13px] text-ink-500">Yuklangan fayl yo&apos;q.</p>}
            {s.folders.map((f) => (
              <div key={f.name} className="flex items-baseline justify-between text-[13px]">
                <span className="font-mono text-ink-700">{f.name}</span>
                <span className="font-tabular text-ink-500">{f.files} ta fayl · {formatBytes(f.bytes)}</span>
              </div>
            ))}
          </CardBody>
        </Card>
      </div>
    </div>
  );
}

/** Thirty bars, one per minute; the red part is the share of that minute's requests that failed on the server. */
function Timeline({ buckets }: { buckets: SystemMetrics["requests"]["timeline"] }) {
  const peak = Math.max(1, ...buckets.map((b) => b.requests));
  return (
    <div className="flex h-24 items-end gap-1" role="img" aria-label="Daqiqalar bo'yicha so'rovlar soni">
      {buckets.map((b) => (
        <div key={b.minute} className="group relative flex h-full flex-1 flex-col justify-end" title={`${formatTime(b.minute)} — ${b.requests} ta so'rov, ${b.errors} ta xato`}>
          {b.errors > 0 && <div className="w-full rounded-t-sm bg-danger-600" style={{ height: `${(b.errors / peak) * 100}%` }} />}
          <div className={`w-full bg-brand-solid ${b.errors > 0 ? "" : "rounded-t-sm"}`} style={{ height: `${((b.requests - b.errors) / peak) * 100}%`, minHeight: b.requests > 0 ? 2 : 0 }} />
        </div>
      ))}
    </div>
  );
}

function JobsPanel() {
  const query = useQuery({ queryKey: ["system", "jobs"], queryFn: getSystemJobs, refetchInterval: 15_000 });
  if (query.isPending) return <Skeleton className="h-48" />;
  if (query.isError) return <Alert>{errorText(query.error)}</Alert>;

  const jobs: SystemJobs = query.data;
  if (!jobs.available) return <EmptyState icon={Layers} title="Fon vazifalari ishlamayapti" description={jobs.note ?? undefined} />;

  return (
    <div className="space-y-4">
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-5">
        <StatTile label="Navbatda" value={jobs.enqueued} icon={Layers} />
        <StatTile label="Bajarilmoqda" value={jobs.processing} icon={Cpu} tone="brand" />
        <StatTile label="Rejalashtirilgan" value={jobs.scheduled} icon={Clock} />
        <StatTile label="Muvaffaqiyatli" value={jobs.succeeded} icon={Activity} />
        <StatTile label="Xato" value={jobs.failed} icon={AlertOctagon} tone={jobs.failed > 0 ? "danger" : "neutral"} hint={`${jobs.servers} ta server`} />
      </div>

      <Card>
        <CardHeader><CardTitle>Takrorlanuvchi vazifalar</CardTitle></CardHeader>
        {jobs.recurring.length === 0 ? (
          <EmptyState icon={Clock} title="Takrorlanuvchi vazifa yo'q" />
        ) : (
          <Table bare>
            <THead><tr><TH>Vazifa</TH><TH>Jadval</TH><TH>Oxirgi</TH><TH>Keyingi</TH></tr></THead>
            <TBody>
              {jobs.recurring.map((j) => (
                <TRow key={j.id}>
                  <TD className="font-mono text-[12.5px] text-ink-900">{j.id}</TD>
                  <TD className="font-mono text-[12px] text-ink-600">{j.cron}</TD>
                  <TD className="text-[12.5px]">
                    {j.lastRun ? formatShortDateTime(j.lastRun) : "—"}
                    {j.lastState && <Badge tone={j.lastState === "Succeeded" ? "success" : "warning"} className="ml-2">{j.lastState}</Badge>}
                  </TD>
                  <TD className="text-[12.5px]">{j.nextRun ? formatShortDateTime(j.nextRun) : "—"}</TD>
                </TRow>
              ))}
            </TBody>
          </Table>
        )}
      </Card>

      <Card>
        <CardHeader><CardTitle>So&apos;nggi xatolar</CardTitle></CardHeader>
        {jobs.recentFailures.length === 0 ? (
          <EmptyState icon={Activity} title="Xato bilan tugagan vazifa yo'q" />
        ) : (
          <ul className="divide-y divide-border">
            {jobs.recentFailures.map((f) => (
              <li key={f.id} className="px-5 py-3">
                <p className="font-mono text-[12.5px] text-ink-900">{f.job}</p>
                <p className="mt-0.5 text-[12px] text-danger-600">{f.reason ?? "Sabab ko'rsatilmagan"}</p>
                {f.failedAt && <p className="text-[11.5px] text-ink-400">{formatShortDateTime(f.failedAt)}</p>}
              </li>
            ))}
          </ul>
        )}
      </Card>
    </div>
  );
}

function LogsPanel() {
  const query = useQuery({ queryKey: ["system", "logs"], queryFn: getServerErrors, refetchInterval: 15_000 });
  if (query.isPending) return <Skeleton className="h-48" />;
  if (query.isError) return <Alert>{errorText(query.error)}</Alert>;

  const errors: ServerError[] = query.data;
  return (
    <Card>
      <CardHeader>
        <CardTitle>Server xatolari (5xx)</CardTitle>
        <span className="text-[12px] text-ink-500">so&apos;nggi {errors.length} ta, xotirada</span>
      </CardHeader>
      {errors.length === 0 ? (
        <EmptyState icon={ShieldCheck} title="Server xatosi qayd etilmagan" />
      ) : (
        <Table bare>
          <THead><tr><TH>Vaqt</TH><TH>So&apos;rov</TH><TH>Xato</TH><TH className="text-right">Vaqti</TH></tr></THead>
          <TBody>
            {errors.map((e, i) => (
              <TRow key={`${e.at}-${i}`}>
                <TD className="whitespace-nowrap text-[12.5px]">{formatShortDateTime(e.at)}</TD>
                <TD>
                  <span className="mr-2 font-mono text-[11px] font-semibold text-ink-500">{e.method}</span>
                  <span className="break-all font-mono text-[12px] text-ink-900">{e.path}</span>
                  {e.traceId && <p className="font-mono text-[11px] text-ink-400">{e.traceId}</p>}
                </TD>
                <TD>
                  <Badge tone="danger">{e.status}</Badge>
                  {e.exceptionType && <p className="mt-1 font-mono text-[12px] text-ink-700">{e.exceptionType}</p>}
                  {e.message && <p className="max-w-md break-words text-[12px] text-ink-500">{e.message}</p>}
                </TD>
                <TD className="text-right font-tabular text-[12.5px]">{e.elapsedMs} ms</TD>
              </TRow>
            ))}
          </TBody>
        </Table>
      )}
    </Card>
  );
}

function DeploymentPanel() {
  const query = useQuery({ queryKey: ["system", "deployment"], queryFn: getDeployment });
  if (query.isPending) return <Skeleton className="h-48" />;
  if (query.isError) return <Alert>{errorText(query.error)}</Alert>;

  const d: Deployment = query.data;
  const rows: [string, string][] = [
    ["Versiya", `v${d.version}`],
    ["Muhit", d.environment],
    ["Ishga tushgan", formatShortDateTime(d.startedAt)],
    ["Runtime", d.runtime],
    ["Tizim", d.os],
    ["Server", d.machineName],
    ["Xotira", formatBytes(d.memoryBytes)],
  ];

  return (
    <div className="space-y-4">
      {d.pendingMigrations.length > 0 && (
        <Alert>
          Qo&apos;llanmagan migratsiya bor ({d.pendingMigrations.length} ta): {d.pendingMigrations.join(", ")}. Baza kodga mos emas.
        </Alert>
      )}
      <div className="grid gap-4 lg:grid-cols-2">
        <Card>
          <CardHeader><CardTitle>Joriy ishga tushirish</CardTitle><GitBranch className="size-4 text-ink-400" aria-hidden /></CardHeader>
          <CardBody className="space-y-2">
            {rows.map(([label, value]) => (
              <div key={label} className="flex items-baseline justify-between gap-4 text-[13px]">
                <span className="text-ink-500">{label}</span>
                <span className="break-all text-right font-mono text-[12.5px] text-ink-900">{value}</span>
              </div>
            ))}
          </CardBody>
        </Card>
        <Card>
          <CardHeader><CardTitle>Migratsiyalar</CardTitle><Database className="size-4 text-ink-400" aria-hidden /></CardHeader>
          <CardBody className="space-y-1.5">
            {d.appliedMigrations.map((m, i) => (
              <p key={m} className="flex items-center gap-2 font-mono text-[12px] text-ink-700">
                {i === 0 && <Badge tone="brand">oxirgi</Badge>}
                {m}
              </p>
            ))}
          </CardBody>
        </Card>
      </div>
    </div>
  );
}
