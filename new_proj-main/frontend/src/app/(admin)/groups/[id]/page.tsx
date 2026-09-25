"use client";

import Link from "next/link";
import { use, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, Award, CalendarClock, ClipboardCheck, DoorOpen, GraduationCap, NotebookPen, UserPlus, Users } from "lucide-react";
import { getGroupOverview, type GroupOverview } from "@/lib/api/academic";
import { enrollStudent, listStudents, type Student } from "@/lib/api/students";
import { ApiError } from "@/lib/api-client";
import { usePermissions } from "@/lib/use-permissions";
import { useDebouncedValue } from "@/lib/use-debounced-value";
import { formatDayMonth, formatTime, formatWeekday } from "@/lib/format-date";
import { GroupAdminActions, RemoveStaffButton } from "@/components/GroupAdminActions";
import { GroupScheduleTab } from "@/components/GroupScheduleTab";
import { Alert } from "@/components/ui/Alert";
import { Avatar } from "@/components/ui/Avatar";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { Card, CardHeader, CardTitle } from "@/components/ui/Card";
import { EmptyState } from "@/components/ui/EmptyState";
import { Input } from "@/components/ui/Field";
import { Modal } from "@/components/ui/Modal";
import { Skeleton } from "@/components/ui/Skeleton";
import { StatTile } from "@/components/ui/StatTile";
import { Tabs } from "@/components/ui/Tabs";
import { Table, TBody, TD, TH, THead, TRow } from "@/components/ui/Table";

const errorText = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

const STATUS_LABEL: Record<GroupOverview["status"], string> = {
  Forming: "Shakllanmoqda",
  Active: "Faol",
  Completed: "Tugallangan",
  Cancelled: "Bekor qilingan",
};
const STATUS_TONE: Record<GroupOverview["status"], "neutral" | "success" | "brand" | "danger"> = {
  Forming: "neutral",
  Active: "success",
  Completed: "brand",
  Cancelled: "danger",
};

/** Green from 85%, amber from 70% — the same reading a teacher gives a register: who is slipping. */
const rateTone = (rate: number | null): string =>
  rate === null ? "text-ink-400" : rate >= 85 ? "text-success-600" : rate >= 70 ? "text-warning-600" : "text-danger-600";

