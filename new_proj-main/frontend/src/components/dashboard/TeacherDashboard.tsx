"use client";

import Link from "next/link";
import { useQuery } from "@tanstack/react-query";
import { AlertTriangle, CalendarClock, ClipboardCheck, GraduationCap, UsersRound } from "lucide-react";
import { fetchTeacherDashboard } from "@/lib/api/dashboard";
import { PortfolioReminder } from "@/components/teachers/PortfolioReminder";
import { ApiError } from "@/lib/api-client";
import { PageHeader } from "@/components/ui/PageHeader";
import { StatTile } from "@/components/ui/StatTile";
import { Card, CardBody, CardHeader, CardTitle } from "@/components/ui/Card";
import { Alert } from "@/components/ui/Alert";
import { Skeleton } from "@/components/ui/Skeleton";
import { Badge } from "@/components/ui/Badge";
import { EmptyState } from "@/components/ui/EmptyState";
import { ComplaintLink } from "@/components/complaints/ComplaintLink";
import { AnnouncementsCard } from "./AnnouncementsCard";
import { CountLink, LessonList, percent } from "./shared";

/**
 * A teacher's morning: the lessons they are about to give, the work waiting on them, and the students whose
 * attendance has started to slide. Every count links to the screen that clears it.
 */
export function TeacherDashboard() {
  const query = useQuery({ queryKey: ["dashboard", "teacher"], queryFn: fetchTeacherDashboard });
  const data = query.data;

  return (
    <main className="p-4 sm:p-6 lg:p-8">
      <PageHeader
        title="Bosh sahifa"
        description="Bugungi darslaringiz va e'tibor talab qiladigan ishlar."
        actions={<ComplaintLink />}
      />

      <PortfolioReminder />

      <AnnouncementsCard />

      {query.isError && (
        <Alert>{query.error instanceof ApiError ? query.error.message : "Ma'lumotlarni yuklab bo'lmadi."}</Alert>
      )}

      {query.isPending && (
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          {Array.from({ length: 4 }, (_, i) => (
            <Skeleton key={i} className="h-24" />
          ))}
        </div>
      )}

      {data && (
        <div className="space-y-4">
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            <StatTile label="Bugungi darslar" value={data.today.length} icon={CalendarClock} tone="brand" />
            <StatTile label="Guruhlarim" value={data.myGroups} icon={UsersRound} hint={`${data.myStudents} o'quvchi`} />
            <StatTile
              label="Tekshirish kutmoqda"
              value={data.pendingHomeworkReviews + data.pendingWritingReviews}
              icon={ClipboardCheck}
              tone={data.pendingHomeworkReviews + data.pendingWritingReviews > 0 ? "warning" : "neutral"}
              hint={`${data.pendingHomeworkReviews} uy vazifa · ${data.pendingWritingReviews} insho`}
            />
            <StatTile
              label="Davomat (30 kun)"
              value={percent(data.attendanceRate)}
              icon={GraduationCap}
              tone={data.attendanceRate >= 0.85 ? "brand" : "warning"}
            />
          </div>

          {data.unmarkedLessons > 0 && (
            <Alert>
              {data.unmarkedLessons} ta o&apos;tgan darsda davomat belgilanmagan.{" "}
              <Link href="/schedule" className="font-medium underline">
                Jadvalga o&apos;tish
              </Link>
            </Alert>
          )}

          <div className="grid gap-4 lg:grid-cols-3">
            <Card className="lg:col-span-2">
              <CardHeader>
                <CardTitle>Bugun</CardTitle>
                <Link href="/schedule" className="text-[12.5px] text-brand-700 hover:underline">
                  To&apos;liq jadval
                </Link>
              </CardHeader>
              <LessonList
                lessons={data.today}
                showTeacher={false}
                emptyDescription="Bugun jadvalingizda dars yo'q."
              />
              {data.tomorrow.length > 0 && (
                <>
                  <div className="border-t border-border px-5 py-2.5">
                    <span className="text-[12px] font-medium uppercase tracking-wide text-ink-400">Ertaga</span>
                  </div>
                  <LessonList lessons={data.tomorrow} showTeacher={false} />
                </>
              )}
            </Card>

            <div className="space-y-4">
              <Card>
                <CardHeader>
                  <CardTitle>Ishlar</CardTitle>
                </CardHeader>
                <CardBody className="space-y-1 p-2">
                  <CountLink
                    href="/exams"
                    label="Uy vazifa tekshiruvi"
                    value={data.pendingHomeworkReviews}
                    tone="warning"
                  />
                  <CountLink
                    href="/ai/writing/reviews"
                    label="Insho tekshiruvi"
                    value={data.pendingWritingReviews}
                    tone="warning"
                  />
                  <CountLink
                    href="/ai/speaking/reviews"
                    label="Speaking tekshiruvi"
                    value={data.pendingSpeakingReviews}
                    tone="warning"
                  />
                  <CountLink
                    href="/schedule"
                    label="Belgilanmagan davomat"
                    value={data.unmarkedLessons}
                    tone="danger"
                  />
                  <CountLink href="/chat" label="O'qilmagan bildirishnoma" value={data.unreadNotifications} />
                </CardBody>
              </Card>

              <Card>
                <CardHeader>
                  <CardTitle>E&apos;tibor kerak</CardTitle>
                </CardHeader>
                {data.risks.length === 0 ? (
                  <EmptyState icon={AlertTriangle} title="Signal yo'q" description="Barcha o'quvchilar me'yorda." />
                ) : (
                  <ul className="divide-y divide-border">
                    {data.risks.map((r) => (
                      <li key={r.studentId} className="px-5 py-3">
                        <div className="flex items-start justify-between gap-2">
                          <Link
                            href={`/students/${r.studentId}`}
                            className="text-[13.5px] font-medium text-ink-900 hover:underline"
                          >
                            {r.fullName}
                          </Link>
                          <Badge tone={r.severity >= 2 ? "danger" : "warning"}>
                            {r.severity >= 2 ? "Yuqori" : "O'rta"}
                          </Badge>
                        </div>
                        <p className="mt-0.5 text-[12px] text-ink-500">
                          {r.groupName} · {r.reason}
                        </p>
                      </li>
                    ))}
                  </ul>
                )}
              </Card>
            </div>
          </div>
        </div>
      )}
    </main>
  );
}
