"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CheckCircle2 } from "lucide-react";
import { getPlacementQuestions, listPlacementAttempts, submitPlacementTest, updateLeadPreferences, type Lead } from "@/lib/api/crm";
import { ApiError } from "@/lib/api-client";
import { usePermissions } from "@/lib/use-permissions";
import { Drawer } from "@/components/ui/Drawer";
import { Button } from "@/components/ui/Button";
import { Alert } from "@/components/ui/Alert";
import { Badge } from "@/components/ui/Badge";
import { formatDateTime } from "@/lib/format-date";

/** docs/00_TZ.md §13 — an operator-administered quiz. Answers are collected client-side and graded on the
 * server, which is the only place that knows the answer key. */
export function PlacementTestDrawer({ lead, onClose }: { lead: Lead; onClose: () => void }) {
  const queryClient = useQueryClient();
  const { can } = usePermissions();
  const [answers, setAnswers] = useState<Record<number, number>>({});
  const [result, setResult] = useState<{ level: string; correct: number; total: number } | null>(null);

  const questions = useQuery({ queryKey: ["placement-questions"], queryFn: getPlacementQuestions });
  const history = useQuery({ queryKey: ["placement-attempts", lead.id], queryFn: () => listPlacementAttempts(lead.id) });

  const submit = useMutation({
    mutationFn: () =>
      submitPlacementTest(lead.id, Object.entries(answers).map(([questionId, selectedIndex]) => ({ questionId: Number(questionId), selectedIndex }))),
    onSuccess: (attempt) => {
      setResult({ level: attempt.recommendedLevel, correct: attempt.correctCount, total: attempt.totalQuestions });
      setAnswers({});
      queryClient.invalidateQueries({ queryKey: ["placement-attempts", lead.id] });
    },
  });

  // The test only suggests a level; writing it onto the lead is the operator's decision.
  const applyLevel = useMutation({
    mutationFn: (level: string) => updateLeadPreferences(lead.id, { currentLevel: level }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["lead", lead.id] });
      queryClient.invalidateQueries({ queryKey: ["leads"] });
      queryClient.invalidateQueries({ queryKey: ["group-recommendations", lead.id] });
    },
  });

  const allAnswered = questions.data ? questions.data.every((q) => answers[q.id] !== undefined) : false;
  const error = questions.error ?? history.error ?? submit.error ?? applyLevel.error;

  return (
    <Drawer open onClose={onClose} title="Daraja testi" description={lead.fullName}>
      <div className="space-y-5">
        {error && <Alert>{error instanceof ApiError ? error.message : "Amalni bajarib bo'lmadi."}</Alert>}

        {history.data && history.data.length > 0 && (
          <div className="space-y-2 rounded-lg border border-border p-3">
            <p className="text-[13px] font-medium text-ink-700">Oldingi urinishlar</p>
            {history.data.map((a) => (
              <div key={a.id} className="flex items-center justify-between text-[13px] text-ink-600">
                <span>{formatDateTime(a.completedAt)}</span>
                <span className="flex items-center gap-1.5">
                  {a.correctCount}/{a.totalQuestions}
                  <Badge tone="brand">{a.recommendedLevel}</Badge>
                </span>
              </div>
            ))}
          </div>
        )}

        {result && (
          <div className="rounded-lg border border-brand-200 bg-brand-50 p-4 text-center">
            <p className="text-[13px] text-ink-600">Tavsiya etilgan daraja</p>
            <p className="mt-1 text-2xl font-semibold text-brand-700">{result.level}</p>
            <p className="mt-1 text-[13px] text-ink-500">{result.correct}/{result.total} to&apos;g&apos;ri javob</p>
            <div className="mt-3 flex flex-wrap justify-center gap-2">
              {can("leads.edit") && (
                applyLevel.isSuccess && applyLevel.variables === result.level
                  ? <Badge tone="success">Lidga saqlandi</Badge>
                  : (
                    <Button size="sm" loading={applyLevel.isPending} onClick={() => applyLevel.mutate(result.level)}>
                      Darajani lidga qo&apos;llash
                    </Button>
                  )
              )}
              <Button variant="secondary" size="sm" onClick={() => setResult(null)}>Qayta topshirish</Button>
            </div>
          </div>
        )}

        {!result && questions.isLoading && <p className="text-sm text-ink-500">Savollar yuklanmoqda...</p>}

        {!result && questions.data && (
          <form className="space-y-5" onSubmit={(e) => { e.preventDefault(); submit.mutate(); }}>
            {questions.data.map((q, i) => (
              <fieldset key={q.id} className="space-y-2">
                <legend className="text-[13.5px] font-medium text-ink-900">{i + 1}. {q.text}</legend>
                <div className="space-y-1.5">
                  {q.options.map((option, idx) => (
                    <label key={idx} className="flex cursor-pointer items-center gap-2 text-[13.5px] text-ink-700">
                      <input
                        type="radio"
                        name={`question-${q.id}`}
                        checked={answers[q.id] === idx}
                        onChange={() => setAnswers((prev) => ({ ...prev, [q.id]: idx }))}
                      />
                      {option}
                    </label>
                  ))}
                </div>
              </fieldset>
            ))}
            <Button type="submit" loading={submit.isPending} disabled={!allAnswered} className="w-full">
              <CheckCircle2 className="size-4" aria-hidden />
              Yakunlash
            </Button>
            {!allAnswered && <p className="text-center text-[12px] text-ink-500">Davom etish uchun barcha savollarga javob bering.</p>}
          </form>
        )}
      </div>
    </Drawer>
  );
}
