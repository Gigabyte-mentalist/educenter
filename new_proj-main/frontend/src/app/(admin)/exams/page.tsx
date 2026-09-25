"use client";

import { useState } from "react";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Award } from "lucide-react";
import { listGroups, listGroupStudents } from "@/lib/api/academic";
import { createExam, listExams, listExamResults, recordExamResult, type Exam, type ExamType, type ExamScores } from "@/lib/api/lms";
import { useAuthSession } from "@/lib/use-auth-session";
import { useDebouncedValue } from "@/lib/use-debounced-value";
import { ApiError } from "@/lib/api-client";
import { ExamRowActions, RemoveResultButton } from "@/components/ExamActions";
import { Pagination } from "@/components/ui/Pagination";
import { PageHeader } from "@/components/ui/PageHeader";
import { Button } from "@/components/ui/Button";
import { Input, Label, Select } from "@/components/ui/Field";
import { Drawer } from "@/components/ui/Drawer";
import { Alert } from "@/components/ui/Alert";
import { EmptyState } from "@/components/ui/EmptyState";
import { TableSkeleton } from "@/components/ui/Skeleton";
import { Table, THead, TBody, TRow, TH, TD } from "@/components/ui/Table";

const types: Record<ExamType, string> = { Placement: "Darajani aniqlash", Unit: "Bo‘lim imtihoni", Monthly: "Oylik", MockIelts: "Mock IELTS", Final: "Yakuniy", Custom: "Boshqa" };
const bandSections = ["listening", "reading", "writing", "speaking", "overall"] as const;
/** Percentages, not IELTS bands — an IELTS-type exam simply leaves them empty. */
const percentSections = ["grammar", "vocabulary"] as const;
const sections = [...bandSections, ...percentSections] as const;
const sectionLabels = {
  listening: "Listening", reading: "Reading", writing: "Writing", speaking: "Speaking", overall: "Overall",
  grammar: "Grammatika %", vocabulary: "Lug‘at %",
};
const PAGE_SIZE = 20;
const errorText = (error: Error) => error instanceof ApiError ? error.message : "Amalni bajarib bo‘lmadi. Qayta urinib ko‘ring.";

