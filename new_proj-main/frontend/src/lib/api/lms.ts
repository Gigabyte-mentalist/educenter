import { apiFetch, apiFetchBlob } from "../api-client";
import type { PageEnvelope } from "@/components/ui/Pagination";
import { buildQuery, DEFAULT_PAGE_SIZE, type PageParams } from "./query";

/** Ten megabytes — the shared upload ceiling. Above it the material has to be a link. */
export const RESOURCE_MAX_BYTES = 10 * 1024 * 1024;
export const RESOURCE_FILE_ACCEPT =
  ".pdf,.ppt,.pptx,.doc,.docx,.xls,.xlsx,.txt,.md,.csv,.png,.jpg,.jpeg,.webp,.gif,.mp3,.m4a,.wav";

export interface ResourceFile {
  fileName: string; contentType: string; size: number;
}

export interface LearningResource {
  id: string; lessonId: string; title: string;
  /** A material is either a link or an uploaded file — exactly one of `url` and `file` is set. */
  url: string | null;
  file: ResourceFile | null;
  description: string | null; createdBy: string; createdAt: string;
}
export function listResources(lessonId: string): Promise<LearningResource[]> {
  return apiFetch<LearningResource[]>(`/api/resources?lessonId=${encodeURIComponent(lessonId)}`);
}
export function createResource(input: { lessonId: string; title: string; url: string; description?: string }): Promise<LearningResource> {
  return apiFetch<LearningResource>("/api/resources", { method: "POST", body: JSON.stringify(input) });
}

export function uploadResource(input: { lessonId: string; title: string; description?: string; file: File }): Promise<LearningResource> {
  const form = new FormData();
  form.set("lessonId", input.lessonId);
  form.set("title", input.title);
  if (input.description) form.set("description", input.description);
  form.set("file", input.file);
  // No Content-Type header: the browser has to set the multipart boundary itself.
  return apiFetch<LearningResource>("/api/resources/upload", { method: "POST", body: form });
}

export function deleteResource(id: string): Promise<void> {
  return apiFetch<void>(`/api/resources/${id}`, { method: "DELETE" });
}

/** Authenticated download — an uploaded material is not reachable by a plain <a href>. */
export function fetchResourceFile(id: string): Promise<Blob> {
  return apiFetchBlob(`/api/resources/${id}/file`);
}

export type ExamType = "Placement" | "Unit" | "Monthly" | "MockIelts" | "Final" | "Custom";
export interface Exam {
  id: string; groupId: string; name: string; type: ExamType; date: string;
}
export interface ExamScores {
  /** IELTS bands, 0–9. */
  listening: number | null; reading: number | null; writing: number | null;
  speaking: number | null; overall: number | null;
  /** Percent, 0–100 — not bands. Null on exams that did not grade them (all results recorded before this existed). */
  grammar: number | null; vocabulary: number | null;
}
export interface ExamResult extends ExamScores {
  id: string; examId: string; studentId: string;
  /** The results list names each row, so the screen does not need the student list. */
  studentName?: string | null;
}
export interface ListExamsQuery extends PageParams { groupId?: string }
export function listExams(query: ListExamsQuery = {}): Promise<PageEnvelope<Exam>> {
  return apiFetch<PageEnvelope<Exam>>(`/api/exams${buildQuery({
    groupId: query.groupId, search: query.search?.trim(), page: query.page ?? 1, size: query.size ?? DEFAULT_PAGE_SIZE,
  })}`);
}
export function createExam(input: Omit<Exam, "id">): Promise<Exam> {
  return apiFetch<Exam>("/api/exams", { method: "POST", body: JSON.stringify(input) });
}
/** Only what is sent changes; the group never does. Allowed after results are in. */
export function updateExam(id: string, input: { name?: string; type?: ExamType; date?: string }): Promise<Exam> {
  return apiFetch<Exam>(`/api/exams/${id}`, { method: "PATCH", body: JSON.stringify(input) });
}
/** Only an exam with no results; otherwise a 409 that says how many. */
export function deleteExam(id: string): Promise<void> {
  return apiFetch<void>(`/api/exams/${id}`, { method: "DELETE" });
}
/** Takes back a wrongly entered result. The student is not notified; their timeline records the removal. */
export function deleteExamResult(examId: string, resultId: string): Promise<void> {
  return apiFetch<void>(`/api/exams/${examId}/results/${resultId}`, { method: "DELETE" });
}
export function listExamResults(id: string): Promise<ExamResult[]> {
  return apiFetch<ExamResult[]>(`/api/exams/${id}/results`);
}
export function recordExamResult(id: string, input: ExamScores & { studentId: string }): Promise<ExamResult> {
  return apiFetch<ExamResult>(`/api/exams/${id}/results`, { method: "POST", body: JSON.stringify(input) });
}

