"use client";

import { Fragment, useCallback, useEffect, useRef, useState } from "react";
import { type InfiniteData, useInfiniteQuery, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ArrowLeft, Check, CheckCheck, Download, FileText, Paperclip, Pin, PinOff, Reply, Search, SendHorizontal, SmilePlus, Trash2, Trash, X,
} from "lucide-react";
import { HubConnectionState } from "@microsoft/signalr";
import {
  CHAT_REACTIONS, deleteChat, deleteChatMessage, fetchChatAttachment, getChat, listChatMessages, markChatRead,
  sendChatMessage, setChatMessagePinned, toggleChatReaction, type ChatMessage,
} from "@/lib/api/chat";
import { ApiError } from "@/lib/api-client";
import { roleLabel } from "@/lib/auth-storage";
import { sendTyping, useChatConnectionState, useChatEvent } from "@/lib/chat-hub";
import { useAuthSession } from "@/lib/use-auth-session";
import { ChatIcon, formatChatTime } from "@/components/chat/ChatList";
import { Alert } from "@/components/ui/Alert";
import { ConfirmDialog } from "@/components/ui/ConfirmDialog";
import { Input } from "@/components/ui/Field";
import { Skeleton } from "@/components/ui/Skeleton";
import { cn } from "@/lib/cn";
import { formatDayHeading } from "@/lib/format-date";

const PAGE_SIZE = 50;

/** Mirrors the server-side SanitizeBody logic: strips invisible Unicode fillers so the send
 *  button stays disabled when the textarea contains only invisible characters like U+3164. */
function sanitizeBody(raw: string): string {
  return raw
    .trim()
    // Explicit invisible code points (zero-width, fillers, BOM, marks)
    .replace(/[\u200B-\u200F\u2028\u2029\u202A-\u202E\u2060-\u2064\u206A-\u206F\u3164\uFEFF\uFFA0]/g, "")
    // Braille patterns used as invisible spacers (U+2800–U+28FF)
    .replace(/[\u2800-\u28FF]/g, "")
    // Collapse 3+ consecutive newlines down to 2 (one blank line max)
    .replace(/\n{3,}/g, "\n\n")
    .trim();
}
const MAX_FILE_BYTES = 10 * 1024 * 1024;
const FILE_ACCEPT = ".jpg,.jpeg,.png,.gif,.webp,.pdf,.txt,.zip,.doc,.docx,.xls,.xlsx,.ppt,.pptx,.mp3,.m4a";

type Pages = InfiniteData<ChatMessage[], string | undefined>;

const errorText = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

