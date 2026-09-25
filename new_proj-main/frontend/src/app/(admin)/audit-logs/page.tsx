"use client";

import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { ScrollText } from "lucide-react";
import { getAuditFilters, listAuditLogs } from "@/lib/api/audit-logs";
import { ApiError } from "@/lib/api-client";
import { PageHeader } from "@/components/ui/PageHeader";
import { Badge } from "@/components/ui/Badge";
import { Table, THead, TBody, TRow, TH, TD } from "@/components/ui/Table";
import { TableSkeleton } from "@/components/ui/Skeleton";
import { EmptyState } from "@/components/ui/EmptyState";
import { Alert } from "@/components/ui/Alert";
import { Pagination } from "@/components/ui/Pagination";
import { Select } from "@/components/ui/Field";
import { formatShortDateTime } from "@/lib/format-date";

const ACTION_TONE: Record<string, "success" | "info" | "danger"> = {
  Added: "success",
  Modified: "info",
  Deleted: "danger",
};

const ACTION_LABEL: Record<string, string> = {
  Added: "Qo'shildi",
  Modified: "O'zgardi",
  Deleted: "O'chirildi",
};

const PAGE_SIZE = 50;

export default function AuditLogsPage() {
  const [page, setPage] = useState(1);
  const [action, setAction] = useState("");
  const [resource, setResource] = useState("");

  const logsQuery = useQuery({
    queryKey: ["audit-logs", page, action, resource],
    queryFn: () =>
      listAuditLogs({ page, size: PAGE_SIZE, action: action || undefined, resource: resource || undefined }),
  });
  const filtersQuery = useQuery({ queryKey: ["audit-logs", "filters"], queryFn: getAuditFilters });

  const logs = logsQuery.data?.items ?? [];
  const resetTo = (apply: () => void) => {
    apply();
    setPage(1);
  };

  return (
    <main className="p-4 sm:p-6 lg:p-8">
      <PageHeader
        title="Audit jurnali"
        description="Filial, foydalanuvchi va rol o'zgarishlari kabi muhim amallar tarixi."
      />

      <div className="mb-4 flex flex-col gap-2 sm:flex-row">
        <Select
          value={action}
          onChange={(e) => resetTo(() => setAction(e.target.value))}
          className="sm:w-48"
          aria-label="Amal bo'yicha filtr"
        >
          <option value="">Barcha amallar</option>
          {filtersQuery.data?.actions.map((a) => (
            <option key={a} value={a}>
              {ACTION_LABEL[a] ?? a}
            </option>
          ))}
        </Select>
        <Select
          value={resource}
          onChange={(e) => resetTo(() => setResource(e.target.value))}
          className="sm:w-56"
          aria-label="Resurs bo'yicha filtr"
        >
          <option value="">Barcha resurslar</option>
          {filtersQuery.data?.resources.map((r) => (
            <option key={r} value={r}>
              {r}
            </option>
          ))}
        </Select>
      </div>

      {logsQuery.isError && (
        <Alert>{logsQuery.error instanceof ApiError ? logsQuery.error.message : "Yuklab bo'lmadi."}</Alert>
      )}

      {logsQuery.isLoading && (
        <div className="rounded-xl border border-border bg-surface">
          <TableSkeleton rows={8} cols={4} />
        </div>
      )}

      {logsQuery.data && logs.length === 0 && (
        <div className="rounded-xl border border-border bg-surface">
          <EmptyState
            icon={ScrollText}
            title={action || resource ? "Bu filtr bo'yicha yozuv yo'q" : "Hali audit yozuvi yo'q"}
          />
        </div>
      )}

      {logsQuery.data && logs.length > 0 && (
        <div className="overflow-hidden rounded-xl border border-border bg-surface">
          <Table bare>
              <THead>
                <tr>
                  <TH>Vaqt</TH>
                  <TH>Kim</TH>
                  <TH>Amal</TH>
                  <TH>Resurs</TH>
                  <TH className="hidden lg:table-cell">Resource ID</TH>
                </tr>
              </THead>
              <TBody>
                {logs.map((log) => (
                  <TRow key={log.id}>
                    <TD className="whitespace-nowrap font-tabular text-[13px]">
                      {formatShortDateTime(log.createdAt)}
                    </TD>
                    <TD className="text-ink-700">
                      {log.userName ?? <span className="text-ink-400">O&apos;chirilgan foydalanuvchi</span>}
                    </TD>
                    <TD>
                      <Badge tone={ACTION_TONE[log.action] ?? "neutral"}>{ACTION_LABEL[log.action] ?? log.action}</Badge>
                    </TD>
                    <TD className="font-medium text-ink-800">{log.resource}</TD>
                    <TD className="hidden font-mono text-[12px] text-ink-500 lg:table-cell">{log.resourceId}</TD>
                  </TRow>
                ))}
              </TBody>
          </Table>
          <Pagination
            page={logsQuery.data.pageNumber}
            pageCount={logsQuery.data.pageCount}
            total={logsQuery.data.total}
            pageSize={logsQuery.data.pageSize}
            onChange={setPage}
          />
        </div>
      )}
    </main>
  );
}