export default function ExamsPage() {
  const role = useAuthSession().user?.role ?? null;
  const canEdit = role === "SUPER_ADMIN" || role === "ADMINISTRATOR" || role === "TEACHER";
  const [groupId, setGroupId] = useState("");
  const [page, setPage] = useState(1);
  const [searchInput, setSearchInput] = useState("");
  const search = useDebouncedValue(searchInput);
  const [creating, setCreating] = useState(false);
  const [selected, setSelected] = useState<Exam | null>(null);
  const groups = useQuery({ queryKey: ["groups"], queryFn: listGroups });
  const exams = useQuery({
    queryKey: ["exams", { groupId, search, page }],
    queryFn: () => listExams({ groupId: groupId || undefined, search, page, size: PAGE_SIZE }),
    placeholderData: keepPreviousData,
  });
  const data = exams.data;
  const filtering = groupId !== "" || search.trim() !== "";

  return <main className="p-4 sm:p-6 lg:p-8">
    <PageHeader title="Imtihonlar" description="Guruh imtihonlari va studentlarning natijalari." actions={canEdit && <Button onClick={() => setCreating(true)}>Imtihon yaratish</Button>} />
    <div className="mb-5 flex flex-wrap items-end gap-3">
      <div className="w-full max-w-xs"><Label htmlFor="exam-search">Imtihon nomi</Label><Input id="exam-search" value={searchInput} onChange={e => { setSearchInput(e.target.value); setPage(1); }} placeholder="Qidirish" /></div>
      <div className="w-full max-w-xs"><Label htmlFor="exam-filter">Guruh</Label><Select id="exam-filter" value={groupId} onChange={e => { setGroupId(e.target.value); setPage(1); }}>
        <option value="">Barcha guruhlar</option>{groups.data?.map(g => <option key={g.id} value={g.id}>{g.name}</option>)}
      </Select></div>
    </div>
    {groups.isError && <Alert>{errorText(groups.error)}</Alert>}
    {exams.isError && <div className="space-y-2"><Alert>{errorText(exams.error)}</Alert><Button onClick={() => void exams.refetch()}>Qayta urinish</Button></div>}
    {exams.isPending && <TableSkeleton rows={4} cols={4} />}
    {data?.total === 0 && <EmptyState icon={Award} title={filtering ? "Hech narsa topilmadi" : "Hali imtihon yo‘q"} />}
    {!!data?.total && <div className={exams.isPlaceholderData ? "opacity-60 transition-opacity" : "transition-opacity"}>
      <div className="overflow-hidden rounded-xl border border-border bg-surface">
        <Table bare><THead><tr><TH>Imtihon</TH><TH>Guruh</TH><TH>Sana</TH><TH>Tur</TH><TH>Natijalar</TH></tr></THead>
          <TBody>{data.items.map(exam => <TRow key={exam.id}><TD>{exam.name}</TD><TD>{groups.data?.find(g => g.id === exam.groupId)?.name ?? "—"}</TD><TD>{exam.date}</TD><TD>{types[exam.type]}</TD><TD><div className="flex flex-wrap items-center gap-1"><Button variant="secondary" size="sm" onClick={() => setSelected(exam)}>Natijalarni ochish</Button>{canEdit && <ExamRowActions exam={exam} typeLabels={types} />}</div></TD></TRow>)}</TBody>
        </Table>
        <Pagination page={data.pageNumber} pageCount={data.pageCount} total={data.total} pageSize={data.pageSize} onChange={setPage} />
      </div>
    </div>}
    {creating && canEdit && <CreateExamDrawer initialGroupId={groupId} onClose={() => setCreating(false)} onCreated={exam => { setCreating(false); setSelected(exam); }} />}
    {selected && <ExamResults key={selected.id} exam={selected} canEdit={canEdit} onClose={() => setSelected(null)} />}
  </main>;
}

