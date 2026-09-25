import type { Metadata } from "next";
import { headers } from "next/headers";
import { Sora, IBM_Plex_Sans, IBM_Plex_Mono } from "next/font/google";
import "./globals.css";
import { Providers } from "./providers";
import { THEME_INIT_SCRIPT } from "@/lib/theme";
import { BRAND_INIT_SCRIPT } from "@/lib/brand-init";

const sora = Sora({
  variable: "--font-display",
  subsets: ["latin"],
  weight: ["500", "600", "700"],
});

const plexSans = IBM_Plex_Sans({
  variable: "--font-body",
  subsets: ["latin"],
  weight: ["400", "500", "600"],
});

const plexMono = IBM_Plex_Mono({
  variable: "--font-mono",
  subsets: ["latin"],
  weight: ["400", "500"],
});

export const metadata: Metadata = {
  title: "EduCenter — boshqaruv paneli",
  description: "O'quv markazi avtomatlashtirish platformasi",
};

export default async function RootLayout({ children }: LayoutProps<"/">) {
  // Set by src/proxy.ts for the Content-Security-Policy. Reading it also makes every page render per request, which a
  // per-request nonce requires.
  const nonce = (await headers()).get("x-nonce") ?? undefined;

  return (
    <html
      lang="uz"
      className={`${sora.variable} ${plexSans.variable} ${plexMono.variable} h-full antialiased`}
      // THEME_INIT_SCRIPT adds data-theme to this element before hydration, which the server HTML cannot
      // contain — it is the one legitimate mismatch here, and React is told so rather than warning on it.
      suppressHydrationWarning
    >
      <head>
        {/* Sets data-theme before the first paint; see THEME_INIT_SCRIPT for why this cannot wait for React. */}
        <script nonce={nonce} dangerouslySetInnerHTML={{ __html: THEME_INIT_SCRIPT }} />
        {/* The centre's brand colour, as the last visit stored it, for the same reason (S07.01). */}
        <script nonce={nonce} dangerouslySetInnerHTML={{ __html: BRAND_INIT_SCRIPT }} />
      </head>
      <body className="min-h-full flex flex-col bg-canvas text-ink-900">
        <Providers>{children}</Providers>
      </body>
    </html>
  );
}
