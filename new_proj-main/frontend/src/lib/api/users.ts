import { apiFetch } from "../api-client";
import type { PageEnvelope } from "@/components/ui/Pagination";

export interface AppUser {
  id: string;
  fullName: string;
  email: string;
  phone: string;
  status: "Active" | "Inactive" | "Suspended";
  roles: string[];
  branchIds: string[];
}

export interface CreateUserInput {
  fullName: string;
  email: string;
  phone: string;
  password: string;
  roleCode: string;
  branchIds: string[];
}

/** Staff work at the school; learners are its customers. The two lists are separate screens. */
export type UserAudience = "staff" | "learners" | "all";

export interface ListUsersQuery {
  role?: string;
  branchId?: string;
  search?: string;
  audience?: UserAudience;
  status?: AppUser["status"];
  page?: number;
  size?: number;
}

export function listUsers(query: ListUsersQuery = {}): Promise<PageEnvelope<AppUser>> {
  const params = new URLSearchParams();
  if (query.role) params.set("role", query.role);
  if (query.branchId) params.set("branchId", query.branchId);
  if (query.search?.trim()) params.set("search", query.search.trim());
  if (query.audience && query.audience !== "all") params.set("audience", query.audience);
  if (query.status) params.set("status", query.status);
  params.set("page", String(query.page ?? 1));
  params.set("size", String(query.size ?? 25));
  return apiFetch<PageEnvelope<AppUser>>(`/api/users?${params}`);
}

export interface UserProfile {
  id: string;
  fullName: string;
  email: string;
  phone: string;
  status: AppUser["status"];
  roles: string[];
  branches: { id: string; name: string }[];
  groups: { id: string; name: string; role: string }[];
  studentId: string | null;
  parentId: string | null;
  createdAt: string;
  updatedAt: string | null;
}

export function getUserProfile(id: string): Promise<UserProfile> {
  return apiFetch<UserProfile>(`/api/users/${id}`);
}

export function createUser(input: CreateUserInput): Promise<AppUser> {
  return apiFetch<AppUser>("/api/users", {
    method: "POST",
    body: JSON.stringify(input),
  });
}

export function updateUser(id: string, input: { fullName: string; phone: string }): Promise<void> {
  return apiFetch<void>(`/api/users/${id}`, { method: "PATCH", body: JSON.stringify(input) });
}

/** Blocks the account and ends its sessions; everything they are attached to stays readable. */
export function deactivateUser(id: string): Promise<void> {
  return apiFetch<void>(`/api/users/${id}/deactivate`, { method: "POST" });
}

export function activateUser(id: string): Promise<void> {
  return apiFetch<void>(`/api/users/${id}/activate`, { method: "POST" });
}

/** An administrator's reset: the server makes a temporary password (or takes the one given), ends every session of
 * the account and requires a change at the next sign-in. The password comes back in this response only. */
export function resetUserPassword(id: string, newPassword?: string): Promise<{ temporaryPassword: string }> {
  return apiFetch<{ temporaryPassword: string }>(`/api/users/${id}/reset-password`, {
    method: "POST",
    body: JSON.stringify(newPassword ? { newPassword } : {}),
  });
}

/** Erases the account. Refused with a 409 and an explanation when anything still references it. */
export function deleteUser(id: string): Promise<void> {
  return apiFetch<void>(`/api/users/${id}`, { method: "DELETE" });
}

export const ROLE_CODES = [
  "SUPER_ADMIN",
  "ADMINISTRATOR",
  "OPERATOR",
  "TEACHER",
  "SUPPORT_TEACHER",
  "STUDENT",
  "PARENT",
  "DEVELOPER",
] as const;

export const STAFF_ROLE_CODES = [
  "SUPER_ADMIN",
  "ADMINISTRATOR",
  "OPERATOR",
  "TEACHER",
  "SUPPORT_TEACHER",
  "DEVELOPER",
] as const;

export const LEARNER_ROLE_CODES = ["STUDENT", "PARENT"] as const;

/** A profile picture is an image of at most this size — the server refuses anything else, this only saves the round trip. */
export const PHOTO_MAX_BYTES = 2 * 1024 * 1024;
export const PHOTO_EXTENSIONS = [".png", ".jpg", ".jpeg", ".webp"];

/** Where a person's picture is fetched from (with the bearer token, `apiFetchBlob`). The version changes with each new picture, so a
 * browser that cached the old one asks again. */
export const userPhotoPath = (userId: string, version: string) => `/api/users/${userId}/photo?v=${encodeURIComponent(version)}`;

/** Sets the picture — `users.edit` for the account itself, or an administrator of it. Images only (never SVG), at most 2 MB. */
export function uploadUserPhoto(userId: string, file: File): Promise<{ version: string }> {
  const form = new FormData();
  form.append("file", file);
  return apiFetch<{ version: string }>(`/api/users/${userId}/photo`, { method: "POST", body: form });
}

export function deleteUserPhoto(userId: string): Promise<void> {
  return apiFetch<void>(`/api/users/${userId}/photo`, { method: "DELETE" });
}
