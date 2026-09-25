import { apiFetch, apiFetchBlob } from "../api-client";

export type AiFeature = "Tutor" | "Writing" | "Speaking";

export interface AiFeatureStatus {
  feature: AiFeature;
  allowed: boolean;
  /** False when no AI provider is configured for the feature. */
  available: boolean;
  limit: number | null;
  used: number | null;
  remaining: number | null;
  unit: string | null;
}

export interface AiStatus {
  /** Answers come from the development fake provider, not a real model. */
  demoMode: boolean;
  features: AiFeatureStatus[];
}

export function getAiStatus(): Promise<AiStatus> {
  return apiFetch<AiStatus>("/api/ai/status");
}

export type WritingTaskType = "IeltsTask1" | "IeltsTask2" | "General";
export type WritingEvaluationStatus = "Queued" | "Processing" | "Completed" | "Failed" | "Rejected";

export const WRITING_TASK_LABEL: Record<WritingTaskType, string> = {
  IeltsTask1: "IELTS Task 1",
  IeltsTask2: "IELTS Task 2",
  General: "Umumiy writing",
};

export interface WritingEvaluationSummary {
  id: string;
  studentId: string;
  studentName: string;
  taskType: WritingTaskType;
  taskTitle: string;
  wordCount: number;
  status: WritingEvaluationStatus;
  /** Estimated IELTS band from AI — not an official score. */
  estimatedBand: number | null;
  teacherBand: number | null;
  homeworkId: string | null;
  homeworkSubmissionId: string | null;
  createdAt: string;
  completedAt: string | null;
}

export interface WritingCriterion {
  key: string;
  label: string;
  aiBand: number | null;
  teacherBand: number | null;
  comment: string | null;
}

export interface WritingIssue {
  excerpt: string;
  correction: string;
  explanation: string;
}

export interface WritingEvaluation {
  summary: WritingEvaluationSummary;
  taskPrompt: string;
  essay: string;
  error: string | null;
  model: string | null;
  criteria: WritingCriterion[];
  strengths: string[];
  weaknesses: string[];
  grammarIssues: WritingIssue[];
  vocabularyIssues: WritingIssue[];
  suggestions: string[];
  teacherComment: string | null;
  overriddenByName: string | null;
  overriddenAt: string | null;
  canOverride: boolean;
  canRetry: boolean;
}

export interface WritingOverrideInput {
  band: number;
  taskResponse: number | null;
  coherenceCohesion: number | null;
  lexicalResource: number | null;
  grammaticalRange: number | null;
  comment: string;
}

export const isWritingPending = (status: WritingEvaluationStatus) => status === "Queued" || status === "Processing";

export function createWritingEvaluation(input: { taskType: WritingTaskType; taskPrompt: string; essay: string }): Promise<WritingEvaluation> {
  return apiFetch<WritingEvaluation>("/api/ai/writing/evaluations", { method: "POST", body: JSON.stringify(input) });
}

export function listMyWritingEvaluations(homeworkId?: string): Promise<WritingEvaluationSummary[]> {
  return apiFetch<WritingEvaluationSummary[]>(
    homeworkId ? `/api/ai/writing/evaluations?homeworkId=${encodeURIComponent(homeworkId)}` : "/api/ai/writing/evaluations",
  );
}

/**
 * The reviewer's inbox. With no filter this returns every assessment from the students in the caller's scope,
 * which is what the teacher screen shows; `pendingOnly` narrows it to the ones no teacher has graded yet.
 */
export function listWritingReviews(filter: {
  studentId?: string;
  homeworkId?: string;
  status?: WritingEvaluationStatus;
  pendingOnly?: boolean;
}): Promise<WritingEvaluationSummary[]> {
  const params = new URLSearchParams();
  if (filter.studentId) params.set("studentId", filter.studentId);
  if (filter.homeworkId) params.set("homeworkId", filter.homeworkId);
  if (filter.status) params.set("status", filter.status);
  if (filter.pendingOnly) params.set("pendingOnly", "true");
  return apiFetch<WritingEvaluationSummary[]>(`/api/ai/writing/reviews?${params}`);
}

