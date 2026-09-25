import { apiFetch } from "../api-client";
import type { ScopeType } from "./me";

export interface MatrixRole {
  code: string;
  name: string;
}

export interface MatrixCell {
  roleCode: string;
  /** What the role ships with; null: it does not hold the permission by default. */
  defaultScope: ScopeType | null;
  /** Set when the Super Admin changed this cell. */
  overrideId: string | null;
  overrideScope: ScopeType | null;
  revoked: boolean;
  /** What the role actually gets now; null: nothing. */
  effectiveScope: ScopeType | null;
}

export interface MatrixPermission {
  code: string;
  resource: string;
  action: string;
  /** The scopes that make sense for this permission — a cell may only be set to one of them. */
  allowedScopes: ScopeType[];
  cells: MatrixCell[];
}

export interface PermissionMatrix {
  roles: MatrixRole[];
  permissions: MatrixPermission[];
}

export const PERMISSION_MATRIX_KEY = ["permission-matrix"] as const;

export const getPermissionMatrix = () => apiFetch<PermissionMatrix>("/api/permissions/matrix");

/** `scope: null` with `revoked: false` puts the cell back to its default. */
export interface SetOverrideInput {
  roleCode: string;
  permissionCode: string;
  scope: ScopeType | null;
  revoked: boolean;
}

export const setPermissionOverride = (input: SetOverrideInput) =>
  apiFetch<MatrixCell>("/api/permissions/overrides", { method: "PUT", body: JSON.stringify(input) });

export const SCOPE_LABEL: Record<ScopeType, string> = {
  AllOrganization: "Butun tashkilot",
  MyBranches: "O'z filiallari",
  MyGroups: "O'z guruhlari",
  AssignedGroups: "Biriktirilgan guruhlar",
  Self: "Faqat o'zi",
  MyChildren: "Farzandlari",
  None: "Yo'q",
};

/** The same, short enough for a cell of the matrix. */
export const SCOPE_SHORT: Record<ScopeType, string> = {
  AllOrganization: "Hammasi",
  MyBranches: "Filial",
  MyGroups: "Guruh",
  AssignedGroups: "Biriktirilgan",
  Self: "O'zi",
  MyChildren: "Farzand",
  None: "—",
};

export const SCOPE_HELP: Record<ScopeType, string> = {
  AllOrganization: "Tashkilotning barcha filiallari",
  MyBranches: "Xodimga biriktirilgan filiallar",
  MyGroups: "O'zi asosiy o'qituvchi bo'lgan guruhlar",
  AssignedGroups: "Yordamchi o'qituvchi sifatida biriktirilgan guruhlar",
  Self: "Faqat o'ziga tegishli ma'lumot",
  MyChildren: "Ota-onaning farzandlariga tegishli ma'lumot",
  None: "",
};
