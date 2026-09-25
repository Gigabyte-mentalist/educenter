"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CheckCircle2, FileText, FileWarning, Library, RotateCcw, ScanEye, Trash2, Upload } from "lucide-react";
import {
  approveKnowledgeDocument, deleteKnowledgeDocument, getKnowledgeTargets, isKnowledgePending, listKnowledgeDocuments,
  previewKnowledgeDocument, reingestKnowledgeDocument, uploadKnowledgeDocument,
  KNOWLEDGE_SCOPE_LABEL, KNOWLEDGE_STATUS_LABEL, KNOWLEDGE_VISIBILITY_LABEL,
  type KnowledgeDocument, type KnowledgeScope, type KnowledgeStatus, type KnowledgeVisibility,
} from "@/lib/api/ai";
import { ApiError } from "@/lib/api-client";
import { PageHeader } from "@/components/ui/PageHeader";
import { Alert } from "@/components/ui/Alert";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { Card, CardBody, CardHeader, CardTitle } from "@/components/ui/Card";
import { Drawer } from "@/components/ui/Drawer";
import { EmptyState } from "@/components/ui/EmptyState";
import { Input, Label, Select } from "@/components/ui/Field";
import { Skeleton } from "@/components/ui/Skeleton";
import { Table, TBody, TD, TH, THead, TRow } from "@/components/ui/Table";

const STATUS_TONE: Record<KnowledgeStatus, "neutral" | "brand" | "success" | "danger" | "warning"> = {
  Uploaded: "neutral",
  Processing: "brand",
  Ready: "success",
  Quarantined: "danger",
  Failed: "warning",
};

const errorText = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);
const formatSize = (bytes: number) => (bytes < 1024 * 1024 ? `${Math.round(bytes / 1024)} KB` : `${(bytes / 1024 / 1024).toFixed(1)} MB`);

export default function KnowledgeBasePage() {
  const [previewId, setPreviewId] = useState<string | null>(null);

  const documentsQuery = useQuery({
    queryKey: ["ai", "knowledge", "documents"],
    queryFn: listKnowledgeDocuments,
    refetchInterval: (query) => (query.state.data?.some((d) => isKnowledgePending(d.status)) ? 3000 : false),
  });

  return (
    <main className="p-4 sm:p-6 lg:p-8">
      <PageHeader
        title="Bilim bazasi"
        description="AI Tutor javoblari shu materiallarga tayanadi. Yuklangan fayl matnga ajratiladi, xavfsizlik tekshiruvidan o'tadi, bo'laklarga bo'linib indekslanadi."
      />

      <div className="grid grid-cols-1 gap-6 xl:grid-cols-[340px_minmax(0,1fr)]">
        <UploadCard />

        <Card>
          <CardHeader>
            <CardTitle>Materiallar</CardTitle>
            {documentsQuery.data && <span className="text-[12.5px] text-ink-500">{documentsQuery.data.length} ta</span>}
          </CardHeader>
          {documentsQuery.isLoading && <CardBody><Skeleton className="h-40" /></CardBody>}
          {documentsQuery.isError && <CardBody><Alert>{errorText(documentsQuery.error, "Yuklab bo'lmadi.")}</Alert></CardBody>}
          {documentsQuery.data?.length === 0 && <EmptyState icon={Library} title="Hali material yuklanmagan" />}
          {documentsQuery.data && documentsQuery.data.length > 0 && (
            <div className="overflow-x-auto">
              <Table>
                <THead>
                  <TRow>
                    <TH>Sarlavha</TH>
                    <TH>Doira</TH>
                    <TH>Kim ko&apos;radi</TH>
                    <TH>Holat</TH>
                    <TH>Bo&apos;laklar</TH>
                    <TH>Amallar</TH>
                  </TRow>
                </THead>
                <TBody>
                  {documentsQuery.data.map((document) => (
                    <DocumentRow key={document.id} document={document} onPreview={() => setPreviewId(document.id)} />
                  ))}
                </TBody>
              </Table>
            </div>
          )}
        </Card>
      </div>

      <PreviewDrawer documentId={previewId} onClose={() => setPreviewId(null)} />
    </main>
  );
}

