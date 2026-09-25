import { apiFetch } from "../api-client";
import type { PageEnvelope } from "@/components/ui/Pagination";
import { buildQuery, DEFAULT_PAGE_SIZE, type PageParams } from "./query";

export type LeadStatus =
  | "Lead" | "Contacted" | "TrialBooked" | "TrialAttended" | "TrialMissed"
  | "NotInterested" | "Lost" | "Converted";

export interface Lead {
  id: string;
  branchId: string;
  fullName: string;
  phone: string;
  source: string;
  interestedCourse: string;
  operatorId: string;
  status: LeadStatus;
  notes: string | null;
  lostReason: string | null;
  convertedStudentId: string | null;
  createdAt: string;
  /** Raised to High by the trial follow-up rule (TZ §10). */
  priority: "Normal" | "High";
  /** The most recent trial lesson booked for the lead, or null — lets the board show it and mark attendance on the card. */
  latestTrial: LeadTrial | null;
  /** Comma-separated {@link PreferredTimeCode}s ("Evening,Weekend"), or null = any time. */
  preferredTime: string | null;
  /** The level the operator recorded ("B1"); a placement test only suggests one. */
  currentLevel: string | null;
}

/** When a lead can attend — what the group recommendation matches against the groups' real lessons. */
export type PreferredTimeCode = "Morning" | "Afternoon" | "Evening" | "Weekend";

export const PREFERRED_TIME_OPTIONS: { code: PreferredTimeCode; label: string; hint: string }[] = [
  { code: "Morning", label: "Ertalab", hint: "8:00–12:00" },
  { code: "Afternoon", label: "Tushdan keyin", hint: "12:00–17:00" },
  { code: "Evening", label: "Kechqurun", hint: "17:00–21:00" },
  { code: "Weekend", label: "Dam olish kunlari", hint: "shanba, yakshanba" },
];

export function parsePreferredTime(value: string | null): PreferredTimeCode[] {
  const codes = (value ?? "").split(",").map((c) => c.trim());
  return PREFERRED_TIME_OPTIONS.map((o) => o.code).filter((c) => codes.includes(c));
}

export function preferredTimeLabel(value: string | null): string {
  const picked = parsePreferredTime(value);
  return picked.length === 0 ? "Farqi yo'q" : PREFERRED_TIME_OPTIONS.filter((o) => picked.includes(o.code)).map((o) => o.label).join(", ");
}

export interface LeadTrial {
  id: string;
  groupId: string;
  groupName: string;
  scheduledAt: string;
  attendanceStatus: "Scheduled" | "Attended" | "Missed";
}

export interface CreateLeadInput {
  branchId: string;
  fullName: string;
  phone: string;
  source: string;
  interestedCourse: string;
  preferredTime?: string;
  currentLevel?: string;
}

/** A field left out stays as it is; an empty string clears it. */
export function updateLeadPreferences(leadId: string, input: { preferredTime?: string; currentLevel?: string }): Promise<Lead> {
  return apiFetch<Lead>(`/api/leads/${leadId}`, { method: "PATCH", body: JSON.stringify(input) });
}

export type CallOutcome =
  | "Answered" | "NoAnswer" | "CallLater" | "Interested" | "NotInterested"
  | "PaymentPromised" | "TrialBooked" | "WrongNumber" | "Transferred" | "Lost";

export interface CrmCall {
  id: string;
  leadId: string;
  outcome: CallOutcome;
  notes: string | null;
  calledAt: string;
}

export const CALL_OUTCOME_LABEL: Record<CallOutcome, string> = {
  Answered: "Javob berdi", NoAnswer: "Javob bermadi", CallLater: "Keyinroq qo'ng'iroq qilish",
  Interested: "Qiziqdi", NotInterested: "Qiziqmadi", PaymentPromised: "To'lovga va'da berdi",
  TrialBooked: "Trial band qildi", WrongNumber: "Noto'g'ri raqam", Transferred: "Yo'naltirildi", Lost: "Yo'qotildi",
};

