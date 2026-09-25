"use client";

import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ChevronDown, ChevronRight, Lock, Search, ShieldCheck } from "lucide-react";
import { ApiError } from "@/lib/api-client";
import type { ScopeType } from "@/lib/api/me";
import {
  getPermissionMatrix, PERMISSION_MATRIX_KEY, SCOPE_HELP, SCOPE_LABEL, SCOPE_SHORT, setPermissionOverride,
  type MatrixCell, type MatrixPermission, type MatrixRole, type PermissionMatrix,
} from "@/lib/api/permissions";
import { cn } from "@/lib/cn";
import { usePermissions } from "@/lib/use-permissions";
import { Alert } from "@/components/ui/Alert";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { Drawer } from "@/components/ui/Drawer";
import { EmptyState } from "@/components/ui/EmptyState";
import { Input } from "@/components/ui/Field";
import { PageHeader } from "@/components/ui/PageHeader";

const EDIT = "permission.policy.edit";

type Tone = "brand" | "info" | "accent" | "neutral";
const SCOPE_TONE: Record<ScopeType, Tone> = {
  AllOrganization: "brand", MyBranches: "info", MyGroups: "accent", AssignedGroups: "accent", Self: "neutral", MyChildren: "neutral", None: "neutral",
};

interface Editing {
  permission: MatrixPermission;
  role: MatrixRole;
  cell: MatrixCell;
}

