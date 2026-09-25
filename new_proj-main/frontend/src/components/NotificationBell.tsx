"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Award, Bell, BellOff, CalendarClock, CheckCheck, ClipboardList, LifeBuoy, ListChecks, Settings2, Sparkles, UsersRound, UserX, Wallet,
  type LucideIcon,
} from "lucide-react";
import { getUnreadCount, listNotifications, markAllNotificationsRead, markNotificationRead, type AppNotification } from "@/lib/api/notifications";
import { ApiError } from "@/lib/api-client";
import { Alert } from "@/components/ui/Alert";
import { Button } from "@/components/ui/Button";
import { Drawer } from "@/components/ui/Drawer";
import { EmptyState } from "@/components/ui/EmptyState";
import { Skeleton } from "@/components/ui/Skeleton";
import { cn } from "@/lib/cn";
import { formatDateTime } from "@/lib/format-date";

const TYPE_ICON: Record<string, { icon: LucideIcon; tone: string }> = {
  PAYMENT_DUE: { icon: Wallet, tone: "bg-warning-50 text-warning-600" },
  PAYMENT_OVERDUE: { icon: Wallet, tone: "bg-danger-50 text-danger-600" },
  ABSENT: { icon: UserX, tone: "bg-danger-50 text-danger-600" },
  ABSENCE_STREAK: { icon: UserX, tone: "bg-danger-50 text-danger-600" },
  EXAM_RESULT: { icon: Award, tone: "bg-accent-300/40 text-accent-700" },
  HOMEWORK_CREATED: { icon: ClipboardList, tone: "bg-brand-100 text-brand-700" },
  HOMEWORK_DEADLINE: { icon: ClipboardList, tone: "bg-warning-50 text-warning-600" },
  HOMEWORK_REVIEWED: { icon: ClipboardList, tone: "bg-success-50 text-success-600" },
  HOMEWORK_CHANGED: { icon: ClipboardList, tone: "bg-warning-50 text-warning-600" },
  GROUP_CLOSED: { icon: UsersRound, tone: "bg-danger-50 text-danger-600" },
  LESSON_REMINDER: { icon: CalendarClock, tone: "bg-brand-100 text-brand-700" },
  SCHEDULE_CHANGED: { icon: CalendarClock, tone: "bg-ink-100 text-ink-600" },
  TEACHER_ASSIGNED: { icon: UsersRound, tone: "bg-ink-100 text-ink-600" },
  LEARNING_SIGNAL: { icon: LifeBuoy, tone: "bg-warning-50 text-warning-600" },
  SUPPORT_RECOMMENDED: { icon: LifeBuoy, tone: "bg-warning-50 text-warning-600" },
  SUPPORT_ASSIGNED: { icon: LifeBuoy, tone: "bg-brand-100 text-brand-700" },
  TASK_ASSIGNED: { icon: ListChecks, tone: "bg-brand-100 text-brand-700" },
  WRITING_EVALUATED: { icon: Sparkles, tone: "bg-brand-100 text-brand-700" },
  SPEAKING_REVIEWED: { icon: Sparkles, tone: "bg-success-50 text-success-600" },
};

function relativeTime(iso: string): string {
  const minutes = Math.round((Date.now() - new Date(iso).getTime()) / 60000);
  if (minutes < 1) return "hozirgina";
  if (minutes < 60) return `${minutes} daqiqa oldin`;
  const hours = Math.round(minutes / 60);
  if (hours < 24) return `${hours} soat oldin`;
  return formatDateTime(iso);
}

function isToday(iso: string): boolean {
  return new Date(iso).toDateString() === new Date().toDateString();
}

