"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CheckCircle2, Plus, RefreshCw, X } from "lucide-react";
import {
  createAndLinkParent, linkExistingParent, listStudentParents, removeStudentParent,
  type ParentRelationship, type StudentParentLink,
} from "@/lib/api/students";
import { listUsers } from "@/lib/api/users";
import { ApiError } from "@/lib/api-client";
import { generatePassword } from "@/lib/generate-password";
import { usePermissions } from "@/lib/use-permissions";
import { useDebouncedValue } from "@/lib/use-debounced-value";
import { Drawer } from "@/components/ui/Drawer";
import { Button } from "@/components/ui/Button";
import { Alert } from "@/components/ui/Alert";
import { ConfirmDialog } from "@/components/ui/ConfirmDialog";
import { Tabs } from "@/components/ui/Tabs";
import { Input, Label, Select } from "@/components/ui/Field";

const RELATIONSHIP_LABEL: Record<ParentRelationship, string> = {
  Father: "Ota", Mother: "Ona", Guardian: "Vasiy", Other: "Boshqa",
};

const errorText = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

/**
 * The card lives inside the student's "Umumiy" tab rather than its own tab — TZ §24 needs a guardian linked before
 * the Parent Portal has anything to show, and a centre entering students in bulk sets this up right after
 * registration, next to the same profile fields.
 */
export function StudentParents({ studentId }: { studentId: string }) {
  const { can } = usePermissions();
  const queryClient = useQueryClient();
  const parentsQuery = useQuery({ queryKey: ["students", studentId, "parents"], queryFn: () => listStudentParents(studentId) });
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [pendingRemove, setPendingRemove] = useState<StudentParentLink | null>(null);

  // The profile header shows the parents too, from the overview — a link or unlink must refresh both.
  const invalidate = () => Promise.all([
    queryClient.invalidateQueries({ queryKey: ["students", studentId, "parents"] }),
    queryClient.invalidateQueries({ queryKey: ["students", studentId, "overview"] }),
  ]);

  const removeMutation = useMutation({
    mutationFn: (parentId: string) => removeStudentParent(studentId, parentId),
    onSuccess: () => {
      invalidate();
      setPendingRemove(null);
    },
  });

  const canManage = can("parents.manage");
  const parents = parentsQuery.data ?? [];

  if (!canManage && parents.length === 0) return null;

  return (
    <div className="border-t border-border p-5">
      <div className="mb-3 flex items-center justify-between">
        <h3 className="text-[13.5px] font-semibold text-ink-900">Ota-ona</h3>
        {canManage && (
          <Button size="sm" variant="secondary" onClick={() => setDrawerOpen(true)}>
            <Plus className="size-3.5" aria-hidden />
            Qo&apos;shish
          </Button>
        )}
      </div>

      {parents.length === 0 ? (
        <p className="text-[12.5px] text-ink-500">Ota-ona biriktirilmagan.</p>
      ) : (
        <div className="space-y-2">
          {parents.map((p) => (
            <div key={p.parentId} className="flex items-center justify-between rounded-lg bg-ink-50 px-3 py-2">
              <div className="min-w-0">
                <p className="truncate text-[13px] font-medium text-ink-900">
                  {p.fullName} <span className="font-normal text-ink-500">· {RELATIONSHIP_LABEL[p.relationship]}</span>
                </p>
                <p className="truncate text-[12px] text-ink-500">{p.phone}</p>
              </div>
              {canManage && (
                <button
                  type="button"
                  onClick={() => { removeMutation.reset(); setPendingRemove(p); }}
                  className="flex size-6 shrink-0 items-center justify-center rounded-md text-ink-400 hover:bg-danger-50 hover:text-danger-600"
                  aria-label={`${p.fullName} bilan bog'lamni uzish`}
                >
                  <X className="size-3.5" aria-hidden />
                </button>
              )}
            </div>
          ))}
        </div>
      )}

      <ConfirmDialog
        open={pendingRemove !== null}
        title={`${pendingRemove?.fullName ?? ""} bilan bog'lamni uzish`}
        description="Ota-ona hisobi qoladi (boshqa farzandi bo'lishi mumkin), lekin bu farzandni endi ko'rmaydi."
        confirmLabel="Uzish"
        loading={removeMutation.isPending}
        error={removeMutation.isError ? errorText(removeMutation.error, "Uzib bo'lmadi.") : null}
        onConfirm={() => pendingRemove && removeMutation.mutate(pendingRemove.parentId)}
        onClose={() => setPendingRemove(null)}
      />

      {drawerOpen && <ParentLinkDrawer studentId={studentId} onClose={() => setDrawerOpen(false)} onLinked={invalidate} />}
    </div>
  );
}

function ParentLinkDrawer({ studentId, onClose, onLinked }: { studentId: string; onClose: () => void; onLinked: () => void }) {
  const [created, setCreated] = useState<{ fullName: string; email: string; password: string } | null>(null);

  if (created) {
    return (
      <Drawer open onClose={onClose} title="Ota-ona qo'shildi" description={created.fullName}>
        <div className="space-y-5">
          <div className="flex items-start gap-3 rounded-xl bg-success-50 p-4">
            <CheckCircle2 className="mt-0.5 size-5 shrink-0 text-success-600" aria-hidden />
            <p className="text-[13px] text-ink-700">{created.fullName} farzandga biriktirildi.</p>
          </div>
          <div className="space-y-1.5 rounded-xl border border-border p-4 text-[13px] text-ink-700">
            <p className="font-medium text-ink-900">Ota-onaga aytib qo&apos;ying</p>
            <p>Login: <span className="font-mono">{created.email}</span></p>
            <p>Parol: <span className="font-mono">{created.password}</span></p>
            <p className="pt-1 text-[12px] text-ink-500">Parol bu oynani yopgach ko&apos;rsatilmaydi.</p>
          </div>
          <Button variant="secondary" className="w-full" onClick={onClose}>Yopish</Button>
        </div>
      </Drawer>
    );
  }

  return (
    <Drawer open onClose={onClose} title="Ota-ona qo'shish" description="Yangi hisob yarating yoki mavjudini biriktiring">
      <Tabs
        tabs={[
          {
            id: "new", label: "Yangi hisob",
            content: <NewParentForm studentId={studentId} onLinked={(r) => { setCreated(r); onLinked(); }} />,
          },
          {
            id: "existing", label: "Mavjud hisob",
            content: <ExistingParentForm studentId={studentId} onLinked={() => { onLinked(); onClose(); }} />,
          },
        ]}
      />
    </Drawer>
  );
}

