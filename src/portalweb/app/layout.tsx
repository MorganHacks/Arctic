import type { Metadata } from "next";
import { cookies } from "next/headers";
import { Inter, Instrument_Serif } from "next/font/google";
import localFont from "next/font/local";
import { READING_MODE, isOn } from "@/lib/features";
import {
  READING_MODE_COOKIE,
  initialReadingMode,
} from "../../../libs/ui/reading-mode";
import { siteConfig } from "@/site.config";

// No stylesheet here on purpose. This app serves two things with nothing in
// common but a hostname — the public page and the hacker portal — and each
// route group brings its own. globals.css locks the body to one screen, which
// is right for the recruitment page and would leave the portal unable to
// scroll past the fold.

const inter = Inter({
  subsets: ["latin"],
  display: "swap",
  variable: "--font-inter",
});

/**
 * The reading face, behind enable_reading_mode_feature.
 *
 * OpenDyslexic, from @fontsource/opendyslexic rather than committed to this
 * repository — it is SIL OFL 1.1, which permits web distribution, and taking
 * it from npm keeps a font binary out of the tree while still self-hosting it.
 * Next hashes and serves the file itself, so nothing is fetched from a CDN.
 *
 * Regular and bold only. The console leans on 600-weight emphasis and two real
 * files look better than synthetic weight between them; an italic is not
 * offered because nothing in reading mode sets any.
 *
 * `preload: false` because reading mode is off by default. Preloading a face
 * almost nobody uses costs every applicant a request and a chunk of bandwidth
 * to save a reader who has it on a fraction of a second.
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
  // The family name comes from the font file, so this is the name the CSS in
  // libs/ui/reading-mode.css refers to. next/font/local cannot rename the
  // family, and renaming it is what the OFL forbids without a new name anyway.
  fallback: ["sans-serif"],
});

/** Display face. Carries the whole page, so it is the only other font loaded. */
const display = Instrument_Serif({
  subsets: ["latin"],
  weight: "400",
  style: ["normal", "italic"],
  display: "swap",
  variable: "--font-display",
});

const title = `MorganHacks ${siteConfig.year} — organizer applications`;
const description = `Organizer applications for MorganHacks ${siteConfig.year} are open to all college students. Applications close ${siteConfig.deadline.weekday}, ${siteConfig.deadline.date} at ${siteConfig.deadline.time}.`;

export const metadata: Metadata = {
  metadataBase: new URL(siteConfig.url),
  title,
  description,
  openGraph: {
    title,
    description,
    url: siteConfig.url,
    siteName: "MorganHacks",
    locale: "en_US",
    type: "website",
    // The image itself comes from app/opengraph-image.tsx, which Next wires up
    // automatically — listing it here as well would only let the two drift.
  },
  twitter: { card: "summary_large_image", title, description },
};

export default async function RootLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  // Reading mode, decided on the server so the very first byte already carries
  // it. `undefined` means the cookie was absent and the client's OS preference
  // decides after mount — see libs/ui/reading-mode.ts for why those two cases
  // are deliberately different.
  const reading = isOn(READING_MODE) ? initialReadingMode((await cookies()).get(READING_MODE_COOKIE)?.value) : undefined;

  return (
    <html
      lang="en"
      data-reading={reading ? "on" : undefined}
      // Pinned, the same way the console pins it, and for the same reason: the
      // MorganHacks mark is drawn for a light ground. Following the reader's
      // system preference meant an applicant on a dark laptop and an organizer
      // looking at the console were seeing two different products, which is
      // not a thing either of them should have to work out.
      //
      // libs/ui/tokens.css carries both palettes, so this chooses between them
      // rather than overriding anything. Supporting dark properly is a real
      // option and a bigger piece of work -- the mark needs a treatment that
      // holds on a dark ground before the preference can be honoured again.
      data-theme="light"
      className={`${inter.variable} ${display.variable} ${openDyslexic.variable}`}
    >
      <body>{children}</body>
    </html>
  );
}
