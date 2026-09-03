import type { Metadata } from "next";
import { loadForm } from "@/lib/api";
import { NoForm } from "../no-form";
import { Questions } from "./questions";
import { SignIn } from "./sign-in";

type Props = {
  params: Promise<{ code: string }>;

  /**
   * Only ever read for `link`, which a refused sign-in link sets.
   *
   * The flag says a link did not work and never which way. Expired, already
   * spent and never issued are one answer, because telling them apart only
   * helps somebody probing links — the same rule the portal's sign-in page
   * follows.
   */
  searchParams: Promise<{ link?: string }>;
};

/**
 * The form's own name in the tab.
 *
 * Worth the second call: somebody applying has three tabs open and "Untitled"
 * on all of them is how they lose the one they were filling in.
 */
export async function generateMetadata({
  params,
}: Pick<Props, "params">): Promise<Metadata> {
  const { code } = await params;
  const form = await loadForm(code);

  return { title: form ? `${form.name} — MorganHacks` : "MorganHacks" };
}

/**
 * One form, at forms.morganhacks.com/&lt;code&gt;.
 *
 * The questions are fetched here rather than in the browser, so the form is in
 * the HTML on first paint. On the connection this is actually used over —
 * campus wifi, a phone, a link somebody just read off a whiteboard — the
 * alternative is a blank page followed by a spinner followed by the form.
 */
export default async function FormPage({ params, searchParams }: Props) {
  const [{ code }, query] = await Promise.all([params, searchParams]);
  const form = await loadForm(code);

  /*
   * Rendered here rather than through `notFound()`, and the difference is
   * visible on a phone.
   *
   * `notFound()` streams an error shell whose body arrives in the flight
   * payload, so the message only appears once React has run. That costs a
   * blank screen on the one page most likely to be somebody's first request
   * on campus wifi — and the reader of this page is a person who mistyped one
   * of seven characters, not a crawler that needs the status code.
   *
   * The cost is that this answers 200. Acceptable: every page here is
   * noindex, nofollow, so nothing we care about is reading the status.
   */
  if (!form) {
    return <NoForm />;
  }

  /*
   * Closed and empty are separate answers, and conflating them told somebody
   * with a live link that the deadline had passed.
   *
   * A form that is open answers with its questions. One that is open and has
   * none is not a form anybody can fill in, which is the same thing an
   * unpublished form is from out here — so it gets the same page, for the same
   * reason: nothing about which codes are real should be inferable from what
   * this page says.
   */
  if (!form.open) {
    return <Closed name={form.name} closedAt={form.closesAt} />;
  }

  /*
   * The two states a form for people on file has before it has any questions.
   *
   * They are told apart deliberately, and it is the difference between a page
   * with something to do on it and a page with nothing. Somebody signed out
   * gets a box; somebody signed in who this form is not for gets a sentence
   * and no box, because offering them a sign-in step they have already
   * completed is how a person requests four links to a form that will not open
   * for them either way.
   *
   * Neither says anything about which addresses we hold. The API answers the
   * email step identically whether or not an address is on file, and this page
   * has nothing extra to add.
   */
  if (form.access === "signIn") {
    return (
      <main className="page">
        <Masthead name={form.name} closesAt={form.closesAt} />
        <SignIn code={form.code} expired={query.link === "expired"} />
      </main>
    );
  }

  if (form.access === "ineligible") {
    return (
      <main className="notice">
        <h1>{form.name}</h1>
        <p>You are signed in, and this form is not one for you to fill in.</p>
        <p>If you think that is wrong, let the organizers know.</p>
      </main>
    );
  }

  if (!form.fields || form.fields.length === 0) {
    return <NoForm />;
  }

  return (
    <main className="page">
      <Masthead name={form.name} closesAt={form.closesAt} you={form.you} />

      <Questions
        code={form.code}
        fields={form.fields}
        prefill={form.prefill}
        fixed={form.fixed}
      />
    </main>
  );
}

/**
 * The form's name, and who is answering it when we know.
 *
 * The line naming the reader appears only on a form that required signing in,
 * where it is doing real work: the form does not ask for a name or an address,
 * so this is the only thing on the page that says whose answers these will be
 * filed as. On a form anybody can open there is nobody to name.
 */
function Masthead({
  name,
  closesAt,
  you,
}: {
  name: string;
  closesAt: string | null;
  you?: { name: string | null; email: string };
}) {
  return (
    <div className="masthead">
      <h1>{name}</h1>
      {you ? (
        <p className="lede">
          Answering as {you.name ? `${you.name}, ` : ""}
          {you.email}.
        </p>
      ) : null}
      {closesAt ? <p className="lede">Open until {longDate(closesAt)}.</p> : null}
    </div>
  );
}

/**
 * A form that has closed.
 *
 * It still resolves, which is the whole point. Somebody following a link off a
 * flyer in March is told the deadline passed; a 404 would read as a broken
 * link and get reported as one.
 */
function Closed({ name, closedAt }: { name: string; closedAt: string | null }) {
  return (
    <main className="notice">
      <h1>{name} has closed</h1>
      <p>
        {closedAt
          ? `This form stopped accepting answers on ${longDate(closedAt)}.`
          : "This form is no longer accepting answers."}
      </p>
      <p>Your link is fine — there is just nothing to fill in any more.</p>
    </main>
  );
}

/**
 * A date somebody can read.
 *
 * Rendered on the server, in one fixed zone, rather than from the browser's
 * locale. A date that differs between the server and the client is a
 * hydration mismatch, and this one would show up as the deadline flickering.
 */
function longDate(iso: string): string {
  return new Intl.DateTimeFormat("en-US", {
    dateStyle: "long",
    timeStyle: "short",
    timeZone: "America/New_York",
  }).format(new Date(iso));
}
