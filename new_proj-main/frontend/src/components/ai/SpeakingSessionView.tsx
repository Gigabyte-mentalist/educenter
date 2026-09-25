"use client";

import { useState } from "react";
import { Gauge, Mic, Play, UserCheck } from "lucide-react";
import type { SpeakingSession } from "@/lib/api/ai";
import { apiFetchBlob } from "@/lib/api-client";
import { Card, CardBody, CardHeader, CardTitle } from "@/components/ui/Card";
import { StatTile } from "@/components/ui/StatTile";
import { formatShortDateTime } from "@/lib/format-date";
import { cn } from "@/lib/cn";

export const formatBand = (band: number | null | undefined) => (band === null || band === undefined ? "—" : band.toFixed(1));
export const formatDuration = (seconds: number) => `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, "0")}`;

/** Where one turn's recording is fetched from: the student's own endpoint, or the reviewer's for the student's profile. */
export type AudioPath = (turnId: string) => string;

/** One practice as a report and a transcript — the same view for the student who made it and for staff who may read it. */

export function SpeakingTranscriptCard({ session, audioPath }: { session: SpeakingSession; audioPath: AudioPath }) {
  return (
    <Card>
      <CardHeader><CardTitle>Suhbat matni</CardTitle></CardHeader>
      <ul className="divide-y divide-border">
        {session.turns.filter((turn) => turn.transcript).map((turn) => (
          <li key={turn.id} className="px-5 py-4">
            <p className="text-[13px] font-medium text-ink-500">
              {session.mode === "FullMock" && turn.part !== null && (
                <span className="mr-1.5 rounded bg-ink-100 px-1.5 py-0.5 text-[11px] font-medium text-ink-600">Part {turn.part}</span>
              )}
              {turn.ordinal}. {turn.question}
            </p>
            <p className="mt-1.5 text-[13.5px] leading-relaxed text-ink-800">{turn.transcript}</p>
            <div className="mt-2 flex flex-wrap items-center gap-3 text-[12px] text-ink-500">
              <span>{formatDuration(turn.spokenSeconds)}</span>
              {turn.wordsPerMinute !== null && <span>{turn.wordsPerMinute} so&apos;z/daqiqa</span>}
              {turn.longPauses !== null && turn.longPauses > 0 && <span>{turn.longPauses} uzun pauza</span>}
              {turn.hasAudio && <TurnAudio path={audioPath(turn.id)} />}
            </div>
          </li>
        ))}
      </ul>
    </Card>
  );
}

/** The recording is fetched with the caller's token, so it cannot be a plain <audio src>. */
function TurnAudio({ path }: { path: string }) {
  const [url, setUrl] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  if (url) return <audio controls src={url} className="h-8" />;

  return (
    <button
      className="inline-flex items-center gap-1 text-brand-600 hover:underline disabled:opacity-50"
      disabled={loading}
      onClick={async () => {
        setLoading(true);
        try {
          const blob = await apiFetchBlob(path);
          setUrl(URL.createObjectURL(blob));
        } finally {
          setLoading(false);
        }
      }}
    >
      <Play className="size-3.5" aria-hidden />
      Yozuvni eshitish
    </button>
  );
}

/** `compact` stacks everything in one column, for the narrow places a teacher reads it in (a side panel, a dialog) — where three criteria
 * side by side leave each a sliver. The student's own page has the room and keeps them in a row. */
