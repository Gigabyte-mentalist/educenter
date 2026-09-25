"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ShieldAlert } from "lucide-react";
import {
  getStudentSpeakingSession, overrideSpeakingSession, studentSpeakingAudioPath, type SpeakingSession,
} from "@/lib/api/ai";
import { ApiError } from "@/lib/api-client";
import { Alert } from "@/components/ui/Alert";
import { Button } from "@/components/ui/Button";
import { Label, Select, Textarea } from "@/components/ui/Field";
import { Skeleton } from "@/components/ui/Skeleton";
import { SpeakingReportCard, SpeakingTranscriptCard, formatBand } from "@/components/ai/SpeakingSessionView";

const BAND_OPTIONS = Array.from({ length: 19 }, (_, i) => i / 2);
const errorText = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

const sessionKey = (studentId: string, sessionId: string) => ["students", studentId, "speaking", sessionId] as const;

/**
 * One student's finished Speaking practice as a teacher works with it: the AI's report, the transcript with every recording to listen
 * to, and — for someone who may — the teacher's own bands, for when the AI got it wrong. Used on the student's profile and on the
 * teacher's Speaking inbox, so the two can never drift apart.
 */
export function SpeakingReviewPanel({ studentId, sessionId }: { studentId: string; sessionId: string }) {
  // Opening a session is written to the audit log, and a finished session does not change under the reader — one fetch is enough.
  const query = useQuery({
    queryKey: sessionKey(studentId, sessionId),
    queryFn: () => getStudentSpeakingSession(studentId, sessionId),
    staleTime: Infinity,
  });

  if (query.isPending) return <Skeleton className="h-64" />;
  if (query.isError) return <Alert>{errorText(query.error, "Mashqni yuklab bo‘lmadi.")}</Alert>;

  const session = query.data;
  return (
    <div className="space-y-4">
      <SpeakingReportCard session={session} compact />
      <SpeakingTranscriptCard session={session} audioPath={(turnId) => studentSpeakingAudioPath(studentId, session.id, turnId)} />
      {session.canOverride && <SpeakingReviewForm key={session.id} studentId={studentId} session={session} />}
    </div>
  );
}

/** The teacher's bands after listening. The AI's stay as they were and the student is told there is a review. */
function SpeakingReviewForm({ studentId, session }: { studentId: string; session: SpeakingSession }) {
  const client = useQueryClient();
  const review = session.teacherReview;
  const [band, setBand] = useState<number>(review?.band ?? session.estimatedBand ?? 6);
  const [criteria, setCriteria] = useState<Record<"fluency" | "lexical" | "grammar", number | null>>({
    fluency: review?.fluencyBand ?? null, lexical: review?.lexicalBand ?? null, grammar: review?.grammarBand ?? null,
  });
  const [comment, setComment] = useState(review?.comment ?? "");

  const save = useMutation({
    mutationFn: () => overrideSpeakingSession(studentId, session.id, { band, ...criteria, comment }),
    onSuccess: (updated) => {
      // The answer is the refreshed session: put it in place rather than fetching again (every fetch of a session is an audit entry).
      client.setQueryData(sessionKey(studentId, session.id), updated);
      void client.invalidateQueries({ queryKey: ["students", studentId, "speaking"], exact: true });
      void client.invalidateQueries({ queryKey: ["speaking-reviews"] });
    },
  });

  const criterionField = (key: "fluency" | "lexical" | "grammar", label: string) => (
    <div key={key}>
      <Label htmlFor={`speaking-${key}`} className="truncate">{label}</Label>
      <Select
        id={`speaking-${key}`} value={criteria[key] ?? ""}
        onChange={(e) => setCriteria((prev) => ({ ...prev, [key]: e.target.value === "" ? null : Number(e.target.value) }))}
      >
        <option value="">—</option>
        {BAND_OPTIONS.map((b) => <option key={b} value={b}>{formatBand(b)}</option>)}
      </Select>
    </div>
  );

  return (
    <form
      className="space-y-3 rounded-xl border border-border bg-ink-50 p-4"
      onSubmit={(e) => { e.preventDefault(); save.mutate(); }}
    >
      <p className="flex items-center gap-1.5 text-[13.5px] font-medium text-ink-900">
        <ShieldAlert className="size-4 text-ink-500" aria-hidden />
        O&apos;qituvchi bahosi
      </p>
      <p className="text-[12.5px] text-ink-500">
        Yozuvni eshitib, AI noto&apos;g&apos;ri baholagan bo&apos;lsa o&apos;z bahoingizni qo&apos;ying. AI natijasi o&apos;zgarmaydi — sizning
        bahongiz uning yonida ko&apos;rsatiladi va student bundan xabardor qilinadi.
      </p>
      <div>
        <Label htmlFor="speaking-band">Umumiy band</Label>
        <Select id="speaking-band" value={band} onChange={(e) => setBand(Number(e.target.value))}>
          {BAND_OPTIONS.map((b) => <option key={b} value={b}>{formatBand(b)}</option>)}
        </Select>
      </div>
      <div className="grid grid-cols-1 gap-2 sm:grid-cols-3">
        {criterionField("fluency", "Fluency & Coherence")}
        {criterionField("lexical", "Lexical Resource")}
        {criterionField("grammar", "Grammatical Range")}
      </div>
      <div>
        <Label htmlFor="speaking-comment">Izoh</Label>
        <Textarea id="speaking-comment" rows={3} value={comment} onChange={(e) => setComment(e.target.value)} />
      </div>
      {save.isError && <Alert>{errorText(save.error, "Saqlab bo‘lmadi.")}</Alert>}
      {save.isSuccess && <Alert tone="success">Baho saqlandi.</Alert>}
      <Button type="submit" size="sm" loading={save.isPending}>Bahoni saqlash</Button>
    </form>
  );
}
