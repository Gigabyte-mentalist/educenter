"use client";

import { useState } from "react";
import Link from "next/link";
import { useQuery } from "@tanstack/react-query";
import {
  ArrowLeft, Bot, Building2, CalendarClock, GraduationCap, TrendingUp, UsersRound, Wallet,
} from "lucide-react";
import { fetchOrganizationDashboard, type OrganizationDashboard as OrganizationDashboardData } from "@/lib/api/dashboard";
import { listBranches } from "@/lib/api/branches";
import { formatMoney } from "@/lib/api/finance";
import { ApiError } from "@/lib/api-client";
import { PageHeader } from "@/components/ui/PageHeader";
import { StatTile } from "@/components/ui/StatTile";
import { Card, CardBody, CardHeader, CardTitle } from "@/components/ui/Card";
import { Table, THead, TBody, TRow, TH, TD } from "@/components/ui/Table";
import { Alert } from "@/components/ui/Alert";
import { Skeleton } from "@/components/ui/Skeleton";
import { Select } from "@/components/ui/Field";
import { EmptyState } from "@/components/ui/EmptyState";
import { CountLink, percent, rateTone } from "./shared";

const formatUsd = (value: number) => (value >= 1 ? `$${value.toFixed(2)}` : `$${value.toFixed(4)}`);

