"use client";

import { useEffect, useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { BookOpen, History, MessageSquarePlus, Send, Sparkles, Trash2 } from "lucide-react";
import {
  askTutor, deleteTutorConversation, getAiStatus, listTutorConversations, listTutorMessages,
  type TutorCitation, type TutorConversation, type TutorMessage,
} from "@/lib/api/ai";
import { ApiError } from "@/lib/api-client";
import { Markdown } from "@/components/ai/Markdown";
import { PageHeader } from "@/components/ui/PageHeader";
import { Alert } from "@/components/ui/Alert";
import { Button } from "@/components/ui/Button";
import { Card, CardBody } from "@/components/ui/Card";
import { Drawer } from "@/components/ui/Drawer";
import { EmptyState } from "@/components/ui/EmptyState";
import { Textarea } from "@/components/ui/Field";
import { Skeleton } from "@/components/ui/Skeleton";
import { cn } from "@/lib/cn";
import { formatDateTime } from "@/lib/format-date";

const errorText = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

/** Below this the answer feels instant, and a typing indicator would only flash. */
const SlowAnswerAfterMs = 1500;

export default function AiTutorPage() {
  const client = useQueryClient();
  const [selectedId, setSelectedId] = useState<string | null>(null);
  /** Set only by "Yangi suhbat" — otherwise the newest thread reopens, so a refresh never looks like lost history. */
  const [startingNew, setStartingNew] = useState(false);
  const [historyOpen, setHistoryOpen] = useState(false);
  const [question, setQuestion] = useState("");
  /** The question already sent, shown in the thread while the answer is still being written. */
  const [pending, setPending] = useState<string | null>(null);
  const [slow, setSlow] = useState(false);
  const slowTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const endRef = useRef<HTMLDivElement>(null);

  const statusQuery = useQuery({ queryKey: ["ai", "status"], queryFn: getAiStatus });
  const conversationsQuery = useQuery({ queryKey: ["ai", "tutor", "conversations"], queryFn: listTutorConversations });

  // Conversations are ordered newest first, so with nothing picked the last thread is the one that opens.
  const conversationId = startingNew ? null : selectedId ?? conversationsQuery.data?.[0]?.id ?? null;

  const messagesQuery = useQuery({
    queryKey: ["ai", "tutor", "messages", conversationId],
    queryFn: () => listTutorMessages(conversationId!),
    enabled: conversationId !== null,
  });

  const ask = useMutation({
    mutationFn: (text: string) => askTutor({ conversationId: conversationId ?? undefined, question: text }),
    // The composer empties the moment the question is sent, and the question appears in the thread instead.
    onMutate: (text) => {
      setPending(text);
      setQuestion("");
      slowTimer.current = setTimeout(() => setSlow(true), SlowAnswerAfterMs);
    },
    onSuccess: (answer) => {
      setSelectedId(answer.conversationId);
      setStartingNew(false);
      client.invalidateQueries({ queryKey: ["ai", "tutor"] });
      client.invalidateQueries({ queryKey: ["ai", "status"] });
    },
    onError: (_error, text) => {
      setPending(null);
      setQuestion(text); // nothing typed is ever lost
    },
    onSettled: () => {
      if (slowTimer.current) clearTimeout(slowTimer.current);
      setSlow(false);
    },
  });

  useEffect(() => () => { if (slowTimer.current) clearTimeout(slowTimer.current); }, []);

  useEffect(() => {
    endRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messagesQuery.data, pending, slow]);

  const send = () => {
    const text = question.trim();
    if (text && !ask.isPending) ask.mutate(text);
  };

  const tutor = statusQuery.data?.features.find((f) => f.feature === "Tutor");

  if (statusQuery.isLoading) return <main className="p-4 sm:p-6 lg:p-8"><Skeleton className="h-96" /></main>;
  if (!tutor?.allowed) {
    return (
      <main className="p-4 sm:p-6 lg:p-8">
        <div className="rounded-xl border border-border bg-surface">
          <EmptyState icon={Sparkles} title="AI Tutor faqat studentlar uchun" description="O'qituvchilar bilim bazasini «Bilim bazasi» sahifasida boshqaradi." />
        </div>
      </main>
    );
  }

  const messages = messagesQuery.data ?? [];
  // The optimistic bubble stays up until the refetched thread actually contains that question — derived rather than
  // cleared in an effect, so the question never blinks out and back in as the server copy arrives.
  const showPending = pending !== null && !messages.some((m) => m.role === "User" && m.content.trim() === pending.trim());
  const showEmptyState = conversationId === null && !showPending;
  const conversations = conversationsQuery.data ?? [];

  return (
    <main className="flex h-[calc(100vh-1px)] flex-col p-4 sm:p-6 lg:p-8">
      <PageHeader
        title="AI Tutor"
        description="Ingliz tili bo'yicha savol bering — javob markazingiz materiallariga tayanadi va manbalari ko'rsatiladi."
        actions={
          <div className="flex items-center gap-3">
            {tutor.limit !== null && (
              <span className="text-[12.5px] text-ink-500">Bugun qoldi: <b className="font-tabular text-ink-800">{tutor.remaining}</b> / {tutor.limit}</span>
            )}
            <Button variant="ghost" size="sm" onClick={() => setHistoryOpen(true)} disabled={conversations.length === 0}>
              <History className="size-4" aria-hidden />
              Avvalgi suhbatlar
              {conversations.length > 0 && <span className="text-ink-400">({conversations.length})</span>}
            </Button>
            <Button
              variant="secondary"
              size="sm"
              onClick={() => { setStartingNew(true); setSelectedId(null); setPending(null); }}
              disabled={ask.isPending}
            >
              <MessageSquarePlus className="size-4" aria-hidden />
              Yangi suhbat
            </Button>
          </div>
        }
      />

      {statusQuery.data?.demoMode && (
        <div className="mb-4"><Alert tone="info">Demo rejim: haqiqiy AI provayder ulanmagan, javoblar sinov uchun yaratiladi.</Alert></div>
      )}

      <Card className="flex min-h-0 flex-1 flex-col overflow-hidden">
        <div className="min-h-0 flex-1 space-y-4 overflow-y-auto p-5" aria-live="polite">
          {showEmptyState && (
            <EmptyState
              icon={Sparkles}
              title="Savolingizni yozing"
              description="Masalan: «Present perfect va past simple farqi nima?» yoki «IELTS Writing Task 2 da qanday kirish yozaman?»"
            />
          )}
          {messagesQuery.isLoading && conversationId !== null && <Skeleton className="h-32" />}
          {messages.map((message) => <MessageBubble key={message.id} message={message} />)}
          {showPending && <QuestionBubble content={pending!} />}
          {ask.isPending && slow && <TypingBubble />}
          <div ref={endRef} />
        </div>

        <CardBody className="border-t border-border">
          {ask.isError && <div className="mb-3"><Alert>{errorText(ask.error, "Javob olib bo'lmadi.")}</Alert></div>}
          <form className="flex items-end gap-2" onSubmit={(e) => { e.preventDefault(); send(); }}>
            <Textarea
              rows={2}
              value={question}
              maxLength={1500}
              onChange={(e) => setQuestion(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === "Enter" && !e.shiftKey) {
                  e.preventDefault();
                  send();
                }
              }}
              placeholder="Savolingizni yozing… (Enter — yuborish, Shift+Enter — yangi qator)"
              className="min-h-0"
            />
            <Button type="submit" loading={ask.isPending} disabled={!question.trim()}>
              <Send className="size-4" aria-hidden />
            </Button>
          </form>
        </CardBody>
      </Card>

      <Drawer
        open={historyOpen}
        onClose={() => setHistoryOpen(false)}
        title="Avvalgi suhbatlar"
        description="Suhbatni tanlang — yozishmalar saqlanib qolgan"
      >
        {conversations.length === 0 && <EmptyState icon={MessageSquarePlus} title="Suhbatlar yo'q" />}
        <ul className="divide-y divide-border">
          {conversations.map((conversation) => (
            <ConversationRow
              key={conversation.id}
              conversation={conversation}
              active={conversation.id === conversationId}
              onOpen={() => {
                setSelectedId(conversation.id);
                setStartingNew(false);
                setPending(null);
                setHistoryOpen(false);
              }}
              onDeleted={() => { if (conversation.id === conversationId) setSelectedId(null); }}
            />
          ))}
        </ul>
      </Drawer>
    </main>
  );
}

