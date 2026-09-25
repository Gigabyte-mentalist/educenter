import { apiFetch, apiFetchBlob } from "../api-client";

export type ChatType = "Direct" | "Group" | "StaffChannel";
export type StaffChannelKind = "General" | "Teachers" | "IeltsTeachers" | "Administrators" | "Announcements";

export const CHAT_REACTIONS = ["👍", "❤️", "😂", "😮", "🙏", "✅"] as const;

export interface ChatContact {
  userId: string;
  fullName: string;
  roleCode: string | null;
}

export interface ChatListItem {
  id: string;
  type: ChatType;
  title: string;
  subtitle: string | null;
  groupId: string | null;
  branchId: string | null;
  channelKind: StaffChannelKind | null;
  counterpart: ChatContact | null;
  lastMessage: { senderId: string; senderName: string; preview: string; createdAt: string } | null;
  unreadCount: number;
  canPost: boolean;
  lastMessageAt: string | null;
}

export interface ChatReader {
  userId: string;
  fullName: string;
  lastReadAt: string;
}

export interface ChatMessage {
  id: string;
  chatId: string;
  senderId: string;
  senderName: string;
  senderRole: string | null;
  body: string | null;
  createdAt: string;
  isDeleted: boolean;
  replyTo: { id: string; senderId: string; senderName: string; preview: string } | null;
  attachment: { fileName: string; contentType: string; size: number; isImage: boolean } | null;
  reactions: { emoji: string; count: number; userIds: string[] }[];
  pinnedAt: string | null;
}

export interface ChatDetail {
  chat: ChatListItem;
  canModerate: boolean;
  canPin: boolean;
  /** Only ever true for a direct chat: group chats are generated from the group and come back with it. */
  canDelete: boolean;
  readers: ChatReader[];
  pinned: ChatMessage[];
}

export function listChats(): Promise<ChatListItem[]> {
  return apiFetch<ChatListItem[]>("/api/chats");
}

export function getChatUnreadCount(): Promise<{ count: number }> {
  return apiFetch<{ count: number }>("/api/chats/unread-count");
}

export function listChatContacts(search?: string): Promise<ChatContact[]> {
  const query = search?.trim() ? `?search=${encodeURIComponent(search.trim())}` : "";
  return apiFetch<ChatContact[]>(`/api/chats/contacts${query}`);
}

export function openDirectChat(userId: string): Promise<ChatListItem> {
  return apiFetch<ChatListItem>("/api/chats/direct", { method: "POST", body: JSON.stringify({ userId }) });
}

export function getChat(id: string): Promise<ChatDetail> {
  return apiFetch<ChatDetail>(`/api/chats/${id}`);
}

export function listChatMessages(id: string, options: { before?: string; search?: string; limit?: number } = {}): Promise<ChatMessage[]> {
  const params = new URLSearchParams();
  if (options.before) params.set("before", options.before);
  if (options.search) params.set("search", options.search);
  params.set("limit", String(options.limit ?? 50));
  return apiFetch<ChatMessage[]>(`/api/chats/${id}/messages?${params}`);
}

export function sendChatMessage(id: string, input: { body?: string; replyToMessageId?: string | null; file?: File | null }): Promise<ChatMessage> {
  const form = new FormData();
  if (input.body?.trim()) form.set("Body", input.body);
  if (input.replyToMessageId) form.set("ReplyToMessageId", input.replyToMessageId);
  if (input.file) form.set("File", input.file);
  return apiFetch<ChatMessage>(`/api/chats/${id}/messages`, { method: "POST", body: form });
}

/** Removes the conversation and every message in it. Not reversible. */
export function deleteChat(id: string): Promise<void> {
  return apiFetch<void>(`/api/chats/${id}`, { method: "DELETE" });
}

export function deleteChatMessage(chatId: string, messageId: string): Promise<ChatMessage> {
  return apiFetch<ChatMessage>(`/api/chats/${chatId}/messages/${messageId}`, { method: "DELETE" });
}

export function setChatMessagePinned(chatId: string, messageId: string, pinned: boolean): Promise<ChatMessage> {
  return apiFetch<ChatMessage>(`/api/chats/${chatId}/messages/${messageId}/pin`, { method: pinned ? "POST" : "DELETE" });
}

export function toggleChatReaction(chatId: string, messageId: string, emoji: string): Promise<ChatMessage> {
  return apiFetch<ChatMessage>(`/api/chats/${chatId}/messages/${messageId}/reactions`, { method: "POST", body: JSON.stringify({ emoji }) });
}

export function markChatRead(id: string): Promise<void> {
  return apiFetch<void>(`/api/chats/${id}/read`, { method: "POST" });
}

export function fetchChatAttachment(chatId: string, messageId: string): Promise<Blob> {
  return apiFetchBlob(`/api/chats/${chatId}/messages/${messageId}/attachment`);
}
