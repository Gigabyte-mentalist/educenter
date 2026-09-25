"use client";

import Link from "next/link";
import { useState } from "react";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { PhoneCall, Clock, CheckCircle2, AlertOctagon, ListChecks, Wallet, UserX, Sparkles } from "lucide-react";
import { completeCrmTask, listCrmTasks, logCall, CALL_OUTCOME_LABEL, type CrmTask, type CrmTaskType, type CallOutcome } from "@/lib/api/crm";
import { ApiError } from "@/lib/api-client";
import { PageHeader } from "@/components/ui/PageHeader";
import { Button } from "@/components/ui/Button";
import { Badge } from "@/components/ui/Badge";
import { StatTile } from "@/components/ui/StatTile";
import { Skeleton } from "@/components/ui/Skeleton";
import { EmptyState } from "@/components/ui/EmptyState";
import { Alert } from "@/components/ui/Alert";
import { Drawer } from "@/components/ui/Drawer";
import { Pagination } from "@/components/ui/Pagination";
import { Input, Label, Select } from "@/components/ui/Field";
import { cn } from "@/lib/cn";
import { formatDateTime } from "@/lib/format-date";

const PAGE_SIZE = 20;

type Tone = "brand" | "warning" | "danger";

const TYPE_CONFIG: Record<CrmTaskType, { label: string; icon: typeof PhoneCall; tone: Tone }> = {
  CallBack: { label: "Qayta qo'ng'iroq", icon: PhoneCall, tone: "brand" },
  FollowUp: { label: "Follow-up", icon: ListChecks, tone: "brand" },
  Debt: { label: "Qarzdorlik", icon: Wallet, tone: "warning" },
  Absence: { label: "Ketma-ket yo'qlik", icon: UserX, tone: "danger" },
};

/** Tasks created by an automation rule get a more specific label than their generic type. */
const REASON_CONFIG: Record<string, { label: string; icon: typeof PhoneCall; tone: Tone }> = {
  TRIAL_FOLLOW_UP: { label: "Trial follow-up", icon: Sparkles, tone: "warning" },
  CONSECUTIVE_ABSENCES: { label: "Ketma-ket yo'qlik", icon: UserX, tone: "danger" },
  DEBT_OVERDUE: { label: "Qarzdorlik", icon: Wallet, tone: "warning" },
};

function isOverdue(task: CrmTask): boolean {
  return new Date(task.dueAt).getTime() < Date.now();
}

