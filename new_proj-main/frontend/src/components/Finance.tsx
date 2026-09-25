"use client";

import Link from "next/link";
import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AlertOctagon, ArrowLeftRight, CalendarClock, CalendarPlus, Percent, Receipt, RotateCcw, Scissors, Wallet } from "lucide-react";
import {
  applyDiscount, applyPromoCode, canSplitInvoice, formatDay, formatDiscountValue, formatMoney, getFinanceLimits, getStudentFinance,
  isOpenInvoice, issueAdvanceInvoices, listInvoices, MAX_INSTALLMENTS, PAYMENT_METHOD_LABEL, recordPayment, refundPayment, revokeDiscount,
  splitInvoice, voidPayment, type DiscountType, type Invoice, type Payment, type PaymentMethod, type Refund,
} from "@/lib/api/finance";
import { ApiError } from "@/lib/api-client";
import { useAuthSession } from "@/lib/use-auth-session";
import { usePermissions } from "@/lib/use-permissions";
import { Alert } from "@/components/ui/Alert";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { Drawer } from "@/components/ui/Drawer";
import { EmptyState } from "@/components/ui/EmptyState";
import { Input, Label, Select, Textarea } from "@/components/ui/Field";
import { Skeleton } from "@/components/ui/Skeleton";
import { StatTile } from "@/components/ui/StatTile";
import { Table, TBody, TD, TH, THead, TRow } from "@/components/ui/Table";
import { cn } from "@/lib/cn";
import { formatDateTime } from "@/lib/format-date";

/** UI gates mirroring docs/01_role_permission_matrix.md §3.7 — the API enforces the same rules. */
export const canTakePayments = (role?: string) => role === "SUPER_ADMIN" || role === "ADMINISTRATOR";
export const canSeePayments = (role?: string) => canTakePayments(role) || role === "STUDENT" || role === "PARENT";
export const canManagePromoCodes = (role?: string) => role === "SUPER_ADMIN";

const errorText = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);
const parseAmount = (value: string) => Number(value.replace(/\s/g, "").replace(",", "."));

export function useInvalidateFinance() {
  const client = useQueryClient();
  return () =>
    Promise.all([
      client.invalidateQueries({ queryKey: ["finance"] }),
      client.invalidateQueries({ queryKey: ["students"] }),
      client.invalidateQueries({ queryKey: ["crm-tasks"] }),
    ]);
}

// ============================================================================================ tables

export function InvoiceStatusBadge({ invoice }: { invoice: Invoice }) {
  if (invoice.status === "Split") return <Badge tone="info">Qismlarga bo&apos;lingan</Badge>;
  if (invoice.status === "Cancelled") return <Badge tone="neutral">Bekor qilingan</Badge>;
  if (invoice.status === "Paid") return <Badge tone="success" dot>To&apos;langan</Badge>;
  if (invoice.isOverdue) return <Badge tone="danger" dot>{invoice.daysOverdue} kun kechikkan</Badge>;
  if (invoice.status === "PartiallyPaid") return <Badge tone="warning" dot>Qisman to&apos;langan</Badge>;
  return <Badge tone="brand" dot>To&apos;lanmagan</Badge>;
}

/**
 * A split invoice is followed by its parts, so "600 000, then 3 x 200 000" reads as one thing. Only parts whose invoice is on the same
 * page are moved under it; a part whose invoice fell on another page stays where it is (it carries its "2/3" mark anyway).
 */
function withPartsUnderTheirInvoice(invoices: Invoice[]): { invoice: Invoice; nested: boolean }[] {
  const ids = new Set(invoices.map((i) => i.id));
  const parts = new Map<string, Invoice[]>();
  for (const invoice of invoices) {
    if (invoice.parentInvoiceId && ids.has(invoice.parentInvoiceId)) {
      parts.set(invoice.parentInvoiceId, [...(parts.get(invoice.parentInvoiceId) ?? []), invoice]);
    }
  }
  return invoices
    .filter((i) => !(i.parentInvoiceId && ids.has(i.parentInvoiceId)))
    .flatMap((invoice) => [
      { invoice, nested: false },
      ...[...(parts.get(invoice.id) ?? [])].sort((a, b) => (a.installmentNumber ?? 0) - (b.installmentNumber ?? 0)).map((part) => ({ invoice: part, nested: true })),
    ]);
}

