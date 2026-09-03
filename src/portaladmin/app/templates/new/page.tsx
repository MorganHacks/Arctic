import Link from "next/link";
import { redirect } from "next/navigation";
import { Editor } from "@/components/templates/editor";
import { currentPerson } from "@/lib/api";
import { Shell } from "../../shell";

/**
 * Writing a template that does not exist yet.
 *
 * A page rather than a panel on the list, because this is a document being
 * written next to a preview of itself and the two need the width. Its own
 * address as well, so the compose screen can send somebody straight here when
 * nothing in the dropdown fits.
 */
export default async function NewTemplate() {
  const person = await currentPerson();
  if (!person) {
    redirect("/sign-in");
  }

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
      <Link href="/templates" className="back">
        ← Templates
      </Link>

      <h1>New template</h1>
      <p className="lede">Nothing is sent by writing one.</p>

      <Editor template={null} canManage />
    </Shell>
  );
}