function ConversationRow({
  conversation,
  active,
  onOpen,
  onDeleted,
}: {
  conversation: TutorConversation;
  active: boolean;
  onOpen: () => void;
  onDeleted: () => void;
}) {
  const client = useQueryClient();
  const remove = useMutation({
    mutationFn: () => deleteTutorConversation(conversation.id),
    onSuccess: () => {
      onDeleted();
      client.invalidateQueries({ queryKey: ["ai", "tutor"] });
    },
  });

  return (
    <li className="group flex items-center gap-1">
      <button
        onClick={onOpen}
        className={cn("min-w-0 flex-1 rounded-lg px-3 py-3 text-left transition-colors hover:bg-ink-50", active && "bg-brand-50 hover:bg-brand-50")}
      >
        <p className="truncate text-[13.5px] font-medium text-ink-900">{conversation.title}</p>
        <p className="text-[12px] text-ink-500">
          {conversation.messageCount} savol · {formatDateTime(conversation.lastMessageAt)}
        </p>
      </button>
      <button
        onClick={() => remove.mutate()}
        className="shrink-0 rounded-md p-1.5 text-ink-400 transition-colors hover:bg-ink-100 hover:text-danger-600"
        aria-label="Suhbatni o'chirish"
      >
        <Trash2 className="size-3.5" aria-hidden />
      </button>
    </li>
  );
}

