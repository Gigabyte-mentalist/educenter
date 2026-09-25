"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AlertTriangle, CalendarPlus, CalendarX2, Clock, DoorOpen, Plus, Trash2, UserRound, X } from "lucide-react";
import {
  generateLessons, getScheduleTemplate, saveScheduleTemplate, scheduleTemplateKey, SLOT_MAX_MINUTES, SLOT_MIN_MINUTES, WEEKDAYS, WEEKDAY_LABEL,
  type GenerationConflict, type GenerationResult, type ScheduleSlot, type Weekday,
} from "@/lib/api/group-schedule";
import { listUsers } from "@/lib/api/users";
import { formatDay } from "@/lib/api/finance";
import { formatTime } from "@/lib/format-date";
import { ApiError } from "@/lib/api-client";
import { cn } from "@/lib/cn";
import { RoomSelect, useActiveRooms } from "@/components/RoomSelect";
import { Alert } from "@/components/ui/Alert";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { Card, CardHeader, CardTitle } from "@/components/ui/Card";
import { Input, Label, Select } from "@/components/ui/Field";
import { Modal } from "@/components/ui/Modal";
import { Skeleton } from "@/components/ui/Skeleton";

const errorText = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

const CONFLICT_LABEL: Record<GenerationConflict["type"], string> = { ROOM: "Xona band", TEACHER: "O‘qituvchi band", GROUP: "Guruh band" };

function endTime(start: string, minutes: number): string {
  const [h, m] = start.split(":").map(Number);
  const total = h * 60 + m + minutes;
  return `${String(Math.floor(total / 60) % 24).padStart(2, "0")}:${String(total % 60).padStart(2, "0")}`;
}

/** The slots of one day overlap when one starts before the previous one ends. */
function overlapOn(slots: ScheduleSlot[], day: Weekday): boolean {
  const ordered = slots.filter((s) => s.dayOfWeek === day).sort((a, b) => a.startTime.localeCompare(b.startTime));
  return ordered.some((s, i) => i > 0 && s.startTime < endTime(ordered[i - 1].startTime, ordered[i - 1].durationMinutes));
}

const isoDay = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;

/**
 * A group's weekly template and the lessons made from it (docs/21.09-audit-jurnali S07.04): the week as a grid of slots written once,
 * then a period turned into real lessons — previewed first, every lesson checked for clashes, all made or none.
 */
export function GroupScheduleTab({ groupId, branchId, closed, canEdit, canGenerate }: {
  groupId: string; branchId: string; closed: boolean; canEdit: boolean; canGenerate: boolean;
}) {
  const template = useQuery({ queryKey: scheduleTemplateKey(groupId), queryFn: () => getScheduleTemplate(groupId) });

  if (template.isPending) return <Skeleton className="h-64" />;
  if (template.isError) return <Alert>{errorText(template.error, "Haftalik jadvalni yuklab bo‘lmadi.")}</Alert>;

  // Keyed on what is saved: after a save (or a refetch) the editor starts again from the server's copy.
  return (
    <TemplateEditor
      key={JSON.stringify(template.data.slots)} groupId={groupId} branchId={branchId} timezone={template.data.timezone}
      saved={template.data.slots} editable={canEdit && !closed} canGenerate={canGenerate && !closed}
    />
  );
}