export function getWritingEvaluation(id: string): Promise<WritingEvaluation> {
  return apiFetch<WritingEvaluation>(`/api/ai/writing/evaluations/${id}`);
}

export function retryWritingEvaluation(id: string): Promise<WritingEvaluation> {
  return apiFetch<WritingEvaluation>(`/api/ai/writing/evaluations/${id}/retry`, { method: "POST" });
}

export function overrideWritingEvaluation(id: string, input: WritingOverrideInput): Promise<WritingEvaluation> {
  return apiFetch<WritingEvaluation>(`/api/ai/writing/evaluations/${id}/override`, { method: "POST", body: JSON.stringify(input) });
}

/* ---------------------------------- Knowledge base (RAG) --------------------------------- */

export type KnowledgeScope = "Organization" | "Branch" | "Course" | "Group";
export type KnowledgeVisibility = "Students" | "StaffOnly";
export type KnowledgeStatus = "Uploaded" | "Processing" | "Ready" | "Quarantined" | "Failed";

export const KNOWLEDGE_SCOPE_LABEL: Record<KnowledgeScope, string> = {
  Organization: "Butun tashkilot",
  Branch: "Filial",
  Course: "Kurs",
  Group: "Guruh",
};

export const KNOWLEDGE_VISIBILITY_LABEL: Record<KnowledgeVisibility, string> = {
  Students: "Studentlar ko'radi",
  StaffOnly: "Faqat xodimlar",
};

export const KNOWLEDGE_STATUS_LABEL: Record<KnowledgeStatus, string> = {
  Uploaded: "Navbatda",
  Processing: "Qayta ishlanmoqda",
  Ready: "Tayyor",
  Quarantined: "Karantinda",
  Failed: "Xatolik",
};

export interface KnowledgeDocument {
  id: string;
  title: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  scope: KnowledgeScope;
  scopeName: string | null;
  visibility: KnowledgeVisibility;
  branchId: string | null;
  courseId: string | null;
  groupId: string | null;
  status: KnowledgeStatus;
  error: string | null;
  chunkCount: number;
  embeddingModel: string | null;
  uploadedByName: string;
  createdAt: string;
  processedAt: string | null;
  /** False when the uploaded file is gone from storage — the document can still be read from its index. */
  fileAvailable: boolean;
  /** Quarantined because the virus scan found malware in it — unlike a prompt-injection hold, a person cannot approve this one. */
  malware: boolean;
}

export interface KnowledgeTargets {
  scopes: KnowledgeScope[];
  branches: { id: string; name: string }[];
  courses: { id: string; name: string }[];
  groups: { id: string; name: string }[];
}

export function getKnowledgeTargets(): Promise<KnowledgeTargets> {
  return apiFetch<KnowledgeTargets>("/api/ai/knowledge/targets");
}

export interface KnowledgeUploadInput {
  file: File;
  title: string;
  scope: KnowledgeScope;
  visibility: KnowledgeVisibility;
  branchId?: string;
  courseId?: string;
  groupId?: string;
}

export const isKnowledgePending = (status: KnowledgeStatus) => status === "Uploaded" || status === "Processing";

export function listKnowledgeDocuments(): Promise<KnowledgeDocument[]> {
  return apiFetch<KnowledgeDocument[]>("/api/ai/knowledge/documents");
}

export function uploadKnowledgeDocument(input: KnowledgeUploadInput): Promise<KnowledgeDocument> {
  const form = new FormData();
  form.append("file", input.file, input.file.name);
  form.append("title", input.title);
  form.append("scope", input.scope);
  form.append("visibility", input.visibility);
  if (input.branchId) form.append("branchId", input.branchId);
  if (input.courseId) form.append("courseId", input.courseId);
  if (input.groupId) form.append("groupId", input.groupId);
  return apiFetch<KnowledgeDocument>("/api/ai/knowledge/documents", { method: "POST", body: form });
}

export interface KnowledgePreview {
  text: string;
  truncated: boolean;
  /** The file is missing and the text was rebuilt from the indexed chunks. */
  restored: boolean;
}

