"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { Suspense, useState } from "react";
import { Ban, CalendarClock, ChevronLeft, ChevronRight, ClipboardCheck, DoorOpen, Pencil, Plus, UsersRound, UserRound } from "lucide-react";
import { listBranches } from "@/lib/api/branches";
import { createLesson, listGroups, listLessons, type Lesson, type ScheduleConflict } from "@/lib/api/academic";
import { listUsers } from "@/lib/api/users";
import { ApiError } from "@/lib/api-client";
import { useAuthSession } from "@/lib/use-auth-session";
import { usePermissions } from "@/lib/use-permissions";
import { canManageAcademic } from "@/lib/nav";
import { useNewParam } from "@/lib/use-new-param";
import { cn } from "@/lib/cn";
import { PageHeader } from "@/components/ui/PageHeader";
import { Badge } from "@/components/ui/Badge";
import { Modal } from "@/components/ui/Modal";
import { Button } from "@/components/ui/Button";
import { Skeleton } from "@/components/ui/Skeleton";
import { Alert } from "@/components/ui/Alert";
import { Drawer } from "@/components/ui/Drawer";
import { Input, Label, Select } from "@/components/ui/Field";
import { formatDayMonth, formatWeekday, formatTime } from "@/lib/format-date";
import { CancelLessonModal, ConflictNotice, EditLessonModal } from "@/components/LessonActions";
import { useOrganization } from "@/components/OrganizationBrand";
import { RoomSelect } from "@/components/RoomSelect";

/** Monday of the week containing `d`, at local midnight — Uzbekiston's ish haftasi Dushanbadan boshlanadi. */
function mondayOf(d: Date): Date {
  const day = d.getDay(); // 0 = Sunday
  const diff = day === 0 ? -6 : 1 - day;
  const monday = new Date(d.getFullYear(), d.getMonth(), d.getDate() + diff);
  return monday;
}

function sameDay(a: Date, b: Date): boolean {
  return a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate();
}

/** One day of the week, opened from its card: every lesson with its time, room and teacher, and a way into the ones
 * the person can act on — the lesson page (attendance, homework, materials) or the group behind it. */
