"use client";

import Link from "next/link";
import { useQuery } from "@tanstack/react-query";
import { Award } from "lucide-react";
import { getTeacherPortfolio } from "@/lib/api/teachers";
import { useAuthSession } from "@/lib/use-auth-session";
import { usePermissions } from "@/lib/use-permissions";

/** A small nudge on the teacher's home screen while their portfolio is still empty; it disappears as soon as anything is filled in. */
export function PortfolioReminder() {
  const userId = useAuthSession().user?.id;
  const { can } = usePermissions();
  const query = useQuery({
    queryKey: ["teachers", userId, "portfolio"],
    queryFn: () => getTeacherPortfolio(userId!),
    enabled: !!userId && can("teacher.portfolio.view"),
  });
  const p = query.data;
  if (!p) return null;
  const empty = !p.profile.bio && !p.profile.specialization && p.profile.yearsOfExperience === null
    && p.credentials.length === 0 && p.experience.length === 0;
  if (!empty) return null;

  return (
    <Link href={`/teachers/${userId}`} className="mb-4 flex items-center gap-3 rounded-xl border border-brand-200 bg-brand-50 px-4 py-3 text-[13.5px] text-brand-800">
      <Award className="size-4 shrink-0" aria-hidden />
      <span><b>Portfolioingizni to‘ldiring</b> — IELTS balli, sertifikatlar va tajribangizni qo‘shing; administrator tasdiqlagach sarlavhangizda ko‘rinadi.</span>
    </Link>
  );
}
