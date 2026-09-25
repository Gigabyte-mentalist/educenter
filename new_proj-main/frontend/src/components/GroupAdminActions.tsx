"use client";

import { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { CheckCircle2, Pencil, Play, X, XCircle } from "lucide-react";
import { removeGroupStaff, setGroupStatus, updateGroup, type GroupOverview } from "@/lib/api/academic";
import { ApiError } from "@/lib/api-client";
import { usePermissions } from "@/lib/use-permissions";
import { Alert } from "@/components/ui/Alert";
import { Button } from "@/components/ui/Button";
import { ConfirmDialog } from "@/components/ui/ConfirmDialog";
import { Input, Label } from "@/components/ui/Field";
import { Modal } from "@/components/ui/Modal";

const errorText = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

/**
 * What the office does to a group once it exists: rename or resize it, start it, finish it, cancel it. Nothing comes back
 * from Completed or Cancelled, so a closed group shows no actions at all. Closing asks first and says what it does to the
 * students — their enrollments end with the group.
 */
export function GroupAdminActions({ group }: { group: GroupOverview }) {
  const { can } = usePermissions();
  const queryClient = useQueryClient();
  const [editOpen, setEditOpen] = useState(false);
  const [closing, setClosing] = useState<"Completed" | "Cancelled" | null>(null);

  const refresh = () => queryClient.invalidateQueries({ queryKey: ["groups"] });
  const activate = useMutation({ mutationFn: () => setGroupStatus(group.id, "Active"), onSuccess: refresh });
  const close = useMutation({
    mutationFn: (status: "Completed" | "Cancelled") => setGroupStatus(group.id, status),
    onSuccess: async () => {
      await refresh();
      setClosing(null);
    },
  });

  if (!can("groups.edit") || group.status === "Completed" || group.status === "Cancelled") return null;

  const stopClosing = () => {
    close.reset();
    setClosing(null);
  };
  const students = `${group.studentCount} ta faol talaba`;

  return (
    <>
      <div className="flex flex-wrap gap-2">
        <Button size="sm" variant="secondary" onClick={() => setEditOpen(true)}>
          <Pencil className="size-3.5" aria-hidden />
          Tahrirlash
        </Button>
        {group.status === "Forming" && (
          <Button size="sm" variant="secondary" loading={activate.isPending} onClick={() => activate.mutate()}>
            <Play className="size-3.5" aria-hidden />
            Faollashtirish
          </Button>
        )}
        {group.status === "Active" && (
          <Button size="sm" variant="secondary" onClick={() => setClosing("Completed")}>
            <CheckCircle2 className="size-3.5" aria-hidden />
            Yopish
          </Button>
        )}
        <Button size="sm" variant="secondary" onClick={() => setClosing("Cancelled")}>
          <XCircle className="size-3.5" aria-hidden />
          Bekor qilish
        </Button>
      </div>
      {activate.isError && <p className="mt-2 w-full text-[13px] text-danger-600">{errorText(activate.error, "Faollashtirib bo'lmadi.")}</p>}

      {editOpen && <EditGroupModal group={group} onClose={() => setEditOpen(false)} />}

      <ConfirmDialog
        open={closing !== null}
        title={closing === "Completed" ? `${group.name} guruhini yopish` : `${group.name} guruhini bekor qilish`}
        description={
          <>
            {closing === "Completed"
              ? <>{students} uchun yozuv <strong>«Tugallangan»</strong> bo&apos;ladi va ular guruhdan chiqadi.</>
              : <>{students} guruhdan <strong>chiqariladi</strong> (sabab: guruh bekor qilindi).</>}{" "}
            Talabalar va o&apos;qituvchilarga xabar yuboriladi. Bu amalni qaytarib bo&apos;lmaydi; guruhning kelajakdagi darslari
            jadvalda qoladi.
          </>
        }
        confirmLabel={closing === "Completed" ? "Guruhni yopish" : "Guruhni bekor qilish"}
        cancelLabel="Orqaga"
        loading={close.isPending}
        error={close.isError ? errorText(close.error, "Holatni o'zgartirib bo'lmadi.") : null}
        onConfirm={() => closing && close.mutate(closing)}
        onClose={stopClosing}
      />
    </>
  );
}

/** Mounted only while open, so the fields start from the group's current values without an effect to copy them in. */
function EditGroupModal({ group, onClose }: { group: GroupOverview; onClose: () => void }) {
  const queryClient = useQueryClient();
  const [name, setName] = useState(group.name);
  const [capacity, setCapacity] = useState(group.capacity);

  const mutation = useMutation({
    mutationFn: () => updateGroup(group.id, { name: name.trim(), capacity }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["groups"] });
      onClose();
    },
  });

  return (
    <Modal open onClose={onClose} title="Guruhni tahrirlash" description={`${group.studentCount} ta faol talaba bor.`}>
      <form
        className="space-y-4"
        onSubmit={(e) => {
          e.preventDefault();
          mutation.mutate();
        }}
      >
        <div>
          <Label htmlFor="edit-group-name">Guruh nomi</Label>
          <Input id="edit-group-name" required maxLength={200} value={name} onChange={(e) => setName(e.target.value)} />
        </div>
        <div>
          <Label htmlFor="edit-group-capacity">Sig&apos;im</Label>
          <Input
            id="edit-group-capacity" type="number" required min={Math.max(1, group.studentCount)}
            value={capacity} onChange={(e) => setCapacity(Number(e.target.value))}
          />
          <p className="mt-1 text-[12px] text-ink-500">Sig&apos;im hozirgi faol talabalar sonidan kam bo&apos;lmasligi kerak.</p>
        </div>
        {mutation.isError && <Alert>{errorText(mutation.error, "Saqlab bo'lmadi.")}</Alert>}
        <Button type="submit" loading={mutation.isPending} className="w-full">Saqlash</Button>
      </form>
    </Modal>
  );
}

/** The ✕ on a staff badge. The server refuses the group's only main teacher and a teacher with lessons still ahead, and says
 * why — the dialog shows that instead of closing. */
export function RemoveStaffButton({ groupId, userId, name }: { groupId: string; userId: string; name: string }) {
  const { can } = usePermissions();
  const queryClient = useQueryClient();
  const [open, setOpen] = useState(false);

  const mutation = useMutation({
    mutationFn: () => removeGroupStaff(groupId, userId),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["groups"] });
      setOpen(false);
    },
  });

  if (!can("groups.edit")) return null;

  const stop = () => {
    mutation.reset();
    setOpen(false);
  };

  return (
    <>
      <button
        type="button" onClick={() => setOpen(true)} aria-label={`${name} guruhdan olib tashlash`}
        className="-mr-1 rounded-full p-0.5 text-current opacity-70 hover:opacity-100"
      >
        <X className="size-3" aria-hidden />
      </button>
      <ConfirmDialog
        open={open}
        title={`${name} guruhdan olib tashlansinmi?`}
        description="O'qituvchi guruhga biriktirilmagan bo'lib qoladi. Uning kelajakdagi darslari bo'lsa, avval ularni boshqa o'qituvchiga o'tkazing."
        confirmLabel="Olib tashlash"
        cancelLabel="Orqaga"
        loading={mutation.isPending}
        error={mutation.isError ? errorText(mutation.error, "Olib tashlab bo'lmadi.") : null}
        onConfirm={() => mutation.mutate()}
        onClose={stop}
      />
    </>
  );
}
