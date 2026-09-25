"use client";

import { UserDirectory } from "@/components/users/UserDirectory";
import { ROLE_CODES } from "@/lib/api/users";

/**
 * Every login in the organization, students and parents included. Separate from /users because that screen
 * answers "who works here" — this one answers "who can sign in", which is the question behind blocking an
 * account or deleting one.
 */
export default function AccountsPage() {
  return (
    <UserDirectory
      audience="all"
      title="Foydalanuvchi hisoblari"
      description="Tizimga kira oladigan barcha hisoblar — xodimlar, o'quvchilar va ota-onalar."
      roleOptions={ROLE_CODES}
      defaultRole="PARENT"
    />
  );
}
