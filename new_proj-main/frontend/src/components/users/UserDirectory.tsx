"use client";

import { useState } from "react";
import Link from "next/link";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Plus, Search, Trash2, UserCheck, UserX, Users2 } from "lucide-react";
import { listBranches } from "@/lib/api/branches";
import {
  activateUser, createUser, deactivateUser, deleteUser, listUsers,
  type AppUser, type UserAudience,
} from "@/lib/api/users";
import { roleLabel } from "@/lib/auth-storage";
import { ApiError } from "@/lib/api-client";
import { PageHeader } from "@/components/ui/PageHeader";
import { Button } from "@/components/ui/Button";
import { Badge } from "@/components/ui/Badge";
import { Avatar } from "@/components/ui/Avatar";
import { Table, THead, TBody, TRow, TH, TD } from "@/components/ui/Table";
import { TableSkeleton } from "@/components/ui/Skeleton";
import { EmptyState } from "@/components/ui/EmptyState";
import { Alert } from "@/components/ui/Alert";
import { Drawer } from "@/components/ui/Drawer";
import { ConfirmDialog } from "@/components/ui/ConfirmDialog";
import { Pagination } from "@/components/ui/Pagination";
import { Input, Label, Select } from "@/components/ui/Field";

const STATUS_TONE = { Active: "success", Inactive: "neutral", Suspended: "danger" } as const;
const STATUS_LABEL = { Active: "Faol", Inactive: "Nofaol", Suspended: "To'xtatilgan" } as const;
const PAGE_SIZE = 25;

const errorText = (error: unknown, fallback: string) =>
  error instanceof ApiError ? error.message : fallback;

/**
 * The staff screen and the accounts screen are the same table over a different slice of the same endpoint:
 * one lists the people who work here, the other every login in the organization. They were a single list
 * until a search for a colleague meant scrolling past every student in the school.
 */
