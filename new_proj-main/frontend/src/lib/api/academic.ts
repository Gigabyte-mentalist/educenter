import { apiFetch, ApiError } from "../api-client";

export interface Course {
  id: string;
  name: string;
  levelCategory: string;
  /** Monthly tuition in so'm; 0 = not billed. */
  monthlyFee: number;
}

export interface CreateCourseInput {
  name: string;
  levelCategory: string;
  monthlyFee?: number;
}

export function listCourses(): Promise<Course[]> {
  return apiFetch<Course[]>("/api/courses");
}

export function createCourse(input: CreateCourseInput): Promise<Course> {
  return apiFetch<Course>("/api/courses", { method: "POST", body: JSON.stringify(input) });
}

export function updateCourse(id: string, input: { name: string; levelCategory: string; monthlyFee: number }): Promise<Course> {
  return apiFetch<Course>(`/api/courses/${id}`, { method: "PATCH", body: JSON.stringify(input) });
}

/** Only a course no group is on (closed groups count); otherwise a 409 that says how many. */
export function deleteCourse(id: string): Promise<void> {
  return apiFetch<void>(`/api/courses/${id}`, { method: "DELETE" });
}

export interface Group {
  id: string;
  branchId: string;
  courseId: string;
  name: string;
  capacity: number;
  status: "Forming" | "Active" | "Completed" | "Cancelled";
  staff: { userId: string; role: "MainTeacher" | "SupportTeacher" }[];
  /** Students actively enrolled right now. */
  studentCount: number;
}

export interface GroupStudentResult {
  studentId: string;
  fullName: string;
  studentNumber: string;
  /** 0-100; null while nothing has been marked for this student. */
  attendanceRate: number | null;
  absences: number;
  homeworkSubmitted: number;
  /** Mean exam overall; null while the student has no result. */
  averageExamScore: number | null;
}

export interface GroupOverview {
  id: string;
  name: string;
  courseName: string;
  branchId: string;
  branchName: string;
  status: Group["status"];
  capacity: number;
  studentCount: number;
  staff: { userId: string; fullName: string; role: "MainTeacher" | "SupportTeacher" }[];
  upcomingLessons: { id: string; startsAt: string; endsAt: string; roomName: string; topic: string | null }[];
  /** Null for a student or parent: they may open the group but not read their classmates' results. */
  results: {
    attendanceRate: number;
    homeworkCompletionRate: number;
    averageExamScore: number | null;
    lessonsHeld: number;
    homeworkAssigned: number;
    examsHeld: number;
    students: GroupStudentResult[];
  } | null;
}

export function getGroupOverview(groupId: string): Promise<GroupOverview> {
  return apiFetch<GroupOverview>(`/api/groups/${groupId}/overview`);
}

export interface CreateGroupInput {
  branchId: string;
  courseId: string;
  name: string;
  capacity: number;
}

export function listGroups(): Promise<Group[]> {
  return apiFetch<Group[]>("/api/groups");
}

export function createGroup(input: CreateGroupInput): Promise<Group> {
  return apiFetch<Group>("/api/groups", { method: "POST", body: JSON.stringify(input) });
}

export function assignGroupStaff(groupId: string, userId: string, role: "MainTeacher" | "SupportTeacher"): Promise<void> {
  return apiFetch<void>(`/api/groups/${groupId}/staff`, { method: "POST", body: JSON.stringify({ userId, role }) });
}

/** Only what is sent changes. The server refuses a capacity below the students actively enrolled, and any edit of a closed group. */
export function updateGroup(groupId: string, input: { name?: string; capacity?: number }): Promise<Group> {
  return apiFetch<Group>(`/api/groups/${groupId}`, { method: "PATCH", body: JSON.stringify(input) });
}

/** Forming → Active → Completed, or Forming/Active → Cancelled. Closing also ends the students' enrollments. */
export function setGroupStatus(groupId: string, status: "Active" | "Completed" | "Cancelled"): Promise<Group> {
  return apiFetch<Group>(`/api/groups/${groupId}/status`, { method: "POST", body: JSON.stringify({ status }) });
}

export function removeGroupStaff(groupId: string, userId: string): Promise<void> {
  return apiFetch<void>(`/api/groups/${groupId}/staff/${userId}`, { method: "DELETE" });
}

export function deleteGroup(groupId: string): Promise<void> {
  return apiFetch<void>(`/api/groups/${groupId}`, { method: "DELETE" });
}

/** The body of the 409 that refuses deleting a group that still has history. */
export interface GroupDeleteRefusal {
  blockers: string[];
  /** False once the group is already closed — there is nothing left to cancel. */
  canCancel: boolean;
}

export function groupDeleteRefusal(error: unknown): GroupDeleteRefusal | null {
  if (!(error instanceof ApiError) || error.status !== 409) return null;
  const body = error.body as Partial<GroupDeleteRefusal> | undefined;
  return Array.isArray(body?.blockers) ? { blockers: body.blockers, canCancel: body.canCancel === true } : null;
}

export interface GroupStudent {
  studentId: string;
  fullName: string;
  studentNumber: string;
}

export function listGroupStudents(groupId: string): Promise<GroupStudent[]> {
  return apiFetch<GroupStudent[]>(`/api/groups/${groupId}/students`);
}

