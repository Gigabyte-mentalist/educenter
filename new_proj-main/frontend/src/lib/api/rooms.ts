import { apiFetch } from "../api-client";
import type { PageEnvelope } from "@/components/ui/Pagination";
import { buildQuery, type PageParams } from "./query";

export interface Room {
  id: string;
  branchId: string;
  name: string;
  /** How many it seats; null while nobody has said. */
  capacity: number | null;
  equipment: string | null;
  /** A room switched off takes no new lesson; the ones it has stay. */
  isActive: boolean;
  /** Lessons held or planned in it — a room with any cannot be erased, only switched off. */
  lessonCount: number;
}

export interface RoomListParams extends PageParams {
  branchId?: string;
  includeInactive?: boolean;
}

export const ROOM_NAME_MAX = 50;

export function listRooms(params: RoomListParams = {}): Promise<PageEnvelope<Room>> {
  return apiFetch<PageEnvelope<Room>>(`/api/rooms${buildQuery({ ...params })}`);
}

export interface RoomInput {
  name: string;
  capacity: number | null;
  equipment: string;
}

export function createRoom(branchId: string, input: RoomInput): Promise<Room> {
  return apiFetch<Room>("/api/rooms", { method: "POST", body: JSON.stringify({ branchId, ...input }) });
}

/** Only what is sent changes; `capacity: 0` clears the capacity and an empty `equipment` clears that. */
export function updateRoom(id: string, input: Partial<{ name: string; capacity: number; equipment: string; isActive: boolean }>): Promise<Room> {
  return apiFetch<Room>(`/api/rooms/${id}`, { method: "PATCH", body: JSON.stringify(input) });
}

/** Only a room nothing was ever held in; otherwise a 409 that says to switch it off instead. */
export function deleteRoom(id: string): Promise<void> {
  return apiFetch<void>(`/api/rooms/${id}`, { method: "DELETE" });
}

export interface RoomBooking {
  lessonId: string;
  startsAt: string;
  endsAt: string;
  groupId: string;
  groupName: string;
  teacherId: string;
  teacherName: string | null;
  topic: string | null;
}

export interface RoomAvailability {
  roomId: string;
  name: string;
  date: string;
  timezone: string;
  busy: RoomBooking[];
}

/** The lessons that hold the room on one calendar day (`yyyy-MM-dd`, the branch's own day). */
export function getRoomAvailability(id: string, date: string): Promise<RoomAvailability> {
  return apiFetch<RoomAvailability>(`/api/rooms/${id}/availability${buildQuery({ date })}`);
}
