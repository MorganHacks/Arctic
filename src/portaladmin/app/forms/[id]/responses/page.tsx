import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { NoResponses } from "@/components/responses/no-responses";
import { Responses } from "@/components/responses/responses";
import { apiFetch, currentPerson, type DraftView } from "@/lib/api";
import { Shell } from "../../../shell";
import { readPage } from "./api";
import { loadResponses, openResponse } from "./actions";

/**
 * What people submitted.
 *
 * The form definition and the first page of answers are fetched together.
 * Neither needs the other's answer, and awaiting them in sequence would make
 * the screen twice as slow to arrive for nothing — but the two do have to be
 * put back together here, because an answer is stored under a question's key
 * and the words of the question live only in the form.
 *
 * The definition is the draft rather than the published version, so a question
 * added this morning has a column before anybody has answered it. That is also
 * why the screen cannot assume the two line up: a response from last month has
 * none of this morning's keys, and one from before a question was deleted has
 * a key no question claims. Both are ordinary and both are shown.
 */
export default async function FormResponses({
  params,
}: {
  params: Promise<{ id: string }>;
}) {
  const { id } = await params;

  const person = await currentPerson();
  if (!person) {
    redirect("/sign-in");
  }

  const [draftResponse, first] = await Promise.all([
    apiFetch(`/admin/forms/${id}/draft`),
    readPage(id, null),
  ]);

  if (draftResponse.status === 403) {
    return (
      <Denied personId={person.personId}>
        You do not have <code>applications.view</code>. Ask an admin.
      </Denied>
    );
  }

  if (draftResponse.status === 404) {
    notFound();
  }

  if (!draftResponse.ok) {
    return <Denied personId={person.personId}>That form could not be loaded.</Denied>;
  }

  const { form, draft, published } = (await draftResponse.json()) as DraftView;

  if (!first.ok) {
    return (
      <Denied personId={person.personId} name={form.name} formId={id}>
        {first.error}
      </Denied>
    );
  }

  // Cosmetic, both of them. The API refuses an export without
  // applications.export and omits a resume link without
  // applications.view_resume whether or not this screen offered either;
  // hiding them is a courtesy to somebody who cannot use them.
  const mine = person.permissions;

  return (
    <Shell personId={person.personId}>
      <Header name={form.name} formId={id}>
        {form.kind}
        {" · "}
        <code>{form.code}</code>
        {" · "}
        {published ? (
          <span className="pill active">Live · v{published.version}</span>
        ) : (
          <span className="pill lapsed">Never published</span>
        )}
      </Header>

      {/* Scaffolding, and says so. Goes with the fixtures in api.ts the moment
          the endpoints land. */}
      {first.mocked ? (
        <p className="error">
          Showing example data. The responses API is not available yet.
        </p>
      ) : null}

      {first.page.items.length === 0 ? (
        <NoResponses
          formId={id}
          publishedVersion={published?.version ?? null}
          fields={draft.fields}
        />
      ) : (
        <Responses
          fields={draft.fields}
          initialItems={first.page.items}
          initialCursor={first.page.nextCursor}
          loadMore={loadResponses.bind(null, id)}
          openResponse={openResponse.bind(null, id)}
          csvHref={
            mine.has("applications.export")
              ? `/api/admin/forms/${id}/responses.csv`
              : null
          }
          canViewResume={mine.has("applications.view_resume")}
        />
      )}
    </Shell>
  );
}

/** The form's name, and the way back to its questions. */
function Header({
  name,
  formId,
  children,
}: {
  name: string;
  formId: string;
  children?: React.ReactNode;
}) {
  return (
    <>
      <Link href="/forms" className="back">
        ← Forms
      </Link>

      <div className="form-head">
        <div>
          <h1>{name}</h1>
          {children ? (
            <p className="lede" style={{ margin: 0 }}>
              {children}
            </p>
          ) : null}
        </div>

        {/* The only route between the two halves of a form that this screen
            can build. A form is built once and read for weeks, and having to
            go back to the list in between is a tax on the weeks. */}
        <div className="tabs">
          <Link href={`/forms/${formId}`} className="tab">
            Questions
          </Link>
          <span className="tab on">Responses</span>
        </div>
      </div>
    </>
  );
}

/** Why there is nothing here, said plainly. */
function Denied({
  personId,
  name,
  formId,
  children,
}: {
  personId: string;
  name?: string;
  formId?: string;
  children: React.ReactNode;
}) {
  return (
    <Shell personId={personId}>
      {name && formId ? (
        <Header name={name} formId={formId} />
      ) : (
        <h1>Responses</h1>
      )}
      <div className="empty">{children}</div>
    </Shell>
  );
}
