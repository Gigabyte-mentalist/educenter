"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AlertTriangle, Ban, Pencil } from "lucide-react";
import {
  bulkCancelLessons, cancelLesson, listGroups, listSubstitutes, updateLesson,
  type Lesson, type ScheduleConflict,
} from "@/lib/api/academic";
import { ApiError } from "@/lib/api-client";
import { usePermissions } from "@/lib/use-permissions";
import { formatShortDateTime, formatTime } from "@/lib/format-date";
import { Alert } from "@/components/ui/Alert";
import { Button } from "@/components/ui/Button";
import { Input, Label, Select } from "@/components/ui/Field";
import { Modal } from "@/components/ui/Modal";
import { RoomSelect } from "@/components/RoomSelect";

const errorText = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

export const CONFLICT_LABEL: Record<ScheduleConflict["type"], string> = {
  ROOM: "Xona band",
  TEACHER: "O'qituvchi band",
  GROUP: "Guruh band",
};

export function ConflictNotice({ conflicts }: { conflicts: ScheduleConflict[] }) {
  if (conflicts.length === 0) return null;
  return (
    <div className="rounded-lg border border-warning-500/30 bg-warning-50 p-3">
      <p className="flex items-center gap-1.5 text-[13px] font-medium text-warning-600">
        <AlertTriangle className="size-4" aria-hidden />
        To&apos;qnashuv aniqlandi
      </p>
      <ul className="mt-2 space-y-1 text-[12.5px] text-warning-600">
        {conflicts.map((c, i) => (
          <li key={i}>{CONFLICT_LABEL[c.type]} — {formatShortDateTime(c.startsAt)} dan {formatTime(c.endsAt)} gacha</li>
        ))}
      </ul>
    </div>
  );
}