export function InvoicesTable({
  invoices,
  showStudent = false,
  onPay,
  onDiscount,
  onRevokeDiscount,
  onSplit,
}: {
  invoices: Invoice[];
  showStudent?: boolean;
  onPay?: (invoice: Invoice) => void;
  onDiscount?: (invoice: Invoice) => void;
  onRevokeDiscount?: (invoice: Invoice) => void;
  onSplit?: (invoice: Invoice) => void;
}) {
  const hasActions = Boolean(onPay || onDiscount || onRevokeDiscount || onSplit);

  if (invoices.length === 0) {
    return (
      <div className="rounded-xl border border-border bg-surface">
        <EmptyState
          icon={Receipt}
          title="Hisob-fakturalar yo'q"
          description="Kursga oylik to'lov belgilangan va student guruhda faol bo'lsa, hisob-faktura avtomatik yaratiladi."
        />
      </div>
    );
  }

  return (
    <div className="overflow-x-auto">
      <Table>
        <THead>
          <tr>
            {showStudent && <TH>Student</TH>}
            <TH>Guruh / davr</TH>
            <TH>Muddat</TH>
            <TH className="text-right">Summa</TH>
            <TH className="text-right">To&apos;langan</TH>
            <TH className="text-right">Qoldiq</TH>
            <TH>Holat</TH>
            {hasActions && <TH />}
          </tr>
        </THead>
        <TBody>
          {withPartsUnderTheirInvoice(invoices).map(({ invoice, nested }) => {
            const open = isOpenInvoice(invoice);
            const split = invoice.status === "Split";
            return (
              <TRow key={invoice.id} className={cn(split && "bg-ink-50/50 text-ink-500")}>
                {showStudent && (
                  <TD>
                    <Link href={`/students/${invoice.studentId}`} className="font-medium text-ink-900 hover:text-brand-700">
                      {invoice.studentName}
                    </Link>
                    <p className="font-mono text-[12px] text-ink-500">{invoice.studentNumber}</p>
                  </TD>
                )}
                <TD className={nested ? "border-l-2 border-brand-200 pl-6" : undefined}>
                  <p className="flex flex-wrap items-center gap-1.5 text-ink-800">
                    {invoice.groupName}
                    {invoice.installmentNumber != null && (
                      <Badge tone="info">{invoice.installmentNumber}/{invoice.installmentCount}</Badge>
                    )}
                  </p>
                  <p className="text-[12.5px] text-ink-500">
                    {formatDay(invoice.periodStart)} – {formatDay(invoice.periodEnd, -1)}
                  </p>
                </TD>
                <TD className="font-tabular text-[13px]">{formatDay(invoice.dueDate)}</TD>
                <TD className="text-right">
                  <p className="font-tabular text-ink-900">{formatMoney(invoice.netAmount, invoice.currency)}</p>
                  {invoice.netAmount !== invoice.amount && (
                    <p className="font-tabular text-[12px] text-ink-400 line-through">{formatMoney(invoice.amount, invoice.currency)}</p>
                  )}
                  {invoice.discount && (
                    <p className="text-[12px] text-brand-700">
                      Chegirma {formatDiscountValue(invoice.discount.type, invoice.discount.value)}
                      {invoice.discount.promoCode && <span className="font-mono"> · {invoice.discount.promoCode}</span>}
                    </p>
                  )}
                  {invoice.creditedAmount > 0 && (
                    <p className="text-[12px] text-warning-600">Qaytarildi −{formatMoney(invoice.creditedAmount, invoice.currency)}</p>
                  )}
                </TD>
                <TD className="text-right font-tabular text-ink-600">{formatMoney(invoice.paidAmount, invoice.currency)}</TD>
                <TD className={cn("text-right font-tabular font-medium", invoice.isOverdue ? "text-danger-600" : "text-ink-900")}>
                  {split ? <span className="font-normal text-ink-400" title="Qoldiq shu hisob-fakturaning qismlarida">—</span> : formatMoney(invoice.balance, invoice.currency)}
                </TD>
                <TD>
                  <InvoiceStatusBadge invoice={invoice} />
                  {invoice.cancelReason && <p className="mt-1 max-w-[220px] text-[12px] text-ink-500">{invoice.cancelReason}</p>}
                </TD>
                {hasActions && (
                  <TD>
                    <div className="flex flex-wrap justify-end gap-1.5">
                      {onPay && open && (
                        <Button size="sm" variant="secondary" onClick={() => onPay(invoice)}>
                          To&apos;lov
                        </Button>
                      )}
                      {onDiscount && open && !invoice.discount && (
                        <Button size="sm" variant="ghost" onClick={() => onDiscount(invoice)}>
                          <Percent className="size-3.5" aria-hidden />
                          Chegirma
                        </Button>
                      )}
                      {onSplit && canSplitInvoice(invoice) && (
                        <Button size="sm" variant="ghost" onClick={() => onSplit(invoice)}>
                          <Scissors className="size-3.5" aria-hidden />
                          Bo&apos;lish
                        </Button>
                      )}
                      {onRevokeDiscount && invoice.discount && invoice.status !== "Cancelled" && (
                        <Button size="sm" variant="ghost" onClick={() => onRevokeDiscount(invoice)}>
                          Chegirmani olib tashlash
                        </Button>
                      )}
                    </div>
                  </TD>
                )}
              </TRow>
            );
          })}
        </TBody>
      </Table>
    </div>
  );
}

export function PaymentsTable({
  payments,
  showStudent = false,
  onVoid,
  onRefund,
}: {
  payments: Payment[];
  showStudent?: boolean;
  onVoid?: (payment: Payment) => void;
  onRefund?: (payment: Payment) => void;
}) {
  const hasActions = Boolean(onVoid || onRefund);

  if (payments.length === 0) {
    return (
      <div className="rounded-xl border border-border bg-surface">
        <EmptyState icon={Wallet} title="To'lovlar hali yo'q" />
      </div>
    );
  }

  return (
    <div className="overflow-x-auto">
      <Table>
        <THead>
          <tr>
            <TH>Sana</TH>
            {showStudent && <TH>Student</TH>}
            <TH>Hisob-faktura</TH>
            <TH>Usul</TH>
            <TH>Qabul qildi</TH>
            <TH className="text-right">Summa</TH>
            {hasActions && <TH />}
          </tr>
        </THead>
        <TBody>
          {payments.map((payment) => {
            const refundable = payment.isVoided ? 0 : payment.amount - payment.refundedAmount;
            return (
              <TRow key={payment.id} className={payment.isVoided ? "opacity-70" : undefined}>
                <TD className="font-tabular text-[13px]">{formatDateTime(payment.paidAt)}</TD>
                {showStudent && (
                  <TD>
                    <Link href={`/students/${payment.studentId}`} className="font-medium text-ink-900 hover:text-brand-700">
                      {payment.studentName}
                    </Link>
                  </TD>
                )}
                <TD>
                  <p className="text-[13px] text-ink-700">{payment.invoiceDescription}</p>
                  {payment.note && <p className="text-[12px] text-ink-500">{payment.note}</p>}
                </TD>
                <TD>{PAYMENT_METHOD_LABEL[payment.method]}</TD>
                <TD className="text-ink-600">{payment.receivedByName ?? "—"}</TD>
                <TD className="text-right">
                  <span className={cn("font-tabular font-medium", payment.isVoided ? "text-ink-400 line-through" : "text-ink-900")}>
                    {formatMoney(payment.amount)}
                  </span>
                  {payment.refundedAmount > 0 && (
                    <p className="text-[12px] text-warning-600">Qaytarildi −{formatMoney(payment.refundedAmount)}</p>
                  )}
                  {payment.isVoided && (
                    <p className="mt-0.5 text-[12px] text-ink-500">
                      <Badge tone="neutral">Bekor qilingan</Badge> {payment.voidReason}
                    </p>
                  )}
                </TD>
                {hasActions && (
                  <TD>
                    <div className="flex flex-wrap justify-end gap-1.5">
                      {onRefund && refundable > 0 && (
                        <Button size="sm" variant="ghost" onClick={() => onRefund(payment)}>
                          <ArrowLeftRight className="size-3.5" aria-hidden />
                          Refund
                        </Button>
                      )}
                      {onVoid && !payment.isVoided && payment.refundedAmount === 0 && (
                        <Button size="sm" variant="ghost" onClick={() => onVoid(payment)}>
                          <RotateCcw className="size-3.5" aria-hidden />
                          Bekor qilish
                        </Button>
                      )}
                    </div>
                  </TD>
                )}
              </TRow>
            );
          })}
        </TBody>
      </Table>
    </div>
  );
}

