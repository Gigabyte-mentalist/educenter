const ACCESS_TOKEN_KEY = "educenter.accessToken";
const REFRESH_TOKEN_KEY = "educenter.refreshToken";

/** Fired on this tab whenever tokens change — the browser's own "storage" event only reaches *other* tabs. */
export const AUTH_CHANGED_EVENT = "educenter:auth-changed";

export interface TokenPair {
  accessToken: string;
  refreshToken: string;
}

function notifyAuthChanged(): void {
  window.dispatchEvent(new Event(AUTH_CHANGED_EVENT));
}

// localStorage only exists client-side — every export here is safe to call from a "use client" component,
// but must never run during server rendering.
export function saveTokens(tokens: TokenPair): void {
  localStorage.setItem(ACCESS_TOKEN_KEY, tokens.accessToken);
  localStorage.setItem(REFRESH_TOKEN_KEY, tokens.refreshToken);
  notifyAuthChanged();
}

export function getAccessToken(): string | null {
  return localStorage.getItem(ACCESS_TOKEN_KEY);
}

export function getRefreshToken(): string | null {
  return localStorage.getItem(REFRESH_TOKEN_KEY);
}

export function clearTokens(): void {
  localStorage.removeItem(ACCESS_TOKEN_KEY);
  localStorage.removeItem(REFRESH_TOKEN_KEY);
  notifyAuthChanged();
}

export interface CurrentUser {
  id: string | null;
  email: string;
  role: string;
}

const ROLE_LABELS_UZ: Record<string, string> = {
  SUPER_ADMIN: "Super Admin",
  ADMINISTRATOR: "Administrator",
  OPERATOR: "Operator",
  TEACHER: "O'qituvchi",
  SUPPORT_TEACHER: "Yordamchi o'qituvchi",
  STUDENT: "Student",
  PARENT: "Ota-ona",
  DEVELOPER: "Dasturchi",
};

export function roleLabel(role: string): string {
  return ROLE_LABELS_UZ[role] ?? role;
}

/** Decodes a JWT payload client-side for display purposes only (e.g. sidebar user card) — never
 * treated as a trust boundary; the backend re-checks every permission on every request regardless. */
export function userFromToken(token: string | null): CurrentUser | null {
  if (!token) return null;

  try {
    const payload = JSON.parse(atob(token.split(".")[1]));
    const email = payload["http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress"];
    const role = payload["http://schemas.microsoft.com/ws/2008/06/identity/claims/role"];
    const id = payload["http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier"] ?? payload.sub ?? null;
    if (!email || !role) return null;
    return { id, email, role: Array.isArray(role) ? role[0] : role };
  } catch {
    return null;
  }
}

export function getCurrentUser(): CurrentUser | null {
  return userFromToken(getAccessToken());
}
