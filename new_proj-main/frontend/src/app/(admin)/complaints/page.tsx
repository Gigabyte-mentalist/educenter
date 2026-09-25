"use client";

import Link from "next/link";
import { Suspense, useState } from "react";
import { usePathname, useRouter } from "next/navigation";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { MessageSquareWarning, Plus, Search } from "lucide-react";
import { COMPLAINT_STATUS_LABEL, listComplaints, type Complaint, type ComplaintStatus } from "@/lib/api/complaints";
import { ApiError } from "@/lib/api-client";
import { formatDateTime } from "@/lib/format-date";
import { useDebouncedValue } from "@/lib/use-debounced-value";
import { useHighlightTarget } from "@/lib/use-highlight-target";
import { useNewParam } from "@/lib/use-new-param";
import { usePermissions } from "@/lib/use-permissions";
import { ComplaintCreateDrawer } from "@/components/complaints/ComplaintCreateDrawer";
import { ComplaintDrawer } from "@/components/complaints/ComplaintDrawer";
import { COMPLAINT_STATUS_TONE } from "@/components/complaints/tone";
import { Alert } from "@/components/ui/Alert";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { Card } from "@/components/ui/Card";
import { EmptyState } from "@/components/ui/EmptyState";
import { Input } from "@/components/ui/Field";
import { PageHeader } from "@/components/ui/PageHeader";
import { Pagination } from "@/components/ui/Pagination";
import { Skeleton } from "@/components/ui/Skeleton";
import { Tabs } from "@/components/ui/Tabs";

const PAGE_SIZE = 20;

const TABS = {
  new: { statuses: ["New"] as ComplaintStatus[], label: "Yangi" },
  "in-progress": { statuses: ["Assigned", "InProgress"] as ComplaintStatus[], label: "Jarayonda" },
  closed: { statuses: ["Resolved", "Closed"] as ComplaintStatus[], label: "Yopilgan" },
};
type TabId = keyof typeof TABS;

export default function ComplaintsPage() {
  return (
    <Suspense fallback={null}>
      <ComplaintsPageContent />
    </Suspense>
  );
}

/**
 * Complaints (docs/00_TZ.md §41). One screen for both sides: whoever runs a branch sees its complaints in three tabs and works them in the drawer;
 * everybody else who may file one — a student, a parent, a teacher — sees the ones they wrote, and rates the outcome. The server sends only what the
 * caller may see, so the two look alike.
 */
function ComplaintsPageContent() {
  const { can } = usePermissions();
  const router = useRouter();
  const pathname = usePathname();
  const staff = can("complaints.view");

  // Universal Search-style deep links: a notification opens `/complaints?highlight=<id>`, which opens that complaint.
  const highlightId = useHighlightTarget(false);
  const [openId, setOpenId] = useState<string | null>(null);
  const [dismissedId, setDismissedId] = useState<string | null>(null);
  const detailId = openId ?? (highlightId && highlightId !== dismissedId ? highlightId : null);

  const { requested: newRequested, clear: clearNew } = useNewParam();
  const [creating, setCreating] = useState(false);
  const showCreate = can("complaints.create") && (creating || newRequested);

  const totals = {
    new: useTotal(TABS.new.statuses),
    "in-progress": useTotal(TABS["in-progress"].statuses),
    closed: useTotal(TABS.closed.statuses),
  };
  const failed = totals.new.error ?? totals["in-progress"].error ?? totals.closed.error;
  const loading = totals.new.isPending || totals["in-progress"].isPending || totals.closed.isPending;
  const count = (id: TabId) => totals[id].data?.total ?? 0;
  // Open on the first tab that has anything: a person whose only complaint is resolved (and waits for their rating) should not land on an empty "Yangi".
  const firstTab = (Object.keys(TABS) as TabId[]).find((id) => count(id) > 0);

  return (
    <main className="p-4 sm:p-6 lg:p-8">
      <PageHeader
        title={staff ? "Shikoyatlar" : "Mening shikoyatlarim"}
        description={staff
          ? "Filialga kelgan shikoyatlar: xodimga biriktiring, yechim yozing, muallif natijani baholaydi."
          : "Yuborgan shikoyatlaringiz va ularning holati. Yechim yozilgach, natijani baholaysiz."}
        actions={can("complaints.create") && (
          <Button onClick={() => setCreating(true)}>
            <Plus className="size-4" aria-hidden />
            Yangi shikoyat
          </Button>
        )}
      />

      {failed && <Alert>{failed instanceof ApiError ? failed.message : "Yuklab bo'lmadi."}</Alert>}
      {loading && <div className="space-y-2">{Array.from({ length: 3 }, (_, i) => <Skeleton key={i} className="h-20" />)}</div>}

      {!loading && (
        <Tabs
          defaultTabId={firstTab}
          tabs={(Object.keys(TABS) as TabId[]).map((id) => ({
            id,
            label: `${TABS[id].label} (${count(id)})`,
            content: (
              <ComplaintsTab
                key={id}
                statuses={TABS[id].statuses}
                staff={staff}
                onOpen={setOpenId}
                emptyTitle={id === "new" ? (staff ? "Yangi shikoyat yo'q" : "Javob kutayotgan shikoyat yo'q")
                  : id === "in-progress" ? "Ko'rib chiqilayotgan shikoyat yo'q"
                  : "Yopilgan shikoyat yo'q"}
              />
            ),
          }))}
        />
      )}

      {detailId && (
        <ComplaintDrawer
          key={detailId}
          complaintId={detailId}
          onClose={() => {
            setOpenId(null);
            if (highlightId) {
              setDismissedId(highlightId);
              router.replace(pathname);
            }
          }}
        />
      )}
      {showCreate && <ComplaintCreateDrawer onClose={() => { setCreating(false); if (newRequested) clearNew(); }} />}
    </main>
  );
}