function CreateExamDrawer({ initialGroupId, onClose, onCreated }: { initialGroupId: string; onClose: () => void; onCreated: (exam: Exam) => void }) {
  const client = useQueryClient();
  const groups = useQuery({ queryKey: ["groups"], queryFn: listGroups });
  const mutation = useMutation({ mutationFn: createExam, onSuccess: async exam => {
    await client.invalidateQueries({ queryKey: ["exams"] }); onCreated(exam);
  } });
  return <Drawer open onClose={() => { if (!mutation.isPending) onClose(); }} title="Imtihon yaratish">
    <form className="space-y-4" onSubmit={e => {
      e.preventDefault(); const form = new FormData(e.currentTarget);
      mutation.mutate({ groupId: String(form.get("groupId")), name: String(form.get("name")).trim(), type: String(form.get("type")) as ExamType, date: String(form.get("date")) });
    }}>
      <div><Label htmlFor="exam-name">Nomi</Label><Input id="exam-name" name="name" required maxLength={200} /></div>
      <div><Label htmlFor="exam-group">Guruh</Label><Select id="exam-group" name="groupId" required defaultValue={initialGroupId}><option value="">Tanlang</option>{groups.data?.map(g => <option key={g.id} value={g.id}>{g.name}</option>)}</Select></div>
      <div><Label htmlFor="exam-type">Turi</Label><Select id="exam-type" name="type" defaultValue="MockIelts">{Object.entries(types).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</Select></div>
      <div><Label htmlFor="exam-date">Sana</Label><Input id="exam-date" name="date" type="date" required /></div>
      {groups.isError && <Alert>{errorText(groups.error)}</Alert>}
      {mutation.isError && <Alert>{errorText(mutation.error)}</Alert>}
      <Button type="submit" loading={mutation.isPending} disabled={!groups.data?.length}>Yaratish</Button>
    </form>
  </Drawer>;
}

function ExamResults({ exam, canEdit, onClose }: { exam: Exam; canEdit: boolean; onClose: () => void }) {
  const client = useQueryClient();
  const results = useQuery({ queryKey: ["exam-results", exam.id], queryFn: () => listExamResults(exam.id) });
  const roster = useQuery({ queryKey: ["group-students", exam.groupId], queryFn: () => listGroupStudents(exam.groupId), enabled: canEdit });
  const [message, setMessage] = useState("");
  const [formKey, setFormKey] = useState(0);
  const mutation = useMutation({ mutationFn: (input: ExamScores & { studentId: string }) => recordExamResult(exam.id, input), onSuccess: async () => {
    await client.invalidateQueries({ queryKey: ["exam-results", exam.id] });
    setMessage("Natija saqlandi."); setFormKey(key => key + 1);
  } });
  const ungraded = roster.data?.filter(s => !results.data?.some(r => r.studentId === s.studentId)) ?? [];
  return <Drawer open title={exam.name} description={`${exam.date} · ${types[exam.type]}`} onClose={() => { if (!mutation.isPending) onClose(); }}>
    <div className="space-y-5">
      {results.isPending && <TableSkeleton rows={3} cols={2} />}
      {results.isError && <><Alert>{errorText(results.error)}</Alert><Button onClick={() => void results.refetch()}>Qayta urinish</Button></>}
      {results.data?.length === 0 && <p className="text-sm text-ink-500">Hali natija kiritilmagan.</p>}
      {results.data?.map(result => <div key={result.id} className="rounded-lg border border-border p-3">
        <div className="mb-2 flex items-start justify-between gap-2">
          <p className="font-medium">{result.studentName ?? "Student"}</p>
          {canEdit && <RemoveResultButton examId={exam.id} resultId={result.id} studentName={result.studentName ?? "Student"} />}
        </div>
        <dl className="grid grid-cols-2 gap-2 text-sm">{sections.map(section => <div key={section}><dt className="text-ink-500">{sectionLabels[section]}</dt><dd>{result[section] ?? "—"}</dd></div>)}</dl>
      </div>)}
      {canEdit && roster.isPending && <p>Studentlar yuklanmoqda...</p>}
      {canEdit && roster.isError && <Alert>{errorText(roster.error)}</Alert>}
      {canEdit && results.isSuccess && roster.isSuccess && ungraded.length === 0 && <p className="text-sm text-ink-500">Natija kiritilmagan faol student qolmagan.</p>}
      {message && <p role="status" className="text-sm text-brand-700">{message}</p>}
      {canEdit && results.isSuccess && ungraded.length > 0 && <form key={formKey} className="space-y-4 border-t border-border pt-4" onSubmit={e => {
        e.preventDefault(); setMessage(""); const form = new FormData(e.currentTarget);
        const scores = Object.fromEntries(sections.map(section => [section, form.get(section) === "" ? null : Number(form.get(section))])) as unknown as ExamScores;
        if (sections.every(section => scores[section] === null)) { setMessage("Kamida bitta ball kiriting."); return; }
        mutation.mutate({ studentId: String(form.get("studentId")), ...scores });
      }}>
        <div><Label htmlFor="result-student">Student</Label><Select id="result-student" name="studentId" required defaultValue=""><option value="">Tanlang</option>{ungraded.map(s => <option key={s.studentId} value={s.studentId}>{s.fullName}</option>)}</Select></div>
        <p className="text-sm text-ink-500">Ballar 0–9 oralig‘ida, Grammatika va Lug‘at foizda (0–100). Baholanmagan bo‘limni bo‘sh qoldiring. Overall alohida kiritiladi.</p>
        {bandSections.map(section => <div key={section}><Label htmlFor={`score-${section}`}>{sectionLabels[section]}</Label><Input id={`score-${section}`} name={section} type="number" min={0} max={9} step="any" /></div>)}
        {percentSections.map(section => <div key={section}><Label htmlFor={`score-${section}`}>{sectionLabels[section]}</Label><Input id={`score-${section}`} name={section} type="number" min={0} max={100} step="any" /></div>)}
        {mutation.isError && <Alert>{errorText(mutation.error)}</Alert>}
        <Button type="submit" loading={mutation.isPending}>Natijani saqlash</Button>
      </form>}
    </div>
  </Drawer>;
}