export function previewKnowledgeDocument(id: string): Promise<KnowledgePreview> {
  return apiFetch<KnowledgePreview>(`/api/ai/knowledge/documents/${id}/preview`);
}

export function approveKnowledgeDocument(id: string): Promise<KnowledgeDocument> {
  return apiFetch<KnowledgeDocument>(`/api/ai/knowledge/documents/${id}/approve`, { method: "POST" });
}

export function reingestKnowledgeDocument(id: string): Promise<KnowledgeDocument> {
  return apiFetch<KnowledgeDocument>(`/api/ai/knowledge/documents/${id}/reingest`, { method: "POST" });
}

export function deleteKnowledgeDocument(id: string): Promise<void> {
  return apiFetch<void>(`/api/ai/knowledge/documents/${id}`, { method: "DELETE" });
}

/* -------------------------------------- AI Tutor ----------------------------------------- */

export interface TutorCitation {
  documentId: string;
  title: string;
  ordinal: number;
}

export interface TutorConversation {
  id: string;
  title: string;
  messageCount: number;
  lastMessageAt: string;
}

export interface TutorMessage {
  id: string;
  role: "User" | "Assistant";
  content: string;
  citations: TutorCitation[];
  createdAt: string;
}

export interface TutorAnswer {
  conversationId: string;
  answer: string;
  citations: TutorCitation[];
  /** True when the question was outside the centre's teaching domain and the tutor declined. */
  outOfDomain: boolean;
}

export function listTutorConversations(): Promise<TutorConversation[]> {
  return apiFetch<TutorConversation[]>("/api/ai/tutor/conversations");
}

export function listTutorMessages(conversationId: string): Promise<TutorMessage[]> {
  return apiFetch<TutorMessage[]>(`/api/ai/tutor/conversations/${conversationId}/messages`);
}

export function askTutor(input: { conversationId?: string; question: string }): Promise<TutorAnswer> {
  return apiFetch<TutorAnswer>("/api/ai/tutor/ask", { method: "POST", body: JSON.stringify(input) });
}

export function deleteTutorConversation(conversationId: string): Promise<void> {
  return apiFetch<void>(`/api/ai/tutor/conversations/${conversationId}`, { method: "DELETE" });
}

/* --------------------------- AI monitoring, limits and benchmark -------------------------- */

export interface AiLimitRow {
  key: string;
  feature: AiFeature;
  label: string;
  unit: string;
  min: number;
  max: number;
  default: number;
  value: number;
}

export interface AiLimits {
  /** False means the organization runs on the catalog defaults. */
  isCustomized: boolean;
  limits: AiLimitRow[];
}

export interface AiUsageTotals {
  requests: number;
  succeeded: number;
  failed: number;
  rejected: number;
  inputTokens: number;
  outputTokens: number;
  costUsd: number;
  averageLatencyMs: number;
  speakingMinutes: number;
  tutorQuestions: number;
  writingEvaluations: number;
}

export interface AiUsageDay {
  date: string;
  requests: number;
  costUsd: number;
}

export interface AiUsageGroup {
  key: string;
  requests: number;
  succeeded: number;
  costUsd: number;
  averageLatencyMs: number;
}

export interface AiUsageStudent {
  studentId: string | null;
  name: string;
  requests: number;
  costUsd: number;
  speakingMinutes: number;
}

export interface AiUsageBranch {
  branchId: string | null;
  branchName: string;
  requests: number;
  costUsd: number;
}

export interface AiUsageFailure {
  at: string;
  feature: AiFeature;
  taskKind: string;
  reason: string | null;
  detail: string | null;
  provider: string | null;
}

export interface AiUsageSummary {
  from: string;
  to: string;
  totals: AiUsageTotals;
  daily: AiUsageDay[];
  byFeature: AiUsageGroup[];
  byProvider: AiUsageGroup[];
  byBranch: AiUsageBranch[];
  topStudents: AiUsageStudent[];
  /** Every non-success reason in the period — rejections (limit, injection) and failures alike. */
  reasons: AiUsageGroup[];
  recentFailures: AiUsageFailure[];
}

