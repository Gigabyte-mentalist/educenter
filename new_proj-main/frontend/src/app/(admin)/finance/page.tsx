"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { Suspense, useState } from "react";
import { keepPreviousData, useMutation, useQuery } from "@tanstack/react-query";
import { AlertOctagon, CheckCircle2, RefreshCw, Search, Users, Wallet } from "lucide-react";
import {
  formatDay, formatMoney, getFinanceOverview, listDebtors, listInvoices, listPayments, runBilling,
  type Debtor, type ListInvoicesQuery,
} from "@/lib/api/finance";
import { ApiError } from "@/lib/api-client";
import { useAuthSession } from "@/lib/use-auth-session";
import { useDebouncedValue } from "@/lib/use-debounced-value";
import { PageHeader } from "@/components/ui/PageHeader";
import { StatTile } from "@/components/ui/StatTile";
import { Tabs } from "@/components/ui/Tabs";
import { Skeleton } from "@/components/ui/Skeleton";
import { Alert } from "@/components/ui/Alert";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { EmptyState } from "@/components/ui/EmptyState";
import { Input, Select } from "@/components/ui/Field";
import { Pagination } from "@/components/ui/Pagination";
import { Table, TBody, TD, TH, THead, TRow } from "@/components/ui/Table";
import { canTakePayments, InvoicesPanel, PaymentsPanel, RecordPaymentDrawer, useInvalidateFinance } from "@/components/Finance";
import { FinanceReport } from "@/components/FinanceReport";
import { PromoCodesPanel } from "@/components/PromoCodes";

type InvoiceFilter = "open" | "overdue" | "paid" | "cancelled" | "all";

const PAGE_SIZE = 25;

/** What each filter asks the server for — the filtering happens in the database, on the page's worth of rows only. */
const INVOICE_FILTERS: Record<InvoiceFilter, Pick<ListInvoicesQuery, "open" | "overdue" | "status">> = {
  open: { open: true },
  overdue: { overdue: true },
  paid: { status: "Paid" },
  cancelled: { status: "Cancelled" },
  all: {},
};

const errorText = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

export default function FinancePage() {
  return (
    <Suspense fallback={null}>
      <FinanceView />
    </Suspense>
  );
}

function FinanceView() {
  const role = useAuthSession().user?.role;
  const managers = canTakePayments(role);
  const invalidate = useInvalidateFinance();
  // Universal Search opens `/finance?highlight=<paymentId>`: the payments tab, showing that one payment.
  const highlightId = useSearchParams().get("highlight");

  // The four figures come from one aggregate query. Each table below loads only its own page, and only while its tab is open.
  const overviewQuery = useQuery({ queryKey: ["finance", "overview"], queryFn: getFinanceOverview });
  const overview = overviewQuery.data;

  const billing = useMutation({ mutationFn: runBilling, onSuccess: () => invalidate() });

  return (
    <main className="p-4 sm:p-6 lg:p-8">
      <PageHeader
        title="To'lovlar va qarzlar"
        description="Oylik hisob-fakturalar kurs narxidan avtomatik chiqadi. Muddati o'tgan qarz 3 kundan oshsa, operator navbatiga vazifa tushadi."
        actions={
          managers && (
            <Button variant="secondary" loading={billing.isPending} onClick={() => billing.mutate()}>
              <RefreshCw className="size-4" aria-hidden />
              Hisob-fakturalarni yangilash
            </Button>
          )
        }
      />

      {billing.isError && <Alert>{errorText(billing.error, "Hisob-kitobni ishga tushirib bo'lmadi.")}</Alert>}
      {billing.data && (
        <p role="status" className="mb-4 rounded-lg bg-brand-50 px-3 py-2.5 text-[13px] text-brand-800">
          Yangi hisob-fakturalar: {billing.data.invoicesIssued} · qarzdor deb belgilandi: {billing.data.studentsMarkedDebt} · qarzi yopildi: {billing.data.studentsCleared} · yangi qarz vazifalari: {billing.data.debtTasks}
        </p>
      )}

      {overviewQuery.isError && <Alert>{errorText(overviewQuery.error, "Moliyaviy ma'lumotlarni yuklab bo'lmadi.")}</Alert>}

      <div className="mb-6 grid grid-cols-1 gap-4 sm:grid-cols-3">
        <StatTile label="Muddati o'tgan qarz" value={overview ? formatMoney(overview.overdueDebt) : "…"} icon={AlertOctagon} tone={overview && overview.overdueDebt > 0 ? "danger" : "neutral"} />
        <StatTile label="Qarzdor studentlar" value={overview?.debtors ?? "…"} icon={Users} tone={overview && overview.debtors > 0 ? "warning" : "neutral"} />
        {overview?.todayIncome != null ? (
          <StatTile label="Bugungi tushum" value={formatMoney(overview.todayIncome)} icon={Wallet} tone="brand" />
        ) : (
          <StatTile label="Ochiq hisob-fakturalar" value={overview?.openInvoices ?? "…"} icon={Wallet} tone="brand" />
        )}
      </div>

      <Tabs
        defaultTabId={managers && highlightId ? "payments" : undefined}
        tabs={[
          { id: "debtors", label: `Qarzdorlar${overview ? ` (${overview.debtors})` : ""}`, content: <DebtorsTab canPay={managers} /> },
          { id: "invoices", label: "Hisob-fakturalar", content: <InvoicesTab /> },
          ...(managers
            ? [
                { id: "payments", label: "To'lovlar", content: <PaymentsTab onlyId={highlightId ?? undefined} /> },
                { id: "report", label: "Hisobot", content: <FinanceReport /> },
                { id: "promo", label: "Promo-kodlar", content: <PromoCodesPanel /> },
              ]
            : []),
        ]}
      />
    </main>
  );
}

