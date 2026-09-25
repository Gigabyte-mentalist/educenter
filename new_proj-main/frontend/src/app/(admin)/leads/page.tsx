"use client";

import { keepPreviousData, useInfiniteQuery, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { Suspense, useState } from "react";
import {
  DndContext, useDraggable, useDroppable, PointerSensor, useSensor, useSensors,
  DragOverlay, pointerWithin, rectIntersection, type CollisionDetection, type DragEndEvent,
} from "@dnd-kit/core";
import Link from "next/link";
import { Contact, Plus, PhoneCall, Sparkles, Send, Globe, Phone as PhoneIcon, UserCheck, DoorOpen, CalendarCheck, CheckCircle2, Search } from "lucide-react";
import { listBranches } from "@/lib/api/branches";
import { createLead, updateLeadStatus, listLeads, markTrialAttendance, type Lead, type LeadStatus, type PreferredTimeCode } from "@/lib/api/crm";
import { ApiError } from "@/lib/api-client";
import { usePermissions } from "@/lib/use-permissions";
import { useDebouncedValue } from "@/lib/use-debounced-value";
import { useNewParam } from "@/lib/use-new-param";
import { formatDayMonth, formatTime } from "@/lib/format-date";
import { PageHeader } from "@/components/ui/PageHeader";
import { Button } from "@/components/ui/Button";
import { Badge } from "@/components/ui/Badge";
import { StatTile } from "@/components/ui/StatTile";
import { EmptyState } from "@/components/ui/EmptyState";
import { Alert } from "@/components/ui/Alert";
import { Drawer } from "@/components/ui/Drawer";
import { Input, Label, Select } from "@/components/ui/Field";
import { Skeleton } from "@/components/ui/Skeleton";
import { LogCallDrawer } from "@/components/crm/LogCallDrawer";
import { CloseLeadDrawer } from "@/components/crm/CloseLeadDrawer";
import { ConvertLeadDrawer } from "@/components/crm/ConvertLeadDrawer";
import { LeadFunnelView } from "@/components/crm/LeadFunnelView";
import { LeadPreferencesFields } from "@/components/crm/LeadPreferencesFields";
import { TrialDrawer } from "@/components/TrialDrawer";

const SOURCE_ICON: Record<string, typeof Globe> = { Instagram: Sparkles, Telegram: Send, Website: Globe, Call: PhoneIcon, Referral: UserCheck, "Walk-in": DoorOpen };

/** The board's six columns. TrialMissed/NotInterested/Lost collapse into one "Yopilgan" column —
 * dropping a card there opens CloseLeadDrawer to pick which of the three actually happened. */
type ColumnKey = "Lead" | "Contacted" | "TrialBooked" | "TrialAttended" | "Closed" | "Converted";

const COLUMNS: ColumnKey[] = ["Lead", "Contacted", "TrialBooked", "TrialAttended", "Closed", "Converted"];
const COLUMN_LABEL: Record<ColumnKey, string> = {
  Lead: "Yangi", Contacted: "Bog'lanildi", TrialBooked: "Trial band qilindi",
  TrialAttended: "Trial'da qatnashdi", Closed: "Yopilgan", Converted: "Studentga aylandi",
};

function columnOf(status: LeadStatus): ColumnKey {
  if (status === "TrialMissed" || status === "NotInterested" || status === "Lost") return "Closed";
  return status;
}

/** Which lead statuses one board column shows — Yopilgan gathers the three ways a lead can end without becoming a student. */
const COLUMN_STATUSES: Record<ColumnKey, LeadStatus[]> = {
  Lead: ["Lead"],
  Contacted: ["Contacted"],
  TrialBooked: ["TrialBooked"],
  TrialAttended: ["TrialAttended"],
  Closed: ["TrialMissed", "NotInterested", "Lost"],
  Converted: ["Converted"],
};
const SOURCES = ["Instagram", "Telegram", "Website", "Referral", "Call", "Walk-in"];
const COLUMN_PAGE_SIZE = 20;

/**
 * One column's leads, twenty at a time. The board used to load every lead the organization had ever received and sort them
 * into columns in the browser — a year of leads later that is thousands of rows to show a few dozen that matter. Each column
 * is now its own query: its total comes with the first page, and "Yana ko'rsatish" fetches the next.
 */
function useLeadColumn(column: ColumnKey, filters: { source: string; search: string }) {
  return useInfiniteQuery({
    queryKey: ["leads", "column", column, filters],
    queryFn: ({ pageParam }) => listLeads({
      statuses: COLUMN_STATUSES[column], source: filters.source || undefined, search: filters.search, page: pageParam, size: COLUMN_PAGE_SIZE,
    }),
    initialPageParam: 1,
    getNextPageParam: (last) => (last.pageNumber < last.pageCount ? last.pageNumber + 1 : undefined),
    placeholderData: keepPreviousData,
  });
}

/**
 * A card is dropped into the column under the pointer. dnd-kit's default compares the dragged card's rectangle with the columns, and the card
 * that follows the pointer (the overlay) is wider than a column: dropping with the pointer over "Bog'lanildi" then landed in the next column,
 * whose share of the overlay was larger. The rectangle rule stays as the fallback for when the pointer is over no column at all.
 */
const columnUnderPointer: CollisionDetection = (args) => {
  const underPointer = pointerWithin(args);
  return underPointer.length > 0 ? underPointer : rectIntersection(args);
};

export default function LeadsPage() {
  return (
    <Suspense fallback={null}>
      <LeadsView />
    </Suspense>
  );
}

function LeadsView() {
  const router = useRouter();
  const queryClient = useQueryClient();
  const branchesQuery = useQuery({ queryKey: ["branches"], queryFn: listBranches });

  const [sourceFilter, setSourceFilter] = useState("");
  const [searchInput, setSearchInput] = useState("");
  const search = useDebouncedValue(searchInput);
  const [view, setView] = useState<"board" | "funnel">("board");
  const [createOpen, setCreateOpen] = useState(false);
  const [callTarget, setCallTarget] = useState<Lead | null>(null);
  const [closeTarget, setCloseTarget] = useState<Lead | null>(null);
  const [trialTarget, setTrialTarget] = useState<Lead | null>(null);
  const [convertTarget, setConvertTarget] = useState<{ lead: Lead; groupId?: string } | null>(null);
  const [activeLead, setActiveLead] = useState<Lead | null>(null);
  const { can } = usePermissions();
  // `?new=1` (the command palette's "Yangi lid") opens the form too, for whoever may create leads.
  const newParam = useNewParam();
  const showCreate = createOpen || (newParam.requested && can("leads.create"));

  const filters = { source: sourceFilter, search };
  const board: Record<ColumnKey, ReturnType<typeof useLeadColumn>> = {
    Lead: useLeadColumn("Lead", filters),
    Contacted: useLeadColumn("Contacted", filters),
    TrialBooked: useLeadColumn("TrialBooked", filters),
    TrialAttended: useLeadColumn("TrialAttended", filters),
    Closed: useLeadColumn("Closed", filters),
    Converted: useLeadColumn("Converted", filters),
  };

  const [fullName, setFullName] = useState("");
  const [phone, setPhone] = useState("");
  const [source, setSource] = useState("Instagram");
  const [interestedCourse, setInterestedCourse] = useState("");
  const [leadBranchId, setLeadBranchId] = useState("");
  const [preferredTimes, setPreferredTimes] = useState<PreferredTimeCode[]>([]);
  const [currentLevel, setCurrentLevel] = useState("");

  const createMutation = useMutation({
    mutationFn: () => createLead({
      branchId: leadBranchId, fullName, phone, source, interestedCourse,
      preferredTime: preferredTimes.join(",") || undefined, currentLevel: currentLevel.trim() || undefined,
    }),
    onSuccess: () => {
      setFullName(""); setPhone(""); setInterestedCourse(""); setPreferredTimes([]); setCurrentLevel("");
      setCreateOpen(false);
      newParam.clear();
      queryClient.invalidateQueries({ queryKey: ["leads"] });
    },
  });

  const moveMutation = useMutation({
    mutationFn: ({ id, status }: { id: string; status: LeadStatus }) => updateLeadStatus(id, status),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["leads"] }),
  });

  // "Keldi"/"Kelmadi" on a card: the trial and the lead's stage move together, as they do in the trial drawer.
  const attendanceMutation = useMutation({
    mutationFn: ({ trialId, attended }: { trialId: string; attended: boolean }) => markTrialAttendance(trialId, attended),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["leads"] }),
  });

  const branchNameById = new Map(branchesQuery.data?.map((b) => [b.id, b.name]));

  const counts = Object.fromEntries(COLUMNS.map((c) => [c, board[c].data?.pages[0]?.total ?? 0])) as Record<ColumnKey, number>;
  const totalLeads = COLUMNS.reduce((sum, c) => sum + counts[c], 0);
  const closedCount = counts.Closed;
  const conversionRate = totalLeads > 0 ? Math.round((counts.Converted / totalLeads) * 100) : 0;
  const firstLoad = COLUMNS.some((c) => board[c].isPending);
  const loadError = COLUMNS.map((c) => board[c].error).find(Boolean);
  const filtering = sourceFilter !== "" || search.trim() !== "";

  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 8 } }));

  function handleDragEnd(event: DragEndEvent) {
    setActiveLead(null);
    const { active, over } = event;
    if (!over) return;
    const lead = active.data.current?.lead as Lead | undefined;
    if (!lead) return;
    const target = over.id as ColumnKey;
    if (target === columnOf(lead.status)) return;
    if (lead.status === "Converted") return; // already a student — nothing to move
    if (target === "Converted") {
      // The status cannot be set by hand: becoming a student creates the account, the enrolment and the first invoice.
      setConvertTarget({ lead, groupId: lead.latestTrial?.attendanceStatus === "Attended" ? lead.latestTrial.groupId : undefined });
      return;
    }
    if (target === "Closed") {
      setCloseTarget(lead);
      return;
    }
    // "Trial band" without a trial is an empty promise: book the actual lesson instead of just changing the label.
    if (target === "TrialBooked") {
      setTrialTarget(lead);
      return;
    }
    if (target === "TrialAttended" && lead.latestTrial?.attendanceStatus === "Scheduled") {
      attendanceMutation.mutate({ trialId: lead.latestTrial.id, attended: true });
      return;
    }
    moveMutation.mutate({ id: lead.id, status: target as LeadStatus });
  }

  return (
    <main className="p-4 sm:p-6 lg:p-8">
      <PageHeader
        title="Lidlar"
        description={view === "board"
          ? "Yangi → Bog'lanildi → Trial band → Trial'da qatnashdi → Studentga aylandi. Har bir kartada shu bosqichdagi keyingi amal tugmasi bor; kartani sudrash yoki ustiga bosib profilni ochish ham mumkin."
          : "Manba → bog'lanish → trial → student → to'lov: lidlar qaysi manbadan kelib, qayerda to'xtab qolayotgani."}
        actions={
          <div className="flex flex-wrap items-center gap-2">
            <div role="group" aria-label="Ko'rinish" className="inline-flex rounded-lg border border-border-strong p-0.5">
              {(["board", "funnel"] as const).map((v) => (
                <button
                  key={v}
                  type="button"
                  aria-pressed={view === v}
                  onClick={() => setView(v)}
                  className={`rounded-md px-3 py-1.5 text-[13px] font-medium ${view === v ? "bg-brand-solid text-white" : "text-ink-600 hover:bg-ink-50"}`}
                >
                  {v === "board" ? "Doska" : "Voronka"}
                </button>
              ))}
            </div>
            {view === "board" && (
              <>
                <div className="relative">
                  <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-ink-400" aria-hidden />
                  <Input value={searchInput} onChange={(e) => setSearchInput(e.target.value)} placeholder="Ism yoki telefon" aria-label="Lidlarni qidirish" className="w-52 pl-9" />
                </div>
                <Select value={sourceFilter} onChange={(e) => setSourceFilter(e.target.value)} aria-label="Manba" className="w-auto min-w-36">
                  <option value="">Barcha manbalar</option>
                  {SOURCES.map((s) => <option key={s} value={s}>{s}</option>)}
                </Select>
              </>
            )}
            <Button onClick={() => setCreateOpen(true)}>
              <Plus className="size-4" aria-hidden />
              Yangi lid
            </Button>
          </div>
        }
      />

      {view === "funnel" && <LeadFunnelView />}

      {view === "board" && loadError && (
        <Alert>{loadError instanceof ApiError ? loadError.message : "Yuklab bo'lmadi."}</Alert>
      )}

      {view === "board" && firstLoad && !loadError && (
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-6">
          {Array.from({ length: 6 }, (_, i) => <Skeleton key={i} className="h-20" />)}
        </div>
      )}

      {view === "board" && !firstLoad && totalLeads === 0 && !filtering && (
        <div className="rounded-xl border border-border bg-surface">
          <EmptyState icon={Contact} title="Hali lid yo'q" description="Birinchi mijozni qo'shing." />
        </div>
      )}

      {view === "board" && !firstLoad && (totalLeads > 0 || filtering) && (
        <>
          <div className="mb-4 grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-6">
            <StatTile label="Yangi" value={counts.Lead} />
            <StatTile label="Bog'lanildi" value={counts.Contacted} />
            <StatTile label="Trial band" value={counts.TrialBooked} />
            <StatTile label="Trial keldi" value={counts.TrialAttended} tone="brand" />
            <StatTile label="Yopilgan" value={closedCount} tone={closedCount > 0 ? "danger" : "neutral"} />
            <StatTile label="Konversiya" value={`${conversionRate}%`} tone="brand" hint={`${counts.Converted}/${totalLeads}`} />
          </div>

          <DndContext
            sensors={sensors}
            collisionDetection={columnUnderPointer}
            onDragStart={(e) => setActiveLead((e.active.data.current?.lead as Lead | undefined) ?? null)}
            onDragEnd={handleDragEnd}
            onDragCancel={() => setActiveLead(null)}
          >
            <div className="overflow-x-auto pb-2">
              <div className="grid min-w-[1200px] grid-cols-6 gap-3">
                {COLUMNS.map((col) => (
                  <BoardColumn
                    key={col}
                    columnKey={col}
                    count={counts[col]}
                    hasMore={!!board[col].hasNextPage}
                    loadingMore={board[col].isFetchingNextPage}
                    onMore={() => void board[col].fetchNextPage()}
                  >
                    {(board[col].data?.pages.flatMap((p) => p.items) ?? []).map((lead) => (
                      <LeadCard
                        key={lead.id}
                        lead={lead}
                        branchName={branchNameById.get(lead.branchId)}
                        onOpen={() => router.push(`/leads/${lead.id}`)}
                        onCall={() => setCallTarget(lead)}
                        onTrial={() => setTrialTarget(lead)}
                        onAttendance={(attended) => lead.latestTrial && attendanceMutation.mutate({ trialId: lead.latestTrial.id, attended })}
                        onConvert={() => setConvertTarget({ lead, groupId: lead.latestTrial?.attendanceStatus === "Attended" ? lead.latestTrial.groupId : undefined })}
                        canCall={can("crm.call.log")}
                        canTrial={can("trial.book")}
                        canConvert={can("students.create")}
                        busy={attendanceMutation.isPending}
                      />
                    ))}
                  </BoardColumn>
                ))}
              </div>
            </div>
            <DragOverlay>
              {activeLead && (
                <div className="w-72 rounded-lg border border-brand-300 bg-surface p-3 shadow-lg">
                  <p className="font-medium text-ink-900">{activeLead.fullName}</p>
                  <p className="font-mono text-[12px] text-ink-500">{activeLead.phone}</p>
                </div>
              )}
            </DragOverlay>
          </DndContext>
        </>
      )}

      <Drawer open={showCreate} onClose={() => { setCreateOpen(false); newParam.clear(); }} title="Yangi lid">
        <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); createMutation.mutate(); }}>
          <div>
            <Label htmlFor="lead-name">F.I.O</Label>
            <Input id="lead-name" required value={fullName} onChange={(e) => setFullName(e.target.value)} />
          </div>
          <div>
            <Label htmlFor="lead-phone">Telefon</Label>
            <Input id="lead-phone" required value={phone} onChange={(e) => setPhone(e.target.value)} placeholder="+998 90 000 00 00" />
          </div>
          <div>
            <Label htmlFor="lead-source">Manba</Label>
            <Select id="lead-source" value={source} onChange={(e) => setSource(e.target.value)}>
              <option value="Instagram">Instagram</option>
              <option value="Telegram">Telegram</option>
              <option value="Website">Website</option>
              <option value="Referral">Referral</option>
              <option value="Call">Call</option>
              <option value="Walk-in">Walk-in</option>
            </Select>
          </div>
          <div>
            <Label htmlFor="lead-course">Qiziqqan kurs</Label>
            <Input id="lead-course" required value={interestedCourse} onChange={(e) => setInterestedCourse(e.target.value)} placeholder="IELTS" />
          </div>
          <div>
            <Label htmlFor="lead-branch">Filial</Label>
            <Select id="lead-branch" required value={leadBranchId} onChange={(e) => setLeadBranchId(e.target.value)}>
              <option value="">Tanlang</option>
              {branchesQuery.data?.map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}
            </Select>
          </div>
          <LeadPreferencesFields idPrefix="lead" times={preferredTimes} onTimes={setPreferredTimes} level={currentLevel} onLevel={setCurrentLevel} />
          {createMutation.isError && (
            <Alert>{createMutation.error instanceof ApiError ? createMutation.error.message : "Lid yaratib bo'lmadi."}</Alert>
          )}
          <Button type="submit" loading={createMutation.isPending} className="w-full">Lid yaratish</Button>
        </form>
      </Drawer>

      {(attendanceMutation.isError || moveMutation.isError) && (
        <div className="mt-4">
          <Alert>
            {(attendanceMutation.error ?? moveMutation.error) instanceof ApiError
              ? (attendanceMutation.error ?? moveMutation.error)!.message
              : "Amalni bajarib bo'lmadi."}
          </Alert>
        </div>
      )}

      {callTarget && <LogCallDrawer key={callTarget.id} lead={callTarget} onClose={() => setCallTarget(null)} />}
      {closeTarget && <CloseLeadDrawer key={closeTarget.id} lead={closeTarget} onClose={() => setCloseTarget(null)} />}
      {trialTarget && (
        <TrialDrawer
          key={trialTarget.id}
          lead={trialTarget}
          onClose={() => setTrialTarget(null)}
          onConvert={(groupId) => { setConvertTarget({ lead: trialTarget, groupId }); setTrialTarget(null); }}
        />
      )}
      {convertTarget && (
        <ConvertLeadDrawer key={convertTarget.lead.id} lead={convertTarget.lead} defaultGroupId={convertTarget.groupId} onClose={() => setConvertTarget(null)} />
      )}
    </main>
  );
}

