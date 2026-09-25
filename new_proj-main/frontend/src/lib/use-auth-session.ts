"use client";

import { useMemo, useSyncExternalStore } from "react";
import { AUTH_CHANGED_EVENT, getAccessToken, userFromToken, type CurrentUser } from "./auth-storage";

function subscribe(onChange: () => void): () => void {
  window.addEventListener("storage", onChange);
  window.addEventListener(AUTH_CHANGED_EVENT, onChange);
  return () => {
    window.removeEventListener("storage", onChange);
    window.removeEventListener(AUTH_CHANGED_EVENT, onChange);
  };
}

const noopSubscribe = () => () => {};

export interface AuthSession {
  /** False during server render and hydration — localStorage isn't readable yet, so "no token" means
   * "don't know", not "logged out". Gate redirects on this or a refresh bounces a valid session to /login. */
  hydrated: boolean;
  token: string | null;
  user: CurrentUser | null;
}

/** The signed-in session, read from localStorage without a setState-in-effect round trip. The snapshot is
 * the raw token string (stable between renders), and the decoded user is derived from it. */
export function useAuthSession(): AuthSession {
  const hydrated = useSyncExternalStore(noopSubscribe, () => true, () => false);
  const token = useSyncExternalStore(subscribe, getAccessToken, () => null);
  const user = useMemo(() => userFromToken(token), [token]);
  return { hydrated, token, user };
}