export interface WritingBenchmarkRow {
  promptVersion: string;
  model: string | null;
  samples: number;
  aiMean: number;
  teacherMean: number;
  /** AI minus teacher: positive means the AI marks higher than the teacher does. */
  bias: number;
  meanAbsoluteError: number;
  withinHalfBand: number;
  taskResponseMae: number | null;
  coherenceMae: number | null;
  lexicalMae: number | null;
  grammarMae: number | null;
}

export interface WritingBenchmark {
  from: string;
  to: string;
  completed: number;
  overridden: number;
  rows: WritingBenchmarkRow[];
}

export interface AiProviderInfo {
  name: string;
  type: string;
  active: boolean;
  inactiveReason: string | null;
  baseUrl: string | null;
  hasApiKey: boolean;
  chatModel: string | null;
  embeddingModel: string | null;
}

export interface AiProviders {
  providers: AiProviderInfo[];
  routes: { task: string; chain: string[] }[];
}

export function getAiUsageSummary(params: { from?: string; to?: string; branchId?: string } = {}): Promise<AiUsageSummary> {
  const query = new URLSearchParams();
  if (params.from) query.set("from", params.from);
  if (params.to) query.set("to", params.to);
  if (params.branchId) query.set("branchId", params.branchId);
  return apiFetch<AiUsageSummary>(`/api/ai/usage/summary?${query}`);
}

export function getWritingBenchmark(params: { from?: string; to?: string } = {}): Promise<WritingBenchmark> {
  const query = new URLSearchParams();
  if (params.from) query.set("from", params.from);
  if (params.to) query.set("to", params.to);
  return apiFetch<WritingBenchmark>(`/api/ai/benchmarks/writing?${query}`);
}

export function getAiLimits(): Promise<AiLimits> {
  return apiFetch<AiLimits>("/api/ai/limits");
}

export function updateAiLimits(limits: Record<string, number>): Promise<AiLimits> {
  return apiFetch<AiLimits>("/api/ai/limits", { method: "PUT", body: JSON.stringify({ limits }) });
}

export function resetAiLimits(): Promise<AiLimits> {
  return apiFetch<AiLimits>("/api/ai/limits", { method: "DELETE" });
}

export function getAiProviders(): Promise<AiProviders> {
  return apiFetch<AiProviders>("/api/ai/providers");
}

/* ------------------------------------- AI Speaking --------------------------------------- */

export type SpeakingMode = "IeltsPart1" | "IeltsPart2" | "IeltsPart3" | "FullMock" | "GeneralEnglish" | "FreeConversation";
export type SpeakingStatus = "Active" | "Completed" | "Failed" | "Abandoned";

export const SPEAKING_MODE_LABEL: Record<SpeakingMode, string> = {
  IeltsPart1: "IELTS Part 1",
  IeltsPart2: "IELTS Part 2 (cue card)",
  IeltsPart3: "IELTS Part 3",
  FullMock: "Full Mock (11–14 daqiqa)",
  GeneralEnglish: "Umumiy ingliz tili",
  FreeConversation: "Erkin suhbat",
};

export const SPEAKING_MODE_HINT: Record<SpeakingMode, string> = {
  IeltsPart1: "Qisqa, kundalik savollar — 5 ta savol",
  IeltsPart2: "Bitta kartochka: 1 daqiqa tayyorgarlik, 1–2 daqiqa gapirish",
  IeltsPart3: "Fikr talab qiladigan chuqurroq savollar — 4 ta savol",
  FullMock: "To'liq imtihon: Part 1 (5 savol), Part 2 (kartochka), Part 3 (5 savol) — kunlik limitdan taxminan 14 daqiqa",
  GeneralEnglish: "Kundalik mavzularda suhbat — 6 ta savol",
  FreeConversation: "Erkin suhbat — 6 ta savol",
};

/** The minute a Part 2 cue card gives before the student speaks (the server's `SpeakingScript.Part2PreparationSeconds`). */
export const SPEAKING_PREPARATION_SECONDS = 60;