function RelationshipSelect({ id, value, onChange }: { id: string; value: ParentRelationship; onChange: (v: ParentRelationship) => void }) {
  return (
    <div>
      <Label htmlFor={id}>Munosabat</Label>
      <Select id={id} value={value} onChange={(e) => onChange(e.target.value as ParentRelationship)}>
        {(Object.keys(RELATIONSHIP_LABEL) as ParentRelationship[]).map((r) => (
          <option key={r} value={r}>{RELATIONSHIP_LABEL[r]}</option>
        ))}
      </Select>
    </div>
  );
}

function NewParentForm({ studentId, onLinked }: { studentId: string; onLinked: (result: { fullName: string; email: string; password: string }) => void }) {
  const [relationship, setRelationship] = useState<ParentRelationship>("Guardian");
  const [fullName, setFullName] = useState("");
  const [phone, setPhone] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");

  const mutation = useMutation({
    mutationFn: () => createAndLinkParent(studentId, { fullName, phone, email, password, relationship }),
    onSuccess: () => onLinked({ fullName, email, password }),
  });

  return (
    <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); mutation.mutate(); }}>
      <RelationshipSelect id="parent-new-relationship" value={relationship} onChange={setRelationship} />
      <div>
        <Label htmlFor="parent-new-name">Ism-familiya</Label>
        <Input id="parent-new-name" required maxLength={200} value={fullName} onChange={(e) => setFullName(e.target.value)} />
      </div>
      <div>
        <Label htmlFor="parent-new-phone">Telefon</Label>
        <Input id="parent-new-phone" type="tel" required maxLength={30} value={phone} onChange={(e) => setPhone(e.target.value)} placeholder="+998 90 123 45 67" />
      </div>
      <div>
        <Label htmlFor="parent-new-email">Login uchun email</Label>
        <Input id="parent-new-email" type="email" required maxLength={200} value={email} onChange={(e) => setEmail(e.target.value)} />
      </div>
      <div>
        <Label htmlFor="parent-new-password">Vaqtinchalik parol</Label>
        <div className="flex gap-2">
          <Input id="parent-new-password" type="text" required minLength={8} value={password} onChange={(e) => setPassword(e.target.value)} />
          <Button type="button" variant="secondary" onClick={() => setPassword(generatePassword())} aria-label="Parol yaratish">
            <RefreshCw className="size-4" aria-hidden />
          </Button>
        </div>
        <p className="mt-1 text-[12px] text-ink-500">Kamida 8 belgi. Saqlagandan keyin ota-onaga aytish uchun ko&apos;rsatiladi.</p>
      </div>
      {mutation.isError && <Alert>{errorText(mutation.error, "Ota-onani qo'shib bo'lmadi.")}</Alert>}
      <Button type="submit" loading={mutation.isPending} className="w-full">Qo&apos;shish</Button>
    </form>
  );
}

function ExistingParentForm({ studentId, onLinked }: { studentId: string; onLinked: () => void }) {
  const [search, setSearch] = useState("");
  const debouncedSearch = useDebouncedValue(search);
  const [userId, setUserId] = useState("");
  const [relationship, setRelationship] = useState<ParentRelationship>("Guardian");

  const usersQuery = useQuery({
    queryKey: ["users", "parent-search", debouncedSearch],
    queryFn: () => listUsers({ role: "PARENT", search: debouncedSearch || undefined, size: 10 }),
  });
  const options = usersQuery.data?.items ?? [];

  const mutation = useMutation({
    mutationFn: () => linkExistingParent(studentId, { userId, relationship }),
    onSuccess: onLinked,
  });

  return (
    <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); mutation.mutate(); }}>
      <div>
        <Label htmlFor="parent-search">Ota-onani qidirish</Label>
        <Input id="parent-search" value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Ism, email yoki telefon" />
      </div>
      <div>
        <Label htmlFor="parent-existing">Hisob</Label>
        <Select id="parent-existing" required value={userId} onChange={(e) => setUserId(e.target.value)}>
          <option value="">Tanlang</option>
          {options.map((u) => (
            <option key={u.id} value={u.id}>{u.fullName} — {u.email}</option>
          ))}
        </Select>
        {usersQuery.data && options.length === 0 && (
          <p className="mt-1 text-[12px] text-ink-500">PARENT rolidagi hisob topilmadi.</p>
        )}
      </div>
      <RelationshipSelect id="parent-existing-relationship" value={relationship} onChange={setRelationship} />
      {mutation.isError && <Alert>{errorText(mutation.error, "Biriktirib bo'lmadi.")}</Alert>}
      <Button type="submit" loading={mutation.isPending} disabled={!userId} className="w-full">Biriktirish</Button>
    </form>
  );
}
