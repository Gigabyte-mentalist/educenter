import { apiFetch } from "../api-client";
import { getDefaultCurrency } from "../currency";
import { formatShortDate } from "../format-date";
import type { PageEnvelope } from "@/components/ui/Pagination";
import { buildQuery, DEFAULT_PAGE_SIZE, type PageParams } from "./query";

/** "Split": cut into instalments — the parts are what is owed, this only records what they add up to (it takes no payment and counts in no total). */
export type InvoiceStatus = "Pending" | "PartiallyPaid" | "Paid" | "Cancelled" | "Split";
export type PaymentMethod = "Cash" | "Card" | "BankTransfer" | "Click" | "Payme" | "Other";
export type DiscountType = "Percent" | "Fixed";

export interface InvoiceDiscount {
  id: string;
  type: DiscountType;
  value: number;
  amount: number;
  reason: string;
  promoCode: string | null;
  appliedByName: string | null;
  createdAt: string;
}

export interface Invoice {
  id: string;
  studentId: string;
  studentName: string;
  studentNumber: string;
  groupId: string;
  groupName: string;
  branchId: string;
  enrollmentId: string;
  description: string;
  /** ISO date (yyyy-mm-dd), inclusive. */
  periodStart: string;
  /** ISO date, exclusive — the next period's start. */
  periodEnd: string;
  dueDate: string;
  amount: number;
  discountAmount: number;
  /** Obligation removed by refunds — money returned for service not received. */
  creditedAmount: number;
  netAmount: number;
  paidAmount: number;
  balance: number;
  currency: string;
  status: InvoiceStatus;
  isOverdue: boolean;
  daysOverdue: number;
  cancelReason: string | null;
  discount: InvoiceDiscount | null;
  createdAt: string;
  /** Set on an instalment: the invoice it was cut from. */
  parentInvoiceId?: string | null;
  /** "2" of "2/3". */
  installmentNumber?: number | null;
  installmentCount?: number | null;
}

export interface Payment {
  id: string;
  invoiceId: string;
  studentId: string;
  studentName: string;
  invoiceDescription: string;
  amount: number;
  refundedAmount: number;
  method: PaymentMethod;
  paidAt: string;
  receivedBy: string;
  receivedByName: string | null;
  note: string | null;
  isVoided: boolean;
  voidedAt: string | null;
  voidReason: string | null;
}

export interface Refund {
  id: string;
  paymentId: string;
  invoiceId: string;
  studentId: string;
  studentName: string;
  amount: number;
  reason: string;
  approvedByName: string | null;
  refundedAt: string;
}

export interface Debtor {
  studentId: string;
  studentName: string;
  studentNumber: string;
  phone: string | null;
  totalDebt: number;
  overdueInvoices: number;
  oldestDueDate: string;
  daysOverdue: number;
  groups: string[];
}

export interface StudentFinance {
  totalBalance: number;
  overdueAmount: number;
  nextDueDate: string | null;
  invoices: Invoice[];
  payments: Payment[];
  refunds: Refund[];
}

export interface BillingResult {
  invoicesIssued: number;
  studentsMarkedDebt: number;
  studentsCleared: number;
  debtTasks: number;
}

/** The caller's own limits; null means "not capped" (organization-wide permission). */
export interface FinanceLimits {
  maxDiscountPercent: number | null;
  maxRefundAmount: number | null;
}

export interface PromoCode {
  id: string;
  code: string;
  description: string | null;
  type: DiscountType;
  value: number;
  validFrom: string | null;
  validUntil: string | null;
  maxUses: number | null;
  usedCount: number;
  courseId: string | null;
  courseName: string | null;
  isActive: boolean;
  createdAt: string;
}

export interface FinanceSummary {
  from: string;
  to: string;
  collected: number;
  refunded: number;
  netCollected: number;
  billed: number;
  discounts: number;
  credited: number;
  expectedRevenue: number;
  paidOnBilled: number;
  outstandingOnBilled: number;
  overdueNow: number;
  debtorsNow: number;
  byMethod: { method: PaymentMethod; amount: number; count: number }[];
  byBranch: {
    branchId: string;
    branchName: string;
    collected: number;
    refunded: number;
    netCollected: number;
    expectedRevenue: number;
    discounts: number;
    overdueNow: number;
    debtorsNow: number;
  }[];
}

