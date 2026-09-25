"use client";

import { useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Ban, CheckCircle2, Headphones, Mic, Square, Timer, Volume2 } from "lucide-react";
import {
  abandonSpeakingSession, answerSpeakingTurn, finishSpeakingSession, getAiStatus, getSpeakingSession,
  listSpeakingSessions, startSpeakingSession,
  SPEAKING_MODE_HINT, SPEAKING_MODE_LABEL, SPEAKING_PREPARATION_SECONDS,
  type SpeakingMode, type SpeakingSession, type SpeakingStatus,
} from "@/lib/api/ai";
import { useAutoRead, useQuestionVoice } from "@/lib/use-speaking-voice";
import { ApiError } from "@/lib/api-client";
import { PageHeader } from "@/components/ui/PageHeader";
import { Alert } from "@/components/ui/Alert";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { Card, CardBody, CardHeader, CardTitle } from "@/components/ui/Card";
import { EmptyState } from "@/components/ui/EmptyState";
import { Skeleton } from "@/components/ui/Skeleton";
import { SpeakingReportCard, SpeakingTranscriptCard, formatBand, formatDuration } from "@/components/ai/SpeakingSessionView";
import { cn } from "@/lib/cn";
import { formatShortDate } from "@/lib/format-date";

const MODES: SpeakingMode[] = ["IeltsPart1", "IeltsPart2", "IeltsPart3", "FullMock", "GeneralEnglish", "FreeConversation"];

const STATUS_LABEL: Record<SpeakingStatus, string> = {
  Active: "Davom etmoqda",
  Completed: "Yakunlandi",
  Failed: "Baholanmadi",
  Abandoned: "To'xtatilgan",
};

const errorText = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

/** Microphone capture. The browser records; only the finished clip is uploaded. */
function useRecorder() {
  const [recording, setRecording] = useState(false);
  const [seconds, setSeconds] = useState(0);
  const recorderRef = useRef<MediaRecorder | null>(null);
  const chunksRef = useRef<Blob[]>([]);
  const timerRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const startedAtRef = useRef(0);

  const start = async () => {
    const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
    const recorder = new MediaRecorder(stream);
    chunksRef.current = [];
    recorder.ondataavailable = (event) => { if (event.data.size > 0) chunksRef.current.push(event.data); };
    recorder.start();

    recorderRef.current = recorder;
    startedAtRef.current = Date.now();
    setSeconds(0);
    setRecording(true);
    timerRef.current = setInterval(() => setSeconds(Math.round((Date.now() - startedAtRef.current) / 1000)), 500);
  };

  const stop = () =>
    new Promise<{ blob: Blob; seconds: number } | null>((resolve) => {
      const recorder = recorderRef.current;
      if (!recorder) return resolve(null);

      recorder.onstop = () => {
        recorder.stream.getTracks().forEach((track) => track.stop());
        if (timerRef.current) clearInterval(timerRef.current);
        setRecording(false);
        resolve({
          blob: new Blob(chunksRef.current, { type: recorder.mimeType || "audio/webm" }),
          seconds: Math.max(1, Math.round((Date.now() - startedAtRef.current) / 1000)),
        });
      };
      recorder.stop();
    });

  return { recording, seconds, start, stop };
}

/**
 * The minute a Part 2 cue card gives before the student speaks. The student starts it; it counts down once a second by the wall clock (a tab
 * put in the background must not stretch the minute) and stops itself when it runs out. It belongs to one question: a new one starts fresh.
 */
function usePreparation() {
  const [state, setState] = useState<{ turnId: string; elapsed: number } | null>(null);
  const timerRef = useRef<ReturnType<typeof setInterval> | null>(null);

  const stop = () => {
    if (timerRef.current) clearInterval(timerRef.current);
    timerRef.current = null;
  };

  const start = (turnId: string) => {
    stop();
    const startedAt = Date.now();
    setState({ turnId, elapsed: 0 });
    timerRef.current = setInterval(() => {
      const elapsed = Math.min(SPEAKING_PREPARATION_SECONDS, Math.round((Date.now() - startedAt) / 1000));
      setState({ turnId, elapsed });
      if (elapsed >= SPEAKING_PREPARATION_SECONDS) stop();
    }, 500);
  };

  /** Seconds left for this question's preparation, or null when the student has not started it. */
  const remainingFor = (turnId: string | null) =>
    state !== null && state.turnId === turnId ? SPEAKING_PREPARATION_SECONDS - state.elapsed : null;

  return { start, stop, remainingFor };
}

const PARTS = [1, 2, 3] as const;

/** Where in the test the student is: the three parts of a Full Mock with the current one marked (a single part shows just itself) — and,
 * on a cue card whose preparation is running, the minute that is left. */
