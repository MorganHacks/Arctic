import { Editor } from "@/components/templates/editor";
import { readPageData } from "@/lib/page-data";
import { Shell } from "../../shell";
import { readPlaceholders } from "../api";

/**
 * Writing a template that does not exist yet.
 *
 * A page rather than a panel on the list, because this is a document being
 * written next to a preview of itself and the two need the width. Its own
 * address as well, so the compose screen can send somebody straight here when
 * nothing in the dropdown fits.
 */
export default async function NewTemplate({
  searchParams,
}: {
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}) {
  const query = await searchParams;

  /*
   * The campaign this is being written for, where somebody arrived from one.
   *
   * Almost always absent here — a new template usually predates every campaign
   * that will ever use it — but the compose screen can send somebody straight
   * to this address, and when it does the names offered should be the ones
   * that campaign's recipients can fill rather than every name in the system.
   */
  const campaign =
    typeof query.campaign === "string" && query.campaign !== ""
      ? query.campaign
      : null;

  const { person, data: names } = await readPageData(() => readPlaceholders(campaign));

  if (!person.permissions.has("email.manage_templates")) {
    return (
      <Shell personId={person.personId}>
        <h1>New template</h1>
        <div className="empty">
          You do not have <code>email.manage_templates</code>. Ask an admin.
        </div>
      </Shell>
    );
  }

  return (
    <Shell personId={person.personId}>
      <Editor
        key={`${person.personId}:new`}
        personId={person.personId}
        template={null}
        defaultRecipient={person.email ?? ""}
        canManage
        available={names.ok ? names.items : null}
      />
    </Shell>
  );
}
