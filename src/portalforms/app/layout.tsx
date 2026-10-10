import { ErrorToasts } from "@/components/ui/error-toast";
import type { Metadata } from "next";
import { cookies } from "next/headers";
import { Geist, Inter } from "next/font/google";
import localFont from "next/font/local";
import { READING_MODE, isOn } from "@/lib/features";
import { READING_MODE_COOKIE, initialReadingMode } from "../../../libs/ui/reading-mode";
import "../../../libs/ui/reading-mode.css";
import "./globals.css";

const inter = Inter({
  subsets: ["latin"],
  display: "swap",
  variable: "--font-inter",
});

const geist = Geist({ subsets: ["latin"], display: "swap", variable: "--font-geist-sans", preload: false });

/**
 * The reading face, behind enable_reading_mode_feature.
 *
 * OpenDyslexic from @fontsource/opendyslexic — SIL OFL 1.1, which permits web
 * distribution, so it is self-hosted from npm rather than committed here. Next
 * hashes and serves it, so no CDN is involved.
 *
 * Regular and bold only; no italic, because nothing in reading mode sets any.
 *
 * `preload: false`, because the flag is off by default and preloading a face
 * almost nobody uses would cost every applicant a request. The application form
 * is the longest stretch of reading an applicant does in this system, which is
 * the reason this app reads the flag at all.
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
  title: "MorganHacks",

  /*
   * Never indexed, and this one is not a formality.
   *
   * A form's code is seven random characters precisely so that holding the
   * link is the whole permission. Letting a crawler find one and publish it
   * turns an unlisted form into a public one — and for the application form
   * that means a search result anybody can submit through, months after
   * registration closed.
   */
  robots: { index: false, follow: false },
};

export default async function RootLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  // Reading mode, resolved on the server so the first byte already carries it.
  // `undefined` means no cookie, and the client's OS preference settles it
  // after mount; see libs/ui/reading-mode.ts.
  const reading = isOn(READING_MODE)
    ? initialReadingMode((await cookies()).get(READING_MODE_COOKIE)?.value)
    : undefined;

  return (
    <html
      lang="en"
      className={`${inter.variable} ${geist.variable} ${openDyslexic.variable}`}
      data-theme="light"
      data-reading={reading ? "on" : undefined}
    >
      <body>{children}<ErrorToasts /></body>
    </html>
  );
}
