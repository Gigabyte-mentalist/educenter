"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { listGroups } from "@/lib/api/academic";
import { listBranches } from "@/lib/api/branches";
import {
  announcementPlace, AUDIENCE_LABEL, createAnnouncement, updateAnnouncement,
  type Announcement, type AnnouncementAudience,
} from "@/lib/api/announcements";
import { ApiError } from "@/lib/api-client";
import { usePermissions } from "@/lib/use-permissions";
import { Alert } from "@/components/ui/Alert";
import { Button } from "@/components/ui/Button";
import { Drawer } from "@/components/ui/Drawer";
import { Input, Label, Select, Textarea } from "@/components/ui/Field";

type Place = "organization" | "branch" | "group";

/** A calendar date as `<input type="date">` wants it, in the person's own time zone. */
const toDateInput = (iso: string) => {
  const d = new Date(iso);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
};

/** The last moment of the chosen day, so "until the 30th" still shows on the 30th. */
const endOfDayIso = (date: string) => new Date(`${date}T23:59:59`).toISOString();

/**
 * Publishing an announcement, or editing one that is up. Where it applies — the whole organization, one branch, one group — is chosen when
 * it is published and then fixed (the people it was sent to have already been told); the wording, the audience and the expiry can change.
 * An administrator's choices stop at their own branches: the whole-organization option is offered only to whoever looks after all of it.
 */
export function AnnouncementDrawer({ existing, onClose }: { existing?: Announcement; onClose: () => void }) {
  const queryClient = useQueryClient();
  const { scopeOf } = usePermissions();
  const wholeOrganization = scopeOf("announcements.manage") === "AllOrganization";

  const [title, setTitle] = useState(existing?.title ?? "");
  const [body, setBody] = useState(existing?.body ?? "");
  const [audience, setAudience] = useState<AnnouncementAudience>(existing?.audience ?? "All");
  const [place, setPlace] = useState<Place>(wholeOrganization ? "organization" : "branch");
  const [branchId, setBranchId] = useState("");
  const [groupId, setGroupId] = useState("");
  const [expires, setExpires] = useState(existing?.expiresAt ? toDateInput(existing.expiresAt) : "");

  const branches = useQuery({ queryKey: ["branches"], queryFn: listBranches, enabled: !existing });
  const groups = useQuery({ queryKey: ["groups"], queryFn: listGroups, enabled: !existing && place === "group" });

  const save = useMutation({
    mutationFn: () => existing
      ? updateAnnouncement(existing.id, { title, body, audience, expiresAt: expires ? endOfDayIso(expires) : undefined, clearExpiry: expires === "" })
      : createAnnouncement({
        title, body, audience,
        branchId: place === "branch" ? branchId : undefined,
        groupId: place === "group" ? groupId : undefined,
        expiresAt: expires ? endOfDayIso(expires) : undefined,
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["announcements"] });
      onClose();
    },
  });

  const placeReady = existing !== undefined || place === "organization" || (place === "branch" && branchId !== "") || (place === "group" && groupId !== "");

  return (
    <Drawer open onClose={onClose} title={existing ? "E'lonni tahrirlash" : "Yangi e'lon"} description={existing ? announcementPlace(existing) : undefined}>
      <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); save.mutate(); }}>
        <div>
          <Label htmlFor="ann-title">Sarlavha</Label>
          <Input id="ann-title" required maxLength={200} value={title} onChange={(e) => setTitle(e.target.value)} />
        </div>
        <div>
          <Label htmlFor="ann-body">Matn</Label>
          <Textarea id="ann-body" required maxLength={4000} rows={6} value={body} onChange={(e) => setBody(e.target.value)} />
        </div>
        <div>
          <Label htmlFor="ann-audience">Kimga</Label>
          <Select id="ann-audience" value={audience} onChange={(e) => setAudience(e.target.value as AnnouncementAudience)}>
            {(Object.keys(AUDIENCE_LABEL) as AnnouncementAudience[]).map((a) => <option key={a} value={a}>{AUDIENCE_LABEL[a]}</option>)}
          </Select>
        </div>

        {!existing && (
          <>
            <div>
              <Label htmlFor="ann-place">Qayerga</Label>
              <Select id="ann-place" value={place} onChange={(e) => setPlace(e.target.value as Place)}>
                {wholeOrganization && <option value="organization">Butun markaz</option>}
                <option value="branch">Bitta filial</option>
                <option value="group">Bitta guruh</option>
              </Select>
            </div>
            {place === "branch" && (
              <div>
                <Label htmlFor="ann-branch">Filial</Label>
                <Select id="ann-branch" required value={branchId} onChange={(e) => setBranchId(e.target.value)}>
                  <option value="">Tanlang</option>
                  {branches.data?.map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}
                </Select>
              </div>
            )}
            {place === "group" && (
              <div>
                <Label htmlFor="ann-group">Guruh</Label>
                <Select id="ann-group" required value={groupId} onChange={(e) => setGroupId(e.target.value)}>
                  <option value="">Tanlang</option>
                  {groups.data?.map((g) => <option key={g.id} value={g.id}>{g.name}</option>)}
                </Select>
              </div>
            )}
          </>
        )}

        <div>
          <Label htmlFor="ann-expires">Amal qilish muddati (ixtiyoriy)</Label>
          <Input id="ann-expires" type="date" value={expires} onChange={(e) => setExpires(e.target.value)} />
          <p className="mt-1.5 text-[12px] text-ink-500">Shu kundan keyin e&apos;lon ko&apos;rinmaydi. Bo&apos;sh qoldirsangiz, o&apos;chirmaguningizcha turadi.</p>
        </div>

        {!existing && (
          <p className="text-[12.5px] text-ink-500">Chiqarilganda kimga qaratilgan bo&apos;lsa, o&apos;shalarga bildirishnoma boradi.</p>
        )}
        {save.isError && <Alert>{save.error instanceof ApiError ? save.error.message : "Saqlab bo'lmadi."}</Alert>}
        <Button type="submit" loading={save.isPending} disabled={!placeReady} className="w-full">{existing ? "Saqlash" : "E'lon qilish"}</Button>
      </form>
    </Drawer>
  );
}
