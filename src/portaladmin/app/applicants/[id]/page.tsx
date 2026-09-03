import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { Answers } from "@/components/applicants/answers";
import { Decision } from "@/components/applicants/decision";
import { History } from "@/components/applicants/history";
import { Notes } from "@/components/applicants/notes";
import { StatusPill, stamp } from "@/components/applicants/status";
import styles from "@/components/applicants/applicants.module.css";
import type { Applicant } from "@/components/applicants/types";
import { currentPerson } from "@/lib/api";
import { Shell } from "../../shell";
import { readApplicant } from "../api";

/**
 * One applicant, in full.
 *
 * Who this is sits across the top, because both columns underneath are read
 * against it and a name in the right-hand rail scrolls away from the answers
 * it is the heading for. Then answers on the left, everything that can be done
 * about them on the right. Not the other way round: the answers are what a
 * decision is made from and they are the long column, so the controls belong
 * beside the scroll rather than under it. Nobody should have to read to the
 * bottom of an essay to find the button.
 *
 * Four of the panels are behind permissions of their own and can be absent —
 * the answers, the notes, the resume link and the decision. Each says which
 * permission is missing rather than rendering empty, because "you cannot see
 * this" and "there is nothing here" are different sentences and only one of
 * them is true. Naming the permission is what turns "it doesn't work" into a
 * request an admin can act on, which matters most on a team that turns over
 * completely every year.
 *
 * Never cached. A decision is made from what this says, so a stale status
 * would be a decision made against a record somebody else has already changed.
 */
export default async function OneApplicant({
  params,
}: {
  params: Promise<{ id: string }>;
}) {
  const { id } = await params;

  const person = await currentPerson();
  if (!person) {
    redirect("/sign-in");
  }

  const read = await readApplicant(id);

  if (!read.ok) {
    if (read.status === 404) {
      notFound();
    }

    return (
      <Shell personId={person.personId}>
        <Link href="/applicants" className="back">
          ← Applicants
        </Link>
        <h1>Applicant</h1>
        <div className="empty">{read.error}</div>
      </Shell>
    );
  }

  const applicant = read.applicant;

  return (
    <Shell personId={person.personId}>
      <Link href="/applicants" className="back">
        ← Applicants
      </Link>

      <div className={styles.record}>
        <div className={styles.person}>
          <h1>{name(applicant)}</h1>
          <StatusPill status={applicant.status} />
        </div>

        {/* The address as text, never in a link or a heading attribute. It is
            what somebody typed into a public form. */}
        <p className={styles.identity}>
          {applicant.email}
          {applicant.school ? ` · ${applicant.school}` : null}
          {` · form v${applicant.formVersion}`}
        </p>

        <h2 className={styles.caption}>Dates (UTC)</h2>
        <Dates applicant={applicant} />
      </div>

      <div className={styles.split}>
        <section className="panel">
          <h2>Answers</h2>
          {applicant.answers === null ? (
            <p className={styles.refusal}>
              You do not have <code>applications.view_responses</code>. Ask an
              admin.
            </p>
          ) : (
            <Answers answers={applicant.answers} />
          )}
        </section>

        <div className={styles.rail}>
          <section className="panel">
            <h2>Status</h2>
            {/*
              Whether this reader may decide is known before the button is
              drawn, so it is said before the button is pressed. The permission
              set is a courtesy and never the gate — the API refuses the change
              whoever asks — but a reader on logistics who picks a status,
              writes a reason and then reads "you do not have
              applications.decide" has been told the same thing a minute later
              and lost the reason they typed.
            */}
            <Decision
              id={applicant.id}
              allowedNext={applicant.allowedNext}
              canDecide={person.permissions.has("applications.decide")}
            />
          </section>

          <section className="panel">
            <h2>Resume</h2>
            <Resume applicant={applicant} />
          </section>

          <section className="panel">
            <h2>History</h2>
            <History steps={applicant.history} />
          </section>

          <section className="panel">
            <h2>Notes</h2>
            {applicant.notes === null ? (
              <p className={styles.refusal}>
                You do not have <code>applications.note</code>. Ask an admin.
              </p>
            ) : (
              <Notes id={applicant.id} notes={applicant.notes} />
            )}
          </section>
        </div>
      </div>
    </Shell>
  );
}

