import { apiFetch, apiFetchBlob } from "../api-client";
import type { PageEnvelope } from "@/components/ui/Pagination";
import { buildQuery, DEFAULT_PAGE_SIZE, type PageParams } from "./query";

export type StudentStatus = "Active" | "Debt" | "Frozen" | "Transferred" | "Graduated" | "Dropped";

export interface Student {
  id: string;
  userId: string;
  fullName: string;
  email: string;
  phone: string;
  studentNumber: string;
  joinedDate: string;
  currentLevel: string | null;
  status: StudentStatus;
}

export interface ListStudentsQuery extends PageParams {
  status?: StudentStatus;
  /** Actively enrolled in this group. */
  groupId?: string;
}

/** One page of students the caller may see — searched, filtered and sliced in the database. */
export function listStudents(query: ListStudentsQuery = {}): Promise<PageEnvelope<Student>> {
  return apiFetch<PageEnvelope<Student>>(`/api/students${buildQuery({
    search: query.search?.trim(), status: query.status, groupId: query.groupId,
    page: query.page ?? 1, size: query.size ?? DEFAULT_PAGE_SIZE,
  })}`);
}

export function getStudent(id: string): Promise<Student> {
  return apiFetch<Student>(`/api/students/${id}`);
}

export interface CreateStudentInput {
  fullName: string;
  phone: string;
  email: string;
  password: string;
  /** Left out, the join date is today. A centre moving over from another system enters the real one. */
  joinedDate?: string;
  currentLevel?: string;
  /** Required for branch staff; only someone holding the whole organization may leave it out. */
  groupId?: string;
  /** Left out, the next STU-yyyyMM-NNNN is issued. */
  studentNumber?: string;
}

/** Registers a student directly: login account, profile and — with a group — the enrollment and first invoice. */
export function createStudent(input: CreateStudentInput): Promise<Student> {
  return apiFetch<Student>("/api/students", { method: "POST", body: JSON.stringify(input) });
}

/** A missing field means "leave it"; an empty `currentLevel` clears it. */
export interface UpdateStudentInput {
  fullName?: string;
  phone?: string;
  studentNumber?: string;
  joinedDate?: string;
  currentLevel?: string;
}

/** Anything on the profile — `students.edit`. */
export function updateStudent(id: string, input: UpdateStudentInput): Promise<Student> {
  return apiFetch<Student>(`/api/students/${id}`, { method: "PATCH", body: JSON.stringify(input) });
}

/** Name and phone only — `students.contact.edit`, what an operator may correct. */
export function updateStudentContact(id: string, input: Pick<UpdateStudentInput, "fullName" | "phone">): Promise<Student> {
  return apiFetch<Student>(`/api/students/${id}/contact`, { method: "PATCH", body: JSON.stringify(input) });
}

export type ParentRelationship = "Father" | "Mother" | "Guardian" | "Other";

export interface StudentParentLink {
  parentId: string;
  userId: string;
  fullName: string;
  phone: string;
  email: string;
  relationship: ParentRelationship;
}

export function listStudentParents(studentId: string): Promise<StudentParentLink[]> {
  return apiFetch<StudentParentLink[]>(`/api/students/${studentId}/parents`);
}

/** Mode (a): pick an existing PARENT-role account. */
export function linkExistingParent(
  studentId: string,
  input: { userId: string; relationship: ParentRelationship },
): Promise<StudentParentLink> {
  return apiFetch<StudentParentLink>(`/api/students/${studentId}/parents`, { method: "POST", body: JSON.stringify(input) });
}

/** Mode (b): create a new guardian account on the spot. */
export function createAndLinkParent(
  studentId: string,
  input: { fullName: string; phone: string; email: string; password: string; relationship: ParentRelationship },
): Promise<StudentParentLink> {
  return apiFetch<StudentParentLink>(`/api/students/${studentId}/parents`, { method: "POST", body: JSON.stringify(input) });
}

export function removeStudentParent(studentId: string, parentId: string): Promise<void> {
  return apiFetch<void>(`/api/students/${studentId}/parents/${parentId}`, { method: "DELETE" });
}

