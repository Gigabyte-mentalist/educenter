"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { use, useState } from "react";
import Link from "next/link";
import { ArrowLeft, DoorOpen, CalendarClock, Plus, FileCheck2, Star, Send, CheckCircle2, Clock3, Sparkles, UserCog, AlertTriangle, MessageSquare } from "lucide-react";
import { listGroupStudents, listLessons, listSubstitutes, replaceTeacher, type Lesson, type ScheduleConflict } from "@/lib/api/academic";
import {
  listAttendance, markAttendance, listHomeworkByLesson, createHomework, createHomeworkWithFile, listSubmissions, reviewSubmission,
  listMySubmissions, submitHomework, submitHomeworkFile, fetchHomeworkAttachment, fetchSubmissionAttachment,
  type AttendanceStatus, type Homework, type HomeworkFile, type HomeworkType,
} from "@/lib/api/lms";
import { ApiError } from "@/lib/api-client";
import { useAuthSession } from "@/lib/use-auth-session";
import { usePermissions } from "@/lib/use-permissions";
import { canManageAcademic, isStudentOrParent, isTeachingStaff } from "@/lib/nav";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { Card, CardBody, CardHeader, CardTitle } from "@/components/ui/Card";
import { Tabs } from "@/components/ui/Tabs";
import { Skeleton } from "@/components/ui/Skeleton";
import { EmptyState } from "@/components/ui/EmptyState";
import { Alert } from "@/components/ui/Alert";
import { Drawer } from "@/components/ui/Drawer";
import { Input, Label, Select, Textarea } from "@/components/ui/Field";
import { Avatar } from "@/components/ui/Avatar";
import { LessonResources } from "@/components/LessonResources";
import { LessonJournal } from "@/components/LessonJournal";
import { DownloadFileButton, HomeworkFilePicker, homeworkFileProblem } from "@/components/HomeworkFiles";
import { LessonAdminActions } from "@/components/LessonActions";
import { HomeworkAdminActions } from "@/components/HomeworkActions";
import { listMyWritingEvaluations, listWritingReviews, type WritingEvaluationSummary } from "@/lib/api/ai";
import { WritingEvaluationView, WritingStatusBadge, formatBand, useWritingEvaluation } from "@/components/ai/WritingEvaluationView";
import { formatDateTime, formatShortDateTime, formatTime } from "@/lib/format-date";
import { safeHttpUrl } from "@/lib/safe-url";

const ATTENDANCE_OPTIONS: AttendanceStatus[] = ["Present", "Absent", "Late", "Excused", "Online"];
const ATTENDANCE_LABEL: Record<AttendanceStatus, string> = {
  Present: "Keldi", Absent: "Kelmadi", Late: "Kechikdi", Excused: "Sababli", Online: "Onlayn",
};
const ATTENDANCE_TONE: Record<AttendanceStatus, "success" | "danger" | "warning" | "neutral" | "brand"> = {
  Present: "success", Absent: "danger", Late: "warning", Excused: "neutral", Online: "brand",
};
const HOMEWORK_TYPES: HomeworkType[] = ["Writing", "Speaking", "Reading", "Listening", "Grammar", "Vocabulary", "FileUpload", "Quiz", "Custom"];

/** Three views of one lesson: teaching staff mark & grade, a student/parent reads their own rows and
 * submits, and an operator/other staff member gets the read-only roster. Every one of these is also
 * enforced by the API — the split here only removes controls that would end in a 403. */
type Viewer = "staff" | "personal" | "readonly";

function resolveViewer(role: string | undefined): Viewer {
  if (isTeachingStaff(role)) return "staff";
  if (isStudentOrParent(role)) return "personal";
  return "readonly";
}

