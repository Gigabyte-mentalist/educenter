"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { History, Play, RotateCcw, Workflow, Zap } from "lucide-react";
import {
  listAutomationRules, listAutomationRuns, resetAutomationRule, runAutomationNow, updateAutomationRule,
  type AutomationRule, type AutomationRunNowResult, type AutomationTrigger,
} from "@/lib/api/automation";
import { ApiError } from "@/lib/api-client";
import { useAuthSession } from "@/lib/use-auth-session";
import { PageHeader } from "@/components/ui/PageHeader";
import { Alert } from "@/components/ui/Alert";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { EmptyState } from "@/components/ui/EmptyState";
import { Input, Label } from "@/components/ui/Field";
import { Skeleton } from "@/components/ui/Skeleton";
import { Table, TBody, TD, TH, THead, TRow } from "@/components/ui/Table";
import { cn } from "@/lib/cn";
import { formatDateTime } from "@/lib/format-date";

const TRIGGER_LABEL: Record<AutomationTrigger, string> = {
  Sweep: "Rejali",
  Event: "Hodisa",
  Manual: "Qo'lda",
};

const errorText = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

export default function AutomationPage() {
  const client = useQueryClient();
  const canManage = useAuthSession().user?.role === "SUPER_ADMIN";

  const rulesQuery = useQuery({ queryKey: ["automation", "rules"], queryFn: listAutomationRules });
  const runsQuery = useQuery({ queryKey: ["automation", "runs"], queryFn: () => listAutomationRuns(50) });

  const runNow = useMutation({
    mutationFn: runAutomationNow,
    onSuccess: () => client.invalidateQueries({ queryKey: ["automation"] }),
  });

  const result = runNow.data;
  const resultText = result ? describeRun(result) : null;

  return (
    <main className="p-4 sm:p-6 lg:p-8">
      <PageHeader
        title="Avtomatlashtirish"
        description="Tizim o'zi bajaradigan qoidalar: hodisa → shart → amal. Chegaralar va amallar butun tashkilot uchun amal qiladi; qoidalar har 5 daqiqada va tegishli hodisa yuz berganda tekshiriladi."
        actions={
          canManage && (
            <Button variant="secondary" loading={runNow.isPending} onClick={() => runNow.mutate()}>
              <Play className="size-4" aria-hidden />
              Hozir ishga tushirish
            </Button>
          )
        }
      />

      {!canManage && (
        <p className="mb-4 rounded-lg bg-ink-50 px-3 py-2.5 text-[13px] text-ink-600">
          Siz qoidalarni ko&apos;ra olasiz. O&apos;zgartirish faqat Super Admin uchun.
        </p>
      )}
      {runNow.isError && <Alert>{errorText(runNow.error, "Qoidalarni ishga tushirib bo'lmadi.")}</Alert>}
      {resultText && (
        <p role="status" className="mb-4 rounded-lg bg-brand-50 px-3 py-2.5 text-[13px] text-brand-800">
          Tekshiruv yakunlandi: {resultText}.
        </p>
      )}
      {rulesQuery.isError && <Alert>{errorText(rulesQuery.error, "Qoidalarni yuklab bo'lmadi.")}</Alert>}

      {rulesQuery.isLoading && (
        <div className="grid grid-cols-1 gap-4 xl:grid-cols-2">
          {Array.from({ length: 4 }).map((_, i) => <Skeleton key={i} className="h-64" />)}
        </div>
      )}

      {rulesQuery.data && (
        <div className="grid grid-cols-1 items-start gap-4 xl:grid-cols-2">
          {rulesQuery.data.map((rule) => (
            // Remount when the server copy changes, so the local draft always starts from saved values.
            <RuleCard key={`${rule.code}-${rule.updatedAt ?? "default"}`} rule={rule} canManage={canManage} />
          ))}
        </div>
      )}

      <section className="mt-8">
        <h2 className="mb-2.5 flex items-center gap-2 text-[13px] font-semibold uppercase tracking-wide text-ink-500">
          <History className="size-3.5" aria-hidden />
          So&apos;nggi faoliyat
        </h2>
        {runsQuery.isLoading && <Skeleton className="h-32" />}
        {runsQuery.data && runsQuery.data.length === 0 && (
          <div className="rounded-xl border border-border bg-surface">
            <EmptyState icon={Zap} title="Hali faoliyat yo'q" description="Qoida biror vazifa, tavsiya yoki xabar yaratganda shu yerda yoziladi." />
          </div>
        )}
        {!!runsQuery.data?.length && (
          <div className="overflow-x-auto">
            <Table>
              <THead>
                <tr>
                  <TH>Vaqt</TH>
                  <TH>Qoida</TH>
                  <TH>Ishga tushgan</TH>
                  <TH>Natija</TH>
                </tr>
              </THead>
              <TBody>
                {runsQuery.data.map((run) => (
                  <TRow key={run.id}>
                    <TD className="font-tabular text-[13px]">{formatDateTime(run.ranAt)}</TD>
                    <TD className="font-medium text-ink-900">{run.ruleName}</TD>
                    <TD>
                      <Badge tone={run.trigger === "Manual" ? "brand" : run.trigger === "Event" ? "accent" : "neutral"}>
                        {TRIGGER_LABEL[run.trigger]}
                      </Badge>
                    </TD>
                    <TD className="text-ink-700">{run.summary}</TD>
                  </TRow>
                ))}
              </TBody>
            </Table>
          </div>
        )}
      </section>
    </main>
  );
}

