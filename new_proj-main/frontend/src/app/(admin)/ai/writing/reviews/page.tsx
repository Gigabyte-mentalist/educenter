"use client";

import { Suspense, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { useQuery } from "@tanstack/react-query";
import { FileText, PenLine } from "lucide-react";
import {
  isWritingPending, listWritingReviews, WRITING_TASK_LABEL, type WritingEvaluationSummary,
} from "@/lib/api/ai";
import { ApiError } from "@/lib/api-client";
import { PageHeader } from "@/components/ui/PageHeader";
import { Alert } from "@/components/ui/Alert";
import { Card, CardBody, CardHeader, CardTitle } from "@/components/ui/Card";
import { EmptyState } from "@/components/ui/EmptyState";
import { Skeleton } from "@/components/ui/Skeleton";
import { Select } from "@/components/ui/Field";
import { WritingEvaluationView, WritingStatusBadge, formatBand, useWritingEvaluation } from "@/components/ai/WritingEvaluationView";
import { cn } from "@/lib/cn";
import { formatShortDateTime } from "@/lib/format-date";

const errorText = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

/**
 * The teacher's side of Writing AI. The endpoint behind it existed from the start but demanded a studentId or a
 * homeworkId, so there was no request a teacher could make without already knowing which essay to look at —
 * which is why students saw their results and their teachers did not.
 */
export default function WritingReviewsPage() {
  return (
    <main className="p-4 sm:p-6 lg:p-8">
      <PageHeader
        title="AI Writing — tekshirish"
        description="Guruhlaringizdagi o'quvchilarning inshosi va AI bahosi. Bahoni tasdiqlashingiz yoki o'zgartirishingiz mumkin."
      />
      <Suspense fallback={<Skeleton className="h-96" />}>
        <ReviewWorkspace />
      </Suspense>
    </main>
  );
}

function ReviewWorkspace() {
  const router = useRouter();
  const selectedId = useSearchParams().get("evaluation");
  const select = (id: string) => router.replace(`/ai/writing/reviews?evaluation=${id}`, { scroll: false });

  const [filter, setFilter] = useState<"pending" | "all">("pending");

  const listQuery = useQuery({
    queryKey: ["ai", "writing", "reviews", filter],
    queryFn: () => listWritingReviews(filter === "pending" ? { pendingOnly: true } : {}),
    // Essays land here from the background evaluator, so an open screen keeps catching up while any are running.
    refetchInterval: (query) => (query.state.data?.some((e) => isWritingPending(e.status)) ? 5000 : false),
  });
  const evaluationQuery = useWritingEvaluation(selectedId);

  const items = listQuery.data ?? [];

  return (
    <div className="grid grid-cols-1 gap-6 xl:grid-cols-[400px_1fr]">
      <Card className="xl:sticky xl:top-6 xl:self-start">
        <CardHeader className="flex items-center justify-between gap-3">
          <CardTitle>Insholar</CardTitle>
          <Select
            value={filter}
            onChange={(e) => setFilter(e.target.value as "pending" | "all")}
            className="h-8 w-40 text-[13px]"
            aria-label="Filtr"
          >
            <option value="pending">Tekshirilmagan</option>
            <option value="all">Hammasi</option>
          </Select>
        </CardHeader>

        {listQuery.isLoading && (
          <CardBody>
            <Skeleton className="h-32" />
          </CardBody>
        )}
        {listQuery.isError && (
          <CardBody>
            <Alert>{errorText(listQuery.error, "Yuklab bo'lmadi.")}</Alert>
          </CardBody>
        )}
        {listQuery.data && items.length === 0 && (
          <EmptyState
            icon={FileText}
            title={filter === "pending" ? "Tekshiriladigan insho yo'q" : "Hali insho yo'q"}
            description={
              filter === "pending"
                ? "Guruhlaringizdagi barcha insholar tekshirilgan."
                : "O'quvchilar insho yuborganda ular shu yerda ko'rinadi."
            }
          />
        )}

        <ul className="divide-y divide-border">
          {items.map((item) => (
            <li key={item.id}>
              <ReviewRow item={item} selected={item.id === selectedId} onSelect={() => select(item.id)} />
            </li>
          ))}
        </ul>
      </Card>

      <div>
        {!selectedId && (
          <div className="rounded-xl border border-border bg-surface">
            <EmptyState
              icon={PenLine}
              title="Inshoni tanlang"
              description="Chapdagi ro'yxatdan birini tanlang — to'liq matn, AI tahlili va baho qo'yish shu yerda ochiladi."
            />
          </div>
        )}
        {evaluationQuery.isLoading && selectedId && <Skeleton className="h-96" />}
        {evaluationQuery.isError && <Alert>{errorText(evaluationQuery.error, "Baholashni yuklab bo'lmadi.")}</Alert>}
        {evaluationQuery.data && <WritingEvaluationView evaluation={evaluationQuery.data} />}
      </div>
    </div>
  );
}

function ReviewRow({
  item,
  selected,
  onSelect,
}: {
  item: WritingEvaluationSummary;
  selected: boolean;
  onSelect: () => void;
}) {
  return (
    <button
      onClick={onSelect}
      className={cn(
        "flex w-full items-center gap-3 px-5 py-3 text-left transition-colors hover:bg-ink-50",
        selected && "bg-brand-50 hover:bg-brand-50",
      )}
    >
      <div className="min-w-0 flex-1">
        <p className="truncate text-[13.5px] font-medium text-ink-900">{item.studentName}</p>
        <p className="truncate text-[12px] text-ink-500">
          {WRITING_TASK_LABEL[item.taskType]} · {item.wordCount} so&apos;z ·{" "}
          {formatShortDateTime(item.createdAt)}
          {item.homeworkId && " · homework"}
        </p>
      </div>
      {item.status === "Completed" ? (
        <div className="shrink-0 text-right">
          <p className="font-tabular text-[16px] font-semibold text-brand-700">
            {formatBand(item.teacherBand ?? item.estimatedBand)}
          </p>
          <p className="text-[11px] text-ink-500">{item.teacherBand === null ? "AI bahosi" : "tasdiqlangan"}</p>
        </div>
      ) : (
        <WritingStatusBadge status={item.status} />
      )}
    </button>
  );
}