export interface SpeakingTurn {
  id: string;
  ordinal: number;
  question: string;
  transcript: string | null;
  spokenSeconds: number;
  wordsPerMinute: number | null;
  longPauses: number | null;
  hasAudio: boolean;
  /** 1, 2 or 3 for an IELTS-shaped practice — the part of the test this question is in; null for the other modes. */
  part: number | null;
}

export interface SpeakingPartComment {
  part: number;
  comment: string | null;
}

export interface SpeakingAssessment {
  fluencyComment: string | null;
  lexicalComment: string | null;
  grammarComment: string | null;
  strengths: string[];
  weaknesses: string[];
  suggestions: string[];
  spokenSeconds: number;
  wordsPerMinute: number;
  longPauses: number;
  turnCount: number;
  /** Absent from every assessment made before answer relevance was asked for. */
  relevanceComment?: string | null;
  /** One comment per part — a Full Mock only. */
  parts?: SpeakingPartComment[] | null;
}

export interface SpeakingSession {
  id: string;
  mode: SpeakingMode;
  modeLabel: string;
  topic: string;
  status: SpeakingStatus;
  turnCount: number;
  totalTurns: number;
  spokenSeconds: number;
  estimatedBand: number | null;
  fluencyBand: number | null;
  lexicalBand: number | null;
  grammarBand: number | null;
  error: string | null;
  createdAt: string;
  completedAt: string | null;
  turns: SpeakingTurn[];
  assessment: SpeakingAssessment | null;
  /** The question waiting for an answer, while the session is active. */
  currentQuestion: string | null;
  /** Set once a teacher has listened and corrected the AI's bands; the AI's own stay as they were. */
  teacherReview: SpeakingTeacherReview | null;
  /** Only ever true on the staff endpoint, for someone who holds `ai.speaking.override` over the student. */
  canOverride: boolean;
  /** How well the answers answered the questions — shown beside the three criteria, and not part of `estimatedBand`. Null before it was asked for. */
  relevanceBand: number | null;
  /** The part of the test the waiting question is in, while the practice is active and IELTS-shaped. */
  currentPart: number | null;
  /** The turn the waiting question belongs to — what its recorded voice is fetched by. */
  currentTurnId: string | null;
}

export interface SpeakingTeacherReview {
  band: number | null;
  fluencyBand: number | null;
  lexicalBand: number | null;
  grammarBand: number | null;
  comment: string | null;
  byName: string | null;
  at: string;
}

export interface SpeakingSessionSummary {
  id: string;
  mode: SpeakingMode;
  modeLabel: string;
  topic: string;
  status: SpeakingStatus;
  turnCount: number;
  spokenSeconds: number;
  estimatedBand: number | null;
  createdAt: string;
  /** The teacher's overall band, once they have corrected the session. */
  teacherBand: number | null;
}

export function listSpeakingSessions(): Promise<SpeakingSessionSummary[]> {
  return apiFetch<SpeakingSessionSummary[]>("/api/ai/speaking/sessions");
}

export function getSpeakingSession(id: string): Promise<SpeakingSession> {
  return apiFetch<SpeakingSession>(`/api/ai/speaking/sessions/${id}`);
}

export function startSpeakingSession(mode: SpeakingMode): Promise<SpeakingSession> {
  return apiFetch<SpeakingSession>("/api/ai/speaking/sessions", { method: "POST", body: JSON.stringify({ mode }) });
}

/** The extension a recording is uploaded under. The server checks that the file is what its name says, and browsers do not all record
 * WebM: Firefox writes Ogg, Safari writes MP4 — a name that always said ".webm" would get those recordings refused. */
function recordingFileName(audio: Blob): string {
  const type = audio.type.split(";")[0].trim().toLowerCase();
  if (type === "audio/ogg") return "answer.ogg";
  if (type === "audio/mp4" || type === "audio/x-m4a") return "answer.m4a";
  if (type === "audio/mpeg") return "answer.mp3";
  if (type === "audio/wav" || type === "audio/x-wav") return "answer.wav";
  return "answer.webm";
}

