"use client";

import { useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { MessageCircle } from "lucide-react";
import { openDirectChat } from "@/lib/api/chat";
import { ApiError } from "@/lib/api-client";
import { Button } from "@/components/ui/Button";

/** Opens (or creates) a direct chat with the given user and jumps straight to it — the profile-page half of
 * Teacher/Student 360°'s Communication tab (docs/00_TZ.md §6-7, audit §3.6/§3.7). Whether the caller is
 * actually allowed to message this person is the API's call (ChatAccess.ContactIdsAsync), not this button's —
 * it just surfaces the error inline if not. */
export function OpenChatButton({ userId, label = "Xabar yozish" }: { userId: string; label?: string }) {
  const router = useRouter();
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: () => openDirectChat(userId),
    onSuccess: (chat) => router.push(`/chat?c=${chat.id}`),
    onError: (err) => setError(err instanceof ApiError ? err.message : "Suhbatni ochib bo'lmadi."),
  });

  return (
    <div className="flex flex-col items-end gap-1">
      <Button size="sm" variant="secondary" loading={mutation.isPending} onClick={() => { setError(null); mutation.mutate(); }}>
        <MessageCircle className="size-3.5" aria-hidden />
        {label}
      </Button>
      {error && <p className="text-[12px] text-danger-600">{error}</p>}
    </div>
  );
}