export interface EnrollmentSummary {
  id: string;
  groupId: string;
  groupName: string;
  status: string;
  enrolledAt: string;
}

export interface StudentOverview {
  student: Student;
  enrollments: EnrollmentSummary[];
  lessonsAttended: number;
  lessonsAbsent: number;
  attendanceRate: number;
  homeworkAssigned: number;
  homeworkSubmitted: number;
  homeworkCompletionRate: number;
  header: StudentHeader;
}

/** What TZ §7 puts in the profile header beyond the student's own fields. */
export interface StudentHeader {
  hasPhoto: boolean;
  /** Changes with each new picture; fetch it from `userPhotoPath(student.userId, photoVersion)`. */
  photoVersion: string | null;
  /** The groups the student is in now — the one joined first at the front. */
  groups: StudentHeaderGroup[];
  parents: { fullName: string; phone: string; relationship: ParentRelationship }[];
}
export interface StudentHeaderGroup {
  groupId: string; groupName: string; branchName: string; courseName: string;
  mainTeacherNames: string[]; supportTeacherNames: string[];
}

export function getStudentOverview(id: string): Promise<StudentOverview> {
  return apiFetch<StudentOverview>(`/api/students/${id}/overview`);
}

export interface TimelineEvent {
  id: string;
  eventType: string;
  payloadJson: string | null;
  occurredAt: string;
}

export function getStudentTimeline(id: string): Promise<TimelineEvent[]> {
  return apiFetch<TimelineEvent[]>(`/api/students/${id}/timeline`);
}

export interface TransferTarget {
  groupId: string;
  groupName: string;
  branchId: string;
  branchName: string;
  courseName: string;
  capacity: number;
  enrolled: number;
  freeSeats: number;
  mainTeacher: string | null;
}

export function getTransferTargets(studentId: string): Promise<TransferTarget[]> {
  return apiFetch<TransferTarget[]>(`/api/students/${studentId}/transfer-targets`);
}

export interface EnrollmentMove {
  studentId: string;
  status: StudentStatus;
  enrollments: EnrollmentSummary[];
}

export function transferStudent(studentId: string, fromGroupId: string, toGroupId: string, reason: string): Promise<EnrollmentMove> {
  return apiFetch<EnrollmentMove>(`/api/students/${studentId}/transfer`, {
    method: "POST",
    body: JSON.stringify({ fromGroupId, toGroupId, reason }),
  });
}

export function freezeStudent(studentId: string, reason: string, resumeOn?: string): Promise<EnrollmentMove> {
  return apiFetch<EnrollmentMove>(`/api/students/${studentId}/freeze`, {
    method: "POST",
    body: JSON.stringify({ reason, resumeOn: resumeOn || null }),
  });
}

export function resumeStudent(studentId: string, groupId?: string): Promise<EnrollmentMove> {
  return apiFetch<EnrollmentMove>(`/api/students/${studentId}/resume`, {
    method: "POST",
    body: JSON.stringify({ groupId: groupId ?? null }),
  });
}

/** Adds a second (or third…) group without ending any enrollment the student already has. */
export function enrollStudent(studentId: string, groupId: string): Promise<EnrollmentMove> {
  return apiFetch<EnrollmentMove>(`/api/students/${studentId}/enrollments`, {
    method: "POST",
    body: JSON.stringify({ groupId }),
  });
}

/** Leaves a group with no destination — unlike a transfer, nothing takes its place. */
export function dropEnrollment(studentId: string, enrollmentId: string, reason: string): Promise<EnrollmentMove> {
  return apiFetch<EnrollmentMove>(`/api/students/${studentId}/enrollments/${enrollmentId}/drop`, {
    method: "POST",
    body: JSON.stringify({ reason }),
  });
}

/** Finishes the course: every open (active or frozen) enrollment completes and the student graduates. */
export function graduateStudent(studentId: string): Promise<EnrollmentMove> {
  return apiFetch<EnrollmentMove>(`/api/students/${studentId}/graduate`, { method: "POST" });
}

/** Ten megabytes — the shared upload ceiling. Above it the document has to be a link. */
export const STUDENT_DOCUMENT_MAX_BYTES = 10 * 1024 * 1024;
export const STUDENT_DOCUMENT_FILE_ACCEPT = ".pdf,.doc,.docx,.png,.jpg,.jpeg,.webp";

