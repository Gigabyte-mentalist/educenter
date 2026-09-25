"use client";

import { Download, Paperclip } from "lucide-react";
import { useState } from "react";
import { ApiError } from "@/lib/api-client";
import { HOMEWORK_FILE_ACCEPT, HOMEWORK_FILE_MAX_BYTES } from "@/lib/api/lms";
import { formatBytes } from "@/lib/format-bytes";
import { Label } from "@/components/ui/Field";

/** What is wrong with a file the user picked, checked before it is sent — the API refuses the same things, this only saves the trip. */
export function homeworkFileProblem(file: File): string | null {
  const dot = file.name.lastIndexOf(".");
  const extension = dot < 0 ? "" : file.name.slice(dot).toLowerCase();
  if (!HOMEWORK_FILE_ACCEPT.split(",").includes(extension))
    return `Bu fayl turi qo'llab-quvvatlanmaydi. Ruxsat etilganlar: ${HOMEWORK_FILE_ACCEPT}.`;
  if (file.size === 0) return "Fayl bo'sh.";
  if (file.size > HOMEWORK_FILE_MAX_BYTES)
    return `Fayl ${formatBytes(HOMEWORK_FILE_MAX_BYTES)} dan katta. Uni Google Drive'ga yuklab, havolasini yuboring.`;
  return null;
}

/** A file input for homework: the size and type are checked the moment a file is chosen, and the problem is shown under it.
 * The parent keeps the file and disables its own submit while <see cref="homeworkFileProblem"/> is not null. */
export function HomeworkFilePicker({ id, label, file, onChange }: {
  id: string; label: string; file: File | null; onChange: (file: File | null) => void;
}) {
  const problem = file ? homeworkFileProblem(file) : null;
  return (
    <div>
      <Label htmlFor={id}>{label}</Label>
      <input
        id={id}
        type="file"
        accept={HOMEWORK_FILE_ACCEPT}
        onChange={(e) => onChange(e.target.files?.[0] ?? null)}
        className="block w-full text-[13px] text-ink-600 file:mr-3 file:rounded-lg file:border-0 file:bg-brand-solid file:px-3 file:py-2 file:text-[13px] file:font-medium file:text-white hover:file:bg-brand-solid-hover"
      />
      {file && !problem && <p className="mt-1.5 text-[12.5px] text-ink-500">{file.name} · {formatBytes(file.size)}</p>}
      {problem && <p className="mt-1.5 text-[12.5px] text-danger-600">{problem}</p>}
      {!file && <p className="mt-1.5 text-[12.5px] text-ink-500">Eng katta hajm — {formatBytes(HOMEWORK_FILE_MAX_BYTES)}.</p>}
    </div>
  );
}

/** A stored file behind the bearer token: fetched on click and handed to the browser as a blob under its own name — a plain
 * <a href> would arrive without the token and 401. */
export function DownloadFileButton({ name, size, load }: { name: string; size?: number; load: () => Promise<Blob> }) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const download = async () => {
    setError(null);
    setBusy(true);
    try {
      const blob = await load();
      const url = URL.createObjectURL(blob);
      const link = document.createElement("a");
      link.href = url;
      link.download = name;
      link.click();
      URL.revokeObjectURL(url);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Faylni yuklab bo'lmadi.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <span className="inline-flex max-w-full flex-col items-start">
      <button
        type="button"
        onClick={() => void download()}
        disabled={busy}
        className="inline-flex max-w-full items-center gap-1.5 text-left text-[12.5px] font-medium text-brand-700 hover:underline disabled:opacity-60"
      >
        <Paperclip className="size-3.5 shrink-0" aria-hidden />
        <span className="truncate">{name}</span>
        {size !== undefined && <span className="shrink-0 font-normal text-ink-500">({formatBytes(size)})</span>}
        <Download className="size-3.5 shrink-0" aria-hidden />
      </button>
      {error && <span className="text-[12px] text-danger-600">{error}</span>}
    </span>
  );
}
