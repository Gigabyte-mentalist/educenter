"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CheckCircle2, Play, UserCheck } from "lucide-react";
import {
  assignComplaint, COMPLAINT_STATUS_LABEL, getComplaint, listComplaintCandidates, rateComplaint, resolveComplaint, setComplaintStatus,
  type Complaint,
} from "@/lib/api/complaints";
import { ApiError } from "@/lib/api-client";
import { formatDateTime } from "@/lib/format-date";
import { cn } from "@/lib/cn";
import { Alert } from "@/components/ui/Alert";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { Drawer } from "@/components/ui/Drawer";
import { Label, Select, Textarea } from "@/components/ui/Field";
import { Skeleton } from "@/components/ui/Skeleton";
import { COMPLAINT_STATUS_TONE } from "./tone";

const errorText = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

/** Every screen that shows complaints — the list, the tabs' counts, the one open here — is refreshed after any change. */
function useRefreshComplaints() {
  const client = useQueryClient();
  return () => client.invalidateQueries({ queryKey: ["complaints"] });
}

/**
 * One complaint, in full, with what the caller may do to it. The buttons come from the flags the server sent (`canManage`, `canAct`, `canRate`)
 * — the same rules it enforces — so a person is never offered a step that ends in a refusal.
 */
export function ComplaintDrawer({ complaintId, onClose }: { complaintId: string; onClose: () => void }) {
  const query = useQuery({ queryKey: ["complaints", "one", complaintId], queryFn: () => getComplaint(complaintId), retry: false });
  const c = query.data;

  return (
    <Drawer open onClose={onClose} title={c?.subject ?? "Shikoyat"} description={c ? `${c.branchName} · ${formatDateTime(c.createdAt)}` : undefined}>
      {query.isPending && <Skeleton className="h-40" />}
      {query.isError && <Alert>{errorText(query.error, "Shikoyatni yuklab bo'lmadi.")}</Alert>}
      {c && <ComplaintBody key={`${c.id}-${c.status}`} c={c} />}
    </Drawer>
  );
}

function ComplaintBody({ c }: { c: Complaint }) {
  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-center gap-2">
        <Badge tone={COMPLAINT_STATUS_TONE[c.status]} dot>{COMPLAINT_STATUS_LABEL[c.status]}</Badge>
        {c.canRate && <Badge tone="warning">Baho kutilmoqda</Badge>}
      </div>

      <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1.5 text-[13px]">
        <dt className="text-ink-500">Yozgan:</dt>
        <dd className="text-ink-800">{c.isMine ? "Siz" : c.authorName}</dd>
        {c.studentName && (
          <>
            <dt className="text-ink-500">Student:</dt>
            <dd className="text-ink-800">{c.studentName}</dd>
          </>
        )}
        <dt className="text-ink-500">Ijrochi:</dt>
        <dd className="text-ink-800">{c.assignedToName ?? "Hali biriktirilmagan"}</dd>
      </dl>

      <p className="whitespace-pre-wrap rounded-lg bg-ink-50 px-3.5 py-3 text-[13.5px] leading-relaxed text-ink-800">{c.body}</p>

      {c.resolution && (
        <section>
          <h3 className="mb-1.5 text-[13px] font-semibold text-ink-900">Yechim{c.resolvedAt ? ` · ${formatDateTime(c.resolvedAt)}` : ""}</h3>
          <p className="whitespace-pre-wrap rounded-lg bg-success-50 px-3.5 py-3 text-[13.5px] leading-relaxed text-ink-800">{c.resolution}</p>
        </section>
      )}

      {c.satisfactionScore !== null && (
        <section>
          <h3 className="mb-1.5 text-[13px] font-semibold text-ink-900">Muallifning bahosi: {c.satisfactionScore} / 10</h3>
          {c.feedbackComment && <p className="whitespace-pre-wrap text-[13.5px] text-ink-700">{c.feedbackComment}</p>}
        </section>
      )}

      {c.canRate && <RateSection c={c} />}
      {c.canManage && (c.status === "New" || c.status === "Assigned" || c.status === "InProgress") && <AssignSection c={c} />}
      {c.canAct && c.status === "Assigned" && <StartSection c={c} />}
      {c.canAct && (c.status === "Assigned" || c.status === "InProgress") && <ResolveSection c={c} />}
      {c.canManage && c.status === "Resolved" && <CloseSection c={c} />}
    </div>
  );
}

function AssignSection({ c }: { c: Complaint }) {
  const refresh = useRefreshComplaints();
  const candidates = useQuery({ queryKey: ["complaints", c.id, "candidates"], queryFn: () => listComplaintCandidates(c.id) });
  const [userId, setUserId] = useState(c.assignedToUserId ?? "");
  const assign = useMutation({ mutationFn: () => assignComplaint(c.id, userId), onSuccess: refresh });

  return (
    <section className="space-y-3 border-t border-border pt-4">
      <h3 className="text-[13px] font-semibold text-ink-900">{c.assignedToUserId ? "Boshqa xodimga biriktirish" : "Xodimga biriktirish"}</h3>
      {candidates.isError && <Alert>{errorText(candidates.error, "Xodimlar ro'yxatini yuklab bo'lmadi.")}</Alert>}
      <div>
        <Label htmlFor="complaint-assignee">Ijrochi</Label>
        <Select id="complaint-assignee" value={userId} onChange={(e) => setUserId(e.target.value)}>
          <option value="">Tanlang</option>
          {candidates.data?.map((u) => <option key={u.userId} value={u.userId}>{u.fullName}</option>)}
        </Select>
      </div>
      {assign.isError && <Alert>{errorText(assign.error, "Biriktirib bo'lmadi.")}</Alert>}
      <Button size="sm" loading={assign.isPending} disabled={userId === "" || userId === c.assignedToUserId} onClick={() => assign.mutate()}>
        <UserCheck className="size-3.5" aria-hidden />
        Biriktirish
      </Button>
    </section>
  );
}

