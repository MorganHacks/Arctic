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
 * What the campaign is, beside what sending it would do. The second is the
 * reason the page exists — the count and a sample of the addresses, resolved
 * now, before anything goes out — and it is why this page looks unlike the
 * rest of the console: a screen whose only job is to slow somebody down for
 * ten seconds has to look different from the thirty screens that do not.
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

  const { campaign, messages, sample, mocked } = read;

  // The form's name where the segment names a form and the form is one this
  // person can read. Its id otherwise, which is less useful and still true.
  const segment = campaign.segment;
  const formName =
    segment?.type === "formRespondents"
      ? (forms.forms.find((form) => form.id === segment.formId)?.name ?? null)
      : null;

  /*
   * What the campaign is.
   *
   * Handed to the sending component rather than rendered beside it, because
   * the two sit in one grid and only that component knows how many rows the
   * grid has — a draft with a resolved preview has a send region under it and
   * a sent campaign does not.
   */
  const facts = (
    <section className={styles.card}>
      <div className={styles.cardHead}>
        <h2>Campaign</h2>
      </div>

      <div className={styles.cardBody}>
        <dl className={styles.facts}>
          <dt>Template</dt>
          <dd>
            {campaign.templateKey ? <code>{campaign.templateKey}</code> : "—"}
            {campaign.templateKind ? (
              <>
                {" "}
                <span className="meta">{campaign.templateKind}</span>
              </>
            ) : null}
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
        <p className="meta" style={{ marginTop: "0.9rem", marginBottom: 0 }}>
          The wording is the template&rsquo;s.
        </p>

        {/*
          The frozen list, once there is one. After a send this is the only
          place the question "who did we actually mail" has an answer at all —
          the segment resolves to somebody else by then — and it is null rather
          than empty when this person may not read addresses.
        */}
        {sample && sample.length > 0 ? (
          <ul className={styles.sample}>
            {sample.map((address) => (
              <li key={address}>{address}</li>
            ))}
          </ul>
        ) : null}
      </div>
    </section>
  );

  return (
    <Shell personId={person.personId}>
      <Link href="/mail" className="back">
        ← Mail
      </Link>

      <div className={styles.head}>
        <div>
          <div className={styles.title}>
            <h1 style={{ margin: 0 }}>{campaign.name}</h1>
            <StatusPill status={campaign.status} />
          </div>
          <p className="meta" style={{ margin: "0.35rem 0 0" }}>
            Created {when(campaign.createdAt)}
          </p>
        </div>

        {/* Said at the top rather than at the button, and only while there is
            still something irreversible to do here. Somebody who learns this
            before scrolling has time to check they opened the right campaign;
            somebody looking at a campaign that has already gone is being told
            about a decision they no longer have. */}
        {campaign.status === "draft" ? (
          <p className={styles.irreversible}>Cannot be undone</p>
        ) : null}
      </div>

      {mocked ? (
        <p className="error">
          Showing example data. The campaigns API is not available yet.
        </p>
      ) : null}

      <Sending
        campaign={campaign}
        canSend={person.permissions.has("email.send_broadcast")}
        me={person.personId}
        messages={messages}
        facts={facts}
        preview={previewRecipients.bind(null, id)}
        send={sendNow.bind(null, id)}
        cancel={stopSending.bind(null, id)}
      />
    </Shell>
  );
}