const monthStart = () => {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, "0")}-01`;
};

export const PERIODS = [
  { value: "month", label: "Shu oy" },
  { value: "30d", label: "Oxirgi 30 kun" },
  { value: "quarter", label: "Oxirgi 3 oy" },
  { value: "year", label: "Shu yil" },
] as const;

export function periodRange(value: string): { from?: string; to?: string } {
  const today = new Date();
  const iso = (d: Date) => d.toISOString().slice(0, 10);
  switch (value) {
    case "30d":
      return { from: iso(new Date(today.getTime() - 29 * 86_400_000)), to: iso(today) };
    case "quarter":
      return { from: iso(new Date(today.getFullYear(), today.getMonth() - 2, 1)), to: iso(today) };
    case "year":
      return { from: `${today.getFullYear()}-01-01`, to: iso(today) };
    default:
      return { from: monthStart(), to: iso(today) };
  }
}

/**
 * The Super Admin's global view, an Administrator's branch view, and (with `lockedBranchId`) a single
 * branch's own dashboard reached from `/branches/[id]`. The branch table is the point of the unlocked
 * screen: a per-branch number only means something next to the other branches' numbers.
 */
export function OrganizationDashboard({
  title, description, lockedBranchId, back,
}: { title: string; description: string; lockedBranchId?: string; back?: { href: string; label: string } }) {
  const [period, setPeriod] = useState<string>("month");
  const [branchId, setBranchId] = useState<string>(lockedBranchId ?? "");

  const range = periodRange(period);
  const effectiveBranchId = lockedBranchId ?? branchId;
  const query = useQuery({
    queryKey: ["dashboard", "organization", period, effectiveBranchId],
    queryFn: () => fetchOrganizationDashboard({ ...range, branchId: effectiveBranchId || undefined }),
  });
  const branchesQuery = useQuery({ queryKey: ["branches"], queryFn: listBranches, enabled: !lockedBranchId });

  const data = query.data;

  return (
    <main className="p-4 sm:p-6 lg:p-8">
      {back && (
        <Link href={back.href} className="mb-4 inline-flex items-center gap-1.5 text-[13px] text-ink-500 hover:text-ink-800">
          <ArrowLeft className="size-3.5" aria-hidden />
          {back.label}
        </Link>
      )}
      <PageHeader
        title={title}
        description={description}
        actions={
          <div className="flex flex-wrap gap-2">
            <Select value={period} onChange={(e) => setPeriod(e.target.value)} aria-label="Davr" className="w-auto min-w-36">
              {PERIODS.map((p) => (
                <option key={p.value} value={p.value}>
                  {p.label}
                </option>
              ))}
            </Select>
            {!lockedBranchId && (branchesQuery.data?.length ?? 0) > 1 && (
              <Select value={branchId} onChange={(e) => setBranchId(e.target.value)} aria-label="Filial" className="w-auto min-w-40">
                <option value="">Barcha filiallar</option>
                {branchesQuery.data?.map((b) => (
                  <option key={b.id} value={b.id}>
                    {b.name}
                  </option>
                ))}
              </Select>
            )}
          </div>
        }
      />

      {query.isError && (
        <Alert>{query.error instanceof ApiError ? query.error.message : "Ko'rsatkichlarni yuklab bo'lmadi."}</Alert>
      )}

      {query.isPending && (
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          {Array.from({ length: 8 }, (_, i) => (
            <Skeleton key={i} className="h-24" />
          ))}
        </div>
      )}

      {data && <OrganizationCards data={data} lockedBranch={!!lockedBranchId} />}
    </main>
  );
}

/**
 * The stat tiles, the branch table and the "needs attention" list of an organization dashboard. Its own component so a single branch's
 * dashboard can show exactly the same "Umumiy" tab. `hideMoney` leaves out the tiles that carry money, for a caller who may not read it.
 */
export function OrganizationCards({ data, lockedBranch, hideMoney }: { data: OrganizationDashboardData; lockedBranch?: boolean; hideMoney?: boolean }) {
  return (
    <div className="space-y-4">
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <StatTile
          label="O'quvchilar"
          value={data.totalStudents}
          icon={GraduationCap}
          tone="brand"
          hint={`${data.activeStudents} faol · ${data.newStudents} yangi · ${data.frozenStudents} muzlatilgan`}
        />
        {!hideMoney && (
          <>
            <StatTile
              label="Tushum"
              value={formatMoney(data.collected)}
              icon={Wallet}
              tone="brand"
              hint={`Kutilgan ${formatMoney(data.expectedRevenue)}`}
            />
            <StatTile
              label="Qarzdorlik"
              value={formatMoney(data.outstanding)}
              icon={Wallet}
              tone={data.debtors > 0 ? "danger" : "neutral"}
              hint={`${data.debtors} ta qarzdor`}
            />
          </>
        )}
        <StatTile
          label="Davomat"
          value={percent(data.attendanceRate)}
          icon={CalendarClock}
          tone={data.attendanceRate >= 0.85 ? "brand" : "warning"}
          hint={`Bugun ${data.lessonsToday} ta dars`}
        />
        <StatTile
          label="Lidlar"
          value={data.newLeads}
          icon={TrendingUp}
          hint={`${data.convertedLeads} ta o'quvchiga aylandi (${percent(data.leadConversionRate)})`}
        />
        <StatTile
          label="Sinov darsi"
          value={data.trialsBooked}
          icon={TrendingUp}
          hint={`${data.trialsAttended} ta kelgan · konversiya ${percent(data.trialConversionRate)}`}
        />
        <StatTile
          label="Guruhlar"
          value={data.activeGroups}
          icon={UsersRound}
          hint={`${data.teacherCount} ta o'qituvchi`}
        />
        {data.aiRequests !== null && (
          // Only a caller who may read the AI report is sent these numbers; the tile leads to that report.
          <Link href="/ai/usage" className="block rounded-xl focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-400">
            <StatTile
              label="AI ishlatilishi"
              value={`${data.aiRequests} so'rov`}
              icon={Bot}
              hint={`${formatUsd(data.aiCostUsd ?? 0)} · ${data.aiActiveStudents ?? 0} ta o'quvchi`}
            />
          </Link>
        )}
        <StatTile
          label="Ushlab qolish"
          value={percent(data.retentionRate)}
          icon={UsersRound}
          tone={data.retentionRate >= 0.8 ? "brand" : "warning"}
          hint="Bir oydan ortiq o'qiyotganlar orasida"
        />
      </div>

      <div className={lockedBranch ? "grid gap-4" : "grid gap-4 lg:grid-cols-3"}>
        {!lockedBranch && (
          <Card className="lg:col-span-2">
            <CardHeader>
              <CardTitle>Filiallar bo&apos;yicha</CardTitle>
              <span className="text-[12px] text-ink-500">
                {data.from} — {data.to}
              </span>
            </CardHeader>
            {data.branches.length === 0 ? (
              <EmptyState icon={Building2} title="Filial topilmadi" />
            ) : (
              <Table bare>
                <THead>
                  <tr>
                    <TH>Filial</TH>
                    <TH className="text-right">O&apos;quvchi</TH>
                    <TH className="text-right">Tushum</TH>
                    <TH className="hidden text-right sm:table-cell">Qarz</TH>
                    <TH className="hidden text-right sm:table-cell">Davomat</TH>
                    <TH className="hidden text-right md:table-cell">Konversiya</TH>
                  </tr>
                </THead>
                <TBody>
                  {[...data.branches]
                    .sort((a, b) => b.collected - a.collected)
                    .map((b) => (
                      <TRow key={b.branchId}>
                        <TD className="font-medium text-ink-900">
                          <Link href={`/branches/${b.branchId}`} className="hover:underline">
                            {b.branchName}
                          </Link>
                          <span className="block text-[11.5px] font-normal text-ink-500">
                            {b.activeGroups} guruh · bugun {b.lessonsToday} dars
                          </span>
                        </TD>
                        <TD className="text-right font-tabular">{b.students}</TD>
                        <TD className="text-right font-tabular">{formatMoney(b.collected)}</TD>
                        <TD className="hidden text-right font-tabular sm:table-cell">
                          <span className={b.outstanding > 0 ? "text-danger-600" : undefined}>
                            {formatMoney(b.outstanding)}
                          </span>
                          {b.debtors > 0 && (
                            <span className="block text-[11.5px] text-ink-500">{b.debtors} qarzdor</span>
                          )}
                        </TD>
                        <TD className={`hidden text-right font-tabular sm:table-cell ${rateTone(b.attendanceRate)}`}>
                          {percent(b.attendanceRate)}
                        </TD>
                        <TD className="hidden text-right font-tabular md:table-cell">
                          {percent(b.conversionRate)}
                          <span className="block text-[11.5px] text-ink-500">
                            {b.convertedLeads}/{b.newLeads}
                          </span>
                        </TD>
                      </TRow>
                    ))}
                </TBody>
              </Table>
            )}
          </Card>
        )}

        <Card>
          <CardHeader>
            <CardTitle>E&apos;tibor talab qiladi</CardTitle>
          </CardHeader>
          <CardBody className="space-y-1 p-2">
            <CountLink
              href="/support"
              label="Ochiq support holatlari"
              value={data.openSupportCases}
              tone="warning"
            />
            <CountLink
              href="/exams"
              label="Tekshirilmagan uy vazifalari"
              value={data.pendingHomeworkReviews}
              tone="warning"
            />
            {!hideMoney && <CountLink href="/finance" label="Qarzdorlar" value={data.debtors} tone="danger" />}
            <CountLink href="/leads" label="Yangi lidlar" value={data.newLeads} />
          </CardBody>
        </Card>
      </div>
    </div>
  );
}
