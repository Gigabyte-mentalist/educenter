"use client";

import Link from "next/link";
import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Users2 } from "lucide-react";
import { listTeachers } from "@/lib/api/teachers";
import { ApiError } from "@/lib/api-client";
import { PageHeader } from "@/components/ui/PageHeader";
import { Table, THead, TBody, TRow, TH, TD } from "@/components/ui/Table";
import { Input, Label } from "@/components/ui/Field";
import { Badge } from "@/components/ui/Badge";
import { Alert } from "@/components/ui/Alert";
import { Button } from "@/components/ui/Button";
import { EmptyState } from "@/components/ui/EmptyState";
import { TableSkeleton } from "@/components/ui/Skeleton";
import { TeacherHeadline } from "@/components/teachers/TeacherHeadline";

export default function TeachersPage() {
  const query = useQuery({ queryKey: ["teachers"], queryFn: listTeachers });
  const [search, setSearch] = useState("");
  const rows = query.data?.filter(t => `${t.fullName} ${t.email}`.toLowerCase().includes(search.trim().toLowerCase()));
  return <main className="p-4 sm:p-6 lg:p-8">
    <PageHeader title="O‘qituvchilar" description="Asosiy va yordamchi o‘qituvchilarning guruhlari, studentlari va jadvali." />
    <div className="mb-5 max-w-sm"><Label htmlFor="teacher-search">Ism yoki email bo‘yicha qidirish</Label><Input id="teacher-search" value={search} onChange={e => setSearch(e.target.value)} /></div>
    {query.isPending && <TableSkeleton rows={4} cols={3} />}
    {query.isError && <><Alert>{query.error instanceof ApiError ? query.error.message : "Yuklab bo‘lmadi."}</Alert><Button onClick={() => void query.refetch()}>Qayta urinish</Button></>}
    {rows?.length === 0 && <EmptyState icon={Users2} title="O‘qituvchi topilmadi" />}
    {!!rows?.length && <Table><THead><tr><TH>O‘qituvchi</TH><TH>Email</TH><TH>Holat</TH></tr></THead><TBody>{rows.map(t => <TRow key={t.id}>
      <TD>{t.canOpen
        ? <Link className="font-medium text-brand-700 underline decoration-brand-300 underline-offset-2 hover:decoration-brand-700" href={`/teachers/${t.id}`}>{t.fullName}</Link>
        : <span className="font-medium text-ink-900">{t.fullName}</span>}
        {t.specialization && <span className="block text-[12.5px] text-ink-500">{t.specialization}</span>}
        <TeacherHeadline chips={t.headline} max={2} compact /></TD><TD>{t.email}</TD><TD><Badge tone={t.status === "Active" ? "success" : "neutral"}>{t.status === "Active" ? "Faol" : t.status === "Suspended" ? "To‘xtatilgan" : "Faol emas"}</Badge></TD>
    </TRow>)}</TBody></Table>}
  </main>;
}