export default function LessonDetailPage({ params }: { params: Promise<{ lessonId: string }> }) {
  const { lessonId } = use(params);
  const role = useAuthSession().user?.role;
  const viewer = resolveViewer(role);
  const canWriteJournal = usePermissions().can("attendance.mark");

  const lessonsQuery = useQuery({ queryKey: ["lessons"], queryFn: () => listLessons() });
  const lesson = lessonsQuery.data?.find((l) => l.id === lessonId);

  if (lessonsQuery.isLoading) {
    return <main className="p-4 sm:p-6 lg:p-8"><Skeleton className="h-24" /></main>;
  }
  if (lessonsQuery.isError) {
    return <main className="p-4 sm:p-6 lg:p-8"><Alert>{lessonsQuery.error instanceof ApiError ? lessonsQuery.error.message : "Yuklab bo'lmadi."}</Alert></main>;
  }
  if (!lesson) {
    return <main className="p-4 sm:p-6 lg:p-8"><Alert>Dars topilmadi yoki sizga ko&apos;rinmaydi.</Alert></main>;
  }

  // A cancelled lesson keeps what was recorded but takes no more: the server refuses new attendance and homework, so staff
  // see the panels read-only rather than buttons that can only fail.
  const recordViewer: Viewer = lesson.status === "Cancelled" && viewer === "staff" ? "readonly" : viewer;

  return (
    <main className="p-4 sm:p-6 lg:p-8">
      <Link href="/schedule" className="mb-4 inline-flex items-center gap-1.5 text-[13px] text-ink-500 hover:text-ink-800">
        <ArrowLeft className="size-3.5" aria-hidden />
        Dars jadvali
      </Link>

      <div className="mb-6 flex items-start justify-between gap-4 rounded-xl border border-border bg-surface p-5">
        <div>
          <h1 className={`text-[18px] font-semibold ${lesson.status === "Cancelled" ? "text-ink-400 line-through" : "text-ink-900"}`}>{lesson.topic || "Dars"}</h1>
          <div className="mt-1.5 flex flex-wrap items-center gap-3 text-[13px] text-ink-500">
            <span className="inline-flex items-center gap-1.5"><DoorOpen className="size-3.5 text-ink-400" aria-hidden />{lesson.roomName}</span>
            <span className="inline-flex items-center gap-1.5">
              <CalendarClock className="size-3.5 text-ink-400" aria-hidden />
              {formatDateTime(lesson.startsAt)} — {formatTime(lesson.endsAt)}
            </span>
          </div>
        </div>
        {canManageAcademic(role) && (
          <div className="flex flex-wrap justify-end gap-2">
            <LessonAdminActions lesson={lesson} />
            {lesson.status !== "Cancelled" && <ReplaceTeacherButton lesson={lesson} />}
          </div>
        )}
      </div>

      {lesson.status === "Cancelled" && (
        <div className="mb-6">
          <Alert>Bu dars bekor qilingan{lesson.cancelReason ? `: ${lesson.cancelReason}` : ""}. Davomat belgilab va vazifa berib bo&apos;lmaydi.</Alert>
        </div>
      )}

      {/* Topic and notes are the teacher's (attendance.mark); a student or parent reads the topic in the header, never the notes. */}
      {canWriteJournal && lesson.status !== "Cancelled" && <LessonJournal lessonId={lessonId} />}

      <Tabs
        tabs={[
          { id: "attendance", label: "Davomat", content: <AttendancePanel lessonId={lessonId} groupId={lesson.groupId} viewer={recordViewer} maxScore={lesson.maxLessonScore ?? null} /> },
          { id: "homework", label: "Homework", content: <HomeworkPanel lessonId={lessonId} groupId={lesson.groupId} viewer={recordViewer} /> },
          { id: "resources", label: "Materiallar", content: <LessonResources lessonId={lessonId} canCreate={viewer === "staff"} /> },
        ]}
      />
    </main>
  );
}

/** One roster row as typed on this screen; a field left out is whatever the server has. The score is text so a half-typed number survives. */
type RowEdit = { status?: AttendanceStatus; note?: string; score?: string };

