/**
 * A link a user typed for others to click, made safe to put in an `href`. Anything that is not plain http(s) — above all
 * `javascript:` and `data:` — becomes `null`, so a hostile value stored in the database can never run as script in the
 * reader's signed-in session. The server refuses such links on the way in; this covers rows that predate that check.
 */
export function safeHttpUrl(value: string | null | undefined): string | null {
  if (!value) return null;
  try {
    const parsed = new URL(value);
    return parsed.protocol === "http:" || parsed.protocol === "https:" ? parsed.href : null;
  } catch {
    return null;
  }
}
