"use client";

import { useEffect } from "react";
import { useRouter } from "next/navigation";
import { KeyRound } from "lucide-react";
import { logout } from "@/lib/api/auth";
import { clearPasswordChangeRequired } from "@/lib/account-status";
import { clearTokens, getCurrentUser, getRefreshToken } from "@/lib/auth-storage";
import { useAuthSession } from "@/lib/use-auth-session";
import { homeForRole } from "@/lib/nav";
import { ChangePasswordForm } from "@/components/ChangePasswordForm";

/**
 * Where a temporary password leads (S01.05). The account is signed in but the API refuses everything except
 * `change-password` until this form is done — so this page sits outside the `(admin)` shell, which would only
 * fire requests that all come back 403. The way out that is not "choose a password" is signing out, so nobody is
 * left stranded on a screen they cannot get past.
 */
export default function ChangePasswordPage() {
  const router = useRouter();
  const { hydrated, token } = useAuthSession();

  useEffect(() => {
    if (hydrated && !token) router.replace("/login");
  }, [hydrated, token, router]);

  const signOut = async () => {
    const refreshToken = getRefreshToken();
    if (refreshToken) await logout(refreshToken).catch(() => {});
    clearTokens();
    clearPasswordChangeRequired();
    router.replace("/login");
  };

  if (!hydrated || !token) return null;

  return (
    <main className="flex flex-1 items-center justify-center p-6">
      <div className="w-full max-w-sm rounded-2xl border border-border bg-surface p-6 shadow-sm">
        <span className="flex size-10 items-center justify-center rounded-lg bg-brand-solid text-white">
          <KeyRound className="size-5" aria-hidden />
        </span>
        <h1 className="mt-4 text-[20px] font-semibold text-ink-900">Yangi parol o&apos;rnating</h1>
        <p className="mt-1.5 text-sm text-ink-500">
          Hisobingiz vaqtinchalik parol bilan ochilgan. Davom etish uchun o&apos;zingizning parolingizni tanlang.
        </p>

        <div className="mt-6">
          <ChangePasswordForm onChanged={() => router.replace(homeForRole(getCurrentUser()?.role))} />
        </div>

        <button
          type="button"
          onClick={signOut}
          className="mt-4 w-full text-center text-[13px] text-ink-500 underline-offset-2 hover:text-ink-700 hover:underline"
        >
          Boshqa hisob bilan kirish
        </button>
      </div>
    </main>
  );
}
