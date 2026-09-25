"use client";

import { useState, type ReactNode } from "react";
import { cn } from "@/lib/cn";

export function Tabs({
  tabs,
  defaultTabId,
}: {
  tabs: { id: string; label: string; content: ReactNode }[];
  /** Lets a caller land on a specific tab up front — e.g. Universal Search's `?highlight=<id>` needs the
   * tab that actually contains the highlighted record open, not always the first one. */
  defaultTabId?: string;
}) {
  const [active, setActive] = useState(defaultTabId ?? tabs[0]?.id);

  // One tab is not a choice: show its content without a row of a single label above it.
  if (tabs.length === 1) return <>{tabs[0].content}</>;

  return (
    <div>
      <div className="mb-5 flex gap-1 overflow-x-auto border-b border-border">
        {tabs.map((tab) => (
          <button
            key={tab.id}
            onClick={() => setActive(tab.id)}
            className={cn(
              "relative shrink-0 px-3.5 py-2.5 text-[13.5px] font-medium transition-colors",
              active === tab.id ? "text-brand-700" : "text-ink-500 hover:text-ink-800",
            )}
          >
            {tab.label}
            {active === tab.id && <span className="absolute inset-x-0 -bottom-px h-0.5 rounded-full bg-brand-solid" />}
          </button>
        ))}
      </div>
      {tabs.find((t) => t.id === active)?.content}
    </div>
  );
}