function PartProgress({ full, part, remaining }: { full: boolean; part: number; remaining: number | null }) {
  return (
    <div className="flex flex-wrap items-center gap-2">
      {(full ? PARTS : [part]).map((p) => (
        <Badge key={p} tone={p < part ? "success" : p === part ? "brand" : "neutral"}>
          Part {p}
          {p === part && part === 2 && remaining !== null && remaining > 0 && ` — tayyorgarlik ${formatDuration(remaining)}`}
        </Badge>
      ))}
    </div>
  );
}

/** The cue card's minute of preparation. Optional: the student may start speaking at any time, and nothing starts by itself when it ends. */
function PreparationPanel({ remaining, disabled, onStart }: { remaining: number | null; disabled: boolean; onStart: () => void }) {
  const finished = remaining !== null && remaining <= 0;

  return (
    <div className="flex flex-wrap items-center gap-3 rounded-xl border border-border p-4">
      <Timer className="size-5 shrink-0 text-ink-500" aria-hidden />
      {remaining === null && (
        <>
          <p className="min-w-0 flex-1 text-[13.5px] text-ink-700">Kartochkani o&apos;qing: gapirishdan oldin 1 daqiqa tayyorlanishingiz mumkin.</p>
          <Button variant="secondary" size="sm" disabled={disabled} onClick={onStart}>Tayyorlanishni boshlash</Button>
        </>
      )}
      {remaining !== null && !finished && (
        <>
          <span className="font-tabular text-[22px] font-semibold text-ink-900">{formatDuration(remaining)}</span>
          <div className="h-2 min-w-24 flex-1 overflow-hidden rounded-full bg-ink-100">
            <div className="h-full rounded-full bg-brand-solid transition-[width]" style={{ width: `${(remaining / SPEAKING_PREPARATION_SECONDS) * 100}%` }} />
          </div>
          <span className="text-[12.5px] text-ink-500">Xohlasangiz, darhol yozishni boshlashingiz mumkin.</span>
        </>
      )}
      {finished && <p className="text-[13.5px] font-medium text-success-600">Tayyorgarlik vaqti tugadi — yozishni boshlang.</p>}
    </div>
  );
}

function AutoReadToggle({ checked, onChange }: { checked: boolean; onChange: (on: boolean) => void }) {
  return (
    <label className="flex w-fit items-center gap-2 text-[12.5px] text-ink-600">
      <input type="checkbox" checked={checked} onChange={(e) => onChange(e.target.checked)} className="size-4 accent-brand-solid" />
      Savolni avtomatik o&apos;qish
    </label>
  );
}

