"use client";

import Link from "next/link";
import { useQuery } from "@tanstack/react-query";
import { Award, BookOpen, CalendarClock, Mic, PenLine, Sparkles, Wallet } from "lucide-react";
import { fetchStudentDashboard, type AiResult } from "@/lib/api/dashboard";
import { formatMoney } from "@/lib/api/finance";
import { ApiError } from "@/lib/api-client";
import { PageHeader } from "@/components/ui/PageHeader";
import { StatTile } from "@/components/ui/StatTile";
import { Card, CardHeader, CardTitle } from "@/components/ui/Card";
import { Alert } from "@/components/ui/Alert";
import { Badge } from "@/components/ui/Badge";
import { Skeleton } from "@/components/ui/Skeleton";
import { EmptyState } from "@/components/ui/EmptyState";
import { Button } from "@/components/ui/Button";
import { ComplaintLink } from "@/components/complaints/ComplaintLink";
import { AnnouncementsCard } from "./AnnouncementsCard";
import { dayMonth, LessonList, percent, timeRange } from "./shared";

const aiIcon = (kind: AiResult["kind"]) => (kind === "writing" ? PenLine : Mic);

const AI_STATUS: Record<string, string> = {
  Queued: "Navbatda",
  Processing: "Tekshirilmoqda",
  Completed: "Tayyor",
  Failed: "Xatolik",
  Rejected: "Rad etildi",
  Active: "Davom etmoqda",
  Abandoned: "Tashlab ketilgan",
};

/**
 * The student's home. Before this screen a student who logged in landed on the student directory and saw a
 * table containing exactly themselves — the scope was right and the page was still the wrong page.
 */
