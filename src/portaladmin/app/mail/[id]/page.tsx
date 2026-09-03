import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import styles from "@/components/mail/mail.module.css";
import { Sending } from "@/components/mail/sending";
import { StatusPill } from "@/components/mail/status";
import { describeSegment, when } from "@/components/mail/types";
import { currentPerson } from "@/lib/api";
import { Shell } from "../../shell";
import { previewRecipients, sendNow, stopSending } from "../actions";
import { readCampaign, readForms } from "../api";

/**
 * One campaign, and the only place it can be sent from.
 *
 * Two panels: what the campaign is, and what sending it would do. The second
 * one is the reason the page exists — the count and a sample of the addresses,
 * resolved now, before anything goes out.
 */
export default async function Campaign({
  params,
}: {
  params: Promise<{ id: string }>;
}) {
  const { id } = await params;

  const person = await currentPerson();
  if (!person) {
    redirect("/sign-in");
  }

  const [read, forms] = await Promise.all([readCampaign(id), readForms()]);

  if (!read.ok) {
    if (read.status === 404) {
      notFound();
    }

    return (
      <Shell personId={person.personId}>
        <Link href="/mail" className="back">
          ← Mail
        </Link>
        <h1>Campaign</h1>
        <div className="empty">
          {read.status === 403 ? (
            <>
              You do not have <code>email.view_stats</code>. Ask an admin.
            </>
          ) : (
            read.error
          )}
        </div>
      </Shell>
    );
  }

  const { campaign, mocked } = read;

  // The form's name where the segment names a form and the form is one this
  // person can read. Its id otherwise, which is less useful and still true.
  const segment = campaign.segment;
  const formName =
    segment?.type === "formRespondents"
      ? (forms.forms.find((form) => form.id === segment.formId)?.name ?? null)
      : null;

  return (
    <Shell personId={person.personId}>
      <Link href="/mail" className="back">
        ← Mail
      </Link>

      <div className={styles.head}>
        <div>
          <h1>{campaign.name}</h1>
          <p className="lede" style={{ margin: 0 }}>
            <StatusPill status={campaign.status} /> · Created{" "}
            {when(campaign.createdAt)}
          </p>
        </div>
      </div>

      {mocked ? (
        <p className="error">
          Showing example data. The campaigns API is not available yet.
        </p>
      ) : null}

      <div className="columns">
        <Sending
          campaign={campaign}
          canSend={person.permissions.has("email.send_broadcast")}
          preview={previewRecipients.bind(null, id)}
          send={sendNow.bind(null, id)}
          cancel={stopSending.bind(null, id)}
        />

        <section className="panel">
          <h2>Campaign</h2>

          <dl className={styles.facts}>
            <dt>Template</dt>
            <dd>
              {campaign.templateKey ? (
                <code>{campaign.templateKey}</code>
              ) : (
                "—"
              )}
            </dd>

            <dt>Segment</dt>
            <dd>{describeSegment(campaign.segment, formName)}</dd>

            <dt>Created</dt>
            <dd className={styles.numeric}>{when(campaign.createdAt)}</dd>

            <dt>Sent</dt>
            <dd className={styles.numeric}>{when(campaign.sentAt)}</dd>
          </dl>

          {/* The subject and the body are the template's, and the template is
              not edited here. */}
          <p className="meta" style={{ marginTop: "0.9rem" }}>
            The wording is the template&rsquo;s.
          </p>
        </section>
      </div>
    </Shell>
  );
}
