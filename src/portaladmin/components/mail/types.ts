/**
 * What a campaign is, as these screens need it.
 *
 * The shapes are the API's, not a second model: a campaign is one row in
 * notify.campaigns and the statuses are the ones its check constraint allows.
 * Nothing here computes what a campaign "really" is — a screen that disagreed
 * with the sender about whether something had gone out would be worse than no
 * screen at all.
 */

/**
 * The six states a campaign can be in.
 *
 * A union rather than a string, because every one of them changes what the
 * screen offers: only a draft can be sent, only a queued campaign can be
 * cancelled, and a sent one can do neither.
 */
export type CampaignStatus =
  | "draft"
  | "queued"
  | "sending"
  | "sent"
  | "cancelled"
  | "failed";

/** A row on the list. */
export type CampaignRow = {
  id: string;
  name: string;
  status: CampaignStatus;
  recipientCount: number;
  createdAt: string;
  /** Null until it has gone out. */
  sentAt: string | null;
};

/**
 * Who a campaign goes to.
 *
 * Three shapes and no more. Comms needs "everybody we accepted", "everybody
 * who filled in the mentor form" and "these nine addresses" — a query builder
 * would be a fourth thing to get wrong on the one screen where being wrong
 * means several hundred people got an email meant for nine.
 *
 * Stored on the campaign rather than resolved and forgotten, which is what
 * makes "who exactly did we email" answerable a month later.
 */
/** An event, for the segment that has to name one. */
export type EventChoice = { id: string; name: string };

export type Segment =
  | { type: "applicationStatus"; eventId: string; statuses: string[] }
  | { type: "formRespondents"; formId: string }
  | { type: "explicitList"; emails: string[] };

/**
 * One campaign, with the fields the list does not carry.
 *
 * `createdBy` and `approvedBy` are the two names on it, and they are the
 * reason this screen can say anything about the approval rule before somebody
 * presses a button: the API refuses a send whose actor is the author, and the
 * only way to know that in advance is to compare these against who is signed
 * in.
 */
export type Campaign = CampaignRow & {
  templateKey?: string | null;

  /** `broadcast` or `transactional`, as the API names it. */
  templateKind?: string | null;
  segment?: Segment | null;

  /** The person who drafted it. Never the person allowed to send it. */
  createdBy?: string | null;

  /** The second name, once there is one. */
  approvedBy?: string | null;
};

/**
 * What happened to the messages a campaign wrote.
 *
 * Only meaningful once it has left draft, and the only honest answer to "who
 * has actually been mailed" while a send is in progress. `gone` is everything
 * that has left this system whatever the provider did with it next, which is
 * the number that decides whether cancelling is still worth anything.
 */
export type MessageProgress = {
  total: number;
  pending: number;
  gone: number;
  byStatus: Record<string, number>;
};

/**
 * What a send would actually do, resolved now.
 *
 * The count is the whole point of the screen and the sample is what makes it
 * checkable: a count of 340 looks the same whether it is the accepted list or
 * everybody who ever started an application, and the addresses are the only
 * thing that tells the two apart.
 */
export type Preview = {
  /** People who will actually be mailed. */
  recipientCount: number;

  sample: string[];

  /**
   * Everybody the segment matched, before suppressions.
   *
   * Shown next to recipientCount when they differ, because "412 matched, 400
   * will be sent" is the sentence that lets somebody find the twelve. The API
   * writes suppressed recipients as rows rather than dropping them, so they
   * are findable rather than merely counted.
   */
  segmentSize?: number;
  suppressedCount?: number;

  /**
   * Why the suppressed ones were suppressed, counted by reason.
   *
   * The keys are the API's own words for a suppression and are shown as it
   * writes them. A screen that translated "bounce" into a friendlier sentence
   * would be maintaining a second, worse copy of a list the sender owns, and
   * the twelve people behind these numbers have to be findable by the name the
   * system actually recorded.
   */
  suppressedByReason?: Record<string, number>;

  /**
   * Everything the API thinks is wrong with this campaign, in its own
   * sentences.
   *
   * Advisory at preview and fatal at send: a template that greets people by a
   * name the segment does not carry is refused by the send, and the person
   * reading this screen is the last one who can still fix it.
   */
  problems?: string[];
};

/** A form somebody could have answered, for the segment picker. */
export type FormChoice = { id: string; name: string };

/**
 * The application statuses, exactly as the database constrains them.
 *
 * Copied from the check constraint on applications.applications rather than
 * invented here. A status this list has that the database does not is a
 * segment that resolves to nobody; one it is missing is a group that cannot be
 * mailed at all.
 */
export const APPLICANT_STATUSES: { value: string; label: string }[] = [
  { value: "incomplete", label: "Incomplete" },
  { value: "submitted", label: "Submitted" },
  { value: "under_review", label: "Under review" },
  { value: "accepted", label: "Accepted" },
  { value: "rejected", label: "Rejected" },
  { value: "waitlisted", label: "Waitlisted" },
  { value: "confirmed", label: "Confirmed" },
  { value: "declined", label: "Declined" },
  { value: "expired", label: "Expired" },
  { value: "checked_in", label: "Checked in" },
  { value: "withdrawn", label: "Withdrawn" },
];

/** The word for a status, or the status itself if it is one this list has not met. */
export function statusLabel(value: string): string {
  return APPLICANT_STATUSES.find((s) => s.value === value)?.label ?? value;
}

/**
 * Who a campaign goes to, in a line.
 *
 * The form's name where it is known and its id where it is not, because a
 * campaign can outlive the form it segmented on and an id is still true.
 */
export function describeSegment(
  segment: Segment | null | undefined,
  formName?: string | null,
): string {
  if (!segment) {
    return "No segment";
  }

  if (segment.type === "applicationStatus") {
    return `Applicants · ${segment.statuses.map(statusLabel).join(", ")}`;
  }

  if (segment.type === "formRespondents") {
    return `Form respondents · ${formName ?? segment.formId}`;
  }

  return `Address list · ${segment.emails.length}`;
}

/**
 * A timestamp, to the minute, in the order that sorts.
 *
 * Sliced off the ISO string rather than put through a locale formatter, which
 * runs in the server's zone on the server and the reader's in the browser —
 * a hydration mismatch on every row, and two different answers to "when did
 * this go out".
 */
export function when(iso: string | null): string {
  if (typeof iso !== "string" || iso.length < 16) {
    return "—";
  }

  return `${iso.slice(0, 10)} ${iso.slice(11, 16)}`;
}