function TemplateEditor({ groupId, branchId, timezone, saved, editable, canGenerate }: {
  groupId: string; branchId: string; timezone: string; saved: ScheduleSlot[]; editable: boolean; canGenerate: boolean;
}) {
  const queryClient = useQueryClient();
  const [slots, setSlots] = useState<ScheduleSlot[]>(saved);
  // `null`: the form is closed; a number edits that slot; "new" adds one (on the day it carries).
  const [form, setForm] = useState<{ index: number | "new"; day: Weekday } | null>(null);

  const dirty = JSON.stringify(slots) !== JSON.stringify(saved);
  const save = useMutation({
    mutationFn: () => saveScheduleTemplate(groupId, slots),
    onSuccess: (updated) => queryClient.setQueryData(scheduleTemplateKey(groupId), updated),
  });

  const ordered = (day: Weekday) =>
    slots.map((slot, index) => ({ slot, index })).filter((x) => x.slot.dayOfWeek === day).sort((a, b) => a.slot.startTime.localeCompare(b.slot.startTime));

  return (
    <div className="space-y-6">
      <Card>
        <CardHeader>
          <CardTitle>Haftalik jadval</CardTitle>
          <span className="flex items-center gap-1.5 text-[12.5px] text-ink-500"><Clock className="size-3.5" aria-hidden />Vaqtlar filial vaqtida ({timezone})</span>
        </CardHeader>
        <div className="grid gap-3 p-4 md:grid-cols-7">
          {WEEKDAYS.map((day) => {
            const here = ordered(day);
            return (
              // A day with nothing in it takes no room on a phone, where the grid is a list; on a wide screen every day keeps its column.
              <div key={day} className={cn("min-w-0 rounded-lg border border-border bg-ink-50/50 p-2", here.length === 0 && !editable && "hidden md:block")}>
                <p className="mb-2 px-1 text-[12px] font-semibold uppercase tracking-wide text-ink-500">{WEEKDAY_LABEL[day]}</p>
                <div className="space-y-1.5">
                  {here.map(({ slot, index }) => (
                    <div key={index} className={cn("rounded-md border bg-surface p-2 text-[12.5px]", overlapOn(slots, day) ? "border-danger-500" : "border-border")}>
                      <button
                        type="button" disabled={!editable} onClick={() => setForm({ index, day })}
                        className="block w-full text-left disabled:cursor-default" aria-label={`${WEEKDAY_LABEL[day]} ${slot.startTime}`}
                      >
                        <span className="font-tabular font-semibold text-brand-700">{slot.startTime}–{endTime(slot.startTime, slot.durationMinutes)}</span>
                        <span className="mt-0.5 flex items-center gap-1 text-ink-600"><DoorOpen className="size-3 shrink-0" aria-hidden />{slot.roomName ?? <span className="text-warning-600">xona yo‘q</span>}</span>
                        <span className="flex items-center gap-1 text-ink-500"><UserRound className="size-3 shrink-0" aria-hidden />{slot.teacherName ?? "Asosiy o‘qituvchi"}</span>
                      </button>
                      {editable && (
                        <button
                          type="button" onClick={() => setSlots(slots.filter((_, i) => i !== index))}
                          className="mt-1 flex items-center gap-1 text-[11.5px] text-danger-600 hover:underline" aria-label={`${WEEKDAY_LABEL[day]} ${slot.startTime} darsini olib tashlash`}
                        >
                          <Trash2 className="size-3" aria-hidden />Olib tashlash
                        </button>
                      )}
                    </div>
                  ))}
                  {editable && (
                    <button
                      type="button" onClick={() => setForm({ index: "new", day })}
                      className="flex w-full items-center justify-center gap-1 rounded-md border border-dashed border-border-strong py-1.5 text-[12px] text-ink-500 hover:bg-surface"
                      aria-label={`${WEEKDAY_LABEL[day]} kuniga dars qo‘shish`}
                    >
                      <Plus className="size-3.5" aria-hidden />Slot
                    </button>
                  )}
                </div>
              </div>
            );
          })}
        </div>

        {slots.length === 0 && <p className="px-5 pb-4 text-[13px] text-ink-500">Hali haftalik jadval yo‘q. Har hafta takrorlanadigan darslarni qo‘shing, so‘ng davr uchun darslarni yaratasiz.</p>}
        {WEEKDAYS.some((day) => overlapOn(slots, day)) && <div className="px-5 pb-4"><Alert>Bir kundagi darslar bir-biriga to‘g‘ri kelyapti — saqlashdan oldin tuzating.</Alert></div>}
        {save.isError && <div className="px-5 pb-4"><Alert>{errorText(save.error, "Saqlab bo‘lmadi.")}</Alert></div>}

        {editable && (
          <div className="flex flex-wrap items-center justify-end gap-2 border-t border-border px-5 py-3">
            {dirty && <span className="mr-auto text-[12.5px] text-warning-600">Saqlanmagan o‘zgarishlar bor</span>}
            {dirty && <Button variant="ghost" onClick={() => setSlots(saved)}>Bekor qilish</Button>}
            <Button loading={save.isPending} disabled={!dirty || WEEKDAYS.some((day) => overlapOn(slots, day))} onClick={() => save.mutate()}>Jadvalni saqlash</Button>
          </div>
        )}
      </Card>

      {canGenerate && <GeneratePanel groupId={groupId} hasSlots={saved.length > 0} dirty={dirty} />}

      {form && (
        <SlotForm
          key={String(form.index)} branchId={branchId} day={form.day} slot={form.index === "new" ? null : slots[form.index]}
          onClose={() => setForm(null)}
          onSave={(next) => {
            setSlots(form.index === "new" ? [...slots, next] : slots.map((s, i) => (i === form.index ? next : s)));
            setForm(null);
          }}
        />
      )}
    </div>
  );
}