/** A tab's count is a one-row query — the total comes with the page envelope — so the tabs are right without loading every complaint. */
function useTotal(statuses: ComplaintStatus[]) {
  return useQuery({
    queryKey: ["complaints", "total", statuses],
    queryFn: () => listComplaints({ statuses, size: 1 }),
  });
}

function ComplaintsTab({
  statuses, staff, onOpen, emptyTitle,
}: {
  statuses: ComplaintStatus[];
  staff: boolean;
  onOpen: (id: string) => void;
  emptyTitle: string;
}) {
  const [page, setPage] = useState(1);
  const [searchInput, setSearchInput] = useState("");
  const search = useDebouncedValue(searchInput);

  const query = useQuery({
    queryKey: ["complaints", "list", statuses, { page, search }],
    queryFn: () => listComplaints({ statuses, page, size: PAGE_SIZE, search }),
    placeholderData: keepPreviousData,
  });
  const data = query.data;

  return (
    <div className="space-y-3">
      {staff && (
        <div className="relative max-w-sm">
          <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-ink-400" aria-hidden />
          <Input
            value={searchInput}
            onChange={(e) => { setSearchInput(e.target.value); setPage(1); }}
            placeholder="Mavzu, matn yoki ism"
            aria-label="Shikoyatlarni qidirish"
            className="pl-9"
          />
        </div>
      )}
      {query.isError && <Alert>{query.error instanceof ApiError ? query.error.message : "Yuklab bo'lmadi."}</Alert>}
      {query.isPending && <Skeleton className="h-40" />}
      {data && data.items.length === 0 && (
        <Card><EmptyState icon={MessageSquareWarning} title={search ? "Hech narsa topilmadi" : emptyTitle} /></Card>
      )}
      {data && data.items.length > 0 && (
        <div className={query.isPlaceholderData ? "opacity-60 transition-opacity" : "transition-opacity"}>
          <div className="divide-y divide-border overflow-hidden rounded-xl border border-border bg-surface">
            {data.items.map((c) => <ComplaintRow key={c.id} c={c} staff={staff} onOpen={() => onOpen(c.id)} />)}
          </div>
          <div className="mt-2 overflow-hidden rounded-xl border border-border bg-surface">
            <Pagination page={data.pageNumber} pageCount={data.pageCount} total={data.total} pageSize={data.pageSize} onChange={setPage} className="border-t-0" />
          </div>
        </div>
      )}
    </div>
  );
}

function ComplaintRow({ c, staff, onOpen }: { c: Complaint; staff: boolean; onOpen: () => void }) {
  return (
    <div className="flex flex-col gap-3 px-5 py-4 sm:flex-row sm:items-start">
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-2">
          <p className="text-[13.5px] font-medium text-ink-900">{c.subject}</p>
          <Badge tone={COMPLAINT_STATUS_TONE[c.status]} dot>{COMPLAINT_STATUS_LABEL[c.status]}</Badge>
          {c.canRate && <Badge tone="warning">Baho kutilmoqda</Badge>}
          {c.satisfactionScore !== null && <Badge tone="neutral">Baho: {c.satisfactionScore}/10</Badge>}
        </div>
        <p className="mt-1 line-clamp-2 whitespace-pre-line text-[13px] text-ink-600">{c.body}</p>
        <p className="mt-1 text-[12.5px] text-ink-500">
          {staff && <>{c.isMine ? "Siz" : c.authorName} · </>}
          {c.studentName && (staff
            ? <><Link href={`/students/${c.studentId}`} className="hover:text-brand-700">{c.studentName}</Link> · </>
            : <>{c.studentName} · </>)}
          {staff && <>{c.branchName} · </>}
          {formatDateTime(c.createdAt)}
          {c.assignedToName && <> · Ijrochi: {c.assignedToName}</>}
        </p>
      </div>
      <Button size="sm" variant="secondary" className="shrink-0" onClick={onOpen}>Ochish</Button>
    </div>
  );
}
