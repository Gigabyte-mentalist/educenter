import { apiFetch } from "../api-client";
import type { PageEnvelope } from "@/components/ui/Pagination";

export interface AuditLog {
  id: string;
  userId: string;
  /** Null when the account has since been deleted — the trail outlives it. */
  userName: string | null;
  action: string;
  resource: string;
  resourceId: string;
  oldValueJson: string | null;
  newValueJson: string | null;
  reason: string | null;
  createdAt: string;
}

export interface AuditFilters {
  actions: string[];
  resources: string[];
}

export interface ListAuditQuery {
  resource?: string;
  action?: string;
  userId?: string;
  from?: string;
  to?: string;
  page?: number;
  size?: number;
}

export function listAuditLogs(query: ListAuditQuery = {}): Promise<PageEnvelope<AuditLog>> {
  const params = new URLSearchParams();
  if (query.resource) params.set("resource", query.resource);
  if (query.action) params.set("action", query.action);
  if (query.userId) params.set("userId", query.userId);
  if (query.from) params.set("from", query.from);
  if (query.to) params.set("to", query.to);
  params.set("page", String(query.page ?? 1));
  params.set("size", String(query.size ?? 50));
  return apiFetch<PageEnvelope<AuditLog>>(`/api/audit-logs?${params}`);
}

/** The action/resource values actually present, so the dropdowns never offer an empty filter. */
export function getAuditFilters(): Promise<AuditFilters> {
  return apiFetch<AuditFilters>("/api/audit-logs/filters");
}
