"use client";

import Link from "next/link";
import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, Check, KeyRound, ShieldCheck, X } from "lucide-react";
import {
  accessLabel, createAccessRequest, decideAccessRequest, GRANTABLE_ACCESS, listAccessRequests, listMyAccessRequests,
  type DataAccessRequest,
} from "@/lib/api/system";
import { ApiError } from "@/lib/api-client";
import { formatShortDateTime } from "@/lib/format-date";
import { usePermissions } from "@/lib/use-permissions";
import { Alert } from "@/components/ui/Alert";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { Card, CardBody, CardHeader, CardTitle } from "@/components/ui/Card";
import { EmptyState } from "@/components/ui/EmptyState";
import { Input, Label, Select, Textarea } from "@/components/ui/Field";
import { PageHeader } from "@/components/ui/PageHeader";
import { Skeleton } from "@/components/ui/Skeleton";

const MIN_REASON = 20;
const HOUR_CHOICES = [1, 2, 4, 8, 24, 72];
const errorText = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

function statusBadge(request: DataAccessRequest) {
  switch (request.status) {
    case "Pending": return <Badge tone="warning" dot>Kutilmoqda</Badge>;
    case "Approved": return request.isActive
      ? <Badge tone="success" dot>Amalda</Badge>
      : <Badge tone="neutral" dot>Muddati tugagan</Badge>;
    case "Rejected": return <Badge tone="danger" dot>Rad etilgan</Badge>;
    case "Revoked": return <Badge tone="neutral" dot>Bekor qilingan</Badge>;
  }
}

/**
 * Temporary access to production data (docs/00_TZ.md §3.8): the Developer asks for one read permission with a reason, an
 * organization-wide approver decides, and the grant lapses on its own. One page, two halves — each shown to whoever holds the
 * permission for it, so a Super Admin sees the queue and a Developer sees the form.
 */
export default function DataAccessPage() {
  const { can, ready } = usePermissions();
  const asks = can("production.data.request");
  const decides = can("production.data.approve");

  return (
    <main className="p-4 sm:p-6 lg:p-8">
      <Link href="/dashboard" className="mb-4 inline-flex items-center gap-1.5 text-[13px] text-ink-500 hover:text-ink-800">
        <ArrowLeft className="size-3.5" aria-hidden />
        Bosh sahifa
      </Link>
      <PageHeader
        title="Ma'lumotga kirish"
        description="Developer talabalar ma'lumotini odatda ko'rmaydi. Zarur bo'lsa sabab ko'rsatib vaqtinchalik ruxsat so'raydi; Super Admin tasdiqlaydi, muddat tugagach ruxsat o'zi yopiladi. Har bir qadam audit jurnaliga yoziladi."
      />
      {!ready && <Skeleton className="h-48" />}
      {ready && !asks && !decides && <EmptyState icon={KeyRound} title="Bu bo'lim siz uchun emas" />}
      <div className="space-y-6">
        {asks && <RequesterView />}
        {decides && <ApproverView />}
      </div>
    </main>
  );
}

