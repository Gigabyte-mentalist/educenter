"use client";

import { useCallback } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { getChatUnreadCount } from "@/lib/api/chat";
import { startChatConnection, useChatEvent } from "@/lib/chat-hub";

/** Unread chat messages next to "Xabarlar" in the sidebar. Live over the chat hub, with a slow poll as a fallback
 * that also brings the connection back if it dropped. */
export function ChatUnreadBadge() {
  const client = useQueryClient();
  const query = useQuery({
    queryKey: ["chats", "unread-count"],
    queryFn: async () => {
      void startChatConnection();
      return getChatUnreadCount();
    },
    refetchInterval: 60_000,
  });

  const refresh = useCallback(() => {
    void client.invalidateQueries({ queryKey: ["chats"] });
  }, [client]);
  useChatEvent("messageCreated", refresh);
  useChatEvent("chatActivity", refresh);

  const count = query.data?.count ?? 0;
  if (count === 0) return null;

  return (
    <span className="ml-auto min-w-5 rounded-full bg-brand-solid px-1.5 py-0.5 text-center text-[11px] font-semibold leading-none text-white">
      {count > 99 ? "99+" : count}
    </span>
  );
}