export interface ListLeadsQuery extends PageParams {
  /** One board column can hold several statuses (Yopilgan = trial missed, not interested, lost). */
  statuses?: LeadStatus[];
  source?: string;
}

/** One page of leads — the board asks for each column separately instead of every lead at once. */
export function listLeads(query: ListLeadsQuery = {}): Promise<PageEnvelope<Lead>> {
  return apiFetch<PageEnvelope<Lead>>(`/api/leads${buildQuery({
    statuses: query.statuses, source: query.source, search: query.search?.trim(),
    page: query.page ?? 1, size: query.size ?? DEFAULT_PAGE_SIZE,
  })}`);
}

/** One step of the marketing funnel; both rates are 0–1 fractions. */
export interface FunnelStage {
  key: "leads" | "contacted" | "trialBooked" | "trialAttended" | "student" | "paid";
  label: string;
  count: number;
  /** Over the step before. Can pass 1 for "student": a lead may enrol without a trial. */
  conversionFromPrevious: number;
  /** Over every lead of the cohort — never above 1. */
  shareOfLeads: number;
}

/** The leads that came in during the period, followed to their furthest step — overall and per source. */
export interface MarketingFunnel {
  stages: FunnelStage[];
  bySource: { source: string; stages: FunnelStage[] }[];
}

/** "Ko'rsatilmagan" is what the server calls a lead with no source. */
export const UNKNOWN_SOURCE = "Ko'rsatilmagan";

export function getLeadFunnel(query: { from?: string; to?: string; branchId?: string; source?: string } = {}): Promise<MarketingFunnel> {
  return apiFetch<MarketingFunnel>(`/api/leads/funnel${buildQuery({
    from: query.from, to: query.to, branchId: query.branchId, source: query.source,
  })}`);
}

export function getLead(id: string): Promise<Lead> {
  return apiFetch<Lead>(`/api/leads/${id}`);
}

export function createLead(input: CreateLeadInput): Promise<Lead> {
  return apiFetch<Lead>("/api/leads", { method: "POST", body: JSON.stringify(input) });
}

/** Manual pipeline move (kanban drag, or the status picker on the Lead 360° page). The server rejects
 * `Converted` here — that state is only ever set by `convertLead`. */
export function updateLeadStatus(leadId: string, status: LeadStatus, reason?: string): Promise<void> {
  return apiFetch<void>(`/api/leads/${leadId}/status`, { method: "PATCH", body: JSON.stringify({ status, reason }) });
}

export function logCall(leadId: string, outcome: CallOutcome, notes?: string): Promise<void> {
  return apiFetch<void>(`/api/leads/${leadId}/calls`, { method: "POST", body: JSON.stringify({ outcome, notes }) });
}

export function listCalls(leadId: string): Promise<CrmCall[]> {
  return apiFetch<CrmCall[]>(`/api/leads/${leadId}/calls`);
}

export function addLeadNote(leadId: string, note: string): Promise<Lead> {
  return apiFetch<Lead>(`/api/leads/${leadId}/notes`, { method: "POST", body: JSON.stringify({ note }) });
}

export interface ConvertLeadInput {
  groupId: string;
  email: string;
  password: string;
  currentLevel?: string;
}

export interface ConvertedStudent {
  id: string;
  userId: string;
  studentNumber: string;
  joinedDate: string;
  currentLevel: string | null;
  status: string;
}

export function convertLead(leadId: string, input: ConvertLeadInput): Promise<ConvertedStudent> {
  return apiFetch<ConvertedStudent>(`/api/leads/${leadId}/convert`, { method: "POST", body: JSON.stringify(input) });
}

export type CrmTaskType = "FollowUp" | "Debt" | "Absence" | "CallBack";

