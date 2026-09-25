"use client";

import Link from "next/link";
import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Activity, CheckCircle2, LifeBuoy, UserCheck, UserRound } from "lucide-react";
import {
  assignSupport, dismissSupport, listSupportCandidates, listSupportCases, proposeSupport, resolveSupport,
  SIGNAL_LABEL, type SupportCase, type SupportCaseStatus,
} from "@/lib/api/support";
import { ApiError } from "@/lib/api-client";
import { useAuthSession } from "@/lib/use-auth-session";
import { Alert } from "@/components/ui/Alert";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { Drawer } from "@/components/ui/Drawer";
import { EmptyState } from "@/components/ui/EmptyState";
import { Label, Select, Textarea } from "@/components/ui/Field";
import { Skeleton } from "@/components/ui/Skeleton";
import { cn } from "@/lib/cn";
import { formatDateTime } from "@/lib/format-date";
import { highlightElementId } from "@/lib/use-highlight-target";

const STATUS_LABEL: Record<SupportCaseStatus, string> = {
  Recommended: "Ko'rib chiqish kerak",
  Assigned: "Jarayonda",
  Dismissed: "Rad etilgan",
  Resolved: "Yopilgan",
};
const STATUS_TONE: Record<SupportCaseStatus, "warning" | "brand" | "neutral" | "success"> = {
  Recommended: "warning",
  Assigned: "brand",
  Dismissed: "neutral",
  Resolved: "success",
};

/** UI gates mirroring the permission matrix §3.9 — the API enforces the same rules on its own. */
export const canViewSupport = (role?: string) =>
  role === "SUPER_ADMIN" || role === "ADMINISTRATOR" || role === "TEACHER" || role === "SUPPORT_TEACHER";
export const canProposeSupport = (role?: string) =>
  role === "SUPER_ADMIN" || role === "ADMINISTRATOR" || role === "TEACHER";
const canReviewSupport = (role?: string) => role === "SUPER_ADMIN" || role === "ADMINISTRATOR";

const errorText = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

function useInvalidateSupport(studentId: string) {
  const client = useQueryClient();
  return () =>
    Promise.all([
      client.invalidateQueries({ queryKey: ["support-cases"] }),
      client.invalidateQueries({ queryKey: ["students", studentId, "timeline"] }),
    ]);
}

export function SupportCaseList({
  cases,
  showStudent = true,
  emptyTitle,
  emptyDescription,
  highlightId,
}: {
  cases: SupportCase[];
  showStudent?: boolean;
  emptyTitle: string;
  emptyDescription?: string;
  /** Universal Search's `?highlight=<id>` (docs/00_TZ.md §27.3) — rings and scrolls to this one case. */
  highlightId?: string | null;
}) {
  const { user } = useAuthSession();
  const [assignTarget, setAssignTarget] = useState<SupportCase | null>(null);
  const [closeTarget, setCloseTarget] = useState<{ supportCase: SupportCase; mode: "dismiss" | "resolve" } | null>(null);

  if (cases.length === 0) {
    return (
      <div className="rounded-xl border border-border bg-surface">
        <EmptyState icon={LifeBuoy} title={emptyTitle} description={emptyDescription} />
      </div>
    );
  }

  return (
    <>
      <div className="divide-y divide-border overflow-hidden rounded-xl border border-border bg-surface">
        {cases.map((supportCase) => (
          <SupportCaseRow
            key={supportCase.id}
            supportCase={supportCase}
            showStudent={showStudent}
            role={user?.role}
            userId={user?.id ?? null}
            highlighted={highlightId === supportCase.id}
            onAssign={() => setAssignTarget(supportCase)}
            onClose={(mode) => setCloseTarget({ supportCase, mode })}
          />
        ))}
      </div>
      {assignTarget && <AssignDrawer key={assignTarget.id} supportCase={assignTarget} onClose={() => setAssignTarget(null)} />}
      {closeTarget && (
        <CloseDrawer
          key={`${closeTarget.supportCase.id}-${closeTarget.mode}`}
          supportCase={closeTarget.supportCase}
          mode={closeTarget.mode}
          onClose={() => setCloseTarget(null)}
        />
      )}
    </>
  );
}

