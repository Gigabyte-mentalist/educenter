"use client";

import { use, useState } from "react";
import Link from "next/link";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ArrowLeft, PhoneCall, Sparkles, GraduationCap, UserCheck, PhoneOutgoing, CalendarCheck,
  Award, StickyNote, Send, Globe, Phone as PhoneIcon, DoorOpen, XCircle, Clock,
} from "lucide-react";
import {
  getLead, listCalls, addLeadNote, updateLeadStatus, listTrialLessons, listPlacementAttempts,
  preferredTimeLabel, CALL_OUTCOME_LABEL, type Lead, type LeadStatus,
} from "@/lib/api/crm";
import { usePermissions } from "@/lib/use-permissions";
import { listBranches } from "@/lib/api/branches";
import { ApiError } from "@/lib/api-client";
import { Avatar } from "@/components/ui/Avatar";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { Card, CardBody, CardHeader, CardTitle } from "@/components/ui/Card";
import { Tabs } from "@/components/ui/Tabs";
import { Skeleton } from "@/components/ui/Skeleton";
import { Alert } from "@/components/ui/Alert";
import { EmptyState } from "@/components/ui/EmptyState";
import { Input, Label } from "@/components/ui/Field";
import { LogCallDrawer } from "@/components/crm/LogCallDrawer";
import { CloseLeadDrawer } from "@/components/crm/CloseLeadDrawer";
import { ConvertLeadDrawer } from "@/components/crm/ConvertLeadDrawer";
import { LeadPreferencesDrawer } from "@/components/crm/LeadPreferencesDrawer";
import { TrialDrawer } from "@/components/TrialDrawer";
import { PlacementTestDrawer } from "@/components/PlacementTestDrawer";
import { formatDateTime } from "@/lib/format-date";

const STATUS_LABEL: Record<LeadStatus, string> = {
  Lead: "Yangi", Contacted: "Bog'lanildi", TrialBooked: "Trial band qilindi", TrialAttended: "Trial'da qatnashdi",
  TrialMissed: "Trial'ni o'tkazib yubordi", NotInterested: "Qiziqmadi", Lost: "Yo'qotildi", Converted: "Studentga aylandi",
};
const STATUS_TONE: Record<LeadStatus, "neutral" | "brand" | "success" | "warning" | "danger"> = {
  Lead: "neutral", Contacted: "brand", TrialBooked: "brand", TrialAttended: "success",
  TrialMissed: "warning", NotInterested: "danger", Lost: "danger", Converted: "success",
};
// Forward-moving stages the dropdown lets you pick directly; the terminal ones go through
// CloseLeadDrawer (docs/kamchiliklar.docx §5) since Lost needs a reason and Converted needs /convert.
const FORWARD_STATUSES: LeadStatus[] = ["Lead", "Contacted", "TrialBooked", "TrialAttended"];
const SOURCE_ICON: Record<string, typeof Globe> = { Instagram: Sparkles, Telegram: Send, Website: Globe, Call: PhoneIcon, Referral: UserCheck, "Walk-in": DoorOpen };

type ActivityItem = { at: string; icon: typeof PhoneOutgoing; label: string; detail?: string };

/**
 * "What do I do with this person now?" — the pipeline is five stages and nothing on the screen said which button belongs to
 * which one, so a lead that attended its trial just sat there. Each stage names its next step and offers exactly that action.
 */
