"use client";

import { useEffect } from "react";
import { useSearchParams } from "next/navigation";

/**
 * Universal Search (docs/00_TZ.md §27.3, audit §3.18) links a Group/Lead/Branch/Support result to its list
 * page with `?highlight=<id>` — these pages have no per-record detail route yet. This scrolls the element
 * carrying `id={highlightElementId(id)}` into view once the list has loaded, and returns the id so the
 * caller can add a highlight style to that one row/card.
 */
export function useHighlightTarget(loaded: boolean): string | null {
  const highlightId = useSearchParams().get("highlight");

  useEffect(() => {
    if (!highlightId || !loaded) return;
    document.getElementById(highlightElementId(highlightId))?.scrollIntoView({ behavior: "smooth", block: "center" });
  }, [highlightId, loaded]);

  return highlightId;
}

export const highlightElementId = (id: string) => `search-highlight-${id}`;

// A background tint, not a ring: box-shadow (which `ring` utilities use) doesn't render on table-row
// elements in most browsers, and this needs to work on both <tr> (Leads/Branches) and <div> (Groups) alike.
// Matches the existing "selected chat" convention (ChatList.tsx's bg-brand-50).
export const highlightClassName = (id: string, highlightId: string | null) =>
  id === highlightId ? "bg-brand-50" : undefined;