/** The teacher's side of one lesson. Staff-only: the notes never reach a student or a parent (the topic does, through the schedule). */
export interface LessonJournal {
  topic: string | null; notes: string | null;
  /** The top of this lesson's score scale; null = the lesson is not scored. */
  maxLessonScore: number | null;
}
export function getLessonJournal(lessonId: string): Promise<LessonJournal> {
  return apiFetch<LessonJournal>(`/api/lessons/${lessonId}/journal`);
}
/** Only what is sent changes; an empty text clears the field, and `maxLessonScore: 0` says "this lesson is not scored" (409 while
 * scores are already given). Works on a lesson that is over, refused (409) on a cancelled one. */
export function saveLessonJournal(
  lessonId: string, input: { topic?: string; notes?: string; maxLessonScore?: number },
): Promise<LessonJournal> {
  return apiFetch<LessonJournal>(`/api/lessons/${lessonId}/journal`, { method: "PATCH", body: JSON.stringify(input) });
}

export type AttendanceStatus = "Present" | "Absent" | "Late" | "Excused" | "Online";

export interface AttendanceRecord {
  id: string;
  lessonId: string;
  studentId: string;
  status: AttendanceStatus;
  /** Staff-only remark ("late — the bus broke down"): null for a student, a parent and for staff who cannot mark attendance. */
  note: string | null;
  score: number | null;
}

export function listAttendance(lessonId: string): Promise<AttendanceRecord[]> {
  return apiFetch<AttendanceRecord[]>(`/api/lessons/${lessonId}/attendance`);
}

export interface AttendanceItem {
  studentId: string; status: AttendanceStatus; note?: string | null; score?: number | null;
}

/** Each item is the student's whole record for the lesson — send the full roster, with the remarks and scores as they should stand. */
export function markAttendance(lessonId: string, items: AttendanceItem[]): Promise<void> {
  return apiFetch<void>(`/api/lessons/${lessonId}/attendance`, { method: "POST", body: JSON.stringify({ items }) });
}

export type HomeworkType = "Writing" | "Speaking" | "Reading" | "Listening" | "Grammar" | "Vocabulary" | "FileUpload" | "Quiz" | "Custom";

/** Twenty megabytes — a spoken answer is an audio file, so the ceiling is twice the other uploads'. Above it the answer is a link. */
export const HOMEWORK_FILE_MAX_BYTES = 20 * 1024 * 1024;
export const HOMEWORK_FILE_ACCEPT = ".pdf,.doc,.docx,.ppt,.pptx,.xls,.xlsx,.png,.jpg,.jpeg,.webp,.mp3,.m4a,.wav,.txt";

/** A stored file as the screens see it: its name and size — where the server keeps it is not their business. */
export interface HomeworkFile {
  fileName: string; contentType: string; size: number;
}

export interface Homework {
  id: string;
  lessonId: string;
  title: string;
  description: string | null;
  type: HomeworkType;
  deadline: string;
  maxScore: number;
  /** The teacher's worksheet or audio for the task, when there is one. */
  attachment: HomeworkFile | null;
  /** Given to some students by name (an individual task) rather than to the whole group. */
  targeted: boolean;
  /** Who an individual task is for — sent only to whoever may give homework; a student or parent sees `targeted` alone. */
  studentIds: string[] | null;
}

export interface CreateHomeworkInput {
  lessonId: string;
  title: string;
  description?: string;
  type: HomeworkType;
  deadline: string;
  maxScore: number;
  /** Absent or empty: the whole group. Otherwise an individual task for these students, each an active student of the lesson's group. */
  studentIds?: string[];
}

