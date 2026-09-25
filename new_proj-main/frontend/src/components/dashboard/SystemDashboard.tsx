"use client";

import { useQuery } from "@tanstack/react-query";
import { Activity, Database, Gauge, Server, Workflow } from "lucide-react";
import { fetchSystemDashboard } from "@/lib/api/dashboard";
import { ApiError } from "@/lib/api-client";
import { PageHeader } from "@/components/ui/PageHeader";
import { StatTile } from "@/components/ui/StatTile";
import { Card, CardBody, CardHeader, CardTitle } from "@/components/ui/Card";
import { Table, THead, TBody, TRow, TH, TD } from "@/components/ui/Table";
import { Alert } from "@/components/ui/Alert";
import { Badge } from "@/components/ui/Badge";
import { Skeleton } from "@/components/ui/Skeleton";
import { EmptyState } from "@/components/ui/EmptyState";
import { formatShortDateTime } from "@/lib/format-date";

const bytes = (value: number | null) => {
  if (value == null) return "—";
  const units = ["B", "KB", "MB", "GB", "TB"];
  let n = value;
  let i = 0;
  while (n >= 1024 && i < units.length - 1) {
    n /= 1024;
    i += 1;
  }
  return `${n.toFixed(n < 10 && i > 0 ? 1 : 0)} ${units[i]}`;
};

/** ".NET TimeSpan" arrives as d.hh:mm:ss or hh:mm:ss — a person wants "3 kun 4 soat". */
const uptime = (value: string) => {
  const [dayPart, clock] = value.includes(".") ? value.split(".") : ["0", value];
  const [h = "0", m = "0"] = (clock ?? "").split(":");
  const days = Number(dayPart);
  const hours = Number(h);
  if (days > 0) return `${days} kun ${hours} soat`;
  if (hours > 0) return `${hours} soat ${Number(m)} daq`;
  return `${Number(m)} daqiqa`;
};

const percent = (value: number) => `${(value * 100).toFixed(1)}%`;

/**
 * The Developer's screen. The role has held system.health.view since Phase 1 with nowhere to use it, so a
 * developer logging in met an empty sidebar. Everything here is measured, never estimated — a number that
 * cannot be read from the database or this process is shown as a dash.
 */
