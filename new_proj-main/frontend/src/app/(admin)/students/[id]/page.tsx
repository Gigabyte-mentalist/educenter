"use client";

import { useQuery } from "@tanstack/react-query";
import { Suspense, use } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import {
  ArrowLeft, CalendarCheck, CalendarClock, ClipboardList, TrendingUp, UsersRound,
  LogIn, LogOut, CheckCircle2, XCircle, FileText, PenSquare, Award, HeartHandshake, LifeBuoy, Ban, Contact, PhoneCall, Sparkles, Wallet, RotateCcw, Percent, ArrowLeftRight,
  MessageCircle, UserPlus, UserMinus, GraduationCap, MessageSquareWarning,
} from "lucide-react";
import { getStudentOverview, getStudentTimeline, type ParentRelationship, type StudentStatus } from "@/lib/api/students";
import { getStudentFinance } from "@/lib/api/finance";
import { ApiError } from "@/lib/api-client";
import { Badge } from "@/components/ui/Badge";
import { Avatar } from "@/components/ui/Avatar";
import { StatTile } from "@/components/ui/StatTile";
import { Tabs } from "@/components/ui/Tabs";
import { Skeleton } from "@/components/ui/Skeleton";
import { EmptyState } from "@/components/ui/EmptyState";
import { Alert } from "@/components/ui/Alert";
import { cn } from "@/lib/cn";
import { StudentAttendanceTab, StudentHomeworkTab, StudentExamsTab } from "@/components/StudentLearningTabs";
import { StudentLearningProfile } from "@/components/StudentLearningProfile";
import { StudentWritingTab } from "@/components/StudentWritingTab";
import { StudentSpeakingTab } from "@/components/StudentSpeakingTab";
import { PhotoUpload } from "@/components/PhotoUpload";
import { useAuthSession } from "@/lib/use-auth-session";
import { StudentSupportTab, canViewSupport } from "@/components/SupportCases";
import { StudentFinanceTab } from "@/components/Finance";
import { DropEnrollmentButton, StudentLifecycleActions } from "@/components/StudentLifecycle";
import { StudentEditButton } from "@/components/StudentEdit";
import { StudentParents } from "@/components/StudentParents";
import { usePermissions } from "@/lib/use-permissions";
import { StudentDocumentsTab, canViewStudentDocuments, canManageStudentDocuments } from "@/components/StudentDocuments";
import { OpenChatButton } from "@/components/OpenChatButton";
import { StudentComplaintButton } from "@/components/complaints/StudentComplaintButton";
import { formatDateTime, formatShortDate } from "@/lib/format-date";
import { formatMoney } from "@/lib/api/finance";

const STATUS_LABEL: Record<StudentStatus, string> = {
  Active: "Faol", Debt: "Qarzdor", Frozen: "Muzlatilgan", Transferred: "Ko'chirilgan", Graduated: "Bitirgan", Dropped: "Tark etgan",
};
const STATUS_TONE: Record<StudentStatus, "success" | "warning" | "neutral" | "brand" | "danger"> = {
  Active: "success", Debt: "warning", Frozen: "neutral", Transferred: "brand", Graduated: "brand", Dropped: "danger",
};

const RELATIONSHIP_LABEL: Record<ParentRelationship, string> = { Father: "ota", Mother: "ona", Guardian: "vasiy", Other: "boshqa" };

