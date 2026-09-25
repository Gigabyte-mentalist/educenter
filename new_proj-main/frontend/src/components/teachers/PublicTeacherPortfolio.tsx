"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { useQuery } from "@tanstack/react-query";
import { ArrowLeft, Briefcase, Eye, ShieldCheck } from "lucide-react";
import { fetchPublicCredentialFile, getPublicPortfolio, CREDENTIAL_TYPES, type PublicCredential, type PublicPortfolio } from "@/lib/api/teachers";
import { ApiError } from "@/lib/api-client";
import { formatDate } from "@/lib/format-date";
import { Alert } from "@/components/ui/Alert";
import { Avatar } from "@/components/ui/Avatar";
import { Badge } from "@/components/ui/Badge";
import { Card, CardBody, CardHeader, CardTitle } from "@/components/ui/Card";
import { EmptyState } from "@/components/ui/EmptyState";
import { Skeleton } from "@/components/ui/Skeleton";
import { Button } from "@/components/ui/Button";
import { CredentialFileViewer } from "./CredentialFileViewer";
import { TeacherHeadline } from "./TeacherHeadline";

const typeLabel = (c: PublicCredential) => CREDENTIAL_TYPES.find((t) => t.value === c.type)?.label.split(" (")[0] ?? c.type;

/**
 * "Mening o‘qituvchim": what a student or a parent may read about the teacher of their own group — the facts an administrator has
 * confirmed, and nothing else (no scan, no reference number, no unconfirmed line). The server decides who may open which teacher.
 */
export function PublicTeacherPortfolio({ teacherId }: { teacherId: string }) {
  const router = useRouter();
  const query = useQuery({ queryKey: ["teachers", teacherId, "public-portfolio"], queryFn: () => getPublicPortfolio(teacherId) });

  return (
    <main className="p-4 sm:p-6 lg:p-8">
      <button type="button" onClick={() => router.back()} className="mb-4 inline-flex items-center gap-1.5 text-[13px] text-ink-500 hover:text-ink-800">
        <ArrowLeft className="size-3.5" aria-hidden />
        Orqaga
      </button>
      {query.isPending && <Skeleton className="h-48" />}
      {query.isError && <Alert>{query.error instanceof ApiError ? query.error.message : "Ma‘lumotni yuklab bo‘lmadi."}</Alert>}
      {query.data && <Body data={query.data} />}
    </main>
  );
}

function Body({ data }: { data: PublicPortfolio }) {
  const [viewing, setViewing] = useState<PublicCredential | null>(null);
  const p = data.profile;
  const facts: [string, string | null][] = [
    ["Ixtisoslik", p.specialization],
    ["Tajriba", p.yearsOfExperience === null ? null : `${p.yearsOfExperience} yil`],
    ["Ta‘lim", p.education],
    ["Tillar", p.languages],
  ];
  return (
    <div className="space-y-6">
      <div className="flex items-start gap-4">
        <Avatar name={data.fullName} className="size-16 text-lg" photo={data.photoVersion ? { userId: data.userId, version: data.photoVersion } : null} />
        <div>
          <h1 className="text-[22px] font-semibold text-ink-900">{data.fullName}</h1>
          {p.specialization && <p className="mt-1 text-sm text-ink-500">{p.specialization}</p>}
          <TeacherHeadline chips={data.headline} compact />
        </div>
      </div>

      {(p.bio || facts.some(([, v]) => v)) && (
        <Card>
          <CardHeader><CardTitle>Haqida</CardTitle></CardHeader>
          <CardBody className="space-y-4">
            {p.bio && <p className="whitespace-pre-wrap text-[13.5px] leading-relaxed text-ink-800">{p.bio}</p>}
            <dl className="grid gap-x-8 gap-y-3 text-[13.5px] sm:grid-cols-2">
              {facts.filter(([, v]) => v).map(([label, value]) => (
                <div key={label}>
                  <dt className="text-[12px] font-medium text-ink-500">{label}</dt>
                  <dd className="mt-0.5 text-ink-900">{value}</dd>
                </div>
              ))}
            </dl>
          </CardBody>
        </Card>
      )}

      <Card>
        <CardHeader><CardTitle>Malaka va sertifikatlar</CardTitle></CardHeader>
        {data.credentials.length === 0
          ? <EmptyState icon={ShieldCheck} title="Tasdiqlangan ma‘lumot hali yo‘q" />
          : <ul className="divide-y divide-border">
            {data.credentials.map((c, i) => (
              <li key={`${c.title}-${i}`} className="px-5 py-3.5">
                <div className="flex flex-wrap items-center gap-2">
                  <span className="text-[14px] font-semibold text-ink-900">{c.title}</span>
                  {c.score && <span className="font-tabular text-[14px] font-semibold text-brand-700">{c.score}</span>}
                  <Badge tone="success"><ShieldCheck className="size-3" aria-hidden />Tasdiqlangan</Badge>
                  {c.isExpired && <Badge tone="danger">Muddati tugagan</Badge>}
                </div>
                <p className="mt-0.5 text-[12.5px] text-ink-500">
                  {[typeLabel(c), c.issuer, c.issuedOn && formatDate(c.issuedOn)].filter(Boolean).join(" · ")}
                </p>
                {c.fileContentType && (
                  <Button variant="secondary" size="sm" className="mt-2" onClick={() => setViewing(c)}>
                    <Eye className="size-3.5" aria-hidden />
                    Hujjatni ko‘rish
                  </Button>
                )}
              </li>
            ))}
          </ul>}
      </Card>

      {data.experience.length > 0 && (
        <Card>
          <CardHeader><CardTitle>Ish tajribasi</CardTitle></CardHeader>
          <ol className="divide-y divide-border">
            {data.experience.map((e) => (
              <li key={e.id} className="flex items-start gap-3 px-5 py-3.5">
                <Briefcase className="mt-0.5 size-4 shrink-0 text-ink-400" aria-hidden />
                <div>
                  <p className="text-[13.5px] font-medium text-ink-900">{e.position} · {e.employer}</p>
                  <p className="mt-0.5 text-[12.5px] text-ink-500">{e.fromYear} — {e.toYear ?? "hozirgacha"}</p>
                  {e.description && <p className="mt-1.5 whitespace-pre-wrap text-[13px] text-ink-700">{e.description}</p>}
                </div>
              </li>
            ))}
          </ol>
        </Card>
      )}
      {viewing && (
        <CredentialFileViewer
          open
          title={viewing.title}
          cacheKey={["public", data.userId, viewing.id]}
          load={() => fetchPublicCredentialFile(data.userId, viewing.id)}
          onClose={() => setViewing(null)}
        />
      )}
    </div>
  );
}