function AttendancePanel({ lessonId, groupId, viewer, maxScore }: { lessonId: string; groupId: string; viewer: Viewer; maxScore: number | null }) {
  const queryClient = useQueryClient();
  // A support teacher sees the roster but holds no attendance.mark — offering them a Save button ends in a 403.
  const canMark = usePermissions().can("attendance.mark");
  const studentsQuery = useQuery({ queryKey: ["groups", groupId, "students"], queryFn: () => listGroupStudents(groupId) });
  const attendanceQuery = useQuery({ queryKey: ["lessons", lessonId, "attendance"], queryFn: () => listAttendance(lessonId) });

  // What the server has, overlaid with what the teacher changed on this screen. Derived on every render
  // rather than copied into state by an effect, so a refetch can never clobber (or be clobbered by) edits.
  const [edits, setEdits] = useState<Record<string, RowEdit>>({});
  const [openRows, setOpenRows] = useState<Record<string, boolean>>({});
  const savedByStudent = new Map((attendanceQuery.data ?? []).map((record) => [record.studentId, record]));
  const rowOf = (studentId: string) => {
    const saved = savedByStudent.get(studentId);
    const edit = edits[studentId];
    return {
      status: edit?.status ?? saved?.status ?? ("Present" as AttendanceStatus),
      note: edit?.note ?? saved?.note ?? "",
      score: edit?.score ?? (saved?.score != null ? String(saved.score) : ""),
      marked: saved !== undefined,
    };
  };
  const editRow = (studentId: string, patch: RowEdit) =>
    setEdits((prev) => ({ ...prev, [studentId]: { ...prev[studentId], ...patch } }));
  const scoreIsInvalid = (score: string) =>
    maxScore !== null && score.trim() !== "" && !(Number.isInteger(Number(score)) && Number(score) >= 0 && Number(score) <= maxScore);
  const anyInvalid = (studentsQuery.data ?? []).some((s) => scoreIsInvalid(rowOf(s.studentId).score));

  const saveMutation = useMutation({
    // Every roster row is sent: an unmarked student is displayed as "Keldi", so saving must record exactly
    // that. Previously only touched rows were posted and untouched students silently stayed unmarked.
    // An item is the student's whole record, so the remark and score go back as they stand on screen.
    mutationFn: () =>
      markAttendance(lessonId, (studentsQuery.data ?? []).map((s) => {
        const row = rowOf(s.studentId);
        return {
          studentId: s.studentId,
          status: row.status,
          note: row.note.trim() || null,
          score: maxScore !== null && row.score.trim() !== "" ? Number(row.score) : null,
        };
      })),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["lessons", lessonId, "attendance"] });
      setEdits({});
    },
  });

  if (studentsQuery.isLoading || attendanceQuery.isLoading) return <Skeleton className="h-48" />;
  if (studentsQuery.isError) {
    return <Alert>{studentsQuery.error instanceof ApiError ? studentsQuery.error.message : "Yuklab bo'lmadi."}</Alert>;
  }

  // A student/parent only receives their own attendance rows — show exactly those, as a status card.
  if (viewer === "personal") {
    const own = attendanceQuery.data ?? [];
    const nameById = new Map(studentsQuery.data?.map((s) => [s.studentId, s.fullName]));
    if (own.length === 0) {
      return (
        <div className="rounded-xl border border-border bg-surface">
          <EmptyState icon={Clock3} title="Davomat hali belgilanmagan" description="O'qituvchi darsdan so'ng davomatni kiritadi." />
        </div>
      );
    }
    return (
      <Card>
        <div className="divide-y divide-border">
          {own.map((record) => (
            <div key={record.id} className="flex items-center gap-3 px-5 py-3.5">
              <Avatar name={nameById.get(record.studentId) ?? "Student"} />
              <p className="min-w-0 flex-1 truncate text-[13.5px] font-medium text-ink-900">
                {nameById.get(record.studentId) ?? "Siz"}
              </p>
              {record.score !== null && maxScore !== null && (
                <span className="font-tabular text-[13px] font-medium text-ink-700">{record.score} / {maxScore}</span>
              )}
              <Badge tone={ATTENDANCE_TONE[record.status]} dot>{ATTENDANCE_LABEL[record.status]}</Badge>
            </div>
          ))}
        </div>
      </Card>
    );
  }

  if (studentsQuery.data && studentsQuery.data.length === 0) {
    return <div className="rounded-xl border border-border bg-surface"><EmptyState icon={FileCheck2} title="Guruhda studentlar yo'q" /></div>;
  }

  const editable = viewer === "staff" && canMark;

  return (
    <Card>
      <CardHeader>
        <CardTitle>Davomat ro&apos;yxati</CardTitle>
        {editable && (
          <Button size="sm" loading={saveMutation.isPending} disabled={anyInvalid} onClick={() => saveMutation.mutate()}>Saqlash</Button>
        )}
      </CardHeader>
      <div className="divide-y divide-border">
        {studentsQuery.data?.map((student) => {
          const row = rowOf(student.studentId);
          const isOpen = openRows[student.studentId] === true;
          const invalid = scoreIsInvalid(row.score);
          const hasExtras = row.note.trim() !== "" || (maxScore !== null && row.score.trim() !== "");
          return (
            <div key={student.studentId} className="px-5 py-3">
              <div className="flex items-center gap-3">
                <Avatar name={student.fullName} />
                <div className="min-w-0 flex-1">
                  <p className="truncate text-[13.5px] font-medium text-ink-900">{student.fullName}</p>
                  <p className="font-mono text-[12px] text-ink-500">{student.studentNumber}</p>
                  {!editable && row.note && <p className="mt-0.5 text-[12.5px] text-ink-500">{row.note}</p>}
                </div>
                {editable ? (
                  <>
                    {/* A tap, not a hover: the remark and the score live one tap under the row, on a phone as on a desktop. */}
                    <Button
                      variant="ghost"
                      size="sm"
                      aria-label={`${student.fullName}: izoh va ball`}
                      aria-expanded={isOpen}
                      onClick={() => setOpenRows((prev) => ({ ...prev, [student.studentId]: !isOpen }))}
                    >
                      <MessageSquare className={`size-4 ${hasExtras ? "text-brand-600" : "text-ink-400"}`} aria-hidden />
                      {maxScore !== null && row.score.trim() !== "" && <span className="font-tabular text-[12.5px]">{row.score}</span>}
                    </Button>
                    <Select
                      value={row.status}
                      onChange={(e) => editRow(student.studentId, { status: e.target.value as AttendanceStatus })}
                      className="w-auto"
                    >
                      {ATTENDANCE_OPTIONS.map((s) => <option key={s} value={s}>{ATTENDANCE_LABEL[s]}</option>)}
                    </Select>
                  </>
                ) : row.marked ? (
                  <>
                    {maxScore !== null && row.score !== "" && (
                      <span className="font-tabular text-[13px] font-medium text-ink-700">{row.score} / {maxScore}</span>
                    )}
                    <Badge tone={ATTENDANCE_TONE[row.status]} dot>{ATTENDANCE_LABEL[row.status]}</Badge>
                  </>
                ) : (
                  <span className="text-[12.5px] text-ink-400">Belgilanmagan</span>
                )}
              </div>
              {editable && isOpen && (
                <div className="mt-3 flex flex-col gap-3 sm:flex-row sm:items-start sm:pl-11">
                  {maxScore !== null && (
                    <div className="sm:w-32">
                      <Label htmlFor={`score-${student.studentId}`}>Ball (0–{maxScore})</Label>
                      <Input
                        id={`score-${student.studentId}`}
                        type="number"
                        inputMode="numeric"
                        min={0}
                        max={maxScore}
                        value={row.score}
                        aria-invalid={invalid}
                        onChange={(e) => editRow(student.studentId, { score: e.target.value })}
                      />
                      {invalid && <p className="mt-1 text-[12px] text-danger-600">0 dan {maxScore} gacha butun son.</p>}
                    </div>
                  )}
                  <div className="min-w-0 flex-1">
                    <Label htmlFor={`note-${student.studentId}`}>Izoh (faqat xodimlar ko&apos;radi)</Label>
                    <Input
                      id={`note-${student.studentId}`}
                      value={row.note}
                      maxLength={500}
                      onChange={(e) => editRow(student.studentId, { note: e.target.value })}
                      placeholder="Masalan: kechikdi — avtobus buzildi"
                    />
                  </div>
                </div>
              )}
            </div>
          );
        })}
      </div>
      {saveMutation.isError && (
        <div className="p-4"><Alert>{saveMutation.error instanceof ApiError ? saveMutation.error.message : "Saqlab bo'lmadi."}</Alert></div>
      )}
    </Card>
  );
}

