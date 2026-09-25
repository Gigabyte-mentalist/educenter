"use client";

import { type HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from "@microsoft/signalr";
import { useEffect, useSyncExternalStore } from "react";
import { ACCOUNT_BLOCKED_CODE, markAccountBlocked } from "./account-status";
import { apiUrl } from "./api-client";
import { getAccessToken } from "./auth-storage";

/** SignalR turns a server-thrown `HubException(ApiErrorCodes.AccountBlocked)` into a rejected invoke whose
 * message contains that code — there is no structured error payload over the wire, just text. */
const isAccountBlockedError = (error: unknown): boolean =>
  error instanceof Error && error.message.includes(ACCOUNT_BLOCKED_CODE);

// One shared connection for the whole admin shell: the sidebar badge and the chat page listen on the same socket.
let connection: HubConnection | null = null;
let starting: Promise<void> | null = null;
const listeners = new Set<() => void>();

const notify = () => listeners.forEach((listener) => listener());

function getConnection(): HubConnection {
  if (!connection) {
    const created = new HubConnectionBuilder()
      .withUrl(apiUrl("/hubs/chat"), {
        // Read on every (re)connect, so a token refreshed by a REST call is picked up.
        accessTokenFactory: () => getAccessToken() ?? "",
        withCredentials: false,
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();
    created.onreconnecting(notify);
    created.onreconnected(() => {
      notify();
      void syncChats();
    });
    created.onclose(notify);
    created.on("accountBlocked", markAccountBlocked);
    connection = created;
  }
  return connection;
}

/** Starts the connection if it is down (idempotent) and subscribes it to the user's chats. */
export function startChatConnection(): Promise<void> {
  const current = getConnection();
  if (current.state !== HubConnectionState.Disconnected) return starting ?? Promise.resolve();

  starting = current
    .start()
    .then(async () => {
      notify();
      await current.invoke("SyncChats");
    })
    .catch((error) => {
      if (isAccountBlockedError(error)) markAccountBlocked();
      notify();
    })
    .finally(() => {
      starting = null;
    });
  return starting;
}

/** Re-subscribes to the chats the user can see now — after the chat list changes (e.g. a new direct chat). */
export async function syncChats(): Promise<void> {
  if (connection?.state === HubConnectionState.Connected) {
    await connection.invoke("SyncChats").catch((error) => {
      if (isAccountBlockedError(error)) markAccountBlocked();
    });
  }
}

export function sendTyping(chatId: string): void {
  if (connection?.state === HubConnectionState.Connected) {
    void connection.invoke("Typing", chatId).catch((error) => {
      if (isAccountBlockedError(error)) markAccountBlocked();
    });
  }
}

export function stopChatConnection(): void {
  void connection?.stop();
  connection = null;
  notify();
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export function useChatConnectionState(): HubConnectionState {
  return useSyncExternalStore(
    subscribe,
    () => connection?.state ?? HubConnectionState.Disconnected,
    () => HubConnectionState.Disconnected,
  );
}

/** Listens to a server event while the component is mounted. Pass a stable handler (useCallback). */
export function useChatEvent<T>(event: string, handler: (payload: T) => void): void {
  useEffect(() => {
    const current = getConnection();
    current.on(event, handler);
    void startChatConnection();
    return () => current.off(event, handler);
  }, [event, handler]);
}
