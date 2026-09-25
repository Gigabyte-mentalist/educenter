"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CheckCircle2, DoorOpen, UserCheck, UserRound } from "lucide-react";
import { listGroups } from "@/lib/api/academic";
import { bookTrial, listTrialLessons, markTrialAttendance, getGroupRecommendations, type Lead } from "@/lib/api/crm";
import { ApiError } from "@/lib/api-client";
import { cn } from "@/lib/cn";
import { Drawer } from "@/components/ui/Drawer";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { Alert } from "@/components/ui/Alert";
import { Input, Label, Select } from "@/components/ui/Field";
import { formatDayMonth, formatTime, formatWeekday } from "@/lib/format-date";

const labels: Record<string, string> = { Scheduled: "Rejalashtirilgan", Attended: "Keldi", Missed: "Kelmadi" };

/** "Pay, 24-sentabr · 16:00" — a lesson as an operator reads it out loud on the phone. */
const slotLabel = (iso: string) => `${formatWeekday(iso).slice(0, 3)}, ${formatDayMonth(iso)} · ${formatTime(iso)}`;

/**
 * A trial is a visit to one real lesson of one real group, so booking is two choices: which group (with who teaches it and
 * how many seats are free — that is what tells an operator whether it suits the person) and which of its coming lessons.
 * Marking "Keldi" leads straight to enrolment: the trial's group is offered as the group to enrol into.
 */