export default function CrmTasksPage() {
  const queryClient = useQueryClient();
  // What is late and what is still to come are two lists with their own counts and pagers — an operator with a backlog
  // should not have to scroll past two hundred late calls to see today's.
  const [overduePage, setOverduePage] = useState(1);
  const [upcomingPage, setUpcomingPage] = useState(1);
  const overdueQuery = useQuery({
    queryKey: ["crm-tasks", "overdue", overduePage],
    queryFn: () => listCrmTasks({ bucket: "overdue", page: overduePage, size: PAGE_SIZE }),
    placeholderData: keepPreviousData,
  });
  const upcomingQuery = useQuery({
    queryKey: ["crm-tasks", "upcoming", upcomingPage],
    queryFn: () => listCrmTasks({ bucket: "upcoming", page: upcomingPage, size: PAGE_SIZE }),
    placeholderData: keepPreviousData,
  });
  const loadError = overdueQuery.error ?? upcomingQuery.error;

  const [callTarget, setCallTarget] = useState<CrmTask | null>(null);
  const [outcome, setOutcome] = useState<CallOutcome>("Answered");
  const [callNotes, setCallNotes] = useState("");

  const completeMutation = useMutation({
    mutationFn: (taskId: string) => completeCrmTask(taskId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["crm-tasks"] }),
  });

  /** Logs the call the task exists to prompt, then closes the task — the two used to be two separate
   * trips (open /leads, find the lead, then come back and mark the task done). */
  const callMutation = useMutation({
    mutationFn: async () => {
      await logCall(callTarget!.leadId!, outcome, callNotes || undefined);
      await completeCrmTask(callTarget!.id);
    },
    onSuccess: () => {
      setCallNotes("");
      setOutcome("Answered");
      setCallTarget(null);
      queryClient.invalidateQueries({ queryKey: ["crm-tasks"] });
      queryClient.invalidateQueries({ queryKey: ["leads"] });
    },
  });

  const overdueTasks = overdueQuery.data?.items ?? [];
  const upcomingTasks = upcomingQuery.data?.items ?? [];
  const overdueTotal = overdueQuery.data?.total ?? 0;
  const upcomingTotal = upcomingQuery.data?.total ?? 0;
  const loaded = overdueQuery.data !== undefined && upcomingQuery.data !== undefined;

  return (
    <main className="p-4 sm:p-6 lg:p-8">
      <PageHeader
        title="Bugungi vazifalar"
        description="Qo'ng'iroq navbati: qayta qo'ng'iroqlar, trial follow-up va ketma-ket yo'qlik vazifalari shu yerda avtomatik paydo bo'ladi."
      />

      {loadError && (
        <Alert>{loadError instanceof ApiError ? loadError.message : "Yuklab bo'lmadi."}</Alert>
      )}
      {completeMutation.isError && (
        <Alert>{completeMutation.error instanceof ApiError ? completeMutation.error.message : "Vazifani yopib bo'lmadi."}</Alert>
      )}

      <div className="mb-6 grid grid-cols-2 gap-4 sm:grid-cols-3">
        <StatTile label="Muddati o'tgan" value={overdueTotal} icon={AlertOctagon} tone="danger" />
        <StatTile label="Navbatda" value={upcomingTotal} icon={Clock} tone="brand" />
        <StatTile label="Jami ochiq" value={overdueTotal + upcomingTotal} icon={ListChecks} />
      </div>

      {!loaded && !loadError && (
        <div className="space-y-2">
          {Array.from({ length: 4 }).map((_, i) => <Skeleton key={i} className="h-16" />)}
        </div>
      )}

      {loaded && overdueTotal + upcomingTotal === 0 && (
        <div className="rounded-xl border border-border bg-surface">
          <EmptyState icon={CheckCircle2} title="Navbat bo'sh" description="Hozircha bajarilishi kerak bo'lgan vazifa yo'q." />
        </div>
      )}

      {overdueTotal > 0 && overdueQuery.data && (
        <TaskGroup
          title={`Muddati o'tgan (${overdueTotal})`}
          tasks={overdueTasks}
          onComplete={(id) => completeMutation.mutate(id)}
          onCall={setCallTarget}
          pendingId={completeMutation.isPending ? completeMutation.variables : undefined}
          paging={{ ...overdueQuery.data, onChange: setOverduePage, stale: overdueQuery.isPlaceholderData }}
        />
      )}
      {upcomingTotal > 0 && upcomingQuery.data && (
        <TaskGroup
          title={`Navbatdagi vazifalar (${upcomingTotal})`}
          tasks={upcomingTasks}
          onComplete={(id) => completeMutation.mutate(id)}
          onCall={setCallTarget}
          pendingId={completeMutation.isPending ? completeMutation.variables : undefined}
          paging={{ ...upcomingQuery.data, onChange: setUpcomingPage, stale: upcomingQuery.isPlaceholderData }}
        />
      )}

      <Drawer
        open={callTarget !== null}
        onClose={() => setCallTarget(null)}
        title="Qo'ng'iroq natijasi"
        description={callTarget ? `${callTarget.subjectName ?? "Noma'lum"} — ${callTarget.subjectPhone ?? ""}` : undefined}
      >
        <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); callMutation.mutate(); }}>
          <div>
            <Label htmlFor="task-call-outcome">Natija</Label>
            <Select id="task-call-outcome" value={outcome} onChange={(e) => setOutcome(e.target.value as CallOutcome)}>
              {(Object.keys(CALL_OUTCOME_LABEL) as CallOutcome[]).map((o) => (
                <option key={o} value={o}>{CALL_OUTCOME_LABEL[o]}</option>
              ))}
            </Select>
          </div>
          <div>
            <Label htmlFor="task-call-notes">Izoh (ixtiyoriy)</Label>
            <Input id="task-call-notes" value={callNotes} onChange={(e) => setCallNotes(e.target.value)} />
          </div>
          <p className="text-[12.5px] text-ink-500">
            Natija saqlangach, bu vazifa avtomatik yopiladi. &quot;Keyinroq qo&apos;ng&apos;iroq qilish&quot;
            tanlansa, ertaga uchun yangi vazifa yaratiladi.
          </p>
          {callMutation.isError && (
            <Alert>{callMutation.error instanceof ApiError ? callMutation.error.message : "Saqlab bo'lmadi."}</Alert>
          )}
          <Button type="submit" loading={callMutation.isPending} className="w-full">Saqlash</Button>
        </form>
      </Drawer>
    </main>
  );
}