export default function PermissionMatrixPage() {
  const { can, ready } = usePermissions();
  const allowed = ready && can(EDIT);
  const matrix = useQuery({ queryKey: PERMISSION_MATRIX_KEY, queryFn: getPermissionMatrix, enabled: allowed });

  const [search, setSearch] = useState("");
  const [onlyChanged, setOnlyChanged] = useState(false);
  const [open, setOpen] = useState<ReadonlySet<string>>(new Set());
  const [editing, setEditing] = useState<Editing | null>(null);

  const term = search.trim().toLowerCase();
  const groups = useMemo(() => {
    const byResource = new Map<string, MatrixPermission[]>();
    for (const permission of matrix.data?.permissions ?? []) {
      if (term && !permission.code.toLowerCase().includes(term) && !permission.resource.toLowerCase().includes(term)) continue;
      if (onlyChanged && !permission.cells.some((c) => c.overrideId)) continue;
      byResource.set(permission.resource, [...(byResource.get(permission.resource) ?? []), permission]);
    }
    return [...byResource.entries()];
  }, [matrix.data, term, onlyChanged]);
  const changedTotal = matrix.data?.permissions.reduce((sum, p) => sum + p.cells.filter((c) => c.overrideId).length, 0) ?? 0;

  if (ready && !can(EDIT)) {
    return (
      <main className="p-4 sm:p-6 lg:p-8">
        <EmptyState icon={Lock} title="Bu sahifa uchun ruxsat yo‘q" description="Ruxsatlar matritsasini faqat Super Admin o‘zgartiradi." />
      </main>
    );
  }

  // While a search or the "changed only" filter is on, every matching group is open — hiding a hit behind a closed section defeats it.
  const forceOpen = term !== "" || onlyChanged;
  const roles = matrix.data?.roles ?? [];

  return (
    <main className="p-4 sm:p-6 lg:p-8">
      <PageHeader
        title="Ruxsatlar matritsasi"
        description="Har bir rol nimani qila olishi va qaysi doirada. O‘zgarish darhol kuchga kiradi va dasturni qayta ishga tushirganda ham saqlanadi."
      />

      <div className="mb-4 flex flex-wrap items-center gap-3">
        <div className="relative w-full max-w-xs">
          <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-ink-400" aria-hidden />
          <Input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Ruxsat kodi yoki resurs…" className="pl-9" aria-label="Qidirish" />
        </div>
        <label className="flex items-center gap-2 text-[13px] text-ink-700">
          <input type="checkbox" checked={onlyChanged} onChange={(e) => setOnlyChanged(e.target.checked)} className="size-4 accent-brand-solid" />
          Faqat o‘zgartirilganlar{changedTotal > 0 && <Badge tone="warning">{changedTotal}</Badge>}
        </label>
        <p className="ml-auto text-[12.5px] text-ink-500">Katakni bosing — doirani o‘zgartirasiz yoki ruxsatni olib tashlaysiz.</p>
      </div>

      {matrix.isError && <Alert>{matrix.error instanceof ApiError ? matrix.error.message : "Matritsani yuklab bo‘lmadi."}</Alert>}

      {matrix.data && groups.length === 0 && (
        <div className="rounded-xl border border-border bg-surface">
          <EmptyState icon={ShieldCheck} title="Hech narsa topilmadi" description={onlyChanged ? "Hali birorta katak o‘zgartirilmagan." : "Boshqa so‘z bilan qidirib ko‘ring."} />
        </div>
      )}

      {matrix.data && groups.length > 0 && (
        <div className="overflow-x-auto rounded-xl border border-border bg-surface">
          <table className="w-full min-w-[56rem] border-collapse text-left text-[13px]">
            <thead>
              <tr className="border-b border-border bg-ink-50">
                <th className="sticky left-0 z-10 w-64 bg-ink-50 px-3 py-2.5 font-medium text-ink-600">Ruxsat</th>
                {roles.map((role) => (
                  <th key={role.code} className="px-2 py-2.5 text-center font-medium text-ink-600">{role.name}</th>
                ))}
              </tr>
            </thead>
            {groups.map(([resource, permissions]) => {
              const isOpen = forceOpen || open.has(resource);
              const changed = permissions.reduce((sum, p) => sum + p.cells.filter((c) => c.overrideId).length, 0);
              return (
                <tbody key={resource} className="border-b border-border last:border-b-0">
                  <tr className="bg-ink-50/60">
                    <td colSpan={roles.length + 1} className="p-0">
                      <button
                        type="button"
                        onClick={() => setOpen((current) => {
                          const next = new Set(current);
                          if (!next.delete(resource)) next.add(resource);
                          return next;
                        })}
                        aria-expanded={isOpen}
                        className="flex w-full items-center gap-2 px-3 py-2 text-left font-medium text-ink-800"
                      >
                        {isOpen ? <ChevronDown className="size-4 text-ink-400" aria-hidden /> : <ChevronRight className="size-4 text-ink-400" aria-hidden />}
                        {resource}
                        <span className="text-[12px] font-normal text-ink-500">{permissions.length} ta ruxsat</span>
                        {changed > 0 && <Badge tone="warning">{changed} ta o‘zgartirilgan</Badge>}
                      </button>
                    </td>
                  </tr>
                  {isOpen && permissions.map((permission) => (
                    <tr key={permission.code} className="border-t border-border/60">
                      <td className="sticky left-0 z-10 bg-surface px-3 py-2 align-middle">
                        <span className="font-mono text-[12.5px] text-ink-800">{permission.code}</span>
                      </td>
                      {roles.map((role) => {
                        const cell = permission.cells.find((c) => c.roleCode === role.code);
                        return (
                          <td key={role.code} className="px-1.5 py-1.5 text-center">
                            {cell && <CellButton cell={cell} label={`${role.name}: ${permission.code}`} onClick={() => setEditing({ permission, role, cell })} />}
                          </td>
                        );
                      })}
                    </tr>
                  ))}
                </tbody>
              );
            })}
          </table>
        </div>
      )}

      <Drawer
        open={editing !== null}
        onClose={() => setEditing(null)}
        title={editing ? `${editing.role.name} · ${editing.permission.code}` : ""}
        description="Bu rol uchun ruxsat doirasini tanlang."
      >
        {editing && <CellEditor key={`${editing.role.code}:${editing.permission.code}`} editing={editing} onDone={() => setEditing(null)} />}
      </Drawer>
    </main>
  );
}

function CellButton({ cell, label, onClick }: { cell: MatrixCell; label: string; onClick: () => void }) {
  const changed = cell.overrideId !== null;
  return (
    <button
      type="button"
      onClick={onClick}
      aria-label={label}
      className={cn(
        "inline-flex min-w-16 justify-center rounded-full py-0.5 outline-offset-2 hover:bg-ink-100",
        changed && "ring-2 ring-warning-500/70",
      )}
    >
      {cell.revoked ? (
        <Badge tone="danger">Olib tashlangan</Badge>
      ) : cell.effectiveScope ? (
        <Badge tone={SCOPE_TONE[cell.effectiveScope]}>{SCOPE_SHORT[cell.effectiveScope]}</Badge>
      ) : (
        <span className="px-2 text-ink-300">—</span>
      )}
    </button>
  );
}

type Choice = ScopeType | "revoked" | null;

