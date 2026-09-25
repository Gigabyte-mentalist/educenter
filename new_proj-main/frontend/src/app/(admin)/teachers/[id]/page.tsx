"use client";

import { use, useState } from "react";
import Link from "next/link";
import { useQuery } from "@tanstack/react-query";
import { Users2, CalendarClock, TrendingUp, ClipboardList, Award } from "lucide-react";
import { getTeacher, getTeacherPerformance, getTeacherPortfolio, type TeacherOverview } from "@/lib/api/teachers";
import { listLessons } from "@/lib/api/academic";
import { ApiError } from "@/lib/api-client";
import { useAuthSession } from "@/lib/use-auth-session";
import { usePermissions } from "@/lib/use-permissions";
import { OpenChatButton } from "@/components/OpenChatButton";
import { TeacherOverviewTab } from "@/components/teachers/TeacherOverviewTab";
import { TeacherWorkloadTab } from "@/components/teachers/TeacherWorkloadTabs";
import { TeacherSupportTab } from "@/components/teachers/TeacherSupportTab";
import { TeacherActivityTab } from "@/components/teachers/TeacherActivityTab";
import { TeacherPortfolioTab } from "@/components/teachers/TeacherPortfolioTab";
import { TeacherHeadline } from "@/components/teachers/TeacherHeadline";
import { PublicTeacherPortfolio } from "@/components/teachers/PublicTeacherPortfolio";
import { Avatar } from "@/components/ui/Avatar";
import { PhotoUpload } from "@/components/PhotoUpload";
import { PageHeader } from "@/components/ui/PageHeader";
import { Tabs } from "@/components/ui/Tabs";
import { StatTile } from "@/components/ui/StatTile";
import { Table, THead, TBody, TRow, TH, TD } from "@/components/ui/Table";
import { Alert } from "@/components/ui/Alert";
import { Button } from "@/components/ui/Button";
import { Skeleton } from "@/components/ui/Skeleton";
import { EmptyState } from "@/components/ui/EmptyState";
import { formatShortDateTime } from "@/lib/format-date";

