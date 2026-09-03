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
 * `html` and `text` are what the API last rendered from `markdown`. They are
 * shown, never edited: the sender renders from the markdown, and a screen that
 * let somebody edit the html directly would be editing something no send reads.
 */
export type Template = {
  key: string;
  kind: TemplateKind;
  subject: string;
  markdown: string;
  html: string;
  text: string;
  fromLocal: string;
  fromDomain: string;
  replyTo: string | null;
  version: number;
  /** The placeholders the saved body uses, as the API found them. */
  placeholders: string[];
};

/** What the body and subject come out as. Rendered by the API, never here. */
export type Rendered = { subject: string; html: string; text: string };

/** Everything a create or a save sends. */
export type TemplateDraft = {
  key: string;
  kind: TemplateKind;
  subject: string;
  markdown: string;
  fromLocal: string;
  fromDomain: string;
  replyTo: string | null;
};

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
