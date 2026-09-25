import { apiFetch } from "../api-client";
import type { PageEnvelope } from "@/components/ui/Pagination";
import { buildQuery, DEFAULT_PAGE_SIZE, type PageParams } from "./query";

/** The road a complaint travels, in order: it only ever moves forward. */
export type ComplaintStatus = "New" | "Assigned" | "InProgress" | "Resolved" | "Closed";

export const COMPLAINT_STATUS_LABEL: Record<ComplaintStatus, string> = {
  New: "Yangi",
  Assigned: "Biriktirilgan",
  InProgress: "Jarayonda",
  Resolved: "Yechilgan",
  Closed: "Yopilgan",
};

export interface Complaint {
  id: string;
  branchId: string;
  branchName: string;
  studentId: string | null;
  studentName: string | null;
  authorUserId: string;
  authorName: string;
  subject: string;
  body: string;
  status: ComplaintStatus;
  assignedToUserId: string | null;
  assignedToName: string | null;
  resolution: string | null;
  createdAt: string;
  resolvedAt: string | null;
  /** The author's verdict on the outcome, 0–10. */
  satisfactionScore: number | null;
  feedbackComment: string | null;
  /** The signed-in person wrote it. */
  isMine: boolean;
  /** They run its branch: may hand it out and close it. */
  canManage: boolean;
  /** They may start it and write its outcome: whoever runs the branch, or the person it was handed to. */
  canAct: boolean;
  /** They wrote it and it is resolved: their verdict is what is missing. */
  canRate: boolean;
}

export interface ComplaintCandidate {
  userId: string;
  fullName: string;
  email: string;
}

export interface ListComplaintsQuery extends PageParams {
  /** Several at once: a screen tab such as "closed" is resolved + closed. */
  statuses?: ComplaintStatus[];
}

export interface CreateComplaintInput {
  subject: string;
  body: string;
  /** Whom it is about. A student's own is about themselves without saying so; a parent names the child when they have several. */
  studentId?: string;
  /** Which branch, when it cannot be told from the student. */
  branchId?: string;
}

/** The complaints the caller may see, newest first. Someone who only files them sees only their own. */
export function listComplaints(query: ListComplaintsQuery = {}): Promise<PageEnvelope<Complaint>> {
  return apiFetch<PageEnvelope<Complaint>>(`/api/complaints${buildQuery({
    statuses: query.statuses, search: query.search?.trim(),
    page: query.page ?? 1, size: query.size ?? DEFAULT_PAGE_SIZE,
  })}`);
}

export function getComplaint(id: string): Promise<Complaint> {
  return apiFetch<Complaint>(`/api/complaints/${id}`);
}

export function createComplaint(input: CreateComplaintInput): Promise<Complaint> {
  return apiFetch<Complaint>("/api/complaints", { method: "POST", body: JSON.stringify(input) });
}

export function listComplaintCandidates(id: string): Promise<ComplaintCandidate[]> {
  return apiFetch<ComplaintCandidate[]>(`/api/complaints/${id}/candidates`);
}

export function assignComplaint(id: string, userId: string): Promise<void> {
  return apiFetch<void>(`/api/complaints/${id}/assign`, { method: "POST", body: JSON.stringify({ userId }) });
}

/** Start it (`InProgress`), or close a resolved one whose author never rated it (`Closed`). Settling is `resolveComplaint`. */
export function setComplaintStatus(id: string, status: "InProgress" | "Closed"): Promise<void> {
  return apiFetch<void>(`/api/complaints/${id}/status`, { method: "POST", body: JSON.stringify({ status }) });
}

export function resolveComplaint(id: string, resolution: string): Promise<void> {
  return apiFetch<void>(`/api/complaints/${id}/resolve`, { method: "POST", body: JSON.stringify({ resolution }) });
}

/** The author's rating of the outcome, 0–10; it closes the complaint. */
export function rateComplaint(id: string, score: number, comment?: string): Promise<void> {
  return apiFetch<void>(`/api/complaints/${id}/feedback`, { method: "POST", body: JSON.stringify({ score, comment }) });
}
