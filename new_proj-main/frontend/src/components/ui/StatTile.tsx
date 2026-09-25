import { type LucideIcon } from "lucide-react";
import { cn } from "@/lib/cn";

export function StatTile({
  label,
  value,
  icon: Icon,
  tone = "neutral",
  hint,
}: {
  label: string;
  value: string | number;
  icon?: LucideIcon;
  tone?: "neutral" | "brand" | "warning" | "danger";
  hint?: string;
}) {
  const toneClasses: Record<string, string> = {
    neutral: "bg-ink-100 text-ink-600",
    brand: "bg-brand-100 text-brand-700",
    warning: "bg-warning-50 text-warning-600",
    danger: "bg-danger-50 text-danger-600",
  };

  return (
    <div className="rounded-xl border border-border bg-surface p-4">
      <div className="flex items-center justify-between">
        <span className="text-[13px] font-medium text-ink-500">{label}</span>
        {Icon && (
          <span className={cn("flex size-7 items-center justify-center rounded-lg", toneClasses[tone])}>
            <Icon className="size-3.5" aria-hidden />
          </span>
        )}
      </div>
      <p className="mt-2 font-tabular text-2xl font-semibold text-ink-900">{value}</p>
      {hint && <p className="mt-1 text-xs text-ink-500">{hint}</p>}
    </div>
  );
}
