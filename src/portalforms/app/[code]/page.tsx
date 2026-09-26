import { PageBackground } from "./page-background";
import { FormLinkCard } from "@/components/ui/form-link-card";
import type { Metadata } from "next";
import { headers } from "next/headers";
import { loadForm, type PublicForm } from "@/lib/api";
import { formShareMetadata, formShareOrigin } from "@/lib/form-sharing";
import { formMlhBadgeColor, formThemeStyle, resolveFormTheme } from "../../../../libs/ui/form-theme";
import { NoForm } from "../no-form";
import { Questions } from "./questions";
import { SignIn } from "./sign-in";
import { MlhBadge } from "./mlh-badge";
import { DeadlineCountdown } from "./deadline";
import { ZONE } from "../../../../libs/ui/zone";
import styles from "./form-page.module.css";

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

export async function generateMetadata({
  params,
}: Pick<Props, "params">): Promise<Metadata> {
  const { code } = await params;
  const [form, requestHeaders] = await Promise.all([loadForm(code, { anonymous: true }), headers()]);

  if (!form) return { title: "MorganHacks", description: "This form is unavailable." };

  return formShareMetadata(form, formShareOrigin(requestHeaders, process.env.FORMS_ORIGIN));
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

  const theme = resolveFormTheme(form.theme);
  const themeStyle = formThemeStyle(theme);
  const linkCard = form.open && (form.fields?.length || form.access === "signIn") ? theme.linkCard : null;
  return <div className={`formTheme ${styles.surface} ${theme.layout === "cards" ? styles.cards : ""}`} style={themeStyle} data-layout={theme.layout} data-color-scheme={themeStyle["--form-color-scheme"]} data-background={themeStyle["--form-background"]} data-link-card={!!linkCard}>
    <PageBackground theme={form.theme} />
    {theme.showMlhBadge && form.mlhSeason ? <MlhBadge season={form.mlhSeason} color={formMlhBadgeColor(theme)} /> : null}
    <FormContent form={form} expired={query.link === "expired"} />
    {linkCard ? <FormLinkCard card={linkCard} /> : null}
  </div>;
}

function FormContent({ form, expired }: { form: PublicForm; expired: boolean }) {

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
        <FormIntro form={form} />
        <SignIn code={form.code} expired={expired} />
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

  const cards = resolveFormTheme(form.theme).layout === "cards";
  return (
    <main className="page">
      {cards ? <HeaderImage form={form} /> : <FormIntro form={form} />}

      <Questions
        code={form.code}
        fields={form.fields}
        prefill={form.prefill}
        fixed={form.fixed}
        intro={cards ? <Masthead name={form.name} closesAt={form.closesAt} you={form.you} /> : undefined}
      />
    </main>
  );
}

function FormIntro({ form }: { form: PublicForm }) {
  return <aside className={styles.intro}>
    <HeaderImage form={form} />
    <Masthead name={form.name} closesAt={form.closesAt} you={form.you} />
  </aside>;
}

function HeaderImage({ form }: { form: PublicForm }) {
  const image = resolveFormTheme(form.theme).headerImage;
  return image ? <img className="formHeaderImage" src={image} alt="" /> : null;
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
      {closesAt ? <div className={styles.deadline}>
        <span className={styles.deadlineLabel}>
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" focusable="false">
            <rect x="3" y="5" width="18" height="16" rx="2" />
            <path d="M16 3v4M8 3v4M3 11h18M8 15h2m4 0h2M8 18h2" />
          </svg>
          Submission deadline
        </span>
        <time dateTime={closesAt}>{longDate(closesAt)}</time>
        <DeadlineCountdown closesAt={closesAt} />
      </div> : null}
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
    timeZone: ZONE,
  }).format(new Date(iso));
}