function DayLessonsModal({
  day, lessons, groupNameById, onClose, onOpenLesson, onOpenGroup,
}: {
  day: Date | null;
  lessons: Lesson[];
  groupNameById: Map<string, string>;
  onClose: () => void;
  onOpenLesson: (lessonId: string) => void;
  onOpenGroup: (groupId: string) => void;
}) {
  const { can } = usePermissions();
  const marksAttendance = can("attendance.mark");
  const opensGroup = can("groups.view");
  const canEdit = can("schedule.edit");
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [editing, setEditing] = useState<Lesson | null>(null);
  const [cancelling, setCancelling] = useState<Lesson[] | null>(null);

  // A cancelled lesson stays in the list, marked, but is not one of the day's lessons: it is not counted, edited or selected.
  const standing = lessons.filter((l) => l.status !== "Cancelled");
  const cancelledCount = lessons.length - standing.length;
  const chosen = standing.filter((l) => selected.has(l.id));
  const close = () => {
    setSelected(new Set());
    onClose();
  };
  const toggle = (id: string) =>
    setSelected((prev) => {
      const next = new Set(prev);
      if (!next.delete(id)) next.add(id);
      return next;
    });

  return (
    <Modal
      open={day !== null}
      onClose={close}
      title={day ? `${formatWeekday(day)}, ${formatDayMonth(day)}` : ""}
      description={`${standing.length} ta dars${cancelledCount > 0 ? `, ${cancelledCount} ta bekor qilingan` : ""}`}
    >
      {canEdit && standing.length > 1 && (
        <div className="mb-3 flex flex-wrap items-center gap-2">
          <Button size="sm" variant="ghost" onClick={() => setSelected(new Set(chosen.length === standing.length ? [] : standing.map((l) => l.id)))}>
            {chosen.length === standing.length ? "Tanlovni bekor qilish" : "Hammasini tanlash"}
          </Button>
          {chosen.length > 0 && (
            <Button size="sm" variant="danger" onClick={() => setCancelling(chosen)}>
              <Ban className="size-3.5" aria-hidden />
              Tanlanganlarni bekor qilish ({chosen.length})
            </Button>
          )}
        </div>
      )}
      <ul className="space-y-3">
        {lessons.map((lesson) => (
          <li key={lesson.id} className={cn("rounded-xl border border-border p-4", lesson.status === "Cancelled" && "bg-ink-50")}>
            <div className="flex items-start justify-between gap-3">
              {canEdit && lesson.status !== "Cancelled" && standing.length > 1 && (
                <input
                  type="checkbox" className="mt-1 size-4 shrink-0" checked={selected.has(lesson.id)} onChange={() => toggle(lesson.id)}
                  aria-label={`${formatTime(lesson.startsAt)} darsini tanlash`}
                />
              )}
              <div className="min-w-0 flex-1">
                <p className={cn("font-tabular text-[15px] font-semibold", lesson.status === "Cancelled" ? "text-ink-400 line-through" : "text-brand-700")}>
                  {formatTime(lesson.startsAt)}–{formatTime(lesson.endsAt)}
                </p>
                <p className={cn("mt-0.5 truncate text-[14px] font-medium", lesson.status === "Cancelled" ? "text-ink-400 line-through" : "text-ink-900")}>
                  {groupNameById.get(lesson.groupId) ?? "—"}
                </p>
              </div>
              <Badge tone={lesson.status === "Cancelled" ? "danger" : "neutral"} className="shrink-0">
                {lesson.status === "Cancelled" ? "Bekor qilindi" : <><DoorOpen className="size-3" aria-hidden />{lesson.roomName}</>}
              </Badge>
            </div>
            {lesson.status === "Cancelled" && (
              <p className="mt-2 text-[12.5px] text-danger-600">Bekor qilindi{lesson.cancelReason ? `: ${lesson.cancelReason}` : ""}</p>
            )}

            <div className="mt-2 space-y-0.5 text-[12.5px] text-ink-500">
              {lesson.teacherName && (
                <p className="flex items-center gap-1.5"><UserRound className="size-3.5 text-ink-400" aria-hidden />{lesson.teacherName}</p>
              )}
              {lesson.topic && <p>{lesson.topic}</p>}
            </div>

            <div className="mt-3 flex flex-wrap gap-2">
              <Button size="sm" onClick={() => onOpenLesson(lesson.id)}>
                <ClipboardCheck className="size-3.5" aria-hidden />
                {marksAttendance && lesson.status !== "Cancelled" ? "Davomat va vazifa" : "Dars sahifasi"}
              </Button>
              {opensGroup && (
                <Button size="sm" variant="secondary" onClick={() => onOpenGroup(lesson.groupId)}>
                  <UsersRound className="size-3.5" aria-hidden />
                  Guruh
                </Button>
              )}
              {canEdit && lesson.status !== "Cancelled" && (
                <>
                  <Button size="sm" variant="secondary" onClick={() => setEditing(lesson)}>
                    <Pencil className="size-3.5" aria-hidden />
                    Tahrirlash
                  </Button>
                  <Button size="sm" variant="secondary" onClick={() => setCancelling([lesson])}>
                    <Ban className="size-3.5" aria-hidden />
                    Bekor qilish
                  </Button>
                </>
              )}
            </div>
          </li>
        ))}
      </ul>
      {editing && <EditLessonModal key={editing.id} lesson={editing} onClose={() => setEditing(null)} />}
      {cancelling && (
        <CancelLessonModal lessons={cancelling} onClose={() => setCancelling(null)} onDone={() => setSelected(new Set())} />
      )}
    </Modal>
  );
}

export default function SchedulePage() {
  return (
    <Suspense fallback={null}>
      <ScheduleView />
    </Suspense>
  );
}