function formatSize(bytes: number): string {
  return bytes < 1024 * 1024 ? `${Math.max(1, Math.round(bytes / 1024))} KB` : `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

const dayLabel = formatDayHeading;

async function downloadAttachment(message: ChatMessage) {
  const blob = await fetchChatAttachment(message.chatId, message.id);
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = message.attachment?.fileName ?? "fayl";
  link.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

export function Conversation({ chatId, onBack }: { chatId: string; onBack: () => void }) {
  const client = useQueryClient();
  const me = useAuthSession().user?.id ?? null;
  const connection = useChatConnectionState();

  const [body, setBody] = useState("");
  const [file, setFile] = useState<File | null>(null);
  const [fileError, setFileError] = useState<string | null>(null);
  const [replyTo, setReplyTo] = useState<ChatMessage | null>(null);
  const [searchOpen, setSearchOpen] = useState(false);
  const [search, setSearch] = useState("");
  const [typing, setTyping] = useState<Record<string, string>>({});
  const [confirmDeleteChat, setConfirmDeleteChat] = useState(false);

  const scrollRef = useRef<HTMLDivElement>(null);
  const nearBottom = useRef(true);
  const lastTypingSent = useRef(0);
  const typingTimers = useRef(new Map<string, ReturnType<typeof setTimeout>>());
  const fileInput = useRef<HTMLInputElement>(null);

  const messagesKey = ["chats", "messages", chatId];
  const detailQuery = useQuery({ queryKey: ["chats", "detail", chatId], queryFn: () => getChat(chatId) });
  const messagesQuery = useInfiniteQuery({
    queryKey: messagesKey,
    queryFn: ({ pageParam }) => listChatMessages(chatId, { before: pageParam, limit: PAGE_SIZE }),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (lastPage) => (lastPage.length === PAGE_SIZE ? lastPage[0].createdAt : undefined),
  });
  const searchTerm = search.trim();
  const searchQuery = useQuery({
    queryKey: ["chats", "search", chatId, searchTerm],
    queryFn: () => listChatMessages(chatId, { search: searchTerm, limit: 30 }),
    enabled: searchOpen && searchTerm.length >= 2,
  });

  const messages = [...(messagesQuery.data?.pages ?? [])].reverse().flat();
  const newest = messages.at(-1);
  const detail = detailQuery.data;
  const chat = detail?.chat;

  // ---- cache updates from the hub and from our own writes ----
  const appendMessage = useCallback((message: ChatMessage) => {
    client.setQueryData<Pages>(["chats", "messages", chatId], (data) => {
      if (!data || data.pages.some((page) => page.some((m) => m.id === message.id))) return data;
      const [first = [], ...rest] = data.pages;
      return { ...data, pages: [[...first, message], ...rest] };
    });
  }, [client, chatId]);

  const replaceMessage = useCallback((message: ChatMessage) => {
    client.setQueryData<Pages>(["chats", "messages", chatId], (data) =>
      data && { ...data, pages: data.pages.map((page) => page.map((m) => (m.id === message.id ? message : m))) });
    void client.invalidateQueries({ queryKey: ["chats", "detail", chatId] });
  }, [client, chatId]);

  const onCreated = useCallback((message: ChatMessage) => {
    if (message.chatId !== chatId) return;
    appendMessage(message);
    setTyping((current) => {
      if (!(message.senderId in current)) return current;
      const next = { ...current };
      delete next[message.senderId];
      return next;
    });
  }, [chatId, appendMessage]);
  const onUpdated = useCallback((message: ChatMessage) => {
    if (message.chatId === chatId) replaceMessage(message);
  }, [chatId, replaceMessage]);
  const onRead = useCallback((payload: { chatId: string }) => {
    if (payload.chatId === chatId) void client.invalidateQueries({ queryKey: ["chats", "detail", chatId] });
  }, [client, chatId]);
  const onTyping = useCallback((payload: { chatId: string; userId: string; name: string }) => {
    if (payload.chatId !== chatId) return;
    setTyping((current) => ({ ...current, [payload.userId]: payload.name }));
    const timers = typingTimers.current;
    clearTimeout(timers.get(payload.userId));
    timers.set(payload.userId, setTimeout(() => {
      setTyping((current) => {
        const next = { ...current };
        delete next[payload.userId];
        return next;
      });
    }, 3500));
  }, [chatId]);

  useChatEvent("messageCreated", onCreated);
  useChatEvent("messageUpdated", onUpdated);
  useChatEvent("readUpdated", onRead);
  useChatEvent("typing", onTyping);

  useEffect(() => {
    const timers = typingTimers.current;
    return () => timers.forEach((timer) => clearTimeout(timer));
  }, []);

  // ---- read cursor + scrolling follow the newest message ----
  const newestId = newest?.id;
  const newestFromMe = newest?.senderId === me;
  useEffect(() => {
    if (!newestId) return;
    const el = scrollRef.current;
    if (el && (nearBottom.current || newestFromMe)) el.scrollTop = el.scrollHeight;
    if (!newestFromMe && document.visibilityState === "visible") {
      void markChatRead(chatId).then(() => client.invalidateQueries({ queryKey: ["chats", "list"] }))
        .then(() => client.invalidateQueries({ queryKey: ["chats", "unread-count"] }))
        .catch(() => {});
    }
  }, [newestId, newestFromMe, chatId, client]);

  const loadOlder = async () => {
    const el = scrollRef.current;
    const previousHeight = el?.scrollHeight ?? 0;
    await messagesQuery.fetchNextPage();
    requestAnimationFrame(() => {
      if (el) el.scrollTop += el.scrollHeight - previousHeight;
    });
  };

  // ---- writes ----
  const send = useMutation({
    mutationFn: () => sendChatMessage(chatId, { body, replyToMessageId: replyTo?.id, file }),
    onSuccess: (message) => {
      appendMessage(message);
      setBody("");
      setFile(null);
      setReplyTo(null);
      void client.invalidateQueries({ queryKey: ["chats", "list"] });
    },
  });
  const remove = useMutation({ mutationFn: (id: string) => deleteChatMessage(chatId, id), onSuccess: replaceMessage });
  const pin = useMutation({ mutationFn: ({ id, pinned }: { id: string; pinned: boolean }) => setChatMessagePinned(chatId, id, pinned), onSuccess: replaceMessage });
  const react = useMutation({ mutationFn: ({ id, emoji }: { id: string; emoji: string }) => toggleChatReaction(chatId, id, emoji), onSuccess: replaceMessage });
  const deleteChatMutation = useMutation({
    mutationFn: () => deleteChat(chatId),
    onSuccess: () => {
      setConfirmDeleteChat(false);
      // The chat no longer exists, so its own queries would 404 on the next refetch; drop them and send the
      // caller back to the list rather than leaving an empty pane pointing at a dead id.
      client.removeQueries({ queryKey: ["chats", "detail", chatId] });
      client.removeQueries({ queryKey: ["chats", "messages", chatId] });
      void client.invalidateQueries({ queryKey: ["chats"] });
      onBack();
    },
  });
  const actionError = remove.error ?? pin.error ?? react.error;

  const canSend = !send.isPending && (sanitizeBody(body).length > 0 || file !== null);
  const submit = () => {
    if (canSend) send.mutate();
  };

  const pickFile = (picked: File | undefined) => {
    setFileError(null);
    if (!picked) return;
    if (picked.size > MAX_FILE_BYTES) {
      setFileError("Fayl hajmi 10 MB dan oshmasligi kerak.");
      return;
    }
    setFile(picked);
  };

  const onBodyChange = (value: string) => {
    // Strip invisible Unicode fillers as the user types so they can never accumulate in the input.
    const cleaned = value
      .replace(/[\u200B-\u200F\u2028\u2029\u202A-\u202E\u2060-\u2064\u206A-\u206F\u3164\uFEFF\uFFA0]/g, "")
      .replace(/[\u2800-\u28FF]/g, "")
      .replace(/\n{3,}/g, "\n\n");
    setBody(cleaned);
    const now = performance.now();
    if (cleaned && now - lastTypingSent.current > 2500) {
      lastTypingSent.current = now;
      sendTyping(chatId);
    }
  };

  const othersRead = (message: ChatMessage) =>
    (detail?.readers ?? []).filter((r) => r.userId !== me && new Date(r.lastReadAt) >= new Date(message.createdAt)).length;

  const typingNames = Object.values(typing);

  if (detailQuery.isError) {
    return (
      <div className="flex flex-1 items-center justify-center p-8">
        <Alert>{errorText(detailQuery.error, "Suhbatni ochib bo'lmadi.")}</Alert>
      </div>
    );
  }

  return (
    <section className="flex min-h-0 flex-1 flex-col bg-canvas" aria-label={chat?.title ?? "Suhbat"}>
      <header className="flex items-center gap-3 border-b border-border bg-surface px-4 py-3">
        <button type="button" onClick={onBack} className="rounded-md p-1.5 text-ink-500 hover:bg-ink-50 md:hidden" aria-label="Suhbatlar ro'yxatiga qaytish">
          <ArrowLeft className="size-4" aria-hidden />
        </button>
        {chat ? <ChatIcon chat={chat} /> : <Skeleton className="size-9 rounded-full" />}
        <div className="min-w-0 flex-1">
          <h2 className="truncate text-[14.5px] font-semibold text-ink-900">{chat?.title ?? "…"}</h2>
          <p className="truncate text-[12px] text-ink-500">
            {typingNames.length > 0
              ? <span className="text-brand-700">{typingNames.join(", ")} yozmoqda…</span>
              : connection === HubConnectionState.Reconnecting || connection === HubConnectionState.Connecting
                ? "Ulanmoqda…"
                : chat?.type === "Direct"
                  ? (chat.counterpart?.roleCode ? roleLabel(chat.counterpart.roleCode) : "Shaxsiy suhbat")
                  : chat?.subtitle}
          </p>
        </div>
        <button
          type="button"
          onClick={() => setSearchOpen((v) => !v)}
          aria-pressed={searchOpen}
          className={cn("rounded-md p-2 text-ink-500 hover:bg-ink-50", searchOpen && "bg-ink-100 text-ink-900")}
          aria-label="Xabarlardan qidirish"
        >
          <Search className="size-4" aria-hidden />
        </button>
        {detail?.canDelete && (
          <button
            type="button"
            onClick={() => {
              deleteChatMutation.reset();
              setConfirmDeleteChat(true);
            }}
            className="rounded-md p-2 text-ink-500 hover:bg-danger-50 hover:text-danger-600"
            aria-label="Suhbatni o'chirish"
            title="Suhbatni o'chirish"
          >
            <Trash className="size-4" aria-hidden />
          </button>
        )}
      </header>

      <ConfirmDialog
        open={confirmDeleteChat}
        title="Suhbatni o'chirish"
        description={`"${chat?.title ?? ""}" suhbati va undagi barcha xabarlar hamda fayllar butunlay o'chiriladi. Buni qaytarib bo'lmaydi.`}
        loading={deleteChatMutation.isPending}
        error={deleteChatMutation.isError ? "Suhbatni o'chirib bo'lmadi." : null}
        onConfirm={() => deleteChatMutation.mutate()}
        onClose={() => setConfirmDeleteChat(false)}
      />

      {searchOpen && (
        <div className="border-b border-border bg-surface px-4 py-3">
          <Input autoFocus value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Kamida 2 ta harf" aria-label="Xabarlardan qidirish" />
          {searchQuery.data && (
            <div className="mt-2 max-h-48 space-y-1 overflow-y-auto">
              {searchQuery.data.length === 0 && <p className="text-[12.5px] text-ink-500">Mos xabar topilmadi.</p>}
              {searchQuery.data.slice().reverse().map((m) => (
                <div key={m.id} className="rounded-md bg-ink-50 px-2.5 py-1.5 text-[12.5px]">
                  <span className="font-medium text-ink-800">{m.senderName}</span>
                  <span className="text-ink-400"> · {formatChatTime(m.createdAt)}</span>
                  <p className="line-clamp-2 text-ink-700">{m.body}</p>
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {detail && detail.pinned.length > 0 && (
        <div className="flex items-center gap-2 border-b border-border bg-accent-300/15 px-4 py-2 text-[12.5px]">
          <Pin className="size-3.5 shrink-0 text-accent-700" aria-hidden />
          <span className="min-w-0 flex-1 truncate text-ink-800">
            <span className="font-medium">{detail.pinned[0].senderName}:</span> {detail.pinned[0].body ?? detail.pinned[0].attachment?.fileName}
          </span>
          {detail.pinned.length > 1 && <span className="shrink-0 text-ink-500">+{detail.pinned.length - 1}</span>}
        </div>
      )}

      <div
        ref={scrollRef}
        onScroll={(e) => {
          const el = e.currentTarget;
          nearBottom.current = el.scrollHeight - el.scrollTop - el.clientHeight < 120;
        }}
        className="min-h-0 flex-1 overflow-y-auto px-4 py-4"
      >
        {messagesQuery.hasNextPage && (
          <div className="mb-3 text-center">
            <button type="button" onClick={loadOlder} disabled={messagesQuery.isFetchingNextPage} className="rounded-full border border-border bg-surface px-3 py-1 text-[12px] text-ink-600 hover:bg-ink-50 disabled:opacity-60">
              {messagesQuery.isFetchingNextPage ? "Yuklanmoqda…" : "Oldingi xabarlar"}
            </button>
          </div>
        )}
        {messagesQuery.isLoading && <div className="space-y-3">{Array.from({ length: 5 }).map((_, i) => <Skeleton key={i} className={cn("h-12 w-2/3", i % 2 && "ml-auto")} />)}</div>}
        {messagesQuery.isError && <Alert>{errorText(messagesQuery.error, "Xabarlarni yuklab bo'lmadi.")}</Alert>}
        {messagesQuery.data && messages.length === 0 && (
          <p className="mt-10 text-center text-[13px] text-ink-500">Hali xabar yo&apos;q. Birinchi bo&apos;lib yozing.</p>
        )}

        <ol className="space-y-1.5">
          {messages.map((message, index) => {
            const previous = messages[index - 1];
            const newDay = !previous || new Date(previous.createdAt).toDateString() !== new Date(message.createdAt).toDateString();
            const grouped = !newDay && previous?.senderId === message.senderId;
            return (
              <Fragment key={message.id}>
                {newDay && (
                  <li className="py-2 text-center" aria-hidden>
                    <span className="rounded-full bg-ink-100 px-2.5 py-0.5 text-[11px] font-medium text-ink-600">{dayLabel(message.createdAt)}</span>
                  </li>
                )}
                <MessageRow
                  message={message}
                  mine={message.senderId === me}
                  me={me}
                  showSender={!grouped && chat?.type !== "Direct" && message.senderId !== me}
                  readCount={message.senderId === me ? othersRead(message) : 0}
                  isDirect={chat?.type === "Direct"}
                  canPin={detail?.canPin ?? false}
                  canDelete={message.senderId === me || (detail?.canModerate ?? false)}
                  onReply={() => setReplyTo(message)}
                  onReact={(emoji) => react.mutate({ id: message.id, emoji })}
                  onPin={() => pin.mutate({ id: message.id, pinned: !message.pinnedAt })}
                  onDelete={() => {
                    if (window.confirm("Xabar o'chirilsinmi?")) remove.mutate(message.id);
                  }}
                />
              </Fragment>
            );
          })}
        </ol>
      </div>

      {actionError && <div className="px-4 pb-2"><Alert>{errorText(actionError, "Amal bajarilmadi.")}</Alert></div>}

      {chat && !chat.canPost ? (
        <p className="border-t border-border bg-surface px-4 py-3 text-center text-[13px] text-ink-500">Bu kanalga faqat administratsiya yozadi.</p>
      ) : (
        <form
          className="border-t border-border bg-surface px-4 py-3"
          onSubmit={(e) => {
            e.preventDefault();
            submit();
          }}
        >
          {replyTo && (
            <div className="mb-2 flex items-center gap-2 rounded-md border-l-2 border-brand-500 bg-brand-50 px-2.5 py-1.5 text-[12.5px]">
              <Reply className="size-3.5 shrink-0 text-brand-600" aria-hidden />
              <span className="min-w-0 flex-1 truncate">
                <span className="font-medium text-ink-800">{replyTo.senderName}</span>
                <span className="text-ink-600"> — {replyTo.body ?? replyTo.attachment?.fileName ?? "Xabar"}</span>
              </span>
              <button type="button" onClick={() => setReplyTo(null)} className="rounded p-0.5 text-ink-500 hover:bg-brand-100" aria-label="Javobni bekor qilish">
                <X className="size-3.5" aria-hidden />
              </button>
            </div>
          )}
          {file && (
            <div className="mb-2 flex items-center gap-2 rounded-md bg-ink-50 px-2.5 py-1.5 text-[12.5px]">
              <Paperclip className="size-3.5 shrink-0 text-ink-500" aria-hidden />
              <span className="min-w-0 flex-1 truncate text-ink-800">{file.name}</span>
              <span className="shrink-0 text-ink-500">{formatSize(file.size)}</span>
              <button type="button" onClick={() => setFile(null)} className="rounded p-0.5 text-ink-500 hover:bg-ink-100" aria-label="Faylni olib tashlash">
                <X className="size-3.5" aria-hidden />
              </button>
            </div>
          )}
          {(fileError || send.isError) && <div className="mb-2"><Alert>{fileError ?? errorText(send.error, "Xabar yuborilmadi.")}</Alert></div>}

          <div className="flex items-end gap-2">
            <input
              ref={fileInput}
              type="file"
              accept={FILE_ACCEPT}
              className="hidden"
              onChange={(e) => {
                pickFile(e.target.files?.[0]);
                e.target.value = "";
              }}
            />
            <button type="button" onClick={() => fileInput.current?.click()} className="rounded-lg p-2.5 text-ink-500 hover:bg-ink-50 hover:text-ink-800" aria-label="Fayl biriktirish">
              <Paperclip className="size-4" aria-hidden />
            </button>
            <textarea
              value={body}
              rows={1}
              maxLength={4000}
              onChange={(e) => onBodyChange(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === "Enter" && !e.shiftKey) {
                  e.preventDefault();
                  submit();
                }
              }}
              placeholder="Xabar yozing…"
              aria-label="Xabar matni"
              className="max-h-40 min-h-10 flex-1 resize-none overflow-y-auto rounded-lg border border-border-strong bg-surface px-3 py-2 text-[14px] text-ink-900 placeholder:text-ink-400 focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-100 field-sizing-content"
            />
            <button
              type="submit"
              disabled={!canSend}
              className="rounded-lg bg-brand-solid p-2.5 text-white hover:bg-brand-solid-hover disabled:opacity-40"
              aria-label="Yuborish"
            >
              <SendHorizontal className="size-4" aria-hidden />
            </button>
          </div>
        </form>
      )}
    </section>
  );
}

function MessageRow({
  message, mine, me, showSender, readCount, isDirect, canPin, canDelete, onReply, onReact, onPin, onDelete,
}: {
  message: ChatMessage;
  mine: boolean;
  me: string | null;
  showSender: boolean;
  readCount: number;
  isDirect: boolean;
  canPin: boolean;
  canDelete: boolean;
  onReply: () => void;
  onReact: (emoji: string) => void;
  onPin: () => void;
  onDelete: () => void;
}) {
  const [picking, setPicking] = useState(false);

  return (
    <li className={cn("group flex w-full flex-col", mine ? "items-end" : "items-start", showSender && "pt-2")}>
      {showSender && (
        <span className="mb-0.5 px-1 text-[11.5px] font-medium text-ink-600">
          {message.senderName}
          {message.senderRole && <span className="font-normal text-ink-400"> · {roleLabel(message.senderRole)}</span>}
        </span>
      )}
      <div className={cn("flex max-w-[min(34rem,85%)] items-center gap-1", mine && "flex-row-reverse")}>
        <div
          className={cn(
            "min-w-0 rounded-2xl px-3 py-2 text-[14px] leading-relaxed",
            message.isDeleted
              ? "border border-dashed border-border-strong bg-transparent italic text-ink-400"
              : mine ? "bg-brand-solid text-white" : "border border-border bg-surface text-ink-900",
          )}
        >
          {message.replyTo && !message.isDeleted && (
            <div className={cn("mb-1.5 rounded-md border-l-2 px-2 py-1 text-[12px]", mine ? "border-white/60 bg-white/10 text-white/90" : "border-brand-500 bg-brand-50 text-ink-700")}>
              <span className="font-medium">{message.replyTo.senderName}</span>
              <p className="line-clamp-2">{message.replyTo.preview}</p>
            </div>
          )}
          {message.isDeleted ? (
            "Xabar o'chirildi"
          ) : (
            <>
              {message.attachment && <AttachmentView message={message} mine={mine} />}
              {message.body && <p className="whitespace-pre-wrap break-words">{message.body}</p>}
            </>
          )}
          <span className={cn("mt-0.5 flex items-center justify-end gap-1 text-[10.5px]", mine && !message.isDeleted ? "text-white/75" : "text-ink-400")}>
            {message.pinnedAt && <Pin className="size-3" aria-label="Qadalgan" />}
            {formatChatTime(message.createdAt)}
            {mine && !message.isDeleted && (readCount > 0
              ? <CheckCheck className="size-3.5" aria-label={isDirect ? "O'qildi" : `${readCount} kishi o'qidi`} />
              : <Check className="size-3.5" aria-label="Yuborildi" />)}
          </span>
        </div>

        {!message.isDeleted && (
          // Revealed on hover for a mouse; always shown where the main pointer is a finger, which has no hover to reveal them with.
          <div className="relative flex shrink-0 items-center gap-0.5 opacity-0 transition-opacity focus-within:opacity-100 group-hover:opacity-100 pointer-coarse:opacity-100">
            <ActionButton label="Javob berish" onClick={onReply}><Reply className="size-3.5" aria-hidden /></ActionButton>
            <ActionButton label="Reaksiya" onClick={() => setPicking((v) => !v)}><SmilePlus className="size-3.5" aria-hidden /></ActionButton>
            {canPin && (
              <ActionButton label={message.pinnedAt ? "Qadashni bekor qilish" : "Qadash"} onClick={onPin}>
                {message.pinnedAt ? <PinOff className="size-3.5" aria-hidden /> : <Pin className="size-3.5" aria-hidden />}
              </ActionButton>
            )}
            {canDelete && <ActionButton label="O'chirish" onClick={onDelete}><Trash2 className="size-3.5" aria-hidden /></ActionButton>}
            {picking && (
              <div className={cn("absolute bottom-full z-10 mb-1 flex gap-0.5 rounded-full border border-border bg-surface p-1 shadow-lg", mine ? "right-0" : "left-0")}>
                {CHAT_REACTIONS.map((emoji) => (
                  <button
                    key={emoji}
                    type="button"
                    onClick={() => {
                      setPicking(false);
                      onReact(emoji);
                    }}
                    className="rounded-full px-1.5 py-0.5 text-[16px] hover:bg-ink-100"
                    aria-label={`${emoji} reaksiyasi`}
                  >
                    {emoji}
                  </button>
                ))}
              </div>
            )}
          </div>
        )}
      </div>

      {message.reactions.length > 0 && (
        <div className={cn("mt-0.5 flex flex-wrap gap-1", mine && "justify-end")}>
          {message.reactions.map((reaction) => {
            const reacted = me !== null && reaction.userIds.includes(me);
            return (
              <button
                key={reaction.emoji}
                type="button"
                onClick={() => onReact(reaction.emoji)}
                aria-pressed={reacted}
                className={cn(
                  "rounded-full border px-1.5 py-0.5 text-[12px] leading-none",
                  reacted ? "border-brand-300 bg-brand-50 text-brand-800" : "border-border bg-surface text-ink-700 hover:bg-ink-50",
                )}
              >
                {reaction.emoji} <span className="font-tabular">{reaction.count}</span>
              </button>
            );
          })}
        </div>
      )}
    </li>
  );
}

