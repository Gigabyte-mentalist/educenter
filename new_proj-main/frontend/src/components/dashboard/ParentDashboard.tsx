"use client";

import Link from "next/link";
import { useQuery } from "@tanstack/react-query";
import { Award, BookOpen, CalendarClock, MessagesSquare, Users2, Wallet } from "lucide-react";
import { fetchParentDashboard, type ParentChild } from "@/lib/api/dashboard";
import { formatMoney } from "@/lib/api/finance";
import { ApiError } from "@/lib/api-client";
import { PageHeader } from "@/components/ui/PageHeader";
import { Card, CardBody, CardHeader, CardTitle } from "@/components/ui/Card";
import { Alert } from "@/components/ui/Alert";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { Skeleton } from "@/components/ui/Skeleton";
import { EmptyState } from "@/components/ui/EmptyState";
import { ComplaintLink } from "@/components/complaints/ComplaintLink";
import { AnnouncementsCard } from "./AnnouncementsCard";
import { dayMonth, LessonList, percent, rateTone, timeRange } from "./shared";

const STATUS_LABEL: Record<ParentChild["status"], string> = {
  Active: "Faol",
  Debt: "Qarzdor",
  Frozen: "Muzlatilgan",
  Transferred: "Ko'chirilgan",
  Graduated: "Bitirgan",
  Dropped: "Tashlab ketgan",
};

const STATUS_TONE: Record<ParentChild["status"], "success" | "danger" | "warning" | "neutral"> = {
  Active: "success",
  Debt: "danger",
  Frozen: "warning",
  Transferred: "neutral",
  Graduated: "neutral",
  Dropped: "neutral",
};

/**
 * One card per child, holding the four things a parent has ever asked the front desk about: is the child going,
 * is the work done, what did they score, and do I owe anything. A Parent used to log in to an empty product.
 */
