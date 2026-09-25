"use client";

import { useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { FileText, Download, ExternalLink, FolderOpen, Link2, Paperclip, Trash2, Upload } from "lucide-react";
import {
  createStudentDocument, deleteStudentDocument, fetchStudentDocumentFile, listStudentDocuments, uploadStudentDocument,
  STUDENT_DOCUMENT_FILE_ACCEPT, STUDENT_DOCUMENT_MAX_BYTES, type StudentDocument,
} from "@/lib/api/students";
import { ApiError } from "@/lib/api-client";
import { Alert } from "@/components/ui/Alert";
import { Button } from "@/components/ui/Button";
import { Drawer } from "@/components/ui/Drawer";
import { ConfirmDialog } from "@/components/ui/ConfirmDialog";
import { Input, Label, Textarea } from "@/components/ui/Field";
import { EmptyState } from "@/components/ui/EmptyState";
import { Skeleton } from "@/components/ui/Skeleton";
import { cn } from "@/lib/cn";
import { formatShortDateTime } from "@/lib/format-date";
import { safeHttpUrl } from "@/lib/safe-url";

/** UI gates mirroring student.documents.view/manage (docs/01_role_permission_matrix.md) — the API enforces
 * the same rule on its own. */
export const canViewStudentDocuments = (role?: string) =>
  role === "SUPER_ADMIN" || role === "ADMINISTRATOR" || role === "OPERATOR" || role === "STUDENT" || role === "PARENT";
export const canManageStudentDocuments = (role?: string) =>
  role === "SUPER_ADMIN" || role === "ADMINISTRATOR" || role === "OPERATOR";

const errorText = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

const formatSize = (bytes: number) =>
  bytes >= 1024 * 1024 ? `${(bytes / (1024 * 1024)).toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`;

type Mode = "file" | "link";

export function StudentDocumentsTab({ studentId, canManage }: { studentId: string; canManage: boolean }) {
  const client = useQueryClient();
  const [open, setOpen] = useState(false);
  const [mode, setMode] = useState<Mode>("file");
  const [validation, setValidation] = useState("");
  const [file, setFile] = useState<File | null>(null);
  const [pendingDelete, setPendingDelete] = useState<StudentDocument | null>(null);
  const fileInput = useRef<HTMLInputElement>(null);

  const query = useQuery({ queryKey: ["students", studentId, "documents"], queryFn: () => listStudentDocuments(studentId) });

  const refresh = async () => {
    await client.invalidateQueries({ queryKey: ["students", studentId, "documents"] });
    setOpen(false);
    setFile(null);
  };

  const linkMutation = useMutation({ mutationFn: createStudentDocument, onSuccess: refresh });
  const fileMutation = useMutation({ mutationFn: uploadStudentDocument, onSuccess: refresh });
  const removeMutation = useMutation({
    mutationFn: (id: string) => deleteStudentDocument(id),
    onSuccess: async () => {
      setPendingDelete(null);
      await client.invalidateQueries({ queryKey: ["students", studentId, "documents"] });
    },
  });

  const saving = linkMutation.isPending || fileMutation.isPending;
  const saveError = linkMutation.error ?? fileMutation.error;

  const openDrawer = () => {
    linkMutation.reset();
    fileMutation.reset();
    setValidation("");
    setFile(null);
    setMode("file");
    setOpen(true);
  };

  const pickFile = (picked: File | null) => {
    setValidation("");
    if (picked && picked.size > STUDENT_DOCUMENT_MAX_BYTES) {
      setValidation("Fayl 10 MB dan katta. Iltimos, uni Google Drive'ga yuklab, \"Havola\" bo'limidan joylashtiring.");
      setFile(null);
      if (fileInput.current) fileInput.current.value = "";
      return;
    }
    setFile(picked);
  };

  return (
    <div className="space-y-4">
      {canManage && (
        <div className="flex justify-end">
          <Button onClick={openDrawer}>
            <Paperclip className="size-4" aria-hidden />
            Hujjat qo&apos;shish
          </Button>
        </div>
      )}

      {query.isPending && <Skeleton className="h-28" />}
      {query.isError && (
        <>
          <Alert>{errorText(query.error, "Hujjatlarni yuklab bo'lmadi.")}</Alert>
          <Button onClick={() => void query.refetch()}>Qayta urinish</Button>
        </>
      )}
      {removeMutation.isError && <Alert>{errorText(removeMutation.error, "O'chirib bo'lmadi.")}</Alert>}

      {query.data?.length === 0 && (
        <div className="rounded-xl border border-border bg-surface">
          <EmptyState
            icon={FolderOpen}
            title="Hali hujjat qo'shilmagan"
            description="Pasport nusxasi, shartnoma yoki kvitansiya shu yerda saqlanadi."
          />
        </div>
      )}

      {query.data?.map((document) => (
        <DocumentCard
          key={document.id}
          document={document}
          canDelete={canManage}
          onDelete={() => {
            removeMutation.reset();
            setPendingDelete(document);
          }}
        />
      ))}

      <ConfirmDialog
        open={pendingDelete !== null}
        title={`"${pendingDelete?.title ?? ""}" hujjatini o'chirish`}
        description={
          pendingDelete?.file
            ? "Hujjat va yuklangan fayl butunlay o'chiriladi."
            : "Hujjat ro'yxatdan o'chiriladi. Havola o'zi turgan joyida qoladi."
        }
        loading={removeMutation.isPending}
        error={removeMutation.isError ? errorText(removeMutation.error, "O'chirib bo'lmadi.") : null}
        onConfirm={() => pendingDelete && removeMutation.mutate(pendingDelete.id)}
        onClose={() => setPendingDelete(null)}
      />

      {open && (
        <Drawer
          open
          title="Student hujjati"
          description="Faylni to'g'ridan-to'g'ri yuklang yoki tashqi havola qo'shing."
          onClose={() => {
            if (!saving) setOpen(false);
          }}
        >
          <div className="mb-4 grid grid-cols-2 gap-1 rounded-lg bg-ink-100 p-1">
            {(["file", "link"] as const).map((m) => (
              <button
                key={m}
                type="button"
                onClick={() => {
                  setMode(m);
                  setValidation("");
                }}
                className={cn(
                  "flex items-center justify-center gap-1.5 rounded-md py-1.5 text-[13px] font-medium transition-colors",
                  mode === m ? "bg-surface text-ink-900 shadow-sm" : "text-ink-600 hover:text-ink-900",
                )}
              >
                {m === "file" ? <Upload className="size-3.5" aria-hidden /> : <Link2 className="size-3.5" aria-hidden />}
                {m === "file" ? "Fayl" : "Havola"}
              </button>
            ))}
          </div>

          <form
            className="space-y-4"
            onSubmit={(e) => {
              e.preventDefault();
              setValidation("");
              const form = new FormData(e.currentTarget);
              const title = String(form.get("title") ?? "").trim();
              const description = String(form.get("description") ?? "").trim() || undefined;
              if (!title) {
                setValidation("Sarlavha kiriting.");
                return;
              }

              if (mode === "file") {
                if (!file) {
                  setValidation("Fayl tanlang.");
                  return;
                }
                fileMutation.mutate({ studentId, title, description, file });
                return;
              }

              const url = String(form.get("url") ?? "").trim();
              try {
                const parsed = new URL(url);
                if (!["http:", "https:"].includes(parsed.protocol) || parsed.username || parsed.password) throw new Error();
              } catch {
                setValidation("Yaroqli HTTP/HTTPS havola kiriting.");
                return;
              }
              linkMutation.mutate({ studentId, title, url, description });
            }}
          >
            <div>
              <Label htmlFor="document-title">Sarlavha</Label>
              <Input id="document-title" name="title" required maxLength={200} placeholder="Pasport nusxasi" />
            </div>

            {mode === "file" ? (
              <div>
                <Label htmlFor="document-file">Fayl</Label>
                <input
                  id="document-file"
                  ref={fileInput}
                  type="file"
                  accept={STUDENT_DOCUMENT_FILE_ACCEPT}
                  onChange={(e) => pickFile(e.target.files?.[0] ?? null)}
                  className="block w-full text-[13px] text-ink-600 file:mr-3 file:rounded-lg file:border-0 file:bg-brand-solid file:px-3 file:py-2 file:text-[13px] file:font-medium file:text-white hover:file:bg-brand-solid-hover"
                />
                {file && (
                  <p className="mt-1.5 text-[12.5px] text-ink-500">
                    {file.name} · {formatSize(file.size)}
                  </p>
                )}
                <p className="mt-1.5 text-[12.5px] text-ink-500">
                  Eng katta hajm — 10 MB. Kattaroq fayllarni Google Drive&apos;ga yuklab, &quot;Havola&quot;
                  bo&apos;limidan qo&apos;shing.
                </p>
              </div>
            ) : (
              <div>
                <Label htmlFor="document-url">Hujjat havolasi</Label>
                <Input id="document-url" name="url" type="url" required maxLength={2000} placeholder="https://drive.google.com/..." />
              </div>
            )}

            <div>
              <Label htmlFor="document-description">Izoh (ixtiyoriy)</Label>
              <Textarea id="document-description" name="description" maxLength={2000} />
            </div>

            {validation && <Alert>{validation}</Alert>}
            {saveError && <Alert>{errorText(saveError, "Hujjatni saqlab bo'lmadi.")}</Alert>}

            <Button type="submit" loading={saving} className="w-full">
              Saqlash
            </Button>
          </form>
        </Drawer>
      )}
    </div>
  );
}

function DocumentCard({
  document,
  canDelete,
  onDelete,
}: {
  document: StudentDocument;
  canDelete: boolean;
  onDelete: () => void;
}) {
  const [downloading, setDownloading] = useState(false);
  const [downloadError, setDownloadError] = useState<string | null>(null);

  // An uploaded file sits behind the bearer token, so it is fetched and handed to the browser as a blob URL
  // rather than linked directly — a plain <a href> would arrive unauthenticated and 401.
  const download = async () => {
    setDownloadError(null);
    setDownloading(true);
    try {
      const blob = await fetchStudentDocumentFile(document.id);
      const url = URL.createObjectURL(blob);
      const link = window.document.createElement("a");
      link.href = url;
      link.download = document.file?.fileName ?? document.title;
      link.click();
      URL.revokeObjectURL(url);
    } catch (error) {
      setDownloadError(errorText(error, "Faylni yuklab bo'lmadi."));
    } finally {
      setDownloading(false);
    }
  };

  return (
    <article className="rounded-xl border border-border bg-surface p-4">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0 flex-1">
          {document.file ? (
            <button
              type="button"
              onClick={download}
              disabled={downloading}
              className="inline-flex items-center gap-2 text-left font-medium text-brand-700 hover:underline disabled:opacity-60"
            >
              <FileText className="size-4 shrink-0" aria-hidden />
              <span className="truncate">{document.title}</span>
              <Download className="size-3.5 shrink-0" aria-hidden />
            </button>
          ) : (
            <a
              href={safeHttpUrl(document.url) ?? "#"}
              target="_blank"
              rel="noopener noreferrer"
              className="inline-flex items-center gap-2 font-medium text-brand-700 hover:underline"
            >
              <span className="truncate">{document.title}</span>
              <ExternalLink className="size-4 shrink-0" aria-hidden />
              <span className="sr-only">Yangi oynada ochiladi</span>
            </a>
          )}

          {document.description && <p className="mt-2 whitespace-pre-wrap text-sm text-ink-600">{document.description}</p>}

          <p className="mt-2 text-xs text-ink-500">
            {document.file && (
              <>
                {document.file.fileName} · {formatSize(document.file.size)} ·{" "}
              </>
            )}
            {formatShortDateTime(document.createdAt)}
          </p>
          {downloadError && <p className="mt-2 text-xs text-danger-600">{downloadError}</p>}
        </div>

        {canDelete && (
          <Button
            variant="ghost"
            size="icon"
            onClick={onDelete}
            className="shrink-0 text-danger-500 hover:bg-danger-50 hover:text-danger-600"
            aria-label={`${document.title} hujjatini o'chirish`}
            title="O'chirish"
          >
            <Trash2 className="size-4" aria-hidden />
          </Button>
        )}
      </div>
    </article>
  );
}