function NextStep({
  lead, onCall, onTrial, onConvert, onReopen, busy,
}: {
  lead: Lead; onCall: () => void; onTrial: () => void; onConvert: () => void; onReopen: () => void; busy: boolean;
}) {
  const { can } = usePermissions();
  const trial = lead.latestTrial;
  const trialLine = trial ? `${trial.groupName}, ${formatDateTime(trial.scheduledAt)}` : "";

  let title = "";
  let text = "";
  const actions: React.ReactNode[] = [];
  const call = can("crm.call.log") && <Button key="call" variant="secondary" size="sm" onClick={onCall}><PhoneCall className="size-3.5" aria-hidden />Qo&apos;ng&apos;iroq qilish</Button>;
  const book = (label: string, primary = true) => can("trial.book") && (
    <Button key="trial" size="sm" variant={primary ? "primary" : "secondary"} onClick={onTrial}><CalendarCheck className="size-3.5" aria-hidden />{label}</Button>
  );
  const convert = (primary: boolean) => can("students.create") && (
    <Button key="convert" size="sm" variant={primary ? "primary" : "secondary"} onClick={onConvert}><UserCheck className="size-3.5" aria-hidden />Studentga aylantirish</Button>
  );

  switch (lead.status) {
    case "Lead":
      title = "1-qadam: mijoz bilan bog'laning";
      text = "Qo'ng'iroq qiling va natijasini yozing. Qiziqsa, mos guruhni tanlab trial darsga yozasiz.";
      actions.push(call, book("Trialga yozish", false));
      break;
    case "Contacted":
      title = "2-qadam: trial darsga yozing";
      text = "Mos guruh va dars vaqtini tanlang — mijoz o'sha darsga sinov uchun keladi. Trial'siz o'qishga qabul qilish ham mumkin.";
      actions.push(book("Trialga yozish"), convert(false), call);
      break;
    case "TrialBooked":
      title = "3-qadam: mijoz trial'ga kelishini kuting";
      text = `Trial: ${trialLine}. Mijoz kelgach «Keldi», kelmasa «Kelmadi» deb belgilang.`;
      actions.push(book("Keldi / Kelmadi belgilash"), call);
      break;
    case "TrialAttended":
      title = "4-qadam: studentga aylantiring";
      text = `Trial'ga keldi${trial ? ` (${trial.groupName})` : ""}. Guruh tanlanadi, login akkaunt yaratiladi va birinchi oylik to'lov hisobi ochiladi.`;
      actions.push(convert(true), call);
      break;
    case "TrialMissed":
      title = "Trial'ga kelmadi";
      text = "Qo'ng'iroq qilib sababini bilib oling: yangi trial belgilang yoki lidni yoping.";
      actions.push(book("Yangi trial belgilash"), call);
      break;
    case "NotInterested":
    case "Lost":
      title = "Lid yopilgan";
      text = lead.lostReason ? `Sabab: ${lead.lostReason}.` : "Mijoz o'qishni rejalashtirmayapti.";
      if (can("leads.edit")) actions.push(<Button key="reopen" variant="secondary" size="sm" disabled={busy} onClick={onReopen}>Qayta ochish</Button>);
      break;
    case "Converted":
      title = "Student bo'ldi";
      text = "Bu mijoz o'qishga qabul qilingan. Keyingi ishlar student profilida.";
      if (lead.convertedStudentId) actions.push(<Link key="student" href={`/students/${lead.convertedStudentId}`}><Button size="sm"><UserCheck className="size-3.5" aria-hidden />Student profilini ochish</Button></Link>);
      break;
  }

  return (
    <div className="mb-6 rounded-xl border border-brand-300 bg-brand-50 p-4">
      <p className="text-[14px] font-semibold text-ink-900">{title}</p>
      <p className="mt-1 text-[13px] leading-relaxed text-ink-600">{text}</p>
      {actions.filter(Boolean).length > 0 && <div className="mt-3 flex flex-wrap gap-2">{actions}</div>}
    </div>
  );
}

