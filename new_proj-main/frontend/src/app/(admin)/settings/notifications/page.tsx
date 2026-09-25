"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { BellOff } from "lucide-react";
import {
  getChannelAvailability, getNotificationPreferences, updateNotificationPreference,
  type NotificationChannel, type NotificationPreference,
} from "@/lib/api/notifications";
import { ApiError } from "@/lib/api-client";
import { PageHeader } from "@/components/ui/PageHeader";
import { Alert } from "@/components/ui/Alert";
import { EmptyState } from "@/components/ui/EmptyState";
import { Skeleton } from "@/components/ui/Skeleton";
import { cn } from "@/lib/cn";

const CHANNELS: { key: NotificationChannel; label: string }[] = [
  { key: "inApp", label: "Ilova" },
  { key: "email", label: "Email" },
  { key: "sms", label: "SMS" },
];

/** One on/off switch with its label, so a row of three reads the same on a phone as on a desktop. */
function ChannelSwitch({
  label, checked, disabled, title, onToggle,
}: { label: string; checked: boolean; disabled?: boolean; title?: string; onToggle: () => void }) {
  return (
    <label className={cn("flex items-center gap-2 text-[12.5px] text-ink-700", disabled ? "opacity-50" : "cursor-pointer")} title={title}>
      <button
        type="button"
        role="switch"
        aria-checked={checked}
        aria-label={label}
        disabled={disabled}
        onClick={onToggle}
        className={cn(
          "relative inline-flex h-5 w-9 shrink-0 items-center rounded-full transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-600",
          checked ? "bg-brand-solid" : "bg-ink-200",
        )}
      >
        <span
          className={cn("inline-block size-4 rounded-full bg-white shadow transition-transform", checked ? "translate-x-4" : "translate-x-0.5")}
        />
      </button>
      {label}
    </label>
  );
}

export default function NotificationSettingsPage() {
  const client = useQueryClient();
  const query = useQuery({ queryKey: ["notifications", "preferences"], queryFn: getNotificationPreferences });
  const availability = useQuery({ queryKey: ["notifications", "channels"], queryFn: getChannelAvailability });

  const mutation = useMutation({
    mutationFn: ({ type, channel, enabled }: { type: string; channel: NotificationChannel; enabled: boolean }) =>
      updateNotificationPreference(type, channel, enabled),
    onMutate: async ({ type, channel, enabled }) => {
      await client.cancelQueries({ queryKey: ["notifications", "preferences"] });
      const previous = client.getQueryData<NotificationPreference[]>(["notifications", "preferences"]);
      client.setQueryData<NotificationPreference[]>(["notifications", "preferences"], (items) =>
        items?.map((p) => (p.type === type
          ? { ...p, inAppEnabled: channel === "inApp" ? enabled : p.inAppEnabled, channels: { ...p.channels, [channel]: enabled } }
          : p)),
      );
      return { previous };
    },
    onError: (_error, _variables, context) => {
      if (context?.previous) client.setQueryData(["notifications", "preferences"], context.previous);
    },
    onSettled: () => client.invalidateQueries({ queryKey: ["notifications", "preferences"] }),
  });

  // Email and SMS can be unavailable (no mail server, no phone number): the switch stays but is dimmed, with the reason beside it.
  const stateOf = (channel: NotificationChannel) => (channel === "inApp" ? undefined : availability.data?.[channel]);
  const unavailable = CHANNELS.filter((c) => c.key !== "inApp" && availability.data && !availability.data[c.key as "email" | "sms"].available);

  return (
    <main className="p-4 sm:p-6 lg:p-8">
      <PageHeader
        title="Bildirishnoma sozlamalari"
        description="Har bir hodisa haqida qayerda xabar olishni tanlang: ilova ichida, emailga yoki SMS orqali. Ro'yxatda faqat sizning rolingizga tegishli bildirishnomalar bor. Email va SMS'da summa yoki ism yozilmaydi — faqat sarlavha va ilovaga havola. Telegram keyingi bosqichda qo'shiladi."
      />

      {query.isError && <Alert>{query.error instanceof ApiError ? query.error.message : "Sozlamalarni yuklab bo'lmadi."}</Alert>}
      {mutation.isError && <Alert>{mutation.error instanceof ApiError ? mutation.error.message : "Sozlamani saqlab bo'lmadi."}</Alert>}
      {query.isLoading && <Skeleton className="h-64 max-w-3xl" />}

      {unavailable.length > 0 && (
        <ul className="mb-4 max-w-3xl space-y-1 rounded-xl border border-border bg-ink-50/60 px-4 py-3 text-[12.5px] text-ink-600">
          {unavailable.map((c) => (
            <li key={c.key}><span className="font-medium text-ink-800">{c.label}:</span> {availability.data?.[c.key as "email" | "sms"].reason}</li>
          ))}
        </ul>
      )}

      {query.data && query.data.length === 0 && (
        <div className="max-w-3xl rounded-xl border border-border bg-surface">
          <EmptyState icon={BellOff} title="Bu rol uchun bildirishnomalar yo'q" />
        </div>
      )}

      {query.data && query.data.length > 0 && (
        <>
          <div className="max-w-3xl divide-y divide-border overflow-hidden rounded-xl border border-border bg-surface">
            {query.data.map((preference) => (
              <div key={preference.type} className="flex flex-col gap-3 px-5 py-4 sm:flex-row sm:items-center sm:gap-6">
                <div className="min-w-0 flex-1">
                  <p className="text-[13.5px] font-medium text-ink-900">{preference.label}</p>
                  <p className="mt-0.5 text-[12.5px] text-ink-500">{preference.description}</p>
                </div>
                <div className="flex flex-wrap items-center gap-x-5 gap-y-2">
                  {CHANNELS.map((c) => {
                    const state = stateOf(c.key);
                    const blocked = state !== undefined && !state.available;
                    return (
                      <ChannelSwitch
                        key={c.key}
                        label={c.label}
                        checked={preference.channels[c.key]}
                        disabled={blocked}
                        title={blocked ? state.reason ?? undefined : undefined}
                        onToggle={() => mutation.mutate({ type: preference.type, channel: c.key, enabled: !preference.channels[c.key] })}
                      />
                    );
                  })}
                </div>
              </div>
            ))}
          </div>
          <p className="mt-3 max-w-3xl text-[12.5px] text-ink-500">SMS pulli bo&apos;lishi mumkin: uni faqat kerakli hodisalar uchun yoqing.</p>
        </>
      )}
    </main>
  );
}