function SlotForm({ branchId, day, slot, onClose, onSave }: {
  branchId: string; day: Weekday; slot: ScheduleSlot | null; onClose: () => void; onSave: (slot: ScheduleSlot) => void;
}) {
  const [dayOfWeek, setDayOfWeek] = useState<Weekday>(slot?.dayOfWeek ?? day);
  const [startTime, setStartTime] = useState(slot?.startTime ?? "18:00");
  const [minutes, setMinutes] = useState(String(slot?.durationMinutes ?? 90));
  const [roomId, setRoomId] = useState(slot?.roomId ?? "");
  const [teacherId, setTeacherId] = useState(slot?.teacherId ?? "");

  const teachers = useQuery({ queryKey: ["users", "TEACHER"], queryFn: () => listUsers({ role: "TEACHER", size: 200 }) });
  const rooms = useActiveRooms(branchId);
  const length = Number(minutes);
  const problem =
    !/^\d{2}:\d{2}$/.test(startTime) ? "Boshlanish vaqtini kiriting."
    : !Number.isInteger(length) || length < SLOT_MIN_MINUTES || length > SLOT_MAX_MINUTES ? `Davomiylik ${SLOT_MIN_MINUTES}–${SLOT_MAX_MINUTES} daqiqa bo‘lsin.`
    : startTime.split(":").map(Number).reduce((h, m) => h * 60 + m) + length > 24 * 60 ? "Dars yarim tundan keyin tugamasin."
    : null;

  return (
    <Modal open onClose={onClose} title={slot ? "Slotni tahrirlash" : "Yangi slot"} description="Har hafta shu kuni shu vaqtda takrorlanadi.">
      <form
        className="space-y-4"
        onSubmit={(e) => {
          e.preventDefault();
          if (problem) return;
          // The names are for the grid until the template is saved and comes back from the server with its own.
          const teacher = teachers.data?.items.find((t) => t.id === teacherId);
          const room = rooms.data?.items.find((r) => r.id === roomId);
          onSave({
            dayOfWeek, startTime, durationMinutes: length, roomId: roomId || null,
            roomName: room?.name ?? (roomId && roomId === slot?.roomId ? slot.roomName : null),
            teacherId: teacherId || null, teacherName: teacher?.fullName ?? (teacherId && teacherId === slot?.teacherId ? slot.teacherName : null),
          });
        }}
      >
        <div className="grid gap-4 sm:grid-cols-3">
          <div>
            <Label htmlFor="slot-day">Kun</Label>
            <Select id="slot-day" value={dayOfWeek} onChange={(e) => setDayOfWeek(e.target.value as Weekday)}>
              {WEEKDAYS.map((d) => <option key={d} value={d}>{WEEKDAY_LABEL[d]}</option>)}
            </Select>
          </div>
          <div>
            <Label htmlFor="slot-start">Boshlanishi</Label>
            <Input id="slot-start" type="time" required value={startTime} onChange={(e) => setStartTime(e.target.value)} />
          </div>
          <div>
            <Label htmlFor="slot-minutes">Davomiyligi (daqiqa)</Label>
            <Input id="slot-minutes" type="number" required min={SLOT_MIN_MINUTES} max={SLOT_MAX_MINUTES} step={5} value={minutes} onChange={(e) => setMinutes(e.target.value)} />
          </div>
        </div>
        <div>
          <Label htmlFor="slot-room">Xona</Label>
          <RoomSelect id="slot-room" branchId={branchId} value={roomId} onChange={setRoomId} current={slot?.roomId ? { id: slot.roomId, name: slot.roomName ?? "Joriy xona" } : undefined} />
          <p className="mt-1.5 text-[12.5px] text-ink-500">Xonasiz jadvalni saqlash mumkin, lekin dars yaratish uchun xona kerak.</p>
        </div>
        <div>
          <Label htmlFor="slot-teacher">O‘qituvchi</Label>
          <Select id="slot-teacher" value={teacherId} onChange={(e) => setTeacherId(e.target.value)}>
            <option value="">Guruhning asosiy o‘qituvchisi</option>
            {slot?.teacherId && !teachers.data?.items.some((t) => t.id === slot.teacherId) && <option value={slot.teacherId}>{slot.teacherName ?? "Joriy o‘qituvchi"}</option>}
            {teachers.data?.items.map((t) => <option key={t.id} value={t.id}>{t.fullName}</option>)}
          </Select>
        </div>
        {problem && <p className="text-[13px] text-danger-600">{problem}</p>}
        <Button type="submit" disabled={problem !== null} className="w-full">{slot ? "Slotni yangilash" : "Slotni qo‘shish"}</Button>
      </form>
    </Modal>
  );
}

