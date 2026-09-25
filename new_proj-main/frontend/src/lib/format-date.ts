/**
 * Uzbek dates, written out rather than delegated to `toLocaleDateString("uz-UZ", …)`.
 *
 * The ICU data shipped with Node and with several browsers has no month names for `uz-UZ`, so it falls back
 * to the numeric placeholder and renders "17-sentabr" as "M09 17". That is not a formatting preference, it is
 * a wrong date on the screen, and it appears everywhere a deadline or a payment due date is shown. Times are
 * left to the platform: those are digits, and the platform gets digits right.
 */

const MONTHS = [
  "yanvar", "fevral", "mart", "aprel", "may", "iyun",
  "iyul", "avgust", "sentabr", "oktabr", "noyabr", "dekabr",
] as const;

const WEEKDAYS = [
  "yakshanba", "dushanba", "seshanba", "chorshanba", "payshanba", "juma", "shanba",
] as const;

const asDate = (value: string | Date): Date => (value instanceof Date ? value : new Date(value));

const time = (d: Date) =>
  `${String(d.getHours()).padStart(2, "0")}:${String(d.getMinutes()).padStart(2, "0")}`;

/** "17-sentabr" — the form used for a deadline or a lesson inside the current year. */
export function formatDayMonth(value: string | Date): string {
  const d = asDate(value);
  return `${d.getDate()}-${MONTHS[d.getMonth()]}`;
}

/** "17-sentabr, 2026" — used once a date is far enough away that the year matters. */
export function formatDate(value: string | Date): string {
  const d = asDate(value);
  return `${d.getDate()}-${MONTHS[d.getMonth()]}, ${d.getFullYear()}`;
}

/** "sentabr 2026" — a month given as "2026-09", as an exam trend or a monthly report labels it. */
export function formatYearMonth(value: string): string {
  const [year, month] = value.split("-");
  return `${MONTHS[Number(month) - 1] ?? month} ${year}`;
}

/** "17-sentabr, 2026 14:30" — a timestamp a person reads, e.g. an audit row or a message header. */
export function formatDateTime(value: string | Date): string {
  const d = asDate(value);
  return `${formatDate(d)} ${time(d)}`;
}

/** "17.09.2026" — for dense tables where a written-out month would not fit. */
export function formatShortDate(value: string | Date): string {
  const d = asDate(value);
  return `${String(d.getDate()).padStart(2, "0")}.${String(d.getMonth() + 1).padStart(2, "0")}.${d.getFullYear()}`;
}

/** "17.09.2026 14:30". */
export function formatShortDateTime(value: string | Date): string {
  return `${formatShortDate(value)} ${time(asDate(value))}`;
}

export const formatTime = (value: string | Date) => time(asDate(value));

/** "Dushanba" — a weekly schedule's column heading, capitalized since it opens the label. */
export function formatWeekday(value: string | Date): string {
  const name = WEEKDAYS[asDate(value).getDay()];
  return name[0].toUpperCase() + name.slice(1);
}

/** "Bugun" / "Kecha" / "payshanba, 17-sentabr" — the separator between days in a message list. */
export function formatDayHeading(value: string | Date): string {
  const d = asDate(value);
  const today = new Date();
  const sameDay = (a: Date, b: Date) =>
    a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate();

  if (sameDay(d, today)) return "Bugun";
  const yesterday = new Date(today);
  yesterday.setDate(today.getDate() - 1);
  if (sameDay(d, yesterday)) return "Kecha";

  return d.getFullYear() === today.getFullYear()
    ? `${WEEKDAYS[d.getDay()]}, ${formatDayMonth(d)}`
    : formatDate(d);
}