export function TrialDrawer({
  lead, onClose, onConvert,
}: { lead: Lead; onClose: () => void; onConvert?: (groupId: string) => void }) {
  const client = useQueryClient();
  const [groupId, setGroupId] = useState("");
  const [slot, setSlot] = useState("");
  const [manual, setManual] = useState(false);
  const [custom, setCustom] = useState("");
  const groups = useQuery({ queryKey: ["groups"], queryFn: listGroups });
  const trials = useQuery({ queryKey: ["trials", lead.id], queryFn: () => listTrialLessons(lead.id) });
  const recommendations = useQuery({ queryKey: ["group-recommendations", lead.id], queryFn: () => getGroupRecommendations(lead.id) });
  const converted = lead.convertedStudentId !== null || lead.status === "Converted";

  const chosen = recommendations.data?.find((r) => r.groupId === groupId);
  const lessons = chosen?.nextLessons ?? [];
  // A group outside the ranked list has no lesson slots to offer, so it is booked by typing the time.
  const typing = manual || (groupId !== "" && lessons.length === 0);
  const scheduledIso = typing ? (custom ? new Date(custom).toISOString() : "") : slot;

  const refresh = async () => {
    await Promise.all([
      client.invalidateQueries({ queryKey: ["trials", lead.id] }),
      client.invalidateQueries({ queryKey: ["leads"] }),
      client.invalidateQueries({ queryKey: ["lead", lead.id] }),
    ]);
  };
  const booking = useMutation({
    mutationFn: () => bookTrial(lead.id, groupId, scheduledIso),
    onSuccess: async () => { setSlot(""); setCustom(""); setGroupId(""); setManual(false); await refresh(); },
  });
  const attendance = useMutation({
    mutationFn: ({ id, attended }: { id: string; attended: boolean }) => markTrialAttendance(id, attended),
    onSuccess: refresh,
  });
  const error = trials.error ?? groups.error ?? booking.error ?? attendance.error;
  const busy = booking.isPending || attendance.isPending;

  function pickGroup(id: string) {
    setGroupId(id);
    setSlot("");
    setManual(false);
  }

  return (
    <Drawer open onClose={onClose} title="Trial darslar" description={lead.fullName}>
      <div className="space-y-5">
        <p className="rounded-xl bg-ink-50 p-3 text-[12.5px] leading-relaxed text-ink-600">
          Trial — mijoz tanlangan guruhning bitta darsiga sinov uchun keladi. U kelgach <strong>«Keldi»</strong> deb belgilang,
          so&apos;ng uni studentga aylantirasiz.
        </p>

        {error && <Alert>{error instanceof ApiError ? error.message : "Amalni bajarib bo‘lmadi. Qayta urinib ko‘ring."}</Alert>}
        {trials.isLoading && <p>Trial darslar yuklanmoqda...</p>}
        {trials.data?.length === 0 && <p className="text-sm text-ink-500">Hali trial dars belgilanmagan.</p>}
        {trials.data?.map((trial) => (
          <div key={trial.id} className="space-y-2 rounded-lg border border-border p-3">
            <div className="flex items-start justify-between gap-2">
              <p className="font-medium">{groups.data?.find(g => g.id === trial.groupId)?.name ?? "Guruh"}</p>
              <Badge tone={trial.attendanceStatus === "Attended" ? "success" : trial.attendanceStatus === "Missed" ? "warning" : "brand"}>
                {labels[trial.attendanceStatus] ?? trial.attendanceStatus}
              </Badge>
            </div>
            <p className="text-sm text-ink-600">{formatWeekday(trial.scheduledAt)}, {formatDayMonth(trial.scheduledAt)} · {formatTime(trial.scheduledAt)}</p>
            {!converted && (
              <div className="flex flex-wrap gap-2">
                <Button size="sm" disabled={busy || trial.attendanceStatus === "Attended"} onClick={() => attendance.mutate({ id: trial.id, attended: true })}>Keldi</Button>
                <Button size="sm" variant="secondary" disabled={busy || trial.attendanceStatus === "Missed"} onClick={() => attendance.mutate({ id: trial.id, attended: false })}>Kelmadi</Button>
              </div>
            )}
            {!converted && trial.attendanceStatus === "Attended" && onConvert && (
              <div className="flex items-center justify-between gap-3 rounded-lg bg-success-50 p-3">
                <p className="flex items-center gap-1.5 text-[12.5px] text-ink-700"><CheckCircle2 className="size-4 text-success-600" aria-hidden />Trial&apos;ga keldi</p>
                <Button size="sm" onClick={() => onConvert(trial.groupId)}>
                  <UserCheck className="size-3.5" aria-hidden />Studentga aylantirish
                </Button>
              </div>
            )}
          </div>
        ))}

        {!converted && (
          <form className="space-y-4 border-t border-border pt-4" onSubmit={e => { e.preventDefault(); if (scheduledIso) booking.mutate(); }}>
            <p className="text-[13px] font-medium text-ink-900">Yangi trial</p>

            <fieldset>
              <legend className="mb-1.5 block text-[13px] font-medium text-ink-700">Qaysi guruhga</legend>
              {recommendations.isPending && <p className="text-[12.5px] text-ink-500">Mos guruhlar qidirilmoqda...</p>}
              {recommendations.data?.length === 0 && (
                <p className="text-[12.5px] text-ink-500">Bo&apos;sh o&apos;rni bor guruh topilmadi. Quyidan boshqa guruhni tanlang.</p>
              )}
              <div className="max-h-72 space-y-2 overflow-y-auto pr-1">
                {recommendations.data?.map((r) => (
                  <label
                    key={r.groupId}
                    className={cn(
                      "block cursor-pointer rounded-lg border p-3 transition-colors",
                      groupId === r.groupId ? "border-brand-400 bg-brand-50" : "border-border hover:border-brand-300",
                    )}
                  >
                    <input type="radio" name="trial-group" className="sr-only" checked={groupId === r.groupId} onChange={() => pickGroup(r.groupId)} />
                    <div className="flex items-start justify-between gap-2">
                      <div className="min-w-0">
                        <p className="text-[13.5px] font-medium text-ink-900">{r.groupName}</p>
                        <p className="text-[12px] text-ink-500">{r.courseName}</p>
                      </div>
                      <Badge tone={r.matchPercent >= 70 ? "success" : "neutral"}>{r.matchPercent}% mos</Badge>
                    </div>
                    <div className="mt-1.5 flex flex-wrap items-center gap-x-3 gap-y-0.5 text-[12px] text-ink-600">
                      {r.teacherName && <span className="inline-flex items-center gap-1"><UserRound className="size-3" aria-hidden />{r.teacherName}</span>}
                      <span>{r.freeSeats} ta bo&apos;sh o&apos;rin</span>
                      {r.nextLessons[0] && <span className="inline-flex items-center gap-1"><DoorOpen className="size-3" aria-hidden />{r.nextLessons[0].roomName}</span>}
                    </div>
                    {/* The seat count is already shown above; the rest of the reasons say why the group ranks where it does. */}
                    {r.reasons.filter((reason) => !reason.includes("o'rin")).length > 0 && (
                      <p className="mt-1 text-[11.5px] text-ink-500">{r.reasons.filter((reason) => !reason.includes("o'rin")).join(" · ")}</p>
                    )}
                  </label>
                ))}
              </div>
              <div className="mt-3">
                <Label htmlFor="trial-group">Yoki boshqa guruh</Label>
                <Select
                  id="trial-group"
                  value={recommendations.data?.some((r) => r.groupId === groupId) ? "" : groupId}
                  onChange={e => pickGroup(e.target.value)}
                >
                  <option value="">Tanlang</option>
                  {groups.data
                    ?.filter(g => (g.status === "Active" || g.status === "Forming") && !recommendations.data?.some(r => r.groupId === g.id))
                    .map(g => <option key={g.id} value={g.id}>{g.name}</option>)}
                </Select>
              </div>
            </fieldset>

            {groupId && (
              <div>
                <p className="mb-1.5 text-[13px] font-medium text-ink-700">Qaysi darsga</p>
                {!typing && (
                  <div className="flex flex-wrap gap-2">
                    {lessons.map((l) => (
                      <button
                        key={l.id}
                        type="button"
                        onClick={() => setSlot(l.startsAt)}
                        className={cn(
                          "rounded-lg border px-3 py-1.5 text-[12.5px] transition-colors",
                          slot === l.startsAt ? "border-brand-400 bg-brand-50 font-medium text-brand-800" : "border-border text-ink-700 hover:border-brand-300",
                        )}
                      >
                        {slotLabel(l.startsAt)}
                      </button>
                    ))}
                  </div>
                )}
                {typing && (
                  <div>
                    <Input id="trial-time" type="datetime-local" required value={custom} onChange={e => setCustom(e.target.value)} aria-label="Dars sanasi va vaqti" />
                    <p className="mt-1 text-xs text-ink-500">
                      {lessons.length === 0 ? "Bu guruhning rejalashtirilgan darsi ko'rinmayapti — vaqtni o'zingiz kiriting. " : ""}
                      Vaqt qurilmangizning mahalliy vaqt zonasida kiritiladi.
                    </p>
                  </div>
                )}
                {lessons.length > 0 && (
                  <button type="button" className="mt-2 text-[12px] text-brand-700 hover:underline" onClick={() => setManual((m) => !m)}>
                    {manual ? "Guruh darslaridan tanlash" : "Boshqa vaqt kiritish"}
                  </button>
                )}
              </div>
            )}

            <Button type="submit" className="w-full" loading={booking.isPending} disabled={busy || !groupId || !scheduledIso || groups.isPending || groups.isError}>
              Trialga yozish
            </Button>
          </form>
        )}
      </div>
    </Drawer>
  );
}
