"use client";

import { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { parsePreferredTime, updateLeadPreferences, type Lead, type PreferredTimeCode } from "@/lib/api/crm";
import { ApiError } from "@/lib/api-client";
import { Drawer } from "@/components/ui/Drawer";
import { Button } from "@/components/ui/Button";
import { Alert } from "@/components/ui/Alert";
import { LeadPreferencesFields } from "@/components/crm/LeadPreferencesFields";

/** Edit a lead's preferred time and level. The trial and convert drawers rank groups on these, so they are refreshed on save. */
export function LeadPreferencesDrawer({
  lead, suggestedLevel, onClose,
}: { lead: Lead; suggestedLevel?: string | null; onClose: () => void }) {
  const queryClient = useQueryClient();
  const [times, setTimes] = useState<PreferredTimeCode[]>(() => parsePreferredTime(lead.preferredTime));
  const [level, setLevel] = useState(lead.currentLevel ?? "");

  const save = useMutation({
    // "" (not undefined) is what tells the server to clear a field.
    mutationFn: () => updateLeadPreferences(lead.id, { preferredTime: times.join(","), currentLevel: level.trim() }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["lead", lead.id] });
      queryClient.invalidateQueries({ queryKey: ["leads"] });
      queryClient.invalidateQueries({ queryKey: ["group-recommendations", lead.id] });
      onClose();
    },
  });

  return (
    <Drawer open onClose={onClose} title="Qulay vaqt va daraja" description={lead.fullName}>
      <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); save.mutate(); }}>
        <LeadPreferencesFields idPrefix="prefs" times={times} onTimes={setTimes} level={level} onLevel={setLevel} suggestedLevel={suggestedLevel} />
        {save.isError && <Alert>{save.error instanceof ApiError ? save.error.message : "Saqlab bo'lmadi."}</Alert>}
        <Button type="submit" loading={save.isPending} className="w-full">Saqlash</Button>
      </form>
    </Drawer>
  );
}
