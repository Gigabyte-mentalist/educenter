"use client";

import { use } from "react";
import { BranchDashboard } from "@/components/dashboard/BranchDashboard";

/**
 * A single branch's own dashboard — reached by clicking a branch name anywhere in the product (Filiallar list, the organization
 * dashboard's ranking table, search). The backend scopes /api/dashboard/branch/{id} to what the caller may see and answers 404/403
 * for a branch that does not exist or is not theirs, which the dashboard shows as its error.
 */
export default function BranchDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  return <BranchDashboard branchId={id} back={{ href: "/branches", label: "Filiallar" }} />;
}