export function ParentDashboard() {
  const query = useQuery({ queryKey: ["dashboard", "parent"], queryFn: fetchParentDashboard });
  const children = query.data?.children ?? [];

  return (
    <main className="p-4 sm:p-6 lg:p-8">
      <PageHeader title="Farzandlarim" description="Davomat, uy vazifalari, imtihon natijalari va to'lovlar." actions={<ComplaintLink />} />

      <AnnouncementsCard />

      {query.isError && (
        <Alert>{query.error instanceof ApiError ? query.error.message : "Ma'lumotlarni yuklab bo'lmadi."}</Alert>
      )}

      {query.isPending && (
        <div className="grid gap-4 lg:grid-cols-2">
          <Skeleton className="h-72" />
          <Skeleton className="h-72" />
        </div>
      )}

      {query.isSuccess && children.length === 0 && (
        <Card>
          <EmptyState
            icon={Users2}
            title="Farzand biriktirilmagan"
            description="Hisobingizga hali farzand ulanmagan. O'quv markazi administratori bilan bog'laning."
          />
        </Card>
      )}

      <div className="grid gap-4 lg:grid-cols-2">
        {children.map((child) => (
          <Card key={child.studentId}>
            <CardHeader>
              <div className="min-w-0">
                <CardTitle className="truncate">{child.fullName}</CardTitle>
                <p className="mt-0.5 text-[12px] text-ink-500">
                  {child.currentLevel ?? "Daraja belgilanmagan"}
                </p>
              </div>
              <Badge tone={STATUS_TONE[child.status]} dot>
                {STATUS_LABEL[child.status]}
              </Badge>
            </CardHeader>

            <CardBody className="space-y-4">
              <div className="grid grid-cols-3 gap-3">
                <div>
                  <p className="text-[11.5px] text-ink-500">Davomat</p>
                  <p className={`font-tabular text-lg font-semibold ${rateTone(child.attendanceRate)}`}>
                    {percent(child.attendanceRate)}
                  </p>
                  <p className="text-[11px] text-ink-400">
                    {child.lessonsAttended}/{child.lessonsHeld} dars
                  </p>
                </div>
                <div>
                  <p className="text-[11.5px] text-ink-500">Vazifalar</p>
                  <p
                    className={`font-tabular text-lg font-semibold ${
                      child.pendingHomework > 0 ? "text-warning-600" : "text-ink-900"
                    }`}
                  >
                    {child.pendingHomework}
                  </p>
                  <p className="text-[11px] text-ink-400">bajarilmagan</p>
                </div>
                <div>
                  <p className="text-[11.5px] text-ink-500">Oxirgi imtihon</p>
                  <p className="font-tabular text-lg font-semibold text-ink-900">
                    {child.lastExamOverall != null ? child.lastExamOverall.toFixed(1) : "—"}
                  </p>
                  <p className="truncate text-[11px] text-ink-400">{child.lastExamName ?? "natija yo'q"}</p>
                </div>
              </div>

              {child.money.outstanding > 0 ? (
                <div className="flex items-start gap-2 rounded-lg bg-danger-50 px-3 py-2.5">
                  <Wallet className="mt-0.5 size-4 shrink-0 text-danger-600" aria-hidden />
                  <div>
                    <p className="text-[13px] font-medium text-danger-600">
                      {formatMoney(child.money.outstanding)} to&apos;lanmagan
                    </p>
                    {child.money.nextDueDate && (
                      <p className="text-[12px] text-ink-600">
                        {child.money.overdue ? "Muddati o'tgan" : "Oxirgi muddat"}:{" "}
                        {dayMonth(child.money.nextDueDate)}
                      </p>
                    )}
                  </div>
                </div>
              ) : (
                <p className="flex items-center gap-1.5 text-[12.5px] text-success-600">
                  <Wallet className="size-3.5" aria-hidden />
                  To&apos;lovlar joyida
                </p>
              )}

              <div>
                <p className="mb-1 flex items-center gap-1.5 text-[11.5px] font-medium uppercase tracking-wide text-ink-400">
                  <CalendarClock className="size-3.5" aria-hidden />
                  Bugun
                </p>
                {child.today.length > 0 ? (
                  <ul className="space-y-1">
                    {child.today.map((l) => (
                      <li key={l.id} className="flex items-center gap-2 text-[13px]">
                        <span className="w-[74px] shrink-0 font-tabular text-[12.5px] text-ink-500">
                          {timeRange(l.startsAt, l.endsAt)}
                        </span>
                        <span className="truncate text-ink-900">{l.groupName}</span>
                        <span className="shrink-0 text-[12px] text-ink-400">{l.roomName}</span>
                      </li>
                    ))}
                  </ul>
                ) : child.nextLesson ? (
                  <p className="text-[13px] text-ink-600">
                    Bugun dars yo&apos;q. Keyingisi: {child.nextLesson.groupName} ·{" "}
                    {dayMonth(child.nextLesson.startsAt)}{" "}
                    {timeRange(child.nextLesson.startsAt, child.nextLesson.endsAt)}
                  </p>
                ) : (
                  <p className="text-[13px] text-ink-500">Jadvalda yaqin dars yo&apos;q.</p>
                )}
              </div>

              <div className="flex flex-wrap gap-2 border-t border-border pt-3">
                <Link href={`/students/${child.studentId}`}>
                  <Button variant="secondary" size="sm">
                    <BookOpen className="size-3.5" aria-hidden />
                    To&apos;liq profil
                  </Button>
                </Link>
                <Link href="/schedule">
                  <Button variant="secondary" size="sm">
                    <CalendarClock className="size-3.5" aria-hidden />
                    Jadval
                  </Button>
                </Link>
                <Link href="/exams">
                  <Button variant="secondary" size="sm">
                    <Award className="size-3.5" aria-hidden />
                    Imtihonlar
                  </Button>
                </Link>
                <Link href="/chat">
                  <Button variant="secondary" size="sm">
                    <MessagesSquare className="size-3.5" aria-hidden />
                    O&apos;qituvchiga yozish
                  </Button>
                </Link>
              </div>
            </CardBody>
          </Card>
        ))}
      </div>

      {children.length === 1 && children[0].today.length > 0 && (
        <Card className="mt-4">
          <CardHeader>
            <CardTitle>Bugungi darslar tafsiloti</CardTitle>
          </CardHeader>
          <LessonList lessons={children[0].today} />
        </Card>
      )}
    </main>
  );
}
