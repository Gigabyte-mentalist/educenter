import { ShieldCheck } from "lucide-react";
import type { HeadlineChip } from "@/lib/api/teachers";
import { Badge } from "@/components/ui/Badge";
import { cn } from "@/lib/cn";

/** "IELTS Academic 8.0 ✓" — the exam scores an administrator has confirmed, newest first. Nothing at all when there are none. */
export function TeacherHeadline({ chips, max, compact }: { chips?: HeadlineChip[] | null; max?: number; compact?: boolean }) {
  const shown = (chips ?? []).slice(0, max ?? chips?.length ?? 0);
  if (shown.length === 0) return null;
  return (
    <div className={cn("flex flex-wrap gap-1.5", compact ? "mt-1.5" : "-mt-3 mb-6")}>
      {shown.map((chip) => (
        <Badge key={`${chip.title}-${chip.score}`} tone="success">
          <ShieldCheck className="size-3" aria-hidden />
          {chip.title}{chip.score ? ` ${chip.score}` : ""}
        </Badge>
      ))}
    </div>
  );
}