function RefundsTable({ refunds }: { refunds: Refund[] }) {
  return (
    <div className="overflow-x-auto">
      <Table>
        <THead>
          <tr>
            <TH>Sana</TH>
            <TH>Sabab</TH>
            <TH>Tasdiqladi</TH>
            <TH className="text-right">Summa</TH>
          </tr>
        </THead>
        <TBody>
          {refunds.map((refund) => (
            <TRow key={refund.id}>
              <TD className="font-tabular text-[13px]">{formatDateTime(refund.refundedAt)}</TD>
              <TD className="text-ink-700">{refund.reason}</TD>
              <TD className="text-ink-600">{refund.approvedByName ?? "—"}</TD>
              <TD className="text-right font-tabular font-medium text-warning-600">−{formatMoney(refund.amount)}</TD>
            </TRow>
          ))}
        </TBody>
      </Table>
    </div>
  );
}

// ============================================================================================ panels

/** Invoices with the actions the signed-in role may take (pay, discount, revoke discount). */
export function InvoicesPanel({ invoices, showStudent = false }: { invoices: Invoice[]; showStudent?: boolean }) {
  const manage = canTakePayments(useAuthSession().user?.role);
  const canSplit = usePermissions().can("payments.edit");
  const [payTarget, setPayTarget] = useState<Invoice | null>(null);
  const [splitTarget, setSplitTarget] = useState<Invoice | null>(null);
  const [discountTarget, setDiscountTarget] = useState<Invoice | null>(null);
  const [revokeTarget, setRevokeTarget] = useState<Invoice | null>(null);

  return (
    <>
      <InvoicesTable
        invoices={invoices}
        showStudent={showStudent}
        onPay={manage ? setPayTarget : undefined}
        onDiscount={manage ? setDiscountTarget : undefined}
        onRevokeDiscount={manage ? setRevokeTarget : undefined}
        onSplit={canSplit ? setSplitTarget : undefined}
      />
      {payTarget && (
        <RecordPaymentDrawer
          key={payTarget.id}
          studentId={payTarget.studentId}
          studentName={payTarget.studentName}
          initialInvoiceId={payTarget.id}
          onClose={() => setPayTarget(null)}
        />
      )}
      {discountTarget && <DiscountDrawer key={discountTarget.id} invoice={discountTarget} onClose={() => setDiscountTarget(null)} />}
      {splitTarget && <SplitInvoiceDrawer key={splitTarget.id} invoice={splitTarget} onClose={() => setSplitTarget(null)} />}
      {revokeTarget?.discount && <RevokeDiscountDrawer key={revokeTarget.id} invoice={revokeTarget} onClose={() => setRevokeTarget(null)} />}
    </>
  );
}

/** Payments with refund / void for managers. */
export function PaymentsPanel({ payments, showStudent = false }: { payments: Payment[]; showStudent?: boolean }) {
  const manage = canTakePayments(useAuthSession().user?.role);
  const [voidTarget, setVoidTarget] = useState<Payment | null>(null);
  const [refundTarget, setRefundTarget] = useState<Payment | null>(null);

  return (
    <>
      <PaymentsTable
        payments={payments}
        showStudent={showStudent}
        onVoid={manage ? setVoidTarget : undefined}
        onRefund={manage ? setRefundTarget : undefined}
      />
      {voidTarget && <VoidPaymentDrawer key={voidTarget.id} payment={voidTarget} onClose={() => setVoidTarget(null)} />}
      {refundTarget && <RefundDrawer key={refundTarget.id} payment={refundTarget} onClose={() => setRefundTarget(null)} />}
    </>
  );
}

// ============================================================================================ drawers

/** Takes a payment for one of the student's open invoices; the oldest one is preselected unless a specific
 * invoice was clicked. Leaving the amount empty takes the full remaining balance. */
