"use client";

import { PREFERRED_TIME_OPTIONS, type PreferredTimeCode } from "@/lib/api/crm";
import { Input, Label } from "@/components/ui/Field";

/** "Qulay vaqt" (which parts of the day suit the person) and "Daraja" — the two facts the group recommendation weighs.
 * Shared by the new-lead form and the lead profile, so both ask the same way. */
export function LeadPreferencesFields({
  idPrefix, times, onTimes, level, onLevel, suggestedLevel,
}: {
  idPrefix: string;
  times: PreferredTimeCode[];
  onTimes: (times: PreferredTimeCode[]) => void;
  level: string;
  onLevel: (level: string) => void;
  /** Level from the latest placement test; offered as a one-tap fill, never applied by itself. */
  suggestedLevel?: string | null;
}) {
  const toggle = (code: PreferredTimeCode) =>
    onTimes(times.includes(code) ? times.filter((c) => c !== code) : [...times, code]);

  return (
    <>
      <fieldset>
        <legend className="mb-1.5 block text-[13px] font-medium text-ink-700">Qulay vaqt</legend>
        <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
          {PREFERRED_TIME_OPTIONS.map((o) => (
            <label
              key={o.code}
              className="flex cursor-pointer items-center gap-2 rounded-lg border border-border px-3 py-2 text-[13px] text-ink-700 has-[:checked]:border-brand-400 has-[:checked]:bg-brand-50"
            >
              <input type="checkbox" checked={times.includes(o.code)} onChange={() => toggle(o.code)} />
              <span>
                {o.label}
                <span className="ml-1.5 text-[11.5px] text-ink-400">{o.hint}</span>
              </span>
            </label>
          ))}
        </div>
        <p className="mt-1.5 text-[12px] text-ink-500">Hech biri tanlanmasa — farqi yo&apos;q.</p>
      </fieldset>
      <div>
        <Label htmlFor={`${idPrefix}-level`}>Daraja</Label>
        <Input
          id={`${idPrefix}-level`}
          value={level}
          maxLength={50}
          onChange={(e) => onLevel(e.target.value)}
          placeholder="A1, B1, IELTS Foundation…"
        />
        {suggestedLevel && suggestedLevel !== level && (
          <button
            type="button"
            onClick={() => onLevel(suggestedLevel)}
            className="mt-1.5 text-[12.5px] font-medium text-brand-700 hover:underline"
          >
            Oxirgi test natijasini qo&apos;llash: {suggestedLevel}
          </button>
        )}
      </div>
    </>
  );
}
