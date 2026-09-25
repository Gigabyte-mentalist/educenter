"use client";

import Link from "next/link";
import { useQuery, type UseQueryResult } from "@tanstack/react-query";
import { ClipboardList } from "lucide-react";
import { type ReactNode } from "react";
import { getStudentAttendance, getStudentHomework, getStudentExams } from "@/lib/api/students";
import { fetchHomeworkAttachment, fetchSubmissionAttachment } from "@/lib/api/lms";
import { DownloadFileButton } from "@/components/HomeworkFiles";
import { ApiError } from "@/lib/api-client";
import { Alert } from "@/components/ui/Alert";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { EmptyState } from "@/components/ui/EmptyState";
import { TableSkeleton } from "@/components/ui/Skeleton";
import { Table, THead, TBody, TRow, TH, TD } from "@/components/ui/Table";
import { formatDateTime } from "@/lib/format-date";

function QueryPanel<T>({ query, empty, children }: { query: UseQueryResult<T[]>; empty: string; children: (rows: T[]) => ReactNode }) {
  if (query.isPending) return <TableSkeleton rows={4} cols={4} />;
  if (query.isError) return <div className="space-y-3">
    <Alert>{query.error instanceof ApiError ? query.error.message : "Ma’lumotlarni yuklab bo‘lmadi."}</Alert>
    <Button variant="secondary" loading={query.isFetching} onClick={() => void query.refetch()}>Qayta urinish</Button>
  </div>;
  if (!query.data.length) return <EmptyState icon={ClipboardList} title={empty} />;
  return <div className="overflow-x-auto">{children(query.data)}</div>;
}

const attendanceLabels = { Present: "Keldi", Absent: "Kelmadi", Late: "Kechikdi", Excused: "Sababli", Online: "Onlayn" };
const localDateTime = formatDateTime;

export function StudentAttendanceTab({ studentId }: { studentId: string }) {
  const query = useQuery({ queryKey: ["students", studentId, "attendance"], queryFn: () => getStudentAttendance(studentId) });
  return <QueryPanel query={query} empty="Hali davomat belgilanmagan">{rows => <Table>
    <THead><tr><TH>Sana</TH><TH>Guruh / mavzu</TH><TH>Holat</TH></tr></THead>
    <TBody>{rows.map(row => <TRow key={row.id}>
      <TD>{localDateTime(row.startsAt)}</TD>
      <TD><Link className="text-brand-700 hover:underline" href={`/schedule/${row.lessonId}`}>{row.groupName}</Link><p className="text-sm text-ink-500">{row.topic ?? "Mavzu kiritilmagan"}</p></TD>
      <TD><Badge tone={row.status === "Absent" ? "danger" : row.status === "Late" ? "warning" : "success"}>{attendanceLabels[row.status]}</Badge></TD>
    </TRow>)}</TBody>
  </Table>}</QueryPanel>;
}

export function StudentHomeworkTab({ studentId }: { studentId: string }) {
  const query = useQuery({ queryKey: ["students", studentId, "homework"], queryFn: () => getStudentHomework(studentId) });
  return <QueryPanel query={query} empty="Hali homework berilmagan">{rows => <Table>
    <THead><tr><TH>Topshiriq</TH><TH>Muddat</TH><TH>Holat</TH><TH>Baho / izoh</TH></tr></THead>
    <TBody>{rows.map(row => <TRow key={row.id}>
      <TD><Link className="text-brand-700 hover:underline" href={`/schedule/${row.lessonId}`}>{row.title}</Link><p className="text-sm text-ink-500">{row.groupName}</p>
        {row.attachmentName && <div className="mt-1"><DownloadFileButton name={row.attachmentName} load={() => fetchHomeworkAttachment(row.id)} /></div>}</TD>
      <TD>{localDateTime(row.deadline)}</TD>
      <TD><Badge tone={row.score !== null ? "success" : row.submittedAt ? "brand" : "neutral"}>{row.score !== null ? "Baholangan" : row.submittedAt ? "Tekshirilmoqda" : "Topshirilmagan"}</Badge>{row.submittedAt && <p className="mt-1 text-xs text-ink-500">{localDateTime(row.submittedAt)}</p>}
        {row.submissionId && row.submissionFileName && <div className="mt-1"><DownloadFileButton name={row.submissionFileName} load={() => fetchSubmissionAttachment(row.submissionId!)} /></div>}</TD>
      <TD>{row.score !== null ? `${row.score} / ${row.maxScore}` : "—"}{row.feedback && <p className="max-w-sm whitespace-pre-wrap text-sm text-ink-500">{row.feedback}</p>}</TD>
    </TRow>)}</TBody>
  </Table>}</QueryPanel>;
}

export function StudentExamsTab({ studentId }: { studentId: string }) {
  const query = useQuery({ queryKey: ["students", studentId, "exams"], queryFn: () => getStudentExams(studentId) });
  return <QueryPanel query={query} empty="Hali imtihon natijalari yo‘q">{rows => <Table>
    <THead><tr><TH>Imtihon</TH><TH>Sana</TH><TH>Listening</TH><TH>Reading</TH><TH>Writing</TH><TH>Speaking</TH><TH>Overall</TH><TH>Grammatika %</TH><TH>Lug‘at %</TH></tr></THead>
    <TBody>{rows.map(row => <TRow key={row.id}>
      <TD>{row.name}<p className="text-sm text-ink-500">{row.groupName}</p></TD><TD>{row.date}</TD>
      <TD>{row.listening ?? "—"}</TD><TD>{row.reading ?? "—"}</TD><TD>{row.writing ?? "—"}</TD><TD>{row.speaking ?? "—"}</TD><TD>{row.overall ?? "—"}</TD>
      <TD>{row.grammar ?? "—"}</TD><TD>{row.vocabulary ?? "—"}</TD>
    </TRow>)}</TBody>
  </Table>}</QueryPanel>;
}