export function RecordPaymentDrawer({
  studentId,
  studentName,
  initialInvoiceId,
  onClose,
}: {
  studentId: string;
  studentName: string;
  initialInvoiceId?: string;
  onClose: () => void;
}) {
  const invalidate = useInvalidateFinance();
  // One student's invoices — a dozen or two, so a single generous page rather than a pager.
  const invoicesQuery = useQuery({ queryKey: ["finance", "invoices", { studentId }], queryFn: () => listInvoices({ studentId, size: 100 }).then((page) => page.items) });
  const open = (invoicesQuery.data ?? []).filter(isOpenInvoice).sort((a, b) => a.dueDate.localeCompare(b.dueDate));

  const [invoiceId, setInvoiceId] = useState(initialInvoiceId ?? "");
  const [amount, setAmount] = useState("");
  const [method, setMethod] = useState<PaymentMethod>("Cash");
  const [note, setNote] = useState("");

  const selected = open.find((i) => i.id === invoiceId) ?? open[0];
  const parsed = amount.trim() === "" ? (selected?.balance ?? 0) : parseAmount(amount);
  const tooMuch = selected ? parsed > selected.balance : false;

  const mutation = useMutation({
    mutationFn: () => recordPayment(selected!.id, { amount: parsed, method, note: note.trim() || undefined }),
    onSuccess: async () => {
      await invalidate();
      onClose();
    },
  });

  return (
    <Drawer open onClose={onClose} title="To'lov qabul qilish" description={studentName}>
      {invoicesQuery.isLoading && <Skeleton className="h-40" />}
      {invoicesQuery.isError && <Alert>{errorText(invoicesQuery.error, "Hisob-fakturalarni yuklab bo'lmadi.")}</Alert>}
      {invoicesQuery.data && open.length === 0 && <EmptyState icon={Receipt} title="To'lanmagan hisob-faktura yo'q" />}

      {selected && (
        <form
          className="space-y-4"
          onSubmit={(e) => {
            e.preventDefault();
            mutation.mutate();
          }}
        >
          <div>
            <Label htmlFor="pay-invoice">Hisob-faktura</Label>
            <Select id="pay-invoice" value={selected.id} onChange={(e) => setInvoiceId(e.target.value)}>
              {open.map((i) => (
                <option key={i.id} value={i.id}>
                  {i.groupName} · {formatDay(i.dueDate)} · qoldiq {formatMoney(i.balance, i.currency)}
                </option>
              ))}
            </Select>
          </div>

          <InvoiceFigures invoice={selected} />

          <div>
            <Label htmlFor="pay-amount">Summa (so&apos;m)</Label>
            <div className="flex gap-2">
              <Input id="pay-amount" inputMode="numeric" value={amount} onChange={(e) => setAmount(e.target.value)} placeholder={String(selected.balance)} />
              <Button type="button" variant="secondary" onClick={() => setAmount(String(selected.balance))}>
                To&apos;liq
              </Button>
            </div>
            <p className="mt-1 text-[12px] text-ink-500">Bo&apos;sh qoldirilsa, to&apos;liq qoldiq qabul qilinadi.</p>
            {tooMuch && <p className="mt-1 text-[12.5px] text-danger-600">Summa qoldiqdan oshmasligi kerak.</p>}
          </div>

          <div>
            <Label htmlFor="pay-method">To&apos;lov usuli</Label>
            <Select id="pay-method" value={method} onChange={(e) => setMethod(e.target.value as PaymentMethod)}>
              {(Object.keys(PAYMENT_METHOD_LABEL) as PaymentMethod[]).map((m) => (
                <option key={m} value={m}>{PAYMENT_METHOD_LABEL[m]}</option>
              ))}
            </Select>
          </div>

          <div>
            <Label htmlFor="pay-note">Izoh (ixtiyoriy)</Label>
            <Input id="pay-note" maxLength={500} value={note} onChange={(e) => setNote(e.target.value)} placeholder="Chek raqami, terminal..." />
          </div>

          {mutation.isError && <Alert>{errorText(mutation.error, "To'lovni saqlab bo'lmadi.")}</Alert>}

          <Button type="submit" loading={mutation.isPending} disabled={!(parsed > 0) || tooMuch} className="w-full">
            {formatMoney(parsed > 0 ? parsed : 0)} qabul qilish
          </Button>
        </form>
      )}
    </Drawer>
  );
}

function InvoiceFigures({ invoice }: { invoice: Invoice }) {
  return (
    <dl className="grid grid-cols-3 gap-2 rounded-lg bg-ink-50 px-3 py-2.5 text-[12.5px]">
      <div>
        <dt className="text-ink-500">Summa</dt>
        <dd className="font-tabular font-medium text-ink-900">{formatMoney(invoice.netAmount, invoice.currency)}</dd>
      </div>
      <div>
        <dt className="text-ink-500">To&apos;langan</dt>
        <dd className="font-tabular font-medium text-ink-900">{formatMoney(invoice.paidAmount, invoice.currency)}</dd>
      </div>
      <div>
        <dt className="text-ink-500">Qoldiq</dt>
        <dd className={cn("font-tabular font-medium", invoice.isOverdue ? "text-danger-600" : "text-ink-900")}>
          {formatMoney(invoice.balance, invoice.currency)}
        </dd>
      </div>
    </dl>
  );
}

export function VoidPaymentDrawer({ payment, onClose }: { payment: Payment; onClose: () => void }) {
  const invalidate = useInvalidateFinance();
  const [reason, setReason] = useState("");
  const mutation = useMutation({
    mutationFn: () => voidPayment(payment.id, reason.trim()),
    onSuccess: async () => {
      await invalidate();
      onClose();
    },
  });

  return (
    <Drawer open onClose={onClose} title="To'lovni bekor qilish" description={`${formatMoney(payment.amount)} · ${payment.studentName}`}>
      <form
        className="space-y-4"
        onSubmit={(e) => {
          e.preventDefault();
          mutation.mutate();
        }}
      >
        <p className="rounded-lg bg-ink-50 px-3 py-2.5 text-[13px] text-ink-700">
          Faqat xato kiritilgan to&apos;lov uchun. To&apos;lov o&apos;chirilmaydi: bekor qilingan deb belgilanadi, hisob-faktura qoldig&apos;i qayta
          ochiladi va amal audit jurnalida saqlanadi. Studentga pul qaytarilgan bo&apos;lsa, «Refund»dan foydalaning.
        </p>
        <div>
          <Label htmlFor="void-reason">Sabab</Label>
          <Textarea id="void-reason" required rows={3} maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} placeholder="Masalan: summa noto'g'ri kiritildi" />
        </div>
        {mutation.isError && <Alert>{errorText(mutation.error, "Bekor qilib bo'lmadi.")}</Alert>}
        <Button type="submit" variant="secondary" loading={mutation.isPending} disabled={!reason.trim()} className="w-full">
          To&apos;lovni bekor qilish
        </Button>
      </form>
    </Drawer>
  );
}

