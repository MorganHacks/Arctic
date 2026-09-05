/**
 * What an email template is, as these screens need it.
 *
 * The shapes are the API's. A template is one row in notify.templates, and
 * `kind` is one of the two values its check constraint allows — the same two
 * that decide which lane and which sending subdomain a message goes out on.
 * Nothing here decides what a template "really" is.
 */

/**
 * The two kinds, and the reason the distinction is on screen at all.
 *
 * `transactional` is the lane a login link goes down. `broadcast` is the lane
 * an announcement goes down. A campaign may only use a broadcast template, so
 * the difference is not a label — it is whether this template can be chosen on
 * the compose screen.
 */
export type TemplateKind = "transactional" | "broadcast";

/** A row on the list. */
export type TemplateRow = {
  key: string;
  kind: TemplateKind;
  subject: string;
  version: number;
  /** Null on a template the API has no edit time for. */
  updatedAt: string | null;
};

/**
 * One template, whole.
 *
 * `html` and `text` are what the API last rendered from `body`. They are shown,
 * never edited: the sender renders from the source, and a screen that let
 * somebody edit the html directly would be editing something no send reads.
 */
export type Template = {
  key: string;
  kind: TemplateKind;
  subject: string;

  /** The source, in whichever language `format` names. */
  body: string;
  format: TemplateFormat;

  html: string;
  text: string;
  fromLocal: string;
  fromDomain: string;
  replyTo: string | null;
  version: number;
  /** What the allow-list removed from the saved source. Absent on an older API. */
  notes?: string[];

  /** The placeholders the saved body uses, as the API found them. */
  placeholders: string[];
};

/**
 * What the body and subject come out as. Rendered by the API, never here.
 *
 * `notes` is what the allow-list removed on the way. Absent on an older API and
 * on the offline example data, so it is optional rather than an empty array --
 * a missing field must not render as "nothing was removed" when the truth is
 * "nobody was asked".
 */
export type Rendered = {
  subject: string;
  html: string;
  text: string;
  notes?: string[];
};

/**
 * A name a send can actually put a value in, and what that value will be.
 *
 * The API's list, read on every page load, and deliberately not a constant in
 * this repo. Three names resolve today; a list written here would be right
 * until the day a fourth is added or one is renamed, and then this editor
 * would be confidently offering a placeholder that makes a campaign refuse to
 * send. Offering nothing is the lesser failure, so where the list cannot be
 * read the menu does not appear at all.
 *
 * `description` is what to tell somebody about the value — null where the API
 * offers no sentence for it, which is not the same as an empty one.
 */
export type Placeholder = { name: string; description: string | null };

/** Everything a create or a save sends. */
export type TemplateDraft = {
  key: string;
  kind: TemplateKind;
  subject: string;
  body: string;
  format: TemplateFormat;
  fromLocal: string;
  fromDomain: string;
  replyTo: string | null;
};

/**
 * The two languages a body can be written in.
 *
 * Markdown for anything that reads as prose, which is most of it. HTML for a
 * design somebody wants control of -- a button needs a table cell with a
 * background colour, and Markdown has no way to say that.
 *
 * The same two strings the API takes, deliberately. A screen that invented its
 * own words for these would need a translation layer, and the translation is
 * where the two would eventually disagree.
 */
export type TemplateFormat = "markdown" | "html";

/** The word for a format, for a button somebody has to read. */
export function formatLabel(format: TemplateFormat): string {
  return format === "html" ? "HTML" : "Markdown";
}

/** The word for a kind. */
export function kindLabel(kind: TemplateKind): string {
  return kind === "broadcast" ? "Broadcast" : "Transactional";
}

/**
 * The placeholders a piece of text asks for.
 *
 * A regex over `{{name}}`, not a second renderer. The API is the only thing
 * that turns markdown into an email; this only reads which names appear, so
 * that somebody typing one can see it land in the list before a send refuses
 * for want of a value. Deduplicated, in the order they are first written.
 */
export function placeholdersIn(...parts: string[]): string[] {
  const found: string[] = [];

  for (const part of parts) {
    for (const match of part.matchAll(/\{\{\s*([\w.]+)\s*\}\}/g)) {
      if (!found.includes(match[1])) {
        found.push(match[1]);
      }
    }
  }

  return found;
}