function RequesterView() {
  const client = useQueryClient();
  const query = useQuery({ queryKey: ["system", "access", "mine"], queryFn: listMyAccessRequests });

  const [code, setCode] = useState(GRANTABLE_ACCESS[0].code);
  const [hours, setHours] = useState(2);
  const [reason, setReason] = useState("");

  const create = useMutation({
    mutationFn: () => createAccessRequest({ permissionCode: code, reason: reason.trim(), hours }),
    onSuccess: () => {
      setReason("");
      client.invalidateQueries({ queryKey: ["system", "access"] });
    },
  });

  const active = query.data?.filter((r) => r.isActive) ?? [];
  const tooShort = reason.trim().length < MIN_REASON;

  return (
    <div className="grid gap-6 xl:grid-cols-[380px_1fr]">
      <Card className="self-start">
        <CardHeader><CardTitle>Yangi so&apos;rov</CardTitle></CardHeader>
        <CardBody>
          <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); if (!tooShort) create.mutate(); }}>
            <div>
              <Label htmlFor="access-code">Nimaga kirish kerak</Label>
              <Select id="access-code" value={code} onChange={(e) => setCode(e.target.value)}>
                {GRANTABLE_ACCESS.map((a) => <option key={a.code} value={a.code}>{a.label}</option>)}
              </Select>
              <p className="mt-1 text-[12px] text-ink-500">Faqat o&apos;qish. Moliya va hisoblar ro&apos;yxatda yo&apos;q.</p>
            </div>
            <div>
              <Label htmlFor="access-hours">Muddat</Label>
              <Select id="access-hours" value={hours} onChange={(e) => setHours(Number(e.target.value))}>
                {HOUR_CHOICES.map((h) => <option key={h} value={h}>{h} soat</option>)}
              </Select>
            </div>
            <div>
              <Label htmlFor="access-reason">Sabab</Label>
              <Textarea
                id="access-reason"
                rows={4}
                value={reason}
                onChange={(e) => setReason(e.target.value)}
                placeholder="Nima tekshiriladi va nega loglar yetarli emas?"
              />
              <p className={`mt-1 text-[12px] ${tooShort && reason.length > 0 ? "text-warning-600" : "text-ink-500"}`}>
                {reason.trim().length} / kamida {MIN_REASON} belgi
              </p>
            </div>
            {create.isError && <Alert>{errorText(create.error, "So'rov yuborilmadi.")}</Alert>}
            <Button type="submit" className="w-full" loading={create.isPending} disabled={tooShort}>
              <ShieldCheck className="size-4" aria-hidden />
              So&apos;rov yuborish
            </Button>
          </form>
        </CardBody>
      </Card>

      <div className="space-y-6">
        {active.length > 0 && (
          <Card>
            <CardHeader><CardTitle>Amaldagi ruxsatlar</CardTitle></CardHeader>
            <ul className="divide-y divide-border">
              {active.map((r) => (
                <li key={r.id} className="flex flex-wrap items-center justify-between gap-3 px-5 py-3">
                  <div>
                    <p className="text-[14px] font-medium text-ink-900">{accessLabel(r.permissionCode)}</p>
                    <p className="text-[12px] text-ink-500">{r.expiresAt && `${formatShortDateTime(r.expiresAt)} gacha`}</p>
                  </div>
                  <Link href={GRANTABLE_ACCESS.find((a) => a.code === r.permissionCode)?.href ?? "/dashboard"}>
                    <Button size="sm" variant="secondary">Ochish</Button>
                  </Link>
                </li>
              ))}
            </ul>
          </Card>
        )}
        <RequestList title="Mening so'rovlarim" query={query} />
      </div>
    </div>
  );
}

function ApproverView() {
  const client = useQueryClient();
  const query = useQuery({ queryKey: ["system", "access", "all"], queryFn: listAccessRequests, refetchInterval: 20_000 });
  const pending = query.data?.filter((r) => r.status === "Pending") ?? [];
  const rest = query.data?.filter((r) => r.status !== "Pending");

  return (
    <div className="space-y-6">
      <Card>
        <CardHeader>
          <CardTitle>Kutilayotgan so&apos;rovlar</CardTitle>
          {pending.length > 0 && <Badge tone="warning">{pending.length}</Badge>}
        </CardHeader>
        {query.isPending && <CardBody><Skeleton className="h-24" /></CardBody>}
        {query.isError && <CardBody><Alert>{errorText(query.error, "Yuklab bo'lmadi.")}</Alert></CardBody>}
        {query.data && pending.length === 0 && <EmptyState icon={ShieldCheck} title="Kutilayotgan so'rov yo'q" />}
        <ul className="divide-y divide-border">
          {pending.map((r) => (
            <PendingRow key={r.id} request={r} onDone={() => client.invalidateQueries({ queryKey: ["system", "access"] })} />
          ))}
        </ul>
      </Card>

      <RequestList
        title="Barcha so'rovlar"
        query={{ ...query, data: rest }}
        showRequester
        onRevoked={() => client.invalidateQueries({ queryKey: ["system", "access"] })}
      />
    </div>
  );
}