type DiscountMode = DiscountType | "Promo";

/** `yyyy-MM-dd` moved by whole days, on the calendar and not the clock (no time zone can shift it). */
function addDays(isoDate: string, days: number): string {
  const [y, m, d] = isoDate.split("-").map(Number);
  return new Date(Date.UTC(y, m - 1, d + days)).toISOString().slice(0, 10);
}

/** The net split into `count` whole-so'm parts, the last one taking whatever the rounding left. */
function evenParts(net: number, count: number): number[] {
  const each = Math.floor(net / count);
  return Array.from({ length: count }, (_, i) => (i === count - 1 ? net - each * (count - 1) : each));
}

/**
 * Cuts an unpaid invoice into two to six instalments (docs/21.09-audit-jurnali S07.05): each with an amount and a due date, together
 * exactly what the student owes. The parts are then paid, reminded about and overdue on their own; the invoice itself stops counting.
 */
export function SplitInvoiceDrawer({ invoice, onClose }: { invoice: Invoice; onClose: () => void }) {
  const invalidate = useInvalidateFinance();
  const net = invoice.netAmount;
  const [count, setCount] = useState(2);
  const [amounts, setAmounts] = useState<string[]>(() => evenParts(net, 2).map(String));
  // A month apart from the invoice's own due date - the usual rhythm of a centre's instalments; every date is editable.
  const [dates, setDates] = useState<string[]>(() => [invoice.dueDate, addDays(invoice.dueDate, 30)]);

  const resize = (next: number) => {
    setCount(next);
    setAmounts(evenParts(net, next).map(String));
    setDates(Array.from({ length: next }, (_, i) => addDays(invoice.dueDate, 30 * i)));
  };

  const numbers = amounts.map(parseAmount);
  const total = numbers.reduce((sum, n) => sum + (Number.isFinite(n) ? n : 0), 0);
  const difference = Math.round((net - total) * 100) / 100;
  const rising = dates.every((d, i) => d !== "" && (i === 0 || d > dates[i - 1]));
  const problem =
    numbers.some((n) => !Number.isFinite(n) || n <= 0) ? "Har bir qism summasi musbat bo‘lsin."
    : difference !== 0 ? `Qismlar yig‘indisi ${formatMoney(net, invoice.currency)} bo‘lishi kerak (hozir ${difference > 0 ? `${formatMoney(difference, invoice.currency)} yetmayapti` : `${formatMoney(-difference, invoice.currency)} ortiqcha`}).`
    : !rising ? "Muddatlar ketma-ket o‘sib borishi kerak."
    : dates[0] < invoice.periodStart ? "Birinchi muddat davr boshlanishidan oldin bo‘lmasin."
    : null;

  const mutation = useMutation({
    mutationFn: () => splitInvoice(invoice.id, numbers.map((amount, i) => ({ amount, dueDate: dates[i] }))),
    onSuccess: async () => {
      await invalidate();
      onClose();
    },
  });

  return (
    <Drawer open onClose={onClose} title="To‘lovni qismlarga bo‘lish" description={`${invoice.studentName} · ${invoice.groupName}`}>
      <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); if (!problem) mutation.mutate(); }}>
        <InvoiceFigures invoice={invoice} />

        <div>
          <Label htmlFor="split-count">Qismlar soni</Label>
          <Select id="split-count" value={count} onChange={(e) => resize(Number(e.target.value))}>
            {Array.from({ length: MAX_INSTALLMENTS - 1 }, (_, i) => i + 2).map((n) => <option key={n} value={n}>{n} qism</option>)}
          </Select>
        </div>

        <div className="space-y-2.5">
          {amounts.map((amount, i) => (
            <div key={i} className="grid grid-cols-[2.5rem_1fr_1fr] items-end gap-2">
              <span className="pb-2 text-center font-tabular text-[13px] font-medium text-ink-500">{i + 1}/{count}</span>
              <div>
                {i === 0 && <Label htmlFor="split-amount-0">Summa</Label>}
                <Input
                  id={`split-amount-${i}`} inputMode="decimal" aria-label={`${i + 1}-qism summasi`} value={amount}
                  onChange={(e) => setAmounts(amounts.map((a, j) => (j === i ? e.target.value : a)))}
                />
              </div>
              <div>
                {i === 0 && <Label htmlFor="split-date-0">Muddat</Label>}
                <Input
                  id={`split-date-${i}`} type="date" aria-label={`${i + 1}-qism muddati`} value={dates[i]}
                  onChange={(e) => setDates(dates.map((d, j) => (j === i ? e.target.value : d)))}
                />
              </div>
            </div>
          ))}
        </div>

        <div className="flex items-center justify-between text-[13px]">
          <span className="text-ink-500">Yig‘indi <span className={cn("font-tabular font-medium", difference === 0 ? "text-success-600" : "text-danger-600")}>{formatMoney(total, invoice.currency)}</span> / {formatMoney(net, invoice.currency)}</span>
          <Button type="button" size="sm" variant="ghost" onClick={() => setAmounts(evenParts(net, count).map(String))}>Teng bo‘lish</Button>
        </div>

        {problem && <p className="text-[13px] text-danger-600">{problem}</p>}
        {mutation.isError && <Alert>{errorText(mutation.error, "Bo‘lib bo‘lmadi.")}</Alert>}
        <p className="text-[12.5px] text-ink-500">Bo‘lgandan keyin har bir qism alohida to‘lanadi va o‘z muddatida eslatma oladi. Bo‘lingan hisob-fakturaning o‘zi qarzga qo‘shilmaydi.</p>

        <Button type="submit" loading={mutation.isPending} disabled={problem !== null} className="w-full">
          {count} qismga bo‘lish
        </Button>
      </form>
    </Drawer>
  );
}