function describeRun(result: AutomationRunNowResult): string {
  if (result.invoicesIssued + result.studentsMarkedDebt + result.studentsCleared + result.ruleActions === 0) {
    return "yangi amal kerak bo'lmadi";
  }
  const parts: [number, string][] = [
    [result.invoicesIssued, "ta hisob-faktura"],
    [result.studentsMarkedDebt, "ta student qarzdor bo'ldi"],
    [result.debtTasks, "ta qarz vazifasi"],
    [result.trialFollowUps, "ta trial vazifasi"],
    [result.absenceSignals, "ta yo'qlik bo'yicha tavsiya"],
    [result.homeworkSignals, "ta homework bo'yicha tavsiya"],
    [result.learningSignals, "ta o'zlashtirish bo'yicha tavsiya"],
    [result.reminders, "ta eslatma"],
  ];
  const listed = parts.filter(([count]) => count > 0).map(([count, label]) => `${count} ${label}`);
  return listed.length > 0
    ? `${listed.join(", ")} — batafsil «So'nggi faoliyat»da`
    : `${result.ruleActions} ta amal bajarildi — batafsil «So'nggi faoliyat»da`;
}

/** Stored whole numbers → what a person types and reads: a band of 5.5 is stored as 55. */
const shown = (stored: number, scale: number) => String(stored / scale);
/** What was typed, back to stored units — null when it is not a number or has more decimals than the parameter keeps. */
function stored(text: string, scale: number): number | null {
  const value = Number(text) * scale;
  return text.trim() !== "" && Number.isFinite(value) && Math.abs(value - Math.round(value)) < 1e-6 ? Math.round(value) : null;
}

