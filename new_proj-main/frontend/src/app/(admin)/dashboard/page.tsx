"use client";

import { useAuthSession } from "@/lib/use-auth-session";
import { OrganizationDashboard } from "@/components/dashboard/OrganizationDashboard";
import { TeacherDashboard } from "@/components/dashboard/TeacherDashboard";
import { StudentDashboard } from "@/components/dashboard/StudentDashboard";
import { ParentDashboard } from "@/components/dashboard/ParentDashboard";
import { SystemDashboard } from "@/components/dashboard/SystemDashboard";
import { Skeleton } from "@/components/ui/Skeleton";

/**
 * One URL, eight landing pages. The role picks the component; the server picks how much of the organization
 * each one is allowed to see, so /dashboard is safe to link to from anywhere without asking who is looking.
 */
export default function DashboardPage() {
  const { hydrated, user } = useAuthSession();

  if (!hydrated)
    return (
      <main className="p-4 sm:p-6 lg:p-8">
        <Skeleton className="h-48" />
      </main>
    );

  switch (user?.role) {
    case "SUPER_ADMIN":
      return (
        <OrganizationDashboard
          title="Bosh sahifa"
          description="Butun tashkilot bo'yicha ko'rsatkichlar va filiallar reytingi."
        />
      );
    case "ADMINISTRATOR":
    case "OPERATOR":
      return (
        <OrganizationDashboard
          title="Filial ko'rsatkichlari"
          description="Sizga biriktirilgan filiallar bo'yicha o'quvchilar, moliya va konversiya."
        />
      );
    case "TEACHER":
    case "SUPPORT_TEACHER":
      return <TeacherDashboard />;
    case "STUDENT":
      return <StudentDashboard />;
    case "PARENT":
      return <ParentDashboard />;
    case "DEVELOPER":
      return <SystemDashboard />;
    default:
      return (
        <main className="p-4 sm:p-6 lg:p-8">
          <p className="text-[13.5px] text-ink-500">
            Bu rol uchun bosh sahifa sozlanmagan. Administrator bilan bog&apos;laning.
          </p>
        </main>
      );
  }
}