export function DiscountDrawer({ invoice, onClose }: { invoice: Invoice; onClose: () => void }) {
  const invalidate = useInvalidateFinance();
  const limitsQuery = useQuery({ queryKey: ["finance", "limits"], queryFn: getFinanceLimits });

  const [mode, setMode] = useState<DiscountMode>("Percent");
  const [value, setValue] = useState("");
  const [code, setCode] = useState("");
  const [reason, setReason] = useState("");

  const numeric = parseAmount(value);
  const amount = mode === "Percent" ? Math.round((invoice.amount * numeric) / 100) : mode === "Fixed" ? numeric : 0;
  const cap = limitsQuery.data?.maxDiscountPercent ?? null;
  const percentOfInvoice = invoice.amount > 0 ? (amount / invoice.amount) * 100 : 0;
  const manual = mode !== "Promo";
  const overCap = manual && cap !== null && percentOfInvoice > cap;
  const overBalance = manual && amount > invoice.balance;
  const badPercent = mode === "Percent" && (numeric <= 0 || numeric > 100);
  const valid = manual
    ? Number.isFinite(numeric) && amount > 0 && !badPercent && !overBalance && !overCap && reason.trim().length > 0
    : code.trim().length > 0;

  const mutation = useMutation({
    mutationFn: () =>
      mode === "Promo"
        ? applyPromoCode(invoice.id, code.trim())
        : applyDiscount(invoice.id, { type: mode, value: numeric, reason: reason.trim() }),
    onSuccess: async () => {
      await invalidate();
      onClose();
    },
  });

  const modes: { id: DiscountMode; label: string }[] = [
    { id: "Percent", label: "Foiz" },
    { id: "Fixed", label: "Summa" },
    { id: "Promo", label: "Promo-kod" },
  ];

  return (
    <Drawer open onClose={onClose} title="Chegirma berish" description={`${invoice.studentName} · ${invoice.groupName}`}>
      <form
        className="space-y-4"
        onSubmit={(e) => {
          e.preventDefault();
          mutation.mutate();
        }}
      >
        <InvoiceFigures invoice={invoice} />

        <div className="grid grid-cols-3 gap-1.5" role="radiogroup" aria-label="Chegirma turi">
          {modes.map((m) => (
            <Button
              key={m.id}
              type="button"
              size="sm"
              variant={mode === m.id ? "primary" : "secondary"}
              aria-pressed={mode === m.id}
              onClick={() => setMode(m.id)}
            >
              {m.label}
            </Button>
          ))}
        </div>

        {manual ? (
          <>
            <div>
              <Label htmlFor="discount-value">{mode === "Percent" ? "Foiz (%)" : "Summa (so'm)"}</Label>
              <Input id="discount-value" inputMode="decimal" required value={value} onChange={(e) => setValue(e.target.value)} placeholder={mode === "Percent" ? "10" : "100000"} />
              {amount > 0 && !badPercent && (
                <p className="mt-1 text-[12.5px] text-ink-600">
                  Chegirma: <span className="font-tabular font-medium">{formatMoney(amount, invoice.currency)}</span> · yangi qoldiq:{" "}
                  <span className="font-tabular font-medium">{formatMoney(Math.max(invoice.balance - amount, 0), invoice.currency)}</span>
                </p>
              )}
              {cap !== null && (
                <p className={cn("mt-1 text-[12px]", overCap ? "text-danger-600" : "text-ink-500")}>
                  Filial darajasida {cap}% gacha chegirma berish mumkin{overCap ? " — kattaroq chegirmani Super Admin beradi." : "."}
                </p>
              )}
              {overBalance && <p className="mt-1 text-[12.5px] text-danger-600">Chegirma qoldiqdan oshmasligi kerak.</p>}
            </div>
            <div>
              <Label htmlFor="discount-reason">Sabab</Label>
              <Textarea id="discount-reason" required rows={3} maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} placeholder="Masalan: aka-uka chegirmasi, a'lo baho" />
            </div>
          </>
        ) : (
          <div>
            <Label htmlFor="discount-code">Promo-kod</Label>
            <Input id="discount-code" required className="font-mono uppercase" value={code} onChange={(e) => setCode(e.target.value)} placeholder="WELCOME10" />
            <p className="mt-1 text-[12px] text-ink-500">Promo-kod oldindan tasdiqlangan — chegirma summasi va shartlari koddan olinadi.</p>
          </div>
        )}

        {mutation.isError && <Alert>{errorText(mutation.error, "Chegirmani saqlab bo'lmadi.")}</Alert>}
        <Button type="submit" loading={mutation.isPending} disabled={!valid} className="w-full">
          Chegirmani qo&apos;llash
        </Button>
      </form>
    </Drawer>
  );
}