function HomeworkPanel({ lessonId, groupId, viewer }: { lessonId: string; groupId: string; viewer: Viewer }) {
  const queryClient = useQueryClient();
  const { can } = usePermissions();
  const homeworkQuery = useQuery({ queryKey: ["homework", "lesson", lessonId], queryFn: () => listHomeworkByLesson(lessonId) });

  const [drawerOpen, setDrawerOpen] = useState(false);
  const [reviewTargetHomeworkId, setReviewTargetHomeworkId] = useState<string | null>(null);
  const [submitTarget, setSubmitTarget] = useState<Homework | null>(null);
  const [title, setTitle] = useState("");
  const [description, setDescription] = useState("");
  const [type, setType] = useState<HomeworkType>("Writing");
  const [deadline, setDeadline] = useState("");
  const [maxScore, setMaxScore] = useState(100);
  const [file, setFile] = useState<File | null>(null);
  const [fileInputKey, setFileInputKey] = useState(0); // a file input cannot be cleared from outside — remounting it does
  const fileProblem = file ? homeworkFileProblem(file) : null;

  // "Kimga": the whole group, or a few students named one by one (an individual task for someone who needs it).
  const [audience, setAudience] = useState<"group" | "students">("group");
  const [pickedStudents, setPickedStudents] = useState<string[]>([]);
  const studentsQuery = useQuery({
    queryKey: ["groups", groupId, "students"],
    queryFn: () => listGroupStudents(groupId),
    enabled: drawerOpen && audience === "students",
  });
  const noOneChosen = audience === "students" && pickedStudents.length === 0;

  const createMutation = useMutation({
    mutationFn: () => {
      const input = {
        lessonId, title, description: description || undefined, type, deadline: new Date(deadline).toISOString(), maxScore,
        studentIds: audience === "students" ? pickedStudents : undefined,
      };
      return file ? createHomeworkWithFile({ ...input, file }) : createHomework(input);
    },
    onSuccess: () => {
      setTitle(""); setDescription(""); setFile(null); setFileInputKey((key) => key + 1); setDrawerOpen(false);
      setAudience("group"); setPickedStudents([]);
      queryClient.invalidateQueries({ queryKey: ["homework", "lesson", lessonId] });
    },
  });

  const submissionsQuery = useQuery({
    queryKey: ["homework", reviewTargetHomeworkId, "submissions"],
    queryFn: () => listSubmissions(reviewTargetHomeworkId!),
    enabled: reviewTargetHomeworkId !== null && viewer === "staff",
  });

  const reviewedHomework = homeworkQuery.data?.find((h) => h.id === reviewTargetHomeworkId);
  const canCreate = viewer === "staff" && can("homework.create");

  // Writing homework: every teaching-staff role holds ai.writing.review, so the AI assessments ride along with the submissions.
  const [aiEvaluationId, setAiEvaluationId] = useState<string | null>(null);
  const aiReviewsQuery = useQuery({
    queryKey: ["ai", "writing", "reviews", "homework", reviewTargetHomeworkId],
    queryFn: () => listWritingReviews({ homeworkId: reviewTargetHomeworkId! }),
    enabled: viewer === "staff" && reviewedHomework?.type === "Writing",
    refetchInterval: (query) => (query.state.data?.some((e) => e.status === "Queued" || e.status === "Processing") ? 5000 : false),
  });
  const aiBySubmission = new Map(
    (aiReviewsQuery.data ?? []).filter((e) => e.homeworkSubmissionId).map((e) => [e.homeworkSubmissionId!, e]),
  );
  const aiEvaluationQuery = useWritingEvaluation(aiEvaluationId);

  return (
    <div>
      {canCreate && (
        <div className="mb-3 flex justify-end">
          <Button size="sm" onClick={() => setDrawerOpen(true)}>
            <Plus className="size-3.5" aria-hidden />
            Homework qo&apos;shish
          </Button>
        </div>
      )}

      {homeworkQuery.isLoading && <Skeleton className="h-32" />}
      {homeworkQuery.isError && (
        <Alert>{homeworkQuery.error instanceof ApiError ? homeworkQuery.error.message : "Yuklab bo'lmadi."}</Alert>
      )}
      {homeworkQuery.data && homeworkQuery.data.length === 0 && (
        <div className="rounded-xl border border-border bg-surface"><EmptyState icon={FileCheck2} title="Bu darsga homework berilmagan" /></div>
      )}

      <div className="space-y-3">
        {homeworkQuery.data?.map((hw) => (
          <Card key={hw.id}>
            {/* Stacked on a phone — the title, the badges and the file name need the whole width; side by side from `sm` up. */}
            <CardBody className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between sm:gap-4">
              <div className="min-w-0">
                <p className="text-[13.5px] font-medium text-ink-900">{hw.title}</p>
                {hw.description && <p className="mt-1 text-[13px] text-ink-600">{hw.description}</p>}
                <div className="mt-1.5 flex flex-wrap items-center gap-1.5">
                  <Badge tone="brand">{hw.type}</Badge>
                  {hw.targeted && <Badge tone="info">Individual{hw.studentIds ? ` · ${hw.studentIds.length} student` : ""}</Badge>}
                </div>
                <p className="mt-1.5 text-[12.5px] text-ink-500">Muddat: {formatDateTime(hw.deadline)} · Max ball: {hw.maxScore}</p>
                {hw.attachment && (
                  <div className="mt-1.5">
                    <DownloadFileButton name={hw.attachment.fileName} size={hw.attachment.size} load={() => fetchHomeworkAttachment(hw.id)} />
                  </div>
                )}
              </div>
              <div className="flex shrink-0 flex-wrap items-center gap-1 sm:justify-end">
                {canCreate && <HomeworkAdminActions homework={hw} />}
                {viewer === "staff" && can("homework.review") && (
                  <Button variant="secondary" size="sm" onClick={() => setReviewTargetHomeworkId(hw.id)}>
                    Topshiriqlar
                  </Button>
                )}
              </div>
              {viewer === "personal" && <MySubmissionStatus homework={hw} onSubmit={() => setSubmitTarget(hw)} />}
            </CardBody>
          </Card>
        ))}
      </div>

      <Drawer open={drawerOpen} onClose={() => setDrawerOpen(false)} title="Yangi homework">
        <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); createMutation.mutate(); }}>
          <div>
            <Label htmlFor="hw-title">Sarlavha</Label>
            <Input id="hw-title" required value={title} onChange={(e) => setTitle(e.target.value)} />
          </div>
          <div>
            <Label htmlFor="hw-desc">Tavsif</Label>
            <Textarea id="hw-desc" value={description} onChange={(e) => setDescription(e.target.value)} />
          </div>
          <div>
            <Label htmlFor="hw-type">Turi</Label>
            <Select id="hw-type" value={type} onChange={(e) => setType(e.target.value as HomeworkType)}>
              {HOMEWORK_TYPES.map((t) => <option key={t} value={t}>{t}</option>)}
            </Select>
          </div>
          <div>
            <Label htmlFor="hw-deadline">Muddat</Label>
            <Input id="hw-deadline" type="datetime-local" required value={deadline} onChange={(e) => setDeadline(e.target.value)} />
          </div>
          <div>
            <Label htmlFor="hw-max">Maksimal ball</Label>
            <Input id="hw-max" type="number" min={1} required value={maxScore} onChange={(e) => setMaxScore(Number(e.target.value))} />
          </div>
          <div>
            <Label htmlFor="hw-audience">Kimga</Label>
            <Select id="hw-audience" value={audience} onChange={(e) => setAudience(e.target.value as "group" | "students")}>
              <option value="group">Butun guruhga</option>
              <option value="students">Tanlangan studentlarga (individual)</option>
            </Select>
          </div>
          {audience === "students" && (
            <div>
              {studentsQuery.isLoading && <Skeleton className="h-24" />}
              {studentsQuery.isError && <Alert>{studentsQuery.error instanceof ApiError ? studentsQuery.error.message : "Studentlarni yuklab bo'lmadi."}</Alert>}
              {studentsQuery.data && studentsQuery.data.length === 0 && <p className="text-[13px] text-ink-500">Guruhda studentlar yo&apos;q.</p>}
              {studentsQuery.data && studentsQuery.data.length > 0 && (
                <fieldset className="max-h-56 space-y-1 overflow-y-auto rounded-lg border border-border p-2">
                  <legend className="sr-only">Studentlar</legend>
                  {studentsQuery.data.map((student) => (
                    <label key={student.studentId} className="flex cursor-pointer items-center gap-2.5 rounded-md px-2 py-1.5 text-[13.5px] text-ink-800">
                      <input
                        type="checkbox"
                        checked={pickedStudents.includes(student.studentId)}
                        onChange={(e) => setPickedStudents((prev) =>
                          e.target.checked ? [...prev, student.studentId] : prev.filter((id) => id !== student.studentId))}
                      />
                      <span className="min-w-0 flex-1 truncate">{student.fullName}</span>
                      <span className="font-mono text-[12px] text-ink-500">{student.studentNumber}</span>
                    </label>
                  ))}
                </fieldset>
              )}
              <p className="mt-1.5 text-[12.5px] text-ink-500">
                {noOneChosen ? "Kamida bitta student tanlang." : `${pickedStudents.length} ta student tanlandi — homeworkni faqat ular ko'radi va topshiradi.`}
              </p>
            </div>
          )}
          <HomeworkFilePicker key={fileInputKey} id="hw-file" label="Topshiriq fayli (ixtiyoriy)" file={file} onChange={setFile} />
          {createMutation.isError && (
            <Alert>{createMutation.error instanceof ApiError ? createMutation.error.message : "Yaratib bo'lmadi."}</Alert>
          )}
          <Button type="submit" loading={createMutation.isPending} disabled={fileProblem !== null || noOneChosen} className="w-full">Yaratish</Button>
        </form>
      </Drawer>

      <Drawer
        open={reviewTargetHomeworkId !== null}
        onClose={() => setReviewTargetHomeworkId(null)}
        title="Topshiriqlar"
        description={reviewedHomework?.title}
      >
        {submissionsQuery.isLoading && <Skeleton className="h-32" />}
        {submissionsQuery.isError && (
          <Alert>{submissionsQuery.error instanceof ApiError ? submissionsQuery.error.message : "Yuklab bo'lmadi."}</Alert>
        )}
        {submissionsQuery.data?.length === 0 && <EmptyState icon={FileCheck2} title="Hali topshirilmagan" />}
        <div className="space-y-4">
          {submissionsQuery.data?.map((submission) => (
            <SubmissionCard
              key={submission.id}
              submission={submission}
              homeworkId={reviewTargetHomeworkId!}
              maxScore={reviewedHomework?.maxScore ?? 100}
              aiEvaluation={aiBySubmission.get(submission.id)}
              onOpenAi={setAiEvaluationId}
            />
          ))}
        </div>
      </Drawer>

      <Drawer
        open={aiEvaluationId !== null}
        onClose={() => setAiEvaluationId(null)}
        title="AI writing tahlili"
        description={aiEvaluationQuery.data?.summary.studentName}
      >
        {aiEvaluationQuery.isLoading && <Skeleton className="h-48" />}
        {aiEvaluationQuery.isError && <Alert>{aiEvaluationQuery.error instanceof ApiError ? aiEvaluationQuery.error.message : "Yuklab bo'lmadi."}</Alert>}
        {aiEvaluationQuery.data && <WritingEvaluationView evaluation={aiEvaluationQuery.data} compact />}
      </Drawer>

      {submitTarget && (
        <SubmitDrawer homework={submitTarget} onClose={() => setSubmitTarget(null)} />
      )}
    </div>
  );
}

