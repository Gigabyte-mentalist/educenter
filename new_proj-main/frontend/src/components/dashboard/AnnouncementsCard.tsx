"use client";

import Link from "next/link";
import { useQuery } from "@tanstack/react-query";
import { announcementPlace, listAnnouncements } from "@/lib/api/announcements";
import { formatDayMonth } from "@/lib/format-date";
import { Badge } from "@/components/ui/Badge";
import { Card, CardHeader, CardTitle } from "@/components/ui/Card";

/**
 * "So'nggi e'lonlar": the three newest announcements that reach the person, at the top of a student's, a parent's or a teacher's home. It is not
 * there at all until there is something to read, so a quiet centre costs a dashboard no space.
 */
export function AnnouncementsCard() {
  const query = useQuery({ queryKey: ["announcements", "recent"], queryFn: () => listAnnouncements({ size: 3 }) });
  const items = query.data?.items ?? [];
  if (items.length === 0) return null;

  return (
    <Card className="mb-4">
      <CardHeader>
        <CardTitle>So&apos;nggi e&apos;lonlar</CardTitle>
        <Link href="/announcements" className="text-[12.5px] text-brand-700 hover:underline">Barchasi</Link>
      </CardHeader>
      <ul className="divide-y divide-border">
        {items.map((a) => (
          <li key={a.id} className="px-5 py-3">
            <div className="flex flex-wrap items-center gap-2">
              <p className="text-[13.5px] font-medium text-ink-900">{a.title}</p>
              <Badge tone="brand">{announcementPlace(a)}</Badge>
              <span className="text-[12px] text-ink-400">{formatDayMonth(a.publishedAt)}</span>
            </div>
            <p className="mt-1 line-clamp-2 whitespace-pre-line text-[13px] text-ink-600">{a.body}</p>
          </li>
        ))}
      </ul>
    </Card>
  );
}
