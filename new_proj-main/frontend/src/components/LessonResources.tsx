"use client";

import { useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { BookOpen, Download, ExternalLink, FileText, Link2, Paperclip, Trash2, Upload } from "lucide-react";
import {
  createResource, deleteResource, fetchResourceFile, listResources, uploadResource,
  RESOURCE_FILE_ACCEPT, RESOURCE_MAX_BYTES, type LearningResource,
} from "@/lib/api/lms";
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

const errorText = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

const formatSize = (bytes: number) =>
  bytes >= 1024 * 1024 ? `${(bytes / (1024 * 1024)).toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`;

type Mode = "file" | "link";

export function LessonResources({ lessonId, canCreate }: { lessonId: string; canCreate: boolean }) {
  const client = useQueryClient();
  const [open, setOpen] = useState(false);
  const [mode, setMode] = useState<Mode>("file");
  const [validation, setValidation] = useState("");
  const [file, setFile] = useState<File | null>(null);
  const [pendingDelete, setPendingDelete] = useState<LearningResource | null>(null);
  const fileInput = useRef<HTMLInputElement>(null);

  const query = useQuery({ queryKey: ["resources", lessonId], queryFn: () => listResources(lessonId) });

  const refresh = async () => {
    await client.invalidateQueries({ queryKey: ["resources", lessonId] });
    setOpen(false);
    setFile(null);
  };

  const linkMutation = useMutation({ mutationFn: createResource, onSuccess: refresh });
  const fileMutation = useMutation({ mutationFn: uploadResource, onSuccess: refresh });
  const removeMutation = useMutation({
    mutationFn: (id: string) => deleteResource(id),
    onSuccess: async () => {
      setPendingDelete(null);
      await client.invalidateQueries({ queryKey: ["resources", lessonId] });
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
    if (picked && picked.size > RESOURCE_MAX_BYTES) {
      // Checked here as well as on the server so a 12 MB deck fails instantly instead of after the upload.
      setValidation(
        "Fayl 10 MB dan katta. Iltimos, uni Google Drive'ga yuklab, \"Havola\" bo'limidan havolasini joylashtiring.",
      );
      setFile(null);
      if (fileInput.current) fileInput.current.value = "";
      return;
    }
    setFile(picked);
  };

  return (
    <div className="space-y-4">
      {canCreate && (
        <Button onClick={openDrawer}>
          <Paperclip className="size-4" aria-hidden />
          Material qo&apos;shish
        </Button>
      )}

      {query.isPending && <Skeleton className="h-28" />}
      {query.isError && (
        <>
          <Alert>{errorText(query.error, "Materiallarni yuklab bo'lmadi.")}</Alert>
          <Button onClick={() => void query.refetch()}>Qayta urinish</Button>
        </>
      )}
      {removeMutation.isError && <Alert>{errorText(removeMutation.error, "O'chirib bo'lmadi.")}</Alert>}

      {query.data?.length === 0 && (
        <EmptyState
          icon={BookOpen}
          title="Hali material qo'shilmagan"
          description="Darsda ishlatilgan taqdimot, kitob yoki mashq havolalari shu yerda ko'rinadi."
        />
      )}

      {query.data?.map((resource) => (
        <ResourceCard
          key={resource.id}
          resource={resource}
          canDelete={canCreate}
          onDelete={() => {
            removeMutation.reset();
            setPendingDelete(resource);
          }}
        />
      ))}

      <ConfirmDialog
        open={pendingDelete !== null}
        title={`"${pendingDelete?.title ?? ""}" materialini o'chirish`}
        description={
          pendingDelete?.file
            ? "Material va yuklangan fayl butunlay o'chiriladi."
            : "Material ro'yxatdan o'chiriladi. Havola o'zi turgan joyida qoladi."
        }
        loading={removeMutation.isPending}
        error={removeMutation.isError ? errorText(removeMutation.error, "O'chirib bo'lmadi.") : null}
        onConfirm={() => pendingDelete && removeMutation.mutate(pendingDelete.id)}
        onClose={() => setPendingDelete(null)}
      />

      {open && (
        <Drawer
          open
          title="Dars materiali"
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
                fileMutation.mutate({ lessonId, title, description, file });
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
              linkMutation.mutate({ lessonId, title, url, description });
            }}
          >
            <div>
              <Label htmlFor="resource-title">Sarlavha</Label>
              <Input id="resource-title" name="title" required maxLength={200} placeholder="Dars 3 — taqdimot" />
            </div>

            {mode === "file" ? (
              <div>
                <Label htmlFor="resource-file">Fayl</Label>
                <input
                  id="resource-file"
                  ref={fileInput}
                  type="file"
                  accept={RESOURCE_FILE_ACCEPT}
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
                <Label htmlFor="resource-url">Material havolasi</Label>
                <Input id="resource-url" name="url" type="url" required maxLength={2000} placeholder="https://drive.google.com/..." />
                <p className="mt-1.5 text-[12.5px] text-ink-500">
                  Material joylashgan xizmatda guruhingiz uchun ko&apos;rish ruxsatini oching.
                </p>
              </div>
            )}

            <div>
              <Label htmlFor="resource-description">Izoh (ixtiyoriy)</Label>
              <Textarea id="resource-description" name="description" maxLength={2000} />
            </div>

            {validation && <Alert>{validation}</Alert>}
            {saveError && <Alert>{errorText(saveError, "Materialni saqlab bo'lmadi.")}</Alert>}

            <Button type="submit" loading={saving} className="w-full">
              Saqlash
            </Button>
          </form>
        </Drawer>
      )}
    </div>
  );
}

function ResourceCard({
  resource,
  canDelete,
  onDelete,
}: {
  resource: LearningResource;
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
      const blob = await fetchResourceFile(resource.id);
      const url = URL.createObjectURL(blob);
      const link = document.createElement("a");
      link.href = url;
      link.download = resource.file?.fileName ?? resource.title;
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
          {resource.file ? (
            <button
              type="button"
              onClick={download}
              disabled={downloading}
              className="inline-flex items-center gap-2 text-left font-medium text-brand-700 hover:underline disabled:opacity-60"
            >
              <FileText className="size-4 shrink-0" aria-hidden />
              <span className="truncate">{resource.title}</span>
              <Download className="size-3.5 shrink-0" aria-hidden />
            </button>
          ) : (
            <a
              href={safeHttpUrl(resource.url) ?? "#"}
              target="_blank"
              rel="noopener noreferrer"
              className="inline-flex items-center gap-2 font-medium text-brand-700 hover:underline"
            >
              <span className="truncate">{resource.title}</span>
              <ExternalLink className="size-4 shrink-0" aria-hidden />
              <span className="sr-only">Yangi oynada ochiladi</span>
            </a>
          )}

          {resource.description && <p className="mt-2 whitespace-pre-wrap text-sm text-ink-600">{resource.description}</p>}

          <p className="mt-2 text-xs text-ink-500">
            {resource.file && (
              <>
                {resource.file.fileName} · {formatSize(resource.file.size)} ·{" "}
              </>
            )}
            {formatShortDateTime(resource.createdAt)}
          </p>
          {downloadError && <p className="mt-2 text-xs text-danger-600">{downloadError}</p>}
        </div>

        {canDelete && (
          <Button
            variant="ghost"
            size="icon"
            onClick={onDelete}
            className="shrink-0 text-danger-500 hover:bg-danger-50 hover:text-danger-600"
            aria-label={`${resource.title} materialini o'chirish`}
            title="O'chirish"
          >
            <Trash2 className="size-4" aria-hidden />
          </Button>
        )}
      </div>
    </article>
  );
}
