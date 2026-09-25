import { apiFetch } from "../api-client";

export type ScopeType = "AllOrganization" | "MyBranches" | "MyGroups" | "AssignedGroups" | "Self" | "MyChildren" | "None";

export interface MyPermission {
  code: string;
  scope: ScopeType;
}

/** The caller's effective permissions — used only to hide what they cannot do; the server re-checks every request. */
export function getMyPermissions(): Promise<MyPermission[]> {
  return apiFetch<MyPermission[]>("/api/me/permissions");
}