export function RevokeDiscountDrawer({ invoice, onClose }: { invoice: Invoice; onClose: () => void }) {
  const invalidate = useInvalidateFinance();
  const discount = invoice.discount!;
  const [reason, setReason] = useState("");
  const mutation = useMutation({
    mutationFn: () => revokeDiscount(discount.id, reason.trim()),
    onSuccess: async () => {
      await invalidate();
      onClose();
    },
  });

  return (
    <Drawer open onClose={onClose} title="Chegirmani olib tashlash" description={`${invoice.studentName} · ${invoice.groupName}`}>
      <form
        className="space-y-4"
        onSubmit={(e) => {
          e.preventDefault();
          mutation.mutate();
        }}
      >
        <dl className="space-y-1 rounded-lg bg-ink-50 px-3 py-2.5 text-[13px]">
          <div className="flex justify-between gap-3">
            <dt className="text-ink-500">Chegirma</dt>
            <dd className="font-tabular font-medium text-ink-900">
              {formatDiscountValue(discount.type, discount.value)} · {formatMoney(discount.amount)}
            </dd>
          </div>
          <div className="flex justify-between gap-3">
            <dt className="text-ink-500">Sabab</dt>
            <dd className="text-right text-ink-800">{discount.reason}</dd>
          </div>
          {discount.appliedByName && (
            <div className="flex justify-between gap-3">
              <dt className="text-ink-500">Bergan</dt>
              <dd className="text-ink-800">{discount.appliedByName}</dd>
            </div>
          )}
        </dl>
        <p className="text-[12.5px] text-ink-600">Qoldiq chegirma summasiga oshadi{discount.promoCode ? ", promo-kodning foydalanishi qaytariladi" : ""}.</p>
        <div>
          <Label htmlFor="revoke-reason">Sabab</Label>
          <Textarea id="revoke-reason" required rows={3} maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} />
        </div>
        {mutation.isError && <Alert>{errorText(mutation.error, "Chegirmani olib tashlab bo'lmadi.")}</Alert>}
        <Button type="submit" variant="secondary" loading={mutation.isPending} disabled={!reason.trim()} className="w-full">
          Chegirmani olib tashlash
        </Button>
      </form>
    </Drawer>
  );
}

export function RefundDrawer({ payment, onClose }: { payment: Payment; onClose: () => void }) {
  const invalidate = useInvalidateFinance();
  const limitsQuery = useQuery({ queryKey: ["finance", "limits"], queryFn: getFinanceLimits });
  const refundable = payment.amount - payment.refundedAmount;

  const [amount, setAmount] = useState("");
  const [reason, setReason] = useState("");
  const parsed = amount.trim() === "" ? refundable : parseAmount(amount);
  const cap = limitsQuery.data?.maxRefundAmount ?? null;
  const overCap = cap !== null && parsed > cap;
  const tooMuch = parsed > refundable;

  const mutation = useMutation({
    mutationFn: () => refundPayment(payment.id, { amount: parsed, reason: reason.trim() }),
    onSuccess: async () => {
      await invalidate();
      onClose();
    },
  });

  return (
    <Drawer open onClose={onClose} title="Pulni qaytarish (refund)" description={`${formatMoney(payment.amount)} · ${payment.studentName}`}>
      <form
        className="space-y-4"
        onSubmit={(e) => {
          e.preventDefault();
          mutation.mutate();
        }}
      >
        <p className="rounded-lg bg-ink-50 px-3 py-2.5 text-[13px] text-ink-700">
          Pul studentga qaytariladi va hisob-fakturadagi majburiyat shu summaga kamayadi — qarz paydo bo&apos;lmaydi.
          Xato kiritilgan to&apos;lov uchun «Bekor qilish»dan foydalaning.
        </p>
        <div>
          <Label htmlFor="refund-amount">Summa (so&apos;m)</Label>
          <Input id="refund-amount" inputMode="numeric" value={amount} onChange={(e) => setAmount(e.target.value)} placeholder={String(refundable)} />
          <p className="mt-1 text-[12px] text-ink-500">Qaytarish mumkin: {formatMoney(refundable)}. Bo&apos;sh qoldirilsa — to&apos;liq.</p>
          {tooMuch && <p className="mt-1 text-[12.5px] text-danger-600">Summa qaytarish mumkin bo&apos;lgan summadan oshmasligi kerak.</p>}
          {cap !== null && (
            <p className={cn("mt-1 text-[12px]", overCap ? "text-danger-600" : "text-ink-500")}>
              Filial darajasida bir martada {formatMoney(cap)} gacha{overCap ? " — kattaroq refundni Super Admin tasdiqlaydi." : "."}
            </p>
          )}
        </div>
        <div>
          <Label htmlFor="refund-reason">Sabab</Label>
          <Textarea id="refund-reason" required rows={3} maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} placeholder="Masalan: kursni tark etdi, 2 hafta qatnashmadi" />
        </div>
        {mutation.isError && <Alert>{errorText(mutation.error, "Refundni saqlab bo'lmadi.")}</Alert>}
        <Button type="submit" loading={mutation.isPending} disabled={!(parsed > 0) || tooMuch || overCap || !reason.trim()} className="w-full">
          {formatMoney(parsed > 0 ? parsed : 0)} qaytarish
        </Button>
      </form>
    </Drawer>
  );
}

