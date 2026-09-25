"use client";

import { useQuery } from "@tanstack/react-query";
import { getMyPermissions, type ScopeType } from "@/lib/api/me";
import { useAuthSession } from "@/lib/use-auth-session";

export interface Permissions {
  /** False until the list has loaded — `can` answers false meanwhile, so nothing forbidden flashes on screen. */
  ready: boolean;
  can: (code: string) => boolean;
  scopeOf: (code: string) => ScopeType | null;
}

/**
 * What the signed-in user may do, by permission code — the UI counterpart of `[Authorize(Policy = "…")]`. Pages use it to
 * leave out buttons, links and options the person could not use (the role name is for navigation only). The server checks
 * again on every request, so this is convenience, never protection.
 */
export function usePermissions(): Permissions {
  const userId = useAuthSession().user?.id ?? null;
  const query = useQuery({
    queryKey: ["me", "permissions", userId],
    queryFn: getMyPermissions,
    enabled: userId !== null,
    staleTime: 5 * 60_000,
  });

  const scopes = new Map(query.data?.map((p) => [p.code, p.scope]));
  return {
    ready: query.isSuccess,
    can: (code) => scopes.has(code),
    scopeOf: (code) => scopes.get(code) ?? null,
  };
}
