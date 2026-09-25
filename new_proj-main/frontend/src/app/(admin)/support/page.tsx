"use client";

import { Suspense, useState } from "react";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { AlertTriangle, CheckCircle2, LifeBuoy, Search } from "lucide-react";
import { getSupportCase, listSupportCases, type SupportCaseStatus } from "@/lib/api/support";
import { ApiError } from "@/lib/api-client";
import { useDebouncedValue } from "@/lib/use-debounced-value";
import { PageHeader } from "@/components/ui/PageHeader";
import { StatTile } from "@/components/ui/StatTile";
import { Tabs } from "@/components/ui/Tabs";
import { Skeleton } from "@/components/ui/Skeleton";
import { Alert } from "@/components/ui/Alert";
import { Input } from "@/components/ui/Field";
import { Pagination } from "@/components/ui/Pagination";
import { SupportCaseList } from "@/components/SupportCases";
import { useHighlightTarget } from "@/lib/use-highlight-target";

const PAGE_SIZE = 20;

const TABS = {
  review: { statuses: ["Recommended"] as SupportCaseStatus[], label: "Ko'rib chiqish kerak" },
  "in-progress": { statuses: ["Assigned"] as SupportCaseStatus[], label: "Jarayonda" },
  closed: { statuses: ["Dismissed", "Resolved"] as SupportCaseStatus[], label: "Yopilgan" },
};
type TabId = keyof typeof TABS;

const tabOf = (status: SupportCaseStatus): TabId => (status === "Recommended" ? "review" : status === "Assigned" ? "in-progress" : "closed");

export default function SupportPage() {
  return (
    <Suspense fallback={null}>
      <SupportPageContent />
    </Suspense>
  );
}

function SupportPageContent() {
  // Only the id is wanted here; the tab that actually renders the case scrolls to it once its list has loaded.
  const highlightId = useHighlightTarget(false);

  // A tab's count is a one-row query — the total comes with the page envelope — so the tabs and tiles are right without loading every case.
  const totals = {
    review: useTotal(TABS.review.statuses),
    "in-progress": useTotal(TABS["in-progress"].statuses),
    closed: useTotal(TABS.closed.statuses),
  };

  // Universal Search links to a case by id. With the list paged it may not be on the first page, so it is fetched on its own:
  // that says which tab to open and lets the tab pin it above the list.
  const highlighted = useQuery({
    queryKey: ["support-cases", "one", highlightId],
    queryFn: () => getSupportCase(highlightId!),
    enabled: !!highlightId,
    retry: false,
  });
  const defaultTabId = highlighted.data ? tabOf(highlighted.data.status) : undefined;
  const failed = totals.review.error ?? totals["in-progress"].error ?? totals.closed.error;
  const loading = totals.review.isPending || totals["in-progress"].isPending || totals.closed.isPending;
  const count = (id: TabId) => totals[id].data?.total ?? 0;

  return (
    <main className="p-4 sm:p-6 lg:p-8">
      <PageHeader
        title="Support"
        description="Yordamga muhtoj studentlar: tizim signallari (ketma-ket yo'qlik, topshirilmagan homework) va o'qituvchi tavsiyalari. Tizim faqat tavsiya beradi, qarorni xodim qabul qiladi."
      />

      {failed && <Alert>{failed instanceof ApiError ? failed.message : "Yuklab bo'lmadi."}</Alert>}

      <div className="mb-6 grid grid-cols-1 gap-4 sm:grid-cols-3">
        <StatTile label="Ko'rib chiqish kerak" value={count("review")} icon={AlertTriangle} tone="warning" />
        <StatTile label="Jarayonda" value={count("in-progress")} icon={LifeBuoy} tone="brand" />
        <StatTile label="Yopilgan" value={count("closed")} icon={CheckCircle2} />
      </div>

      {loading && (
        <div className="space-y-2">
          {Array.from({ length: 3 }).map((_, i) => <Skeleton key={i} className="h-20" />)}
        </div>
      )}

      {!loading && (highlightId ? highlighted.isFetched : true) && (
        <Tabs
          defaultTabId={defaultTabId}
          tabs={(Object.keys(TABS) as TabId[]).map((id) => ({
            id,
            label: `${TABS[id].label} (${count(id)})`,
            content: (
              <CasesTab
                key={id}
                statuses={TABS[id].statuses}
                pinned={highlighted.data && tabOf(highlighted.data.status) === id ? highlighted.data : undefined}
                emptyTitle={
                  id === "review" ? "Ko'rib chiqiladigan tavsiya yo'q"
                    : id === "in-progress" ? "Hozir support berilayotgan student yo'q"
                    : "Yopilgan support yozuvlari yo'q"
                }
                emptyDescription={id === "review" ? "Tizim signal aniqlasa yoki o'qituvchi tavsiya qilsa, shu yerda paydo bo'ladi." : undefined}
              />
            ),
          }))}
        />
      )}
    </main>
  );
}

function useTotal(statuses: SupportCaseStatus[]) {
  return useQuery({
    queryKey: ["support-cases", "total", statuses],
    queryFn: () => listSupportCases({ statuses, size: 1 }),
  });
}

function CasesTab({
  statuses, pinned, emptyTitle, emptyDescription,
}: {
  statuses: SupportCaseStatus[];
  pinned?: Awaited<ReturnType<typeof getSupportCase>>;
  emptyTitle: string;
  emptyDescription?: string;
}) {
  const [page, setPage] = useState(1);
  const [searchInput, setSearchInput] = useState("");
  const search = useDebouncedValue(searchInput);

  const query = useQuery({
    queryKey: ["support-cases", "list", statuses, { page, search }],
    queryFn: () => listSupportCases({ statuses, page, size: PAGE_SIZE, search }),
    placeholderData: keepPreviousData,
  });
  const data = query.data;
  const highlightId = useHighlightTarget(query.isSuccess);
  // The searched-for case leads the first page, once, unless it is already on it.
  const cases = data ? (pinned && page === 1 && !search && !data.items.some((c) => c.id === pinned.id) ? [pinned, ...data.items] : data.items) : [];

  return (
    <div className="space-y-3">
      <div className="relative max-w-sm">
        <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-ink-400" aria-hidden />
        <Input
          value={searchInput}
          onChange={(e) => { setSearchInput(e.target.value); setPage(1); }}
          placeholder="Student ismi yoki ID"
          aria-label="Support yozuvlarini qidirish"
          className="pl-9"
        />
      </div>
      {query.isError && <Alert>{query.error instanceof ApiError ? query.error.message : "Yuklab bo'lmadi."}</Alert>}
      {query.isPending && <Skeleton className="h-40" />}
      {data && (
        <div className={query.isPlaceholderData ? "opacity-60 transition-opacity" : "transition-opacity"}>
          <SupportCaseList cases={cases} highlightId={highlightId} emptyTitle={search ? "Hech narsa topilmadi" : emptyTitle} emptyDescription={search ? undefined : emptyDescription} />
          {data.total > 0 && (
            <div className="mt-2 overflow-hidden rounded-xl border border-border bg-surface">
              <Pagination page={data.pageNumber} pageCount={data.pageCount} total={data.total} pageSize={data.pageSize} onChange={setPage} className="border-t-0" />
            </div>
          )}
        </div>
      )}
    </div>
  );
}
