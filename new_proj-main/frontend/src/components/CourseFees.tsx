"use client";

import { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Pencil, Trash2 } from "lucide-react";
import { deleteCourse, updateCourse, type Course } from "@/lib/api/academic";
import { formatMoney } from "@/lib/api/finance";
import { ApiError } from "@/lib/api-client";
import { usePermissions } from "@/lib/use-permissions";
import { Alert } from "@/components/ui/Alert";
import { Button } from "@/components/ui/Button";
import { ConfirmDialog } from "@/components/ui/ConfirmDialog";
import { Drawer } from "@/components/ui/Drawer";
import { Input, Label } from "@/components/ui/Field";
import { cn } from "@/lib/cn";

/** Course catalog with its monthly tuition — the price billing reads (docs/05_phase3_technical_plan.md §1). A course with groups
 * shows how many instead of a delete button; `groupCounts` only knows the groups the caller can see, so the server still has the
 * last word and its refusal is shown in the dialog. */
export function CourseFees({ courses, groupCounts }: { courses: Course[]; groupCounts?: Map<string, number> }) {
  const queryClient = useQueryClient();
  const { can } = usePermissions();
  const [target, setTarget] = useState<Course | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<Course | null>(null);

  const deleteMutation = useMutation({
    mutationFn: (course: Course) => deleteCourse(course.id),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["courses"] });
      setDeleteTarget(null);
    },
  });
  const stopDeleting = () => {
    deleteMutation.reset();
    setDeleteTarget(null);
  };

  if (courses.length === 0) return null;

  return (
    <section className="mb-6 overflow-hidden rounded-xl border border-border bg-surface">
      <div className="flex items-center justify-between border-b border-border px-5 py-3">
        <h2 className="text-[14px] font-semibold text-ink-900">Kurslar va oylik to&apos;lov</h2>
        <p className="text-[12.5px] text-ink-500">Narxi 0 bo&apos;lgan kurs uchun hisob-faktura chiqmaydi</p>
      </div>
      <div className="divide-y divide-border">
        {courses.map((course) => (
          <div key={course.id} className="flex items-center gap-3 px-5 py-3">
            <div className="min-w-0 flex-1">
              <p className="truncate text-[13.5px] font-medium text-ink-900">{course.name}</p>
              <p className="text-[12.5px] text-ink-500">{course.levelCategory}</p>
            </div>
            <span className={cn("font-tabular text-[13.5px]", course.monthlyFee > 0 ? "text-ink-900" : "text-ink-400")}>
              {course.monthlyFee > 0 ? `${formatMoney(course.monthlyFee)} / oy` : "Narx belgilanmagan"}
            </span>
            <Button size="sm" variant="ghost" onClick={() => setTarget(course)}>
              <Pencil className="size-3.5" aria-hidden />
              Narx
            </Button>
            {can("courses.manage") && ((groupCounts?.get(course.id) ?? 0) > 0 ? (
              <span className="text-[12px] text-ink-500">{groupCounts?.get(course.id)} ta guruh bor</span>
            ) : (
              <Button size="sm" variant="ghost" onClick={() => setDeleteTarget(course)} aria-label={`${course.name} kursini o'chirish`}>
                <Trash2 className="size-3.5" aria-hidden />
                O&apos;chirish
              </Button>
            ))}
          </div>
        ))}
      </div>
      {target && <CourseFeeDrawer key={target.id} course={target} onClose={() => setTarget(null)} />}
      <ConfirmDialog
        open={deleteTarget !== null}
        title={deleteTarget ? `${deleteTarget.name} kursini o'chirish` : ""}
        description="Kurs butunlay o'chiriladi va qaytarib bo'lmaydi. Guruhi bor kursni (yopilgan guruh ham) o'chirib bo'lmaydi."
        loading={deleteMutation.isPending}
        error={deleteMutation.isError ? (deleteMutation.error instanceof ApiError ? deleteMutation.error.message : "O'chirib bo'lmadi.") : null}
        onConfirm={() => deleteTarget && deleteMutation.mutate(deleteTarget)}
        onClose={stopDeleting}
      />
    </section>
  );
}

function CourseFeeDrawer({ course, onClose }: { course: Course; onClose: () => void }) {
  const client = useQueryClient();
  const [fee, setFee] = useState(String(course.monthlyFee));
  const parsed = Number(fee.replace(/\s/g, ""));

  const mutation = useMutation({
    mutationFn: () => updateCourse(course.id, { name: course.name, levelCategory: course.levelCategory, monthlyFee: parsed }),
    onSuccess: async () => {
      await client.invalidateQueries({ queryKey: ["courses"] });
      onClose();
    },
  });

  return (
    <Drawer open onClose={onClose} title="Oylik to'lovni o'zgartirish" description={course.name}>
      <form
        className="space-y-4"
        onSubmit={(e) => {
          e.preventDefault();
          mutation.mutate();
        }}
      >
        <div>
          <Label htmlFor="course-fee">Oylik to&apos;lov (so&apos;m)</Label>
          <Input id="course-fee" inputMode="numeric" required value={fee} onChange={(e) => setFee(e.target.value)} />
          <p className="mt-1 text-[12px] text-ink-500">
            Yangi narx keyingi hisob-fakturalardan boshlab qo&apos;llanadi — allaqachon chiqarilgan hisob-fakturalar o&apos;zgarmaydi.
          </p>
        </div>
        {mutation.isError && <Alert>{mutation.error instanceof ApiError ? mutation.error.message : "Saqlab bo'lmadi."}</Alert>}
        <Button type="submit" loading={mutation.isPending} disabled={!Number.isFinite(parsed) || parsed < 0} className="w-full">
          Saqlash
        </Button>
      </form>
    </Drawer>
  );
}
