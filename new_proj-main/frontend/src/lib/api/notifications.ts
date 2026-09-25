import { apiFetch } from "../api-client";

export interface AppNotification {
  id: string;
  type: string;
  title: string;
  body: string;
  /** In-app path to open, e.g. /students/{id}. */
  link: string | null;
  createdAt: string;
  readAt: string | null;
}

/** The channels a person can be reached on. In-app is the inbox; Email and SMS leave the app. */
export type NotificationChannel = "inApp" | "email" | "sms";

export interface NotificationPreference {
  type: string;
  label: string;
  description: string;
  /** The inbox switch — the same value as `channels.inApp`. */
  inAppEnabled: boolean;
  channels: Record<NotificationChannel, boolean>;
}

/** Whether a channel can reach the person now, and if not, why ("Hozircha sozlanmagan.", "Profilingizda telefon raqami yo'q."). */
export interface ChannelState {
  available: boolean;
  reason: string | null;
}

export interface ChannelAvailability {
  email: ChannelState;
  sms: ChannelState;
}

export function listNotifications(filter: { unreadOnly?: boolean; limit?: number } = {}): Promise<AppNotification[]> {
  const params = new URLSearchParams();
  if (filter.unreadOnly) params.set("unreadOnly", "true");
  if (filter.limit) params.set("limit", String(filter.limit));
  const qs = params.toString();
  return apiFetch<AppNotification[]>(`/api/notifications${qs ? `?${qs}` : ""}`);
}

export function getUnreadCount(): Promise<{ count: number }> {
  return apiFetch<{ count: number }>("/api/notifications/unread-count");
}

export function markNotificationRead(id: string): Promise<void> {
  return apiFetch<void>(`/api/notifications/${id}/read`, { method: "POST" });
}

export function markAllNotificationsRead(): Promise<{ count: number }> {
  return apiFetch<{ count: number }>("/api/notifications/read-all", { method: "POST" });
}

export function getNotificationPreferences(): Promise<NotificationPreference[]> {
  return apiFetch<NotificationPreference[]>("/api/notifications/preferences");
}

/** Turns one channel of one notification type on or off; the other channels stay as they are. */
export function updateNotificationPreference(type: string, channel: NotificationChannel, enabled: boolean): Promise<void> {
  return apiFetch<void>(`/api/notifications/preferences/${type}`, { method: "PUT", body: JSON.stringify({ [channel]: enabled }) });
}

export function getChannelAvailability(): Promise<ChannelAvailability> {
  return apiFetch<ChannelAvailability>("/api/notifications/channels");
}