export function AdvanceInvoicesDrawer({
  studentId,
  studentName,
  groups,
  onClose,
}: {
  studentId: string;
  studentName: string;
  groups: { groupId: string; groupName: string }[];
  onClose: () => void;
}) {
  const invalidate = useInvalidateFinance();
  const [groupId, setGroupId] = useState(groups.length === 1 ? groups[0].groupId : "");
  const [months, setMonths] = useState(3);
  const mutation = useMutation({
    mutationFn: () => issueAdvanceInvoices(studentId, { groupId, months }),
    onSuccess: () => invalidate(),
  });

  return (
    <Drawer open onClose={onClose} title="Oldindan hisob-faktura" description={studentName}>
      <form
        className="space-y-4"
        onSubmit={(e) => {
          e.preventDefault();
          mutation.mutate();
        }}
      >
        <p className="rounded-lg bg-ink-50 px-3 py-2.5 text-[13px] text-ink-700">
          Student bir necha oy uchun oldindan to&apos;lamoqchi bo&apos;lsa, keyingi oylarning hisob-fakturalari hozir chiqariladi va odatdagidek
          to&apos;lanadi. Hozirgi davrdan eng ko&apos;pi 12 oy oldinga.
        </p>
        <div>
          <Label htmlFor="advance-group">Guruh</Label>
          <Select id="advance-group" required value={groupId} onChange={(e) => setGroupId(e.target.value)}>
            <option value="">Tanlang</option>
            {groups.map((g) => (
              <option key={g.groupId} value={g.groupId}>{g.groupName}</option>
            ))}
          </Select>
        </div>
        <div>
          <Label htmlFor="advance-months">Necha oy</Label>
          <Select id="advance-months" value={months} onChange={(e) => setMonths(Number(e.target.value))}>
            {[1, 2, 3, 4, 5, 6, 12].map((m) => (
              <option key={m} value={m}>{m} oy</option>
            ))}
          </Select>
        </div>
        {mutation.isError && <Alert>{errorText(mutation.error, "Hisob-fakturalarni chiqarib bo'lmadi.")}</Alert>}
        {mutation.data && (
          <p role="status" className="rounded-lg bg-brand-50 px-3 py-2.5 text-[13px] text-brand-800">
            {mutation.data.invoicesIssued > 0
              ? `${mutation.data.invoicesIssued} ta hisob-faktura chiqarildi.`
              : "Yangi hisob-faktura chiqmadi — 12 oylik chegaraga yetilgan."}
          </p>
        )}
        <div className="flex gap-2">
          <Button type="submit" loading={mutation.isPending} disabled={!groupId} className="flex-1">
            Chiqarish
          </Button>
          {mutation.data && (
            <Button type="button" variant="secondary" onClick={onClose}>
              Yopish
            </Button>
          )}
        </div>
      </form>
    </Drawer>
  );
}

// ============================================================================================ student 360

/** Student 360° "To'lovlar" tab. Managers, the student and their parents get the full account (payments.view);
 * an operator gets the invoices/debt side only (finance.debt.view). */
export function StudentFinanceTab({
  studentId,
  studentName,
  groups,
}: {
  studentId: string;
  studentName: string;
  groups: { groupId: string; groupName: string }[];
}) {
  const role = useAuthSession().user?.role;
  const full = canSeePayments(role);
  const manage = canTakePayments(role);

  const financeQuery = useQuery({ queryKey: ["finance", "student", studentId], queryFn: () => getStudentFinance(studentId), enabled: full });
  const invoicesQuery = useQuery({
    queryKey: ["finance", "invoices", { studentId }],
    queryFn: () => listInvoices({ studentId, size: 100 }).then((page) => page.items),
    enabled: !full && role === "OPERATOR",
  });

  const [paying, setPaying] = useState(false);
  const [advancing, setAdvancing] = useState(false);

  const query = full ? financeQuery : invoicesQuery;
  const invoices = full ? financeQuery.data?.invoices : invoicesQuery.data;
  const open = (invoices ?? []).filter(isOpenInvoice);
  const overdueAmount = open.filter((i) => i.isOverdue).reduce((sum, i) => sum + i.balance, 0);
  const totalBalance = open.reduce((sum, i) => sum + i.balance, 0);
  const nextDue = open.filter((i) => !i.isOverdue).sort((a, b) => a.dueDate.localeCompare(b.dueDate))[0]?.dueDate ?? null;

  return (
    <div className="space-y-5">
      {query.isLoading && <Skeleton className="h-40" />}
      {query.isError && <Alert>{errorText(query.error, "To'lov ma'lumotlarini yuklab bo'lmadi.")}</Alert>}

      {invoices && (
        <>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <StatTile label="Muddati o'tgan qarz" value={formatMoney(overdueAmount)} icon={AlertOctagon} tone={overdueAmount > 0 ? "danger" : "neutral"} />
            <StatTile label="Jami qoldiq" value={formatMoney(totalBalance)} icon={Wallet} tone={totalBalance > 0 ? "warning" : "neutral"} />
            <StatTile label="Keyingi to'lov" value={nextDue ? formatDay(nextDue) : "—"} icon={CalendarClock} tone="brand" />
          </div>

          {manage && (
            <div className="flex flex-wrap justify-end gap-2">
              {groups.length > 0 && (
                <Button size="sm" variant="secondary" onClick={() => setAdvancing(true)}>
                  <CalendarPlus className="size-3.5" aria-hidden />
                  Oldindan hisob-faktura
                </Button>
              )}
              {open.length > 0 && (
                <Button size="sm" onClick={() => setPaying(true)}>
                  <Wallet className="size-3.5" aria-hidden />
                  To&apos;lov qabul qilish
                </Button>
              )}
            </div>
          )}

          <section>
            <h3 className="mb-2.5 text-[13px] font-semibold uppercase tracking-wide text-ink-500">Hisob-fakturalar</h3>
            <InvoicesPanel invoices={invoices} />
          </section>

          {full && financeQuery.data && (
            <section>
              <h3 className="mb-2.5 text-[13px] font-semibold uppercase tracking-wide text-ink-500">To&apos;lovlar tarixi</h3>
              <PaymentsPanel payments={financeQuery.data.payments} />
            </section>
          )}

          {full && financeQuery.data && financeQuery.data.refunds.length > 0 && (
            <section>
              <h3 className="mb-2.5 text-[13px] font-semibold uppercase tracking-wide text-ink-500">Qaytarilgan pullar</h3>
              <RefundsTable refunds={financeQuery.data.refunds} />
            </section>
          )}
        </>
      )}

      {paying && <RecordPaymentDrawer studentId={studentId} studentName={studentName} onClose={() => setPaying(false)} />}
      {advancing && <AdvanceInvoicesDrawer studentId={studentId} studentName={studentName} groups={groups} onClose={() => setAdvancing(false)} />}
    </div>
  );
}
