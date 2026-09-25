"use client";

import { useQuery } from "@tanstack/react-query";
import { listRooms } from "@/lib/api/rooms";
import { Select } from "@/components/ui/Field";

/** The rooms of a branch that take lessons (up to 200 — a branch has tens). One cache entry per branch, shared by every picker on the page. */
export function useActiveRooms(branchId: string) {
  return useQuery({
    queryKey: ["rooms", "active", branchId],
    queryFn: () => listRooms({ branchId, size: 200 }),
    enabled: branchId !== "",
  });
}

/** What the lesson being edited is in now — kept selectable even when the list no longer offers it (a room switched off, or one that was only ever a name). */
export interface CurrentRoom {
  id: string | null;
  name: string;
}

/**
 * The rooms of one branch that are in use, with how many each seats, for putting a lesson in one. A room is chosen, never typed: that
 * is what stops "A-12", "A12" and "a-12" being three rooms. Empty (`""`) means "no room chosen" — for a lesson that only names its
 * room it is the room it already has.
 */
export function RoomSelect({
  id, branchId, value, onChange, current, required,
}: {
  id: string;
  branchId: string;
  value: string;
  onChange: (roomId: string) => void;
  current?: CurrentRoom;
  required?: boolean;
}) {
  const items = useActiveRooms(branchId).data?.items ?? [];
  const listed = current?.id ? items.some((room) => room.id === current.id) : false;

  return (
    <Select id={id} required={required} value={value} disabled={branchId === ""} onChange={(e) => onChange(e.target.value)}>
      {current && !current.id && <option value="">{current.name} (xonalar ro‘yxatida yo‘q)</option>}
      {(!current || current.id) && <option value="">{branchId === "" ? "Avval filialni tanlang" : "Tanlang"}</option>}
      {current?.id && !listed && <option value={current.id}>{current.name} (nofaol)</option>}
      {items.map((room) => (
        <option key={room.id} value={room.id}>
          {room.name}{room.capacity ? ` · ${room.capacity} kishilik` : ""}
        </option>
      ))}
    </Select>
  );
}
