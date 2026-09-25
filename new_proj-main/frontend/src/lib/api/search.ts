import { apiFetch } from "../api-client";

export type SearchResultType = "student" | "teacher" | "group" | "course" | "lead" | "branch" | "ticket" | "payment";

export interface SearchResult {
  type: SearchResultType;
  id: string;
  title: string;
  subtitle: string | null;
  href: string;
}

export function search(q: string): Promise<SearchResult[]> {
  return apiFetch<SearchResult[]>(`/api/search?q=${encodeURIComponent(q)}`);
}
