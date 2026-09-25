"use client";

import { Bar, BarChart, CartesianGrid, LabelList, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import type { FunnelStage } from "@/lib/api/crm";

// Chart colours are the design tokens, so the chart follows the theme (the ramp turns over at night).
const BRAND = "var(--color-brand-solid)";
const GRID = "var(--color-border)";
const CHART_TEXT = { fontSize: 12, fill: "var(--color-ink-500)" };

/** The lead funnel as horizontal bars, one per stage. Drawn from the server's stages, so the branch dashboard and the
 * leads page — which count with the same server code — also look the same. */
export function FunnelChart({ stages }: { stages: FunnelStage[] }) {
  const data = stages.map((s) => ({ name: s.label, value: s.count }));
  return (
    <div className="h-72 w-full" role="img" aria-label={`Voronka: ${data.map((s) => `${s.name} ${s.value}`).join(", ")}`}>
      <ResponsiveContainer width="100%" height="100%">
        <BarChart data={data} layout="vertical" margin={{ top: 4, right: 32, bottom: 4, left: 8 }}>
          <CartesianGrid stroke={GRID} horizontal={false} />
          <XAxis type="number" hide domain={[0, "dataMax"]} />
          <YAxis type="category" dataKey="name" width={118} tick={CHART_TEXT} tickLine={false} axisLine={false} />
          <Tooltip cursor={{ fill: "transparent" }} contentStyle={{ borderRadius: 8, border: `1px solid ${GRID}` }} />
          <Bar dataKey="value" name="Lid" fill={BRAND} radius={[0, 6, 6, 0]} barSize={22} isAnimationActive={false}>
            <LabelList dataKey="value" position="right" style={{ ...CHART_TEXT, fill: "var(--color-ink-800)" }} />
          </Bar>
        </BarChart>
      </ResponsiveContainer>
    </div>
  );
}