function StartSection({ c }: { c: Complaint }) {
  const refresh = useRefreshComplaints();
  const start = useMutation({ mutationFn: () => setComplaintStatus(c.id, "InProgress"), onSuccess: refresh });

  return (
    <section className="space-y-3 border-t border-border pt-4">
      {start.isError && <Alert>{errorText(start.error, "Holatni o'zgartirib bo'lmadi.")}</Alert>}
      <Button size="sm" variant="secondary" loading={start.isPending} onClick={() => start.mutate()}>
        <Play className="size-3.5" aria-hidden />
        Jarayonga olish
      </Button>
    </section>
  );
}

function ResolveSection({ c }: { c: Complaint }) {
  const refresh = useRefreshComplaints();
  const [resolution, setResolution] = useState("");
  const resolve = useMutation({ mutationFn: () => resolveComplaint(c.id, resolution.trim()), onSuccess: refresh });

  return (
    <section className="space-y-3 border-t border-border pt-4">
      <h3 className="text-[13px] font-semibold text-ink-900">Yechim</h3>
      <div>
        <Label htmlFor="complaint-resolution">Nima qilindi?</Label>
        <Textarea id="complaint-resolution" rows={4} maxLength={2000} value={resolution} onChange={(e) => setResolution(e.target.value)} />
        <p className="mt-1 text-[12px] text-ink-500">Muallif shu matnni ko&apos;radi va natijani baholaydi.</p>
      </div>
      {resolve.isError && <Alert>{errorText(resolve.error, "Yakunlab bo'lmadi.")}</Alert>}
      <Button size="sm" loading={resolve.isPending} disabled={resolution.trim() === ""} onClick={() => resolve.mutate()}>
        <CheckCircle2 className="size-3.5" aria-hidden />
        Yechim bilan yakunlash
      </Button>
    </section>
  );
}

function CloseSection({ c }: { c: Complaint }) {
  const refresh = useRefreshComplaints();
  const close = useMutation({ mutationFn: () => setComplaintStatus(c.id, "Closed"), onSuccess: refresh });

  return (
    <section className="space-y-3 border-t border-border pt-4">
      <p className="text-[12.5px] text-ink-500">Muallif hali baho qo&apos;ymagan. Kutmasdan yopsangiz, keyin baho qo&apos;yib bo&apos;lmaydi.</p>
      {close.isError && <Alert>{errorText(close.error, "Yopib bo'lmadi.")}</Alert>}
      <Button size="sm" variant="secondary" loading={close.isPending} onClick={() => close.mutate()}>Baho kutmasdan yopish</Button>
    </section>
  );
}

const SCORES = Array.from({ length: 11 }, (_, i) => i);

function RateSection({ c }: { c: Complaint }) {
  const refresh = useRefreshComplaints();
  const [score, setScore] = useState<number | null>(null);
  const [comment, setComment] = useState("");
  const rate = useMutation({ mutationFn: () => rateComplaint(c.id, score!, comment.trim() || undefined), onSuccess: refresh });

  return (
    <section className="space-y-3 border-t border-border pt-4">
      <h3 className="text-[13px] font-semibold text-ink-900">Yechimdan qanchalik mamnunsiz?</h3>
      <div role="radiogroup" aria-label="Baho, 0 dan 10 gacha" className="flex flex-wrap gap-1.5">
        {SCORES.map((n) => (
          <button
            key={n}
            type="button"
            role="radio"
            aria-checked={score === n}
            onClick={() => setScore(n)}
            className={cn(
              "flex size-9 items-center justify-center rounded-lg border text-[13px] font-medium transition-colors",
              score === n ? "border-transparent bg-brand-solid text-white" : "border-border-strong bg-surface text-ink-700 hover:bg-ink-50",
            )}
          >
            {n}
          </button>
        ))}
      </div>
      <p className="text-[12px] text-ink-500">0 — umuman mamnun emas, 10 — to&apos;liq mamnun.</p>
      <div>
        <Label htmlFor="complaint-feedback">Izoh (ixtiyoriy)</Label>
        <Textarea id="complaint-feedback" rows={3} maxLength={1000} value={comment} onChange={(e) => setComment(e.target.value)} />
      </div>
      {rate.isError && <Alert>{errorText(rate.error, "Bahoni saqlab bo'lmadi.")}</Alert>}
      <Button size="sm" loading={rate.isPending} disabled={score === null} onClick={() => rate.mutate()}>Bahoni yuborish</Button>
    </section>
  );
}