function RuleCard({ rule, canManage }: { rule: AutomationRule; canManage: boolean }) {
  const client = useQueryClient();
  const [enabled, setEnabled] = useState(rule.enabled);
  const [parameters, setParameters] = useState<Record<string, string>>(
    () => Object.fromEntries(rule.parameters.map((p) => [p.key, shown(p.value, p.scale)])),
  );
  const [actions, setActions] = useState<Record<string, boolean>>(
    () => Object.fromEntries(rule.actions.map((a) => [a.key, a.value])),
  );

  const invalid = rule.parameters.filter((p) => {
    const value = stored(parameters[p.key], p.scale);
    return value === null || value < p.min || value > p.max;
  });
  const dirty =
    enabled !== rule.enabled ||
    rule.parameters.some((p) => stored(parameters[p.key], p.scale) !== p.value) ||
    rule.actions.some((a) => actions[a.key] !== a.value);

  const invalidate = () => client.invalidateQueries({ queryKey: ["automation"] });
  const save = useMutation({
    mutationFn: () =>
      updateAutomationRule(rule.code, {
        enabled,
        parameters: Object.fromEntries(rule.parameters.map((p) => [p.key, stored(parameters[p.key], p.scale) ?? p.value])),
        actions,
      }),
    onSuccess: invalidate,
  });
  const reset = useMutation({ mutationFn: () => resetAutomationRule(rule.code), onSuccess: invalidate });

  return (
    <article className={cn("rounded-xl border bg-surface", enabled ? "border-border" : "border-dashed border-border-strong")}>
      <header className="flex items-start gap-3 border-b border-border px-5 py-4">
        <span className={cn("mt-0.5 flex size-8 shrink-0 items-center justify-center rounded-lg", enabled ? "bg-brand-100 text-brand-700" : "bg-ink-100 text-ink-400")}>
          <Workflow className="size-4" aria-hidden />
        </span>
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-2">
            <h3 id={`rule-${rule.code}`} className="text-[14.5px] font-semibold text-ink-900">{rule.name}</h3>
            {rule.isCustomized ? <Badge tone="brand">Sozlangan</Badge> : <Badge tone="neutral">Standart</Badge>}
          </div>
          <p className="mt-0.5 text-[12.5px] text-ink-500">
            <span className="font-medium text-ink-600">Hodisa:</span> {rule.trigger}
          </p>
        </div>
        <button
          type="button"
          role="switch"
          aria-checked={enabled}
          aria-labelledby={`rule-${rule.code}`}
          disabled={!canManage}
          onClick={() => setEnabled((v) => !v)}
          className={cn(
            "relative mt-1 inline-flex h-5 w-9 shrink-0 items-center rounded-full transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-600 disabled:cursor-not-allowed disabled:opacity-60",
            enabled ? "bg-brand-solid" : "bg-ink-200",
          )}
        >
          <span className={cn("inline-block size-4 rounded-full bg-white shadow transition-transform", enabled ? "translate-x-4" : "translate-x-0.5")} />
        </button>
      </header>

      <div className="space-y-4 px-5 py-4">
        <p className="text-[13px] leading-relaxed text-ink-700">{rule.description}</p>

        {rule.parameters.length > 0 && (
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            {rule.parameters.map((p) => {
              const bad = invalid.some((x) => x.key === p.key);
              return (
                <div key={p.key}>
                  <Label htmlFor={`${rule.code}-${p.key}`}>{p.label}</Label>
                  <div className="flex items-center gap-2">
                    <Input
                      id={`${rule.code}-${p.key}`}
                      type="number"
                      min={p.min / p.scale}
                      max={p.max / p.scale}
                      step={1 / p.scale}
                      disabled={!canManage}
                      value={parameters[p.key]}
                      onChange={(e) => setParameters((prev) => ({ ...prev, [p.key]: e.target.value }))}
                      className={cn("w-24 font-tabular", bad && "border-danger-600")}
                    />
                    <span className="text-[13px] text-ink-500">{p.unit}</span>
                  </div>
                  <p className={cn("mt-1 text-[11.5px]", bad ? "text-danger-600" : "text-ink-400")}>
                    {shown(p.min, p.scale)}–{shown(p.max, p.scale)} · standart {shown(p.default, p.scale)}
                  </p>
                </div>
              );
            })}
          </div>
        )}

        {rule.actions.length > 0 && (
          <fieldset>
            <legend className="mb-1.5 text-[12px] font-semibold uppercase tracking-wide text-ink-400">Amallar</legend>
            <div className="space-y-1.5">
              {rule.actions.map((a) => (
                <label key={a.key} className={cn("flex items-center gap-2.5 text-[13px] text-ink-800", !canManage && "opacity-80")}>
                  <input
                    type="checkbox"
                    className="size-4 accent-brand-600"
                    disabled={!canManage}
                    checked={actions[a.key]}
                    onChange={(e) => setActions((prev) => ({ ...prev, [a.key]: e.target.checked }))}
                  />
                  {a.label}
                </label>
              ))}
            </div>
          </fieldset>
        )}

        {(save.isError || reset.isError) && <Alert>{errorText(save.error ?? reset.error, "Qoidani saqlab bo'lmadi.")}</Alert>}
      </div>

      <footer className="flex flex-wrap items-center justify-between gap-3 border-t border-border px-5 py-3">
        <div className="min-w-0 text-[12px] text-ink-500">
          {rule.lastRun ? (
            <p>
              Oxirgi natija: <span className="text-ink-700">{rule.lastRun.summary}</span> · {formatDateTime(rule.lastRun.ranAt)}
            </p>
          ) : (
            <p>Hali natija bermagan</p>
          )}
          {rule.updatedByName && rule.updatedAt && (
            <p>
              O&apos;zgartirdi: {rule.updatedByName}, {formatDateTime(rule.updatedAt)}
            </p>
          )}
        </div>
        {canManage && (
          <div className="flex gap-2">
            {rule.isCustomized && (
              <Button size="sm" variant="ghost" loading={reset.isPending} onClick={() => reset.mutate()}>
                <RotateCcw className="size-3.5" aria-hidden />
                Standartga qaytarish
              </Button>
            )}
            <Button size="sm" loading={save.isPending} disabled={!dirty || invalid.length > 0} onClick={() => save.mutate()}>
              Saqlash
            </Button>
          </div>
        )}
      </footer>
    </article>
  );
}
