"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { listBranches } from "@/lib/api/branches";
import { createComplaint } from "@/lib/api/complaints";
import { listStudents } from "@/lib/api/students";
import { ApiError } from "@/lib/api-client";
import { usePermissions } from "@/lib/use-permissions";
import { Alert } from "@/components/ui/Alert";
import { Button } from "@/components/ui/Button";
import { Drawer } from "@/components/ui/Drawer";
import { Input, Label, Select, Textarea } from "@/components/ui/Field";

const MAX_SUBJECT = 200;
const MAX_BODY = 4000;

/**
 * Filing a complaint (docs/00_TZ.md §41). What has to be asked depends on who is filing, and the server settles the rest: a student's is about
 * themselves; a parent with several children says which one; a teacher or an office worker says which branch when they work in more than one —
 * or files from a student's page, where the student (and so the branch) is already known.
 */
export function ComplaintCreateDrawer({ onClose, studentId, studentName }: { onClose: () => void; studentId?: string; studentName?: string }) {
  const queryClient = useQueryClient();
  const { scopeOf } = usePermissions();
  const scope = scopeOf("complaints.create");
  const isParent = scope === "MyChildren";
  const picksBranch = scope !== null && scope !== "Self" && !isParent && !studentId;

  const [subject, setSubject] = useState("");
  const [body, setBody] = useState("");
  const [childId, setChildId] = useState("");
  const [branchId, setBranchId] = useState("");

  const children = useQuery({ queryKey: ["students", "children"], queryFn: () => listStudents({ size: 50 }), enabled: isParent && !studentId });
  const branches = useQuery({ queryKey: ["branches"], queryFn: listBranches, enabled: picksBranch });

  // One child or one branch needs no question: the server takes the only one.
  const askChild = isParent && (children.data?.items.length ?? 0) > 1;
  const askBranch = picksBranch && (branches.data?.length ?? 0) > 1;

  const save = useMutation({
    mutationFn: () => createComplaint({
      subject: subject.trim(),
      body: body.trim(),
      studentId: studentId ?? (askChild ? childId : undefined),
      branchId: askBranch ? branchId : undefined,
    }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["complaints"] });
      onClose();
    },
  });

  const ready = subject.trim() !== "" && body.trim() !== "" && (!askChild || childId !== "") && (!askBranch || branchId !== "");

  return (
    <Drawer open onClose={onClose} title="Yangi shikoyat" description={studentName ? `${studentName} haqida` : "Filial administratoriga yuboriladi."}>
      <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); save.mutate(); }}>
        <div>
          <Label htmlFor="complaint-subject">Mavzu</Label>
          <Input id="complaint-subject" required maxLength={MAX_SUBJECT} value={subject} onChange={(e) => setSubject(e.target.value)}
            placeholder="Masalan: dars vaqti tez-tez o'zgaryapti" />
        </div>
        <div>
          <Label htmlFor="complaint-body">Nima bo&apos;ldi?</Label>
          <Textarea id="complaint-body" required rows={6} maxLength={MAX_BODY} value={body} onChange={(e) => setBody(e.target.value)} />
        </div>

        {askChild && (
          <div>
            <Label htmlFor="complaint-child">Qaysi farzand haqida</Label>
            <Select id="complaint-child" required value={childId} onChange={(e) => setChildId(e.target.value)}>
              <option value="">Tanlang</option>
              {children.data?.items.map((s) => <option key={s.id} value={s.id}>{s.fullName}</option>)}
            </Select>
          </div>
        )}
        {askBranch && (
          <div>
            <Label htmlFor="complaint-branch">Qaysi filial</Label>
            <Select id="complaint-branch" required value={branchId} onChange={(e) => setBranchId(e.target.value)}>
              <option value="">Tanlang</option>
              {branches.data?.map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}
            </Select>
          </div>
        )}

        <p className="text-[12.5px] text-ink-500">
          Shikoyatni filial administratori ko&apos;radi, xodimga biriktiradi va natijani sizga yozadi. Keyin uni baholaysiz.
        </p>
        {save.isError && <Alert>{save.error instanceof ApiError ? save.error.message : "Yuborib bo'lmadi."}</Alert>}
        <Button type="submit" loading={save.isPending} disabled={!ready} className="w-full">Yuborish</Button>
      </form>
    </Drawer>
  );
}
