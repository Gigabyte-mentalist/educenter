"use client";

import { useEffect, useMemo, useSyncExternalStore } from "react";
import { useQuery } from "@tanstack/react-query";
import { GraduationCap } from "lucide-react";
import { apiFetchBlob } from "@/lib/api-client";
import {
  applyBrandColor, BRAND_CHANGED_EVENT, blobToDataUrl, parseCachedBranding, readCachedBrandingRaw, saveCachedBranding,
} from "@/lib/brand";
import { setDefaultCurrency } from "@/lib/currency";
import { getCurrentOrganization, organizationLogoPath, type OrganizationSettings } from "@/lib/api/organization";
import { useAuthSession } from "@/lib/use-auth-session";
import { cn } from "@/lib/cn";

export const ORGANIZATION_QUERY_KEY = ["organization", "current"] as const;

/** The signed-in person's organization: its name, logo, colour, currency and working hours. Everyone may read it. */
export function useOrganization() {
  const signedIn = useAuthSession().token !== null;
  return useQuery({ queryKey: ORGANIZATION_QUERY_KEY, queryFn: getCurrentOrganization, enabled: signedIn, staleTime: 5 * 60_000 });
}

/** The logo as a data URL — it needs the bearer token, so it cannot be a plain `<img src>`, and a data URL is also what the
 * sign-in page can be given from the cache. Fetched once per version. */
function useOrganizationLogo(org: OrganizationSettings | undefined) {
  return useQuery({
    queryKey: ["organization", "logo", org?.id, org?.logoVersion],
    queryFn: async () => blobToDataUrl(await apiFetchBlob(organizationLogoPath(org!.id, org!.logoVersion!))),
    enabled: !!org?.logoVersion,
    staleTime: Infinity,
    retry: false,
  });
}

/**
 * Renders nothing. Keeps the page's colour, the default currency and the sign-in page's memory of the centre in step with the
 * organization the signed-in person belongs to — mounted once, in the shell.
 */
export function OrganizationBrandSync() {
  const org = useOrganization().data;
  const logo = useOrganizationLogo(org).data ?? null;

  useEffect(() => {
    if (!org) return;
    applyBrandColor(org.brandColor);
    setDefaultCurrency(org.currency);
  }, [org]);

  useEffect(() => {
    if (!org) return;
    if (org.logoVersion && logo === null) return; // the logo is still on its way — do not replace the remembered one with nothing
    saveCachedBranding({ name: org.name, currency: org.currency, brandColor: org.brandColor, logo });
  }, [org, logo]);

  return null;
}

function subscribeCache(onChange: () => void): () => void {
  window.addEventListener(BRAND_CHANGED_EVENT, onChange);
  window.addEventListener("storage", onChange);
  return () => {
    window.removeEventListener(BRAND_CHANGED_EVENT, onChange);
    window.removeEventListener("storage", onChange);
  };
}

/** The centre as this browser last saw it — for the sign-in page, where nobody is signed in to ask. Null on the server and for a
 * first-time visitor, who see the product's own mark. */
export function useCachedBranding() {
  const raw = useSyncExternalStore(subscribeCache, readCachedBrandingRaw, () => null);
  return useMemo(() => parseCachedBranding(raw), [raw]);
}

const DEFAULT_NAME = "EduCenter";

/** What the shell shows as the centre's name and logo: the live organization, else what was remembered, else the product's own. */
export function useBrand(): { name: string; logo: string | null } {
  const live = useOrganization().data;
  const logo = useOrganizationLogo(live).data ?? null;
  const cached = useCachedBranding();
  return { name: live?.name ?? cached?.name ?? DEFAULT_NAME, logo: live ? (live.logoVersion ? logo : null) : (cached?.logo ?? null) };
}

/** The centre's logo, or the product's mark on the brand colour when there is none. `className` sets the size. */
export function BrandMark({ logo, className }: { logo: string | null; className?: string }) {
  if (logo) {
    return (
      // eslint-disable-next-line @next/next/no-img-element -- a data URL, not an optimizable asset
      <img src={logo} alt="" className={cn("shrink-0 rounded-lg bg-white object-contain", className)} />
    );
  }
  return (
    <span className={cn("flex shrink-0 items-center justify-center rounded-lg bg-brand-solid text-white", className)}>
      <GraduationCap className="size-1/2" aria-hidden />
    </span>
  );
}
