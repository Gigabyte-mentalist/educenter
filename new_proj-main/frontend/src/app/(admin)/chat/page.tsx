"use client";

import { Suspense } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { MessagesSquare } from "lucide-react";
import { ChatList } from "@/components/chat/ChatList";
import { Conversation } from "@/components/chat/Conversation";
import { EmptyState } from "@/components/ui/EmptyState";
import { cn } from "@/lib/cn";

export default function ChatPage() {
  return (
    <Suspense fallback={null}>
      <ChatWorkspace />
    </Suspense>
  );
}

function ChatWorkspace() {
  const router = useRouter();
  const selectedId = useSearchParams().get("c");

  return (
    <main className="flex min-h-0 flex-1 overflow-hidden">
      {/* On narrow screens the list and the conversation take turns. */}
      <ChatList
        selectedId={selectedId}
        onSelect={(id) => router.replace(`/chat?c=${id}`)}
        className={cn("w-full md:w-80 md:shrink-0", selectedId ? "hidden md:flex" : "flex")}
      />
      <div className={cn("min-w-0 flex-1", selectedId ? "flex" : "hidden md:flex")}>
        {selectedId ? (
          <Conversation key={selectedId} chatId={selectedId} onBack={() => router.replace("/chat")} />
        ) : (
          <div className="flex flex-1 items-center justify-center">
            <EmptyState icon={MessagesSquare} title="Suhbatni tanlang" description="Chapdagi ro'yxatdan guruh, kanal yoki shaxsiy suhbatni oching." />
          </div>
        )}
      </div>
    </main>
  );
}
