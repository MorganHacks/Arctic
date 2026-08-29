import type { Metadata } from "next";
import { Inter } from "next/font/google";
import { siteConfig } from "@/site.config";
import "./globals.css";

const inter = Inter({
  subsets: ["latin"],
  weight: ["400", "600", "700"],
  display: "swap",
  variable: "--font-inter",
});

const description =
  "Morgan State's student hackathon. We're building the team that runs the next one.";

export const metadata: Metadata = {
  metadataBase: new URL(siteConfig.url),
  title: "MorganHacks — Morgan State's student hackathon",
  description,
  openGraph: {
    title: "MorganHacks",
    description,
    url: siteConfig.url,
    siteName: "MorganHacks",
    locale: "en_US",
    type: "website",
    // TODO: add `images: ["/og.png"]` once an OG image exists.
  },
};

export default function RootLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en" className={inter.variable}>
      <body>{children}</body>
    </html>
  );
}