export const PAYMENT_METHOD_LABEL: Record<PaymentMethod, string> = {
  Cash: "Naqd",
  Card: "Karta",
  BankTransfer: "Bank o'tkazmasi",
  Click: "Click",
  Payme: "Payme",
  Other: "Boshqa",
};

// The ICU data for uz-UZ groups with commas — "3,975,000" — which reads as a decimal point to anyone here.
// Uzbek groups with a space, so the separator is applied rather than asked for.
const moneyFormat = new Intl.NumberFormat("uz-UZ", { maximumFractionDigits: 0, useGrouping: false });

/** `currency` defaults to the organization's (S07.01, `lib/currency.ts`); an invoice passes its own. */
export function formatMoney(value: number, currency = getDefaultCurrency()): string {
  const amount = moneyFormat.format(value).replace(/\B(?=(\d{3})+(?!\d))/g, " ");
  return currency === "UZS" ? `${amount} so'm` : `${amount} ${currency}`;
}

export function formatDiscountValue(type: DiscountType, value: number): string {
  return type === "Percent" ? `${value}%` : formatMoney(value);
}

/** Formats an ISO calendar date (no time zone shift), optionally moved by whole days. */
export function formatDay(isoDate: string, offsetDays = 0): string {
  const [y, m, d] = isoDate.split("-").map(Number);
  const date = new Date(y, m - 1, d + offsetDays);
  return formatShortDate(date);
}

export function isOpenInvoice(invoice: Invoice): boolean {
  return invoice.status === "Pending" || invoice.status === "PartiallyPaid";
}

/** An unpaid invoice that was not itself cut from another may be cut into instalments. */
export function canSplitInvoice(invoice: Invoice): boolean {
  return invoice.status === "Pending" && invoice.paidAmount === 0 && invoice.netAmount > 0 && !invoice.parentInvoiceId;
}

export const MAX_INSTALLMENTS = 6;

/** `parts` in order: each with its own amount and due date (yyyy-MM-dd); together exactly the invoice's net amount. */
export function splitInvoice(
  invoiceId: string, parts: { amount: number; dueDate: string }[],
): Promise<{ invoice: Invoice; parts: Invoice[] }> {
  return apiFetch(`/api/finance/invoices/${invoiceId}/split`, { method: "POST", body: JSON.stringify({ parts }) });
}

export interface ListInvoicesQuery extends PageParams {
  studentId?: string;
  status?: InvoiceStatus;
  /** Still to be paid: pending or part-paid. */
  open?: boolean;
  overdue?: boolean;
}

export function listInvoices(query: ListInvoicesQuery = {}): Promise<PageEnvelope<Invoice>> {
  return apiFetch<PageEnvelope<Invoice>>(`/api/finance/invoices${buildQuery({
    studentId: query.studentId, status: query.status, open: query.open, overdue: query.overdue, search: query.search?.trim(),
    page: query.page ?? 1, size: query.size ?? DEFAULT_PAGE_SIZE,
  })}`);
}

export function listDebtors(query: PageParams = {}): Promise<PageEnvelope<Debtor>> {
  return apiFetch<PageEnvelope<Debtor>>(`/api/finance/debtors${buildQuery({
    search: query.search?.trim(), page: query.page ?? 1, size: query.size ?? DEFAULT_PAGE_SIZE,
  })}`);
}

/** `id` asks for exactly one payment — what a Universal Search result opens, wherever it would have fallen in the paged list. */
export function listPayments(query: PageParams & { studentId?: string; id?: string } = {}): Promise<PageEnvelope<Payment>> {
  return apiFetch<PageEnvelope<Payment>>(`/api/finance/payments${buildQuery({
    studentId: query.studentId, id: query.id, search: query.search?.trim(), page: query.page ?? 1, size: query.size ?? DEFAULT_PAGE_SIZE,
  })}`);
}