export default function LeadDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const queryClient = useQueryClient();

  const leadQuery = useQuery({ queryKey: ["lead", id], queryFn: () => getLead(id) });
  const callsQuery = useQuery({ queryKey: ["lead-calls", id], queryFn: () => listCalls(id) });
  const trialsQuery = useQuery({ queryKey: ["trials", id], queryFn: () => listTrialLessons(id) });
  const attemptsQuery = useQuery({ queryKey: ["placement-attempts", id], queryFn: () => listPlacementAttempts(id) });
  const branchesQuery = useQuery({ queryKey: ["branches"], queryFn: listBranches });

  const [callOpen, setCallOpen] = useState(false);
  const [closeOpen, setCloseOpen] = useState(false);
  const [convertOpen, setConvertOpen] = useState(false);
  const [convertGroupId, setConvertGroupId] = useState<string | undefined>(undefined);
  const [trialOpen, setTrialOpen] = useState(false);
  const [placementOpen, setPlacementOpen] = useState(false);
  const [prefsOpen, setPrefsOpen] = useState(false);
  const [noteText, setNoteText] = useState("");
  const { can } = usePermissions();

  const statusMutation = useMutation({
    mutationFn: (status: LeadStatus) => updateLeadStatus(id, status),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["lead", id] });
      queryClient.invalidateQueries({ queryKey: ["leads"] });
    },
  });

  const noteMutation = useMutation({
    mutationFn: () => addLeadNote(id, noteText),
    onSuccess: () => {
      setNoteText("");
      queryClient.invalidateQueries({ queryKey: ["lead", id] });
    },
  });

  if (leadQuery.isLoading) {
    return (
      <main className="p-4 sm:p-6 lg:p-8">
        <Skeleton className="h-24 mb-6" />
        <Skeleton className="h-64" />
      </main>
    );
  }

  if (leadQuery.isError || !leadQuery.data) {
    return (
      <main className="p-4 sm:p-6 lg:p-8">
        <Alert>{leadQuery.error instanceof ApiError ? leadQuery.error.message : "Lid topilmadi."}</Alert>
      </main>
    );
  }

  const lead = leadQuery.data;
  const converted = lead.status === "Converted";
  const branchName = branchesQuery.data?.find((b) => b.id === lead.branchId)?.name;
  const SourceIcon = SOURCE_ICON[lead.source] ?? Globe;
  const lastTestLevel = attemptsQuery.data?.[0]?.recommendedLevel ?? null;
  // The group the person just tried is the natural one to enrol into.
  const openConvert = (groupId?: string) => {
    setConvertGroupId(groupId ?? (lead.latestTrial?.attendanceStatus === "Attended" ? lead.latestTrial.groupId : undefined));
    setConvertOpen(true);
  };

  const activity: ActivityItem[] = [
    ...(callsQuery.data ?? []).map((c) => ({
      at: c.calledAt, icon: PhoneOutgoing,
      label: `Qo'ng'iroq — ${CALL_OUTCOME_LABEL[c.outcome]}`, detail: c.notes ?? undefined,
    })),
    ...(trialsQuery.data ?? []).map((t) => ({
      at: t.scheduledAt, icon: CalendarCheck,
      label: "Trial dars", detail: t.attendanceStatus === "Attended" ? "Keldi" : t.attendanceStatus === "Missed" ? "Kelmadi" : "Rejalashtirilgan",
    })),
    ...(attemptsQuery.data ?? []).map((a) => ({
      at: a.completedAt, icon: Award,
      label: "Daraja testi", detail: `${a.recommendedLevel} (${a.correctCount}/${a.totalQuestions})`,
    })),
  ].sort((a, b) => b.at.localeCompare(a.at));

  return (
    <main className="p-4 sm:p-6 lg:p-8">
      <Link href="/leads" className="mb-4 inline-flex items-center gap-1.5 text-[13px] text-ink-500 hover:text-ink-800">
        <ArrowLeft className="size-3.5" aria-hidden />
        Lidlar
      </Link>

      <div className="mb-6 flex flex-wrap items-start justify-between gap-4 rounded-xl border border-border bg-surface p-5">
        <div className="flex items-center gap-4">
          <Avatar name={lead.fullName} className="size-12 text-base" />
          <div>
            <div className="flex flex-wrap items-center gap-2">
              <h1 className="text-[18px] font-semibold text-ink-900">{lead.fullName}</h1>
              <Badge tone={STATUS_TONE[lead.status]} dot>{STATUS_LABEL[lead.status]}</Badge>
              {lead.priority === "High" && <Badge tone="danger">Muhim</Badge>}
            </div>
            <p className="mt-1 font-mono text-[13px] text-ink-500">{lead.phone}</p>
            <div className="mt-1.5 flex flex-wrap items-center gap-x-3 gap-y-1 text-[12.5px] text-ink-500">
              <span className="inline-flex items-center gap-1"><SourceIcon className="size-3.5" aria-hidden />{lead.source}</span>
              {lead.interestedCourse && <span>{lead.interestedCourse}</span>}
              {branchName && <span>{branchName}</span>}
            </div>
          </div>
        </div>

        {!converted && (
          <div className="flex flex-wrap gap-2">
            <Button variant="secondary" size="sm" onClick={() => setCallOpen(true)}>
              <PhoneCall className="size-3.5" aria-hidden />Qo&apos;ng&apos;iroq
            </Button>
            <Button variant="secondary" size="sm" onClick={() => setTrialOpen(true)}>Trial darslar</Button>
            <Button variant="secondary" size="sm" onClick={() => setPlacementOpen(true)}>
              <GraduationCap className="size-3.5" aria-hidden />Daraja testi
            </Button>
            <Button variant={lead.status === "TrialAttended" ? "primary" : "secondary"} size="sm" onClick={() => openConvert()}>
              <UserCheck className="size-3.5" aria-hidden />Studentga aylantirish
            </Button>
          </div>
        )}
      </div>

      <NextStep
        lead={lead}
        busy={statusMutation.isPending}
        onCall={() => setCallOpen(true)}
        onTrial={() => setTrialOpen(true)}
        onConvert={() => openConvert()}
        onReopen={() => statusMutation.mutate("Contacted")}
      />

      <div className="mb-6 flex flex-wrap items-center justify-between gap-3 rounded-xl border border-border bg-surface p-3">
        <dl className="flex flex-wrap gap-x-6 gap-y-1 text-[13px]">
          <div className="flex gap-1.5">
            <dt className="text-ink-500">Qulay vaqt:</dt>
            <dd className="font-medium text-ink-800">{preferredTimeLabel(lead.preferredTime)}</dd>
          </div>
          <div className="flex gap-1.5">
            <dt className="text-ink-500">Daraja:</dt>
            <dd className="font-medium text-ink-800">{lead.currentLevel ?? "Ko'rsatilmagan"}</dd>
          </div>
          {lastTestLevel && (
            <div className="flex gap-1.5">
              <dt className="text-ink-500">Oxirgi test:</dt>
              <dd className="font-medium text-ink-800">{lastTestLevel}</dd>
            </div>
          )}
        </dl>
        {!converted && can("leads.edit") && (
          <Button variant="secondary" size="sm" onClick={() => setPrefsOpen(true)}>
            <Clock className="size-3.5" aria-hidden />O&apos;zgartirish
          </Button>
        )}
      </div>

      {!converted && (
        <div className="mb-6 flex flex-wrap items-center gap-2 rounded-xl border border-border bg-surface p-3">
          <span className="text-[12.5px] font-medium text-ink-600">Bosqich:</span>
          {FORWARD_STATUSES.map((s) => (
            <Button
              key={s}
              size="sm"
              variant={lead.status === s ? "primary" : "secondary"}
              disabled={statusMutation.isPending}
              onClick={() => s !== lead.status && statusMutation.mutate(s)}
            >
              {STATUS_LABEL[s]}
            </Button>
          ))}
          <Button size="sm" variant="ghost" className="text-danger-600 hover:bg-danger-50" onClick={() => setCloseOpen(true)}>
            <XCircle className="size-3.5" aria-hidden />Yopish
          </Button>
          {statusMutation.isError && (
            <Alert>{statusMutation.error instanceof ApiError ? statusMutation.error.message : "O'zgartirib bo'lmadi."}</Alert>
          )}
        </div>
      )}

      <Tabs
        tabs={[
          {
            id: "activity",
            label: "Faoliyat",
            content: (
              <div className="space-y-3">
                {activity.length === 0 && (
                  <div className="rounded-xl border border-border bg-surface">
                    <EmptyState icon={PhoneOutgoing} title="Hali faoliyat yo'q" description="Qo'ng'iroq, trial yoki daraja testi shu yerda ko'rinadi." />
                  </div>
                )}
                {activity.map((item, i) => (
                  <div key={i} className="flex gap-3 rounded-lg border border-border bg-surface p-3">
                    <span className="flex size-8 shrink-0 items-center justify-center rounded-lg bg-brand-100 text-brand-700">
                      <item.icon className="size-4" aria-hidden />
                    </span>
                    <div className="min-w-0">
                      <p className="text-[13px] font-medium text-ink-900">{item.label}</p>
                      {item.detail && <p className="text-[12.5px] text-ink-500">{item.detail}</p>}
                      <p className="text-[11.5px] text-ink-400">{formatDateTime(item.at)}</p>
                    </div>
                  </div>
                ))}
              </div>
            ),
          },
          {
            id: "notes",
            label: "Izohlar",
            content: (
              <Card>
                <CardHeader><CardTitle>Izohlar</CardTitle></CardHeader>
                <CardBody className="space-y-4">
                  {lead.notes ? (
                    <div className="space-y-1.5">
                      {lead.notes.split("\n").map((line, i) => (
                        <p key={i} className="flex items-start gap-2 text-[13px] text-ink-700">
                          <StickyNote className="mt-0.5 size-3.5 shrink-0 text-ink-400" aria-hidden />
                          {line}
                        </p>
                      ))}
                    </div>
                  ) : (
                    <p className="text-[13px] text-ink-500">Hali izoh yo&apos;q.</p>
                  )}
                  {!converted && (
                    <form
                      className="flex gap-2 border-t border-border pt-4"
                      onSubmit={(e) => { e.preventDefault(); if (noteText.trim()) noteMutation.mutate(); }}
                    >
                      <div className="flex-1">
                        <Label htmlFor="new-note" className="sr-only">Yangi izoh</Label>
                        <Input id="new-note" value={noteText} onChange={(e) => setNoteText(e.target.value)} placeholder="Izoh yozing..." />
                      </div>
                      <Button type="submit" size="sm" loading={noteMutation.isPending}>Qo&apos;shish</Button>
                    </form>
                  )}
                  {noteMutation.isError && (
                    <Alert>{noteMutation.error instanceof ApiError ? noteMutation.error.message : "Saqlab bo'lmadi."}</Alert>
                  )}
                </CardBody>
              </Card>
            ),
          },
        ]}
      />

      {callOpen && <LogCallDrawer lead={lead} onClose={() => setCallOpen(false)} />}
      {closeOpen && <CloseLeadDrawer lead={lead} onClose={() => setCloseOpen(false)} />}
      {convertOpen && <ConvertLeadDrawer lead={lead} defaultGroupId={convertGroupId} onClose={() => setConvertOpen(false)} />}
      {trialOpen && (
        <TrialDrawer
          lead={lead}
          onClose={() => setTrialOpen(false)}
          onConvert={(groupId) => { setTrialOpen(false); openConvert(groupId); }}
        />
      )}
      {placementOpen && <PlacementTestDrawer lead={lead} onClose={() => setPlacementOpen(false)} />}
      {prefsOpen && <LeadPreferencesDrawer lead={lead} suggestedLevel={lastTestLevel} onClose={() => setPrefsOpen(false)} />}
    </main>
  );
}