export function SpeakingReportCard({ session, compact = false }: { session: SpeakingSession; compact?: boolean }) {
  const assessment = session.assessment;
  const review = session.teacherReview;

  const criteria: { label: string; band: number | null; comment: string | null | undefined; note?: string }[] = [
    { label: "Fluency & Coherence", band: session.fluencyBand, comment: assessment?.fluencyComment },
    { label: "Lexical Resource", band: session.lexicalBand, comment: assessment?.lexicalComment },
    { label: "Grammatical Range", band: session.grammarBand, comment: assessment?.grammarComment },
  ];
  // Answer relevance is beside the three IELTS criteria, not one of them: it is not in the estimated band. Sessions assessed before it was
  // asked for have none, and show the three as they always did.
  if (session.relevanceBand !== null)
    criteria.push({ label: "Javobning mosligi", band: session.relevanceBand, comment: assessment?.relevanceComment, note: "Taxminiy bandga kiritilmaydi" });

  const parts = assessment?.parts?.filter((p) => p.comment) ?? [];

  return (
    <Card>
      <CardHeader><CardTitle>Natija</CardTitle></CardHeader>
      <CardBody className="space-y-5">
        {review && (
          <div className="rounded-xl border border-success-600/20 bg-success-50 p-5">
            <p className="flex items-center gap-1.5 text-[13px] font-medium text-success-600">
              <UserCheck className="size-4" aria-hidden />
              O&apos;qituvchi bahosi: <span className="font-tabular text-[18px]">{formatBand(review.band)}</span>
            </p>
            {(review.fluencyBand !== null || review.lexicalBand !== null || review.grammarBand !== null) && (
              <p className="mt-1 text-[12.5px] text-ink-600">
                Fluency {formatBand(review.fluencyBand)} · Lexical {formatBand(review.lexicalBand)} · Grammar {formatBand(review.grammarBand)}
              </p>
            )}
            {review.comment && <p className="mt-2 whitespace-pre-wrap text-[13.5px] text-ink-700">{review.comment}</p>}
            <p className="mt-2 text-[12px] text-ink-500">
              {review.byName ?? "O'qituvchi"} · {formatShortDateTime(review.at)} · AI bahosi o&apos;zgarmagan, pastda ko&apos;rsatilgan
            </p>
          </div>
        )}
        <div className={cn("grid grid-cols-1 gap-4", !compact && "md:grid-cols-[200px_1fr]")}>
          <div className="rounded-xl border border-brand-200 bg-brand-50 p-5 text-center">
            <p className="text-[12.5px] font-medium uppercase tracking-wide text-brand-700">Taxminiy band</p>
            <p className="mt-1 font-tabular text-5xl font-semibold text-brand-800">{formatBand(session.estimatedBand)}</p>
            <p className="mt-2 text-[12px] leading-snug text-brand-700">Talaffuz baholanmaydi — bu transkript asosidagi taxmin</p>
          </div>
          <div className={cn("grid grid-cols-1 gap-3", !compact && (criteria.length > 3 ? "sm:grid-cols-2" : "sm:grid-cols-3"))}>
            {criteria.map((criterion) => (
              <div key={criterion.label} className="rounded-xl border border-border p-4">
                <div className="flex items-baseline justify-between gap-2">
                  <p className="text-[13px] font-medium text-ink-900">{criterion.label}</p>
                  <p className="font-tabular text-[18px] font-semibold text-ink-900">{formatBand(criterion.band)}</p>
                </div>
                {criterion.note && <p className="text-[11.5px] text-ink-500">{criterion.note}</p>}
                {criterion.comment && <p className="mt-1.5 text-[12.5px] text-ink-600">{criterion.comment}</p>}
              </div>
            ))}
          </div>
        </div>

        {parts.length > 0 && (
          <div className="rounded-xl border border-border p-4">
            <p className="mb-2 text-[13.5px] font-medium text-ink-900">Qismlar bo&apos;yicha</p>
            <ul className="space-y-1.5 text-[13.5px] text-ink-700">
              {parts.map((p) => (
                <li key={p.part} className="flex gap-2"><span className="shrink-0 font-medium text-ink-500">Part {p.part}</span>{p.comment}</li>
              ))}
            </ul>
          </div>
        )}

        {assessment && (
          <>
            <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
              <StatTile label="Gapirgan vaqt" value={formatDuration(assessment.spokenSeconds)} icon={Mic} />
              <StatTile label="Nutq tezligi" value={`${assessment.wordsPerMinute} so'z/daq`} icon={Gauge} tone="brand" />
              <StatTile label="Uzun pauzalar" value={assessment.longPauses} tone={assessment.longPauses > 5 ? "warning" : "neutral"} />
              <StatTile label="Javoblar" value={assessment.turnCount} />
            </div>

            <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
              <FeedbackList title="Kuchli tomonlar" tone="text-success-600" items={assessment.strengths} />
              <FeedbackList title="Yaxshilash kerak" tone="text-warning-600" items={assessment.weaknesses} />
            </div>
            <FeedbackList title="Tavsiyalar" tone="text-brand-600" items={assessment.suggestions} />
          </>
        )}
      </CardBody>
    </Card>
  );
}

function FeedbackList({ title, tone, items }: { title: string; tone: string; items: string[] }) {
  if (items.length === 0) return null;
  return (
    <div className="rounded-xl border border-border p-4">
      <p className={cn("mb-2 text-[13.5px] font-medium", tone)}>{title}</p>
      <ul className="space-y-1.5 text-[13.5px] text-ink-700">
        {items.map((item, i) => <li key={i} className="flex gap-2"><span className="text-ink-400">•</span>{item}</li>)}
      </ul>
    </div>
  );
}
