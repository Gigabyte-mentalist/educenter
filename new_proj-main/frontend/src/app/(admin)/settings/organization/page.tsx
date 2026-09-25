"use client";

import { useRef, useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Building2, ImagePlus, Lock, Trash2 } from "lucide-react";
import { ApiError } from "@/lib/api-client";
import { MIN_WHITE_CONTRAST, whiteContrast } from "@/lib/brand";
import {
  CURRENCIES, CURRENCY_LABEL, deleteOrganizationLogo, LOGO_EXTENSIONS, LOGO_MAX_BYTES, updateOrganization,
  uploadOrganizationLogo, type OrganizationSettings,
} from "@/lib/api/organization";
import { usePermissions } from "@/lib/use-permissions";
import { BrandMark, ORGANIZATION_QUERY_KEY, useBrand, useOrganization } from "@/components/OrganizationBrand";
import { Alert } from "@/components/ui/Alert";
import { Button } from "@/components/ui/Button";
import { EmptyState } from "@/components/ui/EmptyState";
import { Input, Label, Select } from "@/components/ui/Field";
import { PageHeader } from "@/components/ui/PageHeader";

/** The product's own colour — what the picker shows while the centre has not chosen one. */
const PRODUCT_COLOR = "#26645a";
const LOGO_MAX_MB = LOGO_MAX_BYTES / (1024 * 1024);

const errorMessage = (error: unknown) => (error instanceof ApiError ? error.message : error ? "Amalni bajarib bo‘lmadi." : null);

export default function OrganizationSettingsPage() {
  const { can, ready } = usePermissions();
  const organization = useOrganization();

  if (ready && !can("org.settings.edit")) {
    return (
      <main className="p-4 sm:p-6 lg:p-8">
        <EmptyState icon={Lock} title="Bu sahifa uchun ruxsat yo‘q" description="Tashkilot sozlamalarini faqat Super Admin o‘zgartiradi." />
      </main>
    );
  }

  return (
    <main className="p-4 sm:p-6 lg:p-8">
      <PageHeader
        title="Tashkilot"
        description="Markaz nomi, logosi, brend rangi, valyutasi va ish vaqti. O‘zgarishlar hammaning ekranida ko‘rinadi."
      />
      {organization.isError && <Alert>{errorMessage(organization.error)}</Alert>}
      {organization.data && (
        <div className="grid max-w-4xl gap-6 lg:grid-cols-[1fr_18rem]">
          <SettingsForm organization={organization.data} />
          <LogoCard organization={organization.data} />
        </div>
      )}
    </main>
  );
}

function SettingsForm({ organization }: { organization: OrganizationSettings }) {
  const queryClient = useQueryClient();
  const [name, setName] = useState(organization.name);
  const [currency, setCurrency] = useState(organization.currency);
  const [workdayStart, setWorkdayStart] = useState(organization.workdayStart);
  const [workdayEnd, setWorkdayEnd] = useState(organization.workdayEnd);
  const [brandColor, setBrandColor] = useState(organization.brandColor ?? "");
  const [saved, setSaved] = useState(false);

  const contrast = brandColor ? whiteContrast(brandColor) : null;
  const colorProblem =
    brandColor && contrast === null ? "Rang #RRGGBB ko‘rinishida bo‘lishi kerak (masalan #26645a)."
    : contrast !== null && contrast < MIN_WHITE_CONTRAST
      ? `Bu rang juda och: oq matn uning ustida yomon o‘qiladi (kontrast ${contrast.toFixed(1)}:1, kamida ${MIN_WHITE_CONTRAST}:1 kerak).`
      : null;
  const hoursProblem = workdayEnd <= workdayStart ? "Ish tugashi boshlanishidan keyin bo‘lishi kerak." : null;
  const canSave = name.trim().length > 0 && !colorProblem && !hoursProblem;

  const save = useMutation({
    mutationFn: () => updateOrganization(organization.id, { name, currency, workdayStart, workdayEnd, brandColor }),
    onSuccess: (updated) => {
      queryClient.setQueryData(ORGANIZATION_QUERY_KEY, updated);
      setSaved(true);
    },
  });

  return (
    <form
      className="space-y-5 rounded-xl border border-border bg-surface p-5"
      onSubmit={(event) => {
        event.preventDefault();
        setSaved(false);
        save.mutate();
      }}
    >
      <div>
        <Label htmlFor="org-name">Markaz nomi</Label>
        <Input id="org-name" value={name} maxLength={200} required onChange={(e) => setName(e.target.value)} />
        <p className="mt-1.5 text-[12.5px] text-ink-500">Yon panelda va kirish sahifasida ko‘rinadi.</p>
      </div>

      <div>
        <Label htmlFor="org-currency">Valyuta</Label>
        <Select id="org-currency" value={currency} onChange={(e) => setCurrency(e.target.value)}>
          {CURRENCIES.map((code) => (
            <option key={code} value={code}>{CURRENCY_LABEL[code]}</option>
          ))}
        </Select>
        <p className="mt-1.5 text-[12.5px] text-ink-500">
          Yangi ko‘rsatiladigan summalar shu valyutada yoziladi. Allaqachon chiqarilgan invoyslar o‘z valyutasida qoladi.
        </p>
      </div>

      <div className="grid gap-4 sm:grid-cols-2">
        <div>
          <Label htmlFor="org-start">Ish boshlanishi</Label>
          <Input id="org-start" type="time" value={workdayStart} required onChange={(e) => setWorkdayStart(e.target.value)} />
        </div>
        <div>
          <Label htmlFor="org-end">Ish tugashi</Label>
          <Input id="org-end" type="time" value={workdayEnd} required onChange={(e) => setWorkdayEnd(e.target.value)} />
        </div>
        {hoursProblem && <p className="text-[13px] text-danger-600 sm:col-span-2">{hoursProblem}</p>}
        <p className="text-[12.5px] text-ink-500 sm:col-span-2">Dars qo‘shish formasida boshlanish vaqti shu bo‘yicha taklif qilinadi.</p>
      </div>

      <div>
        <Label htmlFor="org-color-text">Brend rangi</Label>
        <div className="flex flex-wrap items-center gap-3">
          <input
            type="color" aria-label="Rang tanlash" value={colorProblem || !brandColor ? PRODUCT_COLOR : brandColor}
            onChange={(e) => setBrandColor(e.target.value)}
            className="h-9 w-12 cursor-pointer rounded-lg border border-border-strong bg-surface p-1"
          />
          <Input
            id="org-color-text" value={brandColor} placeholder={`${PRODUCT_COLOR} (standart)`} maxLength={7} spellCheck={false}
            onChange={(e) => setBrandColor(e.target.value.trim())} className="w-40 font-mono"
          />
          {brandColor && (
            <Button type="button" size="sm" variant="ghost" onClick={() => setBrandColor("")}>Standart rangga qaytarish</Button>
          )}
        </div>
        {colorProblem ? (
          <p className="mt-1.5 text-[13px] text-danger-600">{colorProblem}</p>
        ) : (
          <div className="mt-3 flex flex-wrap items-center gap-3">
            {/* The swatch is the real thing: white text on the chosen colour, which is exactly what buttons and badges will be. */}
            <span
              className="inline-flex h-9 items-center rounded-lg px-4 text-sm font-medium text-white"
              style={{ backgroundColor: brandColor || PRODUCT_COLOR }}
            >
              Tugma namunasi
            </span>
            {contrast !== null && <span className="text-[12.5px] text-ink-500">Kontrast {contrast.toFixed(1)}:1</span>}
          </div>
        )}
        <p className="mt-1.5 text-[12.5px] text-ink-500">Tugmalar, belgilar va yon paneldagi faol bo‘lim shu rangda bo‘ladi. Bo‘sh qoldirsangiz — standart yashil.</p>
      </div>

      {save.isError && <Alert>{errorMessage(save.error)}</Alert>}
      {saved && !save.isPending && <Alert tone="success">Saqlandi. Yangi sozlamalar hammaning ekranida ko‘rinadi.</Alert>}

      <div className="flex justify-end">
        <Button type="submit" loading={save.isPending} disabled={!canSave}>Saqlash</Button>
      </div>
    </form>
  );
}

