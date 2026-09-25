"use client";

import { UserDirectory } from "@/components/users/UserDirectory";
import { STAFF_ROLE_CODES } from "@/lib/api/users";

export default function StaffPage() {
  return (
    <UserDirectory
      audience="staff"
      title="Xodimlar"
      description="Markazda ishlaydigan odamlar — administrator, operator, o'qituvchi va support."
      roleOptions={STAFF_ROLE_CODES}
      defaultRole="OPERATOR"
    />
  );
}
