"use client";

import { usePathname, useRouter, useSearchParams } from "next/navigation";

/**
 * The `?new=1` convention (S06.06): the command palette's "Yangi student", "Yangi lid" and the like take the person to the list page and ask it to
 * open its creation form. The request lives in the address, so it works whether the page was just loaded or the person was already on it — and closing
 * the form removes it, so a reload does not open the form again. A page opens its form when either its own button or this asks for it; it should
 * honour the request only for someone who may create (the server checks either way).
 *
 * Reads the address, so the component using it sits under a `<Suspense>` like every other page that does.
 */
export function useNewParam(): { requested: boolean; clear: () => void } {
  const requested = useSearchParams().get("new") === "1";
  const router = useRouter();
  const pathname = usePathname();
  return { requested, clear: () => router.replace(pathname) };
}