function SupportCaseRow({
  supportCase: c,
  showStudent,
  role,
  userId,
  highlighted,
  onAssign,
  onClose,
}: {
  supportCase: SupportCase;
  showStudent: boolean;
  role?: string;
  userId: string | null;
  highlighted?: boolean;
  onAssign: () => void;
  onClose: (mode: "dismiss" | "resolve") => void;
}) {
  const reviewer = canReviewSupport(role);
  const canResolve = c.status === "Assigned" && (reviewer || c.supportTeacherId === userId);
  const SourceIcon = c.source === "System" ? Activity : UserRound;
  const signal = SIGNAL_LABEL[c.signalCode] ?? c.signalCode;

  return (
    <div
      id={highlightElementId(c.id)}
      className={cn("flex flex-col gap-3 px-5 py-4 sm:flex-row sm:items-start", highlighted && "bg-brand-50")}
    >
      <span
        className={cn(
          "flex size-9 shrink-0 items-center justify-center rounded-lg",
          c.status === "Recommended" && "bg-warning-50 text-warning-600",
          c.status === "Assigned" && "bg-brand-100 text-brand-700",
          (c.status === "Dismissed" || c.status === "Resolved") && "bg-ink-100 text-ink-500",
        )}
      >
        <SourceIcon className="size-4" aria-hidden />
      </span>

      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-2">
          {showStudent ? (
            <Link href={`/students/${c.studentId}`} className="text-[13.5px] font-medium text-ink-900 hover:text-brand-700">
              {c.studentName}
            </Link>
          ) : (
            <span className="text-[13.5px] font-medium text-ink-900">{signal}</span>
          )}
          <Badge tone={STATUS_TONE[c.status]} dot>{STATUS_LABEL[c.status]}</Badge>
          {showStudent && <Badge tone="neutral">{signal}</Badge>}
        </div>
        <p className="mt-1 text-[13px] text-ink-700">{c.reason}</p>
        <p className="mt-1 text-[12.5px] text-ink-500">
          {c.groupName} · {c.source === "System" ? "Tizim signali" : `Tavsiya qildi: ${c.proposedByName ?? "xodim"}`} · {formatDateTime(c.createdAt)}
        </p>
        {c.supportTeacherName && (
          <p className="mt-1 text-[12.5px] text-ink-600">
            Yordamchi o&apos;qituvchi: <span className="font-medium text-ink-800">{c.supportTeacherName}</span>
          </p>
        )}
        {c.resolutionNote && <p className="mt-1 text-[12.5px] text-ink-500">Izoh: {c.resolutionNote}</p>}
      </div>

      <div className="flex shrink-0 flex-wrap gap-2">
        {reviewer && c.status === "Recommended" && (
          <>
            <Button size="sm" onClick={onAssign}>
              <UserCheck className="size-3.5" aria-hidden />
              Biriktirish
            </Button>
            <Button size="sm" variant="secondary" onClick={() => onClose("dismiss")}>
              Rad etish
            </Button>
          </>
        )}
        {canResolve && (
          <Button size="sm" variant="secondary" onClick={() => onClose("resolve")}>
            <CheckCircle2 className="size-3.5" aria-hidden />
            Yakunlash
          </Button>
        )}
      </div>
    </div>
  );
}

function AssignDrawer({ supportCase, onClose }: { supportCase: SupportCase; onClose: () => void }) {
  const invalidate = useInvalidateSupport(supportCase.studentId);
  const candidates = useQuery({
    queryKey: ["support-cases", supportCase.id, "candidates"],
    queryFn: () => listSupportCandidates(supportCase.id),
  });
  const [teacherId, setTeacherId] = useState("");
  const mutation = useMutation({
    mutationFn: () => assignSupport(supportCase.id, teacherId),
    onSuccess: async () => {
      await invalidate();
      onClose();
    },
  });

  return (
    <Drawer open onClose={onClose} title="Yordamchi o'qituvchi biriktirish" description={`${supportCase.studentName} · ${supportCase.groupName}`}>
      <div className="space-y-4">
        <p className="rounded-lg bg-ink-50 px-3 py-2.5 text-[13px] text-ink-700">{supportCase.reason}</p>
        {candidates.isLoading && <Skeleton className="h-24" />}
        {candidates.isError && <Alert>{errorText(candidates.error, "Ro'yxatni yuklab bo'lmadi.")}</Alert>}
        {candidates.data?.length === 0 && (
          <EmptyState
            icon={UserCheck}
            title="Guruhda yordamchi o'qituvchi yo'q"
            description="Support guruhga biriktirilgan xodim orqali beriladi. Avval Guruhlar sahifasida bu guruhga yordamchi o'qituvchi biriktiring."
          />
        )}
        {!!candidates.data?.length && (
          <form
            className="space-y-4"
            onSubmit={(e) => {
              e.preventDefault();
              mutation.mutate();
            }}
          >
            <div>
              <Label htmlFor="support-teacher">Yordamchi o&apos;qituvchi</Label>
              <Select id="support-teacher" required value={teacherId} onChange={(e) => setTeacherId(e.target.value)}>
                <option value="">Tanlang</option>
                {candidates.data.map((candidate) => (
                  <option key={candidate.userId} value={candidate.userId}>{candidate.fullName}</option>
                ))}
              </Select>
            </div>
            {mutation.isError && <Alert>{errorText(mutation.error, "Biriktirib bo'lmadi.")}</Alert>}
            <Button type="submit" loading={mutation.isPending} className="w-full">
              Tasdiqlash va biriktirish
            </Button>
          </form>
        )}
      </div>
    </Drawer>
  );
}