export default function GroupDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const { can } = usePermissions();
  const query = useQuery({ queryKey: ["groups", id, "overview"], queryFn: () => getGroupOverview(id) });

  if (query.isPending) {
    return (
      <main className="p-4 sm:p-6 lg:p-8">
        <Skeleton className="mb-6 h-24" />
        <div className="mb-6 grid grid-cols-2 gap-4 lg:grid-cols-4">
          {Array.from({ length: 4 }, (_, i) => <Skeleton key={i} className="h-24" />)}
        </div>
        <Skeleton className="h-64" />
      </main>
    );
  }

  if (query.isError) {
    return (
      <main className="p-4 sm:p-6 lg:p-8">
        <BackLink />
        <Alert>{query.error instanceof ApiError ? query.error.message : "Guruhni yuklab bo'lmadi."}</Alert>
      </main>
    );
  }

  const group = query.data;
  const results = group.results;
  const canOpenStudent = can("students.view");
  const canOpenLesson = can("schedule.view");
  const canSchedule = can("groups.edit") || can("schedule.edit");

  return (
    <main className="p-4 sm:p-6 lg:p-8">
      <BackLink />

      <div className="mb-6 rounded-xl border border-border bg-surface p-5">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <div className="flex flex-wrap items-center gap-2.5">
              <h1 className="text-[18px] font-semibold text-ink-900">{group.name}</h1>
              <Badge tone={STATUS_TONE[group.status]} dot>{STATUS_LABEL[group.status]}</Badge>
            </div>
            <p className="mt-1 text-[13px] text-ink-500">{group.courseName} · {group.branchName}</p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <GroupAdminActions group={group} />
            <AddStudentButton groupId={group.id} />
          </div>
        </div>
        <div className="mt-3 flex flex-wrap gap-1.5">
          {group.staff.length === 0 && <span className="text-[13px] text-ink-400">O&apos;qituvchi biriktirilmagan</span>}
          {group.staff.map((s) => (
            <Badge key={s.userId} tone={s.role === "MainTeacher" ? "brand" : "neutral"}>
              {s.role === "MainTeacher" ? "Asosiy" : "Yordamchi"}:{" "}
              {can("teacher.portfolio.view.public")
                ? <Link href={`/teachers/${s.userId}`} className="underline decoration-brand-300 underline-offset-2">{s.fullName}</Link>
                : s.fullName}
              <RemoveStaffButton groupId={group.id} userId={s.userId} name={s.fullName} />
            </Badge>
          ))}
        </div>
      </div>

      {/* The weekly template is the office's: whoever may edit the group or the schedule gets a second tab for it; everyone else the overview alone. */}
      <Tabs
        tabs={[
          {
            id: "overview",
            label: "Umumiy",
            content: (
              <>
      <div className="mb-6 grid grid-cols-2 gap-4 lg:grid-cols-4">
        <StatTile
          label="Talabalar"
          value={`${group.studentCount} / ${group.capacity}`}
          icon={Users}
          tone="brand"
          hint={group.studentCount >= group.capacity ? "Guruh to'lgan" : `${group.capacity - group.studentCount} ta bo'sh o'rin`}
        />
        {results && (
          <>
            <StatTile
              label="Davomat"
              value={`${results.attendanceRate}%`}
              icon={ClipboardCheck}
              tone={results.attendanceRate < 70 ? "danger" : results.attendanceRate < 85 ? "warning" : "neutral"}
              hint={`${results.lessonsHeld} ta o'tilgan dars`}
            />
            <StatTile
              label="Uy vazifa"
              value={`${results.homeworkCompletionRate}%`}
              icon={NotebookPen}
              tone={results.homeworkCompletionRate < 50 ? "warning" : "neutral"}
              hint={`${results.homeworkAssigned} ta vazifa berilgan`}
            />
            <StatTile
              label="O'rtacha imtihon balli"
              value={results.averageExamScore ?? "—"}
              icon={Award}
              hint={results.examsHeld === 0 ? "Imtihon o'tkazilmagan" : `${results.examsHeld} ta imtihon`}
            />
          </>
        )}
      </div>

      <div className="grid gap-6 xl:grid-cols-[1fr_340px]">
        {results ? (
          <Card>
            <CardHeader>
              <CardTitle>Talabalar natijalari</CardTitle>
              <span className="text-[12.5px] text-ink-500">{results.students.length} ta</span>
            </CardHeader>
            {results.students.length === 0 ? (
              <EmptyState icon={GraduationCap} title="Guruhda talaba yo'q" />
            ) : (
              <Table bare>
                <THead>
                  <tr>
                    <TH>Talaba</TH>
                    <TH className="text-right">Davomat</TH>
                    <TH className="text-right">Kelmagan</TH>
                    <TH className="text-right">Vazifa</TH>
                    <TH className="text-right">Imtihon</TH>
                  </tr>
                </THead>
                <TBody>
                  {results.students.map((s) => (
                    <TRow key={s.studentId}>
                      <TD>
                        <div className="flex items-center gap-2.5">
                          <Avatar name={s.fullName} className="size-7 text-[11px]" />
                          <div className="min-w-0">
                            {canOpenStudent ? (
                              <Link href={`/students/${s.studentId}`} className="font-medium text-brand-700 hover:underline">{s.fullName}</Link>
                            ) : (
                              <span className="font-medium text-ink-900">{s.fullName}</span>
                            )}
                            <p className="font-mono text-[11px] text-ink-400">{s.studentNumber}</p>
                          </div>
                        </div>
                      </TD>
                      <TD className={`text-right font-tabular font-medium ${rateTone(s.attendanceRate)}`}>
                        {s.attendanceRate === null ? "—" : `${s.attendanceRate}%`}
                      </TD>
                      <TD className={`text-right font-tabular ${s.absences >= 3 ? "font-medium text-danger-600" : ""}`}>{s.absences}</TD>
                      <TD className="text-right font-tabular">
                        {s.homeworkSubmitted}
                        {results.homeworkAssigned > 0 && <span className="text-ink-400"> / {results.homeworkAssigned}</span>}
                      </TD>
                      <TD className="text-right font-tabular">{s.averageExamScore ?? "—"}</TD>
                    </TRow>
                  ))}
                </TBody>
              </Table>
            )}
          </Card>
        ) : (
          <Card>
            <CardHeader><CardTitle>Guruh</CardTitle></CardHeader>
            <p className="px-5 py-4 text-[13px] text-ink-500">
              Guruhdoshlaringizning natijalari shaxsiy — ularni faqat o&apos;qituvchilar ko&apos;radi.
            </p>
          </Card>
        )}

        <Card className="self-start">
          <CardHeader>
            <CardTitle>Yaqin darslar</CardTitle>
          </CardHeader>
          {group.upcomingLessons.length === 0 ? (
            <EmptyState icon={CalendarClock} title="Dars rejalashtirilmagan" />
          ) : (
            <ul className="divide-y divide-border">
              {group.upcomingLessons.map((lesson) => {
                const body = (
                  <>
                    <p className="text-[13px] font-medium text-ink-900">
                      {formatWeekday(lesson.startsAt)}, {formatDayMonth(lesson.startsAt)}
                    </p>
                    <p className="font-tabular text-[12.5px] text-brand-700">{formatTime(lesson.startsAt)}–{formatTime(lesson.endsAt)}</p>
                    <p className="mt-0.5 flex items-center gap-1 text-[12px] text-ink-500">
                      <DoorOpen className="size-3 shrink-0" aria-hidden />
                      {lesson.roomName}
                      {lesson.topic && <span className="truncate"> · {lesson.topic}</span>}
                    </p>
                  </>
                );
                return (
                  <li key={lesson.id}>
                    {canOpenLesson ? (
                      <Link href={`/schedule/${lesson.id}`} className="block px-5 py-3 hover:bg-ink-50">{body}</Link>
                    ) : (
                      <div className="px-5 py-3">{body}</div>
                    )}
                  </li>
                );
              })}
            </ul>
          )}
        </Card>
      </div>
              </>
            ),
          },
          ...(canSchedule
            ? [{
                id: "schedule",
                label: "Haftalik jadval",
                content: (
                  <GroupScheduleTab
                    groupId={group.id} branchId={group.branchId} closed={group.status === "Completed" || group.status === "Cancelled"}
                    canEdit={can("groups.edit")} canGenerate={can("schedule.edit")}
                  />
                ),
              }]
            : []),
        ]}
      />
    </main>
  );
}

