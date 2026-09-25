import { AlertCircle, CheckCircle2, Info } from "lucide-react";
import { cn } from "@/lib/cn";

export function Alert({
  tone = "danger",
  children,
}: {
  tone?: "danger" | "success" | "info";
  children: React.ReactNode;
}) {
  const config = {
    danger: { icon: AlertCircle, cls: "bg-danger-50 text-danger-600" },
    success: { icon: CheckCircle2, cls: "bg-success-50 text-success-600" },
    info: { icon: Info, cls: "bg-info-50 text-info-600" },
  }[tone];
  const Icon = config.icon;

  return (
    <div className={cn("flex items-start gap-2 rounded-lg px-3 py-2.5 text-[13px]", config.cls)}>
      <Icon className="mt-0.5 size-4 shrink-0" aria-hidden />
      <div>{children}</div>
    </div>
  );
}
