"use client";

import { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { logCall, CALL_OUTCOME_LABEL, type Lead, type CallOutcome } from "@/lib/api/crm";
import { ApiError } from "@/lib/api-client";
import { Drawer } from "@/components/ui/Drawer";
import { Button } from "@/components/ui/Button";
import { Alert } from "@/components/ui/Alert";
import { Input, Label, Select } from "@/components/ui/Field";

/** Reused by the Kanban card's quick action and the Lead 360° page — one call-outcome form either way. */
export function LogCallDrawer({ lead, onClose }: { lead: Lead; onClose: () => void }) {
  const queryClient = useQueryClient();
  const [outcome, setOutcome] = useState<CallOutcome>("Answered");
  const [notes, setNotes] = useState("");

  const callMutation = useMutation({
    mutationFn: () => logCall(lead.id, outcome, notes || undefined),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["leads"] });
      queryClient.invalidateQueries({ queryKey: ["lead", lead.id] });
      queryClient.invalidateQueries({ queryKey: ["lead-calls", lead.id] });
      queryClient.invalidateQueries({ queryKey: ["crm-tasks"] });
      onClose();
    },
  });

  return (
    <Drawer open onClose={onClose} title="Qo'ng'iroq natijasi" description={`${lead.fullName} — ${lead.phone}`}>
      <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); callMutation.mutate(); }}>
        <div>
          <Label htmlFor="call-outcome">Natija</Label>
          <Select id="call-outcome" value={outcome} onChange={(e) => setOutcome(e.target.value as CallOutcome)}>
            {(Object.keys(CALL_OUTCOME_LABEL) as CallOutcome[]).map((o) => (
              <option key={o} value={o}>{CALL_OUTCOME_LABEL[o]}</option>
            ))}
          </Select>
        </div>
        <div>
          <Label htmlFor="call-notes">Izoh (ixtiyoriy)</Label>
          <Input id="call-notes" value={notes} onChange={(e) => setNotes(e.target.value)} />
        </div>
        <p className="text-[12.5px] text-ink-500">
          &quot;Keyinroq qo&apos;ng&apos;iroq qilish&quot; tanlansa, ertaga uchun avtomatik vazifa yaratiladi.
        </p>
        {callMutation.isError && (
          <Alert>{callMutation.error instanceof ApiError ? callMutation.error.message : "Saqlab bo'lmadi."}</Alert>
        )}
        <Button type="submit" loading={callMutation.isPending} className="w-full">Saqlash</Button>
      </form>
    </Drawer>
  );
}
