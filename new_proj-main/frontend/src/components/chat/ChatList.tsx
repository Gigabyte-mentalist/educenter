"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Hash, Megaphone, MessageSquarePlus, Search, UsersRound } from "lucide-react";
import { listChatContacts, listChats, openDirectChat, type ChatListItem } from "@/lib/api/chat";
import { ApiError } from "@/lib/api-client";
import { roleLabel } from "@/lib/auth-storage";
import { syncChats, useChatEvent } from "@/lib/chat-hub";
import { Alert } from "@/components/ui/Alert";
import { Avatar } from "@/components/ui/Avatar";
import { Button } from "@/components/ui/Button";
import { Drawer } from "@/components/ui/Drawer";
import { EmptyState } from "@/components/ui/EmptyState";
import { Input } from "@/components/ui/Field";
import { Skeleton } from "@/components/ui/Skeleton";
import { cn } from "@/lib/cn";
import { formatShortDate, formatTime } from "@/lib/format-date";

export function formatChatTime(iso: string): string {
  const date = new Date(iso);
  const sameDay = date.toDateString() === new Date().toDateString();
  return sameDay ? formatTime(date) : formatShortDate(date).slice(0, 5);
}

export function ChatIcon({ chat, className }: { chat: ChatListItem; className?: string }) {
  if (chat.type === "Direct") return <Avatar name={chat.title} className={className} />;
  const Icon = chat.type === "Group" ? UsersRound : chat.channelKind === "Announcements" ? Megaphone : Hash;
  return (
    <span className={cn("flex size-9 shrink-0 items-center justify-center rounded-full", chat.type === "Group" ? "bg-brand-100 text-brand-700" : "bg-ink-100 text-ink-600", className)}>
      <Icon className="size-4" aria-hidden />
    </span>
  );
}

export function ChatList({ selectedId, onSelect, className }: { selectedId: string | null; onSelect: (id: string) => void; className?: string }) {
  const client = useQueryClient();
  const [search, setSearch] = useState("");
  const [newChatOpen, setNewChatOpen] = useState(false);

  const chatsQuery = useQuery({ queryKey: ["chats", "list"], queryFn: listChats, refetchInterval: 60_000 });

  const refresh = useCallback(() => {
    void client.invalidateQueries({ queryKey: ["chats", "list"] });
  }, [client]);
  useChatEvent("chatActivity", refresh);
  useChatEvent("messageCreated", refresh);
  useChatEvent("readUpdated", refresh);
  // An administrator can delete a conversation out from under whoever has it open; without this their list
  // keeps the row until the next full refetch and clicking it 404s.
  useChatEvent("chatDeleted", refresh);

  // A chat that appeared in the list (new group, new direct chat) needs a hub subscription too.
  const idsKey = (chatsQuery.data ?? []).map((c) => c.id).sort().join(",");
  useEffect(() => {
    if (idsKey) void syncChats();
  }, [idsKey]);

  const sections = useMemo(() => {
    const term = search.trim().toLowerCase();
    const chats = (chatsQuery.data ?? []).filter((c) => !term || c.title.toLowerCase().includes(term) || c.subtitle?.toLowerCase().includes(term));
    return [
      { label: "Shaxsiy", items: chats.filter((c) => c.type === "Direct") },
      { label: "Guruhlar", items: chats.filter((c) => c.type === "Group") },
      { label: "Filial kanallari", items: chats.filter((c) => c.type === "StaffChannel") },
    ].filter((s) => s.items.length > 0);
  }, [chatsQuery.data, search]);

  return (
    <section className={cn("flex min-h-0 flex-col border-r border-border bg-surface", className)} aria-label="Suhbatlar">
      <div className="flex items-center justify-between gap-2 px-4 pb-3 pt-5">
        <h1 className="text-[18px] font-semibold text-ink-900">Xabarlar</h1>
        <Button size="sm" variant="secondary" onClick={() => setNewChatOpen(true)}>
          <MessageSquarePlus className="size-3.5" aria-hidden />
          Yangi suhbat
        </Button>
      </div>
      <div className="px-4 pb-3">
        <div className="relative">
          <Search className="pointer-events-none absolute left-3 top-1/2 size-3.5 -translate-y-1/2 text-ink-400" aria-hidden />
          <Input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Suhbatni qidirish" className="pl-8" aria-label="Suhbatni qidirish" />
        </div>
      </div>

      <div className="min-h-0 flex-1 overflow-y-auto px-2 pb-4">
        {chatsQuery.isLoading && <div className="space-y-2 px-2">{Array.from({ length: 6 }).map((_, i) => <Skeleton key={i} className="h-14" />)}</div>}
        {chatsQuery.isError && <div className="px-2"><Alert>{chatsQuery.error instanceof ApiError ? chatsQuery.error.message : "Suhbatlarni yuklab bo'lmadi."}</Alert></div>}
        {chatsQuery.data && sections.length === 0 && (
          <EmptyState icon={MessageSquarePlus} title={search ? "Hech narsa topilmadi" : "Hali suhbat yo'q"} description={search ? undefined : "«Yangi suhbat» orqali o'qituvchi, student yoki hamkasbingizga yozing."} />
        )}
        {sections.map((section) => (
          <div key={section.label} className="mb-3">
            <p className="mb-1 px-2 text-[11px] font-semibold uppercase tracking-wide text-ink-400">{section.label}</p>
            {section.items.map((chat) => (
              <button
                key={chat.id}
                type="button"
                onClick={() => onSelect(chat.id)}
                aria-current={chat.id === selectedId ? "true" : undefined}
                className={cn(
                  "flex w-full items-center gap-3 rounded-lg px-2 py-2 text-left transition-colors",
                  chat.id === selectedId ? "bg-brand-50" : "hover:bg-ink-50",
                )}
              >
                <ChatIcon chat={chat} />
                <span className="min-w-0 flex-1">
                  <span className="flex items-baseline gap-2">
                    <span className={cn("truncate text-[13.5px] text-ink-900", chat.unreadCount > 0 ? "font-semibold" : "font-medium")}>{chat.title}</span>
                    {chat.lastMessageAt && <span className="ml-auto shrink-0 text-[11px] text-ink-400">{formatChatTime(chat.lastMessageAt)}</span>}
                  </span>
                  <span className="flex items-center gap-2">
                    <span className="truncate text-[12px] text-ink-500">
                      {chat.lastMessage
                        ? `${chat.type === "Direct" ? "" : `${chat.lastMessage.senderName.split(" ")[0]}: `}${chat.lastMessage.preview}`
                        : chat.subtitle ?? (chat.counterpart?.roleCode ? roleLabel(chat.counterpart.roleCode) : "")}
                    </span>
                    {chat.unreadCount > 0 && (
                      <span className="ml-auto min-w-5 shrink-0 rounded-full bg-brand-solid px-1.5 py-0.5 text-center text-[10.5px] font-semibold leading-none text-white">
                        {chat.unreadCount > 99 ? "99+" : chat.unreadCount}
                      </span>
                    )}
                  </span>
                </span>
              </button>
            ))}
          </div>
        ))}
      </div>

      <NewChatDrawer open={newChatOpen} onClose={() => setNewChatOpen(false)} onOpened={(id) => { setNewChatOpen(false); onSelect(id); }} />
    </section>
  );
}

