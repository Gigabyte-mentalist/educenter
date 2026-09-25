"use client";

import { useState } from "react";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { ScrollText } from "lucide-react";
import { listAuditLogs } from "@/lib/api/audit-logs";
import { ApiError } from "@/lib/api-client";
import { formatShortDateTime } from "@/lib/format-date";
import { Alert } from "@/components/ui/Alert";
import { Badge } from "@/components/ui/Badge";
import { EmptyState } from "@/components/ui/EmptyState";
import { Pagination } from "@/components/ui/Pagination";
import { TableSkeleton } from "@/components/ui/Skeleton";
import { Table, THead, TBody, TRow, TH, TD } from "@/components/ui/Table";

const ACTION_TONE: Record<string, "success" | "info" | "danger"> = { Added: "success", Modified: "info", Deleted: "danger" };
const ACTION_LABEL: Record<string, string> = { Added: "Qo‘shdi", Modified: "O‘zgartirdi", Deleted: "O‘chirdi" };
const PAGE_SIZE = 25;

/** Faoliyat tarixi tab: the audit trail of what this teacher did — the audit log filtered to them as the actor. */
export function TeacherActivityTab({ teacherId }: { teacherId: string }) {
  const [page, setPage] = useState(1);
  const query = useQuery({
    queryKey: ["audit-logs", "actor", teacherId, page],
    queryFn: () => listAuditLogs({ userId: teacherId, page, size: PAGE_SIZE }),
    placeholderData: keepPreviousData,
  });

  if (query.isPending) return <TableSkeleton rows={6} cols={3} />;
  if (query.isError) return <Alert>{query.error instanceof ApiError ? query.error.message : "Tarixni yuklab bo‘lmadi."}</Alert>;
  const logs = query.data.items;
  if (logs.length === 0) return <EmptyState icon={ScrollText} title="Hali faoliyat yozuvi yo‘q" description="Audit jurnaliga tushadigan amallar shu yerda ko‘rinadi." />;

  return (
    <div className="overflow-hidden rounded-xl border border-border bg-surface">
      <Table bare>
        <THead><tr><TH>Vaqt</TH><TH>Amal</TH><TH>Obyekt</TH></tr></THead>
        <TBody>{logs.map((log) => (
          <TRow key={log.id}>
            <TD className="whitespace-nowrap font-tabular text-[13px]">{formatShortDateTime(log.createdAt)}</TD>
            <TD><Badge tone={ACTION_TONE[log.action] ?? "neutral"}>{ACTION_LABEL[log.action] ?? log.action}</Badge></TD>
            <TD className="font-medium text-ink-800">{log.resource}</TD>
          </TRow>
        ))}</TBody>
      </Table>
      <Pagination page={query.data.pageNumber} pageCount={query.data.pageCount} total={query.data.total}
        pageSize={query.data.pageSize} onChange={setPage} />
    </div>
  );
}
