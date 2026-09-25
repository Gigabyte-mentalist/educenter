"use client";

import { useState } from "react";
import { MessageSquareWarning } from "lucide-react";
import { usePermissions } from "@/lib/use-permissions";
import { Button } from "@/components/ui/Button";
import { ComplaintCreateDrawer } from "./ComplaintCreateDrawer";

/**
 * "Shikoyat yozish" on a student's profile: the office (or a teacher) records a complaint about, or on behalf of the family of, this student — the
 * student, and so the branch, is already known. Students and parents file from their own screens, so the button is not theirs.
 */
export function StudentComplaintButton({ studentId, studentName }: { studentId: string; studentName: string }) {
  const { can, scopeOf } = usePermissions();
  const [open, setOpen] = useState(false);
  const scope = scopeOf("complaints.create");
  if (!can("complaints.create") || scope === "Self" || scope === "MyChildren") return null;

  return (
    <>
      <Button size="sm" variant="secondary" onClick={() => setOpen(true)}>
        <MessageSquareWarning className="size-3.5" aria-hidden />
        Shikoyat yozish
      </Button>
      {open && <ComplaintCreateDrawer studentId={studentId} studentName={studentName} onClose={() => setOpen(false)} />}
    </>
  );
}
