"use client";

import { useRef, useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { BadgeCheck, Eye, FileText, Pencil, Plus, ShieldCheck, ShieldOff, Trash2, Award } from "lucide-react";
import {
  addCredential, CREDENTIAL_FILE_ACCEPT, CREDENTIAL_FILE_MAX_BYTES, CREDENTIAL_TYPES, deleteCredential, deleteCredentialFile,
  fetchCredentialFile, unverifyCredential, updateCredential, uploadCredentialFile, verifyCredential,
  type Credential, type CredentialInput, type CredentialType,
} from "@/lib/api/teachers";
import { formatDate } from "@/lib/format-date";
import { Alert } from "@/components/ui/Alert";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { Card, CardHeader, CardTitle } from "@/components/ui/Card";
import { ConfirmDialog } from "@/components/ui/ConfirmDialog";
import { Drawer } from "@/components/ui/Drawer";
import { EmptyState } from "@/components/ui/EmptyState";
import { Input, Label, Select, Textarea } from "@/components/ui/Field";
import { Modal } from "@/components/ui/Modal";
import { CredentialFileViewer } from "./CredentialFileViewer";
import { errorText, useRefreshPortfolio } from "./portfolio-shared";

const formatSize = (bytes: number) =>
  bytes >= 1024 * 1024 ? `${(bytes / (1024 * 1024)).toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`;

const typeLabel = (type: CredentialType) =>
  CREDENTIAL_TYPES.find((t) => t.value === type)?.label.split(" (")[0] ?? type;

/**
 * "Malaka va sertifikatlar": IELTS bands, certificates, degrees. What a teacher adds about themselves is <b>unconfirmed</b> until an
 * administrator has looked at the scan or the reference and pressed Tasdiqlash; editing what was confirmed withdraws the
 * confirmation — the server does that, and this screen says so before the teacher saves and after.
 */
export function TeacherCredentials({
  teacherId, credentials, canEdit, canVerify, isSelf,
}: { teacherId: string; credentials: Credential[]; canEdit: boolean; canVerify: boolean; isSelf: boolean }) {
  const refresh = useRefreshPortfolio(teacherId);
  const [editing, setEditing] = useState<Credential | "new" | null>(null);
  const [removing, setRemoving] = useState<Credential | null>(null);
  const [checking, setChecking] = useState<Credential | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const remove = useMutation({
    mutationFn: (id: string) => deleteCredential(teacherId, id),
    onSuccess: async () => {
      await refresh();
      setRemoving(null);
    },
  });
  const verify = useMutation({
    mutationFn: (id: string) => verifyCredential(teacherId, id),
    onSuccess: async () => {
      await refresh();
      setChecking(null);
    },
  });
  const unverify = useMutation({
    mutationFn: (id: string) => unverifyCredential(teacherId, id),
    onSuccess: refresh,
  });
  const removeFile = useMutation({
    mutationFn: (id: string) => deleteCredentialFile(teacherId, id),
    onSuccess: async (result) => {
      await refresh();
      if (result.verificationReset) setNotice(withdrawnNotice(isSelf));
    },
  });

  return (
    <Card>
      <CardHeader>
        <CardTitle>Malaka va sertifikatlar</CardTitle>
        {canEdit && (
          <Button variant="secondary" size="sm" onClick={() => { setNotice(null); setEditing("new"); }}>
            <Plus className="size-3.5" aria-hidden />
            Qo‘shish
          </Button>
        )}
      </CardHeader>

      <div className="space-y-3 p-4">
        {notice && <Alert tone="info">{notice}</Alert>}
        {(unverify.isError || removeFile.isError) && <Alert>{errorText(unverify.error ?? removeFile.error, "Amalni bajarib bo‘lmadi.")}</Alert>}

        {credentials.length === 0
          ? <EmptyState icon={Award} title="Hali qo‘shilmagan" description={canEdit ? "IELTS balli, sertifikat yoki diplomni qo‘shing — administrator tasdiqlaydi." : undefined} />
          : credentials.map((c) => (
            <CredentialCard
              key={c.id}
              teacherId={teacherId}
              credential={c}
              canEdit={canEdit}
              canVerify={canVerify}
              busy={unverify.isPending || removeFile.isPending}
              onEdit={() => { setNotice(null); setEditing(c); }}
              onDelete={() => { remove.reset(); setRemoving(c); }}
              onVerify={() => { verify.reset(); setChecking(c); }}
              onUnverify={() => unverify.mutate(c.id)}
              onRemoveFile={() => removeFile.mutate(c.id)}
            />
          ))}
      </div>

      {editing && (
        <CredentialDrawer
          teacherId={teacherId}
          credential={editing === "new" ? null : editing}
          canVerify={canVerify}
          isSelf={isSelf}
          onDone={async (message) => {
            await refresh();
            setNotice(message);
            setEditing(null);
          }}
          onClose={() => setEditing(null)}
        />
      )}

      <ConfirmDialog
        open={removing !== null}
        title={`«${removing?.title ?? ""}» yozuvini o‘chirish`}
        description={removing?.file ? "Yozuv va yuklangan hujjat butunlay o‘chiriladi." : undefined}
        loading={remove.isPending}
        error={remove.isError ? errorText(remove.error, "O‘chirib bo‘lmadi.") : null}
        onConfirm={() => removing && remove.mutate(removing.id)}
        onClose={() => setRemoving(null)}
      />

      <Modal open={checking !== null} onClose={() => { if (!verify.isPending) setChecking(null); }} title="Tasdiqlash">
        {checking && (
          <div className="space-y-4 p-5 pt-2 text-[13.5px] text-ink-700">
            <p>
              <b className="text-ink-900">{checking.title}</b>
              {checking.score ? ` — ${checking.score}` : ""}
              {checking.issuer ? ` (${checking.issuer})` : ""}
            </p>
            {checking.file || checking.reference
              ? <p>Tasdiqlashdan oldin hujjatni ko‘ring{checking.reference ? " yoki havola/raqamni tekshiring" : ""}. Tasdiqni siz nomingizdan yozamiz.</p>
              : <Alert tone="info">Bu yozuvda na fayl, na tekshirish havolasi/raqami bor — tasdiqlash faqat sizning ishonchingizga tayanadi.</Alert>}
            {checking.reference && <p className="break-all rounded-lg bg-ink-50 px-3 py-2 text-[12.5px]">{checking.reference}</p>}
            {verify.isError && <Alert>{errorText(verify.error, "Tasdiqlab bo‘lmadi.")}</Alert>}
            <div className="flex justify-end gap-2">
              {checking.file && <OpenFileButton teacherId={teacherId} credential={checking} />}
              <Button variant="secondary" onClick={() => setChecking(null)} disabled={verify.isPending}>Bekor qilish</Button>
              <Button onClick={() => verify.mutate(checking.id)} loading={verify.isPending}>
                <BadgeCheck className="size-4" aria-hidden />
                Tasdiqlash
              </Button>
            </div>
          </div>
        )}
      </Modal>
    </Card>
  );
}

const withdrawnNotice = (isSelf: boolean) =>
  isSelf
    ? "Tasdiqlangan ma‘lumot o‘zgardi — tasdiq bekor bo‘ldi. Administrator uni qayta tasdiqlashi kerak."
    : "Tasdiqlangan ma‘lumot o‘zgardi — tasdiq bekor bo‘ldi. Uni qayta tasdiqlang.";

// ── One credential ────────────────────────────────────────────────────────────────────────────────

/** Opens the scan in the app's viewer (an image or a PDF); a copy can be saved from there. */
function OpenFileButton({ teacherId, credential }: { teacherId: string; credential: Credential }) {
  const [open, setOpen] = useState(false);
  return (
    <>
      <Button variant="secondary" size="sm" onClick={() => setOpen(true)}>
        <Eye className="size-3.5" aria-hidden />
        Hujjatni ko‘rish
      </Button>
      {open && (
        <CredentialFileViewer
          open
          title={credential.title}
          cacheKey={["staff", teacherId, credential.id, credential.file?.size ?? 0]}
          load={() => fetchCredentialFile(teacherId, credential.id)}
          fileName={credential.file?.fileName ?? credential.title}
          onClose={() => setOpen(false)}
        />
      )}
    </>
  );
}

function CredentialCard({
  teacherId, credential: c, canEdit, canVerify, busy, onEdit, onDelete, onVerify, onUnverify, onRemoveFile,
}: {
  teacherId: string; credential: Credential; canEdit: boolean; canVerify: boolean; busy: boolean;
  onEdit: () => void; onDelete: () => void; onVerify: () => void; onUnverify: () => void; onRemoveFile: () => void;
}) {
  const dates = [c.issuedOn && `Berilgan: ${formatDate(c.issuedOn)}`, c.expiresOn && `Amal qiladi: ${formatDate(c.expiresOn)} gacha`].filter(Boolean);
  return (
    <article className="rounded-xl border border-border bg-surface p-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-2">
            <h3 className="text-[14.5px] font-semibold text-ink-900">{c.title}</h3>
            {c.score && <span className="font-tabular text-[14.5px] font-semibold text-brand-700">{c.score}</span>}
          </div>
          <p className="mt-0.5 text-[12.5px] text-ink-500">
            {typeLabel(c.type)}{c.issuer ? ` · ${c.issuer}` : ""}
          </p>
          <div className="mt-2 flex flex-wrap items-center gap-2">
            {c.isVerified
              ? <Badge tone="success"><ShieldCheck className="size-3" aria-hidden />Tasdiqlangan{c.verifiedByName ? ` · ${c.verifiedByName}` : ""}</Badge>
              : <Badge tone="warning">Tasdiqlanmagan</Badge>}
            {c.isExpired && <Badge tone="danger">Muddati tugagan</Badge>}
          </div>
          {dates.length > 0 && <p className="mt-2 text-[12.5px] text-ink-600">{dates.join(" · ")}</p>}
          {c.reference && <p className="mt-1 break-all text-[12.5px] text-ink-600">Tekshirish: {c.reference}</p>}
          {c.note && <p className="mt-1.5 whitespace-pre-wrap text-[13px] text-ink-700">{c.note}</p>}
          {c.file && (
            <p className="mt-2 inline-flex items-center gap-1.5 text-[12.5px] text-ink-500">
              <FileText className="size-3.5" aria-hidden />
              {c.file.fileName} · {formatSize(c.file.size)}
            </p>
          )}
        </div>

        <div className="flex flex-wrap items-center justify-end gap-2">
          {c.file && <OpenFileButton teacherId={teacherId} credential={c} />}
          {canVerify && !c.isVerified && (
            <Button size="sm" onClick={onVerify}>
              <BadgeCheck className="size-3.5" aria-hidden />
              Tasdiqlash
            </Button>
          )}
          {canVerify && c.isVerified && (
            <Button variant="secondary" size="sm" onClick={onUnverify} disabled={busy}>
              <ShieldOff className="size-3.5" aria-hidden />
              Tasdiqni bekor qilish
            </Button>
          )}
          {canEdit && (
            <>
              {c.file && <Button variant="ghost" size="sm" onClick={onRemoveFile} disabled={busy}>Faylni olib tashlash</Button>}
              <Button variant="ghost" size="icon" aria-label="Tahrirlash" onClick={onEdit}><Pencil className="size-4" aria-hidden /></Button>
              <Button variant="ghost" size="icon" aria-label="O‘chirish" onClick={onDelete}><Trash2 className="size-4" aria-hidden /></Button>
            </>
          )}
        </div>
      </div>
    </article>
  );
}

// ── The form ──────────────────────────────────────────────────────────────────────────────────────

function CredentialDrawer({
  teacherId, credential, canVerify, isSelf, onDone, onClose,
}: {
  teacherId: string; credential: Credential | null; canVerify: boolean; isSelf: boolean;
  onDone: (notice: string | null) => Promise<void>; onClose: () => void;
}) {
  const fileInput = useRef<HTMLInputElement>(null);
  const [file, setFile] = useState<File | null>(null);
  const [validation, setValidation] = useState("");

  const save = useMutation({
    mutationFn: async ({ input, verifyNow }: { input: CredentialInput; verifyNow: boolean }) => {
      let reset = false;
      const saved = credential ? await updateCredential(teacherId, credential.id, input) : await addCredential(teacherId, input);
      reset = saved.verificationReset;
      if (file) {
        // The scan is part of what an administrator confirms, so it goes in before any confirmation, never after.
        try {
          reset = (await uploadCredentialFile(teacherId, saved.credential.id, file)).verificationReset || reset;
        } catch (e) {
          throw new Error(`Ma‘lumot saqlandi, lekin hujjat yuklanmadi: ${errorText(e, "xatolik")} Uni qayta yuklang.`);
        }
      }
      if (verifyNow) await verifyCredential(teacherId, saved.credential.id);
      return reset;
    },
    onSuccess: (reset) => onDone(reset ? withdrawnNotice(isSelf) : null),
  });

  const pickFile = (picked: File | null) => {
    setValidation("");
    if (picked && picked.size > CREDENTIAL_FILE_MAX_BYTES) {
      setValidation("Fayl 10 MB dan katta.");
      setFile(null);
      if (fileInput.current) fileInput.current.value = "";
      return;
    }
    setFile(picked);
  };

  return (
    <Drawer
      open
      title={credential ? "Yozuvni tahrirlash" : "Malaka / sertifikat"}
      description="IELTS balli, sertifikat, diplom yoki kurs. Hujjat nusxasi ixtiyoriy, lekin tasdiqlashni tezlashtiradi."
      onClose={() => { if (!save.isPending) onClose(); }}
    >
      <form
        className="space-y-4"
        onSubmit={(e) => {
          e.preventDefault();
          setValidation("");
          const form = new FormData(e.currentTarget);
          const text = (name: string) => String(form.get(name) ?? "").trim();
          const type = text("type") as CredentialType;
          if (!text("title")) return setValidation("Nomini kiriting.");
          if (type === "ExamScore" && !text("score")) return setValidation("Imtihon uchun ballni kiriting.");
          if (text("issuedOn") && text("expiresOn") && text("expiresOn") < text("issuedOn")) return setValidation("Amal qilish muddati berilgan sanadan oldin bo‘lmasin.");
          save.mutate({
            input: {
              type, title: text("title"), issuer: text("issuer") || undefined, score: text("score") || undefined,
              issuedOn: text("issuedOn") || undefined, expiresOn: text("expiresOn") || undefined,
              reference: text("reference") || undefined, note: text("note") || undefined,
            },
            verifyNow: form.get("verifyNow") === "on",
          });
        }}
      >
        {credential?.isVerified && (
          <Alert tone="info">
            Bu yozuv tasdiqlangan. Nom, beruvchi, ball, sanalar, tekshirish havolasi yoki hujjatni o‘zgartirsangiz tasdiq bekor bo‘ladi;
            faqat izohni o‘zgartirsangiz — saqlanadi.
          </Alert>
        )}
        <div>
          <Label htmlFor="cred-type">Turi</Label>
          <Select id="cred-type" name="type" defaultValue={credential?.type ?? "ExamScore"}>
            {CREDENTIAL_TYPES.map((t) => <option key={t.value} value={t.value}>{t.label}</option>)}
          </Select>
        </div>
        <div>
          <Label htmlFor="cred-title">Nomi</Label>
          <Input id="cred-title" name="title" required maxLength={200} defaultValue={credential?.title ?? ""} placeholder="IELTS Academic" />
        </div>
        <div className="grid gap-4 sm:grid-cols-2">
          <div>
            <Label htmlFor="cred-issuer">Beruvchi</Label>
            <Input id="cred-issuer" name="issuer" maxLength={200} defaultValue={credential?.issuer ?? ""} placeholder="British Council" />
          </div>
          <div>
            <Label htmlFor="cred-score">Ball / daraja</Label>
            <Input id="cred-score" name="score" maxLength={50} defaultValue={credential?.score ?? ""} placeholder="8.0 (L 8.5, R 8.0, W 7.5, S 8.0)" />
          </div>
        </div>
        <div className="grid gap-4 sm:grid-cols-2">
          <div>
            <Label htmlFor="cred-issued">Berilgan sana</Label>
            <Input id="cred-issued" name="issuedOn" type="date" defaultValue={credential?.issuedOn ?? ""} />
          </div>
          <div>
            <Label htmlFor="cred-expires">Amal qilish muddati</Label>
            <Input id="cred-expires" name="expiresOn" type="date" defaultValue={credential?.expiresOn ?? ""} />
          </div>
        </div>
        <div>
          <Label htmlFor="cred-reference">Tekshirish havolasi yoki raqami</Label>
          <Input id="cred-reference" name="reference" maxLength={500} defaultValue={credential?.reference ?? ""} placeholder="TRF raqami yoki sertifikat havolasi" />
        </div>
        <div>
          <Label htmlFor="cred-file">{credential?.file ? "Hujjatni almashtirish" : "Hujjat nusxasi (PDF yoki rasm)"}</Label>
          <input
            id="cred-file"
            ref={fileInput}
            type="file"
            accept={CREDENTIAL_FILE_ACCEPT}
            onChange={(e) => pickFile(e.target.files?.[0] ?? null)}
            className="block w-full text-[13px] text-ink-600 file:mr-3 file:rounded-lg file:border-0 file:bg-brand-solid file:px-3 file:py-2 file:text-[13px] file:font-medium file:text-white hover:file:bg-brand-solid-hover"
          />
          <p className="mt-1.5 text-[12.5px] text-ink-500">Eng katta hajm — 10 MB. Faqat siz va markaz xodimlari ko‘ra oladi.</p>
        </div>
        <div>
          <Label htmlFor="cred-note">Izoh</Label>
          <Textarea id="cred-note" name="note" rows={2} maxLength={500} defaultValue={credential?.note ?? ""} />
        </div>
        {canVerify && !credential && (
          <label className="flex items-start gap-2 text-[13px] text-ink-700">
            <input type="checkbox" name="verifyNow" className="mt-0.5 size-4" />
            Hujjatni ko‘rdim — darhol tasdiqlansin
          </label>
        )}
        {validation && <Alert>{validation}</Alert>}
        {save.isError && <Alert>{errorText(save.error, save.error instanceof Error ? save.error.message : "Saqlab bo‘lmadi.")}</Alert>}
        <Button type="submit" loading={save.isPending} className="w-full">Saqlash</Button>
      </form>
    </Drawer>
  );
}
