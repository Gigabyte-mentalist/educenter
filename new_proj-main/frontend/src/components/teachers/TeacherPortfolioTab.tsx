"use client";

import { useState } from "react";
import { useMutation, useQuery } from "@tanstack/react-query";
import { Briefcase, Pencil, Plus, Trash2, UserRound } from "lucide-react";
import {
  addExperience, deleteExperience, getTeacherPortfolio, saveTeacherProfile, updateExperience,
  type Experience, type PortfolioProfile,
} from "@/lib/api/teachers";
import { usePermissions } from "@/lib/use-permissions";
import { formatDate } from "@/lib/format-date";
import { Alert } from "@/components/ui/Alert";
import { Button } from "@/components/ui/Button";
import { Card, CardBody, CardHeader, CardTitle } from "@/components/ui/Card";
import { ConfirmDialog } from "@/components/ui/ConfirmDialog";
import { Drawer } from "@/components/ui/Drawer";
import { EmptyState } from "@/components/ui/EmptyState";
import { Input, Label, Textarea } from "@/components/ui/Field";
import { Skeleton } from "@/components/ui/Skeleton";
import { TeacherCredentials } from "./TeacherCredentials";
import { errorText, useRefreshPortfolio } from "./portfolio-shared";

/**
 * Portfolio tab (S05.02): About, qualifications with confirmations, experience. The teacher fills in their own; an administrator
 * of their branch may edit it and is the one who confirms a certificate. The buttons follow the permission codes — the server
 * checks the reach again on every call.
 */
export function TeacherPortfolioTab({ teacherId, isSelf }: { teacherId: string; isSelf: boolean }) {
  const { can, scopeOf } = usePermissions();
  const query = useQuery({ queryKey: ["teachers", teacherId, "portfolio"], queryFn: () => getTeacherPortfolio(teacherId) });

  if (query.isPending) return <Skeleton className="h-48" />;
  if (query.isError) {
    return (
      <>
        <Alert>{errorText(query.error, "Portfolioni yuklab bo‘lmadi.")}</Alert>
        <Button onClick={() => void query.refetch()}>Qayta urinish</Button>
      </>
    );
  }

  const canEdit = can("teacher.portfolio.edit");
  // Nobody vouches for their own paper — except the organization's top scope, which has no one above it to ask.
  const canVerify = can("teacher.portfolio.verify") && (!isSelf || scopeOf("teacher.portfolio.verify") === "AllOrganization");
  const { profile, credentials, experience } = query.data;

  return (
    <div className="space-y-6">
      <AboutSection teacherId={teacherId} profile={profile} canEdit={canEdit} />
      <TeacherCredentials teacherId={teacherId} credentials={credentials} canEdit={canEdit} canVerify={canVerify} isSelf={isSelf} />
      <ExperienceSection teacherId={teacherId} experience={experience} canEdit={canEdit} />
    </div>
  );
}

// ── About ─────────────────────────────────────────────────────────────────────────────────────────

const isBlank = (p: PortfolioProfile) =>
  !p.bio && !p.specialization && p.yearsOfExperience === null && !p.education && !p.languages && !p.teachingSince;

