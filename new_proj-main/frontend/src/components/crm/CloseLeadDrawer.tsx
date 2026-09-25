"use client";

import { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { updateLeadStatus, type Lead, type LeadStatus } from "@/lib/api/crm";
import { ApiError } from "@/lib/api-client";
import { Drawer } from "@/components/ui/Drawer";
import { Button } from "@/components/ui/Button";
import { Alert } from "@/components/ui/Alert";
import { Input, Label } from "@/components/ui/Field";

/**
 * The kanban board's "Yopilgan" column bundles three distinct terminal statuses for a compact view —
 * dropping a card there asks which one actually happened rather than guessing. Only Lost carries a
 * reason (docs/kamchiliklar.docx §5, matching LeadsController.UpdateStatus's MarkLost path).
 */
export function CloseLeadDrawer({ lead, onClose }: { lead: Lead; onClose: () => void }) {
  const queryClient = useQueryClient();
  const [reason, setReason] = useState("");
  const [confirmingLost, setConfirmingLost] = useState(false);

  const mutation = useMutation({
    mutationFn: (status: LeadStatus) => updateLeadStatus(lead.id, status, status === "Lost" ? (reason || undefined) : undefined),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["leads"] });
      queryClient.invalidateQueries({ queryKey: ["lead", lead.id] });
      onClose();
    },
  });

  return (
    <Drawer open onClose={onClose} title="Lidni yopish" description={lead.fullName}>
      <div className="space-y-3">
        <Button
          variant="secondary"
          className="w-full justify-start"
          loading={mutation.isPending && mutation.variables === "TrialMissed"}
          disabled={mutation.isPending}
          onClick={() => mutation.mutate("TrialMissed")}
        >
          Trial&apos;ni o&apos;tkazib yubordi
        </Button>
        <Button
          variant="secondary"
          className="w-full justify-start"
          loading={mutation.isPending && mutation.variables === "NotInterested"}
          disabled={mutation.isPending}
          onClick={() => mutation.mutate("NotInterested")}
        >
          Qiziqmadi
        </Button>

        {!confirmingLost ? (
          <Button variant="secondary" className="w-full justify-start" disabled={mutation.isPending} onClick={() => setConfirmingLost(true)}>
            Yo&apos;qotildi
          </Button>
        ) : (
          <div className="space-y-2 rounded-lg border border-border p-3">
            <Label htmlFor="close-reason">Sabab (ixtiyoriy)</Label>
            <Input id="close-reason" value={reason} onChange={(e) => setReason(e.target.value)} placeholder="Masalan: narx mos kelmadi" />
            <Button
              variant="danger"
              className="w-full"
              loading={mutation.isPending && mutation.variables === "Lost"}
              onClick={() => mutation.mutate("Lost")}
            >
              Yo&apos;qotilgan deb belgilash
            </Button>
          </div>
        )}

        {mutation.isError && (
          <Alert>{mutation.error instanceof ApiError ? mutation.error.message : "Saqlab bo'lmadi."}</Alert>
        )}
      </div>
    </Drawer>
  );
}