/** The search box every finance table shares; typing resets the pager and is debounced before it reaches the server. */
function SearchBox({ value, onChange, label }: { value: string; onChange: (value: string) => void; label: string }) {
  return (
    <div className="relative w-full max-w-xs">
      <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-ink-400" aria-hidden />
      <Input value={value} onChange={(e) => onChange(e.target.value)} placeholder="Ism yoki ID bo'yicha qidirish" aria-label={label} className="pl-9" />
    </div>
  );
}

function DebtorsTab({ canPay }: { canPay: boolean }) {
  const [page, setPage] = useState(1);
  const [searchInput, setSearchInput] = useState("");
  const search = useDebouncedValue(searchInput);
  const [payTarget, setPayTarget] = useState<Debtor | null>(null);

  const query = useQuery({
    queryKey: ["finance", "debtors", { page, search }],
    queryFn: () => listDebtors({ page, size: PAGE_SIZE, search }),
    placeholderData: keepPreviousData,
  });
  const data = query.data;

  return (
    <div className="space-y-3">
      <SearchBox value={searchInput} onChange={(v) => { setSearchInput(v); setPage(1); }} label="Qarzdorlarni qidirish" />
      {query.isError && <Alert>{errorText(query.error, "Qarzdorlarni yuklab bo'lmadi.")}</Alert>}
      {query.isPending && <Skeleton className="h-40" />}
      {data && (
        <div className={query.isPlaceholderData ? "opacity-60 transition-opacity" : "transition-opacity"}>
          <DebtorsTable debtors={data.items} searching={search.trim() !== ""} onPay={canPay ? setPayTarget : undefined}>
            <Pagination page={data.pageNumber} pageCount={data.pageCount} total={data.total} pageSize={data.pageSize} onChange={setPage} />
          </DebtorsTable>
        </div>
      )}
      {payTarget && (
        <RecordPaymentDrawer
          key={payTarget.studentId}
          studentId={payTarget.studentId}
          studentName={payTarget.studentName}
          onClose={() => setPayTarget(null)}
        />
      )}
    </div>
  );
}

function InvoicesTab() {
  const [filter, setFilter] = useState<InvoiceFilter>("open");
  const [page, setPage] = useState(1);
  const [searchInput, setSearchInput] = useState("");
  const search = useDebouncedValue(searchInput);

  const query = useQuery({
    queryKey: ["finance", "invoices", "page", { filter, page, search }],
    queryFn: () => listInvoices({ ...INVOICE_FILTERS[filter], page, size: PAGE_SIZE, search }),
    placeholderData: keepPreviousData,
  });
  const data = query.data;

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <SearchBox value={searchInput} onChange={(v) => { setSearchInput(v); setPage(1); }} label="Hisob-fakturalarni qidirish" />
        <Select aria-label="Holat bo'yicha filtr" className="w-auto" value={filter} onChange={(e) => { setFilter(e.target.value as InvoiceFilter); setPage(1); }}>
          <option value="open">To&apos;lanmagan</option>
          <option value="overdue">Muddati o&apos;tgan</option>
          <option value="paid">To&apos;langan</option>
          <option value="cancelled">Bekor qilingan</option>
          <option value="all">Barchasi</option>
        </Select>
      </div>
      {query.isError && <Alert>{errorText(query.error, "Hisob-fakturalarni yuklab bo'lmadi.")}</Alert>}
      {query.isPending && <Skeleton className="h-40" />}
      {data && (
        <div className={query.isPlaceholderData ? "opacity-60 transition-opacity" : "transition-opacity"}>
          <InvoicesPanel invoices={data.items} showStudent />
          <Pager data={data} onChange={setPage} />
        </div>
      )}
    </div>
  );
}

