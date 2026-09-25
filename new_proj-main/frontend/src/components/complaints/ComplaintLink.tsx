"use client";

import Link from "next/link";
import { MessageSquareWarning } from "lucide-react";
import { usePermissions } from "@/lib/use-permissions";
import { buttonVariants } from "@/components/ui/Button";

/** "Shikoyat yuborish" on a student's, a parent's or a teacher's home: takes them to their complaints with the form already open (`?new=1`). */
export function ComplaintLink() {
  const { can } = usePermissions();
  if (!can("complaints.create")) return null;

  return (
    <Link href="/complaints?new=1" className={buttonVariants({ variant: "secondary", size: "sm" })}>
      <MessageSquareWarning className="size-3.5" aria-hidden />
      Shikoyat yuborish
    </Link>
  );
}