function AboutSection({ teacherId, profile, canEdit }: { teacherId: string; profile: PortfolioProfile; canEdit: boolean }) {
  const refresh = useRefreshPortfolio(teacherId);
  const [open, setOpen] = useState(false);
  const save = useMutation({
    mutationFn: (input: Partial<PortfolioProfile>) => saveTeacherProfile(teacherId, input),
    onSuccess: async () => {
      await refresh();
      setOpen(false);
    },
  });

  const rows: [string, string | null][] = [
    ["Ixtisoslik", profile.specialization],
    ["Tajriba", profile.yearsOfExperience === null ? null : `${profile.yearsOfExperience} yil`],
    ["O‘qita boshlagan", profile.teachingSince ? formatDate(profile.teachingSince) : null],
    ["Ta‘lim", profile.education],
    ["Tillar", profile.languages],
  ];

  return (
    <Card>
      <CardHeader>
        <CardTitle>Haqida</CardTitle>
        {canEdit && (
          <Button variant="secondary" size="sm" onClick={() => { save.reset(); setOpen(true); }}>
            <Pencil className="size-3.5" aria-hidden />
            Tahrirlash
          </Button>
        )}
      </CardHeader>
      {isBlank(profile)
        ? <EmptyState icon={UserRound} title="Hali to‘ldirilmagan" description={canEdit ? "Ixtisoslik, tajriba va tanishtiruvni qo‘shing." : undefined} />
        : <CardBody className="space-y-4">
          {profile.bio && <p className="whitespace-pre-wrap text-[13.5px] leading-relaxed text-ink-800">{profile.bio}</p>}
          <dl className="grid gap-x-8 gap-y-3 text-[13.5px] sm:grid-cols-2">
            {rows.filter(([, value]) => value).map(([label, value]) => (
              <div key={label}>
                <dt className="text-[12px] font-medium text-ink-500">{label}</dt>
                <dd className="mt-0.5 text-ink-900">{value}</dd>
              </div>
            ))}
          </dl>
        </CardBody>}

      {open && (
        <Drawer open title="Haqida" description="Bu ma‘lumot faqat siz va markaz xodimlariga ko‘rinadi." onClose={() => { if (!save.isPending) setOpen(false); }}>
          <form
            className="space-y-4"
            onSubmit={(e) => {
              e.preventDefault();
              const form = new FormData(e.currentTarget);
              const text = (name: string) => String(form.get(name) ?? "").trim() || null;
              const years = text("yearsOfExperience");
              save.mutate({
                bio: text("bio"), specialization: text("specialization"), education: text("education"), languages: text("languages"),
                yearsOfExperience: years === null ? null : Number(years), teachingSince: text("teachingSince"),
              });
            }}
          >
            <div>
              <Label htmlFor="about-specialization">Ixtisoslik</Label>
              <Input id="about-specialization" name="specialization" maxLength={200} defaultValue={profile.specialization ?? ""} placeholder="IELTS Writing" />
            </div>
            <div className="grid gap-4 sm:grid-cols-2">
              <div>
                <Label htmlFor="about-years">Tajriba (yil)</Label>
                <Input id="about-years" name="yearsOfExperience" type="number" min={0} max={60} defaultValue={profile.yearsOfExperience ?? ""} />
              </div>
              <div>
                <Label htmlFor="about-since">O‘qita boshlagan sana</Label>
                <Input id="about-since" name="teachingSince" type="date" defaultValue={profile.teachingSince ?? ""} />
              </div>
            </div>
            <div>
              <Label htmlFor="about-education">Ta‘lim</Label>
              <Input id="about-education" name="education" maxLength={500} defaultValue={profile.education ?? ""} />
            </div>
            <div>
              <Label htmlFor="about-languages">Tillar</Label>
              <Input id="about-languages" name="languages" maxLength={200} defaultValue={profile.languages ?? ""} placeholder="O‘zbek, ingliz, rus" />
            </div>
            <div>
              <Label htmlFor="about-bio">O‘zi haqida</Label>
              <Textarea id="about-bio" name="bio" rows={5} maxLength={2000} defaultValue={profile.bio ?? ""} />
            </div>
            {save.isError && <Alert>{errorText(save.error, "Saqlab bo‘lmadi.")}</Alert>}
            <Button type="submit" loading={save.isPending} className="w-full">Saqlash</Button>
          </form>
        </Drawer>
      )}
    </Card>
  );
}

// ── Experience ────────────────────────────────────────────────────────────────────────────────────

const years = (e: Experience) => `${e.fromYear} — ${e.toYear ?? "hozirgacha"}`;

