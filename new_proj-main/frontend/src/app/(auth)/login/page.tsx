"use client";

import { useMutation } from "@tanstack/react-query";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { GraduationCap, ArrowRight, AlertCircle, ShieldAlert } from "lucide-react";
import { login } from "@/lib/api/auth";
import { ACCOUNT_BLOCKED_CODE } from "@/lib/account-status";
import { getCurrentUser, saveTokens } from "@/lib/auth-storage";
import { ApiError } from "@/lib/api-client";
import { homeForRole } from "@/lib/nav";
import { Button } from "@/components/ui/Button";
import { Input, Label } from "@/components/ui/Field";
import { BrandMark, useCachedBranding } from "@/components/OrganizationBrand";

export default function LoginPage() {
  const router = useRouter();
  // The centre's own name and logo as this browser last saw them; nobody is signed in yet to ask the server.
  const brand = useCachedBranding();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");

  const mutation = useMutation({
    mutationFn: () => login(email, password),
    onSuccess: (tokens) => {
      saveTokens(tokens);
      // A temporary password (fresh registration, or an administrator's reset) is good for nothing else until replaced.
      router.push(tokens.mustChangePassword ? "/change-password" : homeForRole(getCurrentUser()?.role));
    },
  });

  return (
    <main className="flex flex-1">
      <div className="relative hidden flex-1 flex-col justify-between overflow-hidden bg-brand-solid-hover p-12 text-white lg:flex">
        <div
          className="pointer-events-none absolute inset-0 opacity-[0.07]"
          style={{
            backgroundImage:
              "radial-gradient(circle at 1px 1px, white 1px, transparent 0)",
            backgroundSize: "28px 28px",
          }}
          aria-hidden
        />
        <div className="relative flex items-center gap-2.5">
          {brand?.logo ? (
            <BrandMark logo={brand.logo} className="size-9" />
          ) : (
            <span className="flex size-9 items-center justify-center rounded-lg bg-white/15">
              <GraduationCap className="size-5" aria-hidden />
            </span>
          )}
          <span className="font-display text-lg font-semibold">{brand?.name ?? "EduCenter"}</span>
        </div>

        <div className="relative max-w-md">
          <p className="font-display text-[28px] font-semibold leading-tight text-balance">
            Har bir o&apos;quvchi, har bir teacher, har bir filial — bitta tizimda.
          </p>
          <p className="mt-4 text-[15px] text-brand-100">
            CRM, o&apos;quv jarayoni, davomat, homework va imtihon natijalari — barchasi bir joyda.
          </p>
        </div>

        <div className="relative flex items-center gap-6 text-[13px] text-brand-200">
          <span>CRM</span>
          <span className="size-1 rounded-full bg-brand-400" />
          <span>LMS</span>
          <span className="size-1 rounded-full bg-brand-400" />
          <span>Analytics</span>
        </div>
      </div>

      <div className="flex flex-1 items-center justify-center p-8">
        <form
          className="w-full max-w-sm"
          onSubmit={(e) => {
            e.preventDefault();
            mutation.mutate();
          }}
        >
          <div className="mb-8 lg:hidden">
            <BrandMark logo={brand?.logo ?? null} className="size-9" />
          </div>

          <h1 className="text-[22px] font-semibold text-ink-900">Xush kelibsiz</h1>
          <p className="mt-1.5 text-sm text-ink-500">Hisobingizga kirish uchun ma&apos;lumotlarni kiriting.</p>

          <div className="mt-7 space-y-4">
            <div>
              <Label htmlFor="email">Email</Label>
              <Input
                id="email"
                type="email"
                autoComplete="username"
                required
                autoFocus
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                placeholder="siz@markaz.uz"
              />
            </div>

            <div>
              <Label htmlFor="password">Parol</Label>
              <Input
                id="password"
                type="password"
                autoComplete="current-password"
                required
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                placeholder="••••••••"
              />
              <Link href="/forgot-password" className="mt-2 inline-block text-[13px] text-brand-700 hover:underline">
                Parolni unutdingizmi?
              </Link>
            </div>
          </div>

          {mutation.isError && (() => {
            const isBlocked = mutation.error instanceof ApiError
              && (mutation.error.body as { code?: string } | undefined)?.code === ACCOUNT_BLOCKED_CODE;
            const message = mutation.error instanceof ApiError ? mutation.error.message : "Kirishda xatolik yuz berdi.";
            return (
              <div
                className={
                  isBlocked
                    ? "mt-4 flex items-start gap-2 rounded-lg bg-warning-50 px-3 py-2.5 text-[13px] text-warning-600"
                    : "mt-4 flex items-start gap-2 rounded-lg bg-danger-50 px-3 py-2.5 text-[13px] text-danger-600"
                }
              >
                {isBlocked ? <ShieldAlert className="mt-0.5 size-4 shrink-0" aria-hidden /> : <AlertCircle className="mt-0.5 size-4 shrink-0" aria-hidden />}
                <span>{isBlocked && <strong className="block">Hisobingiz bloklangan</strong>}{message}</span>
              </div>
            );
          })()}

          <Button type="submit" size="lg" loading={mutation.isPending} className="mt-6 w-full">
            Kirish
            {!mutation.isPending && <ArrowRight className="size-4" aria-hidden />}
          </Button>
        </form>
      </div>
    </main>
  );
}
