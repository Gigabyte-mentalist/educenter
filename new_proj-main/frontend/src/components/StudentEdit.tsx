"use client";

import { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Pencil } from "lucide-react";
import { updateStudent, updateStudentContact, type Student } from "@/lib/api/students";
import { ApiError } from "@/lib/api-client";
import { usePermissions } from "@/lib/use-permissions";
import { Drawer } from "@/components/ui/Drawer";
import { Button } from "@/components/ui/Button";
import { Alert } from "@/components/ui/Alert";
import { Input, Label } from "@/components/ui/Field";

/**
 * Corrects a student's profile. Two permissions, one screen: `students.edit` (administrators) changes anything on the
 * profile, `students.contact.edit` (operators too) only the name and the phone number — the form shows just the
 * fields the caller's permission would let the API accept, rather than offering ones that would end in a 403.
 */
export function StudentEditButton({ student }: { student: Student }) {
  const { can } = usePermissions();
  const [open, setOpen] = useState(false);
  const canEditAll = can("students.edit");
  if (!canEditAll && !can("students.contact.edit")) return null;

  return (
    <>
      <Button size="sm" variant="secondary" onClick={() => setOpen(true)}>
        <Pencil className="size-3.5" aria-hidden />
        Tahrirlash
      </Button>
      {open && <StudentEditDrawer student={student} canEditAll={canEditAll} onClose={() => setOpen(false)} />}
    </>
  );
}

function StudentEditDrawer({ student, canEditAll, onClose }: { student: Student; canEditAll: boolean; onClose: () => void }) {
  const queryClient = useQueryClient();
  // The drawer is mounted only while open, so these start from the student as they are now on every opening.
  const [fullName, setFullName] = useState(student.fullName);
  const [phone, setPhone] = useState(student.phone);
  const [level, setLevel] = useState(student.currentLevel ?? "");
  const [joinedDate, setJoinedDate] = useState(student.joinedDate);
  const [studentNumber, setStudentNumber] = useState(student.studentNumber);

  const saveMutation = useMutation({
    mutationFn: () => canEditAll
      ? updateStudent(student.id, { fullName, phone, currentLevel: level, joinedDate, studentNumber })
      : updateStudentContact(student.id, { fullName, phone }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["students"] });
      onClose();
    },
  });

  return (
    <Drawer open onClose={onClose} title="Student ma'lumotlarini tahrirlash" description={student.fullName}>
      <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); saveMutation.mutate(); }}>
        <div>
          <Label htmlFor="edit-name">Ism-familiya</Label>
          <Input id="edit-name" required maxLength={200} value={fullName} onChange={(e) => setFullName(e.target.value)} />
        </div>
        <div>
          <Label htmlFor="edit-phone">Telefon</Label>
          <Input id="edit-phone" type="tel" required maxLength={30} value={phone} onChange={(e) => setPhone(e.target.value)} />
        </div>
        {canEditAll ? (
          <>
            <div className="grid grid-cols-2 gap-3">
              <div>
                <Label htmlFor="edit-level">Daraja</Label>
                <Input id="edit-level" maxLength={50} value={level} onChange={(e) => setLevel(e.target.value)} />
              </div>
              <div>
                <Label htmlFor="edit-joined">Qo&apos;shilgan sana</Label>
                <Input id="edit-joined" type="date" required value={joinedDate} onChange={(e) => setJoinedDate(e.target.value)} />
              </div>
            </div>
            <div>
              <Label htmlFor="edit-number">Student raqami</Label>
              <Input id="edit-number" required maxLength={50} value={studentNumber} onChange={(e) => setStudentNumber(e.target.value)} />
            </div>
          </>
        ) : (
          <p className="text-[12.5px] text-ink-500">Sizga faqat ism va telefonni tuzatish ruxsat etilgan.</p>
        )}
        <p className="text-[12px] text-ink-500">Login (email) bu yerdan o&apos;zgarmaydi.</p>
        {saveMutation.isError && (
          <Alert>{saveMutation.error instanceof ApiError ? saveMutation.error.message : "Saqlab bo'lmadi."}</Alert>
        )}
        <Button type="submit" loading={saveMutation.isPending} className="w-full">Saqlash</Button>
      </form>
    </Drawer>
  );
}
