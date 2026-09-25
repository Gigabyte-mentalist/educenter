"use client";

import { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Pencil, Trash2 } from "lucide-react";
import { deleteHomework, updateHomework, type Homework, type HomeworkType } from "@/lib/api/lms";
import { ApiError } from "@/lib/api-client";
import { Alert } from "@/components/ui/Alert";
import { Button } from "@/components/ui/Button";
import { ConfirmDialog } from "@/components/ui/ConfirmDialog";
import { Input, Label, Select, Textarea } from "@/components/ui/Field";
import { Modal } from "@/components/ui/Modal";

const TYPES: HomeworkType[] = ["Writing", "Speaking", "Reading", "Listening", "Grammar", "Vocabulary", "FileUpload", "Quiz", "Custom"];
const errorText = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

/** `<input type="datetime-local">` wants the browser's local wall time, not UTC. */
function toLocalInput(iso: string): string {
  const d = new Date(iso);
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

/**
 * "Tahrirlash" and "O'chirish" on a homework card. A moved deadline is announced to the group by the server; the maximum score
 * cannot drop below a score already given, and a homework someone handed in for cannot be deleted — both are refused with a
 * message the dialog shows instead of closing.
 */
export function HomeworkAdminActions({ homework }: { homework: Homework }) {
  const queryClient = useQueryClient();
  const [editing, setEditing] = useState(false);
  const [deleting, setDeleting] = useState(false);

  const refresh = () => queryClient.invalidateQueries({ queryKey: ["homework"] });
  const deleteMutation = useMutation({
    mutationFn: () => deleteHomework(homework.id),
    onSuccess: async () => {
      await refresh();
      setDeleting(false);
    },
  });
  const stopDeleting = () => {
    deleteMutation.reset();
    setDeleting(false);
  };

  return (
    <>
      <Button size="sm" variant="ghost" onClick={() => setEditing(true)}>
        <Pencil className="size-3.5" aria-hidden />
        Tahrirlash
      </Button>
      <Button size="sm" variant="ghost" onClick={() => setDeleting(true)}>
        <Trash2 className="size-3.5" aria-hidden />
        O&apos;chirish
      </Button>
      {editing && <EditHomeworkModal homework={homework} onDone={refresh} onClose={() => setEditing(false)} />}
      <ConfirmDialog
        open={deleting}
        title={`${homework.title} homeworkini o'chirish`}
        description="Homework butunlay o'chiriladi va qaytarib bo'lmaydi. Topshiriq kelgan homeworkni o'chirib bo'lmaydi."
        loading={deleteMutation.isPending}
        error={deleteMutation.isError ? errorText(deleteMutation.error, "O'chirib bo'lmadi.") : null}
        onConfirm={() => deleteMutation.mutate()}
        onClose={stopDeleting}
      />
    </>
  );
}

/** Mounted only while open, so the fields start from the homework's current values. */
function EditHomeworkModal({ homework, onDone, onClose }: { homework: Homework; onDone: () => Promise<unknown>; onClose: () => void }) {
  const [title, setTitle] = useState(homework.title);
  const [description, setDescription] = useState(homework.description ?? "");
  const [type, setType] = useState<HomeworkType>(homework.type);
  const [deadline, setDeadline] = useState(toLocalInput(homework.deadline));
  const [maxScore, setMaxScore] = useState(homework.maxScore);

  const mutation = useMutation({
    mutationFn: () =>
      updateHomework(homework.id, { title: title.trim(), description: description.trim(), type, deadline: new Date(deadline).toISOString(), maxScore }),
    onSuccess: async () => {
      await onDone();
      onClose();
    },
  });

  return (
    <Modal open onClose={onClose} title="Homeworkni tahrirlash" description="Muddat o'zgarsa, guruh studentlariga xabar yuboriladi.">
      <form
        className="space-y-4"
        onSubmit={(e) => {
          e.preventDefault();
          mutation.mutate();
        }}
      >
        <div>
          <Label htmlFor="edit-hw-title">Sarlavha</Label>
          <Input id="edit-hw-title" required maxLength={200} value={title} onChange={(e) => setTitle(e.target.value)} />
        </div>
        <div>
          <Label htmlFor="edit-hw-description">Tavsif</Label>
          <Textarea id="edit-hw-description" maxLength={2000} value={description} onChange={(e) => setDescription(e.target.value)} />
        </div>
        <div className="grid gap-4 sm:grid-cols-2">
          <div>
            <Label htmlFor="edit-hw-type">Turi</Label>
            <Select id="edit-hw-type" value={type} onChange={(e) => setType(e.target.value as HomeworkType)}>
              {TYPES.map((t) => <option key={t} value={t}>{t}</option>)}
            </Select>
          </div>
          <div>
            <Label htmlFor="edit-hw-max">Maksimal ball</Label>
            <Input id="edit-hw-max" type="number" min={1} required value={maxScore} onChange={(e) => setMaxScore(Number(e.target.value))} />
          </div>
        </div>
        <div>
          <Label htmlFor="edit-hw-deadline">Topshirish muddati</Label>
          <Input id="edit-hw-deadline" type="datetime-local" required value={deadline} onChange={(e) => setDeadline(e.target.value)} />
        </div>
        {mutation.isError && <Alert>{errorText(mutation.error, "Saqlab bo'lmadi.")}</Alert>}
        <Button type="submit" loading={mutation.isPending} className="w-full">Saqlash</Button>
      </form>
    </Modal>
  );
}
