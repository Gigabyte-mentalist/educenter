"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { Suspense, useState } from "react";
import { GraduationCap, ChevronRight, Search } from "lucide-react";
import { listStudents, type StudentStatus } from "@/lib/api/students";
import { ApiError } from "@/lib/api-client";
import { useDebouncedValue } from "@/lib/use-debounced-value";
import { PageHeader } from "@/components/ui/PageHeader";
import { StudentCreateButton } from "@/components/StudentCreateDrawer";
import { Badge } from "@/components/ui/Badge";
import { Avatar } from "@/components/ui/Avatar";
import { Table, THead, TBody, TRow, TH, TD } from "@/components/ui/Table";
import { TableSkeleton } from "@/components/ui/Skeleton";
import { EmptyState } from "@/components/ui/EmptyState";
import { Alert } from "@/components/ui/Alert";
import { Pagination } from "@/components/ui/Pagination";
import { Input, Select } from "@/components/ui/Field";

const STATUS_LABEL: Record<StudentStatus, string> = {
  Active: "Faol", Debt: "Qarzdor", Frozen: "Muzlatilgan", Transferred: "Ko'chirilgan", Graduated: "Bitirgan", Dropped: "Tark etgan",
};
const STATUS_TONE: Record<StudentStatus, "success" | "warning" | "neutral" | "brand" | "danger"> = {
  Active: "success", Debt: "warning", Frozen: "neutral", Transferred: "brand", Graduated: "brand", Dropped: "danger",
};
const PAGE_SIZE = 25;

export default function StudentsPage() {
  const [page, setPage] = useState(1);
  const [searchInput, setSearchInput] = useState("");
  const [status, setStatus] = useState<StudentStatus | "">("");
  const search = useDebouncedValue(searchInput);

  // keepPreviousData: the old rows stay on screen (dimmed) while the next page loads, instead of the table collapsing to a skeleton.
  const studentsQuery = useQuery({
    queryKey: ["students", { page, search, status }],
    queryFn: () => listStudents({ page, size: PAGE_SIZE, search, status: status || undefined }),
    placeholderData: keepPreviousData,
  });
  const data = studentsQuery.data;
  const filtering = search.trim() !== "" || status !== "";

  return (
    <main className="p-4 sm:p-6 lg:p-8">
      <PageHeader
        title="Studentlar"
        description="Enroll bo'lgan barcha studentlar. Batafsil ma'lumot uchun qatorni bosing."
        actions={<Suspense fallback={null}><StudentCreateButton /></Suspense>}
      />

      <div className="mb-4 flex flex-wrap items-center gap-2">
        <div className="relative w-full max-w-sm">
          <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-ink-400" aria-hidden />
          <Input
            value={searchInput}
            onChange={(e) => { setSearchInput(e.target.value); setPage(1); }}
            placeholder="Ism, email, telefon yoki ID bo'yicha qidirish"
            aria-label="Studentlarni qidirish"
            className="pl-9"
          />
        </div>
        <Select
          value={status}
          onChange={(e) => { setStatus(e.target.value as StudentStatus | ""); setPage(1); }}
          aria-label="Holat bo'yicha filtr"
          className="w-auto min-w-40"
        >
          <option value="">Barcha holatlar</option>
          {(Object.keys(STATUS_LABEL) as StudentStatus[]).map((s) => <option key={s} value={s}>{STATUS_LABEL[s]}</option>)}
        </Select>
      </div>

      {studentsQuery.isError && (
        <Alert>{studentsQuery.error instanceof ApiError ? studentsQuery.error.message : "Yuklab bo'lmadi."}</Alert>
      )}

      {studentsQuery.isPending && (
        <div className="rounded-xl border border-border bg-surface"><TableSkeleton rows={5} cols={4} /></div>
      )}

      {data && data.total === 0 && (
        <div className="rounded-xl border border-border bg-surface">
          <EmptyState
            icon={GraduationCap}
            title={filtering ? "Hech narsa topilmadi" : "Hali student yo'q"}
            description={filtering ? "Qidiruv yoki filtrni o'zgartirib ko'ring." : "«Yangi student» tugmasi bilan qo'shing yoki lidlar sahifasidan mijozni studentga aylantiring."}
          />
        </div>
      )}

      {data && data.total > 0 && (
        <div className={studentsQuery.isPlaceholderData ? "opacity-60 transition-opacity" : "transition-opacity"}>
          <div className="overflow-hidden rounded-xl border border-border bg-surface">
            <Table bare>
              <THead>
                <tr>
                  <TH>Student</TH>
                  <TH>Student ID</TH>
                  <TH>Daraja</TH>
                  <TH>Holat</TH>
                  <TH />
                </tr>
              </THead>
              <TBody>
                {data.items.map((student) => (
                  <TRow key={student.id}>
                    <TD colSpan={1}>
                      <Link href={`/students/${student.id}`} className="flex items-center gap-2.5">
                        <Avatar name={student.fullName} />
                        <div>
                          <p className="font-medium text-ink-900">{student.fullName}</p>
                          <p className="text-[12.5px] text-ink-500">{student.email}</p>
                        </div>
                      </Link>
                    </TD>
                    <TD className="font-mono text-[12.5px]">{student.studentNumber}</TD>
                    <TD className="text-ink-600">{student.currentLevel ?? "—"}</TD>
                    <TD>
                      <Badge tone={STATUS_TONE[student.status]} dot>{STATUS_LABEL[student.status]}</Badge>
                    </TD>
                    <TD>
                      <Link href={`/students/${student.id}`} className="flex justify-end text-ink-400 hover:text-brand-600" aria-label={`${student.fullName} profili`}>
                        <ChevronRight className="size-4" aria-hidden />
                      </Link>
                    </TD>
                  </TRow>
                ))}
              </TBody>
            </Table>
            <Pagination page={data.pageNumber} pageCount={data.pageCount} total={data.total} pageSize={data.pageSize} onChange={setPage} />
          </div>
        </div>
      )}
    </main>
  );
}
