import { apiFetch } from "../api-client";

export type AutomationTrigger = "Sweep" | "Event" | "Manual";

export interface AutomationParameter {
  key: string;
  label: string;
  unit: string;
  min: number;
  max: number;
  default: number;
  value: number;
  /** min, max, default and value are whole numbers in stored units; divide by this to show them (a band of 5.5 is 55 with a scale of 10). */
  scale: number;
}

export interface AutomationAction {
  key: string;
  label: string;
  default: boolean;
  value: boolean;
}

export interface AutomationRun {
  id: string;
  ruleCode: string;
  ruleName: string;
  trigger: AutomationTrigger;
  itemCount: number;
  summary: string;
  ranAt: string;
}

export interface AutomationRule {
  code: string;
  name: string;
  trigger: string;
  description: string;
  enabled: boolean;
  /** True when the organization changed something; false means catalog defaults. */
  isCustomized: boolean;
  parameters: AutomationParameter[];
  actions: AutomationAction[];
  updatedAt: string | null;
  updatedByName: string | null;
  lastRun: AutomationRun | null;
}

export interface AutomationRunNowResult {
  invoicesIssued: number;
  studentsMarkedDebt: number;
  studentsCleared: number;
  debtTasks: number;
  trialFollowUps: number;
  absenceSignals: number;
  homeworkSignals: number;
  /** Support recommendations raised by the Writing grammar, Listening and Reading rules. */
  learningSignals: number;
  reminders: number;
  /** Everything the rules recorded in the activity log, including messages to staff. */
  ruleActions: number;
}

export function listAutomationRules(): Promise<AutomationRule[]> {
  return apiFetch<AutomationRule[]>("/api/automation/rules");
}

export function updateAutomationRule(
  code: string,
  input: { enabled: boolean; parameters: Record<string, number>; actions: Record<string, boolean> },
): Promise<AutomationRule> {
  return apiFetch<AutomationRule>(`/api/automation/rules/${code}`, { method: "PUT", body: JSON.stringify(input) });
}

export function resetAutomationRule(code: string): Promise<AutomationRule> {
  return apiFetch<AutomationRule>(`/api/automation/rules/${code}`, { method: "DELETE" });
}

export function listAutomationRuns(limit = 50): Promise<AutomationRun[]> {
  return apiFetch<AutomationRun[]>(`/api/automation/runs?limit=${limit}`);
}

export function runAutomationNow(): Promise<AutomationRunNowResult> {
  return apiFetch<AutomationRunNowResult>("/api/automation/run", { method: "POST" });
}
