import { ErrorToasts } from "@/components/ui/error-toast";
import type { Metadata } from "next";
import { cookies } from "next/headers";
import { Geist, Geist_Mono, Inter } from "next/font/google";
import localFont from "next/font/local";
import { READING_MODE, isOn } from "@/lib/features";
import { READING_MODE_COOKIE, initialReadingMode } from "../../../libs/ui/reading-mode";
import { SidebarStateProvider } from "./sidebar-state";
import { ConsoleShell } from "./shell";
import "../../../libs/ui/reading-mode.css";
import "./globals.css";

const geist = Geist({
  subsets: ["latin"],
  display: "swap",
  variable: "--font-geist-sans",
});

const geistMono = Geist_Mono({
  subsets: ["latin"],
  display: "swap",
  variable: "--font-geist-mono",
});

const inter = Inter({
  subsets: ["latin"],
  display: "swap",
  variable: "--font-inter",
});

/**
 * The reading face, behind enable_reading_mode_feature.
 *
 * OpenDyslexic from @fontsource/opendyslexic — SIL OFL 1.1, which permits web
 * distribution, so it is self-hosted from npm rather than committed here. Next
 * hashes and serves it, so no CDN is involved.
 *
 * Regular and bold only. This console uses 600-weight emphasis throughout and a
 * real bold file looks better than synthetic weight.
 *
 * `preload: false`, because the flag is off by default and preloading a face
 * almost nobody uses would cost every organizer a request.
 */
const openDyslexic = localFont({
  src: [
    {
      path: "../node_modules/@fontsource/opendyslexic/files/opendyslexic-latin-400-normal.woff2",
      weight: "400",
      style: "normal",
    },
    {
      path: "../node_modules/@fontsource/opendyslexic/files/opendyslexic-latin-700-normal.woff2",
      weight: "700",
      style: "normal",
    },
  ],
  display: "swap",
  preload: false,
  variable: "--font-opendyslexic",
  fallback: ["sans-serif"],
});

export const metadata: Metadata = {
  title: "MorganHacks console",
  // Nothing here should ever be indexed or previewed. It is behind a login and
  // everything past it is somebody's personal data.
  robots: { index: false, follow: false },
};

export default async function RootLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  const stored = (await cookies()).get("arctic_sidebar_collapsed")?.value;
  const initialCollapsed = stored === "true" ? true : stored === "false" ? false : undefined;

  // Reading mode, resolved on the server so the first byte already carries it.
  // `undefined` (no cookie) is left for the client to settle from the OS
  // preference; see libs/ui/reading-mode.ts.
  const reading = isOn(READING_MODE)
    ? initialReadingMode((await cookies()).get(READING_MODE_COOKIE)?.value)
    : undefined;

  return (
    <html
      lang="en"
      data-theme="light"
      data-reading={reading ? "on" : undefined}
      className={`${geist.variable} ${geistMono.variable} ${inter.variable} ${openDyslexic.variable}`}
    >
      <body>
        <SidebarStateProvider initialCollapsed={initialCollapsed}>
          <ConsoleShell>{children}</ConsoleShell>
        </SidebarStateProvider>
        <ErrorToasts />
      </body>
    </html>
  );
}
