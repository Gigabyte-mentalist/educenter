"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Suspense, useState } from "react";
import Link from "next/link";
import { Building2, Plus, MapPin, Clock, Pencil, Trash2 } from "lucide-react";
import { createBranch, deleteBranch, listBranches, updateBranch, type Branch } from "@/lib/api/branches";
import { ApiError } from "@/lib/api-client";
import { usePermissions } from "@/lib/use-permissions";
import { useHighlightTarget, highlightElementId, highlightClassName } from "@/lib/use-highlight-target";
import { PageHeader } from "@/components/ui/PageHeader";
import { Button } from "@/components/ui/Button";
import { Table, THead, TBody, TRow, TH, TD } from "@/components/ui/Table";
import { TableSkeleton } from "@/components/ui/Skeleton";
import { EmptyState } from "@/components/ui/EmptyState";
import { Alert } from "@/components/ui/Alert";
import { Drawer } from "@/components/ui/Drawer";
import { ConfirmDialog } from "@/components/ui/ConfirmDialog";
import { Input, Label } from "@/components/ui/Field";

const errorText = (error: unknown, fallback: string) =>
  error instanceof ApiError ? error.message : fallback;

export default function BranchesPage() {
  return (
    <Suspense fallback={null}>
      <BranchesPageContent />
    </Suspense>
  );
}

