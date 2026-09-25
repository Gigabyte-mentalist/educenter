"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { ApiError } from "@/lib/api-client";
import { getLessonJournal, saveLessonJournal } from "@/lib/api/lms";
import { Alert } from "@/components/ui/Alert";
import { Button } from "@/components/ui/Button";
import { Card, CardBody, CardHeader, CardTitle } from "@/components/ui/Card";
import { Input, Label, Textarea } from "@/components/ui/Field";
import { Skeleton } from "@/components/ui/Skeleton";

const TOPIC_MAX = 200;
const NOTES_MAX = 2000;
const SCALE_MAX = 100;

/** The teacher's part of a lesson: what it was about and how it went. It saves on its own — a topic is often written before the
 * class, long before attendance is marked. The notes are staff-only; a student or parent only ever sees the topic, in the schedule. */
export function LessonJournal({ lessonId }: { lessonId: string }) {
  const queryClient = useQueryClient();
  const journalQuery = useQuery({ queryKey: ["lessons", lessonId, "journal"], queryFn: () => getLessonJournal(lessonId) });

  // What the server has, overlaid with what the teacher typed. Derived on every render rather than copied into state by an
  // effect, so a refetch can never clobber (or be clobbered by) an edit in progress.
  const [edits, setEdits] = useState<{ topic?: string; notes?: string; scale?: string }>({});
  const savedScale = journalQuery.data?.maxLessonScore != null ? String(journalQuery.data.maxLessonScore) : "";
  const topic = edits.topic ?? journalQuery.data?.topic ?? "";
  const notes = edits.notes ?? journalQuery.data?.notes ?? "";
  const scale = edits.scale ?? savedScale;
  const topicChanged = edits.topic !== undefined && edits.topic !== (journalQuery.data?.topic ?? "");
  const notesChanged = edits.notes !== undefined && edits.notes !== (journalQuery.data?.notes ?? "");
  const scaleChanged = edits.scale !== undefined && edits.scale.trim() !== savedScale;
  const scaleInvalid = scale.trim() !== "" && !(Number.isInteger(Number(scale)) && Number(scale) >= 1 && Number(scale) <= SCALE_MAX);

  const saveMutation = useMutation({
    mutationFn: () => saveLessonJournal(lessonId, {
      topic: topicChanged ? topic : undefined,
      notes: notesChanged ? notes : undefined,
      // An empty box means "this lesson is not scored", which the API spells 0.
      maxLessonScore: scaleChanged ? (scale.trim() === "" ? 0 : Number(scale)) : undefined,
    }),
    onSuccess: async (saved) => {
      queryClient.setQueryData(["lessons", lessonId, "journal"], saved);
      setEdits({});
      // The topic is also the lesson's title in the schedule and in the header above, and the scale decides whether attendance shows scores.
      await queryClient.invalidateQueries({ queryKey: ["lessons"] });
    },
  });

  if (journalQuery.isLoading) return <Skeleton className="mb-6 h-40" />;
  if (journalQuery.isError) {
    return <div className="mb-6"><Alert>{journalQuery.error instanceof ApiError ? journalQuery.error.message : "Yuklab bo'lmadi."}</Alert></div>;
  }

  const dirty = topicChanged || notesChanged || scaleChanged;

  return (
    <Card className="mb-6">
      <CardHeader>
        <CardTitle>Dars jurnali</CardTitle>
        <Button size="sm" loading={saveMutation.isPending} disabled={!dirty || scaleInvalid} onClick={() => saveMutation.mutate()}>Saqlash</Button>
      </CardHeader>
      <CardBody className="space-y-4">
        <div>
          <Label htmlFor="lesson-topic">Mavzu</Label>
          <Input
            id="lesson-topic"
            value={topic}
            maxLength={TOPIC_MAX}
            onChange={(e) => setEdits((prev) => ({ ...prev, topic: e.target.value }))}
            placeholder="Masalan: Present Perfect vs Past Simple"
          />
          <p className="mt-1 text-[12px] text-ink-500">Mavzuni studentlar va ota-onalar jadvalda ko&apos;radi.</p>
        </div>
        <div>
          <Label htmlFor="lesson-notes">Dars izohi</Label>
          <Textarea
            id="lesson-notes"
            rows={4}
            value={notes}
            maxLength={NOTES_MAX}
            onChange={(e) => setEdits((prev) => ({ ...prev, notes: e.target.value }))}
            placeholder="Dars qanday o'tdi, keyingi darsga eslatmalar…"
          />
          <p className="mt-1 text-[12px] text-ink-500">Izohni faqat xodimlar ko&apos;radi — student va ota-ona ko&apos;rmaydi.</p>
        </div>
        <div className="sm:max-w-xs">
          <Label htmlFor="lesson-scale">Dars balli (eng yuqori)</Label>
          <Input
            id="lesson-scale"
            type="number"
            inputMode="numeric"
            min={1}
            max={SCALE_MAX}
            value={scale}
            aria-invalid={scaleInvalid}
            onChange={(e) => setEdits((prev) => ({ ...prev, scale: e.target.value }))}
            placeholder="Bo'sh — bu darsda ball yo'q"
          />
          <p className={`mt-1 text-[12px] ${scaleInvalid ? "text-danger-600" : "text-ink-500"}`}>
            {scaleInvalid ? `1 dan ${SCALE_MAX} gacha butun son.` : "Belgilansa, davomat jadvalida har bir student uchun ball ustuni chiqadi."}
          </p>
        </div>
        {saveMutation.isError && (
          <Alert>{saveMutation.error instanceof ApiError ? saveMutation.error.message : "Saqlab bo'lmadi."}</Alert>
        )}
        {saveMutation.isSuccess && !dirty && <p className="text-[12.5px] text-success-600">Saqlandi.</p>}
      </CardBody>
    </Card>
  );
}