export interface StudentDocumentFile {
  fileName: string; contentType: string; size: number;
}

export interface StudentDocument {
  id: string; studentId: string; title: string;
  /** A document is either a link or an uploaded file — exactly one of `url` and `file` is set. */
  url: string | null;
  file: StudentDocumentFile | null;
  description: string | null; uploadedBy: string; createdAt: string;
}

export function listStudentDocuments(studentId: string): Promise<StudentDocument[]> {
  return apiFetch<StudentDocument[]>(`/api/student-documents?studentId=${encodeURIComponent(studentId)}`);
}

export function createStudentDocument(input: { studentId: string; title: string; url: string; description?: string }): Promise<StudentDocument> {
  return apiFetch<StudentDocument>("/api/student-documents", { method: "POST", body: JSON.stringify(input) });
}

export function uploadStudentDocument(input: { studentId: string; title: string; description?: string; file: File }): Promise<StudentDocument> {
  const form = new FormData();
  form.set("studentId", input.studentId);
  form.set("title", input.title);
  if (input.description) form.set("description", input.description);
  form.set("file", input.file);
  return apiFetch<StudentDocument>("/api/student-documents/upload", { method: "POST", body: form });
}

export function deleteStudentDocument(id: string): Promise<void> {
  return apiFetch<void>(`/api/student-documents/${id}`, { method: "DELETE" });
}

/** Authenticated download — an uploaded document is not reachable by a plain <a href>. */
export function fetchStudentDocumentFile(id: string): Promise<Blob> {
  return apiFetchBlob(`/api/student-documents/${id}/file`);
}

export interface StudentAttendanceRow {
  id: string; lessonId: string; groupName: string; topic: string | null;
  startsAt: string; status: "Present" | "Absent" | "Late" | "Excused" | "Online";
}

export interface StudentHomeworkRow {
  id: string; lessonId: string; groupName: string; title: string; deadline: string;
  maxScore: number; submittedAt: string | null; score: number | null; feedback: string | null;
  /** Name of the teacher's worksheet (fetched through the homework id), when the task has one. */
  attachmentName: string | null;
  /** The student's own answer; with `submissionFileName` set it carries a file (fetched through this id). */
  submissionId: string | null; submissionFileName: string | null;
}

export interface StudentExamRow {
  id: string; name: string; groupName: string; date: string;
  listening: number | null; reading: number | null; writing: number | null;
  speaking: number | null; overall: number | null;
  /** Percent 0–100; null when the exam did not grade it. */
  grammar: number | null; vocabulary: number | null;
}

/**
 * One of the eight figures of the Learning Profile. `value` is null while nothing is behind it (never zero); `sampleSize` is
 * how many results/lessons/homework it rests on; `trend` is +1 rising, 0 steady, -1 falling, null when there is too little to tell.
 */
export interface ProfileFigure { value: number | null; sampleSize: number; trend: -1 | 0 | 1 | null }
export interface LearningProfile {
  /** IELTS bands, 0–9. */
  listening: ProfileFigure; reading: ProfileFigure; writing: ProfileFigure; speaking: ProfileFigure;
  /** Percentages, 0–100. */
  grammar: ProfileFigure; vocabulary: ProfileFigure; attendance: ProfileFigure; homeworkCompletion: ProfileFigure;
}
export function getLearningProfile(id: string): Promise<LearningProfile> {
  return apiFetch<LearningProfile>(`/api/students/${id}/learning-profile`);
}

export function getStudentAttendance(id: string): Promise<StudentAttendanceRow[]> {
  return apiFetch<StudentAttendanceRow[]>(`/api/students/${id}/attendance`);
}
export function getStudentHomework(id: string): Promise<StudentHomeworkRow[]> {
  return apiFetch<StudentHomeworkRow[]>(`/api/students/${id}/homework`);
}
export function getStudentExams(id: string): Promise<StudentExamRow[]> {
  return apiFetch<StudentExamRow[]>(`/api/students/${id}/exams`);
}