export default function AiSpeakingPage() {
  const client = useQueryClient();
  const [sessionId, setSessionId] = useState<string | null>(null);
  const [micError, setMicError] = useState<string | null>(null);
  const recorder = useRecorder();
  const voice = useQuestionVoice();
  const preparation = usePreparation();
  const [autoRead, setAutoRead] = useAutoRead();

  const statusQuery = useQuery({ queryKey: ["ai", "status"], queryFn: getAiStatus });
  const sessionsQuery = useQuery({ queryKey: ["ai", "speaking", "sessions"], queryFn: listSpeakingSessions });
  const sessionQuery = useQuery({
    queryKey: ["ai", "speaking", "session", sessionId],
    queryFn: () => getSpeakingSession(sessionId!),
    enabled: sessionId !== null,
  });

  const invalidate = () => client.invalidateQueries({ queryKey: ["ai"] });

  /** The student asked for questions to be read on their own: read the one that just arrived. The click that got us here counts as the
   * touch a browser wants before it lets a page make sound. */
  const readAloud = (next: SpeakingSession) => {
    if (autoRead && next.currentQuestion && next.currentTurnId) void voice.play(next.id, next.currentTurnId, next.currentQuestion);
  };

  const start = useMutation({
    mutationFn: (mode: SpeakingMode) => startSpeakingSession(mode),
    onSuccess: (session) => { setSessionId(session.id); invalidate(); readAloud(session); },
  });
  const answer = useMutation({
    mutationFn: ({ blob, seconds }: { blob: Blob; seconds: number }) => answerSpeakingTurn(sessionId!, blob, seconds),
    onSuccess: (session) => { invalidate(); readAloud(session); },
  });
  const finish = useMutation({
    mutationFn: () => finishSpeakingSession(sessionId!),
    onMutate: () => { voice.stop(); preparation.stop(); },
    onSuccess: invalidate,
  });
  const abandon = useMutation({
    mutationFn: () => abandonSpeakingSession(sessionId!),
    onMutate: () => { voice.stop(); preparation.stop(); },
    onSuccess: () => { setSessionId(null); invalidate(); },
  });

  const speaking = statusQuery.data?.features.find((f) => f.feature === "Speaking");
  const session = sessionQuery.data;

  const toggleRecording = async () => {
    setMicError(null);
    if (recorder.recording) {
      const result = await recorder.stop();
      if (result) answer.mutate(result);
      return;
    }

    try {
      voice.stop();
      preparation.stop();
      await recorder.start();
    } catch {
      setMicError("Mikrofonga ruxsat berilmadi. Brauzer sozlamalaridan mikrofonni yoqing.");
    }
  };

  if (statusQuery.isLoading) return <main className="p-4 sm:p-6 lg:p-8"><Skeleton className="h-96" /></main>;
  if (!speaking?.allowed) {
    return (
      <main className="p-4 sm:p-6 lg:p-8">
        <div className="rounded-xl border border-border bg-surface">
          <EmptyState icon={Mic} title="AI Speaking faqat studentlar uchun" />
        </div>
      </main>
    );
  }

  return (
    <main className="p-4 sm:p-6 lg:p-8">
      <PageHeader
        title="AI Speaking"
        description="Mikrofonda gapiring — javobingiz matnga o'giriladi, nutq tezligi o'lchanadi va oxirida taxminiy band beriladi."
        actions={
          speaking.limit !== null && (
            <span className="text-[12.5px] text-ink-500">
              Bugun qoldi: <b className="font-tabular text-ink-800">{speaking.remaining}</b> / {speaking.limit} daqiqa
            </span>
          )
        }
      />

      {statusQuery.data?.demoMode && (
        <div className="mb-4"><Alert tone="info">Demo rejim: haqiqiy AI provayder ulanmagan, javoblar sinov uchun yaratiladi.</Alert></div>
      )}
      {!speaking.available && <div className="mb-4"><Alert>Nutqni matnga o&apos;girish xizmati hozircha sozlanmagan.</Alert></div>}
      <div className="mb-4">
        <Alert tone="info">
          Yakunlangan mashqlaringiz — bahosi, javoblaringiz matni va ovoz yozuvingiz — o&apos;qituvchilaringiz va markaz
          administratorlariga ko&apos;rinadi. Yakunlamagan yoki bekor qilingan mashqlar ko&apos;rinmaydi.
        </Alert>
      </div>

      <div className="grid grid-cols-1 gap-6 xl:grid-cols-[1fr_320px]">
        <div className="space-y-4">
          {session === undefined && sessionId === null && (
            <Card>
              <CardHeader><CardTitle>Mashq turini tanlang</CardTitle></CardHeader>
              <CardBody className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                {MODES.map((mode) => (
                  <button
                    key={mode}
                    disabled={start.isPending || !speaking.available}
                    onClick={() => start.mutate(mode)}
                    className="rounded-xl border border-border bg-surface p-4 text-left transition-colors hover:border-brand-400 hover:bg-brand-50 disabled:opacity-50"
                  >
                    <p className="text-[14px] font-medium text-ink-900">{SPEAKING_MODE_LABEL[mode]}</p>
                    <p className="mt-1 text-[12.5px] text-ink-500">{SPEAKING_MODE_HINT[mode]}</p>
                  </button>
                ))}
              </CardBody>
              <CardBody className="pt-0"><AutoReadToggle checked={autoRead} onChange={setAutoRead} /></CardBody>
              {start.isError && <CardBody className="pt-0"><Alert>{errorText(start.error, "Mashqni boshlab bo'lmadi.")}</Alert></CardBody>}
            </Card>
          )}

          {sessionQuery.isLoading && sessionId !== null && <Skeleton className="h-64" />}

          {session && session.status === "Active" && (
            <Card>
              <CardHeader>
                <CardTitle>{session.modeLabel} · {session.topic}</CardTitle>
                <span className="text-[12.5px] text-ink-500">{session.turnCount} / {session.totalTurns} javob</span>
              </CardHeader>
              <CardBody className="space-y-4">
                {session.currentPart !== null && (
                  <PartProgress full={session.mode === "FullMock"} part={session.currentPart} remaining={preparation.remainingFor(session.currentTurnId)} />
                )}

                {session.currentQuestion && (
                  <div className="rounded-xl border border-brand-200 bg-brand-50 p-4">
                    <div className="flex items-start justify-between gap-3">
                      <p className="whitespace-pre-wrap text-[14px] leading-relaxed text-brand-900">{session.currentQuestion}</p>
                      <Button
                        variant="ghost" size="sm" disabled={!session.currentTurnId}
                        onClick={() => (voice.playing ? voice.stop() : void voice.play(session.id, session.currentTurnId!, session.currentQuestion!))}
                        aria-label={voice.playing ? "Ovozni to'xtatish" : "Savolni tinglash"}
                      >
                        {voice.playing ? <Square className="size-4" aria-hidden /> : <Volume2 className="size-4" aria-hidden />}
                      </Button>
                    </div>
                  </div>
                )}
                <AutoReadToggle checked={autoRead} onChange={setAutoRead} />

                {session.currentPart === 2 && session.currentTurnId && session.currentQuestion && !recorder.recording && (
                  <PreparationPanel
                    remaining={preparation.remainingFor(session.currentTurnId)}
                    disabled={voice.playing || answer.isPending}
                    onStart={() => preparation.start(session.currentTurnId!)}
                  />
                )}

                {micError && <Alert>{micError}</Alert>}
                {answer.isError && <Alert>{errorText(answer.error, "Javobni yuborib bo'lmadi.")}</Alert>}

                <div className="flex flex-wrap items-center gap-3">
                  <Button
                    onClick={toggleRecording}
                    loading={answer.isPending}
                    variant={recorder.recording ? "danger" : "primary"}
                    disabled={!session.currentQuestion || answer.isPending || voice.playing}
                  >
                    {recorder.recording ? <Square className="size-4" aria-hidden /> : <Mic className="size-4" aria-hidden />}
                    {recorder.recording ? `To'xtatish · ${formatDuration(recorder.seconds)}` : "Yozishni boshlash"}
                  </Button>
                  {recorder.recording && <span className="flex size-2.5 animate-pulse rounded-full bg-danger-500" aria-hidden />}
                  {voice.playing && <span className="text-[13px] text-ink-500">Savol o&apos;qilmoqda — tugagach yozishni boshlang.</span>}
                  {answer.isPending && <span className="text-[13px] text-ink-500">Javobingiz matnga o&apos;girilmoqda…</span>}

                  <div className="ml-auto flex items-center gap-2">
                    <Button variant="secondary" size="sm" loading={finish.isPending} disabled={session.turnCount === 0}
                      onClick={() => finish.mutate()}>
                      <CheckCircle2 className="size-3.5" aria-hidden />
                      Yakunlash va baholash
                    </Button>
                    <Button variant="ghost" size="sm" loading={abandon.isPending} onClick={() => abandon.mutate()}>
                      <Ban className="size-3.5" aria-hidden />
                      Bekor qilish
                    </Button>
                  </div>
                </div>
                {finish.isError && <Alert>{errorText(finish.error, "Baholab bo'lmadi.")}</Alert>}
              </CardBody>
            </Card>
          )}

          {session && session.turns.some((t) => t.transcript) && (
            <SpeakingTranscriptCard session={session} audioPath={(turnId) => `/api/ai/speaking/sessions/${session.id}/turns/${turnId}/audio`} />
          )}
          {session && session.status === "Completed" && <SpeakingReportCard session={session} compact />}
          {session && session.status === "Failed" && session.error && <Alert>{session.error}</Alert>}
        </div>

        <Card>
          <CardHeader><CardTitle>Mashqlar tarixi</CardTitle></CardHeader>
          {sessionsQuery.isLoading && <CardBody><Skeleton className="h-24" /></CardBody>}
          {sessionsQuery.data?.length === 0 && <EmptyState icon={Headphones} title="Hali mashq qilmagansiz" />}
          <ul className="divide-y divide-border">
            {sessionsQuery.data?.map((item) => (
              <li key={item.id}>
                <button
                  onClick={() => setSessionId(item.id)}
                  className={cn("w-full px-5 py-3 text-left transition-colors hover:bg-ink-50", item.id === sessionId && "bg-brand-50 hover:bg-brand-50")}
                >
                  <div className="flex items-center justify-between gap-2">
                    <p className="truncate text-[13.5px] font-medium text-ink-900">{item.modeLabel}</p>
                    {item.status === "Completed"
                      ? <span className="font-tabular text-[15px] font-semibold text-brand-700">{formatBand(item.teacherBand ?? item.estimatedBand)}</span>
                      : <Badge tone={item.status === "Active" ? "brand" : "neutral"}>{STATUS_LABEL[item.status]}</Badge>}
                  </div>
                  <p className="truncate text-[12px] text-ink-500">
                    {item.topic} · {formatDuration(item.spokenSeconds)} · {formatShortDate(item.createdAt)}
                    {item.teacherBand !== null && " · o'qituvchi bahosi"}
                  </p>
                </button>
              </li>
            ))}
          </ul>
          {sessionId !== null && (
            <CardBody className="border-t border-border">
              <Button variant="secondary" size="sm" className="w-full" onClick={() => setSessionId(null)}>
                Yangi mashq boshlash
              </Button>
            </CardBody>
          )}
        </Card>
      </div>
    </main>
  );
}