export function listHomeworkByLesson(lessonId: string): Promise<Homework[]> {
  return apiFetch<Homework[]>(`/api/homework?lessonId=${lessonId}`);
}

export function createHomework(input: CreateHomeworkInput): Promise<Homework> {
  return apiFetch<Homework>("/api/homework", { method: "POST", body: JSON.stringify(input) });
}

/** The same homework with the teacher's own file attached; without a file it is the JSON call above. */
export function createHomeworkWithFile(input: CreateHomeworkInput & { file: File }): Promise<Homework> {
  const form = new FormData();
  form.set("lessonId", input.lessonId);
  form.set("title", input.title);
  if (input.description) form.set("description", input.description);
  form.set("type", input.type);
  form.set("deadline", input.deadline);
  form.set("maxScore", String(input.maxScore));
  for (const studentId of input.studentIds ?? []) form.append("studentIds", studentId);
  form.set("file", input.file);
  // No Content-Type header: the browser has to set the multipart boundary itself.
  return apiFetch<Homework>("/api/homework/upload", { method: "POST", body: form });
}

/** Authenticated download of the teacher's file — a plain <a href> would arrive without the token. */
export function fetchHomeworkAttachment(homeworkId: string): Promise<Blob> {
  return apiFetchBlob(`/api/homework/${homeworkId}/attachment`);
}

/** A student's answer file: theirs, their child's for a parent, or a scoped group's for staff. */
export function fetchSubmissionAttachment(submissionId: string): Promise<Blob> {
  return apiFetchBlob(`/api/homework/submissions/${submissionId}/attachment`);
}

/** Only what is sent changes; an empty description clears it. The maximum cannot drop below a score already given (409). */
export function updateHomework(
  id: string, input: { title?: string; description?: string; type?: HomeworkType; deadline?: string; maxScore?: number },
): Promise<Homework> {
  return apiFetch<Homework>(`/api/homework/${id}`, { method: "PATCH", body: JSON.stringify(input) });
}

/** Only a homework nobody handed anything in for; otherwise a 409 that says how many. */
export function deleteHomework(id: string): Promise<void> {
  return apiFetch<void>(`/api/homework/${id}`, { method: "DELETE" });
}

export interface HomeworkSubmission {
  id: string;
  homeworkId: string;
  studentId: string;
  contentUrl: string | null;
  contentText: string | null;
  submittedAt: string;
  score: number | null;
  feedback: string | null;
  reviewedAt: string | null;
  /** The answer as a file, when the student attached one. */
  file: HomeworkFile | null;
}

export function listSubmissions(homeworkId: string): Promise<HomeworkSubmission[]> {
  return apiFetch<HomeworkSubmission[]>(`/api/homework/${homeworkId}/submissions`);
}

/** Student/parent view: the caller's own (or their children's) submissions for one homework. */
export function listMySubmissions(homeworkId: string): Promise<HomeworkSubmission[]> {
  return apiFetch<HomeworkSubmission[]>(`/api/homework/${homeworkId}/my-submissions`);
}

export function submitHomework(homeworkId: string, input: { contentText?: string; contentUrl?: string }): Promise<HomeworkSubmission> {
  return apiFetch<HomeworkSubmission>(`/api/homework/${homeworkId}/submissions`, {
    method: "POST",
    body: JSON.stringify(input),
  });
}

/** A file as the answer — with the student's own words or without. Same rules as the text answer: once, late allowed. */
export function submitHomeworkFile(homeworkId: string, input: { file: File; contentText?: string }): Promise<HomeworkSubmission> {
  const form = new FormData();
  form.set("file", input.file);
  if (input.contentText) form.set("contentText", input.contentText);
  return apiFetch<HomeworkSubmission>(`/api/homework/${homeworkId}/submissions/upload`, { method: "POST", body: form });
}

export function reviewSubmission(submissionId: string, score: number, feedback?: string): Promise<void> {
  return apiFetch<void>(`/api/homework/submissions/${submissionId}/review`, {
    method: "PATCH",
    body: JSON.stringify({ score, feedback }),
  });
}