/** The student's own state for one homework: not submitted → button; submitted → waiting / graded. */
function MySubmissionStatus({ homework, onSubmit }: { homework: Homework; onSubmit: () => void }) {
  const isStudent = useAuthSession().user?.role === "STUDENT";
  const mineQuery = useQuery({ queryKey: ["homework", homework.id, "mine"], queryFn: () => listMySubmissions(homework.id) });

  if (mineQuery.isLoading) return <Skeleton className="h-8 w-28" />;
  const mine = mineQuery.data?.[0];

  if (!mine) {
    const overdue = new Date(homework.deadline) < new Date();
    if (!isStudent) return <span className="text-[12.5px] text-ink-400">Topshirilmagan</span>;
    return (
      <Button size="sm" variant={overdue ? "secondary" : "primary"} onClick={onSubmit}>
        <Send className="size-3.5" aria-hidden />
        {overdue ? "Kech topshirish" : "Topshirish"}
      </Button>
    );
  }

  const aiLink = isStudent && homework.type === "Writing" ? <MyAiWritingLink homeworkId={homework.id} /> : null;

  const ownFile = mine.file && (
    <div className="mt-1 flex justify-end"><SubmissionFileButton submissionId={mine.id} file={mine.file} /></div>
  );

  if (mine.score === null) {
    return (
      <div className="text-right">
        <span className="inline-flex items-center gap-1.5 text-[12.5px] text-ink-500">
          <Clock3 className="size-3.5" aria-hidden />
          Tekshirilmoqda
        </span>
        {ownFile}
        {aiLink}
      </div>
    );
  }

  return (
    <div className="text-right">
      <span className="inline-flex items-center gap-1.5 text-[13px] font-medium text-success-600">
        <CheckCircle2 className="size-3.5" aria-hidden />
        {mine.score} / {homework.maxScore}
      </span>
      {mine.feedback && <p className="mt-0.5 max-w-[220px] text-[12px] text-ink-500">{mine.feedback}</p>}
      {ownFile}
      {aiLink}
    </div>
  );
}

