import type { ComplaintStatus } from "@/lib/api/complaints";

/** Badge colour of each status: waiting is a warning, being worked is the brand's, settled is green, closed is quiet. */
export const COMPLAINT_STATUS_TONE: Record<ComplaintStatus, "warning" | "info" | "brand" | "success" | "neutral"> = {
  New: "warning",
  Assigned: "info",
  InProgress: "brand",
  Resolved: "success",
  Closed: "neutral",
};
