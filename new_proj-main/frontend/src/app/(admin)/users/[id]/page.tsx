"use client";

import { Suspense, use, useState } from "react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ArrowLeft, Building2, Check, Copy, GraduationCap, KeyRound, Mail, Phone, Trash2, UserCheck, UsersRound, UserX,
} from "lucide-react";
import {
  activateUser, deactivateUser, deleteUser, getUserProfile, resetUserPassword, updateUser,
} from "@/lib/api/users";
import { roleLabel, getCurrentUser } from "@/lib/auth-storage";
import { ApiError } from "@/lib/api-client";
import { usePermissions } from "@/lib/use-permissions";
import { TeacherPortfolioTab } from "@/components/teachers/TeacherPortfolioTab";
import { PageHeader } from "@/components/ui/PageHeader";
import { Avatar } from "@/components/ui/Avatar";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { Card, CardBody, CardHeader, CardTitle } from "@/components/ui/Card";
import { Alert } from "@/components/ui/Alert";
import { Skeleton } from "@/components/ui/Skeleton";
import { ConfirmDialog } from "@/components/ui/ConfirmDialog";
import { Drawer } from "@/components/ui/Drawer";
import { Modal } from "@/components/ui/Modal";
import { Input, Label } from "@/components/ui/Field";
import { EmptyState } from "@/components/ui/EmptyState";
import { formatDateTime } from "@/lib/format-date";

const STATUS_TONE = { Active: "success", Inactive: "neutral", Suspended: "danger" } as const;
const STATUS_LABEL = { Active: "Faol", Inactive: "Nofaol", Suspended: "To'xtatilgan" } as const;

const errorText = (error: unknown, fallback: string) =>
  error instanceof ApiError ? error.message : fallback;


/**
 * One person's page. Everything else in the product refers to people by name in a table cell; this is where
 * that name leads — what they are attached to, and the three things an administrator does to an account
 * (edit, block, delete) in one place instead of spread across the list's row actions.
 */
export default function UserProfilePage({ params }: { params: Promise<{ id: string }> }) {
  return (
    <Suspense fallback={null}>
      <UserProfilePageContent params={params} />
    </Suspense>
  );
}

