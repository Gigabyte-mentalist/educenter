import { NextResponse, type NextRequest } from "next/server";

/**
 * Content-Security-Policy, with a fresh nonce per request (docs: guides/content-security-policy).
 *
 * The access and refresh tokens live in localStorage, so a single injected script would be able to read them and take over
 * the signed-in account. This policy is what makes that injection fail: only scripts carrying the nonce run (`'strict-dynamic'`
 * extends that to the chunks they load), inline handlers and `javascript:` links are dead, nothing can be framed or posted
 * to another site, and the page can only talk to its own origin. It is why every page is rendered per request.
 *
 * `style-src-attr 'unsafe-inline'` is deliberate: the UI sets widths on progress bars with `style=""` attributes, which a
 * nonce cannot cover, and an inline style attribute cannot run script. Style *elements* stay nonce-only.
 *
 * Production builds only. `next dev` needs eval and injects unnonced styles, and the dev setup calls the API on another
 * port (see api-client.ts) — a policy there would add friction, not safety, on a developer's machine.
 */
export function proxy(request: NextRequest) {
  if (process.env.NODE_ENV === "development") return NextResponse.next();

  const nonce = Buffer.from(crypto.randomUUID()).toString("base64");

  // SignalR opens a WebSocket to the same host; not every browser counts wss as "self". The Host header is
  // client-controlled and ends up inside a header value, so only a plain host[:port] is ever echoed into it.
  const host = request.headers.get("host") ?? "";
  const socketOrigin = /^[a-z0-9.-]+(:\d{1,5})?$/i.test(host) ? ` wss://${host}` : "";

  // Same-origin by default (NEXT_PUBLIC_API_URL=""); a separately hosted API has to be named to be reachable.
  const apiOrigin = originOf(process.env.NEXT_PUBLIC_API_URL);

  const csp = [
    "default-src 'self'",
    `script-src 'self' 'nonce-${nonce}' 'strict-dynamic'`,
    `style-src 'self' 'nonce-${nonce}'`,
    `style-src-elem 'self' 'nonce-${nonce}'`,
    "style-src-attr 'unsafe-inline'",
    "img-src 'self' blob: data:",
    "font-src 'self'",
    "media-src 'self' blob:",
    `connect-src 'self'${socketOrigin}${apiOrigin ? ` ${apiOrigin}` : ""}`,
    "object-src 'none'",
    "base-uri 'self'",
    "form-action 'self'",
    "frame-ancestors 'none'",
    "upgrade-insecure-requests",
  ].join("; ");

  const requestHeaders = new Headers(request.headers);
  requestHeaders.set("x-nonce", nonce);
  requestHeaders.set("Content-Security-Policy", csp);

  const response = NextResponse.next({ request: { headers: requestHeaders } });
  response.headers.set("Content-Security-Policy", csp);
  return response;
}

function originOf(url: string | undefined): string | null {
  if (!url) return null;
  try {
    return new URL(url).origin;
  } catch {
    return null;
  }
}

export const config = {
  matcher: [
    {
      // Pages only: static assets carry no markup to protect, and prefetches must not burn a nonce.
      source: "/((?!_next/static|_next/image|favicon.ico|.*\\.(?:svg|png|jpg|jpeg|gif|webp|ico)$).*)",
      missing: [
        { type: "header", key: "next-router-prefetch" },
        { type: "header", key: "purpose", value: "prefetch" },
      ],
    },
  ],
};
