import { notFound, redirect } from "next/navigation";
import {
  apiFetch,
  currentPerson,
  type DraftView,
  type VersionRow,
} from "@/lib/api";
import { Shell } from "../../shell";
import { Builder } from "./builder";
import { FormHeader } from "./form-header";

/**
 * The builder.
 *
 * The draft and the history are fetched together rather than one after the
 * other: neither needs the other's answer, and awaiting them in sequence would
 * make the screen twice as slow to arrive for no reason.
 *
 * Everything past this point is one client component. The whole screen is a
 * single document being edited — reordering a question moves it, the preview
 * follows it, the problems follow it — and splitting that across a server
 * boundary would mean a round trip per keystroke.
 */
export default async function FormBuilder({
  params,
}: {
  params: Promise<{ id: string }>;
}) {
  const { id } = await params;

  const person = await currentPerson();
  if (!person) {
    redirect("/sign-in");
  }

  const [draftResponse, versionsResponse] = await Promise.all([
    apiFetch(`/admin/forms/${id}/draft`),
    apiFetch(`/admin/forms/${id}/versions`),
  ]);

  if (draftResponse.status === 403) {
    return (
      <Shell personId={person.personId}>
        <h1>Form</h1>
        <div className="empty">
          You do not have <code>applications.view</code>. Ask an admin.
        </div>
      </Shell>
    );
  }

  if (draftResponse.status === 404) {
    notFound();
  }

  if (!draftResponse.ok) {
    return (
      <Shell personId={person.personId}>
        <h1>Form</h1>
        <div className="empty">That form could not be loaded.</div>
      </Shell>
    );
  }

  const { form, draft, published, locked } =
    (await draftResponse.json()) as DraftView;

  // History is nice to have rather than load-bearing: the builder works
  // without it, so a failure here is an absent panel and not an error page.
  const versions: VersionRow[] = versionsResponse.ok
    ? ((await versionsResponse.json()) as { versions: VersionRow[] }).versions
    : [];

  const mine = person.permissions;

  return (
    <Shell personId={person.personId}>
      <FormHeader
        form={form}
        published={published}
        draftVersion={draft.version}
        tab="questions"
      />

      <Builder
        formName={form.name}
        formId={form.id}
        initialFields={draft.fields}
        lockedKeys={locked}
        versions={versions}
        canManage={mine.has("forms.manage")}
      />
    </Shell>
  );
}