function TaskGroup({
  title,
  tasks,
  onComplete,
  onCall,
  pendingId,
  paging,
}: {
  title: string;
  tasks: CrmTask[];
  onComplete: (id: string) => void;
  onCall: (task: CrmTask) => void;
  pendingId?: string;
  paging: { pageNumber: number; pageCount: number; total: number; pageSize: number; onChange: (page: number) => void; stale: boolean };
}) {
  return (
    <div className="mb-6">
      <h2 className="mb-2.5 text-[13px] font-semibold uppercase tracking-wide text-ink-500">{title}</h2>
      <div className={cn("divide-y divide-border overflow-hidden rounded-xl border border-border bg-surface transition-opacity", paging.stale && "opacity-60")}>
        {tasks.map((task) => {
          const config = (task.reasonCode && REASON_CONFIG[task.reasonCode]) || TYPE_CONFIG[task.type];
          const Icon = config.icon;
          const overdue = isOverdue(task);
          const name = task.subjectName ?? "Noma'lum";

          return (
            <div key={task.id} className="flex items-center gap-4 px-5 py-3.5">
              <span
                className={cn(
                  "flex size-9 shrink-0 items-center justify-center rounded-lg",
                  config.tone === "brand" && "bg-brand-100 text-brand-700",
                  config.tone === "warning" && "bg-warning-50 text-warning-600",
                  config.tone === "danger" && "bg-danger-50 text-danger-600",
                )}
              >
                <Icon className="size-4" aria-hidden />
              </span>

              <div className="min-w-0 flex-1">
                <div className="flex flex-wrap items-center gap-2">
                  {task.studentId ? (
                    <Link href={`/students/${task.studentId}`} className="truncate text-[13.5px] font-medium text-ink-900 hover:text-brand-700">
                      {name}
                    </Link>
                  ) : (
                    <p className="truncate text-[13.5px] font-medium text-ink-900">{name}</p>
                  )}
                  <Badge tone={config.tone}>{config.label}</Badge>
                  {task.studentId && <Badge tone="neutral">Student</Badge>}
                </div>
                {task.note && <p className="mt-0.5 text-[12.5px] text-ink-700">{task.note}</p>}
                <p className="mt-0.5 text-[12.5px] text-ink-500">
                  {task.subjectPhone && <span className="font-mono">{task.subjectPhone}</span>}
                  {task.subjectPhone && " · "}
                  <span className={overdue ? "text-danger-600" : undefined}>
                    Muddat: {formatDateTime(task.dueAt)}
                  </span>
                </p>
              </div>

              <div className="flex shrink-0 gap-1.5">
                {task.leadId && (
                  <Button variant="ghost" size="sm" onClick={() => onCall(task)}>
                    <PhoneCall className="size-3.5" aria-hidden />
                    Qo&apos;ng&apos;iroq
                  </Button>
                )}
                <Button variant="secondary" size="sm" loading={pendingId === task.id} onClick={() => onComplete(task.id)}>
                  <CheckCircle2 className="size-3.5" aria-hidden />
                  Bajarildi
                </Button>
              </div>
            </div>
          );
        })}
        <Pagination page={paging.pageNumber} pageCount={paging.pageCount} total={paging.total} pageSize={paging.pageSize} onChange={paging.onChange} className="border-t-0" />
      </div>
    </div>
  );
}
