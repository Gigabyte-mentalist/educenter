"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeftRight, GraduationCap, LogOut, PlayCircle, Plus, Snowflake } from "lucide-react";
import { listGroups } from "@/lib/api/academic";
import {
  dropEnrollment, enrollStudent, freezeStudent, getTransferTargets, graduateStudent, resumeStudent, transferStudent,
  type EnrollmentSummary,
} from "@/lib/api/students";
import { ApiError } from "@/lib/api-client";
import { usePermissions } from "@/lib/use-permissions";
import { Button } from "@/components/ui/Button";
import { Drawer } from "@/components/ui/Drawer";
import { Alert } from "@/components/ui/Alert";
import { ConfirmDialog } from "@/components/ui/ConfirmDialog";
import { Input, Label, Select, Textarea } from "@/components/ui/Field";

const errorText = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

/**
 * Transfer / Freeze / Resume / Enroll / Graduate (docs/00_TZ.md §10, §20-21). Each action is gated by its own
 * permission (`student.transfer`, `student.freeze`, `student.enroll`) rather than one role check for the whole
 * group — the matrix grants `student.enroll` to the Operator too ("initiate"), who holds neither of the other two.
 */
export function StudentLifecycleActions({
  studentId,
  status,
  enrollments,
}: {
  studentId: string;
  status: string;
  enrollments: EnrollmentSummary[];
}) {
  const { can } = usePermissions();
  const canEnroll = can("student.enroll");
  const canTransfer = can("student.transfer");
  const canFreeze = can("student.freeze");

  const [transferOpen, setTransferOpen] = useState(false);
  const [freezeOpen, setFreezeOpen] = useState(false);
  const [enrollOpen, setEnrollOpen] = useState(false);
  const [graduatePending, setGraduatePending] = useState(false);
  const activeEnrollments = enrollments.filter((e) => e.status === "Active");
  const openEnrollments = enrollments.filter((e) => e.status === "Active" || e.status === "Frozen");
  const queryClient = useQueryClient();

  const invalidate = () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: ["students", studentId, "overview"] }),
      queryClient.invalidateQueries({ queryKey: ["students", studentId, "timeline"] }),
    ]);

  const resumeMutation = useMutation({
    mutationFn: () => resumeStudent(studentId),
    onSuccess: invalidate,
  });

  const graduateMutation = useMutation({
    mutationFn: () => graduateStudent(studentId),
    onSuccess: async () => {
      await invalidate();
      setGraduatePending(false);
    },
  });

  if (!canEnroll && !canTransfer && !canFreeze) return null;

  const enrollAction = canEnroll && (
    <Button size="sm" variant="secondary" onClick={() => setEnrollOpen(true)}>
      <Plus className="size-3.5" aria-hidden />
      Guruhga yozish
    </Button>
  );

  if (status === "Frozen") {
    return (
      <div className="flex flex-col items-end gap-1.5">
        <div className="flex gap-2">
          {enrollAction}
          {canFreeze && (
            <Button size="sm" variant="secondary" loading={resumeMutation.isPending} onClick={() => resumeMutation.mutate()}>
              <PlayCircle className="size-3.5" aria-hidden />
              Qayta faollashtirish
            </Button>
          )}
        </div>
        {resumeMutation.isError && (
          <p className="text-[12px] text-danger-600">{errorText(resumeMutation.error, "Faollashtirib bo'lmadi.")}</p>
        )}
        {enrollOpen && <EnrollDrawer studentId={studentId} enrollments={enrollments} onClose={() => setEnrollOpen(false)} onDone={invalidate} />}
      </div>
    );
  }

  return (
    <>
      <div className="flex flex-wrap items-start justify-end gap-2">
        {enrollAction}
        {canTransfer && (
          <Button size="sm" variant="secondary" disabled={activeEnrollments.length === 0} onClick={() => setTransferOpen(true)}>
            <ArrowLeftRight className="size-3.5" aria-hidden />
            Ko&apos;chirish
          </Button>
        )}
        {canFreeze && (
          <Button size="sm" variant="secondary" disabled={activeEnrollments.length === 0} onClick={() => setFreezeOpen(true)}>
            <Snowflake className="size-3.5" aria-hidden />
            Muzlatish
          </Button>
        )}
        {canEnroll && (
          <Button
            size="sm" variant="secondary" disabled={openEnrollments.length === 0}
            onClick={() => { graduateMutation.reset(); setGraduatePending(true); }}
          >
            <GraduationCap className="size-3.5" aria-hidden />
            Bitirdi
          </Button>
        )}
      </div>
      {transferOpen && (
        <TransferDrawer studentId={studentId} enrollments={activeEnrollments} onClose={() => setTransferOpen(false)} onDone={invalidate} />
      )}
      {freezeOpen && <FreezeDrawer studentId={studentId} onClose={() => setFreezeOpen(false)} onDone={invalidate} />}
      {enrollOpen && <EnrollDrawer studentId={studentId} enrollments={enrollments} onClose={() => setEnrollOpen(false)} onDone={invalidate} />}
      <ConfirmDialog
        open={graduatePending}
        title="Kursni bitirdi deb belgilash"
        description="Barcha faol va muzlatilgan guruhlar tugallangan deb belgilanadi, student holati Bitirgan bo'ladi."
        confirmLabel="Bitirdi"
        loading={graduateMutation.isPending}
        error={graduateMutation.isError ? errorText(graduateMutation.error, "Bitirdi deb belgilab bo'lmadi.") : null}
        onConfirm={() => graduateMutation.mutate()}
        onClose={() => setGraduatePending(false)}
      />
    </>
  );
}