/** A student's answer file, fetched through the submission that carries it. */
function SubmissionFileButton({ submissionId, file }: { submissionId: string; file: HomeworkFile }) {
  return <DownloadFileButton name={file.fileName} size={file.size} load={() => fetchSubmissionAttachment(submissionId)} />;
}

/** A student's AI assessment of their Writing homework, if one was made. */
function MyAiWritingLink({ homeworkId }: { homeworkId: string }) {
  const query = useQuery({
    queryKey: ["ai", "writing", "homework", homeworkId],
    queryFn: () => listMyWritingEvaluations(homeworkId),
    refetchInterval: (q) => (q.state.data?.some((e) => e.status === "Queued" || e.status === "Processing") ? 5000 : false),
  });
  const evaluation = query.data?.[0];
  if (!evaluation) return null;

  return (
    <Link
      href={`/ai/writing?evaluation=${evaluation.id}`}
      className="mt-1 flex items-center justify-end gap-1 text-[12.5px] font-medium text-brand-600 hover:underline"
    >
      <Sparkles className="size-3.5" aria-hidden />
      {evaluation.status === "Completed" ? `AI band ${formatBand(evaluation.estimatedBand)}` : "AI tahlili"}
    </Link>
  );
}

function SubmitDrawer({ homework, onClose }: { homework: Homework; onClose: () => void }) {
  const queryClient = useQueryClient();
  const [contentText, setContentText] = useState("");
  const [contentUrl, setContentUrl] = useState("");
  const [file, setFile] = useState<File | null>(null);
  const fileProblem = file ? homeworkFileProblem(file) : null;

  const mutation = useMutation({
    // A file goes with the student's own words; the link is the other way to hand in something too big or already in Drive.
    mutationFn: () => file
      ? submitHomeworkFile(homework.id, { file, contentText: contentText || undefined })
      : submitHomework(homework.id, { contentText: contentText || undefined, contentUrl: contentUrl || undefined }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["homework", homework.id, "mine"] });
      queryClient.invalidateQueries({ queryKey: ["ai", "writing", "homework", homework.id] });
      onClose();
    },
  });

  return (
    <Drawer open onClose={onClose} title="Homework topshirish" description={homework.title}>
      <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); mutation.mutate(); }}>
        <div>
          <Label htmlFor="submit-text">Javob matni</Label>
          <Textarea
            id="submit-text"
            rows={8}
            value={contentText}
            onChange={(e) => setContentText(e.target.value)}
            placeholder={homework.type === "Writing" ? "Essay yoki javobingizni shu yerga yozing..." : "Javobingiz..."}
          />
        </div>
        <HomeworkFilePicker id="submit-file" label="Fayl yuklash (ixtiyoriy)" file={file} onChange={setFile} />
        <div>
          <Label htmlFor="submit-url">Fayl havolasi (ixtiyoriy)</Label>
          <Input
            id="submit-url"
            type="url"
            value={contentUrl}
            disabled={file !== null}
            onChange={(e) => setContentUrl(e.target.value)}
            placeholder="https://drive.google.com/..."
          />
          {file !== null && <p className="mt-1.5 text-[12.5px] text-ink-500">Fayl tanlangan — havola yuborilmaydi.</p>}
        </div>
        {mutation.isError && (
          <Alert>{mutation.error instanceof ApiError ? mutation.error.message : "Topshirib bo'lmadi."}</Alert>
        )}
        <Button
          type="submit"
          loading={mutation.isPending}
          className="w-full"
          disabled={fileProblem !== null || (!contentText.trim() && !contentUrl.trim() && file === null)}
        >
          <Send className="size-4" aria-hidden />
          Topshirish
        </Button>
      </form>
    </Drawer>
  );
}

