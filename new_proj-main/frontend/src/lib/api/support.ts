import { apiFetch } from "../api-client";
import type { PageEnvelope } from "@/components/ui/Pagination";
import { buildQuery, DEFAULT_PAGE_SIZE, type PageParams } from "./query";

export type SupportCaseStatus = "Recommended" | "Assigned" | "Dismissed" | "Resolved";
export type SupportCaseSource = "System" | "Staff";

export interface SupportCase {
  id: string;
  studentId: string;
  studentName: string;
  studentNumber: string;
  groupId: string;
  groupName: string;
  source: SupportCaseSource;
  signalCode: string;
  reason: string;
  status: SupportCaseStatus;
  proposedBy: string | null;
  proposedByName: string | null;
  supportTeacherId: string | null;
  supportTeacherName: string | null;
  resolutionNote: string | null;
  createdAt: string;
  assignedAt: string | null;
  closedAt: string | null;
}

export interface SupportCandidate {
  userId: string;
  fullName: string;
  email: string;
}

export const SIGNAL_LABEL: Record<string, string> = {
  CONSECUTIVE_ABSENCES: "Ketma-ket yo'qlik",
  HOMEWORK_MISSED: "Homework topshirilmayapti",
  STAFF_PROPOSAL: "Xodim tavsiyasi",
  WRITING_GRAMMAR_WEAK: "Writing: grammatika zaif",
  LISTENING_LOW: "Listening: past natijalar",
  READING_TIME: "Reading: vaqtni boshqarish",
  SPEAKING_FLUENCY_LOW: "Speaking: fluency past",
};

export interface ListSupportCasesQuery extends PageParams {
  /** Several at once: a screen tab such as "closed" is dismissed + resolved. */
  statuses?: SupportCaseStatus[];
  studentId?: string;
}

export function listSupportCases(query: ListSupportCasesQuery = {}): Promise<PageEnvelope<SupportCase>> {
  return apiFetch<PageEnvelope<SupportCase>>(`/api/support-cases${buildQuery({
    statuses: query.statuses, studentId: query.studentId, search: query.search?.trim(),
    page: query.page ?? 1, size: query.size ?? DEFAULT_PAGE_SIZE,
  })}`);
}

export function getSupportCase(id: string): Promise<SupportCase> {
  return apiFetch<SupportCase>(`/api/support-cases/${id}`);
}

export function proposeSupport(input: { studentId: string; groupId: string; reason: string }): Promise<SupportCase> {
  return apiFetch<SupportCase>("/api/support-cases", { method: "POST", body: JSON.stringify(input) });
}

export function listSupportCandidates(caseId: string): Promise<SupportCandidate[]> {
  return apiFetch<SupportCandidate[]>(`/api/support-cases/${caseId}/candidates`);
}

export function assignSupport(caseId: string, supportTeacherId: string): Promise<void> {
  return apiFetch<void>(`/api/support-cases/${caseId}/assign`, { method: "POST", body: JSON.stringify({ supportTeacherId }) });
}

export function dismissSupport(caseId: string, note?: string): Promise<void> {
  return apiFetch<void>(`/api/support-cases/${caseId}/dismiss`, { method: "POST", body: JSON.stringify({ note }) });
}

export function resolveSupport(caseId: string, note?: string): Promise<void> {
  return apiFetch<void>(`/api/support-cases/${caseId}/resolve`, { method: "POST", body: JSON.stringify({ note }) });
}
