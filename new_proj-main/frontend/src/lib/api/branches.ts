import { apiFetch } from "../api-client";

export interface Branch {
  id: string;
  name: string;
  address: string;
  timezone: string;
}

export interface CreateBranchInput {
  name: string;
  address: string;
  timezone: string;
}

export function listBranches(): Promise<Branch[]> {
  return apiFetch<Branch[]>("/api/branches");
}

export function createBranch(input: CreateBranchInput): Promise<Branch> {
  return apiFetch<Branch>("/api/branches", {
    method: "POST",
    body: JSON.stringify(input),
  });
}

export function updateBranch(id: string, input: CreateBranchInput): Promise<Branch> {
  return apiFetch<Branch>(`/api/branches/${id}`, {
    method: "PATCH",
    body: JSON.stringify(input),
  });
}

/** Refused with a 409 and an explanation while the branch still holds groups, lessons or staff. */
export function deleteBranch(id: string): Promise<void> {
  return apiFetch<void>(`/api/branches/${id}`, { method: "DELETE" });
}
