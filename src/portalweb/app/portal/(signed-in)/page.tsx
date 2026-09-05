import Link from "next/link";
import { redirect } from "next/navigation";
import { currentPortal, readableDate } from "@/lib/api";
import { readableTime } from "../../../../../libs/ui/zone";
import { RsvpPanel } from "./rsvp";

/**
 * Where a sign-in link lands, and the only screen most applicants will open.
 *
 * One question: where is my application. The answer is the first thing on the
 * page, in the words the API chose — nothing here maps a status, because the
 * mapping is the API's and a second copy of it on this side would eventually
 * disagree with the one the team signed off.
 */
export default async function Status() {
  const portal = await currentPortal();
  if (!portal) {
    redirect("/portal/sign-in");
  }

  const { application } = portal;

  if (!application) {
    return (
      <>
        <h1>Your application</h1>
        <p className="lede">
          You are signed in, but you have not started an application yet.
        </p>
        <div className="empty">
          <p>
            Nothing here yet. When applications open, this is where you will
            track yours.
          </p>
          <p className="quiet" style={{ marginBottom: 0 }}>
            Applied with a different email address? Sign out and use that one.
          </p>
        </div>
      </>
    );
  }

  return (
    <>
      <h1>Your application</h1>
      <p className="lede">
        Everything we can tell you right now is on this page.
      </p>

      <section className="status" aria-label="Where your application is">
        <p className="status__label">{application.statusLabel}</p>
        <p className="status__next">{application.nextStep}</p>

        {application.receivedAt ? (
          <p className="status__meta">
            Received {readableDate(application.receivedAt)}
          </p>
        ) : null}
      </section>

      {/*
        Directly under the status line, because on the one day this appears it
        is the reason the person opened the page. The deadline is formatted
        here rather than in the client component so the date is rendered once,
        on the server, in the event's zone — the same zone the console writes
        it in and the same one the API says the day in.
      */}
      <RsvpPanel
        rsvp={application.rsvp}
        deadline={readableTime(application.rsvp.deadline)}
      />

      <section className="panel">
        <h2>Your details</h2>
        <p className="quiet">
          {application.profileEditable
            ? "Name, school, shirt size, and anything we need to know about "
              + "food or access. You can change these while your application "
              + "is still open."
            : application.profileLockedReason}
        </p>
        <div className="actions">
          <Link className="button" href="/portal/profile">
            {application.profileEditable ? "Edit your details" : "View your details"}
          </Link>
        </div>
      </section>

      <section className="panel">
        <h2>Emails we have sent you</h2>
        <p className="quiet">
          Every message we have sent, and whether it arrived. Start here if you
          think you missed something.
        </p>
        <div className="actions">
          <Link className="button" href="/portal/messages">
            See your emails
          </Link>
        </div>
      </section>
    </>
  );
}