function ExperienceSection({ teacherId, experience, canEdit }: { teacherId: string; experience: Experience[]; canEdit: boolean }) {
  const refresh = useRefreshPortfolio(teacherId);
  // `null` closed · `"new"` adding · an Experience editing it.
  const [editing, setEditing] = useState<Experience | "new" | null>(null);
  const [removing, setRemoving] = useState<Experience | null>(null);

  const save = useMutation({
    mutationFn: (input: { employer: string; position: string; fromYear: number; toYear: number | null; description?: string }) =>
      editing && editing !== "new" ? updateExperience(teacherId, editing.id, input) : addExperience(teacherId, input),
    onSuccess: async () => {
      await refresh();
      setEditing(null);
    },
  });
  const remove = useMutation({
    mutationFn: (id: string) => deleteExperience(teacherId, id),
    onSuccess: async () => {
      await refresh();
      setRemoving(null);
    },
  });
  const current = editing && editing !== "new" ? editing : null;

  return (
    <Card>
      <CardHeader>
        <CardTitle>Ish tajribasi</CardTitle>
        {canEdit && (
          <Button variant="secondary" size="sm" onClick={() => { save.reset(); setEditing("new"); }}>
            <Plus className="size-3.5" aria-hidden />
            Qo‘shish
          </Button>
        )}
      </CardHeader>
      {experience.length === 0
        ? <EmptyState icon={Briefcase} title="Ish tajribasi kiritilmagan" />
        : <ol className="divide-y divide-border">
          {experience.map((e) => (
            <li key={e.id} className="flex items-start justify-between gap-3 px-5 py-3.5">
              <div className="min-w-0">
                <p className="text-[13.5px] font-medium text-ink-900">{e.position} · {e.employer}</p>
                <p className="mt-0.5 text-[12.5px] text-ink-500">{years(e)}</p>
                {e.description && <p className="mt-1.5 whitespace-pre-wrap text-[13px] text-ink-700">{e.description}</p>}
              </div>
              {canEdit && (
                <div className="flex shrink-0 gap-1">
                  <Button variant="ghost" size="icon" aria-label="Tahrirlash" onClick={() => { save.reset(); setEditing(e); }}><Pencil className="size-4" aria-hidden /></Button>
                  <Button variant="ghost" size="icon" aria-label="O‘chirish" onClick={() => { remove.reset(); setRemoving(e); }}><Trash2 className="size-4" aria-hidden /></Button>
                </div>
              )}
            </li>
          ))}
        </ol>}

      {editing && (
        <Drawer open title={current ? "Tajribani tahrirlash" : "Ish tajribasi"} onClose={() => { if (!save.isPending) setEditing(null); }}>
          <form
            className="space-y-4"
            onSubmit={(e) => {
              e.preventDefault();
              const form = new FormData(e.currentTarget);
              const text = (name: string) => String(form.get(name) ?? "").trim();
              const to = text("toYear");
              save.mutate({
                employer: text("employer"), position: text("position"), fromYear: Number(text("fromYear")),
                toYear: to === "" ? null : Number(to), description: text("description") || undefined,
              });
            }}
          >
            <div>
              <Label htmlFor="exp-employer">Ish joyi</Label>
              <Input id="exp-employer" name="employer" required maxLength={200} defaultValue={current?.employer ?? ""} />
            </div>
            <div>
              <Label htmlFor="exp-position">Lavozim</Label>
              <Input id="exp-position" name="position" required maxLength={200} defaultValue={current?.position ?? ""} />
            </div>
            <div className="grid gap-4 sm:grid-cols-2">
              <div>
                <Label htmlFor="exp-from">Boshlangan yil</Label>
                <Input id="exp-from" name="fromYear" type="number" required min={1950} defaultValue={current?.fromYear ?? ""} />
              </div>
              <div>
                <Label htmlFor="exp-to">Tugagan yil</Label>
                <Input id="exp-to" name="toYear" type="number" min={1950} defaultValue={current?.toYear ?? ""} placeholder="hozirgacha" />
              </div>
            </div>
            <div>
              <Label htmlFor="exp-description">Izoh (ixtiyoriy)</Label>
              <Textarea id="exp-description" name="description" rows={3} maxLength={1000} defaultValue={current?.description ?? ""} />
            </div>
            {save.isError && <Alert>{errorText(save.error, "Saqlab bo‘lmadi.")}</Alert>}
            <Button type="submit" loading={save.isPending} className="w-full">Saqlash</Button>
          </form>
        </Drawer>
      )}

      <ConfirmDialog
        open={removing !== null}
        title={`«${removing?.position ?? ""} · ${removing?.employer ?? ""}» yozuvini o‘chirish`}
        loading={remove.isPending}
        error={remove.isError ? errorText(remove.error, "O‘chirib bo‘lmadi.") : null}
        onConfirm={() => removing && remove.mutate(removing.id)}
        onClose={() => setRemoving(null)}
      />
    </Card>
  );
}

