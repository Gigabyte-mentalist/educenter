import { parseCachedBranding, readCachedBrandingRaw } from "./brand";

// The currency prices are written in when the caller does not name one (an invoice names its own). It is the organization's
// setting (S07.01), so it lives in a small store the shell updates once the organization has loaded; the shell re-mounts the page
// when it changes, which is what makes every formatMoney call already on screen pick up the new one.
let current = "UZS";
let restored = false;
const listeners = new Set<() => void>();

function restore(): void {
  if (restored || typeof window === "undefined") return;
  restored = true;
  current = parseCachedBranding(readCachedBrandingRaw())?.currency ?? current;
}

export function getDefaultCurrency(): string {
  restore();
  return current;
}

export function setDefaultCurrency(code: string): void {
  restore();
  if (code === current) return;
  current = code;
  listeners.forEach((listener) => listener());
}

export function subscribeDefaultCurrency(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}