/** @param onlyId A payment a Universal Search result opened: shown on its own until the person asks for the whole list. */
function PaymentsTab({ onlyId }: { onlyId?: string }) {
  const [page, setPage] = useState(1);
  const [searchInput, setSearchInput] = useState("");
  const [showAll, setShowAll] = useState(false);
  const search = useDebouncedValue(searchInput);
  const id = showAll ? undefined : onlyId;

  const query = useQuery({
    queryKey: ["finance", "payments", "page", { page, search, id }],
    queryFn: () => listPayments({ page, size: PAGE_SIZE, search, id }),
    placeholderData: keepPreviousData,
  });
  const data = query.data;

  return (
    <div className="space-y-3">
      {id ? (
        <div className="flex flex-wrap items-center justify-between gap-2 rounded-lg bg-brand-50 px-3 py-2.5 text-[13px] text-brand-800">
          <span>Qidiruvdan ochilgan to&apos;lov.</span>
          <Button variant="secondary" size="sm" onClick={() => setShowAll(true)}>Barcha to&apos;lovlar</Button>
        </div>
      ) : (
        <SearchBox value={searchInput} onChange={(v) => { setSearchInput(v); setPage(1); }} label="To'lovlarni qidirish" />
      )}
      {query.isError && <Alert>{errorText(query.error, "To'lovlarni yuklab bo'lmadi.")}</Alert>}
      {query.isPending && <Skeleton className="h-40" />}
      {data && (
        <div className={query.isPlaceholderData ? "opacity-60 transition-opacity" : "transition-opacity"}>
          <PaymentsPanel payments={data.items} showStudent />
          <Pager data={data} onChange={setPage} />
        </div>
      )}
    </div>
  );
}

/** The pager under a table that draws its own card — kept in a card of its own so the two do not double their borders. */
function Pager({ data, onChange }: { data: { pageNumber: number; pageCount: number; total: number; pageSize: number }; onChange: (page: number) => void }) {
  if (data.total === 0) return null;
  return (
    <div className="mt-2 overflow-hidden rounded-xl border border-border bg-surface">
      <Pagination page={data.pageNumber} pageCount={data.pageCount} total={data.total} pageSize={data.pageSize} onChange={onChange} className="border-t-0" />
    </div>
  );
}

function DebtorsTable({
  debtors, onPay, searching, children,
}: { debtors: Debtor[]; onPay?: (debtor: Debtor) => void; searching: boolean; children?: React.ReactNode }) {
  if (debtors.length === 0) {
    return (
      <div className="rounded-xl border border-border bg-surface">
        <EmptyState
          icon={CheckCircle2}
          title={searching ? "Hech narsa topilmadi" : "Qarzdor student yo'q"}
          description={searching ? "Qidiruvni o'zgartirib ko'ring." : "Muddati o'tgan to'lanmagan hisob-faktura hozircha yo'q."}
        />
      </div>
    );
  }

  return (
    <div className="overflow-hidden rounded-xl border border-border bg-surface">
      <Table bare>
        <THead>
          <tr>
            <TH>Student</TH>
            <TH>Guruh</TH>
            <TH>Telefon</TH>
            <TH>Kechikish</TH>
            <TH className="text-right">Qarz</TH>
            {onPay && <TH />}
          </tr>
        </THead>
        <TBody>
          {debtors.map((d) => (
            <TRow key={d.studentId}>
              <TD>
                <Link href={`/students/${d.studentId}`} className="font-medium text-ink-900 hover:text-brand-700">{d.studentName}</Link>
                <p className="font-mono text-[12px] text-ink-500">{d.studentNumber}</p>
              </TD>
              <TD className="text-ink-600">{d.groups.join(", ")}</TD>
              <TD className="font-mono text-[12.5px]">{d.phone ?? "—"}</TD>
              <TD>
                <Badge tone={d.daysOverdue >= 3 ? "danger" : "warning"} dot>{d.daysOverdue} kun</Badge>
                <p className="mt-0.5 text-[12px] text-ink-500">
                  {formatDay(d.oldestDueDate)} dan beri{d.overdueInvoices > 1 ? ` · ${d.overdueInvoices} ta hisob-faktura` : ""}
                </p>
              </TD>
              <TD className="text-right font-tabular font-semibold text-danger-600">{formatMoney(d.totalDebt)}</TD>
              {onPay && (
                <TD className="text-right">
                  <Button size="sm" variant="secondary" onClick={() => onPay(d)}>
                    <Wallet className="size-3.5" aria-hidden />
                    To&apos;lov qabul qilish
                  </Button>
                </TD>
              )}
            </TRow>
          ))}
        </TBody>
      </Table>
      {children}
    </div>
  );
}
