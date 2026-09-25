"use client";

import { useSyncExternalStore } from "react";

/** Matches `ApiErrorCodes.AccountBlocked` on the backend — the text next to it is for display only. */
export const ACCOUNT_BLOCKED_CODE = "ACCOUNT_BLOCKED";

/** Matches `ApiErrorCodes.PasswordChangeRequired` — a temporary password is good for nothing but
 * `/change-password` until replaced (docs/21.09-audit-jurnali S01.05). */
export const PASSWORD_CHANGE_REQUIRED_CODE = "PASSWORD_CHANGE_REQUIRED";

let blocked = false;
let passwordChangeRequired = false;
const listeners = new Set<() => void>();
const notify = () => listeners.forEach((listener) => listener());

/** Set the moment any response (REST or chat) says this session's account is blocked. The admin layout reacts
 * by replacing the whole screen with `BlockedScreen` — see docs/21.09-audit-jurnali/S01.04. */
export function markAccountBlocked(): void {
  if (blocked) return;
  blocked = true;
  notify();
}

/** Cleared on logout, so a fresh sign-in (by the same or a different person, on the same tab) starts clean. */
export function clearAccountBlocked(): void {
  if (!blocked) return;
  blocked = false;
  notify();
}

/** Set when the API answers `PASSWORD_CHANGE_REQUIRED` — an administrator reset this account's password while the
 * session was open. The admin layout reacts by moving to `/change-password`; unlike the blocked screen this one
 * ends by the holder's own action, so the form clears it once the password is replaced. */
export function markPasswordChangeRequired(): void {
  if (passwordChangeRequired) return;
  passwordChangeRequired = true;
  notify();
}

export function clearPasswordChangeRequired(): void {
  if (!passwordChangeRequired) return;
  passwordChangeRequired = false;
  notify();
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export function useAccountBlocked(): boolean {
  return useSyncExternalStore(subscribe, () => blocked, () => false);
}

export function usePasswordChangeRequired(): boolean {
  return useSyncExternalStore(subscribe, () => passwordChangeRequired, () => false);
}
