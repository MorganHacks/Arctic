import { currentPerson } from "@/lib/api";
import { sectionsFor } from "./sections";
import { SidebarLayout, SidebarPage } from "./sidebar";
import { displayName } from "@/lib/person-profile";
import { READING_MODE, isOn } from "@/lib/features";
import { READING_MODE_COOKIE, initialReadingMode } from "../../../libs/ui/reading-mode";
import { cookies } from "next/headers";

type SidebarOptions = {
  fullName?: string | null;
  email?: string | null;
  templateCount?: number;
};

/**
 * Reading mode, resolved here rather than in the client sidebar.
 *
 * `isOn` is server-only by design, so the flag cannot be read in a "use client"
 * component — which means the shell decides and passes the answer down, the
 * same way it passes identity and permissions.
 *
 * Undefined means no cookie, and the client settles it from the OS preference
 * after mount.
 */
async function readingMode() {
  if (!isOn(READING_MODE)) return undefined;
  return { enabled: true as const, initial: initialReadingMode((await cookies()).get(READING_MODE_COOKIE)?.value) };
}

async function sidebarProps({ fullName, email, templateCount }: SidebarOptions = {}) {
  const person = await currentPerson();
  const address = email ?? person?.email ?? null;
  return {
    sections: sectionsFor(
      person?.permissions ?? new Set<string>(),
      templateCount === undefined ? {} : { "/templates": templateCount },
    ),
    identity: {
      label: displayName(person?.fullName ?? fullName, address),
      email: address,
      avatarUrl: person?.avatarUrl ?? null,
    },
  };
}

export async function ConsoleShell({ children }: { children: React.ReactNode }) {
  return (
    <SidebarLayout {...await sidebarProps()} reading={await readingMode()}>
      {children}
    </SidebarLayout>
  );
}

/** The server owns identity and permissions; the sidebar only renders them. */
export async function Shell({
  personId,
  fullName,
  email,
  templateCount,
  children,
}: {
  personId: string;
  fullName?: string | null;
  email?: string | null;
  templateCount?: number;
  children: React.ReactNode;
}) {
  // currentPerson is memoized per request, including the page's earlier check.
  return (
    <SidebarPage {...await sidebarProps({ fullName, email, templateCount })}>
      {children}
    </SidebarPage>
  );
}
