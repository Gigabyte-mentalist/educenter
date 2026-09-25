"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { Filter, Percent, Users2, Wallet } from "lucide-react";
import { getLeadFunnel, UNKNOWN_SOURCE, type FunnelStage } from "@/lib/api/crm";
import { listBranches } from "@/lib/api/branches";
import { ApiError } from "@/lib/api-client";
import { Alert } from "@/components/ui/Alert";
import { Card, CardBody, CardHeader, CardTitle } from "@/components/ui/Card";
import { EmptyState } from "@/components/ui/EmptyState";
import { Select } from "@/components/ui/Field";
import { Skeleton } from "@/components/ui/Skeleton";
import { StatTile } from "@/components/ui/StatTile";
import { Table, TBody, TD, TH, THead, TRow } from "@/components/ui/Table";
import { FunnelChart } from "@/components/crm/FunnelChart";
import { PERIODS, periodRange } from "@/components/dashboard/OrganizationDashboard";
import { percent } from "@/components/dashboard/shared";

const SOURCES = ["Instagram", "Telegram", "Website", "Referral", "Call", "Walk-in", UNKNOWN_SOURCE];

const stageOf = (stages: FunnelStage[], key: FunnelStage["key"]) => stages.find((s) => s.key === key);

/**
 * Where the students come from (docs/00_TZ.md §11): the leads that came in during the period, followed to wherever they have
 * got to — contacted, booked a trial, came, enrolled, paid — overall and for each source. The counting is the server's
 * (the branch dashboard's CRM block uses the same query), this only draws it.
 */
export function LeadFunnelView() {
  const [period, setPeriod] = useState<string>("quarter");
  const [branchId, setBranchId] = useState("");
  const [source, setSource] = useState("");

  const branchesQuery = useQuery({ queryKey: ["branches"], queryFn: listBranches });
  const range = periodRange(period);
  const query = useQuery({
    queryKey: ["leads", "funnel", period, branchId, source],
    queryFn: () => getLeadFunnel({ ...range, branchId: branchId || undefined, source: source || undefined }),
    placeholderData: keepPreviousData,
  });
  const data = query.data;

  const leads = data ? stageOf(data.stages, "leads")?.count ?? 0 : 0;
  const student = data ? stageOf(data.stages, "student") : undefined;
  const paid = data ? stageOf(data.stages, "paid") : undefined;

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-2">
        <Select value={period} onChange={(e) => setPeriod(e.target.value)} aria-label="Davr" className="w-auto min-w-36">
          {PERIODS.map((p) => <option key={p.value} value={p.value}>{p.label}</option>)}
        </Select>
        {(branchesQuery.data?.length ?? 0) > 1 && (
          <Select value={branchId} onChange={(e) => setBranchId(e.target.value)} aria-label="Filial" className="w-auto min-w-40">
            <option value="">Barcha filiallar</option>
            {branchesQuery.data?.map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}
          </Select>
        )}
        <Select value={source} onChange={(e) => setSource(e.target.value)} aria-label="Manba" className="w-auto min-w-36">
          <option value="">Barcha manbalar</option>
          {SOURCES.map((s) => <option key={s} value={s}>{s}</option>)}
        </Select>
      </div>

      {query.isError && <Alert>{query.error instanceof ApiError ? query.error.message : "Voronkani yuklab bo'lmadi."}</Alert>}
      {query.isPending && <Skeleton className="h-72" />}

      {data && leads === 0 && <EmptyState icon={Users2} title="Bu davrda yangi lid yo'q" description="Davrni yoki filtrni o'zgartirib ko'ring." />}

      {data && leads > 0 && (
        <>
          <div className="grid gap-4 sm:grid-cols-3">
            <StatTile label="Lidlar" value={leads} icon={Users2} hint="davrda kelgan" />
            <StatTile label="Lid → student" value={percent(student?.shareOfLeads ?? 0)} icon={Percent} tone="brand" hint={`${student?.count ?? 0} / ${leads}`} />
            <StatTile label="Lid → to'lov" value={percent(paid?.shareOfLeads ?? 0)} icon={Wallet} hint={`${paid?.count ?? 0} ta to'lov qilgan student`} />
          </div>

          <div className="grid gap-4 lg:grid-cols-2">
            <Card>
              <CardHeader><CardTitle>Voronka</CardTitle><span className="text-[12px] text-ink-500">davrda kelgan lidlar</span></CardHeader>
              <CardBody><FunnelChart stages={data.stages} /></CardBody>
            </Card>

            <Card>
              <CardHeader><CardTitle>Bosqichlar</CardTitle><Filter className="size-4 text-ink-400" aria-hidden /></CardHeader>
              <Table bare className="min-w-[420px]">
                <THead>
                  <tr>
                    <TH>Bosqich</TH>
                    <TH className="text-right">Soni</TH>
                    <TH className="text-right">Oldingisidan</TH>
                    <TH className="text-right">Lidlardan</TH>
                  </tr>
                </THead>
                <TBody>
                  {data.stages.map((s, i) => (
                    <TRow key={s.key}>
                      <TD className="font-medium text-ink-900">{s.label}</TD>
                      <TD className="text-right font-tabular">{s.count}</TD>
                      <TD className="text-right font-tabular">{i === 0 ? "—" : percent(s.conversionFromPrevious)}</TD>
                      <TD className="text-right font-tabular">{percent(s.shareOfLeads)}</TD>
                    </TRow>
                  ))}
                </TBody>
              </Table>
              <p className="border-t border-border px-4 py-3 text-[12px] leading-relaxed text-ink-500">
                Trial&apos;siz to&apos;g&apos;ridan-to&apos;g&apos;ri o&apos;qishga qabul qilinganlar ham «Student bo&apos;ldi» ga kiradi, shuning uchun bu bosqich
                oldingisidan katta bo&apos;lishi mumkin. To&apos;lov — bekor qilinmagan to&apos;lovi bor student.
              </p>
            </Card>
          </div>

          <Card>
            <CardHeader><CardTitle>Manba bo&apos;yicha</CardTitle></CardHeader>
            <Table bare className="min-w-[560px]">
              <THead>
                <tr>
                  <TH>Manba</TH>
                  <TH className="text-right">Lid</TH>
                  <TH className="text-right">Trial keldi</TH>
                  <TH className="text-right">Student</TH>
                  <TH className="text-right">To&apos;lov</TH>
                  <TH className="text-right">Konversiya</TH>
                </tr>
              </THead>
              <TBody>
                {data.bySource.map((row) => (
                  <TRow key={row.source}>
                    <TD className="font-medium text-ink-900">{row.source}</TD>
                    <TD className="text-right font-tabular">{stageOf(row.stages, "leads")?.count ?? 0}</TD>
                    <TD className="text-right font-tabular">{stageOf(row.stages, "trialAttended")?.count ?? 0}</TD>
                    <TD className="text-right font-tabular">{stageOf(row.stages, "student")?.count ?? 0}</TD>
                    <TD className="text-right font-tabular">{stageOf(row.stages, "paid")?.count ?? 0}</TD>
                    <TD className="text-right font-tabular">{percent(stageOf(row.stages, "student")?.shareOfLeads ?? 0)}</TD>
                  </TRow>
                ))}
              </TBody>
            </Table>
          </Card>
        </>
      )}
    </div>
  );
}