function DocumentRow({ document, onPreview }: { document: KnowledgeDocument; onPreview: () => void }) {
  const client = useQueryClient();
  const invalidate = () => client.invalidateQueries({ queryKey: ["ai", "knowledge"] });
  const approve = useMutation({ mutationFn: () => approveKnowledgeDocument(document.id), onSuccess: invalidate });
  const reingest = useMutation({ mutationFn: () => reingestKnowledgeDocument(document.id), onSuccess: invalidate });
  const remove = useMutation({ mutationFn: () => deleteKnowledgeDocument(document.id), onSuccess: invalidate });

  return (
    <TRow>
      <TD>
        <p className="font-medium text-ink-900">{document.title}</p>
        <p className="text-[12px] text-ink-500">
          {document.fileName} · {formatSize(document.sizeBytes)} · {document.uploadedByName}
        </p>
        {document.error && <p className="mt-1 max-w-sm text-[12px] text-danger-600">{document.error}</p>}
        {!document.fileAvailable && (
          <p className="mt-1 flex max-w-sm items-center gap-1 text-[12px] text-warning-600" title="Indekslangan matn ishlaydi; o'zgartirish uchun hujjatni qayta yuklang.">
            <FileWarning className="size-3.5 shrink-0" aria-hidden />
            Asl fayl yo&apos;qolgan
          </p>
        )}
      </TD>
      <TD>
        <span className="text-[13px] text-ink-700">{document.scopeName ?? KNOWLEDGE_SCOPE_LABEL[document.scope]}</span>
      </TD>
      <TD>
        <Badge tone={document.visibility === "Students" ? "brand" : "neutral"}>{KNOWLEDGE_VISIBILITY_LABEL[document.visibility]}</Badge>
      </TD>
      <TD>
        <Badge tone={STATUS_TONE[document.status]} dot>{KNOWLEDGE_STATUS_LABEL[document.status]}</Badge>
      </TD>
      <TD className="font-tabular">{document.chunkCount}</TD>
      <TD>
        <div className="flex items-center gap-1">
          <Button variant="ghost" size="sm" onClick={onPreview}><ScanEye className="size-3.5" aria-hidden />Matn</Button>
          {document.status === "Quarantined" && document.fileAvailable && !document.malware && (
            <Button variant="secondary" size="sm" loading={approve.isPending} onClick={() => approve.mutate()}>
              <CheckCircle2 className="size-3.5" aria-hidden />
              Tasdiqlash
            </Button>
          )}
          {(document.status === "Failed" || document.status === "Ready") && document.fileAvailable && (
            <Button variant="ghost" size="sm" loading={reingest.isPending} onClick={() => reingest.mutate()} aria-label="Qayta indekslash">
              <RotateCcw className="size-3.5" aria-hidden />
            </Button>
          )}
          <Button variant="ghost" size="sm" loading={remove.isPending} onClick={() => remove.mutate()} aria-label="O'chirish">
            <Trash2 className="size-3.5 text-danger-600" aria-hidden />
          </Button>
        </div>
      </TD>
    </TRow>
  );
}

