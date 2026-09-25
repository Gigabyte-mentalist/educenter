import { apiFetch, ApiError } from "../api-client";

export const WEEKDAYS = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"] as const;
export type Weekday = (typeof WEEKDAYS)[number];

export const WEEKDAY_LABEL: Record<Weekday, string> = {
  Monday: "Dushanba", Tuesday: "Seshanba", Wednesday: "Chorshanba", Thursday: "Payshanba", Friday: "Juma", Saturday: "Shanba", Sunday: "Yakshanba",
};
export const WEEKDAY_SHORT: Record<Weekday, string> = {
  Monday: "Du", Tuesday: "Se", Wednesday: "Chor", Thursday: "Pay", Friday: "Ju", Saturday: "Sha", Sunday: "Yak",
};

/** One recurring slot of the group's week, on the branch's wall clock. */
export interface ScheduleSlot {
  dayOfWeek: Weekday;
  /** "HH:mm" */
  startTime: string;
  durationMinutes: number;
  roomId: string | null;
  roomName: string | null;
  /** Null: the group's main teacher takes it. */
  teacherId: string | null;
  teacherName: string | null;
}

export interface ScheduleTemplate {
  groupId: string;
  /** The branch's time zone — the slot times are read on its clock. */
  timezone: string;
  slots: ScheduleSlot[];
}

export const SLOT_MIN_MINUTES = 15;
export const SLOT_MAX_MINUTES = 360;

export const scheduleTemplateKey = (groupId: string) => ["groups", groupId, "schedule-template"] as const;

export const getScheduleTemplate = (groupId: string) => apiFetch<ScheduleTemplate>(`/api/groups/${groupId}/schedule-template`);

/** Replaces the whole template; the slots of one day must not overlap and each ends the same day. */
export const saveScheduleTemplate = (groupId: string, slots: ScheduleSlot[]) =>
  apiFetch<ScheduleTemplate>(`/api/groups/${groupId}/schedule-template`, {
    method: "PUT",
    body: JSON.stringify({
      slots: slots.map((s) => ({
        dayOfWeek: s.dayOfWeek, startTime: s.startTime, durationMinutes: s.durationMinutes, roomId: s.roomId, teacherId: s.teacherId,
      })),
    }),
  });

export interface GeneratedLesson {
  /** yyyy-MM-dd, the branch's calendar day. */
  date: string;
  startsAt: string;
  endsAt: string;
  roomName: string;
  teacherId: string;
  teacherName: string | null;
}

export interface GenerationConflict {
  date: string;
  type: "ROOM" | "TEACHER" | "GROUP";
  conflictingLessonId: string;
  startsAt: string;
  endsAt: string;
}

export interface GenerationResult {
  /** True for an answer that made nothing: what would be made, and what it would clash with. */
  preview: boolean;
  created: number;
  /** Lessons the group already has at those times — not made twice. */
  skipped: number;
  lessons: GeneratedLesson[];
  conflicts: GenerationConflict[];
  warnings: string[];
}

export interface GenerateInput {
  from: string;
  to: string;
  skipDates: string[];
}

/** Makes the lessons of a period (or, with `preview`, only lists them). A clash refuses the whole run and comes back as `conflicts`;
 * any other refusal (no template, no room, a closed group) throws an ApiError with the server's message. */
export async function generateLessons(
  groupId: string, input: GenerateInput, preview: boolean,
): Promise<{ ok: true; result: GenerationResult } | { ok: false; conflicts: GenerationConflict[] }> {
  try {
    const result = await apiFetch<GenerationResult>(`/api/groups/${groupId}/generate-lessons${preview ? "?preview=true" : ""}`, {
      method: "POST",
      body: JSON.stringify({ from: input.from, to: input.to, skipDates: input.skipDates }),
    });
    return { ok: true, result };
  } catch (err) {
    const body = err instanceof ApiError && err.status === 409 ? (err.body as { conflicts?: GenerationConflict[] } | undefined) : undefined;
    if (body?.conflicts) return { ok: false, conflicts: body.conflicts };
    throw err;
  }
}
