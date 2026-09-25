"use client";

import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { PenLine, UserCheck } from "lucide-react";
import {
  isWritingPending, listMyWritingEvaluations, listWritingReviews, WRITING_TASK_LABEL, type WritingEvaluationSummary,
} from "@/lib/api/ai";
import { ApiError } from "@/lib/api-client";
import { Alert } from "@/components/ui/Alert";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { Drawer } from "@/components/ui/Drawer";
import { EmptyState } from "@/components/ui/EmptyState";
import { Skeleton, TableSkeleton } from "@/components/ui/Skeleton";
import { Table, THead, TBody, TRow, TH, TD } from "@/components/ui/Table";
import { WritingEvaluationView, WritingStatusBadge, formatBand, useWritingEvaluation } from "@/components/ai/WritingEvaluationView";
import { formatShortDateTime } from "@/lib/format-date";

const errorText = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

/** The band that stands for an essay: the teacher's, where they corrected the AI. */
const bandOf = (e: WritingEvaluationSummary) => e.teacherBand ?? e.estimatedBand;
const momentOf = (e: WritingEvaluationSummary) => e.completedAt ?? e.createdAt;

/**
 * The student's essays graded by the Writing AI. Staff read them through the reviewers' list, filtered to this student
 * (`ai.writing.review`, within their own scope); a student reads their own (`ai.writing.use`) — the two endpoints already
 * exist, this tab only puts them on the profile.
 */
export function StudentWritingTab({ studentId, staffView }: { studentId: string; staffView: boolean }) {
  const [openId, setOpenId] = useState<string | null>(null);
  const query = useQuery({
    queryKey: ["students", studentId, "writing", staffView ? "reviews" : "mine"],
    queryFn: () => (staffView ? listWritingReviews({ studentId }) : listMyWritingEvaluations()),
    // An essay lands here from the background evaluator, so an open tab keeps catching up while any is still running.
    refetchInterval: (q) => (q.state.data?.some((e) => isWritingPending(e.status)) ? 5000 : false),
  });
  const evaluation = useWritingEvaluation(openId);

  if (query.isPending) return <TableSkeleton rows={4} cols={5} />;
  if (query.isError) {
    return (
      <div className="space-y-3">
        <Alert>{errorText(query.error, "Insholarni yuklab bo‘lmadi.")}</Alert>
        <Button variant="secondary" loading={query.isFetching} onClick={() => void query.refetch()}>Qayta urinish</Button>
      </div>
    );
  }
  if (query.data.length === 0) return <EmptyState icon={PenLine} title="Hali insho yozilmagan" />;

  const rows = [...query.data].sort((a, b) => momentOf(b).localeCompare(momentOf(a)));
  // Plain numbers, oldest to newest — enough to see the direction without a chart library.
  const history = rows.filter((e) => e.status === "Completed" && bandOf(e) !== null).slice(0, 8).reverse().map((e) => formatBand(bandOf(e)));

  return (
    <div className="space-y-4">
      {history.length > 1 && (
        <p className="text-[13px] text-ink-500">
          So‘nggi baholar: <span className="font-tabular font-medium text-ink-800">{history.join(" → ")}</span>
        </p>
      )}
      <div className="overflow-x-auto">
        <Table>
          <THead><tr><TH>Sana</TH><TH>Mavzu</TH><TH>Band</TH><TH>Holat</TH><TH><span className="sr-only">Ochish</span></TH></tr></THead>
          <TBody>{rows.map((e) => (
            <TRow key={e.id}>
              <TD>{formatShortDateTime(momentOf(e))}</TD>
              <TD>
                {e.taskTitle}
                <p className="text-sm text-ink-500">{WRITING_TASK_LABEL[e.taskType]} · {e.wordCount} so‘z{e.homeworkId ? " · homework" : ""}</p>
              </TD>
              <TD>
                <span className="font-tabular font-medium">{formatBand(bandOf(e))}</span>
                {e.teacherBand !== null && (
                  <Badge tone="success" className="ml-2"><UserCheck className="size-3" aria-hidden />Tuzatilgan</Badge>
                )}
              </TD>
              <TD><WritingStatusBadge status={e.status} /></TD>
              <TD className="text-right"><Button variant="secondary" size="sm" onClick={() => setOpenId(e.id)}>Ko‘rish</Button></TD>
            </TRow>
          ))}</TBody>
        </Table>
      </div>

      <Drawer open={openId !== null} onClose={() => setOpenId(null)} title="AI writing tahlili" description={evaluation.data?.summary.studentName}>
        {evaluation.isPending && openId !== null && <Skeleton className="h-48" />}
        {evaluation.isError && <Alert>{errorText(evaluation.error, "Tahlilni yuklab bo‘lmadi.")}</Alert>}
        {evaluation.data && <WritingEvaluationView evaluation={evaluation.data} compact />}
      </Drawer>
    </div>
  );
}