function EnrollDrawer({
  studentId,
  enrollments,
  onClose,
  onDone,
}: {
  studentId: string;
  enrollments: EnrollmentSummary[];
  onClose: () => void;
  onDone: () => Promise<unknown>;
}) {
  const [groupId, setGroupId] = useState("");
  const groupsQuery = useQuery({ queryKey: ["groups"], queryFn: listGroups });
  const currentGroupIds = new Set(enrollments.filter((e) => e.status === "Active" || e.status === "Frozen").map((e) => e.groupId));
  const openGroups = (groupsQuery.data ?? [])
    .filter((g) => (g.status === "Active" || g.status === "Forming") && !currentGroupIds.has(g.id));

  const mutation = useMutation({
    mutationFn: () => enrollStudent(studentId, groupId),
    onSuccess: async () => {
      await onDone();
      onClose();
    },
  });

  return (
    <Drawer open onClose={onClose} title="Guruhga yozish" description="Mavjud guruhlarga qo'shimcha guruh sifatida yoziladi.">
      <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); mutation.mutate(); }}>
        <div>
          <Label htmlFor="enroll-group">Guruh</Label>
          <Select id="enroll-group" required value={groupId} onChange={(e) => setGroupId(e.target.value)}>
            <option value="">Tanlang</option>
            {openGroups.map((g) => {
              const free = g.capacity - g.studentCount;
              return (
                <option key={g.id} value={g.id} disabled={free <= 0}>
                  {g.name} — {free > 0 ? `${free} ta o'rin` : "to'lgan"}
                </option>
              );
            })}
          </Select>
        </div>
        {mutation.isError && <Alert>{errorText(mutation.error, "Yozib bo'lmadi.")}</Alert>}
        <Button type="submit" loading={mutation.isPending} disabled={!groupId} className="w-full">
          Yozish
        </Button>
      </form>
    </Drawer>
  );
}

/** "Chiqarish" — dropped inline into each active enrollment row on the Umumiy tab, since a drop is per-group,
 * not per-student (docs/21.09-audit-jurnali/S01.03-guruhga-yozish-chiqarish.md §3.5). */
export function DropEnrollmentButton({ studentId, enrollment }: { studentId: string; enrollment: EnrollmentSummary }) {
  const [open, setOpen] = useState(false);
  const [reason, setReason] = useState("");
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: () => dropEnrollment(studentId, enrollment.id, reason),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["students", studentId, "overview"] }),
        queryClient.invalidateQueries({ queryKey: ["students", studentId, "timeline"] }),
      ]);
      setOpen(false);
      setReason("");
    },
  });

  return (
    <>
      <Button size="sm" variant="ghost" onClick={() => { mutation.reset(); setOpen(true); }}>
        <LogOut className="size-3.5" aria-hidden />
        Chiqarish
      </Button>
      {open && (
        <Drawer open onClose={() => setOpen(false)} title="Guruhdan chiqarish" description={enrollment.groupName}>
          <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); mutation.mutate(); }}>
            <div>
              <Label htmlFor="drop-reason">Sabab</Label>
              <Textarea id="drop-reason" required value={reason} onChange={(e) => setReason(e.target.value)} />
            </div>
            {mutation.isError && <Alert>{errorText(mutation.error, "Chiqarib bo'lmadi.")}</Alert>}
            <Button type="submit" variant="danger" loading={mutation.isPending} disabled={!reason.trim()} className="w-full">
              Chiqarish
            </Button>
          </form>
        </Drawer>
      )}
    </>
  );
}

