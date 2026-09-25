import {
  ACCOUNT_BLOCKED_CODE, PASSWORD_CHANGE_REQUIRED_CODE, markAccountBlocked, markPasswordChangeRequired,
} from "./account-status";
import { clearTokens, getAccessToken, getRefreshToken, saveTokens } from "./auth-storage";

// NEXT_PUBLIC_API_URL="" (set, but empty) means "same origin": the production gateway serves the pages and /api and
// /hubs from one address, so requests are relative and no CORS is involved.
// If it isn't set at all, target the backend on whatever host the browser actually used to
// load this page (not a hardcoded "localhost") — this machine is reachable over several hostnames
// (localhost, LAN IP, Tailscale), and "localhost" in a fetch() always means the browser's own device.
function resolveApiBaseUrl(): string {
  if (process.env.NEXT_PUBLIC_API_URL !== undefined) return process.env.NEXT_PUBLIC_API_URL;
  if (typeof window !== "undefined") return `${window.location.protocol}//${window.location.hostname}:5167`;
  return "http://localhost:5167";
}

const API_BASE_URL = resolveApiBaseUrl();

const UNREACHABLE_MESSAGE = "Serverga ulanib bo'lmadi. Backend ishga tushirilganini tekshiring.";

export class ApiError extends Error {
  constructor(
    public status: number,
    message: string,
    /** The parsed JSON error body, when the server sent one (e.g. { conflicts: [...] } on 409). */
    public body: unknown = undefined,
  ) {
    super(message);
  }
}

let refreshInFlight: Promise<boolean> | null = null;

async function refreshAccessToken(): Promise<boolean> {
  const refreshToken = getRefreshToken();
  if (!refreshToken) return false;

  let response: Response;
  try {
    response = await fetch(`${API_BASE_URL}/api/auth/refresh`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ refreshToken }),
    });
  } catch {
    // Network failure mid-refresh: keep the tokens — the session may well still be valid once the
    // backend is reachable again, and wiping them would bounce the user to /login for no reason.
    return false;
  }

  if (!response.ok) {
    const body = (await response.json().catch(() => ({}))) as { code?: string };
    if (response.status === 403 && body.code === ACCOUNT_BLOCKED_CODE) {
      // Tokens are left alone on purpose: the account was blocked while the user was still signed in, and
      // BlockedScreen's own "log out" button is what clears them — not this refresh failure.
      markAccountBlocked();
      return false;
    }
    clearTokens();
    return false;
  }

  const tokens = await response.json();
  saveTokens({ accessToken: tokens.accessToken, refreshToken: tokens.refreshToken });
  return true;
}

/** Server error bodies are `{ error: "..." }`; anything else gets a status-specific default the user
 * can act on, instead of the raw statusText ("Forbidden") that means nothing to an operator. */
function messageFor(status: number, body: { error?: unknown }): string {
  if (typeof body.error === "string" && body.error.trim()) return body.error;
  switch (status) {
    case 401: return "Sessiya muddati tugagan. Qaytadan kiring.";
    case 403: return "Bu amal uchun sizda ruxsat yo'q.";
    case 404: return "Ma'lumot topilmadi.";
    case 409: return "Bu ma'lumot allaqachon mavjud yoki boshqa yozuv bilan to'qnashmoqda.";
    case 429: return "Juda ko'p so'rov yuborildi. Bir daqiqadan so'ng qayta urinib ko'ring.";
    default: return status >= 500 ? "Serverda xatolik yuz berdi. Birozdan so'ng qayta urinib ko'ring." : "So'rov bajarilmadi.";
  }
}

export function apiUrl(path: string): string {
  return `${API_BASE_URL}${path}`;
}

/** Fetch with the bearer token: on a 401 refreshes once and retries; throws ApiError on a network failure or a
 * non-2xx status. JSON bodies get their Content-Type here; FormData lets the browser set the multipart boundary. */
async function authorizedFetch(path: string, init: RequestInit = {}): Promise<Response> {
  const doFetch = async (): Promise<Response> => {
    const token = getAccessToken();
    return fetch(`${API_BASE_URL}${path}`, {
      ...init,
      headers: {
        ...(init.body instanceof FormData ? {} : { "Content-Type": "application/json" }),
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
        ...init.headers,
      },
    });
  };

  let response: Response;
  try {
    response = await doFetch();
  } catch {
    // fetch() itself throws (connection refused, DNS failure, offline) rather than resolving with a
    // bad status — surface that distinctly so the UI can say "server unreachable" instead of a generic
    // "something went wrong" that reads the same as a wrong password.
    throw new ApiError(0, UNREACHABLE_MESSAGE);
  }

  if (response.status === 401 && getRefreshToken()) {
    refreshInFlight ??= refreshAccessToken().finally(() => {
      refreshInFlight = null;
    });

    if (await refreshInFlight) {
      try {
        response = await doFetch();
      } catch {
        throw new ApiError(0, UNREACHABLE_MESSAGE);
      }
    }
  }

  if (!response.ok) {
    const body = (await response.json().catch(() => ({}))) as { error?: unknown; code?: string };
    if (response.status === 403 && body.code === ACCOUNT_BLOCKED_CODE) markAccountBlocked();
    if (response.status === 403 && body.code === PASSWORD_CHANGE_REQUIRED_CODE) markPasswordChangeRequired();
    throw new ApiError(response.status, messageFor(response.status, body), body);
  }

  return response;
}

/** Fetch wrapper for JSON endpoints: attaches the bearer token, and on a 401 refreshes once and retries. */
export async function apiFetch<T>(path: string, init: RequestInit = {}): Promise<T> {
  const response = await authorizedFetch(path, init);
  if (response.status === 204) return undefined as T;
  return response.json() as Promise<T>;
}

/** Authenticated binary download (e.g. a chat attachment) — a plain <img src> or link cannot carry the bearer token. */
export async function apiFetchBlob(path: string): Promise<Blob> {
  return (await authorizedFetch(path)).blob();
}
