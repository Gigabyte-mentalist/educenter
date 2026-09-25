"use client";

import { useState } from "react";
import Link from "next/link";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CheckCircle2, CreditCard, KeyRound, Plus, RefreshCw, UsersRound } from "lucide-react";
import { listGroups } from "@/lib/api/academic";
import { createStudent, type Student } from "@/lib/api/students";
import { ApiError } from "@/lib/api-client";
import { generatePassword } from "@/lib/generate-password";
import { usePermissions } from "@/lib/use-permissions";
import { useNewParam } from "@/lib/use-new-param";
import { Drawer } from "@/components/ui/Drawer";
import { Button } from "@/components/ui/Button";
import { Alert } from "@/components/ui/Alert";
import { Input, Label, Select } from "@/components/ui/Field";

/** "Yangi student" — shown only to whoever holds `students.create`. */
export function StudentCreateButton() {
  const { can } = usePermissions();
  const [open, setOpen] = useState(false);
  // `?new=1` (the command palette's "Yangi student") opens the form too.
  const asked = useNewParam();
  if (!can("students.create")) return null;

  return (
    <>
      <Button onClick={() => setOpen(true)}>
        <Plus className="size-4" aria-hidden />
        Yangi student
      </Button>
      {(open || asked.requested) && <StudentCreateDrawer onClose={() => { setOpen(false); asked.clear(); }} />}
    </>
  );
}

/**
 * Registers a student without a lead — a centre moving over from another system has hundreds who never were one. After a
 * save the form clears the person but keeps the group, level and join date, so a whole class can be entered in a row.
 * Branch staff must name a group (the API refuses otherwise: an ungrouped student would not show in their branch), so the
 * field is required unless the caller holds the whole organization.
 */