function LogoCard({ organization }: { organization: OrganizationSettings }) {
  const queryClient = useQueryClient();
  const input = useRef<HTMLInputElement>(null);
  const brand = useBrand();
  const [problem, setProblem] = useState<string | null>(null);

  const done = (updated?: OrganizationSettings) => {
    if (updated) queryClient.setQueryData(ORGANIZATION_QUERY_KEY, updated);
    else queryClient.invalidateQueries({ queryKey: ORGANIZATION_QUERY_KEY });
  };
  const upload = useMutation({ mutationFn: (file: File) => uploadOrganizationLogo(organization.id, file), onSuccess: done });
  const remove = useMutation({ mutationFn: () => deleteOrganizationLogo(organization.id), onSuccess: () => done() });
  const error = problem ?? errorMessage(upload.error) ?? errorMessage(remove.error);

  return (
    <div className="h-fit space-y-4 rounded-xl border border-border bg-surface p-5">
      <p className="text-[13px] font-medium text-ink-700">Logo</p>
      <div className="flex items-center gap-4">
        <BrandMark logo={brand.logo} className="size-16 border border-border" />
        <p className="text-[12.5px] text-ink-500">
          {organization.logoVersion ? "Yon panelda va kirish sahifasida ko‘rinadi." : "Logo yo‘q — standart belgi ko‘rinadi."}
        </p>
      </div>

      <input
        ref={input} type="file" accept={LOGO_EXTENSIONS.join(",")} className="sr-only" aria-label="Logo tanlash" tabIndex={-1}
        onChange={(event) => {
          const file = event.target.files?.[0];
          event.target.value = ""; // choosing the same file again must still fire
          if (!file) return;
          const name = file.name.toLowerCase();
          const found = !LOGO_EXTENSIONS.some((extension) => name.endsWith(extension))
            ? `Logo faqat rasm bo‘lsin: ${LOGO_EXTENSIONS.join(", ")} (SVG qabul qilinmaydi).`
            : file.size > LOGO_MAX_BYTES ? `Logo ${LOGO_MAX_MB} MB dan katta.` : null;
          setProblem(found);
          if (!found) upload.mutate(file);
        }}
      />
      <div className="flex flex-wrap gap-2">
        <Button size="sm" variant="secondary" loading={upload.isPending} onClick={() => input.current?.click()}>
          <ImagePlus className="size-3.5" aria-hidden />
          {organization.logoVersion ? "Logoni almashtirish" : "Logo yuklash"}
        </Button>
        {organization.logoVersion && (
          <Button size="sm" variant="ghost" loading={remove.isPending} onClick={() => { setProblem(null); remove.mutate(); }}>
            <Trash2 className="size-3.5" aria-hidden />
            Olib tashlash
          </Button>
        )}
      </div>
      <p className="flex items-start gap-1.5 text-[12.5px] text-ink-500">
        <Building2 className="mt-0.5 size-3.5 shrink-0" aria-hidden />
        PNG, JPG yoki WebP, {LOGO_MAX_MB} MB gacha. Kvadrat rasm yaxshi ko‘rinadi.
      </p>
      {error && <Alert>{error}</Alert>}
    </div>
  );
}
