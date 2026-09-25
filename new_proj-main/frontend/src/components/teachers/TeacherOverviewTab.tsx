"use client";

import { useQuery } from "@tanstack/react-query";
import { CalendarDays, GraduationCap, Users2 } from "lucide-react";
import { getTeacherSummary } from "@/lib/api/teachers";
import { ApiError } from "@/lib/api-client";
import { formatDate } from "@/lib/format-date";
import { Alert } from "@/components/ui/Alert";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { Card, CardBody } from "@/components/ui/Card";
import { Skeleton } from "@/components/ui/Skeleton";
import { StatTile } from "@/components/ui/StatTile";

const STATUS_LABEL: Record<string, string> = { Active: "Faol", Suspended: "To‘xtatilgan" };

/** Umumiy tab: who this teacher is at a glance — where they work, since when, how many groups and students they carry. */
export function TeacherOverviewTab({ teacherId }: { teacherId: string }) {
  const query = useQuery({ queryKey: ["teachers", teacherId, "summary"], queryFn: () => getTeacherSummary(teacherId) });
  if (query.isPending) return <Skeleton className="h-40" />;
  if (query.isError) {
    return (
      <>
        <Alert>{query.error instanceof ApiError ? query.error.message : "Ma‘lumotni yuklab bo‘lmadi."}</Alert>
        <Button onClick={() => void query.refetch()}>Qayta urinish</Button>
      </>
    );
  }
  const s = query.data;
  return (
    <div className="space-y-4">
      <div className="grid gap-4 sm:grid-cols-3">
        <StatTile label="Guruhlar" value={s.groupsCount} icon={Users2} />
        <StatTile label="Faol studentlar" value={s.activeStudents} icon={GraduationCap} />
        <StatTile label="Ishga kirgan" value={formatDate(s.joinedAt)} icon={CalendarDays} />
      </div>
      <Card>
        <CardBody>
          <dl className="grid gap-x-8 gap-y-4 text-[13.5px] sm:grid-cols-2">
            <Row label="Email">{s.email}</Row>
            <Row label="Telefon">{s.phone}</Row>
            <Row label="Filial">{s.branches.length ? s.branches.join(", ") : "—"}</Row>
            <Row label="Holat">
              <Badge tone={s.status === "Active" ? "success" : "neutral"}>{STATUS_LABEL[s.status] ?? "Faol emas"}</Badge>
            </Row>
          </dl>
        </CardBody>
      </Card>
    </div>
  );
}

function Row({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <dt className="text-[12px] font-medium text-ink-500">{label}</dt>
      <dd className="mt-0.5 text-ink-900">{children}</dd>
    </div>
  );
}
