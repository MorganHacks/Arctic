import type { Metadata } from "next";
import { loadForm } from "@/lib/api";
import { NoForm } from "../no-form";
import { Questions } from "./questions";

type Props = { params: Promise<{ code: string }> };

/**
 * The form's own name in the tab.
 *
 * Worth the second call: somebody applying has three tabs open and "Untitled"
 * on all of them is how they lose the one they were filling in.
 */
export async function generateMetadata({ params }: Props): Promise<Metadata> {
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
export default async function FormPage({ params }: Props) {
  const { code } = await params;
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

  if (!form.fields || form.fields.length === 0) {
    return <NoForm />;
  }

  return (
    <main className="page">
      <div className="masthead">
        <p className="wordmark">MorganHacks</p>
        <h1>{form.name}</h1>
        {form.closesAt ? (
          <p className="lede">Open until {longDate(form.closesAt)}.</p>
        ) : null}
      </div>

      <Questions code={form.code} fields={form.fields} />
    </main>
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