/** `<input type="datetime-local">` wants the browser's local wall time, not UTC. */
function toLocalInput(iso: string): string {
  const d = new Date(iso);
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

/**
 * Moves a lesson, changes its room or teacher, or retitles it — only what differs is sent. A clash comes back as a list (the
 * lesson does not clash with itself); a lesson that is over or cancelled is refused with the server's own message. Mount it
 * only while open, so the fields start from the lesson's current values.
 */
export function EditLessonModal({ lesson, onClose }: { lesson: Lesson; onClose: () => void }) {
  const queryClient = useQueryClient();
  const [startsAt, setStartsAt] = useState(toLocalInput(lesson.startsAt));
  const [endsAt, setEndsAt] = useState(toLocalInput(lesson.endsAt));
  const [roomId, setRoomId] = useState(lesson.roomId ?? "");
  // A room worth a second look (it seats fewer than the group) is said after the lesson is saved, before the window closes.
  const [warnings, setWarnings] = useState<string[] | null>(null);
  const [teacherId, setTeacherId] = useState(lesson.teacherId);
  const [topic, setTopic] = useState(lesson.topic ?? "");
  const [conflicts, setConflicts] = useState<ScheduleConflict[]>([]);

  // The lesson's own branch comes from its group; rooms are that branch's, stand-in teachers are those already teaching there.
  const groupsQuery = useQuery({ queryKey: ["groups"], queryFn: listGroups });
  const branchId = groupsQuery.data?.find((g) => g.id === lesson.groupId)?.branchId ?? "";
  const substitutesQuery = useQuery({ queryKey: ["schedule", "substitutes", lesson.id], queryFn: () => listSubstitutes(lesson.id) });

  const changes = {
    ...(new Date(startsAt).getTime() !== new Date(lesson.startsAt).getTime() && { startsAt: new Date(startsAt).toISOString() }),
    ...(new Date(endsAt).getTime() !== new Date(lesson.endsAt).getTime() && { endsAt: new Date(endsAt).toISOString() }),
    ...(roomId !== "" && roomId !== (lesson.roomId ?? "") && { roomId }),
    ...(teacherId !== lesson.teacherId && { teacherId }),
    ...(topic.trim() !== (lesson.topic ?? "") && { topic: topic.trim() }),
  };

  const mutation = useMutation({
    mutationFn: () => updateLesson(lesson.id, changes),
    onSuccess: async (result) => {
      if (!result.ok) {
        setConflicts(result.conflicts);
        return;
      }
      await queryClient.invalidateQueries({ queryKey: ["lessons"] });
      if (result.lesson.warnings?.length) setWarnings(result.lesson.warnings);
      else onClose();
    },
  });

  return (
    <Modal open onClose={onClose} title="Darsni tahrirlash" description="Vaqt, xona yoki o'qituvchi band bo'lsa, tizim ogohlantiradi.">
      <form
        className="space-y-4"
        onSubmit={(e) => {
          e.preventDefault();
          setConflicts([]);
          mutation.mutate();
        }}
      >
        <div className="grid gap-4 sm:grid-cols-2">
          <div>
            <Label htmlFor="edit-lesson-start">Boshlanishi</Label>
            <Input id="edit-lesson-start" type="datetime-local" required value={startsAt} onChange={(e) => setStartsAt(e.target.value)} />
          </div>
          <div>
            <Label htmlFor="edit-lesson-end">Tugashi</Label>
            <Input id="edit-lesson-end" type="datetime-local" required value={endsAt} onChange={(e) => setEndsAt(e.target.value)} />
          </div>
        </div>
        <div>
          <Label htmlFor="edit-lesson-room">Xona</Label>
          <RoomSelect
            id="edit-lesson-room" branchId={branchId} value={roomId} onChange={setRoomId}
            current={{ id: lesson.roomId ?? null, name: lesson.roomName }}
          />
        </div>
        <div>
          <Label htmlFor="edit-lesson-teacher">O&apos;qituvchi</Label>
          <Select id="edit-lesson-teacher" value={teacherId} onChange={(e) => setTeacherId(e.target.value)}>
            <option value={lesson.teacherId}>{lesson.teacherName ?? "Joriy o'qituvchi"}</option>
            {substitutesQuery.data?.filter((s) => s.userId !== lesson.teacherId).map((s) => (
              <option key={s.userId} value={s.userId}>{s.fullName}</option>
            ))}
          </Select>
          <p className="mt-1 text-[12px] text-ink-500">Shu vaqtda band bo&apos;lmagan filial o&apos;qituvchilari ko&apos;rsatiladi.</p>
        </div>
        <div>
          <Label htmlFor="edit-lesson-topic">Mavzu</Label>
          <Input id="edit-lesson-topic" maxLength={200} value={topic} onChange={(e) => setTopic(e.target.value)} />
        </div>

        <ConflictNotice conflicts={conflicts} />
        {mutation.isError && <Alert>{errorText(mutation.error, "Saqlab bo'lmadi.")}</Alert>}
        {warnings && (
          <Alert tone="info">
            <p className="font-medium">Dars saqlandi. Diqqat:</p>
            <ul className="mt-1 list-disc pl-4">{warnings.map((w) => <li key={w}>{w}</li>)}</ul>
          </Alert>
        )}

        {warnings ? (
          <Button type="button" onClick={onClose} className="w-full">Yopish</Button>
        ) : (
          <Button type="submit" loading={mutation.isPending} disabled={Object.keys(changes).length === 0} className="w-full">
            Saqlash
          </Button>
        )}
      </form>
    </Modal>
  );
}

/**
 * Cancels one lesson, or several at once (a holiday, a closed branch — all or nothing on the server). The reason is required and
 * goes to the students, their parents and the teachers. The lessons stay on the schedule, marked, with what was recorded.
 */
export function CancelLessonModal({ lessons, onClose, onDone }: { lessons: Lesson[]; onClose: () => void; onDone?: () => void }) {
  const queryClient = useQueryClient();
  const [reason, setReason] = useState("");

  const mutation = useMutation({
    mutationFn: async () => {
      if (lessons.length === 1) await cancelLesson(lessons[0].id, reason.trim());
      else await bulkCancelLessons(lessons.map((l) => l.id), reason.trim());
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["lessons"] });
      await queryClient.invalidateQueries({ queryKey: ["groups"] });
      onDone?.();
      onClose();
    },
  });

  const many = lessons.length > 1;
  return (
    <Modal
      open onClose={onClose}
      title={many ? `${lessons.length} ta darsni bekor qilish` : "Darsni bekor qilish"}
      description="Talabalar, ota-onalar va o'qituvchilarga sabab bilan xabar yuboriladi. Davomat va vazifalar saqlanadi."
    >
      <form
        className="space-y-4"
        onSubmit={(e) => {
          e.preventDefault();
          mutation.mutate();
        }}
      >
        <div>
          <Label htmlFor="cancel-lesson-reason">Sabab</Label>
          <Input
            id="cancel-lesson-reason" required autoFocus maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)}
            placeholder="Masalan: bayram kuni, o'qituvchi kasal"
          />
        </div>
        {mutation.isError && <Alert>{errorText(mutation.error, "Bekor qilib bo'lmadi.")}</Alert>}
        <div className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
          <Button type="button" variant="secondary" onClick={onClose} disabled={mutation.isPending}>Orqaga</Button>
          <Button type="submit" variant="danger" loading={mutation.isPending} disabled={reason.trim().length === 0}>
            {many ? "Hammasini bekor qilish" : "Darsni bekor qilish"}
          </Button>
        </div>
      </form>
    </Modal>
  );
}

/** "Tahrirlash" and "Bekor qilish" for one lesson, for whoever holds `schedule.edit`. Nothing for a cancelled lesson. */
export function LessonAdminActions({ lesson }: { lesson: Lesson }) {
  const { can } = usePermissions();
  const [editing, setEditing] = useState(false);
  const [cancelling, setCancelling] = useState(false);
  if (!can("schedule.edit") || lesson.status === "Cancelled") return null;

  return (
    <>
      <Button size="sm" variant="secondary" onClick={() => setEditing(true)}>
        <Pencil className="size-3.5" aria-hidden />
        Tahrirlash
      </Button>
      <Button size="sm" variant="secondary" onClick={() => setCancelling(true)}>
        <Ban className="size-3.5" aria-hidden />
        Bekor qilish
      </Button>
      {editing && <EditLessonModal lesson={lesson} onClose={() => setEditing(false)} />}
      {cancelling && <CancelLessonModal lessons={[lesson]} onClose={() => setCancelling(false)} />}
    </>
  );
}
