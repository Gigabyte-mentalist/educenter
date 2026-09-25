"use client";

import { useQueryClient } from "@tanstack/react-query";
import { ApiError } from "@/lib/api-client";

export const errorText = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

/** Everything the teacher's page shows about a portfolio is refreshed together: the tab, the header chips, the list. */
export function useRefreshPortfolio(teacherId: string) {
  const client = useQueryClient();
  return async () => {
    await Promise.all([
      client.invalidateQueries({ queryKey: ["teachers", teacherId, "portfolio"] }),
      client.invalidateQueries({ queryKey: ["teachers", teacherId, "summary"] }),
      client.invalidateQueries({ queryKey: ["teachers"], exact: true }),
    ]);
  };
}
