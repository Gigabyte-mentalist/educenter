"use client";

import Link from "next/link";
import { useQuery } from "@tanstack/react-query";
import { UsersRound } from "lucide-react";
import { getTeacherSupport, type StaffLink } from "@/lib/api/teachers";
import { ApiError } from "@/lib/api-client";
import { Alert } from "@/components/ui/Alert";
import { Button } from "@/components/ui/Button";
import { EmptyState } from "@/components/ui/EmptyState";
import { Skeleton } from "@/components/ui/Skeleton";
import { Table, THead, TBody, TRow, TH, TD } from "@/components/ui/Table";

/** Yordamchilar tab: who assists this teacher in their groups and, if they are an assistant themselves, whose groups they help with. */
export function TeacherSupportTab({ teacherId }: { teacherId: string }) {
  const query = useQuery({ queryKey: ["teachers", teacherId, "support"], queryFn: () => getTeacherSupport(teacherId) });
  if (query.isPending) return <Skeleton className="h-40" />;
  if (query.isError) {
    return (
      <>
        <Alert>{query.error instanceof ApiError ? query.error.message : "Ma‘lumotni yuklab bo‘lmadi."}</Alert>
        <Button onClick={() => void query.refetch()}>Qayta urinish</Button>
      </>
    );
  }
  const { supportTeachers, assistsIn } = query.data;
  if (supportTeachers.length === 0 && assistsIn.length === 0)
    return <EmptyState icon={UsersRound} title="Yordamchi o‘qituvchilar yo‘q" description="Guruhga yordamchi o‘qituvchi biriktirilganda shu yerda ko‘rinadi." />;

  return (
    <div className="space-y-6">
      {supportTeachers.length > 0 && <Section title="Guruhlaridagi yordamchilar" rows={supportTeachers} colleague="Yordamchi" />}
      {assistsIn.length > 0 && <Section title="Yordam beradigan guruhlari" rows={assistsIn} colleague="Asosiy o‘qituvchi" />}
    </div>
  );
}

function Section({ title, rows, colleague }: { title: string; rows: StaffLink[]; colleague: string }) {
  return (
    <section>
      <h3 className="mb-2 text-[13.5px] font-semibold text-ink-800">{title}</h3>
      <Table>
        <THead><tr><TH>{colleague}</TH><TH>Guruh</TH></tr></THead>
        <TBody>{rows.map((r) => (
          <TRow key={`${r.userId}-${r.groupId}`}>
            <TD className="font-medium text-ink-900">{r.fullName}</TD>
            <TD><Link className="text-brand-700 hover:underline" href={`/groups/${r.groupId}`}>{r.groupName}</Link></TD>
          </TRow>
        ))}</TBody>
      </Table>
    </section>
  );
}
