import { apiFetch } from "../api-client";

/** The organization as everyone signed in sees it (`GET /api/organizations/current`) — no slug, no limits. */
export interface OrganizationSettings {
  id: string;
  name: string;
  currency: string;
  /** "HH:mm" */
  workdayStart: string;
  workdayEnd: string;
  /** "#rrggbb", or null while the centre uses the product's own colour. */
  brandColor: string | null;
  /** Null while there is no logo; changes with each new one. */
  logoVersion: string | null;
}

export const CURRENCIES = ["UZS", "USD", "EUR", "RUB"] as const;
export const CURRENCY_LABEL: Record<string, string> = {
  UZS: "O'zbek so'mi (UZS)",
  USD: "AQSh dollari (USD)",
  EUR: "Yevro (EUR)",
  RUB: "Rossiya rubli (RUB)",
};

/** A logo is an image of at most this size — the server refuses anything else, this only saves the round trip. */
export const LOGO_MAX_BYTES = 1024 * 1024;
export const LOGO_EXTENSIONS = [".png", ".jpg", ".jpeg", ".webp"];

export const getCurrentOrganization = () => apiFetch<OrganizationSettings>("/api/organizations/current");

/** `brandColor: ""` goes back to the product's own colour; leaving a field out keeps it. */
export interface UpdateOrganizationInput {
  name?: string;
  currency?: string;
  workdayStart?: string;
  workdayEnd?: string;
  brandColor?: string;
}

export const updateOrganization = (id: string, input: UpdateOrganizationInput) =>
  apiFetch<OrganizationSettings>(`/api/organizations/${id}`, { method: "PATCH", body: JSON.stringify(input) });

/** Where the logo is fetched from (with the bearer token, `apiFetchBlob`). The version changes with each new logo. */
export const organizationLogoPath = (id: string, version: string) => `/api/organizations/${id}/logo?v=${encodeURIComponent(version)}`;

export function uploadOrganizationLogo(id: string, file: File): Promise<OrganizationSettings> {
  const form = new FormData();
  form.append("file", file);
  return apiFetch<OrganizationSettings>(`/api/organizations/${id}/logo`, { method: "POST", body: form });
}

export const deleteOrganizationLogo = (id: string) => apiFetch<void>(`/api/organizations/${id}/logo`, { method: "DELETE" });