function ScheduleView() {
  const router = useRouter();
  const queryClient = useQueryClient();
  // schedule.edit belongs to Super Admin / Administrator only (docs/01_role_permission_matrix.md).
  const canManage = canManageAcademic(useAuthSession().user?.role);

  const [weekStart, setWeekStart] = useState(() => mondayOf(new Date()));
  const weekEnd = new Date(weekStart.getFullYear(), weekStart.getMonth(), weekStart.getDate() + 7);
  const days = Array.from({ length: 7 }, (_, i) =>
    new Date(weekStart.getFullYear(), weekStart.getMonth(), weekStart.getDate() + i));

  const lessonsQuery = useQuery({
    queryKey: ["lessons", weekStart.toISOString()],
    queryFn: () => listLessons({ from: weekStart.toISOString(), to: weekEnd.toISOString() }),
  });
  const groupsQuery = useQuery({ queryKey: ["groups"], queryFn: listGroups });
  const branchesQuery = useQuery({ queryKey: ["branches"], queryFn: listBranches, enabled: canManage });
  const teachersQuery = useQuery({ queryKey: ["users", "TEACHER"], queryFn: () => listUsers({ role: "TEACHER", size: 200 }), enabled: canManage });

  const [selectedDay, setSelectedDay] = useState<Date | null>(null);
  const [drawerOpen, setDrawerOpen] = useState(false);
  // `?new=1` (the command palette's "Dars qo'shish") opens the form too, for whoever may edit the schedule.
  const newParam = useNewParam();
  const showDrawer = drawerOpen || (newParam.requested && canManage);
  const [branchId, setBranchId] = useState("");
  const [groupId, setGroupId] = useState("");
  const [teacherId, setTeacherId] = useState("");
  const [roomId, setRoomId] = useState("");
  const [startsAt, setStartsAt] = useState("");
  const [endsAt, setEndsAt] = useState("");
  const [conflicts, setConflicts] = useState<ScheduleConflict[]>([]);
  // A room that seats fewer than the group is said after the lesson is saved: a warning, not a refusal.
  const [warnings, setWarnings] = useState<string[]>([]);
  const workdayStart = useOrganization().data?.workdayStart;

  // The form opens on tomorrow at the centre's opening time, so a typical lesson needs only its end to be filled in. Set when the
  // drawer is opened by the button (never during render, where the current time is not allowed).
  const openDrawer = () => {
    if (!startsAt && workdayStart) {
      const tomorrow = new Date();
      tomorrow.setDate(tomorrow.getDate() + 1);
      const pad = (n: number) => String(n).padStart(2, "0");
      setStartsAt(`${tomorrow.getFullYear()}-${pad(tomorrow.getMonth() + 1)}-${pad(tomorrow.getDate())}T${workdayStart}`);
    }
    setDrawerOpen(true);
  };

  const createMutation = useMutation({
    mutationFn: () =>
      createLesson({
        branchId,
        groupId,
        teacherId,
        roomId,
        startsAt: new Date(startsAt).toISOString(),
        endsAt: new Date(endsAt).toISOString(),
      }),
    onSuccess: (result) => {
      if (result.ok) {
        setConflicts([]);
        setRoomId("");
        setWarnings(result.lesson.warnings ?? []);
        setDrawerOpen(false);
        newParam.clear();
        queryClient.invalidateQueries({ queryKey: ["lessons"] });
      } else {
        setConflicts(result.conflicts);
      }
    },
  });

  const groupNameById = new Map(groupsQuery.data?.map((g) => [g.id, g.name]));
  const today = new Date();

  const lessonsByDay = (day: Date): Lesson[] =>
    (lessonsQuery.data ?? [])
      .filter((l) => sameDay(new Date(l.startsAt), day))
      .sort((a, b) => a.startsAt.localeCompare(b.startsAt));

  return (
    <main className="p-4 sm:p-6 lg:p-8">
      <PageHeader
        title="Dars jadvali"
        description={canManage
          ? "Rejalashtirilgan darslar. Yangi dars qo'shishda xona/o'qituvchi/guruh to'qnashuvi avtomatik tekshiriladi."
          : "Sizning guruhlaringiz darslari. Davomat va homework uchun darsni tanlang."}
        actions={
          <div className="flex flex-wrap items-center gap-2">
            <div className="flex items-center gap-1 rounded-lg border border-border bg-surface p-1">
              <Button variant="ghost" size="icon" title="Oldingi hafta" onClick={() => setWeekStart(mondayOf(new Date(weekStart.getFullYear(), weekStart.getMonth(), weekStart.getDate() - 7)))}>
                <ChevronLeft className="size-4" aria-hidden />
              </Button>
              <Button variant="ghost" size="sm" onClick={() => setWeekStart(mondayOf(new Date()))}>
                Bugun
              </Button>
              <Button variant="ghost" size="icon" title="Keyingi hafta" onClick={() => setWeekStart(mondayOf(new Date(weekStart.getFullYear(), weekStart.getMonth(), weekStart.getDate() + 7)))}>
                <ChevronRight className="size-4" aria-hidden />
              </Button>
            </div>
            {canManage && (
              <Button onClick={openDrawer}>
                <Plus className="size-4" aria-hidden />
                Dars qo&apos;shish
              </Button>
            )}
          </div>
        }
      />

      {lessonsQuery.isError && (
        <Alert>{lessonsQuery.error instanceof ApiError ? lessonsQuery.error.message : "Yuklab bo'lmadi."}</Alert>
      )}

      {warnings.length > 0 && (
        <div className="mb-4 flex items-start justify-between gap-3">
          <Alert tone="info">
            <p className="font-medium">Dars qo&apos;shildi. Diqqat:</p>
            <ul className="mt-1 list-disc pl-4">{warnings.map((w) => <li key={w}>{w}</li>)}</ul>
          </Alert>
          <Button size="sm" variant="ghost" onClick={() => setWarnings([])}>Yopish</Button>
        </div>
      )}

      {lessonsQuery.isLoading && (
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-7">
          {Array.from({ length: 7 }, (_, i) => <Skeleton key={i} className="h-24 sm:h-36" />)}
        </div>
      )}

      {lessonsQuery.data && (
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-7">
          {days.map((day) => {
            const isToday = sameDay(day, today);
            const dayLessons = lessonsByDay(day);
            // The day's number counts the lessons that will take place; a day with only cancelled ones still opens.
            const standing = dayLessons.filter((l) => l.status !== "Cancelled");
            const hasLessons = dayLessons.length > 0;
            return (
              <button
                key={day.toISOString()}
                type="button"
                disabled={!hasLessons}
                onClick={() => setSelectedDay(day)}
                aria-label={`${formatWeekday(day)}, ${formatDayMonth(day)} — ${hasLessons ? `${standing.length} ta dars` : "dars yo'q"}`}
                className={cn(
                  "flex min-h-[92px] min-w-0 flex-col rounded-xl border p-3 text-left transition-colors sm:min-h-[150px] sm:p-3.5",
                  isToday ? "border-brand-300 bg-brand-50" : "border-border bg-surface",
                  hasLessons ? "cursor-pointer hover:border-brand-400 hover:bg-brand-50" : "cursor-default opacity-70",
                )}
              >
                <div className="flex items-start justify-between gap-2">
                  <div className="min-w-0">
                    <p className="truncate text-[13.5px] font-semibold text-ink-900 sm:text-[14.5px]">{formatWeekday(day)}</p>
                    <p className="text-[11.5px] text-ink-500 sm:text-[12.5px]">{formatDayMonth(day)}</p>
                  </div>
                  {standing.length > 0 && (
                    <span className="flex size-6 shrink-0 items-center justify-center rounded-full bg-brand-solid font-tabular text-[12px] font-semibold text-white">
                      {standing.length}
                    </span>
                  )}
                </div>

                {isToday && <Badge tone="brand" className="mt-1.5 self-start">Bugun</Badge>}

                {standing.length > 0 ? (
                  // Previews only where the card is big enough to hold them; a phone shows the count and the tap opens the day.
                  <ul className="mt-2.5 hidden space-y-1 sm:block">
                    {standing.slice(0, 3).map((lesson) => (
                      <li key={lesson.id} className="flex min-w-0 items-baseline gap-1.5 text-[12px]">
                        <span className="shrink-0 font-tabular font-medium text-brand-700">{formatTime(lesson.startsAt)}</span>
                        <span className="truncate text-ink-700">{groupNameById.get(lesson.groupId) ?? "—"}</span>
                      </li>
                    ))}
                    {standing.length > 3 && <li className="text-[11.5px] text-ink-500">+{standing.length - 3} ta yana</li>}
                  </ul>
                ) : hasLessons ? (
                  <p className="mt-auto pt-2 text-[12px] text-ink-400">Hammasi bekor qilingan</p>
                ) : (
                  <p className="mt-auto pt-2 text-[12px] text-ink-400">Dars yo&apos;q</p>
                )}
              </button>
            );
          })}
        </div>
      )}

      <DayLessonsModal
        day={selectedDay}
        lessons={selectedDay ? lessonsByDay(selectedDay) : []}
        groupNameById={groupNameById}
        onClose={() => setSelectedDay(null)}
        onOpenLesson={(lessonId) => router.push(`/schedule/${lessonId}`)}
        onOpenGroup={(groupId) => router.push(`/groups/${groupId}`)}
      />

      {lessonsQuery.data && lessonsQuery.data.length === 0 && !lessonsQuery.isLoading && (
        <p className="mt-4 flex items-center justify-center gap-2 text-[13px] text-ink-400">
          <CalendarClock className="size-4" aria-hidden />
          Bu haftaga dars rejalashtirilmagan
        </p>
      )}

      <Drawer open={showDrawer} onClose={() => { setDrawerOpen(false); newParam.clear(); }} title="Yangi dars" description="Vaqt, xona yoki o'qituvchi band bo'lsa, tizim avtomatik ogohlantiradi.">
        <form
          className="space-y-4"
          onSubmit={(e) => {
            e.preventDefault();
            createMutation.mutate();
          }}
        >
          <div>
            <Label htmlFor="lesson-branch">Filial</Label>
            <Select id="lesson-branch" required value={branchId} onChange={(e) => { setBranchId(e.target.value); setRoomId(""); }}>
              <option value="">Tanlang</option>
              {branchesQuery.data?.map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}
            </Select>
          </div>
          <div>
            <Label htmlFor="lesson-group">Guruh</Label>
            <Select id="lesson-group" required value={groupId} onChange={(e) => setGroupId(e.target.value)}>
              <option value="">Tanlang</option>
              {groupsQuery.data?.map((g) => <option key={g.id} value={g.id}>{g.name}</option>)}
            </Select>
          </div>
          <div>
            <Label htmlFor="lesson-teacher">O&apos;qituvchi</Label>
            <Select id="lesson-teacher" required value={teacherId} onChange={(e) => setTeacherId(e.target.value)}>
              <option value="">Tanlang</option>
              {teachersQuery.data?.items.map((t) => <option key={t.id} value={t.id}>{t.fullName}</option>)}
            </Select>
          </div>
          <div>
            <Label htmlFor="lesson-room">Xona</Label>
            <RoomSelect id="lesson-room" required branchId={branchId} value={roomId} onChange={setRoomId} />
          </div>
          <div className="grid grid-cols-2 gap-3">
            <div>
              <Label htmlFor="lesson-start">Boshlanish</Label>
              <Input id="lesson-start" type="datetime-local" required value={startsAt} onChange={(e) => setStartsAt(e.target.value)} />
            </div>
            <div>
              <Label htmlFor="lesson-end">Tugash</Label>
              <Input id="lesson-end" type="datetime-local" required value={endsAt} onChange={(e) => setEndsAt(e.target.value)} />
            </div>
          </div>

          <ConflictNotice conflicts={conflicts} />

          {createMutation.isError && (
            <Alert>{createMutation.error instanceof ApiError ? createMutation.error.message : "Dars yaratib bo'lmadi."}</Alert>
          )}

          <Button type="submit" loading={createMutation.isPending} className="w-full">
            Darsni rejalashtirish
          </Button>
        </form>
      </Drawer>
    </main>
  );
}