function BoardColumn({
  columnKey, count, children, hasMore, loadingMore, onMore,
}: { columnKey: ColumnKey; count: number; children: React.ReactNode; hasMore: boolean; loadingMore: boolean; onMore: () => void }) {
  const { setNodeRef, isOver } = useDroppable({ id: columnKey });
  return (
    <div
      ref={setNodeRef}
      className={`flex min-h-[200px] flex-col rounded-xl border p-2 transition-colors ${
        isOver ? "border-brand-400 bg-brand-50" : "border-border bg-ink-50/60"
      }`}
    >
      <div className="mb-2 flex items-center justify-between px-1">
        <h3 className="text-[12.5px] font-semibold text-ink-700">{COLUMN_LABEL[columnKey]}</h3>
        <Badge tone="neutral">{count}</Badge>
      </div>
      <div className="space-y-2">{children}</div>
      {hasMore && (
        <Button variant="ghost" size="sm" className="mt-2 w-full" loading={loadingMore} onClick={onMore}>
          Yana ko&apos;rsatish
        </Button>
      )}
    </div>
  );
}

const cardButtonBase = "inline-flex items-center gap-1 rounded-md border px-2 py-1 text-[11.5px] font-medium hover:bg-ink-50 disabled:opacity-50";
const cardButton = `${cardButtonBase} border-border-strong text-ink-700`;
const cardButtonPositive = `${cardButtonBase} border-success-600/40 text-success-600`;