export function answerSpeakingTurn(sessionId: string, audio: Blob, durationSeconds: number): Promise<SpeakingSession> {
  const form = new FormData();
  form.append("audio", audio, recordingFileName(audio));
  form.append("durationSeconds", String(durationSeconds));
  return apiFetch<SpeakingSession>(`/api/ai/speaking/sessions/${sessionId}/turns`, { method: "POST", body: form });
}

export function finishSpeakingSession(id: string): Promise<SpeakingSession> {
  return apiFetch<SpeakingSession>(`/api/ai/speaking/sessions/${id}/finish`, { method: "POST" });
}

export function abandonSpeakingSession(id: string): Promise<SpeakingSession> {
  return apiFetch<SpeakingSession>(`/api/ai/speaking/sessions/${id}/abandon`, { method: "POST" });
}

/** The examiner's question read aloud. Rejects with a 503 `ApiError` when no voice is configured (or its provider is down) — the page then
 * shows the question as text and offers the browser's own voice. */
export function getSpeakingQuestionAudio(sessionId: string, turnId: string): Promise<Blob> {
  return apiFetchBlob(`/api/ai/speaking/sessions/${sessionId}/turns/${turnId}/question-audio`);
}

/* ------------------- Staff reading a student's practice (ai.speaking.review) ------------------- */

/** One finished practice in a student's Speaking tab: the four bands, without the transcript or the recordings. */
export interface StudentSpeakingRow {
  id: string;
  mode: SpeakingMode;
  modeLabel: string;
  topic: string;
  turnCount: number;
  spokenSeconds: number;
  estimatedBand: number | null;
  fluencyBand: number | null;
  lexicalBand: number | null;
  grammarBand: number | null;
  createdAt: string;
  completedAt: string | null;
  teacherBand: number | null;
}

/** Only finished sessions the student knew were shared with staff; anything else is a 404. */
export function listStudentSpeaking(studentId: string): Promise<StudentSpeakingRow[]> {
  return apiFetch<StudentSpeakingRow[]>(`/api/students/${studentId}/speaking/sessions`);
}

/** The whole session — questions, transcribed answers with pace and pauses, the assessment. Opening it is written to the audit log. */
export function getStudentSpeakingSession(studentId: string, sessionId: string): Promise<SpeakingSession> {
  return apiFetch<SpeakingSession>(`/api/students/${studentId}/speaking/sessions/${sessionId}`);
}

/** Where a turn's recording is fetched from (with the bearer token, through `apiFetchBlob`). */
export const studentSpeakingAudioPath = (studentId: string, sessionId: string, turnId: string) =>
  `/api/students/${studentId}/speaking/sessions/${sessionId}/turns/${turnId}/audio`;

/** A finished, shared session in the teacher's inbox. */
export interface SpeakingReviewRow {
  id: string;
  studentId: string;
  studentName: string;
  mode: SpeakingMode;
  modeLabel: string;
  topic: string;
  spokenSeconds: number;
  estimatedBand: number | null;
  teacherBand: number | null;
  completedAt: string | null;
}

/** Every finished practice of the students in the caller's scope; `pendingOnly` leaves out what a teacher has already corrected. */
export function listSpeakingReviews(filter: { pendingOnly?: boolean; studentId?: string } = {}): Promise<SpeakingReviewRow[]> {
  const params = new URLSearchParams();
  if (filter.pendingOnly) params.set("pendingOnly", "true");
  if (filter.studentId) params.set("studentId", filter.studentId);
  return apiFetch<SpeakingReviewRow[]>(`/api/ai/speaking/reviews?${params}`);
}

export interface SpeakingOverrideInput {
  band: number;
  fluency: number | null;
  lexical: number | null;
  grammar: number | null;
  comment: string;
}

/** The teacher's own bands after listening — beside the AI's, which stay unchanged. The student is told. */
export function overrideSpeakingSession(studentId: string, sessionId: string, input: SpeakingOverrideInput): Promise<SpeakingSession> {
  return apiFetch<SpeakingSession>(`/api/students/${studentId}/speaking/sessions/${sessionId}/override`, { method: "POST", body: JSON.stringify(input) });
}
