"use client";

import { Suspense, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { FileText, PenLine, Send } from "lucide-react";
import {
  createWritingEvaluation, getAiStatus, isWritingPending, listMyWritingEvaluations,
  WRITING_TASK_LABEL, type WritingTaskType,
} from "@/lib/api/ai";
import { ApiError } from "@/lib/api-client";
import { PageHeader } from "@/components/ui/PageHeader";
import { Alert } from "@/components/ui/Alert";
import { Button } from "@/components/ui/Button";
import { Card, CardBody, CardHeader, CardTitle } from "@/components/ui/Card";
import { EmptyState } from "@/components/ui/EmptyState";
import { Label, Select, Textarea } from "@/components/ui/Field";
import { Skeleton } from "@/components/ui/Skeleton";
import { WritingEvaluationView, WritingStatusBadge, formatBand, useWritingEvaluation } from "@/components/ai/WritingEvaluationView";
import { cn } from "@/lib/cn";
import { formatDateTime } from "@/lib/format-date";

const MIN_WORDS = 50;
const RECOMMENDED_WORDS: Record<WritingTaskType, number | null> = { IeltsTask1: 150, IeltsTask2: 250, General: null };

const countWords = (text: string) => (text.match(/[\p{L}\p{N}]+(?:['’-][\p{L}\p{N}]+)*/gu) ?? []).length;
const errorText = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

export default function AiWritingPage() {
  return (
    <main className="p-4 sm:p-6 lg:p-8">
      <PageHeader
        title="AI Writing"
        description="Inshongizni yuboring — AI IELTS mezonlari bo'yicha taxminiy band, xatolar va tavsiyalarni beradi. Writing homeworklaringiz ham avtomatik tahlil qilinadi."
      />
      {/* The selected assessment lives in the URL (?evaluation=…), read with useSearchParams — which needs a Suspense boundary. */}
      <Suspense fallback={<Skeleton className="h-96" />}>
        <AiWritingWorkspace />
      </Suspense>
    </main>
  );
}

function AiWritingWorkspace() {
  const router = useRouter();
  const selectedId = useSearchParams().get("evaluation");
  const select = (id: string) => router.replace(`/ai/writing?evaluation=${id}`, { scroll: false });

  const statusQuery = useQuery({ queryKey: ["ai", "status"], queryFn: getAiStatus });
  const historyQuery = useQuery({
    queryKey: ["ai", "writing", "mine"],
    queryFn: () => listMyWritingEvaluations(),
    refetchInterval: (query) => (query.state.data?.some((e) => isWritingPending(e.status)) ? 5000 : false),
  });
  const evaluationQuery = useWritingEvaluation(selectedId);

  const writing = statusQuery.data?.features.find((f) => f.feature === "Writing");
  if (statusQuery.isLoading) return <Skeleton className="h-96" />;
  if (statusQuery.isError) return <Alert>{errorText(statusQuery.error, "Yuklab bo'lmadi.")}</Alert>;
  if (!writing?.allowed) {
    return (
      <div className="rounded-xl border border-border bg-surface">
        <EmptyState icon={PenLine} title="AI Writing faqat studentlar uchun" description="O'qituvchilar AI tahlilini homework topshiriqlari ichida ko'radi." />
      </div>
    );
  }

  return (
    <div className="space-y-4">
      {statusQuery.data?.demoMode && (
        <Alert tone="info">
          Demo rejim: haqiqiy AI provayder hali ulanmagan, natijalar sinov uchun avtomatik yaratiladi.
        </Alert>
      )}

      <div className="grid grid-cols-1 gap-6 xl:grid-cols-[420px_1fr]">
        <div className="space-y-6">
          <NewEssayForm
            remaining={writing.remaining}
            limit={writing.limit}
            available={writing.available}
            onCreated={select}
          />

          <Card>
            <CardHeader>
              <CardTitle>Tarix</CardTitle>
            </CardHeader>
            {historyQuery.isLoading && <CardBody><Skeleton className="h-24" /></CardBody>}
            {historyQuery.isError && <CardBody><Alert>{errorText(historyQuery.error, "Yuklab bo'lmadi.")}</Alert></CardBody>}
            {historyQuery.data?.length === 0 && <EmptyState icon={FileText} title="Hali insho yubormagansiz" />}
            <ul className="divide-y divide-border">
              {historyQuery.data?.map((item) => (
                <li key={item.id}>
                  <button
                    onClick={() => select(item.id)}
                    className={cn(
                      "flex w-full items-center gap-3 px-5 py-3 text-left transition-colors hover:bg-ink-50",
                      item.id === selectedId && "bg-brand-50 hover:bg-brand-50",
                    )}
                  >
                    <div className="min-w-0 flex-1">
                      <p className="truncate text-[13.5px] font-medium text-ink-900">{item.taskTitle}</p>
                      <p className="text-[12px] text-ink-500">
                        {WRITING_TASK_LABEL[item.taskType]} · {item.wordCount} so&apos;z ·{" "}
                        {formatDateTime(item.createdAt)}
                        {item.homeworkId && " · homework"}
                      </p>
                    </div>
                    {item.status === "Completed" ? (
                      <span className="font-tabular text-[16px] font-semibold text-brand-700">
                        {formatBand(item.teacherBand ?? item.estimatedBand)}
                      </span>
                    ) : (
                      <WritingStatusBadge status={item.status} />
                    )}
                  </button>
                </li>
              ))}
            </ul>
          </Card>
        </div>

        <div>
          {!selectedId && (
            <div className="rounded-xl border border-border bg-surface">
              <EmptyState icon={PenLine} title="Natija shu yerda ko'rinadi" description="Yangi insho yuboring yoki tarixdan birini tanlang." />
            </div>
          )}
          {selectedId && evaluationQuery.isLoading && <Skeleton className="h-96" />}
          {selectedId && evaluationQuery.isError && <Alert>{errorText(evaluationQuery.error, "Natijani yuklab bo'lmadi.")}</Alert>}
          {evaluationQuery.data && <WritingEvaluationView evaluation={evaluationQuery.data} />}
        </div>
      </div>
    </div>
  );
}

function NewEssayForm({
  remaining,
  limit,
  available,
  onCreated,
}: {
  remaining: number | null;
  limit: number | null;
  available: boolean;
  onCreated: (id: string) => void;
}) {
  const client = useQueryClient();
  const [taskType, setTaskType] = useState<WritingTaskType>("IeltsTask2");
  const [taskPrompt, setTaskPrompt] = useState("");
  const [essay, setEssay] = useState("");
  const words = countWords(essay);
  const recommended = RECOMMENDED_WORDS[taskType];

  const create = useMutation({
    mutationFn: () => createWritingEvaluation({ taskType, taskPrompt, essay }),
    onSuccess: (evaluation) => {
      setEssay("");
      client.invalidateQueries({ queryKey: ["ai"] });
      onCreated(evaluation.summary.id);
    },
  });

  const outOfAllowance = remaining !== null && remaining <= 0;

  return (
    <Card>
      <CardHeader>
        <CardTitle>Yangi insho</CardTitle>
        {limit !== null && (
          <span className="text-[12.5px] text-ink-500">Bugun qoldi: <b className="font-tabular text-ink-800">{remaining}</b> / {limit}</span>
        )}
      </CardHeader>
      <CardBody>
        <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); create.mutate(); }}>
          <div>
            <Label htmlFor="writing-type">Topshiriq turi</Label>
            <Select id="writing-type" value={taskType} onChange={(e) => setTaskType(e.target.value as WritingTaskType)}>
              {(Object.keys(WRITING_TASK_LABEL) as WritingTaskType[]).map((type) => (
                <option key={type} value={type}>{WRITING_TASK_LABEL[type]}</option>
              ))}
            </Select>
          </div>
          <div>
            <Label htmlFor="writing-task">Topshiriq (savol)</Label>
            <Textarea
              id="writing-task"
              rows={3}
              required
              maxLength={2000}
              value={taskPrompt}
              onChange={(e) => setTaskPrompt(e.target.value)}
              placeholder="Masalan: Some people think that university education should be free. To what extent do you agree?"
            />
          </div>
          <div>
            <Label htmlFor="writing-essay">Insho</Label>
            <Textarea
              id="writing-essay"
              rows={12}
              required
              maxLength={9000}
              value={essay}
              onChange={(e) => setEssay(e.target.value)}
              placeholder="Inshongizni ingliz tilida shu yerga yozing..."
            />
            <p className={cn("mt-1 text-[12.5px]", words < MIN_WORDS ? "text-ink-400" : recommended && words < recommended ? "text-warning-600" : "text-success-600")}>
              {words} so&apos;z
              {recommended ? ` · tavsiya etilgan minimum ${recommended}` : ""}
              {words < MIN_WORDS && ` · yuborish uchun kamida ${MIN_WORDS}`}
            </p>
          </div>

          {!available && <Alert>AI xizmati hozircha sozlanmagan.</Alert>}
          {outOfAllowance && <Alert tone="info">Bugungi limit tugadi. Ertaga yana yuborishingiz mumkin.</Alert>}
          {create.isError && <Alert>{errorText(create.error, "Yuborib bo'lmadi.")}</Alert>}

          <Button
            type="submit"
            className="w-full"
            loading={create.isPending}
            disabled={!available || outOfAllowance || words < MIN_WORDS || !taskPrompt.trim()}
          >
            <Send className="size-4" aria-hidden />
            Baholashga yuborish
          </Button>
        </form>
      </CardBody>
    </Card>
  );
}
