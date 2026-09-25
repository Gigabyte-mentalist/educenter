"use client";

import { useMutation } from "@tanstack/react-query";
import Link from "next/link";
import { useState } from "react";
import { ArrowLeft, CheckCircle2, KeyRound, MailCheck } from "lucide-react";
import { requestPasswordReset, resetPassword } from "@/lib/api/auth";
import { ApiError } from "@/lib/api-client";
import { Alert } from "@/components/ui/Alert";
import { Button } from "@/components/ui/Button";
import { Input, Label } from "@/components/ui/Field";

const MIN_PASSWORD_LENGTH = 8;

/**
 * "Parolni unutdim" (S06.04): the email or phone number, then the six-digit code that arrives there and a new password. The first step is
 * always followed by the second, whether or not the account exists — the message never says which, so the screen cannot be used to find out
 * who has an account (a blocked person is not left waiting for a code either: the text says only "if the account exists").
 */
export default function ForgotPasswordPage() {
  const [identifier, setIdentifier] = useState("");
  const [step, setStep] = useState<"identify" | "reset" | "done">("identify");
  const [code, setCode] = useState("");
  const [password, setPassword] = useState("");
  const [repeat, setRepeat] = useState("");

  const request = useMutation({
    mutationFn: () => requestPasswordReset(identifier.trim()),
    onSuccess: () => setStep("reset"),
  });

  const reset = useMutation({
    mutationFn: () => resetPassword(identifier.trim(), code.replace(/\D/g, ""), password),
    onSuccess: () => setStep("done"),
  });

  const mismatch = repeat !== "" && repeat !== password;
  const canReset = code.replace(/\D/g, "").length === 6 && password.length >= MIN_PASSWORD_LENGTH && password === repeat;

  return (
    <main className="flex flex-1 items-center justify-center p-6">
      <div className="w-full max-w-sm rounded-2xl border border-border bg-surface p-6 shadow-sm">
        <span className="flex size-10 items-center justify-center rounded-lg bg-brand-solid text-white">
          {step === "done" ? <CheckCircle2 className="size-5" aria-hidden /> : step === "reset" ? <MailCheck className="size-5" aria-hidden /> : <KeyRound className="size-5" aria-hidden />}
        </span>

        {step === "identify" && (
          <form className="mt-4" onSubmit={(e) => { e.preventDefault(); request.mutate(); }}>
            <h1 className="text-[20px] font-semibold text-ink-900">Parolni tiklash</h1>
            <p className="mt-1.5 text-sm text-ink-500">
              Hisobingizga bog&apos;langan email yoki telefon raqamini kiriting — unga 6 raqamli tasdiqlash kodi yuboramiz.
            </p>
            <div className="mt-6">
              <Label htmlFor="identifier">Email yoki telefon</Label>
              <Input
                id="identifier"
                autoComplete="username"
                required
                autoFocus
                value={identifier}
                onChange={(e) => setIdentifier(e.target.value)}
                placeholder="siz@markaz.uz yoki 90 123 45 67"
              />
            </div>
            {request.isError && (
              <div className="mt-4"><Alert>{request.error instanceof ApiError ? request.error.message : "So'rovni yuborib bo'lmadi."}</Alert></div>
            )}
            <Button type="submit" size="lg" loading={request.isPending} className="mt-6 w-full">Kod yuborish</Button>
          </form>
        )}

        {step === "reset" && (
          <form className="mt-4" onSubmit={(e) => { e.preventDefault(); if (canReset) reset.mutate(); }}>
            <h1 className="text-[20px] font-semibold text-ink-900">Kodni kiriting</h1>
            <p className="mt-1.5 text-sm text-ink-500">
              Agar hisob mavjud bo&apos;lsa, kod yuborildi. U 15 daqiqa amal qiladi; kelmasa, spam jildini tekshiring yoki qayta yuboring.
            </p>
            <div className="mt-6 space-y-4">
              <div>
                <Label htmlFor="code">Tasdiqlash kodi</Label>
                <Input
                  id="code"
                  inputMode="numeric"
                  autoComplete="one-time-code"
                  pattern="[0-9 ]*"
                  maxLength={7}
                  required
                  autoFocus
                  value={code}
                  onChange={(e) => setCode(e.target.value)}
                  placeholder="123456"
                  className="font-mono tracking-widest"
                />
              </div>
              <div>
                <Label htmlFor="new-password">Yangi parol</Label>
                <Input
                  id="new-password"
                  type="password"
                  autoComplete="new-password"
                  required
                  minLength={MIN_PASSWORD_LENGTH}
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                />
                <p className="mt-1.5 text-[12px] text-ink-500">Kamida {MIN_PASSWORD_LENGTH} belgi.</p>
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
                {mismatch && <p className="mt-1.5 text-[12px] text-danger-600">Parollar bir xil emas.</p>}
              </div>
            </div>
            {reset.isError && (
              <div className="mt-4"><Alert>{reset.error instanceof ApiError ? reset.error.message : "Parolni o'zgartirib bo'lmadi."}</Alert></div>
            )}
            <Button type="submit" size="lg" loading={reset.isPending} disabled={!canReset} className="mt-6 w-full">Parolni o&apos;zgartirish</Button>
            <button
              type="button"
              disabled={request.isPending}
              onClick={() => request.mutate()}
              className="mt-4 w-full text-center text-[13px] text-ink-500 underline-offset-2 hover:text-ink-700 hover:underline disabled:opacity-50"
            >
              Kodni qayta yuborish
            </button>
            {request.isSuccess && !request.isPending && <p className="mt-2 text-center text-[12px] text-ink-500">Kod qayta yuborildi.</p>}
          </form>
        )}

        {step === "done" && (
          <div className="mt-4">
            <h1 className="text-[20px] font-semibold text-ink-900">Parol o&apos;zgartirildi</h1>
            <p className="mt-1.5 text-sm text-ink-500">Boshqa qurilmalardagi seanslar yopildi. Yangi parol bilan kiring.</p>
            <Link href="/login" className="mt-6 block">
              <Button size="lg" className="w-full">Kirish</Button>
            </Link>
          </div>
        )}

        {step !== "done" && (
          <Link href="/login" className="mt-4 inline-flex items-center gap-1.5 text-[13px] text-ink-500 hover:text-ink-800">
            <ArrowLeft className="size-3.5" aria-hidden />
            Kirish sahifasiga qaytish
          </Link>
        )}
      </div>
    </main>
  );
}