function NewChatDrawer({ open, onClose, onOpened }: { open: boolean; onClose: () => void; onOpened: (id: string) => void }) {
  const client = useQueryClient();
  const [search, setSearch] = useState("");
  const contactsQuery = useQuery({ queryKey: ["chats", "contacts", search.trim()], queryFn: () => listChatContacts(search), enabled: open });

  const open1to1 = useMutation({
    mutationFn: openDirectChat,
    onSuccess: async (chat) => {
      await client.invalidateQueries({ queryKey: ["chats", "list"] });
      onOpened(chat.id);
    },
  });

  return (
    <Drawer open={open} onClose={onClose} title="Yangi suhbat" description="Siz yozishingiz mumkin bo'lgan odamlar: o'qituvchilar, studentlar, ota-onalar va hamkasblar — rolingizga qarab.">
      <div className="space-y-3">
        <Input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Ism bo'yicha qidirish" aria-label="Ism bo'yicha qidirish" />
        {open1to1.isError && <Alert>{open1to1.error instanceof ApiError ? open1to1.error.message : "Suhbatni ochib bo'lmadi."}</Alert>}
        {contactsQuery.isError && <Alert>{contactsQuery.error instanceof ApiError ? contactsQuery.error.message : "Kontaktlarni yuklab bo'lmadi."}</Alert>}
        {contactsQuery.isLoading && <Skeleton className="h-40" />}
        {contactsQuery.data?.length === 0 && <p className="py-6 text-center text-[13px] text-ink-500">Hech kim topilmadi.</p>}
        <div className="space-y-0.5">
          {contactsQuery.data?.map((contact) => (
            <button
              key={contact.userId}
              type="button"
              disabled={open1to1.isPending}
              onClick={() => open1to1.mutate(contact.userId)}
              className="flex w-full items-center gap-3 rounded-lg px-2 py-2 text-left hover:bg-ink-50 disabled:opacity-60"
            >
              <Avatar name={contact.fullName} />
              <span className="min-w-0">
                <span className="block truncate text-[13.5px] font-medium text-ink-900">{contact.fullName}</span>
                {contact.roleCode && <span className="block text-[12px] text-ink-500">{roleLabel(contact.roleCode)}</span>}
              </span>
            </button>
          ))}
        </div>
      </div>
    </Drawer>
  );
}
