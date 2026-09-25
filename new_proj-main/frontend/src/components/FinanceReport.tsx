"use client";

import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { AlertOctagon, ArrowLeftRight, Percent, Receipt, TrendingUp, Wallet } from "lucide-react";
import { listBranches } from "@/lib/api/branches";
import { formatDay, formatMoney, getFinanceSummary, PAYMENT_METHOD_LABEL } from "@/lib/api/finance";
import { ApiError } from "@/lib/api-client";
import { Alert } from "@/components/ui/Alert";
import { EmptyState } from "@/components/ui/EmptyState";
import { Input, Label, Select } from "@/components/ui/Field";
import { Skeleton } from "@/components/ui/Skeleton";
import { StatTile } from "@/components/ui/StatTile";
import { Table, TBody, TD, TH, THead, TRow } from "@/components/ui/Table";

type Preset = "this-month" | "last-month" | "custom";

const isoDate = (d: Date) =>
  `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;

function monthRange(offset: number) {
  const now = new Date();
  return {
    from: isoDate(new Date(now.getFullYear(), now.getMonth() + offset, 1)),
    to: isoDate(new Date(now.getFullYear(), now.getMonth() + offset + 1, 0)),
  };
}

/** Branch finance numbers from docs/00_TZ.md §5.2 "Finance": revenue, expected revenue, paid, overdue, debt,
 * discounts, refunds — for a chosen period, with a per-branch and per-payment-method breakdown. */
export function FinanceReport() {
  const [preset, setPreset] = useState<Preset>("this-month");
  const [custom, setCustom] = useState(() => monthRange(0));
  const [branchId, setBranchId] = useState("");

  const range = preset === "this-month" ? monthRange(0) : preset === "last-month" ? monthRange(-1) : custom;
  const validRange = range.from !== "" && range.to !== "" && range.from <= range.to;

  const branchesQuery = useQuery({ queryKey: ["branches"], queryFn: listBranches });
  const summaryQuery = useQuery({
    queryKey: ["finance", "summary", range.from, range.to, branchId],
    queryFn: () => getFinanceSummary({ from: range.from, to: range.to, branchId: branchId || undefined }),
    enabled: validRange,
  });

  const s = summaryQuery.data;
  const collectedShare = s && s.expectedRevenue > 0 ? Math.round((s.paidOnBilled / s.expectedRevenue) * 100) : null;

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-end gap-3">
        <div>
          <Label htmlFor="report-period">Davr</Label>
          <Select id="report-period" className="w-auto" value={preset} onChange={(e) => setPreset(e.target.value as Preset)}>
            <option value="this-month">Joriy oy</option>
            <option value="last-month">O&apos;tgan oy</option>
            <option value="custom">Boshqa davr</option>
          </Select>
        </div>
        {preset === "custom" && (
          <>
            <div>
              <Label htmlFor="report-from">Boshlanish</Label>
              <Input id="report-from" type="date" value={custom.from} onChange={(e) => setCustom((c) => ({ ...c, from: e.target.value }))} />
            </div>
            <div>
              <Label htmlFor="report-to">Tugash</Label>
              <Input id="report-to" type="date" value={custom.to} onChange={(e) => setCustom((c) => ({ ...c, to: e.target.value }))} />
            </div>
          </>
        )}
        {(branchesQuery.data?.length ?? 0) > 1 && (
          <div>
            <Label htmlFor="report-branch">Filial</Label>
            <Select id="report-branch" className="w-auto" value={branchId} onChange={(e) => setBranchId(e.target.value)}>
              <option value="">Barcha filiallar</option>
              {branchesQuery.data!.map((b) => (
                <option key={b.id} value={b.id}>{b.name}</option>
              ))}
            </Select>
          </div>
        )}
      </div>

      {!validRange && <Alert>Tugash sanasi boshlanish sanasidan oldin bo&apos;lmasin.</Alert>}
      {summaryQuery.isError && (
        <Alert>{summaryQuery.error instanceof ApiError ? summaryQuery.error.message : "Hisobotni yuklab bo'lmadi."}</Alert>
      )}
      {summaryQuery.isLoading && <Skeleton className="h-48" />}

      {s && (
        <>
          <p className="text-[13px] text-ink-500">
            {formatDay(s.from)} – {formatDay(s.to)}
          </p>

          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
            <StatTile label="Tushum" value={formatMoney(s.collected)} icon={Wallet} tone="brand" hint="Davrda qabul qilingan to'lovlar" />
            <StatTile label="Qaytarilgan" value={formatMoney(s.refunded)} icon={ArrowLeftRight} tone={s.refunded > 0 ? "warning" : "neutral"} hint={`Sof tushum: ${formatMoney(s.netCollected)}`} />
            <StatTile
              label="Kutilgan tushum"
              value={formatMoney(s.expectedRevenue)}
              icon={TrendingUp}
              hint={collectedShare === null ? "Davrda hisob-faktura yo'q" : `Shundan to'langan: ${collectedShare}%`}
            />
            <StatTile label="Hisoblangan" value={formatMoney(s.billed)} icon={Receipt} hint={`To'lanmagan qoldiq: ${formatMoney(s.outstandingOnBilled)}`} />
            <StatTile label="Chegirmalar" value={formatMoney(s.discounts)} icon={Percent} tone={s.discounts > 0 ? "warning" : "neutral"} />
            <StatTile label="Muddati o'tgan qarz (bugun)" value={formatMoney(s.overdueNow)} icon={AlertOctagon} tone={s.overdueNow > 0 ? "danger" : "neutral"} hint={`${s.debtorsNow} ta qarzdor student`} />
          </div>

          <section>
            <h3 className="mb-2.5 text-[13px] font-semibold uppercase tracking-wide text-ink-500">Filiallar kesimida</h3>
            <div className="overflow-x-auto">
              <Table>
                <THead>
                  <tr>
                    <TH>Filial</TH>
                    <TH className="text-right">Tushum</TH>
                    <TH className="text-right">Qaytarilgan</TH>
                    <TH className="text-right">Kutilgan</TH>
                    <TH className="text-right">Chegirmalar</TH>
                    <TH className="text-right">Qarz</TH>
                    <TH className="text-right">Qarzdorlar</TH>
                  </tr>
                </THead>
                <TBody>
                  {s.byBranch.map((b) => (
                    <TRow key={b.branchId}>
                      <TD className="font-medium text-ink-900">{b.branchName}</TD>
                      <TD className="text-right font-tabular">{formatMoney(b.collected)}</TD>
                      <TD className="text-right font-tabular text-ink-600">{formatMoney(b.refunded)}</TD>
                      <TD className="text-right font-tabular">{formatMoney(b.expectedRevenue)}</TD>
                      <TD className="text-right font-tabular text-ink-600">{formatMoney(b.discounts)}</TD>
                      <TD className="text-right font-tabular font-medium text-danger-600">{formatMoney(b.overdueNow)}</TD>
                      <TD className="text-right font-tabular">{b.debtorsNow}</TD>
                    </TRow>
                  ))}
                </TBody>
              </Table>
            </div>
          </section>

          <section>
            <h3 className="mb-2.5 text-[13px] font-semibold uppercase tracking-wide text-ink-500">To&apos;lov usullari</h3>
            {s.byMethod.length === 0 ? (
              <div className="rounded-xl border border-border bg-surface">
                <EmptyState icon={Wallet} title="Bu davrda to'lov qabul qilinmagan" />
              </div>
            ) : (
              <div className="overflow-x-auto">
                <Table>
                  <THead>
                    <tr>
                      <TH>Usul</TH>
                      <TH className="text-right">Soni</TH>
                      <TH className="text-right">Summa</TH>
                    </tr>
                  </THead>
                  <TBody>
                    {s.byMethod.map((m) => (
                      <TRow key={m.method}>
                        <TD>{PAYMENT_METHOD_LABEL[m.method]}</TD>
                        <TD className="text-right font-tabular">{m.count}</TD>
                        <TD className="text-right font-tabular font-medium">{formatMoney(m.amount)}</TD>
                      </TRow>
                    ))}
                  </TBody>
                </Table>
              </div>
            )}
          </section>
        </>
      )}
    </div>
  );
}
