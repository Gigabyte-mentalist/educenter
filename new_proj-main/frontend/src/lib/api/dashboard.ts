import { apiFetch } from "../api-client";
import type { FunnelStage } from "./crm";

export type StudentStatus = "Active" | "Debt" | "Frozen" | "Transferred" | "Graduated" | "Dropped";

export interface DashboardLesson {
  id: string;
  groupId: string;
  groupName: string;
  branchName: string;
  roomName: string;
  startsAt: string;
  endsAt: string;
  topic: string | null;
  teacherId: string;
  teacherName: string;
}

export interface PendingHomework {
  id: string;
  lessonId: string;
  title: string;
  groupName: string;
  deadline: string;
  overdue: boolean;
}

export interface AiResult {
  kind: "writing" | "speaking";
  title: string;
  band: number | null;
  status: string;
  at: string;
  link: string;
}

export interface MoneyDue {
  outstanding: number;
  nextDueDate: string | null;
  overdue: boolean;
}

export interface BranchKpi {
  branchId: string;
  branchName: string;
  students: number;
  activeGroups: number;
  lessonsToday: number;
  collected: number;
  outstanding: number;
  debtors: number;
  attendanceRate: number;
  newLeads: number;
  convertedLeads: number;
  conversionRate: number;
}

export interface OrganizationDashboard {
  from: string;
  to: string;
  totalStudents: number;
  activeStudents: number;
  frozenStudents: number;
  debtStudents: number;
  newStudents: number;
  activeGroups: number;
  lessonsToday: number;
  teacherCount: number;
  collected: number;
  expectedRevenue: number;
  outstanding: number;
  debtors: number;
  newLeads: number;
  trialsBooked: number;
  trialsAttended: number;
  convertedLeads: number;
  trialConversionRate: number;
  leadConversionRate: number;
  attendanceRate: number;
  retentionRate: number;
  openSupportCases: number;
  pendingHomeworkReviews: number;
  branches: BranchKpi[];
  refundedAmount: number;
  discountAmount: number;
  /** AI use in the period — null for a caller who may not read the AI usage report (`ai.usage.view`). */
  aiRequests: number | null;
  aiCostUsd: number | null;
  aiActiveStudents: number | null;
}

// ── One branch's own dashboard (S05.03) ─────────────────────────────────────────────────────────────

/** Bands are 0–9, Grammar and Vocabulary percentages 0–100; each is null when nothing was graded for it. `attendanceRate` is a 0–1 fraction. */
export interface AcademicBlock {
  listening: number | null; reading: number | null; writing: number | null; speaking: number | null; overall: number | null;
  grammar: number | null; vocabulary: number | null; results: number;
  trend: { month: string; average: number; results: number }[];
  lessonsHeld: number; lessonsCancelled: number; attendanceRate: number;
}

export interface BranchTeacherRow {
  teacherId: string; fullName: string; groups: number; lessonsHeld: number; attended: number; counted: number; attendanceRate: number;
  averageReviewHours: number | null; homeworkPendingReview: number;
  retentionBase: number; retained: number; retentionRate: number | null;
  openCases: number; closedCases: number; rank: number | null;
}

export interface SourceFunnel {
  source: string; leads: number; contacted: number; trialAttended: number; converted: number; lost: number; conversionRate: number;
}

/** The leads that came in during the period, followed to wherever they have got to. Rates are 0–1 fractions. */
export interface LeadFunnel {
  leads: number; contacted: number; trialBooked: number; trialAttended: number; converted: number; lost: number;
  trialToStudent: number; trialToPayment: number;
  leadConversionRate: number; trialToStudentRate: number; trialToPaymentRate: number;
  sources: SourceFunnel[];
  /** The same six steps as the leads page's funnel — both come from one server query. */
  stages: FunnelStage[];
}

export interface BranchDashboard {
  branchId: string; branchName: string; address: string; timezone: string; from: string; to: string;
  overview: OrganizationDashboard;
  /** False for a caller who may not read payments: the money in `overview` is then zero and the Finance tab is not shown. */
  financeVisible: boolean;
  academic: AcademicBlock;
  /** Null for a caller without `dashboard.teachers.view`. */
  teachers: { teachers: BranchTeacherRow[] } | null;
  crm: LeadFunnel;
}