export interface TrialLesson {
  id: string;
  leadId: string;
  groupId: string;
  scheduledAt: string;
  attendanceStatus: string;
}

export function listTrialLessons(leadId: string): Promise<TrialLesson[]> {
  return apiFetch<TrialLesson[]>(`/api/trial-lessons?leadId=${encodeURIComponent(leadId)}`);
}

export function bookTrial(leadId: string, groupId: string, scheduledAt: string): Promise<TrialLesson> {
  return apiFetch<TrialLesson>(`/api/leads/${leadId}/trial`, {
    method: "POST", body: JSON.stringify({ groupId, scheduledAt }),
  });
}

export function markTrialAttendance(id: string, attended: boolean): Promise<void> {
  return apiFetch<void>(`/api/trial-lessons/${id}/attendance`, {
    method: "PATCH", body: JSON.stringify({ attended }),
  });
}
export type CrmTaskStatus = "Open" | "Done" | "Cancelled";

export interface CrmTask {
  id: string;
  leadId: string | null;
  studentId: string | null;
  assignedTo: string;
  type: CrmTaskType;
  status: CrmTaskStatus;
  dueAt: string;
  /** Set on tasks created by an automation rule, e.g. TRIAL_FOLLOW_UP, CONSECUTIVE_ABSENCES. */
  reasonCode: string | null;
  note: string | null;
  subjectName: string | null;
  subjectPhone: string | null;
}

export type CrmTaskBucket = "overdue" | "upcoming";

/** The queue is two lists with their own counts: what is late and what is still to come. */
export function listCrmTasks(query: PageParams & { bucket?: CrmTaskBucket; includeCompleted?: boolean } = {}): Promise<PageEnvelope<CrmTask>> {
  return apiFetch<PageEnvelope<CrmTask>>(`/api/crm/tasks${buildQuery({
    bucket: query.bucket, includeCompleted: query.includeCompleted, page: query.page ?? 1, size: query.size ?? DEFAULT_PAGE_SIZE,
  })}`);
}

export function completeCrmTask(taskId: string): Promise<void> {
  return apiFetch<void>(`/api/crm/tasks/${taskId}/complete`, { method: "POST" });
}

/** docs/00_TZ.md §12 Group Recommendation Engine — ranked, scored purely from data the domain tracks. */
export interface GroupRecommendation {
  groupId: string;
  groupName: string;
  courseId: string;
  courseName: string;
  freeSeats: number;
  capacity: number;
  matchPercent: number;
  reasons: string[];
  /** The group's main teacher. */
  teacherName: string | null;
  /** Its next lessons — a trial is a visit to one of these. */
  nextLessons: { id: string; startsAt: string; endsAt: string; roomName: string }[];
}

export function getGroupRecommendations(leadId: string): Promise<GroupRecommendation[]> {
  return apiFetch<GroupRecommendation[]>(`/api/leads/${leadId}/group-recommendations`);
}

/** docs/00_TZ.md §13 Placement Test — a fixed quiz an operator administers to a lead before enrollment. */
export interface PlacementQuestion {
  id: number;
  text: string;
  options: string[];
}

export function getPlacementQuestions(): Promise<PlacementQuestion[]> {
  return apiFetch<PlacementQuestion[]>("/api/placement-test/questions");
}

export interface PlacementAttempt {
  id: string;
  leadId: string;
  correctCount: number;
  totalQuestions: number;
  recommendedLevel: string;
  completedAt: string;
}

export function listPlacementAttempts(leadId: string): Promise<PlacementAttempt[]> {
  return apiFetch<PlacementAttempt[]>(`/api/leads/${leadId}/placement-test`);
}

export function submitPlacementTest(leadId: string, answers: { questionId: number; selectedIndex: number }[]): Promise<PlacementAttempt> {
  return apiFetch<PlacementAttempt>(`/api/leads/${leadId}/placement-test`, { method: "POST", body: JSON.stringify({ answers }) });
}
