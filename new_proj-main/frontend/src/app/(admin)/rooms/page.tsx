"use client";

import { useState } from "react";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { DoorOpen, Pencil, Plus, Power, Search, Trash2 } from "lucide-react";
import { listBranches } from "@/lib/api/branches";
import {
  createRoom, deleteRoom, getRoomAvailability, listRooms, ROOM_NAME_MAX, updateRoom, type Room,
} from "@/lib/api/rooms";
import { ApiError } from "@/lib/api-client";
import { formatTime } from "@/lib/format-date";
import { usePermissions } from "@/lib/use-permissions";
import { useDebouncedValue } from "@/lib/use-debounced-value";
import { Alert } from "@/components/ui/Alert";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { ConfirmDialog } from "@/components/ui/ConfirmDialog";
import { Drawer } from "@/components/ui/Drawer";
import { EmptyState } from "@/components/ui/EmptyState";
import { Input, Label, Select, Textarea } from "@/components/ui/Field";
import { PageHeader } from "@/components/ui/PageHeader";
import { Pagination } from "@/components/ui/Pagination";
import { TableSkeleton } from "@/components/ui/Skeleton";
import { Table, TBody, TD, TH, THead, TRow } from "@/components/ui/Table";

const PAGE_SIZE = 25;
const errorText = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

