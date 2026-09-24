import { currentPerson } from "@/lib/api";
import { sectionsFor } from "./sections";
import { SidebarLayout, SidebarPage } from "./sidebar";
import { displayName } from "@/lib/person-profile";

type SidebarOptions = {
  fullName?: string | null;
  email?: string | null;
  templateCount?: number;
};

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
  return <SidebarLayout {...await sidebarProps()}>{children}</SidebarLayout>;
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
