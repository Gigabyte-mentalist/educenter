"use client";

import { useEffect } from "react";
import { useQuery } from "@tanstack/react-query";
import { Download } from "lucide-react";
import { ApiError } from "@/lib/api-client";
import { Alert } from "@/components/ui/Alert";
import { Button } from "@/components/ui/Button";
import { Modal } from "@/components/ui/Modal";
import { Skeleton } from "@/components/ui/Skeleton";

/**
 * Shows the scan of a certificate inside the app — an image or a PDF — instead of handing the browser a download. The file sits behind
 * the bearer token, so it is fetched as a blob and shown from an object URL that is dropped as soon as the window closes.
 * `fileName` is given only to those who may also keep a copy (the teacher and the office); a student or a parent just reads it.
 */
export function CredentialFileViewer({
  open, title, cacheKey, load, fileName, onClose,
}: { open: boolean; title: string; cacheKey: readonly unknown[]; load: () => Promise<Blob>; fileName?: string; onClose: () => void }) {
  const query = useQuery({
    queryKey: ["credential-file", ...cacheKey],
    queryFn: async () => {
      const blob = await load();
      return { url: URL.createObjectURL(blob), type: blob.type };
    },
    enabled: open,
    staleTime: Infinity,
    gcTime: 0,
  });
  const url = query.data?.url;
  useEffect(() => () => { if (url) URL.revokeObjectURL(url); }, [url]);

  const isPdf = query.data?.type === "application/pdf";
  return (
    <Modal open={open} onClose={onClose} title={title} className="max-w-4xl">
      {query.isPending && <Skeleton className="h-[60vh]" />}
      {query.isError && <Alert>{query.error instanceof ApiError ? query.error.message : "Hujjatni yuklab bo‘lmadi."}</Alert>}
      {query.data && (
        <div className="space-y-3">
          {isPdf
            ? <iframe src={query.data.url} title={title} className="h-[68vh] w-full rounded-lg border border-border" />
            // eslint-disable-next-line @next/next/no-img-element -- a blob: URL of a private scan; next/image cannot optimise it
            : <img src={query.data.url} alt={title} className="mx-auto max-h-[68vh] max-w-full rounded-lg border border-border object-contain" />}
          {fileName && (
            <div className="flex justify-end">
              <a href={query.data.url} download={fileName}>
                <Button variant="secondary" size="sm" type="button">
                  <Download className="size-3.5" aria-hidden />
                  Nusxasini saqlash
                </Button>
              </a>
            </div>
          )}
        </div>
      )}
    </Modal>
  );
}