function CellEditor({ editing, onDone }: { editing: Editing; onDone: () => void }) {
  const { permission, role, cell } = editing;
  const queryClient = useQueryClient();
  const [choice, setChoice] = useState<Choice>(cell.revoked ? "revoked" : cell.effectiveScope);

  const save = useMutation({
    mutationFn: (next: { scope: ScopeType | null; revoked: boolean }) =>
      setPermissionOverride({ roleCode: role.code, permissionCode: permission.code, ...next }),
    onSuccess: (updated) => {
      // Only this cell changed — patch it in place instead of fetching all ~250 rows again.
      queryClient.setQueryData<PermissionMatrix>(PERMISSION_MATRIX_KEY, (matrix) => matrix && {
        ...matrix,
        permissions: matrix.permissions.map((p) => p.code !== permission.code ? p : {
          ...p, cells: p.cells.map((c) => c.roleCode === updated.roleCode ? updated : c),
        }),
      });
      // What this person's own screen offers follows the same matrix.
      queryClient.invalidateQueries({ queryKey: ["me", "permissions"] });
      onDone();
    },
  });

  const unchanged = choice === (cell.revoked ? "revoked" : cell.effectiveScope);
  const submit = () => {
    if (choice === "revoked") save.mutate({ scope: null, revoked: true });
    else if (choice) save.mutate({ scope: choice, revoked: false });
  };

  return (
    <div className="space-y-5 p-5">
      <dl className="grid grid-cols-2 gap-3 text-[13px]">
        <div>
          <dt className="text-ink-500">Standart</dt>
          <dd className="mt-0.5 font-medium text-ink-800">{cell.defaultScope ? SCOPE_LABEL[cell.defaultScope] : "Ruxsat yo‘q"}</dd>
        </div>
        <div>
          <dt className="text-ink-500">Hozir</dt>
          <dd className="mt-0.5 font-medium text-ink-800">
            {cell.revoked ? "Olib tashlangan" : cell.effectiveScope ? SCOPE_LABEL[cell.effectiveScope] : "Ruxsat yo‘q"}
            {cell.overrideId && <Badge tone="warning" className="ml-2">o‘zgartirilgan</Badge>}
          </dd>
        </div>
      </dl>

      <fieldset className="space-y-2">
        <legend className="mb-1.5 text-[13px] font-medium text-ink-700">Doira</legend>
        {permission.allowedScopes.map((scope) => (
          <ChoiceRow key={scope} name="scope" checked={choice === scope} onSelect={() => setChoice(scope)} title={SCOPE_LABEL[scope]} help={SCOPE_HELP[scope]} />
        ))}
        {cell.defaultScope !== null && (
          <ChoiceRow
            name="scope" checked={choice === "revoked"} onSelect={() => setChoice("revoked")} tone="danger"
            title="Ruxsatni olib tashlash" help="Bu rol ushbu amalni umuman qila olmaydi."
          />
        )}
      </fieldset>

      <p className="text-[12.5px] text-ink-500">
        Server ruxsatni har so‘rovda qayta tekshiradi, shuning uchun o‘zgarish darhol kuchga kiradi. Foydalanuvchining ekranidagi tugmalar ro‘yxati bir necha daqiqada yangilanadi.
      </p>

      {save.isError && <Alert>{save.error instanceof ApiError ? save.error.message : "Saqlab bo‘lmadi."}</Alert>}

      <div className="flex flex-wrap justify-between gap-2">
        {cell.overrideId ? (
          <Button variant="secondary" loading={save.isPending} onClick={() => save.mutate({ scope: null, revoked: false })}>Standartga qaytarish</Button>
        ) : <span />}
        <Button loading={save.isPending} disabled={unchanged || choice === null} onClick={submit}>Saqlash</Button>
      </div>
    </div>
  );
}

function ChoiceRow({ name, checked, onSelect, title, help, tone }: {
  name: string; checked: boolean; onSelect: () => void; title: string; help: string; tone?: "danger";
}) {
  return (
    <label
      className={cn(
        "flex cursor-pointer items-start gap-3 rounded-lg border px-3 py-2.5",
        checked ? (tone === "danger" ? "border-danger-500 bg-danger-50" : "border-brand-500 bg-brand-50") : "border-border hover:bg-ink-50",
      )}
    >
      <input type="radio" name={name} checked={checked} onChange={onSelect} className="mt-1 size-4 accent-brand-solid" />
      <span>
        <span className={cn("block text-[13.5px] font-medium", tone === "danger" ? "text-danger-600" : "text-ink-900")}>{title}</span>
        <span className="block text-[12.5px] text-ink-500">{help}</span>
      </span>
    </label>
  );
}