export function UserDirectory({
  audience,
  title,
  description,
  roleOptions,
  defaultRole,
}: {
  audience: UserAudience;
  title: string;
  description: string;
  roleOptions: readonly string[];
  defaultRole: string;
}) {
  const queryClient = useQueryClient();

  const [page, setPage] = useState(1);
  const [search, setSearch] = useState("");
  const [roleFilter, setRoleFilter] = useState("");
  const [statusFilter, setStatusFilter] = useState<AppUser["status"] | "">("");

  // The query key carries every filter, so changing one refetches instead of showing the previous page's rows.
  const usersQuery = useQuery({
    queryKey: ["users", audience, page, search, roleFilter, statusFilter],
    queryFn: () =>
      listUsers({
        audience,
        page,
        size: PAGE_SIZE,
        search: search || undefined,
        role: roleFilter || undefined,
        status: statusFilter || undefined,
      }),
  });
  const branchesQuery = useQuery({ queryKey: ["branches"], queryFn: listBranches });

  // A student is registered from /students, where a group and a profile are created alongside the login — this
  // screen still lists students (they can sign in), it just does not create them.
  const creatableRoleOptions = roleOptions.filter((r) => r !== "STUDENT");

  const [drawerOpen, setDrawerOpen] = useState(false);
  const [fullName, setFullName] = useState("");
  const [email, setEmail] = useState("");
  const [phone, setPhone] = useState("");
  const [password, setPassword] = useState("");
  const [roleCode, setRoleCode] = useState<string>(defaultRole === "STUDENT" ? (creatableRoleOptions[0] ?? defaultRole) : defaultRole);
  const [branchId, setBranchId] = useState("");

  const [pendingDelete, setPendingDelete] = useState<AppUser | null>(null);

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ["users"] });

  const createMutation = useMutation({
    mutationFn: () =>
      createUser({ fullName, email, phone, password, roleCode, branchIds: branchId ? [branchId] : [] }),
    onSuccess: () => {
      setFullName("");
      setEmail("");
      setPhone("");
      setPassword("");
      setDrawerOpen(false);
      invalidate();
    },
  });

  const statusMutation = useMutation({
    mutationFn: ({ id, next }: { id: string; next: "activate" | "deactivate" }) =>
      next === "activate" ? activateUser(id) : deactivateUser(id),
    onSuccess: invalidate,
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => deleteUser(id),
    onSuccess: () => {
      setPendingDelete(null);
      invalidate();
    },
  });

  const branchNameById = new Map(branchesQuery.data?.map((b) => [b.id, b.name]));
  const users = usersQuery.data?.items ?? [];

  // Any filter change puts the caller back on page 1 — page 7 of the old list is meaningless in the new one.
  const resetTo = (apply: () => void) => {
    apply();
    setPage(1);
  };

  return (
    <main className="p-4 sm:p-6 lg:p-8">
      <PageHeader
        title={title}
        description={description}
        actions={
          <Button onClick={() => setDrawerOpen(true)}>
            <Plus className="size-4" aria-hidden />
            <span className="hidden sm:inline">Yangi foydalanuvchi</span>
            <span className="sm:hidden">Qo&apos;shish</span>
          </Button>
        }
      />

      <div className="mb-4 flex flex-col gap-2 sm:flex-row sm:items-center">
        <div className="relative flex-1">
          <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-ink-400" aria-hidden />
          <Input
            value={search}
            onChange={(e) => resetTo(() => setSearch(e.target.value))}
            placeholder="Ism, email yoki telefon bo'yicha qidirish"
            className="pl-9"
            aria-label="Qidirish"
          />
        </div>
        <Select
          value={roleFilter}
          onChange={(e) => resetTo(() => setRoleFilter(e.target.value))}
          className="sm:w-48"
          aria-label="Rol bo'yicha filtr"
        >
          <option value="">Barcha rollar</option>
          {roleOptions.map((code) => (
            <option key={code} value={code}>
              {roleLabel(code)}
            </option>
          ))}
        </Select>
        <Select
          value={statusFilter}
          onChange={(e) => resetTo(() => setStatusFilter(e.target.value as AppUser["status"] | ""))}
          className="sm:w-40"
          aria-label="Holat bo'yicha filtr"
        >
          <option value="">Barcha holatlar</option>
          <option value="Active">Faol</option>
          <option value="Inactive">Nofaol</option>
          <option value="Suspended">To&apos;xtatilgan</option>
        </Select>
      </div>

      {usersQuery.isError && <Alert>{errorText(usersQuery.error, "Yuklab bo'lmadi.")}</Alert>}
      {statusMutation.isError && <Alert>{errorText(statusMutation.error, "Holatni o'zgartirib bo'lmadi.")}</Alert>}

      {usersQuery.isLoading && (
        <div className="rounded-xl border border-border bg-surface">
          <TableSkeleton rows={6} cols={5} />
        </div>
      )}

      {usersQuery.data && users.length === 0 && (
        <div className="rounded-xl border border-border bg-surface">
          <EmptyState
            icon={Users2}
            title={search || roleFilter || statusFilter ? "Hech narsa topilmadi" : "Hali foydalanuvchi yo'q"}
            description={
              search || roleFilter || statusFilter
                ? "Qidiruv yoki filtrni o'zgartirib ko'ring."
                : "Birinchi foydalanuvchini qo'shing."
            }
          />
        </div>
      )}

      {usersQuery.data && users.length > 0 && (
        <div className="overflow-hidden rounded-xl border border-border bg-surface">
          {/* The table scrolls sideways rather than squeezing columns: on a phone the name and the actions
              must stay readable, and a wrapped phone number is worse than a swipe. */}
          <Table bare>
              <THead>
                <tr>
                  <TH>Foydalanuvchi</TH>
                  <TH className="hidden md:table-cell">Telefon</TH>
                  <TH>Rollar</TH>
                  <TH className="hidden lg:table-cell">Filial</TH>
                  <TH>Holat</TH>
                  <TH className="text-right">Amallar</TH>
                </tr>
              </THead>
              <TBody>
                {users.map((user) => (
                  <TRow key={user.id}>
                    <TD>
                      <Link href={`/users/${user.id}?from=${audience}`} className="flex items-center gap-2.5 hover:underline">
                        <Avatar name={user.fullName} />
                        <div className="min-w-0">
                          <p className="truncate font-medium text-ink-900">{user.fullName}</p>
                          <p className="truncate text-[12.5px] text-ink-500">{user.email}</p>
                        </div>
                      </Link>
                    </TD>
                    <TD className="hidden font-mono text-[12.5px] md:table-cell">{user.phone}</TD>
                    <TD>
                      <div className="flex flex-wrap gap-1">
                        {user.roles.map((r) => (
                          <Badge key={r} tone="brand">
                            {roleLabel(r)}
                          </Badge>
                        ))}
                      </div>
                    </TD>
                    <TD className="hidden text-ink-600 lg:table-cell">
                      {user.branchIds.map((id) => branchNameById.get(id) ?? id).join(", ") || "—"}
                    </TD>
                    <TD>
                      <Badge tone={STATUS_TONE[user.status]} dot>
                        {STATUS_LABEL[user.status]}
                      </Badge>
                    </TD>
                    <TD>
                      <div className="flex items-center justify-end gap-1">
                        {user.status === "Active" ? (
                          <Button
                            variant="ghost"
                            size="icon"
                            title="Bloklash"
                            aria-label={`${user.fullName} hisobini bloklash`}
                            onClick={() => statusMutation.mutate({ id: user.id, next: "deactivate" })}
                          >
                            <UserX className="size-4" aria-hidden />
                          </Button>
                        ) : (
                          <Button
                            variant="ghost"
                            size="icon"
                            title="Faollashtirish"
                            aria-label={`${user.fullName} hisobini faollashtirish`}
                            onClick={() => statusMutation.mutate({ id: user.id, next: "activate" })}
                          >
                            <UserCheck className="size-4" aria-hidden />
                          </Button>
                        )}
                        <Button
                          variant="ghost"
                          size="icon"
                          title="O'chirish"
                          aria-label={`${user.fullName} hisobini o'chirish`}
                          className="text-danger-500 hover:bg-danger-50 hover:text-danger-600"
                          onClick={() => {
                            deleteMutation.reset();
                            setPendingDelete(user);
                          }}
                        >
                          <Trash2 className="size-4" aria-hidden />
                        </Button>
                      </div>
                    </TD>
                  </TRow>
                ))}
              </TBody>
          </Table>
          <Pagination
            page={usersQuery.data.pageNumber}
            pageCount={usersQuery.data.pageCount}
            total={usersQuery.data.total}
            pageSize={usersQuery.data.pageSize}
            onChange={setPage}
          />
        </div>
      )}

      <ConfirmDialog
        open={pendingDelete !== null}
        title={`${pendingDelete?.fullName ?? ""} hisobini o'chirish`}
        description={
          <>
            Hisob butunlay o&apos;chiriladi va qaytarib bo&apos;lmaydi. Agar bu odamning tarixi (darslar,
            to&apos;lovlar, xabarlar) saqlanishi kerak bo&apos;lsa, o&apos;chirish o&apos;rniga{" "}
            <strong>bloklang</strong>.
          </>
        }
        loading={deleteMutation.isPending}
        error={deleteMutation.isError ? errorText(deleteMutation.error, "O'chirib bo'lmadi.") : null}
        onConfirm={() => pendingDelete && deleteMutation.mutate(pendingDelete.id)}
        onClose={() => setPendingDelete(null)}
      />

      <Drawer
        open={drawerOpen}
        onClose={() => setDrawerOpen(false)}
        title="Yangi foydalanuvchi"
        description="Xodim yoki foydalanuvchi ma'lumotlarini kiriting."
      >
        <form
          className="space-y-4"
          onSubmit={(e) => {
            e.preventDefault();
            createMutation.mutate();
          }}
        >
          <div>
            <Label htmlFor="user-name">F.I.O</Label>
            <Input id="user-name" required value={fullName} onChange={(e) => setFullName(e.target.value)} />
          </div>
          <div>
            <Label htmlFor="user-email">Email</Label>
            <Input id="user-email" type="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
          </div>
          <div>
            <Label htmlFor="user-phone">Telefon</Label>
            <Input id="user-phone" required value={phone} onChange={(e) => setPhone(e.target.value)} placeholder="+998 90 000 00 00" />
          </div>
          <div>
            <Label htmlFor="user-password">Vaqtinchalik parol</Label>
            <Input id="user-password" type="password" required value={password} onChange={(e) => setPassword(e.target.value)} />
          </div>
          <div>
            <Label htmlFor="user-role">Rol</Label>
            <Select id="user-role" value={roleCode} onChange={(e) => setRoleCode(e.target.value)}>
              {creatableRoleOptions.map((code) => (
                <option key={code} value={code}>
                  {roleLabel(code)}
                </option>
              ))}
            </Select>
            {roleOptions.includes("STUDENT") && (
              <p className="mt-1 text-[12px] text-ink-500">
                Student <Link href="/students" className="underline hover:text-ink-700">/students</Link> sahifasidan yaratiladi — u yerda guruh va profil ham birga ochiladi.
              </p>
            )}
          </div>
          <div>
            <Label htmlFor="user-branch">Filial</Label>
            <Select id="user-branch" value={branchId} onChange={(e) => setBranchId(e.target.value)}>
              <option value="">Biriktirilmagan</option>
              {branchesQuery.data?.map((b) => (
                <option key={b.id} value={b.id}>
                  {b.name}
                </option>
              ))}
            </Select>
          </div>

          {createMutation.isError && <Alert>{errorText(createMutation.error, "Foydalanuvchi yaratib bo'lmadi.")}</Alert>}

          <Button type="submit" loading={createMutation.isPending} className="w-full">
            Foydalanuvchi yaratish
          </Button>
        </form>
      </Drawer>
    </main>
  );
}