function CloseDrawer({ supportCase, mode, onClose }: { supportCase: SupportCase; mode: "dismiss" | "resolve"; onClose: () => void }) {
  const invalidate = useInvalidateSupport(supportCase.studentId);
  const [note, setNote] = useState("");
  const mutation = useMutation({
    mutationFn: () => (mode === "dismiss" ? dismissSupport(supportCase.id, note || undefined) : resolveSupport(supportCase.id, note || undefined)),
    onSuccess: async () => {
      await invalidate();
      onClose();
    },
  });
  const copy =
    mode === "dismiss"
      ? { title: "Tavsiyani rad etish", label: "Nega support kerak emas?", action: "Rad etish" }
      : { title: "Support'ni yakunlash", label: "Natija: nima qilindi, student holati qanday?", action: "Yakunlash" };

  return (
    <Drawer open onClose={onClose} title={copy.title} description={`${supportCase.studentName} · ${supportCase.groupName}`}>
      <form
        className="space-y-4"
        onSubmit={(e) => {
          e.preventDefault();
          mutation.mutate();
        }}
      >
        <div>
          <Label htmlFor="support-note">{copy.label}</Label>
          <Textarea id="support-note" rows={4} maxLength={1000} value={note} onChange={(e) => setNote(e.target.value)} />
          <p className="mt-1 text-[12px] text-ink-500">Izoh support yozuvida saqlanadi.</p>
        </div>
        {mutation.isError && <Alert>{errorText(mutation.error, "Saqlab bo'lmadi.")}</Alert>}
        <Button type="submit" loading={mutation.isPending} className="w-full" variant={mode === "dismiss" ? "secondary" : "primary"}>
          {copy.action}
        </Button>
      </form>
    </Drawer>
  );
}

function ProposeSupportDrawer({
  studentId,
  groups,
  onClose,
}: {
  studentId: string;
  groups: { groupId: string; groupName: string }[];
  onClose: () => void;
}) {
  const invalidate = useInvalidateSupport(studentId);
  const [groupId, setGroupId] = useState(groups.length === 1 ? groups[0].groupId : "");
  const [reason, setReason] = useState("");
  const mutation = useMutation({
    mutationFn: () => proposeSupport({ studentId, groupId, reason: reason.trim() }),
    onSuccess: async () => {
      await invalidate();
      onClose();
    },
  });

  return (
    <Drawer open onClose={onClose} title="Support tavsiya qilish" description="Tavsiya administratorga ko'rib chiqish uchun yuboriladi.">
      <form
        className="space-y-4"
        onSubmit={(e) => {
          e.preventDefault();
          mutation.mutate();
        }}
      >
        <div>
          <Label htmlFor="propose-group">Guruh</Label>
          <Select id="propose-group" required value={groupId} onChange={(e) => setGroupId(e.target.value)}>
            <option value="">Tanlang</option>
            {groups.map((g) => (
              <option key={g.groupId} value={g.groupId}>{g.groupName}</option>
            ))}
          </Select>
        </div>
        <div>
          <Label htmlFor="propose-reason">Sabab</Label>
          <Textarea
            id="propose-reason"
            required
            rows={4}
            maxLength={500}
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            placeholder="Masalan: Writing Task 2 da grammatik xatolar ko'p, speaking'da ikkilanib qoladi."
          />
        </div>
        {mutation.isError && <Alert>{errorText(mutation.error, "Tavsiyani yuborib bo'lmadi.")}</Alert>}
        <Button type="submit" loading={mutation.isPending} className="w-full" disabled={!reason.trim()}>
          Tavsiya qilish
        </Button>
      </form>
    </Drawer>
  );
}

/** Student 360° "Support" tab: this student's cases plus, for teachers/admins, a manual proposal. */
export function StudentSupportTab({ studentId, groups }: { studentId: string; groups: { groupId: string; groupName: string }[] }) {
  const { user } = useAuthSession();
  const [proposing, setProposing] = useState(false);
  const query = useQuery({ queryKey: ["support-cases", { studentId }], queryFn: () => listSupportCases({ studentId, size: 50 }) });

  return (
    <div className="space-y-4">
      {canProposeSupport(user?.role) && groups.length > 0 && (
        <div className="flex justify-end">
          <Button size="sm" onClick={() => setProposing(true)}>
            <LifeBuoy className="size-3.5" aria-hidden />
            Support tavsiya qilish
          </Button>
        </div>
      )}
      {query.isLoading && <Skeleton className="h-32" />}
      {query.isError && <Alert>{errorText(query.error, "Support ma'lumotlarini yuklab bo'lmadi.")}</Alert>}
      {query.data && (
        <SupportCaseList
          cases={query.data.items}
          showStudent={false}
          emptyTitle="Support tavsiyalari yo'q"
          emptyDescription="Ketma-ket yo'qlik yoki topshirilmagan homework aniqlansa, tavsiya shu yerda paydo bo'ladi."
        />
      )}
      {proposing && <ProposeSupportDrawer studentId={studentId} groups={groups} onClose={() => setProposing(false)} />}
    </div>
  );
}