export const fetchBranchDashboard = (branchId: string, query: { from?: string; to?: string } = {}) => {
  const params = new URLSearchParams();
  if (query.from) params.set("from", query.from);
  if (query.to) params.set("to", query.to);
  const qs = params.toString();
  return apiFetch<BranchDashboard>(`/api/dashboard/branch/${branchId}${qs ? `?${qs}` : ""}`);
};

export interface RiskStudent {
  studentId: string;
  fullName: string;
  groupName: string;
  reason: string;
  severity: number;
}

export interface TeacherDashboard {
  today: DashboardLesson[];
  tomorrow: DashboardLesson[];
  myGroups: number;
  myStudents: number;
  pendingHomeworkReviews: number;
  pendingWritingReviews: number;
  /** Finished Speaking practice of the teacher's students that nobody has corrected yet. */
  pendingSpeakingReviews: number;
  unmarkedLessons: number;
  attendanceRate: number;
  risks: RiskStudent[];
  unreadNotifications: number;
}

export interface StudentDashboard {
  studentId: string;
  fullName: string;
  currentLevel: string | null;
  status: StudentStatus;
  today: DashboardLesson[];
  upcoming: DashboardLesson[];
  pendingHomework: PendingHomework[];
  attendanceRate: number;
  lessonsAttended: number;
  lessonsHeld: number;
  money: MoneyDue;
  recentAi: AiResult[];
  lastExamOverall: number | null;
  lastExamName: string | null;
  lastExamDate: string | null;
}

export interface ParentChild {
  studentId: string;
  fullName: string;
  currentLevel: string | null;
  status: StudentStatus;
  today: DashboardLesson[];
  nextLesson: DashboardLesson | null;
  attendanceRate: number;
  lessonsAttended: number;
  lessonsHeld: number;
  pendingHomework: number;
  money: MoneyDue;
  lastExamOverall: number | null;
  lastExamName: string | null;
  lastExamDate: string | null;
}

export interface ParentDashboard {
  children: ParentChild[];
}

export interface AiProviderHealth {
  provider: string;
  requests: number;
  failures: number;
  errorRate: number;
  p95LatencyMs: number;
  costUsd: number;
}

export interface AutomationRun {
  ruleCode: string;
  trigger: string;
  itemCount: number;
  summary: string;
  ranAt: string;
}

export interface SystemDashboard {
  databaseReachable: boolean;
  databaseLatencyMs: number;
  databaseVersion: string | null;
  databaseSizeBytes: number | null;
  databaseError: string | null;
  apiVersion: string;
  environment: string;
  uptime: string;
  aiRequests24h: number;
  aiFailures24h: number;
  aiErrorRate24h: number;
  aiCost24h: number;
  aiP95LatencyMs: number;
  providers: AiProviderHealth[];
  queuedWritingJobs: number;
  processingWritingJobs: number;
  failedWritingJobs24h: number;
  recentAutomationRuns: AutomationRun[];
  auditEvents24h: number;
  activeSessions: number;
  tables: { name: string; rows: number }[];
}

export function fetchOrganizationDashboard(query: { from?: string; to?: string; branchId?: string } = {}) {
  const params = new URLSearchParams();
  if (query.from) params.set("from", query.from);
  if (query.to) params.set("to", query.to);
  if (query.branchId) params.set("branchId", query.branchId);
  const qs = params.toString();
  return apiFetch<OrganizationDashboard>(`/api/dashboard/organization${qs ? `?${qs}` : ""}`);
}

export const fetchTeacherDashboard = () => apiFetch<TeacherDashboard>("/api/dashboard/teacher");
export const fetchStudentDashboard = () => apiFetch<StudentDashboard>("/api/dashboard/student");
export const fetchParentDashboard = () => apiFetch<ParentDashboard>("/api/dashboard/parent");
export const fetchSystemDashboard = () => apiFetch<SystemDashboard>("/api/system/health");
