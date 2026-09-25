"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Lightbulb, Loader2, RotateCcw, ShieldAlert, ThumbsDown, ThumbsUp, UserCheck } from "lucide-react";
import {
  getWritingEvaluation, isWritingPending, overrideWritingEvaluation, retryWritingEvaluation,
  type WritingEvaluation, type WritingEvaluationStatus, type WritingIssue,
} from "@/lib/api/ai";
import { ApiError } from "@/lib/api-client";
import { Markdown } from "@/components/ai/Markdown";
import { Alert } from "@/components/ui/Alert";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { Label, Select, Textarea } from "@/components/ui/Field";
import { cn } from "@/lib/cn";
import { formatDateTime } from "@/lib/format-date";

const STATUS_LABEL: Record<WritingEvaluationStatus, string> = {
  Queued: "Navbatda",
  Processing: "Baholanmoqda",
  Completed: "Tayyor",
  Failed: "Bajarilmadi",
  Rejected: "Rad etildi",
};
const STATUS_TONE: Record<WritingEvaluationStatus, "neutral" | "brand" | "success" | "danger" | "warning"> = {
  Queued: "neutral",
  Processing: "brand",
  Completed: "success",
  Failed: "warning",
  Rejected: "danger",
};

const BAND_OPTIONS = Array.from({ length: 19 }, (_, i) => i / 2);

export const formatBand = (band: number | null | undefined) => (band === null || band === undefined ? "—" : band.toFixed(1));
const errorText = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

/** One assessment, polled every few seconds while the background job is still running. */
export function useWritingEvaluation(id: string | null) {
  return useQuery({
    queryKey: ["ai", "writing", "evaluation", id],
    queryFn: () => getWritingEvaluation(id!),
    enabled: id !== null,
    refetchInterval: (query) => (query.state.data && isWritingPending(query.state.data.summary.status) ? 3000 : false),
  });
}

export function WritingStatusBadge({ status }: { status: WritingEvaluationStatus }) {
  return <Badge tone={STATUS_TONE[status]} dot>{STATUS_LABEL[status]}</Badge>;
}

/** The full Writing AI result: estimated band, criteria, feedback, issues — plus retry for the owner and the override
 * form for a teacher. <c>compact</c> stacks everything in one column for drawers. */
