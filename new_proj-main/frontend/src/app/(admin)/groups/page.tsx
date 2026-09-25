"use client";

import Link from "next/link";
import { Suspense, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { UsersRound, Plus, BookOpen, UserPlus, Users, ArrowRight, Trash2 } from "lucide-react";
import { listBranches } from "@/lib/api/branches";
import { assignGroupStaff, createCourse, createGroup, listCourses, listGroups, type Group } from "@/lib/api/academic";
import { listUsers } from "@/lib/api/users";
import { ApiError } from "@/lib/api-client";
import { useAuthSession } from "@/lib/use-auth-session";
import { canManageAcademic } from "@/lib/nav";
import { usePermissions } from "@/lib/use-permissions";
import { useHighlightTarget, highlightElementId, highlightClassName } from "@/lib/use-highlight-target";
import { useNewParam } from "@/lib/use-new-param";
import { CourseFees } from "@/components/CourseFees";
import { GroupDeleteDialog } from "@/components/GroupDeleteDialog";
import { PageHeader } from "@/components/ui/PageHeader";
import { Button } from "@/components/ui/Button";
import { Badge } from "@/components/ui/Badge";
import { Card, CardBody, CardHeader, CardTitle } from "@/components/ui/Card";
import { Skeleton } from "@/components/ui/Skeleton";
import { EmptyState } from "@/components/ui/EmptyState";
import { Alert } from "@/components/ui/Alert";
import { Drawer } from "@/components/ui/Drawer";
import { Input, Label, Select } from "@/components/ui/Field";

const STATUS_LABEL: Record<Group["status"], string> = {
  Forming: "Shakllanmoqda",
  Active: "Faol",
  Completed: "Tugallangan",
  Cancelled: "Bekor qilingan",
};
const STATUS_TONE: Record<Group["status"], "neutral" | "success" | "brand" | "danger"> = {
  Forming: "neutral",
  Active: "success",
  Completed: "brand",
  Cancelled: "danger",
};

export default function GroupsPage() {
  return (
    <Suspense fallback={null}>
      <GroupsPageContent />
    </Suspense>
  );
}

function GroupsPageContent() {
  const queryClient = useQueryClient();
  // Only Super Admin / Administrator may create courses & groups or staff them; everyone else gets a
  // read-only view and we don't fire the lookups (branches, teacher list) their role can't call.
  const canManage = canManageAcademic(useAuthSession().user?.role);
  const canDelete = usePermissions().can("groups.delete");
  const groupsQuery = useQuery({ queryKey: ["groups"], queryFn: listGroups });
  const highlightId = useHighlightTarget(groupsQuery.isSuccess);
  const coursesQuery = useQuery({ queryKey: ["courses"], queryFn: listCourses });
  const branchesQuery = useQuery({ queryKey: ["branches"], queryFn: listBranches, enabled: canManage });
  const teachersQuery = useQuery({ queryKey: ["users", "TEACHER"], queryFn: () => listUsers({ role: "TEACHER", size: 200 }), enabled: canManage });
  // Support teachers staff groups too — without them a group's "Yordamchi" badge can only show an id.
  const supportQuery = useQuery({ queryKey: ["users", "SUPPORT_TEACHER"], queryFn: () => listUsers({ role: "SUPPORT_TEACHER", size: 200 }), enabled: canManage });
  const staffCandidates = [...(teachersQuery.data?.items ?? []), ...(supportQuery.data?.items ?? [])];

  const [search, setSearch] = useState("");
  const [courseDrawerOpen, setCourseDrawerOpen] = useState(false);
  const [groupDrawerOpen, setGroupDrawerOpen] = useState(false);
  // `?new=1` (the command palette's "Yangi guruh") opens the form too, for whoever may create.
  const newParam = useNewParam();
  const showGroupDrawer = groupDrawerOpen || (newParam.requested && canManage);
  const [staffTargetGroup, setStaffTargetGroup] = useState<Group | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<Group | null>(null);

  const [courseName, setCourseName] = useState("");
  const [levelCategory, setLevelCategory] = useState("IELTS");
  const [monthlyFee, setMonthlyFee] = useState("");
  const [groupName, setGroupName] = useState("");
  const [capacity, setCapacity] = useState(12);
  const [branchId, setBranchId] = useState("");
  const [courseId, setCourseId] = useState("");
  const [staffUserId, setStaffUserId] = useState("");
  const [staffRole, setStaffRole] = useState<"MainTeacher" | "SupportTeacher">("MainTeacher");

  const createCourseMutation = useMutation({
    mutationFn: () => createCourse({ name: courseName, levelCategory, monthlyFee: Number(monthlyFee.replace(/\s/g, "")) || 0 }),
    onSuccess: () => {
      setCourseName("");
      setMonthlyFee("");
      setCourseDrawerOpen(false);
      queryClient.invalidateQueries({ queryKey: ["courses"] });
    },
  });

  const createGroupMutation = useMutation({
    mutationFn: () => createGroup({ branchId, courseId, name: groupName, capacity }),
    onSuccess: () => {
      setGroupName("");
      setGroupDrawerOpen(false);
      newParam.clear();
      queryClient.invalidateQueries({ queryKey: ["groups"] });
    },
  });

  const assignStaffMutation = useMutation({
    mutationFn: () => assignGroupStaff(staffTargetGroup!.id, staffUserId, staffRole),
    onSuccess: () => {
      setStaffUserId("");
      setStaffTargetGroup(null);
      queryClient.invalidateQueries({ queryKey: ["groups"] });
    },
  });

  const courseNameById = new Map(coursesQuery.data?.map((c) => [c.id, c.name]));
  const needle = search.trim().toLowerCase();
  const visibleGroups = needle
    ? groupsQuery.data?.filter((g) => `${g.name} ${courseNameById.get(g.courseId) ?? ""}`.toLowerCase().includes(needle))
    : groupsQuery.data;
  const branchNameById = new Map(branchesQuery.data?.map((b) => [b.id, b.name]));
  const teacherNameById = new Map(staffCandidates.map((t) => [t.id, t.fullName]));

  return (
    <main className="p-4 sm:p-6 lg:p-8">
      <PageHeader
        title="Guruhlar"
        description={canManage
          ? "Kurslar va o'quv guruhlari — o'qituvchi biriktirish shu yerdan amalga oshiriladi."
          : "Sizga biriktirilgan guruhlar."}
        actions={canManage && (
          <>
            <Button variant="secondary" onClick={() => setCourseDrawerOpen(true)}>
              <BookOpen className="size-4" aria-hidden />
              Yangi kurs
            </Button>
            <Button onClick={() => setGroupDrawerOpen(true)}>
              <Plus className="size-4" aria-hidden />
              Yangi guruh
            </Button>
          </>
        )}
      />

      {groupsQuery.isError && (
        <Alert>{groupsQuery.error instanceof ApiError ? groupsQuery.error.message : "Yuklab bo'lmadi."}</Alert>
      )}

      {groupsQuery.isLoading && (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {Array.from({ length: 3 }).map((_, i) => (
            <Skeleton key={i} className="h-40" />
          ))}
        </div>
      )}

      {groupsQuery.data && groupsQuery.data.length === 0 && (
        <div className="rounded-xl border border-border bg-surface">
          <EmptyState icon={UsersRound} title="Hali guruh yo'q" description="Avval kurs, so'ng guruh yarating." />
        </div>
      )}

      {(groupsQuery.data?.length ?? 0) > 6 && (
        <div className="mb-4 max-w-sm">
          <Label htmlFor="group-search">Guruh yoki kurs bo&apos;yicha qidirish</Label>
          <Input id="group-search" value={search} onChange={(e) => setSearch(e.target.value)} />
        </div>
      )}

      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {visibleGroups?.map((group) => (
          <Card key={group.id} id={highlightElementId(group.id)} className={highlightClassName(group.id, highlightId)}>
            <CardHeader>
              <CardTitle>
                <Link href={`/groups/${group.id}`} className="hover:text-brand-700 hover:underline">{group.name}</Link>
              </CardTitle>
              <Badge tone={STATUS_TONE[group.status]} dot>
                {STATUS_LABEL[group.status]}
              </Badge>
            </CardHeader>
            <CardBody className="space-y-3">
              <p className="text-[13px] text-ink-500">
                {courseNameById.get(group.courseId) ?? "—"}
                {canManage && <> · {branchNameById.get(group.branchId) ?? "—"}</>}
              </p>

              <div className="flex items-center gap-1.5 text-[13px] text-ink-600">
                <Users className="size-3.5 text-ink-400" aria-hidden />
                <span className="font-tabular">{group.studentCount} / {group.capacity}</span> talaba
              </div>

              <div className="flex flex-wrap gap-1.5">
                {group.staff.length === 0 && <span className="text-[13px] text-ink-400">O&apos;qituvchi biriktirilmagan</span>}
                {group.staff.map((s, i) => (
                  <Badge key={i} tone={s.role === "MainTeacher" ? "brand" : "neutral"}>
                    {s.role === "MainTeacher" ? "Asosiy" : "Yordamchi"}
                    {canManage && <>: {teacherNameById.get(s.userId) ?? s.userId.slice(0, 8)}</>}
                  </Badge>
                ))}
              </div>

              <div className="flex items-center justify-between gap-2 pt-1">
                {canManage ? (
                  <div className="-ml-2 flex items-center">
                    <Button variant="ghost" size="sm" onClick={() => setStaffTargetGroup(group)}>
                      <UserPlus className="size-3.5" aria-hidden />
                      O&apos;qituvchi biriktirish
                    </Button>
                    {/* Only an empty-looking group offers it; the server still refuses one with lessons, invoices or past enrollments. */}
                    {canDelete && group.studentCount === 0 && (
                      <Button variant="ghost" size="sm" onClick={() => setDeleteTarget(group)} aria-label={`${group.name} guruhini o'chirish`}>
                        <Trash2 className="size-3.5" aria-hidden />
                        O&apos;chirish
                      </Button>
                    )}
                  </div>
                ) : <span />}
                <Link href={`/groups/${group.id}`} className="inline-flex items-center gap-1 text-[13px] font-medium text-brand-700 hover:underline">
                  Batafsil
                  <ArrowRight className="size-3.5" aria-hidden />
                </Link>
              </div>
            </CardBody>
          </Card>
        ))}
      </div>

      {visibleGroups?.length === 0 && groupsQuery.data && groupsQuery.data.length > 0 && (
        <p className="py-8 text-center text-[13px] text-ink-500">Qidiruv bo&apos;yicha guruh topilmadi.</p>
      )}

      {/* Prices sit under the groups: with a real centre's dozen courses this list is longer than the screen. */}
      {canManage && coursesQuery.data && (
        <div className="mt-8">
          <CourseFees
            courses={coursesQuery.data}
            groupCounts={groupsQuery.data?.reduce((counts, g) => counts.set(g.courseId, (counts.get(g.courseId) ?? 0) + 1), new Map<string, number>())}
          />
        </div>
      )}

      <Drawer open={courseDrawerOpen} onClose={() => setCourseDrawerOpen(false)} title="Yangi kurs">
        <form
          className="space-y-4"
          onSubmit={(e) => {
            e.preventDefault();
            createCourseMutation.mutate();
          }}
        >
          <div>
            <Label htmlFor="course-name">Kurs nomi</Label>
            <Input id="course-name" required value={courseName} onChange={(e) => setCourseName(e.target.value)} placeholder="IELTS Intermediate" />
          </div>
          <div>
            <Label htmlFor="course-level">Daraja</Label>
            <Input id="course-level" required value={levelCategory} onChange={(e) => setLevelCategory(e.target.value)} placeholder="IELTS, A1, B2..." />
          </div>
          <div>
            <Label htmlFor="course-fee-new">Oylik to&apos;lov (so&apos;m)</Label>
            <Input id="course-fee-new" inputMode="numeric" value={monthlyFee} onChange={(e) => setMonthlyFee(e.target.value)} placeholder="Masalan: 800000" />
            <p className="mt-1 text-[12px] text-ink-500">Bo&apos;sh yoki 0 bo&apos;lsa, bu kurs uchun hisob-faktura chiqmaydi.</p>
          </div>
          {createCourseMutation.isError && (
            <Alert>{createCourseMutation.error instanceof ApiError ? createCourseMutation.error.message : "Kurs yaratib bo'lmadi."}</Alert>
          )}
          <Button type="submit" loading={createCourseMutation.isPending} className="w-full">
            Kurs yaratish
          </Button>
        </form>
      </Drawer>

      <Drawer open={showGroupDrawer} onClose={() => { setGroupDrawerOpen(false); newParam.clear(); }} title="Yangi guruh">
        <form
          className="space-y-4"
          onSubmit={(e) => {
            e.preventDefault();
            createGroupMutation.mutate();
          }}
        >
          <div>
            <Label htmlFor="group-name">Guruh nomi</Label>
            <Input id="group-name" required value={groupName} onChange={(e) => setGroupName(e.target.value)} placeholder="IELTS-21" />
          </div>
          <div>
            <Label htmlFor="group-capacity">Sig&apos;im</Label>
            <Input id="group-capacity" type="number" min={1} required value={capacity} onChange={(e) => setCapacity(Number(e.target.value))} />
          </div>
          <div>
            <Label htmlFor="group-branch">Filial</Label>
            <Select id="group-branch" required value={branchId} onChange={(e) => setBranchId(e.target.value)}>
              <option value="">Tanlang</option>
              {branchesQuery.data?.map((b) => (
                <option key={b.id} value={b.id}>{b.name}</option>
              ))}
            </Select>
          </div>
          <div>
            <Label htmlFor="group-course">Kurs</Label>
            <Select id="group-course" required value={courseId} onChange={(e) => setCourseId(e.target.value)}>
              <option value="">Tanlang</option>
              {coursesQuery.data?.map((c) => (
                <option key={c.id} value={c.id}>{c.name}</option>
              ))}
            </Select>
          </div>
          {createGroupMutation.isError && (
            <Alert>{createGroupMutation.error instanceof ApiError ? createGroupMutation.error.message : "Guruh yaratib bo'lmadi."}</Alert>
          )}
          <Button type="submit" loading={createGroupMutation.isPending} className="w-full">
            Guruh yaratish
          </Button>
        </form>
      </Drawer>

      {deleteTarget && <GroupDeleteDialog key={deleteTarget.id} group={deleteTarget} onClose={() => setDeleteTarget(null)} />}

      <Drawer
        open={staffTargetGroup !== null}
        onClose={() => setStaffTargetGroup(null)}
        title="O'qituvchi biriktirish"
        description={staffTargetGroup ? `Guruh: ${staffTargetGroup.name}` : undefined}
      >
        <form
          className="space-y-4"
          onSubmit={(e) => {
            e.preventDefault();
            assignStaffMutation.mutate();
          }}
        >
          <div>
            <Label htmlFor="staff-user">O&apos;qituvchi</Label>
            <Select id="staff-user" required value={staffUserId} onChange={(e) => setStaffUserId(e.target.value)}>
              <option value="">Tanlang</option>
              {staffCandidates.map((t) => (
                <option key={t.id} value={t.id}>{t.fullName}</option>
              ))}
            </Select>
          </div>
          <div>
            <Label htmlFor="staff-role">Roli</Label>
            <Select id="staff-role" value={staffRole} onChange={(e) => setStaffRole(e.target.value as "MainTeacher" | "SupportTeacher")}>
              <option value="MainTeacher">Asosiy o&apos;qituvchi</option>
              <option value="SupportTeacher">Yordamchi o&apos;qituvchi</option>
            </Select>
          </div>
          {assignStaffMutation.isError && (
            <Alert>{assignStaffMutation.error instanceof ApiError ? assignStaffMutation.error.message : "Biriktirib bo'lmadi."}</Alert>
          )}
          <Button type="submit" loading={assignStaffMutation.isPending} className="w-full">
            Biriktirish
          </Button>
        </form>
      </Drawer>
    </main>
  );
}