const EVENT_CONFIG: Record<string, { label: string; icon: typeof LogIn; tone: string }> = {
  LEAD_CREATED: { label: "Lid sifatida ro'yxatga olindi", icon: Contact, tone: "text-ink-600 bg-ink-100" },
  CRM_CALL: { label: "Operator qo'ng'iroq qildi", icon: PhoneCall, tone: "text-ink-600 bg-ink-100" },
  TRIAL_LESSON: { label: "Trial dars", icon: Sparkles, tone: "text-accent-700 bg-accent-300/40" },
  ENROLLED: { label: "Guruhga yozildi", icon: LogIn, tone: "text-brand-600 bg-brand-100" },
  LESSON_ATTENDED: { label: "Darsga keldi", icon: CheckCircle2, tone: "text-success-600 bg-success-50" },
  LESSON_ABSENT: { label: "Darsga kelmadi", icon: XCircle, tone: "text-danger-600 bg-danger-50" },
  HOMEWORK_SUBMITTED: { label: "Homework topshirdi", icon: FileText, tone: "text-brand-600 bg-brand-100" },
  HOMEWORK_REVIEWED: { label: "Homework baholandi", icon: PenSquare, tone: "text-info-600 bg-info-50" },
  EXAM_RESULT_RECORDED: { label: "Imtihon natijasi", icon: Award, tone: "text-accent-700 bg-accent-300/40" },
  EXAM_RESULT_REMOVED: { label: "Imtihon natijasi olib tashlandi", icon: Award, tone: "text-ink-500 bg-ink-100" },
  SUPPORT_RECOMMENDED: { label: "Support tavsiya qilindi", icon: LifeBuoy, tone: "text-warning-600 bg-warning-50" },
  SUPPORT_ASSIGNED: { label: "Yordamchi o'qituvchi biriktirildi", icon: HeartHandshake, tone: "text-brand-600 bg-brand-100" },
  SUPPORT_DISMISSED: { label: "Support tavsiyasi rad etildi", icon: Ban, tone: "text-ink-500 bg-ink-100" },
  SUPPORT_RESOLVED: { label: "Support yakunlandi", icon: CheckCircle2, tone: "text-success-600 bg-success-50" },
  COMPLAINT_CREATED: { label: "Shikoyat yuborildi", icon: MessageSquareWarning, tone: "text-warning-600 bg-warning-50" },
  COMPLAINT_RESOLVED: { label: "Shikoyat yechildi", icon: CheckCircle2, tone: "text-success-600 bg-success-50" },
  PAYMENT_RECEIVED: { label: "To'lov qabul qilindi", icon: Wallet, tone: "text-success-600 bg-success-50" },
  PAYMENT_VOIDED: { label: "To'lov bekor qilindi", icon: RotateCcw, tone: "text-danger-600 bg-danger-50" },
  DISCOUNT_APPLIED: { label: "Chegirma berildi", icon: Percent, tone: "text-brand-600 bg-brand-100" },
  DISCOUNT_REVOKED: { label: "Chegirma bekor qilindi", icon: Percent, tone: "text-ink-500 bg-ink-100" },
  INVOICE_SPLIT: { label: "To'lov qismlarga bo'lindi", icon: CalendarClock, tone: "text-info-600 bg-info-50" },
  REFUND_ISSUED: { label: "Pul qaytarildi", icon: ArrowLeftRight, tone: "text-warning-600 bg-warning-50" },
  WRITING_EVALUATED: { label: "AI writing tahlili", icon: Sparkles, tone: "text-brand-600 bg-brand-100" },
  WRITING_OVERRIDDEN: { label: "O'qituvchi writing bahosini ko'rib chiqdi", icon: PenSquare, tone: "text-info-600 bg-info-50" },
  SPEAKING_REVIEWED: { label: "O'qituvchi speaking bahosini ko'rib chiqdi", icon: PenSquare, tone: "text-info-600 bg-info-50" },
  PARENT_LINKED: { label: "Ota-ona biriktirildi", icon: UserPlus, tone: "text-brand-600 bg-brand-100" },
  PARENT_UNLINKED: { label: "Ota-ona bilan bog'lam uzildi", icon: UserMinus, tone: "text-ink-500 bg-ink-100" },
  ENROLLMENT_DROPPED: { label: "Guruhdan chiqarildi", icon: LogOut, tone: "text-danger-600 bg-danger-50" },
  GRADUATED: { label: "Kursni bitirdi", icon: GraduationCap, tone: "text-success-600 bg-success-50" },
};

