"use client";

import { Suspense, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { useQuery } from "@tanstack/react-query";
import { Headphones, Mic } from "lucide-react";
import { listSpeakingReviews, type SpeakingReviewRow } from "@/lib/api/ai";
import { ApiError } from "@/lib/api-client";
import { PageHeader } from "@/components/ui/PageHeader";
import { Alert } from "@/components/ui/Alert";
import { Card, CardBody, CardHeader, CardTitle } from "@/components/ui/Card";
import { EmptyState } from "@/components/ui/EmptyState";
import { Skeleton } from "@/components/ui/Skeleton";
import { Select } from "@/components/ui/Field";
import { SpeakingReviewPanel } from "@/components/ai/SpeakingReviewPanel";
import { formatBand, formatDuration } from "@/components/ai/SpeakingSessionView";
import { cn } from "@/lib/cn";
import { formatShortDateTime } from "@/lib/format-date";

const errorText = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

/**
 * The teacher's side of Speaking practice: the finished sessions of their students, to listen to and, where the AI got it wrong, to
 * grade. Students are told their finished practice is visible here. Sits beside the Writing review, and works the same way.
 */
export default function SpeakingReviewsPage() {
  return (
    <main className="p-4 sm:p-6 lg:p-8">
      <PageHeader
        title="AI Speaking — tekshirish"
        description="O'quvchilaringizning tugallangan Speaking mashqlari. Yozuvni eshiting va AI noto'g'ri baholagan bo'lsa o'z bahoingizni qo'ying."
      />
      <Suspense fallback={<Skeleton className="h-96" />}>
        <ReviewWorkspace />
      </Suspense>
    </main>
  );
}

function ReviewWorkspace() {
  const router = useRouter();
  const params = useSearchParams();
  const selectedStudent = params.get("student");
  const selectedSession = params.get("session");
  const select = (row: SpeakingReviewRow) => router.replace(`/ai/speaking/reviews?student=${row.studentId}&session=${row.id}`, { scroll: false });

  const [filter, setFilter] = useState<"pending" | "all">("pending");
  const list = useQuery({
    queryKey: ["speaking-reviews", filter],
    queryFn: () => listSpeakingReviews(filter === "pending" ? { pendingOnly: true } : {}),
  });
  const items = list.data ?? [];

  return (
    <div className="grid grid-cols-1 gap-6 xl:grid-cols-[400px_1fr]">
      <Card className="xl:sticky xl:top-6 xl:self-start">
        <CardHeader className="flex items-center justify-between gap-3">
          <CardTitle>Mashqlar</CardTitle>
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

        {list.isLoading && <CardBody><Skeleton className="h-32" /></CardBody>}
        {list.isError && <CardBody><Alert>{errorText(list.error, "Yuklab bo'lmadi.")}</Alert></CardBody>}
        {list.data && items.length === 0 && (
          <EmptyState
            icon={Headphones}
            title={filter === "pending" ? "Tekshiriladigan mashq yo'q" : "Hali mashq yo'q"}
            description={
              filter === "pending"
                ? "Guruhlaringizdagi barcha tugallangan mashqlar tekshirilgan."
                : "O'quvchilar Speaking mashqini tugatganda u shu yerda ko'rinadi."
            }
          />
        )}

        <ul className="divide-y divide-border">
          {items.map((item) => (
            <li key={item.id}>
              <ReviewRow item={item} selected={item.id === selectedSession} onSelect={() => select(item)} />
            </li>
          ))}
        </ul>
      </Card>

      <div>
        {selectedStudent && selectedSession ? (
          <SpeakingReviewPanel key={selectedSession} studentId={selectedStudent} sessionId={selectedSession} />
        ) : (
          <div className="rounded-xl border border-border bg-surface">
            <EmptyState
              icon={Mic}
              title="Mashqni tanlang"
              description="Chapdagi ro'yxatdan birini tanlang — AI tahlili, suhbat matni, ovoz yozuvi va baho qo'yish shu yerda ochiladi."
            />
          </div>
        )}
      </div>
    </div>
  );
}

function ReviewRow({ item, selected, onSelect }: { item: SpeakingReviewRow; selected: boolean; onSelect: () => void }) {
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
          {item.modeLabel} · {formatDuration(item.spokenSeconds)}
          {item.completedAt && <> · {formatShortDateTime(item.completedAt)}</>}
        </p>
      </div>
      <div className="shrink-0 text-right">
        <p className="font-tabular text-[16px] font-semibold text-brand-700">{formatBand(item.teacherBand ?? item.estimatedBand)}</p>
        <p className="text-[11px] text-ink-500">{item.teacherBand === null ? "AI bahosi" : "tasdiqlangan"}</p>
      </div>
    </button>
  );
}