/**
 * The resume, or why there is not a link to it.
 *
 * Three different absences and each gets its own sentence: no file was
 * attached, this reader may not open one, or the file is gone. The last is the
 * one worth telling somebody about — it means bytes we said we had are not
 * there — and it already shouts in the API's log.
 *
 * The middle one is the state this screen was drawn for. Logistics holds
 * `applications.view` and not `applications.view_resume` on purpose, because a
 * CV is more sensitive than a headcount, so this is not an edge case — it is
 * what a whole team sees on every record they open, and it has to read as a
 * boundary somebody drew rather than as a panel that failed to load.
 *
 * The link is signed and lives about five minutes. It is minted when this page
 * is rendered rather than with the list, because a page of fifty rows would
 * mean fifty live links to open none of the files.
 */
function Resume({ applicant }: { applicant: Applicant }) {
  if (!applicant.hasResume) {
    return <p className="meta">No resume attached.</p>;
  }

  if (applicant.resume === null) {
    return (
      <p className={styles.refusal}>
        There is a resume. You do not have{" "}
        <code>applications.view_resume</code> to open it.
      </p>
    );
  }

  return (
    <p className={styles.resume}>
      {/* target and rel together. The file is one a stranger uploaded, and a
          new tab that can reach back into this one is a way for it to. */}
      <a
        href={applicant.resume.url}
        target="_blank"
        rel="noopener noreferrer"
        className={styles.filename}
      >
        {applicant.resume.filename}
      </a>
      {applicant.resume.sizeBytes !== null ? (
        <span className="meta">{kb(applicant.resume.sizeBytes)}</span>
      ) : null}
      <span className="meta">Link expires in about five minutes.</span>
    </p>
  );
}

/**
 * The moments in this application's life, as things to compare.
 *
 * A row across the record rather than a column in the rail. They are read
 * against each other — how long it sat between submitting and a decision,
 * whether the RSVP has run out — and a stack of label-and-value pairs is a
 * stack nobody compares.
 *
 * Only the ones that happened. A grid of "—" against every stage an applicant
 * has not reached is a grid nobody reads, and the history panel below already
 * says what has and has not happened.
 */
function Dates({ applicant }: { applicant: Applicant }) {
  const rows: [string, string | null][] = [
    ["Started", stamp(applicant.createdAt)],
    ["Submitted", stamp(applicant.submittedAt)],
    ["Decided", stamp(applicant.decidedAt)],
    ["RSVP by", stamp(applicant.rsvpDeadline)],
    ["Confirmed", stamp(applicant.confirmedAt)],
    ["Declined", stamp(applicant.declinedAt)],
    ["Checked in", stamp(applicant.checkedInAt)],
  ];

  return (
    <dl className={styles.stamps}>
      {rows
        .filter(([, at]) => at !== null)
        .map(([what, at]) => (
          <div key={what}>
            <dt>{what}</dt>
            <dd>{at}</dd>
          </div>
        ))}
    </dl>
  );
}

/**
 * What to call somebody.
 *
 * Both names are nullable: the row exists from the moment somebody opens the
 * form, and the constraint that insists on a name only applies once they
 * submit. A half-filled draft often has nothing but an address.
 */
function name(applicant: Applicant): string {
  const both = [applicant.firstName, applicant.lastName].filter(Boolean).join(" ");
  return both === "" ? applicant.email : both;
}

/** A file size somebody can judge at a glance. Never the exact byte count. */
function kb(bytes: number): string {
  return `${Math.max(1, Math.round(bytes / 1024))} KB`;
}