function ActionButton({ label, onClick, children }: { label: string; onClick: () => void; children: React.ReactNode }) {
  return (
    <button type="button" onClick={onClick} title={label} aria-label={label} className="rounded-md p-1.5 text-ink-400 hover:bg-ink-100 hover:text-ink-800">
      {children}
    </button>
  );
}

function AttachmentView({ message, mine }: { message: ChatMessage; mine: boolean }) {
  const attachment = message.attachment!;
  const image = useQuery({
    queryKey: ["chats", "attachment", message.id],
    queryFn: async () => URL.createObjectURL(await fetchChatAttachment(message.chatId, message.id)),
    enabled: attachment.isImage,
    staleTime: Infinity,
    gcTime: 30 * 60_000,
  });

  if (attachment.isImage) {
    return image.data ? (
      <button type="button" onClick={() => window.open(image.data, "_blank", "noopener")} className="mb-1 block" aria-label={`${attachment.fileName} rasmini ochish`}>
        {/* eslint-disable-next-line @next/next/no-img-element -- authenticated blob URL, not an optimizable asset */}
        <img src={image.data} alt={attachment.fileName} className="max-h-64 rounded-lg object-contain" />
      </button>
    ) : (
      <Skeleton className="mb-1 h-40 w-56" />
    );
  }

  return (
    <button
      type="button"
      onClick={() => void downloadAttachment(message)}
      className={cn("mb-1 flex items-center gap-2 rounded-lg px-2.5 py-2 text-left text-[13px]", mine ? "bg-white/15 hover:bg-white/25" : "bg-ink-50 hover:bg-ink-100")}
    >
      <FileText className="size-5 shrink-0" aria-hidden />
      <span className="min-w-0">
        <span className="block truncate font-medium">{attachment.fileName}</span>
        <span className={cn("block text-[11.5px]", mine ? "text-white/75" : "text-ink-500")}>{formatSize(attachment.size)}</span>
      </span>
      <Download className="size-4 shrink-0" aria-hidden />
    </button>
  );
}