function GeneratePanel({ groupId, hasSlots, dirty }: { groupId: string; hasSlots: boolean; dirty: boolean }) {
  const queryClient = useQueryClient();
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [skip, setSkip] = useState<string[]>([]);
  const [skipInput, setSkipInput] = useState("");
  const [shown, setShown] = useState<GenerationResult | null>(null);
  const [done, setDone] = useState<GenerationResult | null>(null);

  // A preview describes one request; changing the period or the holidays makes it stale.
  const reset = () => { setShown(null); setDone(null); };
  const preset = (days: number) => {
    const start = new Date();
    start.setDate(start.getDate() + 1);
    const end = new Date(start);
    end.setDate(end.getDate() + days - 1);
    setFrom(isoDay(start));
    setTo(isoDay(end));
    reset();
  };

  const input = { from, to, skipDates: skip };
  const preview = useMutation({
    mutationFn: () => generateLessons(groupId, input, true),
    onSuccess: (r) => { setDone(null); setShown(r.ok ? r.result : null); },
  });
  const create = useMutation({
    mutationFn: () => generateLessons(groupId, input, false),
    onSuccess: async (r) => {
      if (r.ok) {
        setShown(null);
        setDone(r.result);
        await Promise.all([queryClient.invalidateQueries({ queryKey: ["lessons"] }), queryClient.invalidateQueries({ queryKey: ["groups", groupId] })]);
      } else {
        // Refused as a whole: the clashes are shown against the same list a preview gives.
        setShown((current) => (current ? { ...current, conflicts: r.conflicts } : { preview: true, created: 0, skipped: 0, lessons: [], conflicts: r.conflicts, warnings: [] }));
      }
    },
  });

  const ready = from !== "" && to !== "" && from <= to;
  const blocked = !hasSlots ? "Avval haftalik jadvalni saqlang." : dirty ? "Avval jadvaldagi o‘zgarishlarni saqlang — darslar saqlangan jadvaldan yaratiladi." : null;

  return (
    <Card>
      <CardHeader>
        <CardTitle>Darslarni yaratish</CardTitle>
        <span className="text-[12.5px] text-ink-500">Bir safar 6 oygacha</span>
      </CardHeader>
      <div className="space-y-4 p-5">
        {blocked && <Alert tone="info">{blocked}</Alert>}

        <div className="grid gap-4 sm:grid-cols-2">
          <div>
            <Label htmlFor="gen-from">Boshlanish sanasi</Label>
            <Input id="gen-from" type="date" value={from} onChange={(e) => { setFrom(e.target.value); reset(); }} />
          </div>
          <div>
            <Label htmlFor="gen-to">Tugash sanasi</Label>
            <Input id="gen-to" type="date" value={to} min={from || undefined} onChange={(e) => { setTo(e.target.value); reset(); }} />
          </div>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button size="sm" variant="secondary" onClick={() => preset(28)}>Keyingi 4 hafta</Button>
          <Button size="sm" variant="secondary" onClick={() => preset(91)}>Keyingi 3 oy</Button>
        </div>

        <div>
          <Label htmlFor="gen-skip">Dars bo‘lmaydigan kunlar (bayram)</Label>
          <div className="flex flex-wrap items-center gap-2">
            <Input id="gen-skip" type="date" value={skipInput} onChange={(e) => setSkipInput(e.target.value)} className="w-48" />
            <Button
              size="sm" variant="secondary" disabled={!skipInput || skip.includes(skipInput)}
              onClick={() => { setSkip([...skip, skipInput].sort()); setSkipInput(""); reset(); }}
            >
              <CalendarX2 className="size-3.5" aria-hidden />Qo‘shish
            </Button>
          </div>
          {skip.length > 0 && (
            <div className="mt-2 flex flex-wrap gap-1.5">
              {skip.map((day) => (
                <Badge key={day} tone="neutral">
                  {formatDay(day)}
                  <button type="button" onClick={() => { setSkip(skip.filter((d) => d !== day)); reset(); }} aria-label={`${formatDay(day)} kunini olib tashlash`}><X className="size-3" aria-hidden /></button>
                </Badge>
              ))}
            </div>
          )}
        </div>

        {(preview.isError || create.isError) && <Alert>{errorText(preview.error ?? create.error, "Darslarni yaratib bo‘lmadi.")}</Alert>}

        {done && (
          <Alert tone="success">
            {done.created} ta dars yaratildi{done.skipped > 0 ? `, ${done.skipped} tasi allaqachon bor edi (o‘tkazib yuborildi)` : ""}. Guruh o‘quvchilariga bitta xabar yuborildi.
            {done.warnings.map((w) => <span key={w} className="mt-1 block">{w}</span>)}
          </Alert>
        )}

        {shown && <PreviewList result={shown} />}

        <div className="flex flex-wrap justify-end gap-2">
          <Button variant="secondary" loading={preview.isPending} disabled={!ready || blocked !== null} onClick={() => preview.mutate()}>Oldindan ko‘rish</Button>
          <Button
            loading={create.isPending} disabled={!ready || blocked !== null || shown === null || shown.conflicts.length > 0 || shown.lessons.length === 0}
            onClick={() => create.mutate()}
          >
            <CalendarPlus className="size-4" aria-hidden />
            {shown && shown.lessons.length > 0 ? `${shown.lessons.length} ta darsni yaratish` : "Darslarni yaratish"}
          </Button>
        </div>
      </div>
    </Card>
  );
}

