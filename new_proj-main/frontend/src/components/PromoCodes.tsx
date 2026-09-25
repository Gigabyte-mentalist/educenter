"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Plus, Ticket } from "lucide-react";
import { listCourses } from "@/lib/api/academic";
import {
  createPromoCode, deactivatePromoCode, formatDay, formatDiscountValue, listPromoCodes, type DiscountType, type PromoCode,
} from "@/lib/api/finance";
import { ApiError } from "@/lib/api-client";
import { useAuthSession } from "@/lib/use-auth-session";
import { Alert } from "@/components/ui/Alert";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { Drawer } from "@/components/ui/Drawer";
import { EmptyState } from "@/components/ui/EmptyState";
import { Input, Label, Select } from "@/components/ui/Field";
import { Skeleton } from "@/components/ui/Skeleton";
import { Table, TBody, TD, TH, THead, TRow } from "@/components/ui/Table";
import { canManagePromoCodes } from "@/components/Finance";

const errorText = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

function validity(p: PromoCode): string {
  if (!p.validFrom && !p.validUntil) return "Muddatsiz";
  if (p.validFrom && p.validUntil) return `${formatDay(p.validFrom)} – ${formatDay(p.validUntil)}`;
  return p.validFrom ? `${formatDay(p.validFrom)} dan` : `${formatDay(p.validUntil!)} gacha`;
}

/** Promo codes: everyone who may apply discounts sees them; only organization-wide finance managers create or stop them. */
export function PromoCodesPanel() {
  const client = useQueryClient();
  const canManage = canManagePromoCodes(useAuthSession().user?.role);
  const query = useQuery({ queryKey: ["finance", "promo-codes"], queryFn: listPromoCodes });
  const [creating, setCreating] = useState(false);

  const deactivate = useMutation({
    mutationFn: deactivatePromoCode,
    onSuccess: () => client.invalidateQueries({ queryKey: ["finance", "promo-codes"] }),
  });

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-[13px] text-ink-500">
          Promo-kod hisob-fakturaga «Chegirma» oynasi orqali qo&apos;llanadi.
          {!canManage && " Yangi promo-kodni Super Admin yaratadi."}
        </p>
        {canManage && (
          <Button size="sm" onClick={() => setCreating(true)}>
            <Plus className="size-3.5" aria-hidden />
            Promo-kod yaratish
          </Button>
        )}
      </div>

      {query.isLoading && <Skeleton className="h-32" />}
      {query.isError && <Alert>{errorText(query.error, "Promo-kodlarni yuklab bo'lmadi.")}</Alert>}
      {deactivate.isError && <Alert>{errorText(deactivate.error, "Promo-kodni to'xtatib bo'lmadi.")}</Alert>}
      {query.data?.length === 0 && (
        <div className="rounded-xl border border-border bg-surface">
          <EmptyState icon={Ticket} title="Promo-kodlar yo'q" />
        </div>
      )}

      {!!query.data?.length && (
        <div className="overflow-x-auto">
          <Table>
            <THead>
              <tr>
                <TH>Kod</TH>
                <TH>Chegirma</TH>
                <TH>Kurs</TH>
                <TH>Amal qilish</TH>
                <TH className="text-right">Foydalanildi</TH>
                <TH>Holat</TH>
                {canManage && <TH />}
              </tr>
            </THead>
            <TBody>
              {query.data.map((p) => (
                <TRow key={p.id}>
                  <TD>
                    <span className="font-mono font-medium text-ink-900">{p.code}</span>
                    {p.description && <p className="text-[12px] text-ink-500">{p.description}</p>}
                  </TD>
                  <TD className="font-tabular">{formatDiscountValue(p.type, p.value)}</TD>
                  <TD className="text-ink-600">{p.courseName ?? "Barcha kurslar"}</TD>
                  <TD className="text-[13px] text-ink-600">{validity(p)}</TD>
                  <TD className="text-right font-tabular">
                    {p.usedCount}
                    {p.maxUses !== null && <span className="text-ink-400"> / {p.maxUses}</span>}
                  </TD>
                  <TD>
                    <Badge tone={p.isActive ? "success" : "neutral"} dot>{p.isActive ? "Faol" : "To'xtatilgan"}</Badge>
                  </TD>
                  {canManage && (
                    <TD className="text-right">
                      {p.isActive && (
                        <Button size="sm" variant="ghost" loading={deactivate.isPending && deactivate.variables === p.id} onClick={() => deactivate.mutate(p.id)}>
                          To&apos;xtatish
                        </Button>
                      )}
                    </TD>
                  )}
                </TRow>
              ))}
            </TBody>
          </Table>
        </div>
      )}

      {creating && <CreatePromoCodeDrawer onClose={() => setCreating(false)} />}
    </div>
  );
}