export default function StudentDetailPage({ params }: { params: Promise<{ id: string }> }) {
  return (
    <Suspense fallback={null}>
      <StudentDetailPageContent params={params} />
    </Suspense>
  );
}

// UserDirectory/UserProfilePage tag their links with ?from=<origin> so the back link here can
// return to wherever the visitor actually started (Hisoblar/Xodimlar), not always "Studentlar".
const BACK_TARGET: Record<string, { href: string; label: string }> = {
  all: { href: "/accounts", label: "Hisoblar" },
  staff: { href: "/users", label: "Xodimlar" },
};

function StudentDetailPageContent({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const from = useSearchParams().get("from");
  const back = (from && BACK_TARGET[from]) || { href: "/students", label: "Studentlar" };
  // Operators may open Student 360° (students.view) but hold no homework.view grant — the tab would only
  // ever show a 403, so it isn't offered. The API still enforces this on its own.
  const { user } = useAuthSession();
  const { can, scopeOf } = usePermissions();
  const role = user?.role;
  const canSeeHomework = role !== "OPERATOR";
  // The payments tab and tile need payments.view. An Operator holds only finance.debt.view (the debt list on /finance), so the profile
  // used to ask the server for a page it would refuse — a 403 in the console for every profile an operator opened.
  const showFinanceTile = can("payments.view");

  const overviewQuery = useQuery({ queryKey: ["students", id, "overview"], queryFn: () => getStudentOverview(id) });
  const timelineQuery = useQuery({ queryKey: ["students", id, "timeline"], queryFn: () => getStudentTimeline(id) });
  const financeQuery = useQuery({
    queryKey: ["students", id, "finance"],
    queryFn: () => getStudentFinance(id),
    enabled: showFinanceTile,
  });

  if (overviewQuery.isLoading) {
    return (
      <main className="p-4 sm:p-6 lg:p-8">
        <Skeleton className="h-24 mb-6" />
        <div className="grid grid-cols-3 gap-4 mb-6">
          <Skeleton className="h-24" /><Skeleton className="h-24" /><Skeleton className="h-24" />
        </div>
      </main>
    );
  }

  if (overviewQuery.isError || !overviewQuery.data) {
    return (
      <main className="p-4 sm:p-6 lg:p-8">
        <Alert>{overviewQuery.error instanceof ApiError ? overviewQuery.error.message : "Student topilmadi."}</Alert>
      </main>
    );
  }

  const { student, enrollments, attendanceRate, homeworkCompletionRate, header } = overviewQuery.data;
  const [primaryGroup, ...otherGroups] = header.groups;
  // A person changes their own picture; an administrator that of someone in their reach. A teacher may open a student's profile
  // and holds users.edit too — but only for themselves, so the button is offered on the scope, not on the permission alone.
  const canChangePhoto = user?.id === student.userId || ["AllOrganization", "MyBranches"].includes(scopeOf("users.edit") ?? "");

  return (
    <main className="p-4 sm:p-6 lg:p-8">
      <Link href={back.href} className="mb-4 inline-flex items-center gap-1.5 text-[13px] text-ink-500 hover:text-ink-800">
        <ArrowLeft className="size-3.5" aria-hidden />
        {back.label}
      </Link>

      <div className="mb-6 flex flex-wrap items-start gap-4 rounded-xl border border-border bg-surface p-5">
        <Avatar
          name={student.fullName} className="size-16 text-lg"
          photo={header.hasPhoto && header.photoVersion ? { userId: student.userId, version: header.photoVersion } : null}
        />
        <div className="min-w-[220px] flex-1 space-y-0.5 text-[13px] text-ink-500">
          <div className="flex flex-wrap items-center gap-2.5">
            <h1 className="text-[18px] font-semibold text-ink-900">{student.fullName}</h1>
            <Badge tone={STATUS_TONE[student.status]} dot>{STATUS_LABEL[student.status]}</Badge>
          </div>
          <p className="break-words">
            {student.email} · {student.phone} · <span className="font-mono">{student.studentNumber}</span>
            {student.currentLevel && <> · Daraja: {student.currentLevel}</>}
            {" · "}Qo&apos;shilgan: {formatShortDate(student.joinedDate)}
          </p>
          {primaryGroup && (
            <p className="break-words">
              {primaryGroup.branchName} · {primaryGroup.courseName} · <span className="font-medium text-ink-700">{primaryGroup.groupName}</span>
              {otherGroups.length > 0 && <> · +{otherGroups.length} guruh</>}
            </p>
          )}
          {primaryGroup && (primaryGroup.mainTeacherNames.length > 0 || primaryGroup.supportTeacherNames.length > 0) && (
            <p className="break-words">
              {primaryGroup.mainTeacherNames.length > 0 && <>O&apos;qituvchi: {primaryGroup.mainTeacherNames.join(", ")}</>}
              {primaryGroup.supportTeacherNames.length > 0 && (
                <>{primaryGroup.mainTeacherNames.length > 0 && " · "}Yordamchi: {primaryGroup.supportTeacherNames.join(", ")}</>
              )}
            </p>
          )}
          {header.parents.length > 0 && (
            <p className="break-words">
              {header.parents.map((parent, i) => (
                <span key={i}>
                  {i > 0 && " · "}Ota-ona: {parent.fullName} ({RELATIONSHIP_LABEL[parent.relationship]}) {parent.phone}
                </span>
              ))}
            </p>
          )}
        </div>
        <div className="flex flex-wrap items-start justify-end gap-2">
          {canChangePhoto && <PhotoUpload userId={student.userId} hasPhoto={header.hasPhoto} refresh={["students", id, "overview"]} />}
          <StudentEditButton student={student} />
          {user?.id !== student.userId && <OpenChatButton userId={student.userId} />}
          <StudentComplaintButton studentId={id} studentName={student.fullName} />
          <StudentLifecycleActions studentId={id} status={student.status} enrollments={enrollments} />
        </div>
      </div>

      <div className="mb-6 grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <StatTile label="Davomat" value={`${attendanceRate}%`} icon={CalendarCheck} tone={attendanceRate < 70 ? "danger" : "brand"} />
        <StatTile label="Homework bajarilishi" value={`${homeworkCompletionRate}%`} icon={ClipboardList} tone={homeworkCompletionRate < 60 ? "warning" : "brand"} />
        <StatTile label="Guruhlar" value={enrollments.length} icon={UsersRound} />
        {showFinanceTile && financeQuery.data && (
          <StatTile
            label="Balans"
            value={formatMoney(financeQuery.data.totalBalance)}
            icon={Wallet}
            tone={financeQuery.data.overdueAmount > 0 ? "danger" : financeQuery.data.totalBalance > 0 ? "warning" : "brand"}
          />
        )}
      </div>

      <Tabs
        tabs={[
          {
            id: "overview",
            label: "Umumiy",
            content: (
              <div className="rounded-xl border border-border bg-surface">
                {enrollments.length === 0 ? (
                  <EmptyState icon={TrendingUp} title="Guruhga yozilmagan" />
                ) : (
                  <div className="divide-y divide-border">
                    {enrollments.map((e) => (
                      <div key={e.id} className="flex items-center justify-between px-5 py-3.5">
                        <div>
                          <p className="text-[13.5px] font-medium text-ink-900">{e.groupName}</p>
                          <p className="text-[12.5px] text-ink-500">
                            {formatShortDate(e.enrolledAt)} sanasida yozildi
                          </p>
                        </div>
                        <div className="flex items-center gap-2">
                          <Badge tone={e.status === "Active" ? "success" : "neutral"}>{e.status}</Badge>
                          {e.status === "Active" && can("student.enroll") && (
                            <DropEnrollmentButton studentId={id} enrollment={e} />
                          )}
                        </div>
                      </div>
                    ))}
                  </div>
                )}
                <StudentParents studentId={id} />
              </div>
            ),
          },
          ...(can("students.view")
            ? [{ id: "learning", label: "Learning", content: <StudentLearningProfile studentId={id} /> }]
            : []),
          {
            id: "attendance",
            label: "Davomat",
            content: <StudentAttendanceTab studentId={id} />,
          },
          ...(canSeeHomework
            ? [{ id: "homework", label: "Homework", content: <StudentHomeworkTab studentId={id} /> }]
            : []),
          {
            id: "exams",
            label: "Imtihonlar",
            content: <StudentExamsTab studentId={id} />,
          },
          // Staff read a student's essays through the reviewers' list (ai.writing.review); a student reads their own (ai.writing.use).
          ...(can("ai.writing.review") || can("ai.writing.use")
            ? [{ id: "writing", label: "Writing", content: <StudentWritingTab studentId={id} staffView={can("ai.writing.review")} /> }]
            : []),
          // Finished Speaking practice the student knew was shared (ai.speaking.review); the server keeps everything else out of reach.
          ...(can("ai.speaking.review")
            ? [{ id: "speaking", label: "Speaking", content: <StudentSpeakingTab studentId={id} /> }]
            : []),
          ...(showFinanceTile
            ? [{ id: "finance", label: "To'lovlar", content: <StudentFinanceTab studentId={id} studentName={student.fullName} groups={enrollments.filter((e) => e.status === "Active")} /> }]
            : []),
          ...(canViewSupport(role)
            ? [{ id: "support", label: "Support", content: <StudentSupportTab studentId={id} groups={enrollments.filter((e) => e.status === "Active")} /> }]
            : []),
          ...(canViewStudentDocuments(role)
            ? [{ id: "documents", label: "Hujjatlar", content: <StudentDocumentsTab studentId={id} canManage={canManageStudentDocuments(role)} /> }]
            : []),
          ...(user?.id !== student.userId
            ? [{
                id: "communication",
                label: "Muloqot",
                content: (
                  <div className="rounded-xl border border-border bg-surface">
                    <EmptyState
                      icon={MessageCircle}
                      title="To'g'ridan-to'g'ri xabar yozing"
                      description={`${student.fullName} bilan suhbatni shu yerdan boshlashingiz mumkin.`}
                      action={<OpenChatButton userId={student.userId} label="Suhbatni ochish" />}
                    />
                  </div>
                ),
              }]
            : []),
          {
            id: "timeline",
            label: "Tarix",
            content: (
              <div className="rounded-xl border border-border bg-surface p-5">
                {timelineQuery.isLoading && <Skeleton className="h-40" />}
                {timelineQuery.data && timelineQuery.data.length === 0 && (
                  <EmptyState icon={TrendingUp} title="Hali tarix yo'q" />
                )}
                {timelineQuery.data && timelineQuery.data.length > 0 && (
                  <ol className="space-y-0">
                    {timelineQuery.data.map((event, i) => {
                      const config = EVENT_CONFIG[event.eventType] ?? { label: event.eventType, icon: FileText, tone: "text-ink-500 bg-ink-100" };
                      const Icon = config.icon;
                      const isLast = i === timelineQuery.data!.length - 1;
                      return (
                        <li key={event.id} className="relative flex gap-3.5 pb-6 last:pb-0">
                          {!isLast && <span className="absolute left-[15px] top-8 h-full w-px bg-border" aria-hidden />}
                          <span className={cn("relative flex size-8 shrink-0 items-center justify-center rounded-full", config.tone)}>
                            <Icon className="size-3.5" aria-hidden />
                          </span>
                          <div className="min-w-0 flex-1 pt-1">
                            <p className="text-[13.5px] font-medium text-ink-900">{config.label}</p>
                            <p className="text-[12px] text-ink-500">{formatDateTime(event.occurredAt)}</p>
                          </div>
                        </li>
                      );
                    })}
                  </ol>
                )}
              </div>
            ),
          },
        ]}
      />
    </main>
  );
}