function UserProfilePageContent({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const router = useRouter();
  const queryClient = useQueryClient();
  const query = useQuery({ queryKey: ["users", "profile", id], queryFn: () => getUserProfile(id) });

  // UserDirectory tags its row links with ?from=<audience> so this page can return the visitor
  // to whichever list they actually came from (Hisoblar vs Xodimlar) instead of always to Staff.
  const from = useSearchParams().get("from");
  const back = from === "all" ? { href: "/accounts", label: "Hisoblar" } : { href: "/users", label: "Xodimlar" };

  const { can, scopeOf } = usePermissions();
  const [editOpen, setEditOpen] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [confirmReset, setConfirmReset] = useState(false);
  // Held only in this component's state: the server returns the temporary password once and never again.
  const [temporaryPassword, setTemporaryPassword] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);
  const [fullName, setFullName] = useState("");
  const [phone, setPhone] = useState("");

  const invalidate = () => {
    queryClient.invalidateQueries({ queryKey: ["users"] });
  };

  const editMutation = useMutation({
    mutationFn: () => updateUser(id, { fullName, phone }),
    onSuccess: () => {
      setEditOpen(false);
      invalidate();
    },
  });

  const statusMutation = useMutation({
    mutationFn: (next: "activate" | "deactivate") => (next === "activate" ? activateUser(id) : deactivateUser(id)),
    onSuccess: invalidate,
  });

  const deleteMutation = useMutation({ mutationFn: () => deleteUser(id) });

  const resetMutation = useMutation({
    mutationFn: () => resetUserPassword(id),
    onSuccess: (result) => {
      setConfirmReset(false);
      setCopied(false);
      setTemporaryPassword(result.temporaryPassword);
    },
  });

  const copyPassword = async () => {
    if (!temporaryPassword) return;
    try {
      await navigator.clipboard.writeText(temporaryPassword);
      setCopied(true);
    } catch {
      // Clipboard access needs a secure context (localhost or https); over a plain-http LAN address the browser
      // refuses — the password is selectable on screen, so nothing is lost.
    }
  };

  if (query.isPending)
    return (
      <main className="p-4 sm:p-6 lg:p-8">
        <Skeleton className="h-48" />
      </main>
    );

  if (query.isError)
    return (
      <main className="space-y-4 p-4 sm:p-6 lg:p-8">
        <Alert>{errorText(query.error, "Profilni yuklab bo'lmadi.")}</Alert>
        <Button onClick={() => void query.refetch()}>Qayta urinish</Button>
      </main>
    );

  const user = query.data;
  const isSelf = getCurrentUser()?.id === user.id;

  return (
    <main className="p-4 sm:p-6 lg:p-8">
      <Link href={back.href} className="mb-4 inline-flex items-center gap-1 text-sm text-brand-700 hover:underline">
        <ArrowLeft className="size-4" aria-hidden />
        {back.label}
      </Link>

      <PageHeader
        title={user.fullName}
        description={user.email}
        actions={
          <div className="flex flex-wrap gap-2">
            <Button
              variant="secondary"
              onClick={() => {
                setFullName(user.fullName);
                setPhone(user.phone);
                setEditOpen(true);
              }}
            >
              Tahrirlash
            </Button>
            {!isSelf && can("users.password.reset") && (
              <Button
                variant="secondary"
                onClick={() => {
                  resetMutation.reset();
                  setConfirmReset(true);
                }}
              >
                <KeyRound className="size-4" aria-hidden />
                Parolni tiklash
              </Button>
            )}
            {!isSelf && (
              <>
                {user.status === "Active" ? (
                  <Button variant="secondary" onClick={() => statusMutation.mutate("deactivate")} loading={statusMutation.isPending}>
                    <UserX className="size-4" aria-hidden />
                    Bloklash
                  </Button>
                ) : (
                  <Button variant="secondary" onClick={() => statusMutation.mutate("activate")} loading={statusMutation.isPending}>
                    <UserCheck className="size-4" aria-hidden />
                    Faollashtirish
                  </Button>
                )}
                <Button
                  variant="danger"
                  onClick={() => {
                    deleteMutation.reset();
                    setConfirmDelete(true);
                  }}
                >
                  <Trash2 className="size-4" aria-hidden />
                  O&apos;chirish
                </Button>
              </>
            )}
          </div>
        }
      />

      {statusMutation.isError && <Alert>{errorText(statusMutation.error, "Holatni o'zgartirib bo'lmadi.")}</Alert>}

      <div className="grid gap-4 lg:grid-cols-3">
        <Card className="lg:col-span-1">
          <CardBody className="space-y-4">
            <div className="flex items-center gap-3">
              <Avatar name={user.fullName} className="size-12 text-base" />
              <div className="min-w-0">
                <p className="truncate font-medium text-ink-900">{user.fullName}</p>
                <Badge tone={STATUS_TONE[user.status]} dot>
                  {STATUS_LABEL[user.status]}
                </Badge>
              </div>
            </div>

            <dl className="space-y-2 text-[13px]">
              <div className="flex items-center gap-2 text-ink-600">
                <Mail className="size-4 shrink-0 text-ink-400" aria-hidden />
                <span className="truncate">{user.email}</span>
              </div>
              <div className="flex items-center gap-2 text-ink-600">
                <Phone className="size-4 shrink-0 text-ink-400" aria-hidden />
                <span className="font-mono">{user.phone}</span>
              </div>
            </dl>

            <div>
              <p className="mb-1.5 text-[12.5px] font-medium text-ink-500">Rollar</p>
              <div className="flex flex-wrap gap-1">
                {user.roles.map((r) => (
                  <Badge key={r} tone="brand">
                    {roleLabel(r)}
                  </Badge>
                ))}
              </div>
            </div>

            <div>
              <p className="mb-1.5 text-[12.5px] font-medium text-ink-500">Filiallar</p>
              {user.branches.length ? (
                <div className="flex flex-wrap gap-1">
                  {user.branches.map((b) => (
                    <Badge key={b.id} tone="neutral">
                      <Building2 className="size-3" aria-hidden />
                      {b.name}
                    </Badge>
                  ))}
                </div>
              ) : (
                <p className="text-[13px] text-ink-500">Biriktirilmagan</p>
              )}
            </div>

            <div className="border-t border-border pt-3 text-[12.5px] text-ink-500">
              <p>Yaratilgan: {formatDateTime(user.createdAt)}</p>
              {user.updatedAt && <p>Oxirgi o&apos;zgarish: {formatDateTime(user.updatedAt)}</p>}
            </div>
          </CardBody>
        </Card>

        <div className="space-y-4 lg:col-span-2">
          {(user.studentId || user.parentId) && (
            <Card>
              <CardHeader>
                <CardTitle>Bog&apos;langan karta</CardTitle>
              </CardHeader>
              <CardBody className="flex flex-wrap gap-2">
                {user.studentId && (
                  <Link href={`/students/${user.studentId}?from=${from ?? "users"}`}>
                    <Button variant="secondary">
                      <GraduationCap className="size-4" aria-hidden />
                      O&apos;quvchi kartasi
                    </Button>
                  </Link>
                )}
                {user.parentId && <Badge tone="info">Ota-ona kartasi mavjud</Badge>}
              </CardBody>
            </Card>
          )}

          <Card>
            <CardHeader>
              <CardTitle>Guruhlar</CardTitle>
            </CardHeader>
            <CardBody>
              {user.groups.length ? (
                <ul className="divide-y divide-border">
                  {user.groups.map((g) => (
                    <li key={g.id} className="flex items-center justify-between gap-3 py-2.5 first:pt-0 last:pb-0">
                      <Link href={`/groups?group=${g.id}`} className="truncate text-[13.5px] text-brand-700 hover:underline">
                        {g.name}
                      </Link>
                      <Badge tone="neutral">
                        {g.role === "MainTeacher" ? "Asosiy o'qituvchi" : "Yordamchi o'qituvchi"}
                      </Badge>
                    </li>
                  ))}
                </ul>
              ) : (
                <EmptyState icon={UsersRound} title="Guruhga biriktirilmagan" />
              )}
            </CardBody>
          </Card>
        </div>
      </div>

      <ConfirmDialog
        open={confirmDelete}
        title={`${user.fullName} hisobini o'chirish`}
        description={
          <>
            Hisob butunlay o&apos;chiriladi va qaytarib bo&apos;lmaydi. Tarix saqlanishi kerak bo&apos;lsa,{" "}
            <strong>bloklang</strong>.
          </>
        }
        loading={deleteMutation.isPending}
        error={deleteMutation.isError ? errorText(deleteMutation.error, "O'chirib bo'lmadi.") : null}
        onConfirm={() =>
          deleteMutation.mutate(undefined, {
            onSuccess: () => {
              invalidate();
              router.replace("/users");
            },
          })
        }
        onClose={() => setConfirmDelete(false)}
      />

      <ConfirmDialog
        open={confirmReset}
        title={`${user.fullName} parolini tiklash`}
        description={
          <>
            Yangi vaqtinchalik parol yaratiladi. Hisobning <strong>barcha qurilmalardagi sessiyalari tugatiladi</strong>,
            u birinchi kirishda o&apos;z parolini tanlashi shart bo&apos;ladi.
          </>
        }
        confirmLabel="Parolni tiklash"
        loading={resetMutation.isPending}
        error={resetMutation.isError ? errorText(resetMutation.error, "Parolni tiklab bo'lmadi.") : null}
        onConfirm={() => resetMutation.mutate()}
        onClose={() => setConfirmReset(false)}
      />

      <Modal
        open={temporaryPassword !== null}
        onClose={() => setTemporaryPassword(null)}
        title="Vaqtinchalik parol"
        description={user.fullName}
      >
        <div className="space-y-4">
          <div className="space-y-1.5 rounded-xl bg-ink-50 p-4 text-[13px] text-ink-700">
            <p>Login: <span className="font-mono">{user.email}</span></p>
            <p>Parol: <span className="select-all font-mono text-[15px] font-semibold text-ink-900">{temporaryPassword}</span></p>
          </div>
          <p className="text-[12.5px] leading-relaxed text-ink-500">
            Parol faqat hozir ko&apos;rsatiladi — oynani yopgach qayta ko&apos;rib bo&apos;lmaydi. Uni foydalanuvchiga yetkazing;
            u kirgach parolni o&apos;zi almashtiradi.
          </p>
          <div className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
            <Button variant="secondary" onClick={() => void copyPassword()}>
              {copied ? <Check className="size-4" aria-hidden /> : <Copy className="size-4" aria-hidden />}
              {copied ? "Nusxa olindi" : "Nusxa olish"}
            </Button>
            <Button onClick={() => setTemporaryPassword(null)}>Tayyor</Button>
          </div>
        </div>
      </Modal>

      <Drawer open={editOpen} onClose={() => setEditOpen(false)} title="Profilni tahrirlash">
        <form
          className="space-y-4"
          onSubmit={(e) => {
            e.preventDefault();
            editMutation.mutate();
          }}
        >
          <div>
            <Label htmlFor="edit-name">F.I.O</Label>
            <Input id="edit-name" required value={fullName} onChange={(e) => setFullName(e.target.value)} />
          </div>
          <div>
            <Label htmlFor="edit-phone">Telefon</Label>
            <Input id="edit-phone" required value={phone} onChange={(e) => setPhone(e.target.value)} />
          </div>
          {editMutation.isError && <Alert>{errorText(editMutation.error, "Saqlab bo'lmadi.")}</Alert>}
          <Button type="submit" loading={editMutation.isPending} className="w-full">
            Saqlash
          </Button>
        </form>
      </Drawer>

      {/* A teacher (whose portfolio permissions are "own") keeps their portfolio here, on their own profile: add, edit, view and remove. */}
      {isSelf && scopeOf("teacher.portfolio.view") === "Self" && (
        <section className="mt-8">
          <h2 className="mb-3 text-[16px] font-semibold text-ink-900">Portfolio</h2>
          <TeacherPortfolioTab teacherId={user.id} isSelf />
        </section>
      )}
    </main>
  );
}
