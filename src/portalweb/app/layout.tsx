import type { Metadata } from "next";
import { Inter, Instrument_Serif } from "next/font/google";
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

export default function RootLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en" className={`${inter.variable} ${display.variable}`}>
      <body>{children}</body>
    </html>
  );
}
