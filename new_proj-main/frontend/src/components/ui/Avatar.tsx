"use client";

import { useQuery } from "@tanstack/react-query";
import { apiFetchBlob } from "@/lib/api-client";
import { userPhotoPath } from "@/lib/api/users";
import { cn } from "@/lib/cn";

const PALETTE = [
  "bg-brand-100 text-brand-700",
  "bg-accent-300/40 text-accent-700",
  "bg-info-50 text-info-600",
  "bg-success-50 text-success-600",
];

function hashString(value: string): number {
  let hash = 0;
  for (let i = 0; i < value.length; i++) hash = (hash * 31 + value.charCodeAt(i)) >>> 0;
  return hash;
}

function initials(name: string): string {
  const parts = name.trim().split(/\s+/);
  return ((parts[0]?.[0] ?? "") + (parts[1]?.[0] ?? "")).toUpperCase();
}

/** A person's picture, fetched once per version and kept: it needs the bearer token, so it cannot be a plain `<img src>`. */
function usePhoto(userId: string, version: string) {
  return useQuery({
    queryKey: ["user-photo", userId, version],
    queryFn: async () => URL.createObjectURL(await apiFetchBlob(userPhotoPath(userId, version))),
    staleTime: Infinity,
    retry: false,
  });
}

function PhotoImage({ userId, version }: { userId: string; version: string }) {
  const photo = usePhoto(userId, version);
  // Until it arrives, or if it cannot be fetched, the initials underneath show instead — never a broken image.
  if (!photo.data) return null;
  return (
    <>
      {/* eslint-disable-next-line @next/next/no-img-element -- authenticated blob URL, not an optimizable asset */}
      <img src={photo.data} alt="" className="absolute inset-0 size-full object-cover" />
    </>
  );
}

/**
 * Initials on a tinted circle — or the person's picture over them when `photo` is given. Lists pass no `photo`: one request per
 * row would make a page of fifty students fifty requests; only a profile header asks for the picture.
 */
export function Avatar({ name, className, photo }: { name: string; className?: string; photo?: { userId: string; version: string } | null }) {
  const palette = PALETTE[hashString(name) % PALETTE.length];
  return (
    <span
      className={cn(
        "relative flex size-8 shrink-0 items-center justify-center overflow-hidden rounded-full text-xs font-semibold",
        palette,
        className,
      )}
    >
      {initials(name) || "?"}
      {photo && <PhotoImage userId={photo.userId} version={photo.version} />}
    </span>
  );
}