function TransferDrawer({
  studentId,
  enrollments,
  onClose,
  onDone,
}: {
  studentId: string;
  enrollments: EnrollmentSummary[];
  onClose: () => void;
  onDone: () => Promise<unknown>;
}) {
  const [fromGroupId, setFromGroupId] = useState(enrollments[0]?.groupId ?? "");
  const [toGroupId, setToGroupId] = useState("");
  const [reason, setReason] = useState("");

  const targetsQuery = useQuery({
    queryKey: ["students", studentId, "transfer-targets"],
    queryFn: () => getTransferTargets(studentId),
  });

  const mutation = useMutation({
    mutationFn: () => transferStudent(studentId, fromGroupId, toGroupId, reason),
    onSuccess: async () => {
      await onDone();
      onClose();
    },
  });

  return (
    <Drawer open onClose={onClose} title="Guruhni almashtirish" description="Eski guruhdagi yozuv yopiladi, yangisi shu zahoti ochiladi.">
      <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); mutation.mutate(); }}>
        <div>
          <Label htmlFor="transfer-from">Hozirgi guruh</Label>
          <Select id="transfer-from" required value={fromGroupId} onChange={(e) => setFromGroupId(e.target.value)}>
            {enrollments.map((e) => <option key={e.groupId} value={e.groupId}>{e.groupName}</option>)}
          </Select>
        </div>
        <div>
          <Label htmlFor="transfer-to">Yangi guruh</Label>
          {targetsQuery.isLoading && <p className="text-[12.5px] text-ink-500">Yuklanmoqda...</p>}
          {targetsQuery.data && (
            <Select id="transfer-to" required value={toGroupId} onChange={(e) => setToGroupId(e.target.value)}>
              <option value="">Tanlang</option>
              {targetsQuery.data.map((t) => (
                <option key={t.groupId} value={t.groupId}>
                  {t.branchName} · {t.groupName} ({t.freeSeats} bo&apos;sh joy)
                </option>
              ))}
            </Select>
          )}
          {targetsQuery.data?.length === 0 && (
            <p className="mt-1 text-[12px] text-ink-500">Bo&apos;sh joyli mos guruh topilmadi.</p>
          )}
        </div>
        <div>
          <Label htmlFor="transfer-reason">Sabab</Label>
          <Textarea id="transfer-reason" required value={reason} onChange={(e) => setReason(e.target.value)} />
        </div>
        {mutation.isError && <Alert>{errorText(mutation.error, "Ko'chirib bo'lmadi.")}</Alert>}
        <Button type="submit" loading={mutation.isPending} disabled={!toGroupId || !fromGroupId} className="w-full">
          Ko&apos;chirish
        </Button>
      </form>
    </Drawer>
  );
}

function FreezeDrawer({
  studentId,
  onClose,
  onDone,
}: {
  studentId: string;
  onClose: () => void;
  onDone: () => Promise<unknown>;
}) {
  const [reason, setReason] = useState("");
  const [resumeOn, setResumeOn] = useState("");

  const mutation = useMutation({
    mutationFn: () => freezeStudent(studentId, reason, resumeOn || undefined),
    onSuccess: async () => {
      await onDone();
      onClose();
    },
  });

  return (
    <Drawer open onClose={onClose} title="O'qishni muzlatish" description="Barcha faol guruhlar to'xtatiladi, hisob-kitob ham to'xtaydi.">
      <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); mutation.mutate(); }}>
        <div>
          <Label htmlFor="freeze-reason">Sabab</Label>
          <Textarea id="freeze-reason" required value={reason} onChange={(e) => setReason(e.target.value)} />
        </div>
        <div>
          <Label htmlFor="freeze-resume">Qaytish sanasi (ixtiyoriy)</Label>
          <Input id="freeze-resume" type="date" value={resumeOn} onChange={(e) => setResumeOn(e.target.value)} />
        </div>
        {mutation.isError && <Alert>{errorText(mutation.error, "Muzlatib bo'lmadi.")}</Alert>}
        <Button type="submit" loading={mutation.isPending} disabled={!reason.trim()} className="w-full">
          Muzlatish
        </Button>
      </form>
    </Drawer>
  );
}
