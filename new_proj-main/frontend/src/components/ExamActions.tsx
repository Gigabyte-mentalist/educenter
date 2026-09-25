"use client";

import { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Pencil, Trash2, X } from "lucide-react";
import { deleteExam, deleteExamResult, updateExam, type Exam, type ExamType } from "@/lib/api/lms";
import { ApiError } from "@/lib/api-client";
import { Alert } from "@/components/ui/Alert";
import { Button } from "@/components/ui/Button";
import { ConfirmDialog } from "@/components/ui/ConfirmDialog";
import { Input, Label, Select } from "@/components/ui/Field";
import { Modal } from "@/components/ui/Modal";

const errorText = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

/**
 * "Tahrirlash" and "O'chirish" on an exam row. Fixing an exam is allowed even after results are in; deleting one is not —
 * the server refuses an exam that has results and says how many, and the dialog shows that instead of closing.
 */
export function ExamRowActions({ exam, typeLabels }: { exam: Exam; typeLabels: Record<ExamType, string> }) {
  const queryClient = useQueryClient();
  const [editing, setEditing] = useState(false);
  const [deleting, setDeleting] = useState(false);

  const deleteMutation = useMutation({
    mutationFn: () => deleteExam(exam.id),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["exams"] });
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
      {editing && <EditExamModal exam={exam} typeLabels={typeLabels} onClose={() => setEditing(false)} />}
      <ConfirmDialog
        open={deleting}
        title={`${exam.name} imtihonini o'chirish`}
        description="Imtihon butunlay o'chiriladi va qaytarib bo'lmaydi. Natijasi kiritilgan imtihonni o'chirib bo'lmaydi."
        loading={deleteMutation.isPending}
        error={deleteMutation.isError ? errorText(deleteMutation.error, "O'chirib bo'lmadi.") : null}
        onConfirm={() => deleteMutation.mutate()}
        onClose={stopDeleting}
      />
    </>
  );
}

/** Mounted only while open, so the fields start from the exam's current values. */
function EditExamModal({ exam, typeLabels, onClose }: { exam: Exam; typeLabels: Record<ExamType, string>; onClose: () => void }) {
  const queryClient = useQueryClient();
  const [name, setName] = useState(exam.name);
  const [type, setType] = useState<ExamType>(exam.type);
  const [date, setDate] = useState(exam.date);

  const mutation = useMutation({
    mutationFn: () => updateExam(exam.id, { name: name.trim(), type, date }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["exams"] });
      onClose();
    },
  });

  return (
    <Modal open onClose={onClose} title="Imtihonni tahrirlash" description="Guruhni o'zgartirib bo'lmaydi. Kiritilgan natijalar joyida qoladi.">
      <form
        className="space-y-4"
        onSubmit={(e) => {
          e.preventDefault();
          mutation.mutate();
        }}
      >
        <div>
          <Label htmlFor="edit-exam-name">Nomi</Label>
          <Input id="edit-exam-name" required maxLength={200} value={name} onChange={(e) => setName(e.target.value)} />
        </div>
        <div className="grid gap-4 sm:grid-cols-2">
          <div>
            <Label htmlFor="edit-exam-type">Turi</Label>
            <Select id="edit-exam-type" value={type} onChange={(e) => setType(e.target.value as ExamType)}>
              {Object.entries(typeLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
            </Select>
          </div>
          <div>
            <Label htmlFor="edit-exam-date">Sana</Label>
            <Input id="edit-exam-date" type="date" required value={date} onChange={(e) => setDate(e.target.value)} />
          </div>
        </div>
        {mutation.isError && <Alert>{errorText(mutation.error, "Saqlab bo'lmadi.")}</Alert>}
        <Button type="submit" loading={mutation.isPending} className="w-full">Saqlash</Button>
      </form>
    </Modal>
  );
}

/** The ✕ on a result card in the results drawer — for a result entered by mistake. The student is not told; their timeline is. */
export function RemoveResultButton({ examId, resultId, studentName }: { examId: string; resultId: string; studentName: string }) {
  const queryClient = useQueryClient();
  const [open, setOpen] = useState(false);

  const mutation = useMutation({
    mutationFn: () => deleteExamResult(examId, resultId),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["exam-results", examId] });
      setOpen(false);
    },
  });
  const stop = () => {
    mutation.reset();
    setOpen(false);
  };

  return (
    <>
      <button
        type="button" onClick={() => setOpen(true)} aria-label={`${studentName} natijasini o'chirish`}
        className="rounded-full p-1 text-ink-400 hover:text-danger-600"
      >
        <X className="size-4" aria-hidden />
      </button>
      <ConfirmDialog
        open={open}
        title={`${studentName} natijasini o'chirish`}
        description="Noto'g'ri kiritilgan natija olib tashlanadi. Studentga xabar yuborilmaydi, lekin uning tarixida bu qayd qoladi."
        loading={mutation.isPending}
        error={mutation.isError ? errorText(mutation.error, "O'chirib bo'lmadi.") : null}
        onConfirm={() => mutation.mutate()}
        onClose={stop}
      />
    </>
  );
}