/** Sidebar bell: unread badge (polled every minute) and the inbox drawer. */
export function NotificationBell() {
  const router = useRouter();
  const client = useQueryClient();
  const [open, setOpen] = useState(false);

  const countQuery = useQuery({ queryKey: ["notifications", "unread-count"], queryFn: getUnreadCount, refetchInterval: 60_000 });
  const listQuery = useQuery({ queryKey: ["notifications", "list"], queryFn: () => listNotifications({ limit: 50 }), enabled: open });

  const invalidate = () => client.invalidateQueries({ queryKey: ["notifications"] });
  const markRead = useMutation({ mutationFn: markNotificationRead, onSuccess: invalidate });
  const markAll = useMutation({ mutationFn: markAllNotificationsRead, onSuccess: invalidate });

  const unread = countQuery.data?.count ?? 0;
  const items = listQuery.data ?? [];
  const today = items.filter((n) => isToday(n.createdAt));
  const earlier = items.filter((n) => !isToday(n.createdAt));

  const openItem = (notification: AppNotification) => {
    if (!notification.readAt) markRead.mutate(notification.id);
    if (notification.link) {
      setOpen(false);
      router.push(notification.link);
    }
  };

  return (
    <>
      <button
        type="button"
        onClick={() => setOpen(true)}
        className="relative flex size-8 items-center justify-center rounded-lg text-ink-500 transition-colors hover:bg-ink-50 hover:text-ink-900"
        aria-label={unread > 0 ? `Bildirishnomalar: ${unread} ta o'qilmagan` : "Bildirishnomalar"}
      >
        <Bell className="size-4" aria-hidden />
        {unread > 0 && (
          <span className="absolute -right-1 -top-1 flex h-4 min-w-4 items-center justify-center rounded-full bg-danger-600 px-1 text-[10px] font-semibold leading-none text-white">
            {unread > 99 ? "99+" : unread}
          </span>
        )}
      </button>

      <Drawer
        open={open}
        onClose={() => setOpen(false)}
        title="Bildirishnomalar"
        description={unread > 0 ? `${unread} ta o'qilmagan` : "Hammasi o'qilgan"}
      >
        <div className="mb-4 flex items-center justify-between gap-2">
          <Button size="sm" variant="ghost" disabled={unread === 0} loading={markAll.isPending} onClick={() => markAll.mutate()}>
            <CheckCheck className="size-3.5" aria-hidden />
            Hammasini o&apos;qilgan qilish
          </Button>
          <Link
            href="/settings/notifications"
            onClick={() => setOpen(false)}
            className="inline-flex items-center gap-1.5 rounded-md px-2 py-1.5 text-[13px] text-ink-600 hover:bg-ink-50 hover:text-ink-900"
          >
            <Settings2 className="size-3.5" aria-hidden />
            Sozlamalar
          </Link>
        </div>

        {listQuery.isLoading && (
          <div className="space-y-2">
            {Array.from({ length: 4 }).map((_, i) => <Skeleton key={i} className="h-16" />)}
          </div>
        )}
        {listQuery.isError && (
          <Alert>{listQuery.error instanceof ApiError ? listQuery.error.message : "Bildirishnomalarni yuklab bo'lmadi."}</Alert>
        )}
        {listQuery.data && items.length === 0 && (
          <EmptyState icon={BellOff} title="Bildirishnomalar yo'q" description="To'lov, davomat, homework va boshqa muhim hodisalar shu yerda paydo bo'ladi." />
        )}

        {today.length > 0 && <NotificationSection title="Bugun" items={today} onOpen={openItem} />}
        {earlier.length > 0 && <NotificationSection title="Oldinroq" items={earlier} onOpen={openItem} />}
      </Drawer>
    </>
  );
}

function NotificationSection({ title, items, onOpen }: { title: string; items: AppNotification[]; onOpen: (n: AppNotification) => void }) {
  return (
    <section className="mb-5">
      <h3 className="mb-2 text-[11px] font-semibold uppercase tracking-wide text-ink-400">{title}</h3>
      <ul className="space-y-1">
        {items.map((n) => {
          const config = TYPE_ICON[n.type] ?? { icon: Bell, tone: "bg-ink-100 text-ink-600" };
          const Icon = config.icon;
          const unread = !n.readAt;
          return (
            <li key={n.id}>
              <button
                type="button"
                onClick={() => onOpen(n)}
                className={cn(
                  "flex w-full items-start gap-3 rounded-lg px-2.5 py-2.5 text-left transition-colors hover:bg-ink-50",
                  unread && "bg-brand-50/60",
                )}
              >
                <span className={cn("mt-0.5 flex size-8 shrink-0 items-center justify-center rounded-lg", config.tone)}>
                  <Icon className="size-4" aria-hidden />
                </span>
                <span className="min-w-0 flex-1">
                  <span className={cn("block text-[13.5px] text-ink-900", unread ? "font-semibold" : "font-medium")}>{n.title}</span>
                  <span className="mt-0.5 block text-[12.5px] leading-snug text-ink-600">{n.body}</span>
                  <span className="mt-1 block text-[11.5px] text-ink-400">{relativeTime(n.createdAt)}</span>
                </span>
                {unread && <span className="mt-2 size-2 shrink-0 rounded-full bg-brand-solid" aria-label="O'qilmagan" />}
              </button>
            </li>
          );
        })}
      </ul>
    </section>
  );
}