function PendingRow({ request, onDone }: { request: DataAccessRequest; onDone: () => void }) {
  const [hours, setHours] = useState(request.hours);
  const [note, setNote] = useState("");
  const decide = useMutation({
    mutationFn: (action: "approve" | "reject") => decideAccessRequest(request.id, action, { hours, note: note.trim() || undefined }),
    onSuccess: onDone,
  });

  return (
    <li className="space-y-3 px-5 py-4">
      <div className="flex flex-wrap items-center gap-2">
        <span className="text-[14px] font-medium text-ink-900">{request.requesterName}</span>
        <Badge tone="brand">{accessLabel(request.permissionCode)}</Badge>
        <span className="text-[12px] text-ink-500">{request.hours} soat so&apos;raldi · {formatShortDateTime(request.createdAt)}</span>
      </div>
      <p className="whitespace-pre-wrap rounded-lg bg-ink-50 px-3 py-2 text-[13px] text-ink-700">{request.reason}</p>
      <div className="flex flex-wrap items-end gap-3">
        <div>
          <Label htmlFor={`hours-${request.id}`}>Beriladigan muddat</Label>
          <Select id={`hours-${request.id}`} value={hours} onChange={(e) => setHours(Number(e.target.value))} className="w-32">
            {[...new Set([...HOUR_CHOICES, request.hours])].sort((a, b) => a - b).map((h) => <option key={h} value={h}>{h} soat</option>)}
          </Select>
        </div>
        <div className="min-w-48 flex-1">
          <Label htmlFor={`note-${request.id}`}>Izoh</Label>
          <Input id={`note-${request.id}`} value={note} onChange={(e) => setNote(e.target.value)} placeholder="Ixtiyoriy" />
        </div>
        <Button size="sm" loading={decide.isPending && decide.variables === "approve"} onClick={() => decide.mutate("approve")}>
          <Check className="size-3.5" aria-hidden />
          Tasdiqlash
        </Button>
        <Button size="sm" variant="secondary" loading={decide.isPending && decide.variables === "reject"} onClick={() => decide.mutate("reject")}>
          <X className="size-3.5" aria-hidden />
          Rad etish
        </Button>
      </div>
      {decide.isError && <Alert>{errorText(decide.error, "Qaror saqlanmadi.")}</Alert>}
    </li>
  );
}

function RequestList({
  title, query, showRequester, onRevoked,
}: {
  title: string;
  query: { isPending: boolean; isError: boolean; error: unknown; data?: DataAccessRequest[] };
  showRequester?: boolean;
  onRevoked?: () => void;
}) {
  return (
    <Card>
      <CardHeader><CardTitle>{title}</CardTitle></CardHeader>
      {query.isPending && <CardBody><Skeleton className="h-24" /></CardBody>}
      {query.isError && <CardBody><Alert>{errorText(query.error, "Yuklab bo'lmadi.")}</Alert></CardBody>}
      {query.data?.length === 0 && <EmptyState icon={KeyRound} title="So'rov yo'q" />}
      <ul className="divide-y divide-border">
        {query.data?.map((r) => <HistoryRow key={r.id} request={r} showRequester={showRequester} onRevoked={onRevoked} />)}
      </ul>
    </Card>
  );
}

function HistoryRow({ request, showRequester, onRevoked }: { request: DataAccessRequest; showRequester?: boolean; onRevoked?: () => void }) {
  const revoke = useMutation({
    mutationFn: () => decideAccessRequest(request.id, "revoke", {}),
    onSuccess: onRevoked,
  });

  return (
    <li className="px-5 py-3.5">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex flex-wrap items-center gap-2">
          {showRequester && <span className="text-[14px] font-medium text-ink-900">{request.requesterName}</span>}
          <Badge tone="brand">{accessLabel(request.permissionCode)}</Badge>
          {statusBadge(request)}
        </div>
        {onRevoked && request.isActive && (
          <Button size="sm" variant="secondary" loading={revoke.isPending} onClick={() => revoke.mutate()}>Bekor qilish</Button>
        )}
      </div>
      <p className="mt-1.5 whitespace-pre-wrap text-[13px] text-ink-700">{request.reason}</p>
      <p className="mt-1 text-[12px] text-ink-500">
        {formatShortDateTime(request.createdAt)} · {request.hours} soat so&apos;raldi
        {request.decidedByName && <> · {request.decidedByName} qaror qildi</>}
        {request.expiresAt && request.status === "Approved" && <> · {formatShortDateTime(request.expiresAt)} gacha</>}
      </p>
      {request.decisionNote && <p className="mt-0.5 text-[12px] italic text-ink-500">«{request.decisionNote}»</p>}
      {revoke.isError && <Alert>{errorText(revoke.error, "Bekor qilib bo'lmadi.")}</Alert>}
    </li>
  );
}
