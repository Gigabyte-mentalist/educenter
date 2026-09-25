"use client";

import { useRef, useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Camera, Trash2 } from "lucide-react";
import { deleteUserPhoto, PHOTO_EXTENSIONS, PHOTO_MAX_BYTES, uploadUserPhoto } from "@/lib/api/users";
import { ApiError } from "@/lib/api-client";
import { Alert } from "@/components/ui/Alert";
import { Button } from "@/components/ui/Button";

const MAX_MB = PHOTO_MAX_BYTES / (1024 * 1024);

/** The problem with a chosen file, or null. The server checks again; this only spares the person a round trip. */
function problemWith(file: File): string | null {
  const name = file.name.toLowerCase();
  if (!PHOTO_EXTENSIONS.some((extension) => name.endsWith(extension))) return `Foto faqat rasm bo‘lsin: ${PHOTO_EXTENSIONS.join(", ")}.`;
  if (file.size > PHOTO_MAX_BYTES) return `Foto ${MAX_MB} MB dan katta.`;
  return null;
}

/**
 * Choose, replace or remove a person's picture. `refresh` is the query that shows it (the profile that carries `hasPhoto`), so the
 * new picture appears as soon as it is saved.
 */
export function PhotoUpload({ userId, hasPhoto, refresh }: { userId: string; hasPhoto: boolean; refresh: readonly unknown[] }) {
  const queryClient = useQueryClient();
  const input = useRef<HTMLInputElement>(null);
  const [problem, setProblem] = useState<string | null>(null);

  const done = () => queryClient.invalidateQueries({ queryKey: refresh });
  const upload = useMutation({ mutationFn: (file: File) => uploadUserPhoto(userId, file), onSuccess: done });
  const remove = useMutation({ mutationFn: () => deleteUserPhoto(userId), onSuccess: done });
  const error = problem ?? [upload.error, remove.error].map((e) => (e instanceof ApiError ? e.message : e ? "Amalni bajarib bo‘lmadi." : null)).find(Boolean) ?? null;

  return (
    <div className="space-y-2">
      <div className="flex flex-wrap gap-2">
        <input
          ref={input} type="file" accept={PHOTO_EXTENSIONS.join(",")} className="sr-only" aria-label="Foto tanlash" tabIndex={-1}
          onChange={(event) => {
            const file = event.target.files?.[0];
            event.target.value = ""; // choosing the same file again must still fire
            if (!file) return;
            const found = problemWith(file);
            setProblem(found);
            if (!found) upload.mutate(file);
          }}
        />
        <Button size="sm" variant="secondary" loading={upload.isPending} onClick={() => input.current?.click()}>
          <Camera className="size-3.5" aria-hidden />
          {hasPhoto ? "Fotoni almashtirish" : "Foto yuklash"}
        </Button>
        {hasPhoto && (
          <Button size="sm" variant="ghost" loading={remove.isPending} onClick={() => { setProblem(null); remove.mutate(); }}>
            <Trash2 className="size-3.5" aria-hidden />
            Olib tashlash
          </Button>
        )}
      </div>
      {error && <Alert>{error}</Alert>}
    </div>
  );
}