function StudentCreateDrawer({ onClose }: { onClose: () => void }) {
  const queryClient = useQueryClient();
  const { scopeOf } = usePermissions();
  const groupRequired = scopeOf("students.create") !== "AllOrganization";
  const groupsQuery = useQuery({ queryKey: ["groups"], queryFn: listGroups });

  const [groupId, setGroupId] = useState("");
  const [level, setLevel] = useState("");
  const [joinedDate, setJoinedDate] = useState("");
  const [fullName, setFullName] = useState("");
  const [phone, setPhone] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [studentNumber, setStudentNumber] = useState("");
  const [created, setCreated] = useState<{ student: Student; password: string; groupName?: string } | null>(null);

  const openGroups = groupsQuery.data?.filter((g) => g.status === "Active" || g.status === "Forming") ?? [];

  const createMutation = useMutation({
    mutationFn: () => createStudent({
      fullName, phone, email, password,
      groupId: groupId || undefined,
      currentLevel: level || undefined,
      joinedDate: joinedDate || undefined,
      studentNumber: studentNumber || undefined,
    }),
    onSuccess: (student) => {
      queryClient.invalidateQueries({ queryKey: ["students"] });
      queryClient.invalidateQueries({ queryKey: ["groups"] });
      setCreated({ student, password, groupName: groupsQuery.data?.find((g) => g.id === groupId)?.name });
    },
  });

  function addAnother() {
    setFullName(""); setPhone(""); setEmail(""); setPassword(""); setStudentNumber("");
    createMutation.reset();
    setCreated(null);
  }

  if (created) {
    return (
      <Drawer open onClose={onClose} title="Student yaratildi" description={created.student.fullName}>
        <div className="space-y-5">
          <div className="flex items-start gap-3 rounded-xl bg-success-50 p-4">
            <CheckCircle2 className="mt-0.5 size-5 shrink-0 text-success-600" aria-hidden />
            <div className="text-[13px] text-ink-700">
              <p className="font-medium text-ink-900">{created.student.fullName} qo&apos;shildi</p>
              <p className="mt-1">
                Raqami <span className="font-mono">{created.student.studentNumber}</span>
                {created.groupName && <> · guruh <strong>{created.groupName}</strong></>}
              </p>
            </div>
          </div>
          <div className="space-y-1.5 rounded-xl border border-border p-4 text-[13px] text-ink-700">
            <p className="font-medium text-ink-900">Studentga aytib qo&apos;ying</p>
            <p>Login: <span className="font-mono">{created.student.email}</span></p>
            <p>Parol: <span className="font-mono">{created.password}</span></p>
            <p className="pt-1 text-[12px] text-ink-500">Parol bu oynani yopgach ko&apos;rsatilmaydi.</p>
          </div>
          <div className="flex flex-col gap-2">
            <Button onClick={addAnother}><Plus className="size-4" aria-hidden />Yana bitta student qo&apos;shish</Button>
            <Link href={`/students/${created.student.id}`}><Button variant="secondary" className="w-full">Student profilini ochish</Button></Link>
            <Button variant="secondary" onClick={onClose}>Yopish</Button>
          </div>
        </div>
      </Drawer>
    );
  }

  return (
    <Drawer open onClose={onClose} title="Yangi student" description="Lidsiz to'g'ridan-to'g'ri ro'yxatga olish">
      <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); createMutation.mutate(); }}>
        <ul className="space-y-2 rounded-xl bg-ink-50 p-3.5 text-[12.5px] text-ink-700">
          <li className="flex items-start gap-2"><KeyRound className="mt-0.5 size-3.5 shrink-0 text-ink-400" aria-hidden />Tizimga kirish uchun login akkaunt yaratiladi</li>
          <li className="flex items-start gap-2"><UsersRound className="mt-0.5 size-3.5 shrink-0 text-ink-400" aria-hidden />Tanlangan guruhga yoziladi</li>
          <li className="flex items-start gap-2">
            <CreditCard className="mt-0.5 size-3.5 shrink-0 text-ink-400" aria-hidden />
            Guruhga yozilganda joriy oy uchun to&apos;lov hisobi ochiladi — u allaqachon to&apos;lagan bo&apos;lsa, Moliya bo&apos;limida bekor qiling
          </li>
        </ul>

        <div>
          <Label htmlFor="student-group">Guruh{groupRequired ? "" : " (ixtiyoriy)"}</Label>
          <Select id="student-group" required={groupRequired} value={groupId} onChange={(e) => setGroupId(e.target.value)}>
            <option value="">{groupRequired ? "Tanlang" : "Guruhsiz"}</option>
            {openGroups.map((g) => {
              const free = g.capacity - g.studentCount;
              return (
                <option key={g.id} value={g.id} disabled={free <= 0}>
                  {g.name} — {free > 0 ? `${free} ta o'rin` : "to'lgan"}
                </option>
              );
            })}
          </Select>
          {!groupRequired && (
            <p className="mt-1 text-[12px] text-ink-500">Guruhsiz student filial xodimlariga guruhga yozilgunicha ko&apos;rinmaydi.</p>
          )}
        </div>

        <div className="grid grid-cols-2 gap-3">
          <div>
            <Label htmlFor="student-level">Daraja</Label>
            <Input id="student-level" value={level} onChange={(e) => setLevel(e.target.value)} placeholder="B1, IELTS…" />
          </div>
          <div>
            <Label htmlFor="student-joined">Qo&apos;shilgan sana</Label>
            <Input id="student-joined" type="date" value={joinedDate} onChange={(e) => setJoinedDate(e.target.value)} />
          </div>
        </div>
        <p className="-mt-2 text-[12px] text-ink-500">Sana bo&apos;sh bo&apos;lsa — bugun. Boshqa tizimdan ko&apos;chirayotgan bo&apos;lsangiz haqiqiy sanani yozing.</p>

        <div>
          <Label htmlFor="student-name">Ism-familiya</Label>
          <Input id="student-name" required maxLength={200} value={fullName} onChange={(e) => setFullName(e.target.value)} />
        </div>
        <div>
          <Label htmlFor="student-phone">Telefon</Label>
          <Input id="student-phone" type="tel" required maxLength={30} value={phone} onChange={(e) => setPhone(e.target.value)} placeholder="+998 90 123 45 67" />
        </div>
        <div>
          <Label htmlFor="student-email">Login uchun email</Label>
          <Input id="student-email" type="email" required maxLength={200} value={email} onChange={(e) => setEmail(e.target.value)} />
        </div>
        <div>
          <Label htmlFor="student-password">Vaqtinchalik parol</Label>
          <div className="flex gap-2">
            <Input id="student-password" type="text" required minLength={8} value={password} onChange={(e) => setPassword(e.target.value)} />
            <Button type="button" variant="secondary" onClick={() => setPassword(generatePassword())} aria-label="Parol yaratish">
              <RefreshCw className="size-4" aria-hidden />
            </Button>
          </div>
          <p className="mt-1 text-[12px] text-ink-500">Kamida 8 belgi. Saqlagandan keyin talabaga aytish uchun ko&apos;rsatiladi.</p>
        </div>
        <div>
          <Label htmlFor="student-number">Student raqami (ixtiyoriy)</Label>
          <Input id="student-number" maxLength={50} value={studentNumber} onChange={(e) => setStudentNumber(e.target.value)} placeholder="Bo'sh qoldirsangiz avtomatik beriladi" />
        </div>

        {createMutation.isError && (
          <Alert>{createMutation.error instanceof ApiError ? createMutation.error.message : "Studentni yaratib bo'lmadi."}</Alert>
        )}
        <Button type="submit" loading={createMutation.isPending} className="w-full">Studentni yaratish</Button>
      </form>
    </Drawer>
  );
}