/** Teacher Replacement (docs/00_TZ.md §19, audit §3.12/§5.10): the substitute list is fetched only once the
 * drawer opens, and a past lesson never gets the button at all — the API would refuse it either way. */
function ReplaceTeacherButton({ lesson }: { lesson: Lesson }) {
  const queryClient = useQueryClient();
  const [open, setOpen] = useState(false);
  const [teacherId, setTeacherId] = useState("");
  const [reason, setReason] = useState("");
  const [conflicts, setConflicts] = useState<ScheduleConflict[]>([]);

  const substitutesQuery = useQuery({
    queryKey: ["schedule", "lessons", lesson.id, "substitutes"],
    queryFn: () => listSubstitutes(lesson.id),
    enabled: open,
  });

  const mutation = useMutation({
    mutationFn: () => replaceTeacher(lesson.id, teacherId, reason),
    onSuccess: (result) => {
      if (result.ok) {
        setConflicts([]);
        setTeacherId("");
        setReason("");
        setOpen(false);
        queryClient.invalidateQueries({ queryKey: ["lessons"] });
      } else {
        setConflicts(result.conflicts);
      }
    },
  });

  const isPast = new Date(lesson.endsAt) < new Date();
  if (isPast) return null;

  return (
    <>
      <Button size="sm" variant="secondary" onClick={() => setOpen(true)}>
        <UserCog className="size-3.5" aria-hidden />
        O&apos;qituvchini almashtirish
      </Button>
      <Drawer
        open={open}
        onClose={() => setOpen(false)}
        title="O'qituvchini almashtirish"
        description="Zaxira o'qituvchining jadvali avtomatik tekshiriladi."
      >
        <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); mutation.mutate(); }}>
          {substitutesQuery.isLoading && <Skeleton className="h-24" />}
          {substitutesQuery.isError && (
            <Alert>{substitutesQuery.error instanceof ApiError ? substitutesQuery.error.message : "Yuklab bo'lmadi."}</Alert>
          )}
          {substitutesQuery.data && substitutesQuery.data.length === 0 && (
            <p className="text-[13px] text-ink-500">Bu vaqtda bo&apos;sh o&apos;qituvchi topilmadi.</p>
          )}
          {substitutesQuery.data && substitutesQuery.data.length > 0 && (
            <div>
              <Label htmlFor="substitute">Yangi o&apos;qituvchi</Label>
              <Select id="substitute" required value={teacherId} onChange={(e) => setTeacherId(e.target.value)}>
                <option value="">Tanlang</option>
                {substitutesQuery.data.map((s) => (
                  <option key={s.userId} value={s.userId}>{s.fullName}{s.isMainTeacher ? " (asosiy)" : ""}</option>
                ))}
              </Select>
            </div>
          )}
          <div>
            <Label htmlFor="replace-reason">Sabab</Label>
            <Textarea id="replace-reason" required value={reason} onChange={(e) => setReason(e.target.value)} />
          </div>
          {conflicts.length > 0 && (
            <div className="rounded-lg border border-warning-500/30 bg-warning-50 p-3">
              <p className="flex items-center gap-1.5 text-[13px] font-medium text-warning-600">
                <AlertTriangle className="size-4" aria-hidden />
                Zaxira o&apos;qituvchi shu vaqtda band
              </p>
              <ul className="mt-2 space-y-1 text-[12.5px] text-warning-600">
                {conflicts.map((c, i) => (
                  <li key={i}>{formatShortDateTime(c.startsAt)} dan {formatTime(c.endsAt)} gacha</li>
                ))}
              </ul>
            </div>
          )}
          {mutation.isError && (
            <Alert>{mutation.error instanceof ApiError ? mutation.error.message : "Almashtirib bo'lmadi."}</Alert>
          )}
          <Button type="submit" loading={mutation.isPending} disabled={!teacherId} className="w-full">
            Almashtirish
          </Button>
        </form>
      </Drawer>
    </>
  );
}

