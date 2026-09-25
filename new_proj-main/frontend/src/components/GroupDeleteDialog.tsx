"use client";

import { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { deleteGroup, groupDeleteRefusal, setGroupStatus, type Group, type GroupDeleteRefusal } from "@/lib/api/academic";
import { ApiError } from "@/lib/api-client";
import { Alert } from "@/components/ui/Alert";
import { Button } from "@/components/ui/Button";
import { ConfirmDialog } from "@/components/ui/ConfirmDialog";
import { Modal } from "@/components/ui/Modal";

const errorText = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

/**
 * Deleting a group is only for one with no history. When the server refuses (it still has enrollments, lessons, invoices…)
 * this lists what stands in the way and, while the group is still open, offers cancelling it instead — that keeps the
 * history and ends the students' enrollments. Mount it with `key={group.id}` so each group starts from a fresh state.
 */
export function GroupDeleteDialog({ group, onClose }: { group: Group; onClose: () => void }) {
  const queryClient = useQueryClient();
  const [refusal, setRefusal] = useState<GroupDeleteRefusal | null>(null);

  const done = async () => {
    await queryClient.invalidateQueries({ queryKey: ["groups"] });
    onClose();
  };

  const deleteMutation = useMutation({
    mutationFn: () => deleteGroup(group.id),
    onSuccess: done,
    onError: (error) => setRefusal(groupDeleteRefusal(error)),
  });
  const cancelMutation = useMutation({ mutationFn: () => setGroupStatus(group.id, "Cancelled"), onSuccess: done });

  if (refusal) {
    return (
      <Modal open onClose={onClose} title={`${group.name} guruhini o'chirib bo'lmaydi`} description="Unga bog'liq ma'lumotlar bor:">
        <div className="space-y-4">
          <ul className="list-disc space-y-1 pl-5 text-[13px] text-ink-700">
            {refusal.blockers.map((blocker) => <li key={blocker}>{blocker}</li>)}
          </ul>
          <p className="text-[13px] text-ink-600">
            {refusal.canCancel
              ? "Tarix saqlanishi uchun guruhni o'chirmasdan bekor qiling: faol talabalar guruhdan chiqadi, yozuvlar qoladi."
              : "Guruh allaqachon yopilgan — tarix saqlanadi."}
          </p>
          {cancelMutation.isError && <Alert>{errorText(cancelMutation.error, "Guruhni bekor qilib bo'lmadi.")}</Alert>}
          <div className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
            <Button variant="secondary" onClick={onClose} disabled={cancelMutation.isPending}>Yopish</Button>
            {refusal.canCancel && (
              <Button variant="danger" loading={cancelMutation.isPending} onClick={() => cancelMutation.mutate()}>
                Guruhni bekor qilish
              </Button>
            )}
          </div>
        </div>
      </Modal>
    );
  }

  return (
    <ConfirmDialog
      open
      title={`${group.name} guruhini o'chirish`}
      description="Guruh butunlay o'chiriladi va qaytarib bo'lmaydi. Unda talaba, dars yoki hisob-faktura bo'lsa, o'chirish rad etiladi."
      loading={deleteMutation.isPending}
      error={deleteMutation.isError ? errorText(deleteMutation.error, "O'chirib bo'lmadi.") : null}
      onConfirm={() => deleteMutation.mutate()}
      onClose={onClose}
    />
  );
}
