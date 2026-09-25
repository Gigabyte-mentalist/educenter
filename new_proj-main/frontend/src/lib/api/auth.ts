import { apiFetch } from "../api-client";

export interface TokenResponse {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  /** Set on a temporary password (fresh registration, or an admin's reset) — the caller may only reach
   * `/api/auth/change-password` until it replaces it (docs/21.09-audit-jurnali S01.05). */
  mustChangePassword: boolean;
}

export function login(email: string, password: string): Promise<TokenResponse> {
  return apiFetch<TokenResponse>("/api/auth/login", {
    method: "POST",
    body: JSON.stringify({ email, password }),
  });
}

export function logout(refreshToken: string): Promise<void> {
  return apiFetch<void>("/api/auth/logout", {
    method: "POST",
    body: JSON.stringify({ refreshToken }),
  });
}

/** "Parolni unutdim", step one. The server answers 204 whether or not the account exists, so the screen may not say which. */
export function requestPasswordReset(identifier: string): Promise<void> {
  return apiFetch<void>("/api/auth/forgot-password", { method: "POST", body: JSON.stringify({ identifier }) });
}

/** Step two: the six-digit code and a new password. Every way it can fail to identify the person is one and the same 400. */
export function resetPassword(identifier: string, code: string, newPassword: string): Promise<void> {
  return apiFetch<void>("/api/auth/reset-password", { method: "POST", body: JSON.stringify({ identifier, code, newPassword }) });
}

/** Self-service: rotates every other device's session out, but returns a fresh pair for this one. */
export function changePassword(currentPassword: string, newPassword: string): Promise<TokenResponse> {
  return apiFetch<TokenResponse>("/api/auth/change-password", {
    method: "POST",
    body: JSON.stringify({ currentPassword, newPassword }),
  });
}