/** `student.enroll` — the matrix grants this to Operator too, unlike the transfer/freeze actions on the
 * student's own profile, so this button is not limited to Administrator the way group editing is. */
function AddStudentButton({ groupId }: { groupId: string }) {
  const { can } = usePermissions();
  const [open, setOpen] = useState(false);
  if (!can("student.enroll")) return null;

  return (
    <>
      <Button size="sm" variant="secondary" onClick={() => setOpen(true)}>
        <UserPlus className="size-3.5" aria-hidden />
        Student qo&apos;shish
      </Button>
      {open && <AddStudentModal groupId={groupId} onClose={() => setOpen(false)} />}
    </>
  );
}

function AddStudentModal({ groupId, onClose }: { groupId: string; onClose: () => void }) {
  const queryClient = useQueryClient();
  const [search, setSearch] = useState("");
  const debouncedSearch = useDebouncedValue(search);

  const studentsQuery = useQuery({
    queryKey: ["students", "search-for-enroll", debouncedSearch],
    queryFn: () => listStudents({ search: debouncedSearch, size: 10 }),
    enabled: debouncedSearch.trim().length > 0,
  });

  const mutation = useMutation({
    mutationFn: (studentId: string) => enrollStudent(studentId, groupId),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["groups"] });
      onClose();
    },
  });

  const addedStudentId = mutation.variables;

  return (
    <Modal open onClose={onClose} title="Student qo'shish" description="Mavjud studentni qidirib shu guruhga qo'shimcha yozing.">
      <div className="space-y-3">
        <Input
          autoFocus value={search} onChange={(e) => setSearch(e.target.value)}
          placeholder="Ism, email, telefon yoki ID bo'yicha qidirish"
        />
        {mutation.isError && <Alert>{errorText(mutation.error, "Qo'shib bo'lmadi.")}</Alert>}
        {studentsQuery.isLoading && <p className="text-[12.5px] text-ink-500">Yuklanmoqda...</p>}
        {studentsQuery.data && studentsQuery.data.items.length === 0 && (
          <p className="text-[12.5px] text-ink-500">Hech narsa topilmadi.</p>
        )}
        {studentsQuery.data && studentsQuery.data.items.length > 0 && (
          <ul className="divide-y divide-border rounded-lg border border-border">
            {studentsQuery.data.items.map((s: Student) => (
              <li key={s.id} className="flex items-center justify-between gap-3 px-3 py-2.5">
                <div className="flex min-w-0 items-center gap-2.5">
                  <Avatar name={s.fullName} className="size-7 text-[11px]" />
                  <div className="min-w-0">
                    <p className="truncate text-[13px] font-medium text-ink-900">{s.fullName}</p>
                    <p className="truncate text-[12px] text-ink-500">{s.email}</p>
                  </div>
                </div>
                <Button
                  size="sm" loading={mutation.isPending && addedStudentId === s.id}
                  disabled={mutation.isPending} onClick={() => mutation.mutate(s.id)}
                >
                  Qo&apos;shish
                </Button>
              </li>
            ))}
          </ul>
        )}
      </div>
    </Modal>
  );
}

function BackLink() {
  return (
    <Link href="/groups" className="mb-4 inline-flex items-center gap-1.5 text-[13px] text-ink-500 hover:text-ink-800">
      <ArrowLeft className="size-3.5" aria-hidden />
      Guruhlar
    </Link>
  );
}