export interface Lesson {
  id: string;
  groupId: string;
  teacherId: string;
  roomName: string;
  startsAt: string;
  endsAt: string;
  topic: string | null;
  /** Only the schedule list carries it; a single-lesson response (create, replace) leaves it null. */
  teacherName?: string | null;
  /** A cancelled lesson stays on the schedule, marked, but is not held: it counts nowhere and takes no attendance. */
  status: "Scheduled" | "Cancelled";
  cancelReason: string | null;
  /** The top of the lesson's score scale; null while the lesson is not scored (then attendance shows no score column). */
  maxLessonScore?: number | null;
  /** The room it is held in; null for a lesson that only ever named its room (then `roomName` is all there is). */
  roomId?: string | null;
  /** Only on the response to creating or changing a lesson: worth a second look, not a refusal (the room seats fewer than the group). */
  warnings?: string[] | null;
}

export interface CreateLessonInput {
  branchId: string;
  groupId: string;
  teacherId: string;
  /** The room by id (S07.03); `roomName` alone still works and leaves the lesson tied to no room. */
  roomId?: string;
  roomName?: string;
  startsAt: string;
  endsAt: string;
  topic?: string;
}

export interface ScheduleConflict {
  type: "ROOM" | "TEACHER" | "GROUP";
  conflictingLessonId: string;
  startsAt: string;
  endsAt: string;
}

export function listLessons(query: { from?: string; to?: string; branchId?: string } = {}): Promise<Lesson[]> {
  const params = new URLSearchParams();
  if (query.from) params.set("from", query.from);
  if (query.to) params.set("to", query.to);
  if (query.branchId) params.set("branchId", query.branchId);
  const qs = params.toString();
  return apiFetch<Lesson[]>(`/api/schedule${qs ? `?${qs}` : ""}`);
}

export interface Substitute {
  userId: string;
  fullName: string;
  email: string;
  isMainTeacher: boolean;
}

export function listSubstitutes(lessonId: string): Promise<Substitute[]> {
  return apiFetch<Substitute[]>(`/api/schedule/lessons/${lessonId}/substitutes`);
}

export async function replaceTeacher(
  lessonId: string, teacherId: string, reason: string,
): Promise<{ ok: true; lesson: Lesson } | { ok: false; conflicts: ScheduleConflict[] }> {
  try {
    const lesson = await apiFetch<Lesson>(`/api/schedule/lessons/${lessonId}/replace-teacher`, {
      method: "POST",
      body: JSON.stringify({ teacherId, reason }),
    });
    return { ok: true, lesson };
  } catch (err) {
    if (err instanceof ApiError && err.status === 409) {
      const body = err.body as { conflicts?: ScheduleConflict[] } | undefined;
      return { ok: false, conflicts: body?.conflicts ?? [] };
    }
    throw err;
  }
}

/** Only what is sent changes. Datetimes are ISO strings. Like create, a clash comes back as `conflicts` rather than a throw;
 * any other refusal (a cancelled or finished lesson) throws an ApiError with the server's message. */
export async function updateLesson(
  lessonId: string,
  input: { startsAt?: string; endsAt?: string; roomId?: string; roomName?: string; teacherId?: string; topic?: string },
): Promise<{ ok: true; lesson: Lesson } | { ok: false; conflicts: ScheduleConflict[] }> {
  try {
    const lesson = await apiFetch<Lesson>(`/api/schedule/lessons/${lessonId}`, { method: "PATCH", body: JSON.stringify(input) });
    return { ok: true, lesson };
  } catch (err) {
    const body = err instanceof ApiError && err.status === 409 ? (err.body as { conflicts?: ScheduleConflict[] } | undefined) : undefined;
    if (body?.conflicts) return { ok: false, conflicts: body.conflicts };
    throw err;
  }
}

export function cancelLesson(lessonId: string, reason: string): Promise<Lesson> {
  return apiFetch<Lesson>(`/api/schedule/lessons/${lessonId}/cancel`, { method: "POST", body: JSON.stringify({ reason }) });
}

/** All or nothing; lessons already cancelled are skipped and counted. */
export function bulkCancelLessons(lessonIds: string[], reason: string): Promise<{ cancelled: number; alreadyCancelled: number }> {
  return apiFetch(`/api/schedule/lessons/bulk-cancel`, { method: "POST", body: JSON.stringify({ lessonIds, reason }) });
}

/** Only a lesson nothing was recorded against; otherwise a 409 with `blockers`, and cancelling is the way. */
export function deleteLesson(lessonId: string): Promise<void> {
  return apiFetch<void>(`/api/schedule/lessons/${lessonId}`, { method: "DELETE" });
}

export async function createLesson(
  input: CreateLessonInput,
): Promise<{ ok: true; lesson: Lesson } | { ok: false; conflicts: ScheduleConflict[] }> {
  try {
    const lesson = await apiFetch<Lesson>("/api/schedule/lessons", {
      method: "POST",
      body: JSON.stringify(input),
    });
    return { ok: true, lesson };
  } catch (err) {
    if (err instanceof ApiError && err.status === 409) {
      const body = err.body as { conflicts?: ScheduleConflict[] } | undefined;
      return { ok: false, conflicts: body?.conflicts ?? [] };
    }
    throw err;
  }
}