/** The figures above the finance tables — summed in the database, not in the browser from the full lists. */
export interface FinanceOverview {
  overdueDebt: number;
  debtors: number;
  openInvoices: number;
  /** Null when the caller may see debt but not payments. */
  todayIncome: number | null;
}

export function getFinanceOverview(): Promise<FinanceOverview> {
  return apiFetch<FinanceOverview>("/api/finance/overview");
}

export function getStudentFinance(studentId: string): Promise<StudentFinance> {
  return apiFetch<StudentFinance>(`/api/students/${studentId}/finance`);
}

export function recordPayment(invoiceId: string, input: { amount: number; method: PaymentMethod; note?: string }): Promise<Payment> {
  return apiFetch<Payment>(`/api/finance/invoices/${invoiceId}/payments`, { method: "POST", body: JSON.stringify(input) });
}

export function voidPayment(paymentId: string, reason: string): Promise<void> {
  return apiFetch<void>(`/api/finance/payments/${paymentId}/void`, { method: "POST", body: JSON.stringify({ reason }) });
}

export function cancelInvoice(invoiceId: string, reason: string): Promise<void> {
  return apiFetch<void>(`/api/finance/invoices/${invoiceId}/cancel`, { method: "POST", body: JSON.stringify({ reason }) });
}

export function runBilling(): Promise<BillingResult> {
  return apiFetch<BillingResult>("/api/finance/billing/run", { method: "POST" });
}

export function issueAdvanceInvoices(studentId: string, input: { groupId: string; months: number }): Promise<{ invoicesIssued: number }> {
  return apiFetch<{ invoicesIssued: number }>(`/api/students/${studentId}/advance-invoices`, { method: "POST", body: JSON.stringify(input) });
}

export function getFinanceLimits(): Promise<FinanceLimits> {
  return apiFetch<FinanceLimits>("/api/finance/limits");
}

export function applyDiscount(invoiceId: string, input: { type: DiscountType; value: number; reason: string }): Promise<Invoice> {
  return apiFetch<Invoice>(`/api/finance/invoices/${invoiceId}/discounts`, { method: "POST", body: JSON.stringify(input) });
}

export function applyPromoCode(invoiceId: string, code: string): Promise<Invoice> {
  return apiFetch<Invoice>(`/api/finance/invoices/${invoiceId}/promo`, { method: "POST", body: JSON.stringify({ code }) });
}

export function revokeDiscount(discountId: string, reason: string): Promise<void> {
  return apiFetch<void>(`/api/finance/discounts/${discountId}/revoke`, { method: "POST", body: JSON.stringify({ reason }) });
}

export function refundPayment(paymentId: string, input: { amount: number; reason: string }): Promise<Refund> {
  return apiFetch<Refund>(`/api/finance/payments/${paymentId}/refunds`, { method: "POST", body: JSON.stringify(input) });
}

export function listPromoCodes(): Promise<PromoCode[]> {
  return apiFetch<PromoCode[]>("/api/finance/promo-codes");
}

export interface CreatePromoCodeInput {
  code: string;
  description?: string;
  type: DiscountType;
  value: number;
  validFrom?: string;
  validUntil?: string;
  maxUses?: number;
  courseId?: string;
}

export function createPromoCode(input: CreatePromoCodeInput): Promise<PromoCode> {
  return apiFetch<PromoCode>("/api/finance/promo-codes", { method: "POST", body: JSON.stringify(input) });
}

export function deactivatePromoCode(id: string): Promise<void> {
  return apiFetch<void>(`/api/finance/promo-codes/${id}/deactivate`, { method: "POST" });
}

export function getFinanceSummary(filter: { from: string; to: string; branchId?: string }): Promise<FinanceSummary> {
  const params = new URLSearchParams({ from: filter.from, to: filter.to });
  if (filter.branchId) params.set("branchId", filter.branchId);
  return apiFetch<FinanceSummary>(`/api/finance/reports/summary?${params.toString()}`);
}