export function SystemDashboard() {
  const query = useQuery({
    queryKey: ["dashboard", "system"],
    queryFn: fetchSystemDashboard,
    refetchInterval: 30_000,
  });
  const data = query.data;

  return (
    <main className="p-4 sm:p-6 lg:p-8">
      <PageHeader
        title="Tizim monitoringi"
        description="Baza, AI provayderlar, navbat va avtomatlashtirish holati. Har 30 soniyada yangilanadi."
      />

      {query.isError && (
        <Alert>{query.error instanceof ApiError ? query.error.message : "Holatni yuklab bo'lmadi."}</Alert>
      )}

      {query.isPending && (
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          {Array.from({ length: 4 }, (_, i) => (
            <Skeleton key={i} className="h-24" />
          ))}
        </div>
      )}

      {data && (
        <div className="space-y-4">
          {!data.databaseReachable && (
            <Alert>
              Ma&apos;lumotlar bazasiga ulanib bo&apos;lmadi.
              {data.databaseError && (
                <span className="mt-1 block font-mono text-[12px]">{data.databaseError}</span>
              )}
            </Alert>
          )}

          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            <StatTile
              label="Baza"
              value={data.databaseReachable ? `${data.databaseLatencyMs} ms` : "Ulanmadi"}
              icon={Database}
              tone={data.databaseReachable ? "brand" : "danger"}
              hint={bytes(data.databaseSizeBytes)}
            />
            <StatTile
              label="API ishlash vaqti"
              value={uptime(data.uptime)}
              icon={Server}
              hint={`${data.environment} · v${data.apiVersion}`}
            />
            <StatTile
              label="AI so'rovlari (24 soat)"
              value={data.aiRequests24h}
              icon={Activity}
              tone={data.aiErrorRate24h > 0.1 ? "danger" : "neutral"}
              hint={`${data.aiFailures24h} xato (${percent(data.aiErrorRate24h)}) · p95 ${data.aiP95LatencyMs} ms`}
            />
            <StatTile
              label="AI xarajati (24 soat)"
              value={`$${data.aiCost24h.toFixed(4)}`}
              icon={Gauge}
              hint={`${data.activeSessions} faol sessiya · ${data.auditEvents24h} audit yozuvi`}
            />
          </div>

          {(data.queuedWritingJobs > 0 || data.processingWritingJobs > 0 || data.failedWritingJobs24h > 0) && (
            <Card>
              <CardHeader>
                <CardTitle>Fon vazifalari (Writing)</CardTitle>
              </CardHeader>
              <CardBody className="flex flex-wrap gap-6">
                <div>
                  <p className="text-[12px] text-ink-500">Navbatda</p>
                  <p className="font-tabular text-xl font-semibold text-ink-900">{data.queuedWritingJobs}</p>
                </div>
                <div>
                  <p className="text-[12px] text-ink-500">Bajarilmoqda</p>
                  <p className="font-tabular text-xl font-semibold text-ink-900">{data.processingWritingJobs}</p>
                </div>
                <div>
                  <p className="text-[12px] text-ink-500">24 soatda xato</p>
                  <p
                    className={`font-tabular text-xl font-semibold ${
                      data.failedWritingJobs24h > 0 ? "text-danger-600" : "text-ink-900"
                    }`}
                  >
                    {data.failedWritingJobs24h}
                  </p>
                </div>
              </CardBody>
            </Card>
          )}

          <div className="grid gap-4 lg:grid-cols-2">
            <Card>
              <CardHeader>
                <CardTitle>AI provayderlar (24 soat)</CardTitle>
              </CardHeader>
              {data.providers.length === 0 ? (
                <EmptyState icon={Activity} title="So'rov bo'lmagan" />
              ) : (
                <Table bare>
                  <THead>
                    <tr>
                      <TH>Provayder</TH>
                      <TH className="text-right">So&apos;rov</TH>
                      <TH className="text-right">Xato</TH>
                      <TH className="text-right">p95</TH>
                      <TH className="text-right">Xarajat</TH>
                    </tr>
                  </THead>
                  <TBody>
                    {data.providers.map((p) => (
                      <TRow key={p.provider}>
                        <TD className="font-medium text-ink-900">{p.provider}</TD>
                        <TD className="text-right font-tabular">{p.requests}</TD>
                        <TD className="text-right font-tabular">
                          <span className={p.failures > 0 ? "text-danger-600" : undefined}>
                            {p.failures} ({percent(p.errorRate)})
                          </span>
                        </TD>
                        <TD className="text-right font-tabular">{p.p95LatencyMs} ms</TD>
                        <TD className="text-right font-tabular">${p.costUsd.toFixed(4)}</TD>
                      </TRow>
                    ))}
                  </TBody>
                </Table>
              )}
            </Card>

            <Card>
              <CardHeader>
                <CardTitle>Avtomatlashtirish yurishlari</CardTitle>
              </CardHeader>
              {data.recentAutomationRuns.length === 0 ? (
                <EmptyState icon={Workflow} title="Yurish qayd etilmagan" />
              ) : (
                <ul className="divide-y divide-border">
                  {data.recentAutomationRuns.map((r, i) => (
                    <li key={`${r.ruleCode}-${r.ranAt}-${i}`} className="px-5 py-3">
                      <div className="flex items-start justify-between gap-2">
                        <span className="font-mono text-[12.5px] text-ink-900">{r.ruleCode}</span>
                        <Badge tone="neutral">{r.itemCount}</Badge>
                      </div>
                      <p className="mt-0.5 text-[12px] text-ink-500">
                        {formatShortDateTime(r.ranAt)} ·{" "}
                        {r.summary}
                      </p>
                    </li>
                  ))}
                </ul>
              )}
            </Card>
          </div>

          <Card>
            <CardHeader>
              <CardTitle>Jadval hajmlari</CardTitle>
              <span className="text-[12px] text-ink-500">planner statistikasi, taxminiy</span>
            </CardHeader>
            {data.tables.length === 0 ? (
              <EmptyState icon={Database} title="Statistika yo'q" />
            ) : (
              <CardBody className="flex flex-wrap gap-x-8 gap-y-2">
                {data.tables.map((t) => (
                  <span key={t.name} className="text-[12.5px]">
                    <span className="font-mono text-ink-600">{t.name}</span>{" "}
                    <span className="font-tabular font-medium text-ink-900">{t.rows.toLocaleString("uz-UZ")}</span>
                  </span>
                ))}
              </CardBody>
            )}
          </Card>

          {data.databaseVersion && (
            <p className="font-mono text-[11.5px] text-ink-400">{data.databaseVersion}</p>
          )}
        </div>
      )}
    </main>
  );
}