export function WritingEvaluationView({ evaluation, compact = false }: { evaluation: WritingEvaluation; compact?: boolean }) {
  const client = useQueryClient();
  const { summary } = evaluation;
  const retry = useMutation({
    mutationFn: () => retryWritingEvaluation(summary.id),
    onSuccess: () => client.invalidateQueries({ queryKey: ["ai", "writing"] }),
  });

  if (isWritingPending(summary.status)) {
    return (
      <div className="flex items-center gap-3 rounded-xl border border-border bg-surface p-5">
        <Loader2 className="size-5 animate-spin text-brand-600" aria-hidden />
        <div>
          <p className="text-[14px] font-medium text-ink-900">AI inshoni baholamoqda…</p>
          <p className="text-[13px] text-ink-500">Odatda bir daqiqagacha davom etadi. Sahifa o&apos;zi yangilanadi.</p>
        </div>
      </div>
    );
  }

  if (summary.status === "Failed" || summary.status === "Rejected") {
    return (
      <div className="space-y-3">
        <Alert tone={summary.status === "Rejected" ? "danger" : "info"}>
          {evaluation.error ?? "Baholab bo'lmadi."}
        </Alert>
        {evaluation.canRetry && (
          <Button variant="secondary" loading={retry.isPending} onClick={() => retry.mutate()}>
            <RotateCcw className="size-4" aria-hidden />
            Qayta baholash
          </Button>
        )}
        {retry.isError && <Alert>{errorText(retry.error, "Qayta yuborib bo'lmadi.")}</Alert>}
        <EssayBlock evaluation={evaluation} />
      </div>
    );
  }

  const twoColumns = compact ? "grid-cols-1" : "grid-cols-1 md:grid-cols-2";

  return (
    <div className="space-y-5">
      <div className={cn("grid gap-4", compact ? "grid-cols-1" : "grid-cols-1 md:grid-cols-[220px_1fr]")}>
        <div className="rounded-xl border border-brand-200 bg-brand-50 p-5 text-center">
          <p className="text-[12.5px] font-medium uppercase tracking-wide text-brand-700">Taxminiy IELTS band</p>
          <p className="mt-1 font-tabular text-5xl font-semibold text-brand-800">{formatBand(summary.estimatedBand)}</p>
          <p className="mt-2 text-[12px] leading-snug text-brand-700">AI bahosi — rasmiy IELTS natijasi emas</p>
        </div>
        {summary.teacherBand !== null ? (
          <div className="rounded-xl border border-success-600/20 bg-success-50 p-5">
            <p className="flex items-center gap-1.5 text-[13px] font-medium text-success-600">
              <UserCheck className="size-4" aria-hidden />
              O&apos;qituvchi bahosi: <span className="font-tabular text-[18px]">{formatBand(summary.teacherBand)}</span>
            </p>
            {evaluation.teacherComment && <p className="mt-2 whitespace-pre-wrap text-[13.5px] text-ink-700">{evaluation.teacherComment}</p>}
            <p className="mt-2 text-[12px] text-ink-500">
              {evaluation.overriddenByName}
              {evaluation.overriddenAt && ` · ${formatDateTime(evaluation.overriddenAt)}`}
            </p>
          </div>
        ) : (
          <div className="rounded-xl border border-border bg-surface p-5 text-[13px] text-ink-600">
            <p className="font-medium text-ink-800">{summary.taskTitle}</p>
            <p className="mt-1">{summary.wordCount} so&apos;z · o&apos;qituvchi hali ko&apos;rib chiqmagan</p>
          </div>
        )}
      </div>

      <div className={cn("grid gap-3", twoColumns)}>
        {evaluation.criteria.map((criterion) => (
          <div key={criterion.key} className="rounded-xl border border-border bg-surface p-4">
            <div className="flex items-baseline justify-between gap-3">
              <p className="text-[13.5px] font-medium text-ink-900">{criterion.label}</p>
              <p className="font-tabular text-[18px] font-semibold text-ink-900">
                {formatBand(criterion.aiBand)}
                {criterion.teacherBand !== null && (
                  <span className="ml-2 text-[13px] font-medium text-success-600">→ {formatBand(criterion.teacherBand)}</span>
                )}
              </p>
            </div>
            {criterion.comment && <Markdown content={criterion.comment} className="mt-1.5 text-[13px] text-ink-600" />}
          </div>
        ))}
      </div>

      <div className={cn("grid gap-3", twoColumns)}>
        <FeedbackList title="Kuchli tomonlar" icon={ThumbsUp} tone="text-success-600" items={evaluation.strengths} />
        <FeedbackList title="Yaxshilash kerak" icon={ThumbsDown} tone="text-warning-600" items={evaluation.weaknesses} />
      </div>

      <IssueList title="Grammatika" issues={evaluation.grammarIssues} />
      <IssueList title="Lug'at" issues={evaluation.vocabularyIssues} />
      <FeedbackList title="Tavsiyalar" icon={Lightbulb} tone="text-brand-600" items={evaluation.suggestions} />

      <EssayBlock evaluation={evaluation} />

      {evaluation.canOverride && <OverrideForm evaluation={evaluation} />}
    </div>
  );
}

function FeedbackList({ title, icon: Icon, tone, items }: { title: string; icon: typeof ThumbsUp; tone: string; items: string[] }) {
  if (items.length === 0) return null;
  return (
    <div className="rounded-xl border border-border bg-surface p-4">
      <p className={cn("mb-2 flex items-center gap-1.5 text-[13.5px] font-medium", tone)}>
        <Icon className="size-4" aria-hidden />
        {title}
      </p>
      <ul className="space-y-1.5 text-[13.5px] text-ink-700">
        {items.map((item, i) => <li key={i} className="flex gap-2"><span className="text-ink-400">•</span>{item}</li>)}
      </ul>
    </div>
  );
}

function IssueList({ title, issues }: { title: string; issues: WritingIssue[] }) {
  if (issues.length === 0) return null;
  return (
    <div className="rounded-xl border border-border bg-surface p-4">
      <p className="mb-2 text-[13.5px] font-medium text-ink-900">{title} <span className="text-ink-400">({issues.length})</span></p>
      <ul className="divide-y divide-border">
        {issues.map((issue, i) => (
          <li key={i} className="py-2.5 first:pt-0 last:pb-0">
            <p className="text-[13.5px]">
              <span className="rounded bg-danger-50 px-1 text-danger-600 line-through decoration-danger-600/50">{issue.excerpt}</span>
              <span className="mx-1.5 text-ink-400">→</span>
              <span className="rounded bg-success-50 px-1 text-success-600">{issue.correction}</span>
            </p>
            {issue.explanation && <p className="mt-1 text-[12.5px] text-ink-500">{issue.explanation}</p>}
          </li>
        ))}
      </ul>
    </div>
  );
}

