"use client";

import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Mic } from "lucide-react";
import { listStudentSpeaking } from "@/lib/api/ai";
import { ApiError } from "@/lib/api-client";
import { Alert } from "@/components/ui/Alert";
import { Button } from "@/components/ui/Button";
import { EmptyState } from "@/components/ui/EmptyState";
import { Modal } from "@/components/ui/Modal";
import { TableSkeleton } from "@/components/ui/Skeleton";
import { Table, THead, TBody, TRow, TH, TD } from "@/components/ui/Table";
import { formatBand, formatDuration } from "@/components/ai/SpeakingSessionView";
import { SpeakingReviewPanel } from "@/components/ai/SpeakingReviewPanel";
import { formatShortDateTime } from "@/lib/format-date";

const errorText = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

/**
 * The student's finished Speaking practice for the people who teach them (`ai.speaking.review`): bands in the list, and on
 * opening one the transcript, the recordings and the assessment. The student knows this — the practice page says so — and only
 * practice done after that notice is here; the server decides that, and opening a session or a recording is logged.
 */
export function StudentSpeakingTab({ studentId }: { studentId: string }) {
  const [openId, setOpenId] = useState<string | null>(null);
  const list = useQuery({ queryKey: ["students", studentId, "speaking"], queryFn: () => listStudentSpeaking(studentId) });

  if (list.isPending) return <TableSkeleton rows={4} cols={5} />;
  if (list.isError) {
    return (
      <div className="space-y-3">
        <Alert>{errorText(list.error, "Mashqlarni yuklab bo‘lmadi.")}</Alert>
        <Button variant="secondary" loading={list.isFetching} onClick={() => void list.refetch()}>Qayta urinish</Button>
      </div>
    );
  }
  if (list.data.length === 0) return <EmptyState icon={Mic} title="Hali yakunlangan Speaking mashqi yo‘q" />;

  return (
    <div className="space-y-4">
      <div className="overflow-x-auto">
        <Table>
          <THead><tr>
            <TH>Sana</TH><TH>Mashq</TH><TH>Davomiyligi</TH><TH>AI band</TH><TH>Fluency</TH><TH>Lexical</TH><TH>Grammar</TH><TH>O&apos;qituvchi</TH>
            <TH><span className="sr-only">Ochish</span></TH>
          </tr></THead>
          <TBody>{list.data.map((row) => (
            <TRow key={row.id}>
              <TD>{formatShortDateTime(row.completedAt ?? row.createdAt)}</TD>
              <TD>{row.modeLabel}<p className="text-sm text-ink-500">{row.topic}</p></TD>
              <TD className="font-tabular">{formatDuration(row.spokenSeconds)}</TD>
              <TD className="font-tabular font-medium">{formatBand(row.estimatedBand)}</TD>
              <TD className="font-tabular">{formatBand(row.fluencyBand)}</TD>
              <TD className="font-tabular">{formatBand(row.lexicalBand)}</TD>
              <TD className="font-tabular">{formatBand(row.grammarBand)}</TD>
              <TD className="font-tabular font-medium">{formatBand(row.teacherBand)}</TD>
              <TD className="text-right"><Button variant="secondary" size="sm" onClick={() => setOpenId(row.id)}>Ko‘rish</Button></TD>
            </TRow>
          ))}</TBody>
        </Table>
      </div>
      <p className="text-xs text-ink-500">
        Taxminiy band transkript asosida beriladi, talaffuz baholanmaydi: yozuvni eshitib, AI noto‘g‘ri baholagan bo‘lsa o‘z bahoingizni
        qo‘ying. Bu raqamlar Learning profildagi Speaking ko‘rsatkichiga kirmaydi — u faqat imtihon natijasidan.
      </p>

      <Modal open={openId !== null} onClose={() => setOpenId(null)} title="Speaking mashqi" className="max-w-3xl">
        {openId !== null && <SpeakingReviewPanel studentId={studentId} sessionId={openId} />}
      </Modal>
    </div>
  );
}