export default function TeacherProfile({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const { user } = useAuthSession();
  const { can, scopeOf, ready } = usePermissions();
  const [workloadPeriod, setWorkloadPeriod] = useState("30");
  // A student or parent reaches this page from their group: they read the confirmed part of the portfolio, not the 360° view.
  const publicView = can("teacher.portfolio.view.public") && !can("teacher.portfolio.view");
  const query = useQuery({ queryKey: ["teachers", id], queryFn: () => getTeacher(id), enabled: ready && !publicView });
  // The same query the Portfolio tab reads: the header's specialization and chips come out of it.
  const portfolio = useQuery({ queryKey: ["teachers", id, "portfolio"], queryFn: () => getTeacherPortfolio(id), enabled: can("teacher.portfolio.view") });
  if (publicView) return <PublicTeacherPortfolio teacherId={id} />;
  if (query.isPending) return <main className="p-4 sm:p-6 lg:p-8"><Skeleton className="h-48" /></main>;
  if (query.isError) return <main className="space-y-4 p-4 sm:p-6 lg:p-8"><Alert>{query.error instanceof ApiError ? query.error.message : "Profilni yuklab bo‘lmadi."}</Alert><Button onClick={() => void query.refetch()}>Qayta urinish</Button></main>;
  const teacher = query.data;
  return <main className="p-4 sm:p-6 lg:p-8">
    <Link href="/teachers" className="mb-4 inline-block text-sm text-brand-700">← O‘qituvchilar</Link>
    <div className="mb-2 flex items-start gap-4">
      <Avatar name={teacher.fullName} className="size-16 text-lg" photo={teacher.photoVersion ? { userId: teacher.userId, version: teacher.photoVersion } : null} />
      <div className="min-w-0 flex-1">
        <PageHeader
          title={teacher.fullName}
          description={[portfolio.data?.profile.specialization, teacher.email].filter(Boolean).join(" · ")}
          actions={user?.id !== teacher.userId && <OpenChatButton userId={teacher.userId} />}
        />
        {(user?.id === teacher.userId || ["AllOrganization", "MyBranches"].includes(scopeOf("users.edit") ?? "")) &&
          <div className="-mt-3 mb-4"><PhotoUpload userId={teacher.userId} hasPhoto={!!teacher.photoVersion} refresh={["teachers", id]} /></div>}
      </div>
    </div>
    <TeacherHeadline chips={portfolio.data?.headline} />
    <Tabs tabs={[
      { id: "overview", label: "Umumiy", content: <TeacherOverviewTab teacherId={id} /> },
      { id: "groups", label: "Guruhlar", content: teacher.groups.length ? <Table><THead><tr><TH>Guruh</TH><TH>Vazifa</TH><TH>Studentlar</TH></tr></THead><TBody>{teacher.groups.map(g => <TRow key={g.groupId}><TD>{g.groupName}</TD><TD>{g.role === "MainTeacher" ? "Asosiy o‘qituvchi" : "Yordamchi o‘qituvchi"}</TD><TD>{g.studentCount}</TD></TRow>)}</TBody></Table> : <EmptyState icon={Users2} title="Hali guruh biriktirilmagan" /> },
      { id: "students", label: "Studentlar", content: teacher.students.length ? <Table><THead><tr><TH>Student</TH><TH>ID</TH><TH>Guruh</TH></tr></THead><TBody>{teacher.students.map(s => <TRow key={`${s.studentId}-${s.groupId}`}><TD><Link className="text-brand-700 hover:underline" href={`/students/${s.studentId}`}>{s.fullName}</Link></TD><TD>{s.studentNumber}</TD><TD>{s.groupName}</TD></TRow>)}</TBody></Table> : <EmptyState icon={Users2} title="Faol studentlar yo‘q" /> },
      { id: "schedule", label: "Jadval", content: <TeacherSchedule teacher={teacher} /> },
      { id: "attendance", label: "Davomat", content: <TeacherWorkloadTab teacherId={id} view="attendance" period={workloadPeriod} onPeriodChange={setWorkloadPeriod} /> },
      { id: "homework", label: "Homework", content: <TeacherWorkloadTab teacherId={id} view="homework" period={workloadPeriod} onPeriodChange={setWorkloadPeriod} /> },
      { id: "exams", label: "Imtihonlar", content: <TeacherWorkloadTab teacherId={id} view="exams" period={workloadPeriod} onPeriodChange={setWorkloadPeriod} /> },
      { id: "support", label: "Yordamchilar", content: <TeacherSupportTab teacherId={id} /> },
      { id: "performance", label: "Natijadorlik", content: <TeacherPerformanceTab teacherId={id} /> },
      ...(can("teacher.portfolio.view") ? [{ id: "portfolio", label: "Portfolio", content: <TeacherPortfolioTab teacherId={id} isSelf={user?.id === teacher.userId} /> }] : []),
      // The audit trail is the office's to read, not the teacher's own.
      ...(can("audit.log.view") ? [{ id: "activity", label: "Faoliyat tarixi", content: <TeacherActivityTab teacherId={id} /> }] : []),
    ]} />
  </main>;
}

/** Performance tab (docs/00_TZ.md §6, audit §3.6): built from what the product already tracks. KPI's
 * editable weights and Feedback stay Phase 6 — see the backend endpoint's doc-comment. */
function TeacherPerformanceTab({ teacherId }: { teacherId: string }) {
  const query = useQuery({ queryKey: ["teachers", teacherId, "performance"], queryFn: () => getTeacherPerformance(teacherId) });
  if (query.isPending) return <Skeleton className="h-40" />;
  if (query.isError) return <><Alert>{query.error instanceof ApiError ? query.error.message : "Natijadorlikni yuklab bo'lmadi."}</Alert><Button onClick={() => void query.refetch()}>Qayta urinish</Button></>;
  const p = query.data;
  return (
    <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
      <StatTile label="Davomat (studentlar)" value={`${p.attendanceRate}%`} icon={TrendingUp} tone={p.attendanceRate < 70 ? "danger" : "brand"} />
      <StatTile label="Homework bajarilishi" value={`${p.homeworkCompletionRate}%`} icon={ClipboardList} tone={p.homeworkCompletionRate < 60 ? "warning" : "brand"} />
      <StatTile label="O'rtacha imtihon balli" value={p.averageExamScore ?? "—"} icon={Award} />
    </div>
  );
}

function TeacherSchedule({ teacher }: { teacher: TeacherOverview }) {
  const query = useQuery({ queryKey: ["lessons"], queryFn: () => listLessons() });
  if (query.isPending) return <Skeleton className="h-40" />;
  if (query.isError) return <><Alert>{query.error instanceof ApiError ? query.error.message : "Jadvalni yuklab bo‘lmadi."}</Alert><Button onClick={() => void query.refetch()}>Qayta urinish</Button></>;
  const groups = new Map(teacher.groups.map(g => [g.groupId, g]));
  const lessons = query.data.filter(l => groups.has(l.groupId) && (l.teacherId === teacher.userId || groups.get(l.groupId)?.role === "SupportTeacher"));
  if (!lessons.length) return <EmptyState icon={CalendarClock} title="Darslar rejalashtirilmagan" />;
  return <Table><THead><tr><TH>Sana</TH><TH>Guruh / mavzu</TH><TH>Xona</TH></tr></THead><TBody>{[...lessons].sort((a, b) => a.startsAt.localeCompare(b.startsAt)).map(l => <TRow key={l.id}>
    <TD>{formatShortDateTime(l.startsAt)}</TD><TD><Link className="text-brand-700 hover:underline" href={`/schedule/${l.id}`}>{groups.get(l.groupId)?.groupName}</Link><p className="text-sm text-ink-500">{l.topic}</p></TD><TD>{l.roomName}</TD>
  </TRow>)}</TBody></Table>;
}
