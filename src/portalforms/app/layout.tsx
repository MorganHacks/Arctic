import type { Metadata } from "next";
import { Inter } from "next/font/google";
import "./globals.css";

const inter = Inter({
  subsets: ["latin"],
  display: "swap",
  variable: "--font-inter",
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

export default function RootLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en" className={inter.variable}>
      <body>{children}</body>
    </html>
  );
}