function BranchesPageContent() {
  const queryClient = useQueryClient();
  // Each affordance follows its own permission: the Administrator may edit a branch of theirs but neither create nor delete one.
  const { can } = usePermissions();
  const canCreate = can("branch.create");
  const canEdit = can("branch.edit");
  const canDelete = can("branch.delete");
  const branchesQuery = useQuery({ queryKey: ["branches"], queryFn: listBranches });
  const highlightId = useHighlightTarget(branchesQuery.isSuccess);

  // One drawer serves both create and edit; `editing` is what tells them apart.
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [editing, setEditing] = useState<Branch | null>(null);
  const [name, setName] = useState("");
  const [address, setAddress] = useState("");
  const [timezone, setTimezone] = useState("Asia/Tashkent");
  const [pendingDelete, setPendingDelete] = useState<Branch | null>(null);

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ["branches"] });

  const saveMutation = useMutation({
    mutationFn: () =>
      editing
        ? updateBranch(editing.id, { name, address, timezone })
        : createBranch({ name, address, timezone }),
    onSuccess: () => {
      setDrawerOpen(false);
      invalidate();
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => deleteBranch(id),
    onSuccess: () => {
      setPendingDelete(null);
      invalidate();
    },
  });

  const openCreate = () => {
    saveMutation.reset();
    setEditing(null);
    setName("");
    setAddress("");
    setTimezone("Asia/Tashkent");
    setDrawerOpen(true);
  };

  const openEdit = (branch: Branch) => {
    saveMutation.reset();
    setEditing(branch);
    setName(branch.name);
    setAddress(branch.address);
    setTimezone(branch.timezone);
    setDrawerOpen(true);
  };

  return (
    <main className="p-4 sm:p-6 lg:p-8">
      <PageHeader
        title="Filiallar"
        description={canCreate || canEdit
          ? "Tashkilotingizga tegishli barcha filiallar ro'yxati."
          : "Filialingiz statistikasi uchun nomini bosing."}
        actions={canCreate && (
          <Button onClick={openCreate}>
            <Plus className="size-4" aria-hidden />
            <span className="hidden sm:inline">Yangi filial</span>
            <span className="sm:hidden">Qo&apos;shish</span>
          </Button>
        )}
      />

      {branchesQuery.isError && <Alert>{errorText(branchesQuery.error, "Filiallarni yuklab bo'lmadi.")}</Alert>}

      {branchesQuery.isLoading && (
        <div className="rounded-xl border border-border bg-surface">
          <TableSkeleton rows={4} cols={3} />
        </div>
      )}

      {branchesQuery.data && branchesQuery.data.length === 0 && (
        <div className="rounded-xl border border-border bg-surface">
          <EmptyState
            icon={Building2}
            title="Hali filial qo'shilmagan"
            description={canCreate ? "Birinchi filialingizni qo'shib, guruh va jadval yaratishni boshlang." : undefined}
            action={canCreate && (
              <Button variant="secondary" onClick={openCreate}>
                <Plus className="size-4" aria-hidden />
                Filial qo&apos;shish
              </Button>
            )}
          />
        </div>
      )}

      {branchesQuery.data && branchesQuery.data.length > 0 && (
        <div className="overflow-hidden rounded-xl border border-border bg-surface">
          <Table bare>
              <THead>
                <tr>
                  <TH>Nomi</TH>
                  <TH>Manzil</TH>
                  <TH className="hidden sm:table-cell">Vaqt zonasi</TH>
                  {(canEdit || canDelete) && <TH className="text-right">Amallar</TH>}
                </tr>
              </THead>
              <TBody>
                {branchesQuery.data.map((branch) => (
                  <TRow key={branch.id} id={highlightElementId(branch.id)} className={highlightClassName(branch.id, highlightId)}>
                    <TD className="font-medium text-ink-900">
                      <Link href={`/branches/${branch.id}`} className="hover:underline">
                        {branch.name}
                      </Link>
                    </TD>
                    <TD>
                      <span className="inline-flex items-center gap-1.5 text-ink-600">
                        <MapPin className="size-3.5 shrink-0 text-ink-400" aria-hidden />
                        {branch.address}
                      </span>
                    </TD>
                    <TD className="hidden sm:table-cell">
                      <span className="inline-flex items-center gap-1.5 font-mono text-[12.5px] text-ink-600">
                        <Clock className="size-3.5 text-ink-400" aria-hidden />
                        {branch.timezone}
                      </span>
                    </TD>
                    {(canEdit || canDelete) && (
                      <TD>
                        <div className="flex items-center justify-end gap-1">
                          {canEdit && (
                            <Button
                              variant="ghost"
                              size="icon"
                              title="Tahrirlash"
                              aria-label={`${branch.name} filialini tahrirlash`}
                              onClick={() => openEdit(branch)}
                            >
                              <Pencil className="size-4" aria-hidden />
                            </Button>
                          )}
                          {canDelete && (
                            <Button
                              variant="ghost"
                              size="icon"
                              title="O'chirish"
                              aria-label={`${branch.name} filialini o'chirish`}
                              className="text-danger-500 hover:bg-danger-50 hover:text-danger-600"
                              onClick={() => {
                                deleteMutation.reset();
                                setPendingDelete(branch);
                              }}
                            >
                              <Trash2 className="size-4" aria-hidden />
                            </Button>
                          )}
                        </div>
                      </TD>
                    )}
                  </TRow>
                ))}
              </TBody>
          </Table>
        </div>
      )}

      <ConfirmDialog
        open={pendingDelete !== null}
        title={`${pendingDelete?.name ?? ""} filialini o'chirish`}
        description="Filial faqat bo'sh bo'lsa o'chiriladi — guruh, dars yoki xodim biriktirilgan bo'lsa, server nimasi qolganini aytadi."
        loading={deleteMutation.isPending}
        error={deleteMutation.isError ? errorText(deleteMutation.error, "O'chirib bo'lmadi.") : null}
        onConfirm={() => pendingDelete && deleteMutation.mutate(pendingDelete.id)}
        onClose={() => setPendingDelete(null)}
      />

      <Drawer
        open={drawerOpen}
        onClose={() => setDrawerOpen(false)}
        title={editing ? "Filialni tahrirlash" : "Yangi filial"}
        description="Filial ma'lumotlarini kiriting."
      >
        <form
          className="space-y-4"
          onSubmit={(e) => {
            e.preventDefault();
            saveMutation.mutate();
          }}
        >
          <div>
            <Label htmlFor="branch-name">Nomi</Label>
            <Input id="branch-name" required value={name} onChange={(e) => setName(e.target.value)} placeholder="Chilonzor filiali" />
          </div>
          <div>
            <Label htmlFor="branch-address">Manzil</Label>
            <Input id="branch-address" required value={address} onChange={(e) => setAddress(e.target.value)} placeholder="Toshkent, Chilonzor" />
          </div>
          <div>
            <Label htmlFor="branch-timezone">Vaqt zonasi</Label>
            <Input id="branch-timezone" required value={timezone} onChange={(e) => setTimezone(e.target.value)} />
          </div>

          {saveMutation.isError && <Alert>{errorText(saveMutation.error, "Saqlab bo'lmadi.")}</Alert>}

          <Button type="submit" loading={saveMutation.isPending} className="w-full">
            {editing ? "Saqlash" : "Filial yaratish"}
          </Button>
        </form>
      </Drawer>
    </main>
  );
}