export default function RoomsPage() {
  const queryClient = useQueryClient();
  const { can } = usePermissions();
  const canManage = can("rooms.manage");

  const [page, setPage] = useState(1);
  const [searchInput, setSearchInput] = useState("");
  const search = useDebouncedValue(searchInput);
  const [branchId, setBranchId] = useState("");
  const [showInactive, setShowInactive] = useState(true);

  const branches = useQuery({ queryKey: ["branches"], queryFn: listBranches });
  const rooms = useQuery({
    queryKey: ["rooms", "list", { page, search, branchId, showInactive }],
    queryFn: () => listRooms({ page, size: PAGE_SIZE, search, branchId: branchId || undefined, includeInactive: showInactive }),
    placeholderData: keepPreviousData,
  });
  const branchName = new Map(branches.data?.map((b) => [b.id, b.name]));
  const data = rooms.data;

  // One drawer serves both create and edit; `editing` tells them apart, `open` says whether it is shown.
  const [open, setOpen] = useState(false);
  const [editing, setEditing] = useState<Room | null>(null);
  const [pendingDelete, setPendingDelete] = useState<Room | null>(null);

  const refresh = () => queryClient.invalidateQueries({ queryKey: ["rooms"] });
  const toggle = useMutation({ mutationFn: (room: Room) => updateRoom(room.id, { isActive: !room.isActive }), onSuccess: refresh });
  const remove = useMutation({
    mutationFn: (room: Room) => deleteRoom(room.id),
    onSuccess: () => { setPendingDelete(null); refresh(); },
  });

  return (
    <main className="p-4 sm:p-6 lg:p-8">
      <PageHeader
        title="Xonalar"
        description="Filial xonalari: sig‘imi va jihozi. Darsga xona shu ro‘yxatdan tanlanadi, band vaqt avtomatik tekshiriladi."
        actions={canManage && (
          <Button onClick={() => { setEditing(null); setOpen(true); }}>
            <Plus className="size-4" aria-hidden />
            <span className="hidden sm:inline">Yangi xona</span>
            <span className="sm:hidden">Qo‘shish</span>
          </Button>
        )}
      />

      <div className="mb-4 flex flex-wrap items-center gap-2">
        <div className="relative w-full max-w-xs">
          <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-ink-400" aria-hidden />
          <Input
            value={searchInput} onChange={(e) => { setSearchInput(e.target.value); setPage(1); }}
            placeholder="Nom yoki jihoz bo‘yicha" aria-label="Xonalarni qidirish" className="pl-9"
          />
        </div>
        {(branches.data?.length ?? 0) > 1 && (
          <Select value={branchId} onChange={(e) => { setBranchId(e.target.value); setPage(1); }} aria-label="Filial" className="w-52">
            <option value="">Barcha filiallar</option>
            {branches.data?.map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}
          </Select>
        )}
        <label className="flex items-center gap-2 text-[13px] text-ink-700">
          <input type="checkbox" checked={showInactive} onChange={(e) => { setShowInactive(e.target.checked); setPage(1); }} className="size-4 accent-brand-solid" />
          Nofaollarni ham ko‘rsatish
        </label>
      </div>

      {rooms.isError && <Alert>{errorText(rooms.error, "Xonalarni yuklab bo‘lmadi.")}</Alert>}
      {toggle.isError && <Alert>{errorText(toggle.error, "Holatni o‘zgartirib bo‘lmadi.")}</Alert>}

      {rooms.isLoading && (
        <div className="rounded-xl border border-border bg-surface"><TableSkeleton rows={5} cols={5} /></div>
      )}

      {data && data.items.length === 0 && (
        <div className="rounded-xl border border-border bg-surface">
          <EmptyState
            icon={DoorOpen}
            title={search ? "Hech narsa topilmadi" : "Hali xona qo‘shilmagan"}
            description={!search && canManage ? "Xonalarni qo‘shing — jadvalda ular ro‘yxatdan tanlanadi va band vaqt to‘qnashuvi xona bo‘yicha tekshiriladi." : undefined}
          />
        </div>
      )}

      {data && data.items.length > 0 && (
        <div className={`overflow-hidden rounded-xl border border-border bg-surface ${rooms.isPlaceholderData ? "opacity-60" : ""}`}>
          <Table bare>
            <THead>
              <tr>
                <TH>Xona</TH>
                <TH className="hidden md:table-cell">Filial</TH>
                <TH>Sig‘im</TH>
                <TH className="hidden lg:table-cell">Jihoz</TH>
                <TH className="hidden sm:table-cell">Darslar</TH>
                <TH>Holat</TH>
                <TH className="text-right">Amallar</TH>
              </tr>
            </THead>
            <TBody>
              {data.items.map((room) => (
                <TRow key={room.id} className={room.isActive ? "" : "text-ink-400"}>
                  <TD className="font-medium text-ink-900">{room.name}</TD>
                  <TD className="hidden md:table-cell">{branchName.get(room.branchId) ?? "—"}</TD>
                  <TD className="font-tabular">{room.capacity ?? "—"}</TD>
                  <TD className="hidden max-w-xs truncate lg:table-cell">{room.equipment ?? "—"}</TD>
                  <TD className="hidden font-tabular sm:table-cell">{room.lessonCount}</TD>
                  <TD>{room.isActive ? <Badge tone="success">Faol</Badge> : <Badge>Nofaol</Badge>}</TD>
                  <TD>
                    <div className="flex items-center justify-end gap-1">
                      <Button variant="ghost" size="icon" title={canManage ? "Tahrirlash va band vaqtlar" : "Band vaqtlar"} aria-label={`${room.name} xonasi`} onClick={() => { setEditing(room); setOpen(true); }}>
                        <Pencil className="size-4" aria-hidden />
                      </Button>
                      {canManage && (
                        <>
                          <Button
                            variant="ghost" size="icon" loading={toggle.isPending && toggle.variables?.id === room.id}
                            title={room.isActive ? "Nofaol qilish" : "Faollashtirish"}
                            aria-label={`${room.name} xonasini ${room.isActive ? "nofaol qilish" : "faollashtirish"}`}
                            onClick={() => toggle.mutate(room)}
                          >
                            <Power className="size-4" aria-hidden />
                          </Button>
                          <Button
                            variant="ghost" size="icon" title="O‘chirish" aria-label={`${room.name} xonasini o‘chirish`}
                            className="text-danger-500 hover:bg-danger-50 hover:text-danger-600"
                            onClick={() => { remove.reset(); setPendingDelete(room); }}
                          >
                            <Trash2 className="size-4" aria-hidden />
                          </Button>
                        </>
                      )}
                    </div>
                  </TD>
                </TRow>
              ))}
            </TBody>
          </Table>
          <Pagination page={data.pageNumber} pageCount={data.pageCount} total={data.total} pageSize={data.pageSize} onChange={setPage} />
        </div>
      )}

      <Drawer
        open={open} onClose={() => setOpen(false)}
        title={editing ? editing.name : "Yangi xona"}
        description={editing ? (canManage ? "Xona ma‘lumoti va shu xonaning band vaqtlari." : "Xonaning band vaqtlari.") : "Xona nomi filial ichida takrorlanmaydi."}
      >
        {open && (
          <RoomPanel
            key={editing?.id ?? "new"} room={editing} canManage={canManage} defaultBranchId={branchId || (branches.data?.length === 1 ? branches.data[0].id : "")}
            branches={branches.data ?? []} onSaved={() => { setOpen(false); refresh(); }}
          />
        )}
      </Drawer>

      <ConfirmDialog
        open={pendingDelete !== null}
        title={`“${pendingDelete?.name ?? ""}” xonasini o‘chirish`}
        description="Xona ro‘yxatdan butunlay olib tashlanadi. Darsi bo‘lgan xonani o‘chirib bo‘lmaydi — uni nofaol qiling."
        loading={remove.isPending}
        error={remove.isError ? errorText(remove.error, "O‘chirib bo‘lmadi.") : null}
        onConfirm={() => pendingDelete && remove.mutate(pendingDelete)}
        onClose={() => setPendingDelete(null)}
      />
    </main>
  );
}