function UploadCard() {
  const client = useQueryClient();
  const [file, setFile] = useState<File | null>(null);
  const [title, setTitle] = useState("");
  const [chosenScope, setChosenScope] = useState<KnowledgeScope | null>(null);
  const [visibility, setVisibility] = useState<KnowledgeVisibility>("Students");
  const [targetId, setTargetId] = useState("");

  // The server says what this person may attach material to: a teacher gets their groups and courses, never
  // "Butun tashkilot" — offering it only to answer "ruxsatingiz yo'q" after the upload is a dead end.
  const targetsQuery = useQuery({ queryKey: ["ai", "knowledge", "targets"], queryFn: getKnowledgeTargets });
  const allowedScopes = targetsQuery.data?.scopes ?? [];
  const scope: KnowledgeScope | null = chosenScope && allowedScopes.includes(chosenScope) ? chosenScope : (allowedScopes[0] ?? null);
  const targets = scope === "Branch" ? targetsQuery.data?.branches
    : scope === "Course" ? targetsQuery.data?.courses
    : scope === "Group" ? targetsQuery.data?.groups
    : undefined;

  const upload = useMutation({
    mutationFn: () =>
      uploadKnowledgeDocument({
        file: file!,
        title: title || file!.name,
        scope: scope!,
        visibility,
        branchId: scope === "Branch" ? targetId : undefined,
        courseId: scope === "Course" ? targetId : undefined,
        groupId: scope === "Group" ? targetId : undefined,
      }),
    onSuccess: () => {
      setFile(null);
      setTitle("");
      setTargetId("");
      client.invalidateQueries({ queryKey: ["ai", "knowledge"] });
    },
  });

  return (
    <Card>
      <CardHeader>
        <CardTitle>Material yuklash</CardTitle>
      </CardHeader>
      <CardBody>
        <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); if (file) upload.mutate(); }}>
          <div>
            <Label htmlFor="kb-file">Fayl</Label>
            <Input
              id="kb-file"
              type="file"
              accept=".txt,.md,.csv,.pdf,.docx,.pptx"
              onChange={(e) => setFile(e.target.files?.[0] ?? null)}
            />
            <p className="mt-1 text-[12px] text-ink-500">
              TXT, MD, CSV, PDF, DOCX yoki PPTX (slaydlar matni) — ko&apos;pi bilan 10 MB. Har bir fayl saqlashdan oldin xavfsizlik tekshiruvidan o&apos;tadi.
            </p>
          </div>
          <div>
            <Label htmlFor="kb-title">Sarlavha</Label>
            <Input id="kb-title" value={title} onChange={(e) => setTitle(e.target.value)} placeholder={file?.name ?? "Masalan: IELTS Writing qo'llanma"} />
          </div>
          <div>
            <Label htmlFor="kb-scope">Kimga tegishli</Label>
            <Select
              id="kb-scope"
              value={scope ?? ""}
              disabled={allowedScopes.length <= 1}
              onChange={(e) => { setChosenScope(e.target.value as KnowledgeScope); setTargetId(""); }}
            >
              {allowedScopes.map((s) => <option key={s} value={s}>{KNOWLEDGE_SCOPE_LABEL[s]}</option>)}
            </Select>
          </div>
          {scope !== null && scope !== "Organization" && (
            <div>
              <Label htmlFor="kb-target">{KNOWLEDGE_SCOPE_LABEL[scope]}</Label>
              <Select id="kb-target" value={targetId} onChange={(e) => setTargetId(e.target.value)} required>
                <option value="">Tanlang…</option>
                {targets?.map((target) => <option key={target.id} value={target.id}>{target.name}</option>)}
              </Select>
            </div>
          )}
          <div>
            <Label htmlFor="kb-visibility">Ko&apos;rinishi</Label>
            <Select id="kb-visibility" value={visibility} onChange={(e) => setVisibility(e.target.value as KnowledgeVisibility)}>
              <option value="Students">{KNOWLEDGE_VISIBILITY_LABEL.Students}</option>
              <option value="StaffOnly">{KNOWLEDGE_VISIBILITY_LABEL.StaffOnly}</option>
            </Select>
            <p className="mt-1 text-[12px] text-ink-500">«Faqat xodimlar» materiali AI Tutor javoblarida hech qachon ishlatilmaydi.</p>
          </div>

          {targetsQuery.isSuccess && allowedScopes.length === 0 && (
            <Alert>Sizga hali guruh biriktirilmagan, shuning uchun material qo&apos;sha olmaysiz.</Alert>
          )}
          {upload.isError && <Alert>{errorText(upload.error, "Yuklab bo'lmadi.")}</Alert>}
          <Button
            type="submit"
            className="w-full"
            loading={upload.isPending}
            disabled={!file || scope === null || (scope !== "Organization" && !targetId)}
          >
            <Upload className="size-4" aria-hidden />
            Yuklash va indekslash
          </Button>
        </form>
      </CardBody>
    </Card>
  );
}

function PreviewDrawer({ documentId, onClose }: { documentId: string | null; onClose: () => void }) {
  const query = useQuery({
    queryKey: ["ai", "knowledge", "preview", documentId],
    queryFn: () => previewKnowledgeDocument(documentId!),
    enabled: documentId !== null,
  });

  return (
    <Drawer open={documentId !== null} onClose={onClose} title="Hujjat matni" description="Indekslashdan oldin ajratilgan matn">
      {query.isLoading && <Skeleton className="h-64" />}
      {query.isError && <Alert>{errorText(query.error, "Matnni o'qib bo'lmadi.")}</Alert>}
      {query.data && (
        <>
          {query.data.restored && (
            <p className="mb-3 flex items-start gap-1.5 rounded-lg bg-warning-50 px-3 py-2 text-[12.5px] text-warning-600">
              <FileWarning className="mt-0.5 size-3.5 shrink-0" aria-hidden />
              Asl fayl saqlash joyida yo&apos;q — matn indeksdagi bo&apos;laklardan tiklandi.
            </p>
          )}
          <p className="whitespace-pre-wrap text-[13px] leading-relaxed text-ink-700">{query.data.text}</p>
          {query.data.truncated && (
            <p className="mt-3 flex items-center gap-1.5 text-[12.5px] text-ink-500">
              <FileText className="size-3.5" aria-hidden />
              Matn qisqartirildi — to&apos;liq hujjat indekslangan.
            </p>
          )}
        </>
      )}
    </Drawer>
  );
}