function EssayBlock({ evaluation }: { evaluation: WritingEvaluation }) {
  return (
    <details className="rounded-xl border border-border bg-surface p-4">
      <summary className="cursor-pointer text-[13.5px] font-medium text-ink-800">Topshiriq va insho</summary>
      <p className="mt-3 whitespace-pre-wrap rounded-lg bg-ink-50 p-3 text-[13px] text-ink-700">{evaluation.taskPrompt}</p>
      <p className="mt-3 whitespace-pre-wrap text-[13.5px] leading-relaxed text-ink-800">{evaluation.essay}</p>
    </details>
  );
}

function OverrideForm({ evaluation }: { evaluation: WritingEvaluation }) {
  const client = useQueryClient();
  const initial = (key: string) => evaluation.criteria.find((c) => c.key === key)?.teacherBand ?? null;
  const [band, setBand] = useState<number>(evaluation.summary.teacherBand ?? evaluation.summary.estimatedBand ?? 6);
  const [criteria, setCriteria] = useState<Record<string, number | null>>({
    taskResponse: initial("taskResponse"),
    coherenceCohesion: initial("coherenceCohesion"),
    lexicalResource: initial("lexicalResource"),
    grammaticalRange: initial("grammaticalRange"),
  });
  const [comment, setComment] = useState(evaluation.teacherComment ?? "");

  const save = useMutation({
    mutationFn: () =>
      overrideWritingEvaluation(evaluation.summary.id, {
        band,
        taskResponse: criteria.taskResponse,
        coherenceCohesion: criteria.coherenceCohesion,
        lexicalResource: criteria.lexicalResource,
        grammaticalRange: criteria.grammaticalRange,
        comment,
      }),
    onSuccess: () => client.invalidateQueries({ queryKey: ["ai", "writing"] }),
  });

  return (
    <form
      className="space-y-3 rounded-xl border border-border bg-ink-50 p-4"
      onSubmit={(e) => { e.preventDefault(); save.mutate(); }}
    >
      <p className="flex items-center gap-1.5 text-[13.5px] font-medium text-ink-900">
        <ShieldAlert className="size-4 text-ink-500" aria-hidden />
        O&apos;qituvchi bahosi
      </p>
      <p className="text-[12.5px] text-ink-500">AI natijasi o&apos;zgarmaydi — sizning bahongiz uning yonida ko&apos;rsatiladi.</p>
      <div>
        <Label htmlFor="override-band">Umumiy band</Label>
        <Select id="override-band" value={band} onChange={(e) => setBand(Number(e.target.value))}>
          {BAND_OPTIONS.map((b) => <option key={b} value={b}>{b.toFixed(1)}</option>)}
        </Select>
      </div>
      <div className="grid grid-cols-2 gap-2">
        {evaluation.criteria.map((criterion) => (
          <div key={criterion.key}>
            <Label htmlFor={`override-${criterion.key}`} className="truncate">{criterion.label}</Label>
            <Select
              id={`override-${criterion.key}`}
              value={criteria[criterion.key] ?? ""}
              onChange={(e) => setCriteria((prev) => ({ ...prev, [criterion.key]: e.target.value === "" ? null : Number(e.target.value) }))}
            >
              <option value="">—</option>
              {BAND_OPTIONS.map((b) => <option key={b} value={b}>{b.toFixed(1)}</option>)}
            </Select>
          </div>
        ))}
      </div>
      <div>
        <Label htmlFor="override-comment">Izoh</Label>
        <Textarea id="override-comment" rows={3} value={comment} onChange={(e) => setComment(e.target.value)} />
      </div>
      {save.isError && <Alert>{errorText(save.error, "Saqlab bo'lmadi.")}</Alert>}
      {save.isSuccess && <Alert tone="success">Baho saqlandi.</Alert>}
      <Button type="submit" size="sm" loading={save.isPending}>Bahoni saqlash</Button>
    </form>
  );
}