function SubmissionCard({
  submission,
  homeworkId,
  maxScore,
  aiEvaluation,
  onOpenAi,
}: {
  submission: { id: string; studentId: string; contentText: string | null; contentUrl: string | null; file: HomeworkFile | null; score: number | null; feedback: string | null };
  homeworkId: string;
  maxScore: number;
  aiEvaluation?: WritingEvaluationSummary;
  onOpenAi: (evaluationId: string) => void;
}) {
  const queryClient = useQueryClient();
  const [score, setScore] = useState(submission.score ?? 0);
  const [feedback, setFeedback] = useState(submission.feedback ?? "");

  const mutation = useMutation({
    mutationFn: () => reviewSubmission(submission.id, score, feedback || undefined),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["homework", homeworkId, "submissions"] }),
  });

  return (
    <div className="rounded-lg border border-border p-3.5">
      {aiEvaluation && (
        <div className="mb-2.5 flex items-center justify-between gap-2 rounded-md bg-brand-50 px-2.5 py-1.5">
          <span className="flex items-center gap-1.5 text-[12.5px] text-brand-800">
            <Sparkles className="size-3.5" aria-hidden />
            {aiEvaluation.status === "Completed"
              ? <>AI band <b className="font-tabular">{formatBand(aiEvaluation.estimatedBand)}</b>{aiEvaluation.teacherBand !== null && <> · siz: <b className="font-tabular">{formatBand(aiEvaluation.teacherBand)}</b></>}</>
              : <WritingStatusBadge status={aiEvaluation.status} />}
          </span>
          <Button size="sm" variant="ghost" onClick={() => onOpenAi(aiEvaluation.id)}>AI tahlil</Button>
        </div>
      )}
      <p className="whitespace-pre-wrap text-[13px] text-ink-700">{submission.contentText ?? "—"}</p>
      {safeHttpUrl(submission.contentUrl) && (
        <a href={safeHttpUrl(submission.contentUrl)!} target="_blank" rel="noopener noreferrer" className="mt-1 inline-block text-[12.5px] text-brand-600 underline-offset-2 hover:underline">
          Biriktirilgan fayl
        </a>
      )}
      {submission.file && <div className="mt-1.5"><SubmissionFileButton submissionId={submission.id} file={submission.file} /></div>}
      <div className="mt-3 flex items-end gap-2">
        <div className="flex-1">
          <Label htmlFor={`score-${submission.id}`}>Ball (0–{maxScore})</Label>
          <Input id={`score-${submission.id}`} type="number" min={0} max={maxScore} value={score} onChange={(e) => setScore(Number(e.target.value))} />
        </div>
        <div className="flex-[2]">
          <Label htmlFor={`feedback-${submission.id}`}>Izoh</Label>
          <Input id={`feedback-${submission.id}`} value={feedback} onChange={(e) => setFeedback(e.target.value)} />
        </div>
        <Button size="sm" loading={mutation.isPending} onClick={() => mutation.mutate()}>
          <Star className="size-3.5" aria-hidden />
          Baholash
        </Button>
      </div>
      {mutation.isError && (
        <p className="mt-2 text-[12.5px] text-danger-600">{mutation.error instanceof ApiError ? mutation.error.message : "Baholab bo'lmadi."}</p>
      )}
      {submission.score !== null && (
        <p className="mt-2 text-[12.5px] text-success-600">Baholangan: {submission.score} ball</p>
      )}
    </div>
  );
}
