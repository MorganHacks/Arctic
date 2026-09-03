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
      <body>
        {/*
         * The wordmark, in the same place on every page here.
         *
         * In the layout rather than on the form, because there are six pages
         * on this site — the form, a form that closed, a code that leads
         * nowhere, the sign-in step, the error and the page after submitting —
         * and a mark that moves between them is one somebody has to look for
         * again each time. Text, because there is no logo file in this
         * repository and inventing one is not this page's decision to make.
         *
         * Not a link. It would have to point at something, and what lives at
         * the apex domain is not this year's site.
         */}
        <header className="chrome">
          <p className="wordmark">MorganHacks</p>
        </header>

        {children}
      </body>
    </html>
  );
}
