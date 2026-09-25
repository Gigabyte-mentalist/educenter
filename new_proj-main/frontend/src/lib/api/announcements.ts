import { apiFetch } from "../api-client";
import type { PageEnvelope } from "@/components/ui/Pagination";
import { buildQuery, DEFAULT_PAGE_SIZE, type PageParams } from "./query";

/** Who an announcement is for. */
export type AnnouncementAudience = "All" | "Students" | "Parents" | "Staff";

export const AUDIENCE_LABEL: Record<AnnouncementAudience, string> = {
  All: "Hamma", Students: "Studentlar", Parents: "Ota-onalar", Staff: "Xodimlar",
};

export interface Announcement {
  id: string;
  title: string;
  body: string;
  audience: AnnouncementAudience;
  /** Empty together with `groupId`: the whole organization. A group's announcement carries its branch too. */
  branchId: string | null;
  branchName: string | null;
  groupId: string | null;
  groupName: string | null;
  authorId: string;
  authorName: string;
  publishedAt: string;
  expiresAt: string | null;
  /** Whether the signed-in person may edit or delete this one — the buttons are drawn from it. */
  canManage: boolean;
}

export interface CreateAnnouncementInput {
  title: string;
  body: string;
  audience: AnnouncementAudience;
  branchId?: string;
  groupId?: string;
  expiresAt?: string;
}

/** What was sent changes; the rest stays. Where it applies cannot be changed — delete and publish again. */
export interface UpdateAnnouncementInput {
  title?: string;
  body?: string;
  audience?: AnnouncementAudience;
  expiresAt?: string;
  clearExpiry?: boolean;
}

/** The live announcements that reach the caller, newest first. */
export function listAnnouncements(query: PageParams = {}): Promise<PageEnvelope<Announcement>> {
  return apiFetch<PageEnvelope<Announcement>>(`/api/announcements${buildQuery({ page: query.page ?? 1, size: query.size ?? DEFAULT_PAGE_SIZE })}`);
}

export function createAnnouncement(input: CreateAnnouncementInput): Promise<Announcement> {
  return apiFetch<Announcement>("/api/announcements", { method: "POST", body: JSON.stringify(input) });
}

export function updateAnnouncement(id: string, input: UpdateAnnouncementInput): Promise<Announcement> {
  return apiFetch<Announcement>(`/api/announcements/${id}`, { method: "PATCH", body: JSON.stringify(input) });
}

export function deleteAnnouncement(id: string): Promise<void> {
  return apiFetch<void>(`/api/announcements/${id}`, { method: "DELETE" });
}

/** "Butun markaz", "Chilonzor filiali", "IELTS-21 guruhi" — where an announcement applies, as a person says it. */
export function announcementPlace(a: Pick<Announcement, "branchName" | "groupName" | "groupId" | "branchId">): string {
  if (a.groupId) return a.groupName ? `${a.groupName} guruhi` : "Guruh";
  if (a.branchId) return a.branchName ?? "Filial";
  return "Butun markaz";
}