export function StudentDashboard() {
  const query = useQuery({ queryKey: ["dashboard", "student"], queryFn: fetchStudentDashboard });
  const data = query.data;

  if (query.isError)
    return (
      <main className="p-4 sm:p-6 lg:p-8">
        <Alert>{query.error instanceof ApiError ? query.error.message : "Ma'lumotlarni yuklab bo'lmadi."}</Alert>
      </main>
    );

  return (
    <main className="p-4 sm:p-6 lg:p-8">
      <PageHeader
        title={data ? `Salom, ${data.fullName.split(" ")[0]}` : "Bosh sahifa"}
        description={
          data?.currentLevel ? `Daraja: ${data.currentLevel}` : "Bugungi darslar va topshiriqlaringiz."
        }
        actions={<ComplaintLink />}
      />

      {query.isPending && (
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          {Array.from({ length: 4 }, (_, i) => (
            <Skeleton key={i} className="h-24" />
          ))}
        </div>
      )}

      <AnnouncementsCard />

      {data && (
        <div className="space-y-4">
          {data.money.outstanding > 0 && (
            <Alert>
              {data.money.overdue ? "Muddati o'tgan to'lov: " : "To'lov kutilmoqda: "}
              <strong>{formatMoney(data.money.outstanding)}</strong>
              {data.money.nextDueDate && ` · oxirgi muddat ${dayMonth(data.money.nextDueDate)}`}
            </Alert>
          )}

          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            <StatTile label="Bugungi darslar" value={data.today.length} icon={CalendarClock} tone="brand" />
            <StatTile
              label="Davomat"
              value={percent(data.attendanceRate)}
              icon={CalendarClock}
              tone={data.attendanceRate >= 0.85 ? "brand" : "warning"}
              hint={`${data.lessonsAttended} / ${data.lessonsHeld} dars`}
            />
            <StatTile
              label="Bajarilmagan vazifa"
              value={data.pendingHomework.length}
              icon={BookOpen}
              tone={data.pendingHomework.some((h) => h.overdue) ? "danger" : "neutral"}
              hint={
                data.pendingHomework.filter((h) => h.overdue).length > 0
                  ? `${data.pendingHomework.filter((h) => h.overdue).length} tasi muddati o'tgan`
                  : undefined
              }
            />
            <StatTile
              label="Oxirgi imtihon"
              value={data.lastExamOverall != null ? data.lastExamOverall.toFixed(1) : "—"}
              icon={Award}
              hint={data.lastExamName ?? "Natija hali yo'q"}
            />
          </div>

          <div className="grid gap-4 lg:grid-cols-3">
            <Card className="lg:col-span-2">
              <CardHeader>
                <CardTitle>Bugun</CardTitle>
                <Link href="/schedule" className="text-[12.5px] text-brand-700 hover:underline">
                  Jadval
                </Link>
              </CardHeader>
              <LessonList lessons={data.today} emptyDescription="Bugun darsingiz yo'q — dam oling." />

              {data.today.length === 0 && data.upcoming.length > 0 && (
                <div className="border-t border-border px-5 py-3">
                  <p className="text-[12px] font-medium uppercase tracking-wide text-ink-400">Keyingi dars</p>
                  <p className="mt-1 text-[13.5px] text-ink-900">
                    {data.upcoming[0].groupName} · {dayMonth(data.upcoming[0].startsAt)}{" "}
                    {timeRange(data.upcoming[0].startsAt, data.upcoming[0].endsAt)}
                  </p>
                </div>
              )}
            </Card>

            <Card>
              <CardHeader>
                <CardTitle>Uy vazifalari</CardTitle>
              </CardHeader>
              {data.pendingHomework.length === 0 ? (
                <EmptyState icon={BookOpen} title="Hammasi topshirilgan" />
              ) : (
                <ul className="divide-y divide-border">
                  {data.pendingHomework.slice(0, 6).map((h) => (
                    <li key={h.id} className="px-5 py-3">
                      <div className="flex items-start justify-between gap-2">
                        <Link
                          href={`/schedule/${h.lessonId}`}
                          className="text-[13.5px] font-medium text-ink-900 hover:underline"
                        >
                          {h.title}
                        </Link>
                        {h.overdue && <Badge tone="danger">Kechikdi</Badge>}
                      </div>
                      <p className="mt-0.5 text-[12px] text-ink-500">
                        {h.groupName} · {dayMonth(h.deadline)}gacha
                      </p>
                    </li>
                  ))}
                </ul>
              )}
            </Card>
          </div>

          <Card>
            <CardHeader>
              <CardTitle>So&apos;nggi AI natijalari</CardTitle>
              <div className="flex gap-2">
                <Link href="/ai/writing">
                  <Button variant="secondary" size="sm">
                    <PenLine className="size-3.5" aria-hidden />
                    Writing
                  </Button>
                </Link>
                <Link href="/ai/tutor">
                  <Button variant="secondary" size="sm">
                    <Sparkles className="size-3.5" aria-hidden />
                    Tutor
                  </Button>
                </Link>
              </div>
            </CardHeader>
            {data.recentAi.length === 0 ? (
              <EmptyState
                icon={Sparkles}
                title="Hali AI natijasi yo'q"
                description="Insho yuboring yoki speaking mashqini boshlang — natija shu yerda chiqadi."
              />
            ) : (
              <ul className="divide-y divide-border">
                {data.recentAi.map((r, i) => {
                  const Icon = aiIcon(r.kind);
                  return (
                    <li key={`${r.kind}-${r.at}-${i}`} className="flex items-center gap-3 px-5 py-3">
                      <span className="flex size-8 shrink-0 items-center justify-center rounded-lg bg-brand-50 text-brand-700">
                        <Icon className="size-4" aria-hidden />
                      </span>
                      <div className="min-w-0 flex-1">
                        <Link href={r.link} className="block truncate text-[13.5px] text-ink-900 hover:underline">
                          {r.title}
                        </Link>
                        <p className="text-[12px] text-ink-500">
                          {dayMonth(r.at)} · {AI_STATUS[r.status] ?? r.status}
                        </p>
                      </div>
                      <span className="font-tabular text-[15px] font-semibold text-ink-900">
                        {r.band != null ? r.band.toFixed(1) : "—"}
                      </span>
                    </li>
                  );
                })}
              </ul>
            )}
          </Card>

          {data.money.outstanding === 0 && (
            <p className="flex items-center gap-1.5 text-[12.5px] text-ink-500">
              <Wallet className="size-3.5" aria-hidden />
              To&apos;lovlaringiz joyida — qarzdorlik yo&apos;q.
            </p>
          )}
        </div>
      )}
    </main>
  );
}