function CreatePromoCodeDrawer({ onClose }: { onClose: () => void }) {
  const client = useQueryClient();
  const coursesQuery = useQuery({ queryKey: ["courses"], queryFn: listCourses });

  const [code, setCode] = useState("");
  const [description, setDescription] = useState("");
  const [type, setType] = useState<DiscountType>("Percent");
  const [value, setValue] = useState("");
  const [validFrom, setValidFrom] = useState("");
  const [validUntil, setValidUntil] = useState("");
  const [maxUses, setMaxUses] = useState("");
  const [courseId, setCourseId] = useState("");

  const mutation = useMutation({
    mutationFn: () =>
      createPromoCode({
        code: code.trim(),
        description: description.trim() || undefined,
        type,
        value: Number(value.replace(/\s/g, "").replace(",", ".")),
        validFrom: validFrom || undefined,
        validUntil: validUntil || undefined,
        maxUses: maxUses ? Number(maxUses) : undefined,
        courseId: courseId || undefined,
      }),
    onSuccess: async () => {
      await client.invalidateQueries({ queryKey: ["finance", "promo-codes"] });
      onClose();
    },
  });

  return (
    <Drawer open onClose={onClose} title="Promo-kod yaratish">
      <form
        className="space-y-4"
        onSubmit={(e) => {
          e.preventDefault();
          mutation.mutate();
        }}
      >
        <div>
          <Label htmlFor="promo-code">Kod</Label>
          <Input id="promo-code" required className="font-mono uppercase" maxLength={32} value={code} onChange={(e) => setCode(e.target.value)} placeholder="WELCOME10" />
          <p className="mt-1 text-[12px] text-ink-500">Lotin harflari, raqamlar, «-» va «_»; 3–32 belgi.</p>
        </div>
        <div>
          <Label htmlFor="promo-description">Izoh (ixtiyoriy)</Label>
          <Input id="promo-description" maxLength={200} value={description} onChange={(e) => setDescription(e.target.value)} placeholder="Yangi o'quvchilar aksiyasi" />
        </div>
        <div className="grid grid-cols-2 gap-3">
          <div>
            <Label htmlFor="promo-type">Turi</Label>
            <Select id="promo-type" value={type} onChange={(e) => setType(e.target.value as DiscountType)}>
              <option value="Percent">Foiz</option>
              <option value="Fixed">Summa</option>
            </Select>
          </div>
          <div>
            <Label htmlFor="promo-value">{type === "Percent" ? "Foiz (%)" : "Summa (so'm)"}</Label>
            <Input id="promo-value" required inputMode="decimal" value={value} onChange={(e) => setValue(e.target.value)} />
          </div>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <div>
            <Label htmlFor="promo-from">Boshlanish (ixtiyoriy)</Label>
            <Input id="promo-from" type="date" value={validFrom} onChange={(e) => setValidFrom(e.target.value)} />
          </div>
          <div>
            <Label htmlFor="promo-until">Tugash (ixtiyoriy)</Label>
            <Input id="promo-until" type="date" value={validUntil} onChange={(e) => setValidUntil(e.target.value)} />
          </div>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <div>
            <Label htmlFor="promo-max">Foydalanish limiti</Label>
            <Input id="promo-max" type="number" min={1} value={maxUses} onChange={(e) => setMaxUses(e.target.value)} placeholder="Cheksiz" />
          </div>
          <div>
            <Label htmlFor="promo-course">Kurs</Label>
            <Select id="promo-course" value={courseId} onChange={(e) => setCourseId(e.target.value)}>
              <option value="">Barcha kurslar</option>
              {coursesQuery.data?.map((c) => (
                <option key={c.id} value={c.id}>{c.name}</option>
              ))}
            </Select>
          </div>
        </div>
        {mutation.isError && <Alert>{errorText(mutation.error, "Promo-kodni yaratib bo'lmadi.")}</Alert>}
        <Button type="submit" loading={mutation.isPending} disabled={!code.trim() || !value.trim()} className="w-full">
          Yaratish
        </Button>
      </form>
    </Drawer>
  );
}