function MessageBubble({ message }: { message: TutorMessage }) {
  if (message.role === "User") return <QuestionBubble content={message.content} />;

  return (
    <div className="flex animate-message-in justify-start">
      <div className="max-w-[75%] rounded-xl bg-ink-50 px-4 py-3 text-ink-800">
        <Markdown content={message.content} />
        {message.citations.length > 0 && <Citations citations={message.citations} />}
      </div>
    </div>
  );
}

function QuestionBubble({ content }: { content: string }) {
  return (
    <div className="flex animate-message-in justify-end">
      <div className="max-w-[75%] whitespace-pre-wrap rounded-xl bg-brand-solid px-4 py-3 text-[13.5px] leading-relaxed text-white">
        {content}
      </div>
    </div>
  );
}

function TypingBubble() {
  return (
    <div className="flex animate-message-in justify-start">
      <div className="flex items-center gap-1.5 rounded-xl bg-ink-50 px-4 py-3.5" role="status" aria-label="AI Tutor javob yozmoqda">
        {[0, 1, 2].map((i) => (
          <span
            key={i}
            className="size-1.5 animate-typing-dot rounded-full bg-ink-400"
            style={{ animationDelay: `${i * 0.15}s` }}
          />
        ))}
      </div>
    </div>
  );
}

function Citations({ citations }: { citations: TutorCitation[] }) {
  return (
    <div className="mt-3 border-t border-border pt-2">
      <p className="mb-1.5 flex items-center gap-1.5 text-[12px] font-medium text-ink-500">
        <BookOpen className="size-3.5" aria-hidden />
        Manbalar
      </p>
      <ul className="flex flex-wrap gap-1.5">
        {citations.map((citation) => (
          <li key={`${citation.documentId}-${citation.ordinal}`} className="rounded-full bg-surface px-2.5 py-0.5 text-[12px] text-ink-600">
            {citation.title}
          </li>
        ))}
      </ul>
    </div>
  );
}