function PreviewList({ result }: { result: GenerationResult }) {
  const clashing = new Set(result.conflicts.map((c) => c.date));
  return (
    <div className="space-y-3">
      {result.conflicts.length > 0 && (
        <div className="rounded-lg border border-warning-500/30 bg-warning-50 p-3">
          <p className="flex items-center gap-1.5 text-[13px] font-medium text-warning-600"><AlertTriangle className="size-4" aria-hidden />To‘qnashuv bor — hech qaysi dars yaratilmaydi</p>
          <ul className="mt-2 space-y-1 text-[12.5px] text-warning-600">
            {result.conflicts.map((c, i) => <li key={i}>{formatDay(c.date)} — {CONFLICT_LABEL[c.type]} ({formatTime(c.startsAt)}–{formatTime(c.endsAt)})</li>)}
          </ul>
          <p className="mt-2 text-[12px] text-warning-600">Bu kunni &quot;bayram&quot; qatoriga qo‘shing yoki jadvaldagi xona/o‘qituvchini o‘zgartiring.</p>
        </div>
      )}
      {result.warnings.map((w) => <Alert key={w} tone="info">{w}</Alert>)}
      <p className="text-[13px] text-ink-600">
        <span className="font-medium text-ink-900">{result.lessons.length} ta yangi dars</span>
        {result.skipped > 0 && <span> · {result.skipped} tasi allaqachon bor (o‘tkazib yuboriladi)</span>}
      </p>
      {result.lessons.length > 0 && (
        <ul className="max-h-64 divide-y divide-border overflow-y-auto rounded-lg border border-border text-[13px]">
          {result.lessons.map((l) => (
            <li key={l.startsAt} className={cn("flex flex-wrap items-baseline gap-x-3 px-3 py-2", clashing.has(l.date) && "bg-warning-50")}>
              <span className="w-28 font-medium text-ink-900">{formatDay(l.date)}</span>
              <span className="font-tabular text-brand-700">{formatTime(l.startsAt)}–{formatTime(l.endsAt)}</span>
              <span className="text-ink-600">{l.roomName}</span>
              <span className="text-ink-500">{l.teacherName ?? "—"}</span>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
