import { BRAND_VARS_KEY } from "./brand-init";

/** White text has to stay readable on the brand colour (WCAG AA) — the server refuses a lighter one, this only warns first. */
export const MIN_WHITE_CONTRAST = 4.5;

const STEPS = [50, 100, 200, 300, 400, 500, 600, 700, 800, 900] as const;
type Rgb = [number, number, number];

function parse(hex: string): Rgb | null {
  return /^#[0-9a-f]{6}$/i.test(hex) ? [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16)) as Rgb : null;
}

const toHex = (rgb: Rgb) => `#${rgb.map((c) => Math.round(c).toString(16).padStart(2, "0")).join("")}`;

/** `from` moved `amount` (0–1) of the way to `to`. */
function mix(from: Rgb, to: Rgb, amount: number): Rgb {
  return from.map((c, i) => c + (to[i] - c) * amount) as Rgb;
}

const WHITE: Rgb = [255, 255, 255];
const BLACK: Rgb = [0, 0, 0];
// The night-mode surface the dark ramp's low end blends into (a hair of warmth, like the rest of the dark neutrals).
const NIGHT: Rgb = [23, 22, 20];

export function whiteContrast(hex: string): number | null {
  const rgb = parse(hex);
  if (!rgb) return null;
  const [r, g, b] = rgb.map((v) => {
    const c = v / 255;
    return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  });
  return 1.05 / (0.2126 * r + 0.7152 * g + 0.0722 * b + 0.05);
}

// Amounts chosen to land near the product's own teal ramp when fed its own colour (#26645a).
const LIGHT: Record<(typeof STEPS)[number], (brand: Rgb) => Rgb> = {
  50: (b) => mix(b, WHITE, 0.93), 100: (b) => mix(b, WHITE, 0.85), 200: (b) => mix(b, WHITE, 0.7),
  300: (b) => mix(b, WHITE, 0.5), 400: (b) => mix(b, WHITE, 0.3), 500: (b) => mix(b, WHITE, 0.12),
  600: (b) => b, 700: (b) => mix(b, BLACK, 0.2), 800: (b) => mix(b, BLACK, 0.4), 900: (b) => mix(b, BLACK, 0.58),
};

// Night mode inverts the ramp: the low steps are dark tints of the colour, the high steps light ones, so text-brand-700 stays readable.
const DARK: Record<(typeof STEPS)[number], (brand: Rgb) => Rgb> = {
  50: (b) => mix(NIGHT, b, 0.16), 100: (b) => mix(NIGHT, b, 0.26), 200: (b) => mix(NIGHT, b, 0.42),
  300: (b) => mix(NIGHT, b, 0.62), 400: (b) => mix(b, WHITE, 0.25), 500: (b) => mix(b, WHITE, 0.4),
  600: (b) => mix(b, WHITE, 0.55), 700: (b) => mix(b, WHITE, 0.68), 800: (b) => mix(b, WHITE, 0.8), 900: (b) => mix(b, WHITE, 0.9),
};

/**
 * The custom properties that re-colour the product for one brand colour (`--org-l-*` for day mode, `--org-d-*` for night). globals.css
 * reads them with the product's own colours as the fallback, so removing them all puts the original look back. The filled surfaces
 * (`solid`) are the colour itself in both modes — white text on them is what the server's contrast rule protects.
 */
export function brandVariables(hex: string): Record<string, string> {
  const brand = parse(hex);
  if (!brand) return {};
  const vars: Record<string, string> = {};
  for (const step of STEPS) {
    vars[`--org-l-${step}`] = toHex(LIGHT[step](brand));
    vars[`--org-d-${step}`] = toHex(DARK[step](brand));
  }
  vars["--org-l-solid"] = toHex(brand);
  vars["--org-l-solid-hover"] = toHex(mix(brand, BLACK, 0.2));
  vars["--org-d-solid"] = toHex(brand);
  vars["--org-d-solid-hover"] = toHex(mix(brand, WHITE, 0.1));
  return vars;
}

const ALL_VARIABLES = Object.keys(brandVariables("#000000"));

/** Puts the brand colour on the page (null puts the product's own back) and remembers it for the next visit's first paint. */
export function applyBrandColor(hex: string | null): void {
  const root = document.documentElement;
  for (const name of ALL_VARIABLES) root.style.removeProperty(name);

  const vars = hex ? brandVariables(hex) : {};
  for (const [name, value] of Object.entries(vars)) root.style.setProperty(name, value);

  try {
    if (hex) localStorage.setItem(BRAND_VARS_KEY, JSON.stringify(vars));
    else localStorage.removeItem(BRAND_VARS_KEY);
  } catch {
    // Not being able to remember it only costs a flash of the default colour on the next visit.
  }
}

// ---- what the sign-in page may show before anyone has signed in -----------------------------------------------------------------

const CACHE_KEY = "educenter:org-branding";
export const BRAND_CHANGED_EVENT = "educenter:brand-changed";

/** The centre's name, colour and logo as this browser last saw them. The logo is fetched with a bearer token, so a page nobody has
 * signed in to yet can only show it from here — a first-time visitor sees the product's own mark. */
export interface CachedBranding {
  name: string;
  currency: string;
  brandColor: string | null;
  /** The logo as a data URL, kept only when it is small enough not to crowd localStorage. */
  logo: string | null;
}

/** Above this the logo is not cached (a data URL is a third larger than the file). */
const MAX_CACHED_LOGO_CHARS = 400_000;

export function readCachedBrandingRaw(): string | null {
  try {
    return localStorage.getItem(CACHE_KEY);
  } catch {
    return null;
  }
}

export function parseCachedBranding(raw: string | null): CachedBranding | null {
  if (!raw) return null;
  try {
    const value = JSON.parse(raw) as Partial<CachedBranding>;
    if (typeof value.name !== "string") return null;
    return {
      name: value.name,
      currency: typeof value.currency === "string" ? value.currency : "UZS",
      brandColor: typeof value.brandColor === "string" ? value.brandColor : null,
      logo: typeof value.logo === "string" && value.logo.startsWith("data:image/") ? value.logo : null,
    };
  } catch {
    return null;
  }
}

export function saveCachedBranding(branding: CachedBranding): void {
  const stored = { ...branding, logo: branding.logo && branding.logo.length <= MAX_CACHED_LOGO_CHARS ? branding.logo : null };
  try {
    localStorage.setItem(CACHE_KEY, JSON.stringify(stored));
    window.dispatchEvent(new Event(BRAND_CHANGED_EVENT));
  } catch {
    // Storage full or blocked: the sign-in page shows the product's own mark, nothing breaks.
  }
}

export function blobToDataUrl(blob: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result));
    reader.onerror = () => reject(reader.error);
    reader.readAsDataURL(blob);
  });
}
