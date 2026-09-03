import { notFound, redirect } from "next/navigation";
import { NoResponses } from "@/components/responses/no-responses";
import { Responses } from "@/components/responses/responses";
import {
  apiFetch,
  currentPerson,
  type DraftView,
  type FormSummary,
} from "@/lib/api";
import { Shell } from "../../../shell";
import { FormHeader } from "../form-header";
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
      <Denied personId={person.personId} form={form} published={published}>
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
      <FormHeader form={form} published={published} tab="responses" />

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

/**
 * Why there is nothing here, said plainly.
 *
 * With the header when the form itself loaded and only the answers did not,
 * because the way out of this screen is the tab beside it — an error page with
 * no navigation on it leaves somebody with the back button and a guess.
 */
function Denied({
  personId,
  form,
  published,
  children,
}: {
  personId: string;
  form?: FormSummary;
  published?: { version: number; publishedAt: string | null } | null;
  children: React.ReactNode;
}) {
  return (
    <Shell personId={personId}>
      {form ? (
        <FormHeader form={form} published={published ?? null} tab="responses" />
      ) : (
        <h1>Responses</h1>
      )}
      <div className="empty">{children}</div>
    </Shell>
  );
}
