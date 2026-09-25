"use client";

import { ChevronLeft, ChevronRight } from "lucide-react";
import { Button } from "@/components/ui/Button";
import { cn } from "@/lib/cn";

export interface PageEnvelope<T> {
  items: T[];
  total: number;
  pageNumber: number;
  pageSize: number;
  pageCount: number;
}

/**
 * The pager under a long table. It shows which rows are on screen out of how many rather than only page
 * numbers: "1–25 / 1240" is what tells someone the list is far bigger than the screen suggests, which is the
 * information that was missing when these tables returned everything at once.
 */
export function Pagination({
  page,
  pageCount,
  total,
  pageSize,
  onChange,
  className,
}: {
  page: number;
  pageCount: number;
  total: number;
  pageSize: number;
  onChange: (page: number) => void;
  className?: string;
}) {
  if (total === 0) return null;

  const first = (page - 1) * pageSize + 1;
  const last = Math.min(page * pageSize, total);

  return (
    <div
      className={cn(
        "flex flex-col gap-3 border-t border-border px-4 py-3 sm:flex-row sm:items-center sm:justify-between",
        className,
      )}
    >
      <p className="text-[13px] text-ink-500">
        <span className="font-medium text-ink-700">
          {first}–{last}
        </span>{" "}
        / {total} ta
      </p>

      <div className="flex items-center gap-2">
        <Button
          variant="secondary"
          size="sm"
          onClick={() => onChange(page - 1)}
          disabled={page <= 1}
          aria-label="Oldingi sahifa"
        >
          <ChevronLeft className="size-4" aria-hidden />
          Oldingi
        </Button>
        <span className="min-w-20 text-center text-[13px] tabular-nums text-ink-600">
          {page} / {Math.max(pageCount, 1)}
        </span>
        <Button
          variant="secondary"
          size="sm"
          onClick={() => onChange(page + 1)}
          disabled={page >= pageCount}
          aria-label="Keyingi sahifa"
        >
          Keyingi
          <ChevronRight className="size-4" aria-hidden />
        </Button>
      </div>
    </div>
  );
}
