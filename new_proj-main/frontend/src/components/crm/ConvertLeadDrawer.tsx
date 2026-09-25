"use client";

import { useState } from "react";
import Link from "next/link";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CheckCircle2, CreditCard, KeyRound, UsersRound } from "lucide-react";
import { listGroups } from "@/lib/api/academic";
import { convertLead, getGroupRecommendations, listPlacementAttempts, type ConvertedStudent, type Lead } from "@/lib/api/crm";
import { ApiError } from "@/lib/api-client";
import { Drawer } from "@/components/ui/Drawer";
import { Button } from "@/components/ui/Button";
import { Alert } from "@/components/ui/Alert";
import { Input, Label, Select } from "@/components/ui/Field";

/**
 * Lead → Student. What it does is spelled out at the top because "Studentga aylantirish" alone does not say that a login
 * account is created and the first month is billed — the two things an operator has to tell the person afterwards.
 * `defaultGroupId` is the group of the trial the lead just attended: they came to that class, so it is the obvious choice.
 */
export function ConvertLeadDrawer({
  lead, onClose, defaultGroupId,
}: { lead: Lead; onClose: () => void; defaultGroupId?: string }) {
  const queryClient = useQueryClient();
  const groupsQuery = useQuery({ queryKey: ["groups"], queryFn: listGroups });
  // Group Recommendation Engine (docs/00_TZ.md §12) — ranks this lead's open groups so the
  // "Guruh" select can list the best fit first instead of a flat alphabetical dump.
  const recommendationsQuery = useQuery({
    queryKey: ["group-recommendations", lead.id],
    queryFn: () => getGroupRecommendations(lead.id),
  });
  const attemptsQuery = useQuery({
    queryKey: ["placement-attempts", lead.id],
    queryFn: () => listPlacementAttempts(lead.id),
  });

  const [groupId, setGroupId] = useState(defaultGroupId ?? "");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [level, setLevel] = useState("");
  const [created, setCreated] = useState<ConvertedStudent | null>(null);

  const convertMutation = useMutation({
    mutationFn: () => convertLead(lead.id, { groupId, email, password, currentLevel: level || undefined }),
    onSuccess: (student) => {
      queryClient.invalidateQueries({ queryKey: ["leads"] });
      queryClient.invalidateQueries({ queryKey: ["lead", lead.id] });
      queryClient.invalidateQueries({ queryKey: ["students"] });
      queryClient.invalidateQueries({ queryKey: ["groups"] });
      setCreated(student);
    },
  });

  const chosenGroupName = groupsQuery.data?.find((g) => g.id === groupId)?.name;

  if (created) {
    return (
      <Drawer open onClose={onClose} title="Student yaratildi" description={lead.fullName}>
        <div className="space-y-5">
          <div className="flex items-start gap-3 rounded-xl bg-success-50 p-4">
            <CheckCircle2 className="mt-0.5 size-5 shrink-0 text-success-600" aria-hidden />
            <div className="text-[13px] text-ink-700">
              <p className="font-medium text-ink-900">{lead.fullName} endi student</p>
              <p className="mt-1">
                Raqami <span className="font-mono">{created.studentNumber}</span>
                {chosenGroupName && <> · guruh <strong>{chosenGroupName}</strong></>}
              </p>
            </div>
          </div>
          <div className="space-y-1.5 rounded-xl border border-border p-4 text-[13px] text-ink-700">
            <p className="font-medium text-ink-900">Talabaga aytib qo&apos;ying</p>
            <p>Login: <span className="font-mono">{email}</span></p>
            <p>Parol: siz kiritgan vaqtinchalik parol</p>
          </div>
          <div className="flex flex-col gap-2">
            <Link href={`/students/${created.id}`}><Button className="w-full">Student profilini ochish</Button></Link>
            <Button variant="secondary" onClick={onClose}>Yopish</Button>
          </div>
        </div>
      </Drawer>
    );
  }

  return (
    <Drawer open onClose={onClose} title="Studentga aylantirish" description={lead.fullName}>
      <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); convertMutation.mutate(); }}>
        <ul className="space-y-2 rounded-xl bg-ink-50 p-3.5 text-[12.5px] text-ink-700">
          <li className="flex items-start gap-2"><KeyRound className="mt-0.5 size-3.5 shrink-0 text-ink-400" aria-hidden />Tizimga kirish uchun login akkaunt yaratiladi</li>
          <li className="flex items-start gap-2"><UsersRound className="mt-0.5 size-3.5 shrink-0 text-ink-400" aria-hidden />Tanlangan guruhga yoziladi</li>
          <li className="flex items-start gap-2"><CreditCard className="mt-0.5 size-3.5 shrink-0 text-ink-400" aria-hidden />Birinchi oylik to&apos;lov hisobi avtomatik ochiladi</li>
        </ul>
        {lead.latestTrial?.attendanceStatus === "Attended" && (
          <p className="text-[12.5px] text-ink-500">
            Trial&apos;da <strong>{lead.latestTrial.groupName}</strong> guruhida qatnashgan.
          </p>
        )}
        <div>
          <Label htmlFor="convert-group">Guruh</Label>
          <Select id="convert-group" required value={groupId} onChange={(e) => setGroupId(e.target.value)}>
            <option value="">Tanlang</option>
            {recommendationsQuery.data && recommendationsQuery.data.length > 0 && (
              <optgroup label="Tavsiya etilgan">
                {recommendationsQuery.data.map((r) => (
                  <option key={r.groupId} value={r.groupId}>{r.groupName} — {r.matchPercent}% mos · {r.freeSeats} ta o&apos;rin</option>
                ))}
              </optgroup>
            )}
            <optgroup label="Boshqa guruhlar">
              {groupsQuery.data
                ?.filter((g) => !recommendationsQuery.data?.some((r) => r.groupId === g.id) && (g.status === "Active" || g.status === "Forming"))
                .map((g) => <option key={g.id} value={g.id}>{g.name}</option>)}
            </optgroup>
          </Select>
        </div>
        <div>
          <Label htmlFor="convert-level">Daraja (ixtiyoriy)</Label>
          <Input id="convert-level" value={level} onChange={(e) => setLevel(e.target.value)} placeholder="B1, IELTS Intermediate..." />
          {attemptsQuery.data && attemptsQuery.data.length > 0 && (
            <p className="mt-1.5 text-[12px] text-ink-500">
              Daraja testi tavsiyasi: <strong>{attemptsQuery.data[0].recommendedLevel}</strong>{" "}
              <button type="button" className="text-brand-700 hover:underline" onClick={() => setLevel(attemptsQuery.data![0].recommendedLevel)}>
                qo&apos;llash
              </button>
            </p>
          )}
        </div>
        <div>
          <Label htmlFor="convert-email">Login uchun email</Label>
          <Input id="convert-email" type="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
          <p className="mt-1 text-[12px] text-ink-500">Student tizimga shu email bilan kiradi.</p>
        </div>
        <div>
          <Label htmlFor="convert-password">Vaqtinchalik parol</Label>
          <Input id="convert-password" type="text" required minLength={8} value={password} onChange={(e) => setPassword(e.target.value)} />
          <p className="mt-1 text-[12px] text-ink-500">Kamida 8 belgi. Talabaga shuni aytasiz.</p>
        </div>
        {convertMutation.isError && (
          <Alert>{convertMutation.error instanceof ApiError ? convertMutation.error.message : "Konvertatsiya qilib bo'lmadi."}</Alert>
        )}
        <Button type="submit" loading={convertMutation.isPending} className="w-full">Studentga aylantirish</Button>
      </form>
    </Drawer>
  );
}