/** One card, one next step: the button shown is the thing an operator does with a lead at this stage. */
function LeadCard({
  lead, branchName, onOpen, onCall, onTrial, onAttendance, onConvert, canCall, canTrial, canConvert, busy,
}: {
  lead: Lead;
  branchName: string | undefined;
  onOpen: () => void;
  onCall: () => void;
  onTrial: () => void;
  onAttendance: (attended: boolean) => void;
  onConvert: () => void;
  canCall: boolean;
  canTrial: boolean;
  canConvert: boolean;
  busy: boolean;
}) {
  // The lead rides along as drag data: the board no longer holds a flat list to look the dragged card up in.
  const { attributes, listeners, setNodeRef, transform, isDragging } = useDraggable({ id: lead.id, data: { lead } });
  const SourceIcon = SOURCE_ICON[lead.source] ?? Globe;
  const style = transform ? { transform: `translate3d(${transform.x}px, ${transform.y}px, 0)` } : undefined;
  const converted = lead.status === "Converted";
  const trial = lead.latestTrial;
  // Buttons must not start a drag or open the profile: the card itself is both draggable and clickable.
  const stop = (fn: () => void) => (e: React.MouseEvent) => { e.stopPropagation(); fn(); };
  const canBookTrial = canTrial && (lead.status === "Lead" || lead.status === "Contacted" || lead.status === "TrialMissed");

  return (
    <div
      ref={setNodeRef}
      style={style}
      {...listeners}
      {...attributes}
      onClick={onOpen}
      className={`cursor-pointer touch-none rounded-lg border border-border bg-surface p-2.5 text-[12.5px] shadow-sm hover:border-brand-300 ${isDragging ? "opacity-40" : ""}`}
    >
      <div className="flex items-start justify-between gap-2">
        <p className="font-medium text-ink-900">{lead.fullName}</p>
        {lead.priority === "High" && <Badge tone="danger" className="shrink-0">Muhim</Badge>}
      </div>
      <p className="font-mono text-[11.5px] text-ink-500">{lead.phone}</p>
      <div className="mt-1.5 flex items-center gap-1.5 text-ink-500">
        <SourceIcon className="size-3 shrink-0" aria-hidden />
        <span className="truncate">{lead.interestedCourse || lead.source}</span>
      </div>
      {branchName && <p className="mt-0.5 truncate text-[11px] text-ink-400">{branchName}</p>}

      {trial && !converted && (
        <p className="mt-1.5 flex items-start gap-1.5 rounded-md bg-ink-50 px-2 py-1.5 text-[11.5px] text-ink-600">
          <CalendarCheck className="mt-0.5 size-3 shrink-0" aria-hidden />
          <span>
            <span className="font-medium text-ink-800">{trial.groupName}</span>
            <br />
            {formatDayMonth(trial.scheduledAt)}, {formatTime(trial.scheduledAt)}
            {trial.attendanceStatus === "Attended" && <span className="text-success-600"> · keldi</span>}
            {trial.attendanceStatus === "Missed" && <span className="text-warning-600"> · kelmadi</span>}
          </span>
        </p>
      )}

      {!converted && (
        <div className="mt-2 flex flex-wrap gap-1.5">
          {lead.status === "TrialBooked" && trial?.attendanceStatus === "Scheduled" && canTrial && (
            <>
              <button type="button" disabled={busy} onClick={stop(() => onAttendance(true))} className={cardButtonPositive}>
                <CheckCircle2 className="size-3" aria-hidden />Keldi
              </button>
              <button type="button" disabled={busy} onClick={stop(() => onAttendance(false))} className={cardButton}>Kelmadi</button>
            </>
          )}
          {lead.status === "TrialAttended" && canConvert && (
            <button type="button" onClick={stop(onConvert)} className="inline-flex items-center gap-1 rounded-md bg-brand-solid px-2.5 py-1 text-[11.5px] font-semibold text-white hover:opacity-90">
              <UserCheck className="size-3" aria-hidden />Studentga aylantirish
            </button>
          )}
          {canBookTrial && (
            <button type="button" onClick={stop(onTrial)} className={cardButton}>
              <CalendarCheck className="size-3" aria-hidden />Trialga yozish
            </button>
          )}
          {canCall && (
            <button type="button" onClick={stop(onCall)} className={cardButton}>
              <PhoneCall className="size-3" aria-hidden />Qo&apos;ng&apos;iroq
            </button>
          )}
        </div>
      )}

      {converted && lead.convertedStudentId && (
        <Link
          href={`/students/${lead.convertedStudentId}`}
          onClick={(e) => e.stopPropagation()}
          className="mt-2 inline-flex items-center gap-1 text-[11.5px] font-medium text-brand-700 hover:underline"
        >
          <UserCheck className="size-3" aria-hidden />Student profili
        </Link>
      )}
    </div>
  );
}