function RoomPanel({ room, canManage, defaultBranchId, branches, onSaved }: {
  room: Room | null;
  canManage: boolean;
  defaultBranchId: string;
  branches: { id: string; name: string }[];
  onSaved: () => void;
}) {
  const [name, setName] = useState(room?.name ?? "");
  const [branchId, setBranchId] = useState(room?.branchId ?? defaultBranchId);
  const [capacity, setCapacity] = useState(room?.capacity ? String(room.capacity) : "");
  const [equipment, setEquipment] = useState(room?.equipment ?? "");

  const save = useMutation({
    mutationFn: () => {
      const seats = capacity.trim() === "" ? null : Number(capacity);
      // On an edit "no capacity" is 0 ("clear"); on create it is simply left out.
      return room
        ? updateRoom(room.id, { name, capacity: seats ?? 0, equipment })
        : createRoom(branchId, { name, capacity: seats, equipment });
    },
    onSuccess: onSaved,
  });

  const seats = capacity.trim() === "" ? null : Number(capacity);
  const invalidSeats = seats !== null && (!Number.isInteger(seats) || seats < 1 || seats > 1000);

  return (
    <div className="space-y-6 p-5">
      {canManage && (
        <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); save.mutate(); }}>
          {!room && (
            <div>
              <Label htmlFor="room-branch">Filial</Label>
              <Select id="room-branch" required value={branchId} onChange={(e) => setBranchId(e.target.value)}>
                <option value="">Tanlang</option>
                {branches.map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}
              </Select>
            </div>
          )}
          <div>
            <Label htmlFor="room-name">Nomi</Label>
            <Input id="room-name" required maxLength={ROOM_NAME_MAX} value={name} onChange={(e) => setName(e.target.value)} placeholder="A101" />
          </div>
          <div>
            <Label htmlFor="room-capacity">Sig‘imi (kishi)</Label>
            <Input id="room-capacity" type="number" inputMode="numeric" min={1} max={1000} value={capacity} onChange={(e) => setCapacity(e.target.value)} placeholder="Noma‘lum" />
            <p className="mt-1.5 text-[12.5px] text-ink-500">Guruh sig‘imidan kichik bo‘lsa, darsga qo‘yishda ogohlantiriladi (xato emas).</p>
          </div>
          <div>
            <Label htmlFor="room-equipment">Jihozi</Label>
            <Textarea id="room-equipment" maxLength={500} value={equipment} onChange={(e) => setEquipment(e.target.value)} placeholder="Proyektor, doska, karnay…" />
          </div>
          {save.isError && <Alert>{errorText(save.error, "Saqlab bo‘lmadi.")}</Alert>}
          <Button type="submit" loading={save.isPending} disabled={!name.trim() || invalidSeats || (!room && !branchId)} className="w-full">
            {room ? "Saqlash" : "Xonani qo‘shish"}
          </Button>
        </form>
      )}

      {room && <Availability room={room} />}
    </div>
  );
}

/** Who holds the room on a day — the office's answer to "is A101 free on Thursday afternoon?". */
function Availability({ room }: { room: Room }) {
  const [date, setDate] = useState("");
  const availability = useQuery({
    queryKey: ["rooms", "availability", room.id, date],
    queryFn: () => getRoomAvailability(room.id, date),
  });
  const busy = availability.data?.busy ?? [];

  return (
    <section className="space-y-3 border-t border-border pt-5">
      <h3 className="text-[13px] font-semibold text-ink-800">Band vaqtlar</h3>
      <div>
        <Label htmlFor="room-date">Kun</Label>
        <Input id="room-date" type="date" value={date || availability.data?.date || ""} onChange={(e) => setDate(e.target.value)} className="w-48" />
      </div>
      {availability.isError && <Alert>{errorText(availability.error, "Yuklab bo‘lmadi.")}</Alert>}
      {availability.data && busy.length === 0 && <p className="text-[13px] text-ink-500">Bu kuni xona bo‘sh.</p>}
      {busy.length > 0 && (
        <ul className="divide-y divide-border rounded-lg border border-border">
          {busy.map((b) => (
            <li key={b.lessonId} className="px-3 py-2 text-[13px]">
              <span className="font-tabular font-medium text-brand-700">{formatTime(b.startsAt)}–{formatTime(b.endsAt)}</span>
              <span className="ml-2 text-ink-800">{b.groupName}</span>
              <span className="block text-[12px] text-ink-500">{b.teacherName ?? "—"}{b.topic ? ` · ${b.topic}` : ""}</span>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
