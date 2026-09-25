import { apiFetch } from "../api-client";

/* Developer observability (docs/00_TZ.md §3.8, §53) and temporary access to production data. */

export interface EndpointMetric {
  method: string;
  route: string;
  requests: number;
  errors: number;
  p50Ms: number;
  p95Ms: number;
  p99Ms: number;
}

export interface SystemMetrics {
  requests: {
    windowMinutes: number;
    collectingSince: string;
    requests: number;
    requestsPerMinute: number;
    serverErrors: number;
    clientErrors: number;
    errorRate: number;
    p50Ms: number;
    p95Ms: number;
    p99Ms: number;
    timeline: { minute: string; requests: number; errors: number }[];
    endpoints: EndpointMetric[];
  };
  database: {
    connections: number;
    maxConnections: number;
    activeQueries: { pid: number; runningSeconds: number; query: string }[];
    statementStatsAvailable: boolean;
    slowStatements: { query: string; calls: number; meanMs: number; totalMs: number }[];
  };
  storage: {
    diskTotalBytes: number | null;
    diskFreeBytes: number | null;
    folders: { name: string; bytes: number; files: number }[];
  };
}

export interface SystemJobs {
  available: boolean;
  note: string | null;
  enqueued: number;
  processing: number;
  scheduled: number;
  succeeded: number;
  failed: number;
  servers: number;
  recurring: { id: string; cron: string; nextRun: string | null; lastRun: string | null; lastState: string | null }[];
  recentFailures: { id: string; job: string; reason: string | null; failedAt: string | null }[];
}

export interface ServerError {
  at: string;
  method: string;
  path: string;
  status: number;
  elapsedMs: number;
  traceId: string | null;
  exceptionType: string | null;
  message: string | null;
}

export interface Deployment {
  version: string;
  environment: string;
  runtime: string;
  os: string;
  machineName: string;
  startedAt: string;
  memoryBytes: number;
  lastMigration: string | null;
  appliedMigrations: string[];
  pendingMigrations: string[];
}

export const getSystemMetrics = () => apiFetch<SystemMetrics>("/api/system/metrics");
export const getSystemJobs = () => apiFetch<SystemJobs>("/api/system/jobs");
export const getServerErrors = () => apiFetch<ServerError[]>("/api/system/logs");
export const getDeployment = () => apiFetch<Deployment>("/api/system/deployment");

/* -------------------------- Temporary access to production data -------------------------- */

export type DataAccessStatus = "Pending" | "Approved" | "Rejected" | "Revoked";

export interface DataAccessRequest {
  id: string;
  requesterId: string;
  requesterName: string;
  permissionCode: string;
  reason: string;
  hours: number;
  status: DataAccessStatus;
  createdAt: string;
  decidedAt: string | null;
  decidedByName: string | null;
  decisionNote: string | null;
  expiresAt: string | null;
  /** Approved and not yet expired. */
  isActive: boolean;
}

/** What a Developer may ask for: read access to academic records. Mirrors DataAccessRequest.GrantableCodes on the server. */
export const GRANTABLE_ACCESS: { code: string; label: string; href: string }[] = [
  { code: "students.view", label: "Talabalar", href: "/students" },
  { code: "groups.view", label: "Guruhlar", href: "/groups" },
  { code: "attendance.view", label: "Davomat", href: "/schedule" },
  { code: "exams.view", label: "Imtihonlar", href: "/exams" },
  { code: "homework.view", label: "Uy vazifalari", href: "/schedule" },
];

export const accessLabel = (code: string) => GRANTABLE_ACCESS.find((a) => a.code === code)?.label ?? code;

export const listMyAccessRequests = () => apiFetch<DataAccessRequest[]>("/api/system/data-access/mine");
export const listAccessRequests = () => apiFetch<DataAccessRequest[]>("/api/system/data-access");

export const createAccessRequest = (input: { permissionCode: string; reason: string; hours: number }) =>
  apiFetch<DataAccessRequest>("/api/system/data-access", { method: "POST", body: JSON.stringify(input) });

export const decideAccessRequest = (id: string, action: "approve" | "reject" | "revoke", input: { hours?: number; note?: string }) =>
  apiFetch<DataAccessRequest>(`/api/system/data-access/${id}/${action}`, { method: "POST", body: JSON.stringify(input) });
