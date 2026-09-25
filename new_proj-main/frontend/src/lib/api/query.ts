export type QueryValue = string | number | boolean | null | undefined | readonly string[];

/** "?a=1&b=x" — empty values are left out, arrays repeat the key (`statuses=A&statuses=B`, which is what ASP.NET binds to an array). */
export function buildQuery(params: Record<string, QueryValue>): string {
  const query = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value === undefined || value === null || value === "") continue;
    if (Array.isArray(value)) value.forEach((v) => query.append(key, v));
    else query.set(key, String(value));
  }
  const text = query.toString();
  return text ? `?${text}` : "";
}

/** What every paged list accepts. `size` defaults to 25 on the server too; sending it explicitly keeps the pager and the request in step. */
export interface PageParams {
  page?: number;
  size?: number;
  search?: string;
}

export const DEFAULT_PAGE_SIZE = 25;
