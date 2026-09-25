"use client";

import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Megaphone, Pencil, Plus, Trash2 } from "lucide-react";
import {
  announcementPlace, AUDIENCE_LABEL, deleteAnnouncement, listAnnouncements, type Announcement,
} from "@/lib/api/announcements";
import { ApiError } from "@/lib/api-client";
import { usePermissions } from "@/lib/use-permissions";
import { formatDate } from "@/lib/format-date";
import { AnnouncementDrawer } from "@/components/announcements/AnnouncementDrawer";
import { Alert } from "@/components/ui/Alert";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { Card } from "@/components/ui/Card";
import { ConfirmDialog } from "@/components/ui/ConfirmDialog";
import { EmptyState } from "@/components/ui/EmptyState";
import { PageHeader } from "@/components/ui/PageHeader";
import { Pagination } from "@/components/ui/Pagination";
import { Skeleton } from "@/components/ui/Skeleton";

const PAGE_SIZE = 10;

/**
 * The centre's announcements to the people they concern (docs/00_TZ.md §24): what reaches you — your branch's, your groups', your children's —
 * newest first. Whoever may publish also gets the buttons to publish, edit and take down; the server says per announcement whether that is theirs.
 */
export default function AnnouncementsPage() {
  const queryClient = useQueryClient();
  const { can } = usePermissions();
  const [page, setPage] = useState(1);
  const [creating, setCreating] = useState(false);
  const [editing, setEditing] = useState<Announcement | null>(null);
  const [deleting, setDeleting] = useState<Announcement | null>(null);

  const query = useQuery({
    queryKey: ["announcements", "list", page],
    queryFn: () => listAnnouncements({ page, size: PAGE_SIZE }),
    placeholderData: keepPreviousData,
  });
  const data = query.data;

  const remove = useMutation({
    mutationFn: (id: string) => deleteAnnouncement(id),
    onSuccess: () => {
      setDeleting(null);
      queryClient.invalidateQueries({ queryKey: ["announcements"] });
    },
  });

  return (
    <main className="p-4 sm:p-6 lg:p-8">
      <PageHeader
        title="E'lonlar"
        description="Markaz, filial yoki guruhingizga tegishli e'lonlar."
        actions={can("announcements.manage") && (
          <Button onClick={() => setCreating(true)}>
            <Plus className="size-4" aria-hidden />
            Yangi e&apos;lon
          </Button>
        )}
      />

      {query.isError && <Alert>{query.error instanceof ApiError ? query.error.message : "E'lonlarni yuklab bo'lmadi."}</Alert>}
      {query.isPending && <div className="space-y-3">{Array.from({ length: 3 }, (_, i) => <Skeleton key={i} className="h-28" />)}</div>}

      {data && data.items.length === 0 && (
        <Card><EmptyState icon={Megaphone} title="Hozircha e'lon yo'q" description="Yangi e'lon chiqqanda shu yerda ko'rinadi." /></Card>
      )}

      {data && data.items.length > 0 && (
        <div className="space-y-3">
          {data.items.map((a) => (
            <Card key={a.id} className="p-5">
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div className="min-w-0">
                  <h2 className="text-[15px] font-semibold text-ink-900">{a.title}</h2>
                  <div className="mt-1.5 flex flex-wrap items-center gap-2 text-[12.5px] text-ink-500">
                    <Badge tone="brand">{announcementPlace(a)}</Badge>
                    {can("announcements.manage") && <Badge tone="neutral">{AUDIENCE_LABEL[a.audience]}</Badge>}
                    <span>{formatDate(a.publishedAt)} · {a.authorName}</span>
                    {a.expiresAt && <span>· {formatDate(a.expiresAt)} gacha</span>}
                  </div>
                </div>
                {a.canManage && (
                  <div className="flex gap-2">
                    <Button variant="secondary" size="sm" onClick={() => setEditing(a)}>
                      <Pencil className="size-3.5" aria-hidden />Tahrirlash
                    </Button>
                    <Button variant="ghost" size="sm" className="text-danger-600 hover:bg-danger-50" onClick={() => setDeleting(a)}>
                      <Trash2 className="size-3.5" aria-hidden />O&apos;chirish
                    </Button>
                  </div>
                )}
              </div>
              <p className="mt-3 whitespace-pre-wrap text-[13.5px] leading-relaxed text-ink-700">{a.body}</p>
            </Card>
          ))}
          <Pagination page={data.pageNumber} pageCount={data.pageCount} total={data.total} pageSize={data.pageSize} onChange={setPage} className="rounded-xl border border-border bg-surface" />
        </div>
      )}

      {creating && <AnnouncementDrawer onClose={() => setCreating(false)} />}
      {editing && <AnnouncementDrawer key={editing.id} existing={editing} onClose={() => setEditing(null)} />}
      <ConfirmDialog
        open={deleting !== null}
        title="E'lonni o'chirish"
        description={deleting ? <>«{deleting.title}» hammaning ro&apos;yxatidan olib tashlanadi. Bildirishnomalar joyida qoladi.</> : undefined}
        loading={remove.isPending}
        error={remove.error instanceof ApiError ? remove.error.message : remove.isError ? "O'chirib bo'lmadi." : null}
        onConfirm={() => deleting && remove.mutate(deleting.id)}
        onClose={() => { remove.reset(); setDeleting(null); }}
      />
    </main>
  );
}
