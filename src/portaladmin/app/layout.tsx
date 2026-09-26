import { ErrorToasts } from "@/components/ui/error-toast";
import type { Metadata } from "next";
import { cookies } from "next/headers";
import { Geist, Geist_Mono, Inter } from "next/font/google";
import { SidebarStateProvider } from "./sidebar-state";
import { ConsoleShell } from "./shell";
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

export const metadata: Metadata = {
  title: "MorganHacks console",
  // Nothing here should ever be indexed or previewed. It is behind a login and
  // everything past it is somebody's personal data.
  robots: { index: false, follow: false },
};

export default async function RootLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  const preference = (await cookies()).get("arctic_sidebar_collapsed")?.value;
  const initialCollapsed = preference === "true" ? true : preference === "false" ? false : undefined;

  return (
    <html
      lang="en"
      data-theme="light"
      className={`${geist.variable} ${geistMono.variable} ${inter.variable}`}
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
