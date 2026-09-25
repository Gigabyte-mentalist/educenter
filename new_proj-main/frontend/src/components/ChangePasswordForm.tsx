"use client";

import { useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { changePassword } from "@/lib/api/auth";
import { clearPasswordChangeRequired } from "@/lib/account-status";
import { saveTokens } from "@/lib/auth-storage";
import { ApiError } from "@/lib/api-client";
import { Alert } from "@/components/ui/Alert";
import { Button } from "@/components/ui/Button";
import { FieldError, Input, Label } from "@/components/ui/Field";

/** `PasswordPolicy.MinLength` on the API — checked here too so a typo is caught before a round trip. */
const MIN_LENGTH = 8;

/**
 * The one form behind both ways to change a password: the forced screen a temporary password leads to, and the
 * everyday "Xavfsizlik" page. The server signs every other device out; the pair it sends back is saved here so this
 * session carries on without logging in again.
 */
export function ChangePasswordForm({ onChanged }: { onChanged: () => void }) {
  const [current, setCurrent] = useState("");
  const [next, setNext] = useState("");
  const [repeat, setRepeat] = useState("");

  const mutation = useMutation({
    mutationFn: () => changePassword(current, next),
    onSuccess: (tokens) => {
      saveTokens({ accessToken: tokens.accessToken, refreshToken: tokens.refreshToken });
      clearPasswordChangeRequired(); // the admin layout, if it sent the holder here, may stop holding them back
      setCurrent("");
      setNext("");
      setRepeat("");
      onChanged();
    },
  });

  const problem =
    next.length > 0 && next.length < MIN_LENGTH ? `Parol kamida ${MIN_LENGTH} belgidan iborat bo'lishi kerak.`
    : next.length > 0 && next === current ? "Yangi parol joriy paroldan farq qilsin."
    : repeat.length > 0 && repeat !== next ? "Parollar bir xil emas."
    : null;
  const ready = current.length > 0 && next.length >= MIN_LENGTH && next !== current && repeat === next;

  return (
    <form
      className="space-y-4"
      onSubmit={(e) => {
        e.preventDefault();
        if (ready) mutation.mutate();
      }}
    >
      <div>
        <Label htmlFor="current-password">Joriy parol</Label>
        <Input
          id="current-password"
          type="password"
          autoComplete="current-password"
          required
          autoFocus
          value={current}
          onChange={(e) => setCurrent(e.target.value)}
        />
      </div>
      <div>
        <Label htmlFor="new-password">Yangi parol</Label>
        <Input
          id="new-password"
          type="password"
          autoComplete="new-password"
          required
          value={next}
          onChange={(e) => setNext(e.target.value)}
        />
      </div>
      <div>
        <Label htmlFor="repeat-password">Yangi parolni takrorlang</Label>
        <Input
          id="repeat-password"
          type="password"
          autoComplete="new-password"
          required
          value={repeat}
          onChange={(e) => setRepeat(e.target.value)}
        />
        <FieldError>{problem}</FieldError>
      </div>

      {mutation.isError && (
        <Alert>{mutation.error instanceof ApiError ? mutation.error.message : "Parolni o'zgartirib bo'lmadi."}</Alert>
      )}

      <Button type="submit" loading={mutation.isPending} disabled={!ready} className="w-full">
        Parolni saqlash
      </Button>
    </form>
  );
}
