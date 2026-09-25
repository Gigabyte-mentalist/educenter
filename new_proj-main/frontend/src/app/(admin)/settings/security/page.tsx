"use client";

import { useState } from "react";
import { PageHeader } from "@/components/ui/PageHeader";
import { Alert } from "@/components/ui/Alert";
import { ChangePasswordForm } from "@/components/ChangePasswordForm";

export default function SecuritySettingsPage() {
  const [changed, setChanged] = useState(false);

  return (
    <main className="p-4 sm:p-6 lg:p-8">
      <PageHeader
        title="Xavfsizlik"
        description="Parolingizni o'zgartiring. Boshqa qurilmalardagi sessiyalar tugatiladi, bu qurilmada ishlashda davom etasiz."
      />

      <div className="max-w-md space-y-4">
        {changed && <Alert tone="success">Parol o&apos;zgartirildi. Boshqa qurilmalardagi sessiyalar tugatildi.</Alert>}
        <div className="rounded-xl border border-border bg-surface p-5">
          <ChangePasswordForm onChanged={() => setChanged(true)} />
        </div>
      </div>
    </main>
  );
}
