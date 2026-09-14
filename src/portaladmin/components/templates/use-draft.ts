"use client";

import { useMemo, useState } from "react";
import {
  placeholdersIn,
  type Placeholder,
  type Template,
  type TemplateDraft,
  type TemplateFormat,
  type TemplateKind,
} from "./types";

/**
 * Everything the author is typing, and nothing about what happens to it.
 *
 * Split out of the editor component because the fields and the two things
 * done with them — rendering a preview, saving a version — depend on quite
 * different parts of this. The preview cares about three fields; saving cares
 * about all of them; the form cares about each one separately. Holding all
 * three concerns in one component meant every keystroke in the reply-to box
 * re-ran the logic that decides whether to render the email.
 */

/** The sending identity, which is fixed rather than typed. */
const FROM_LOCAL = "mail";
const FROM_DOMAIN = "morganhacks.com";

/**
 * What a new template says it is from.
 *
 * The address is fixed above because an address somebody types is an address
 * that can be wrong, and a from address not verified in SES does not bounce —
 * it fails to send at all. The name is not fixed, because it is copy.
 */
const DEFAULT_FROM_NAME = "MorganHacks";

export type Draft = {
  key: string;
  kind: TemplateKind;
  subject: string;
  body: string;
  format: TemplateFormat;
  fromName: string;
  replyTo: string;
};

export type DraftHandle = {
  draft: Draft;
  /** One setter, so a new field does not mean a new prop through two layers. */
  set: <K extends keyof Draft>(field: K, value: Draft[K]) => void;
  /** The shape the API takes, with the empties turned into nulls. */
  toRequest: () => TemplateDraft;
  /** The names the body asks for, derived from what is typed. */
  used: string[];
  /** The names that resolve, or null where nobody could say. */
  resolves: Set<string> | null;
};

export function useDraft(
  template: Template | null,
  available: Placeholder[] | null,
): DraftHandle {
  const [draft, setDraft] = useState<Draft>(() => ({
    key: template?.key ?? "",
    // Broadcast only, here. The API still serves both kinds — the sign-in link
    // is a transactional template and is sent by atlas, not written here — but
    // the console has no reason to offer a kind nobody composes by hand.
    kind: template?.kind ?? "broadcast",
    subject: template?.subject ?? "",
    body: template?.body ?? "",
    // A new template is prose until somebody decides it is a design, and prose
    // is the one that cannot be got wrong: there is no way to write a
    // stylesheet in Markdown and therefore no way to lose one.
    format: template?.format ?? "markdown",
    fromName: template?.fromName ?? DEFAULT_FROM_NAME,
    replyTo: template?.replyTo ?? "",
  }));

  /**
   * Derived while rendering rather than kept in a second piece of state.
   *
   * A placeholder list held in state is a list that can disagree with the body
   * it came from for one render, and the disagreement is invisible: the screen
   * simply says a name is unknown that is not, or stays quiet about one that
   * is.
   */
  const used = useMemo(
    () => placeholdersIn(draft.subject, draft.body),
    [draft.subject, draft.body],
  );

  /**
   * Null all the way through where the API could not be read.
   *
   * Empty means the API answered and there is nothing to offer; null means
   * nobody knows. The difference decides whether a name can be called unknown,
   * and accusing a perfectly good placeholder of being wrong is worse than
   * saying nothing.
   */
  const resolves = useMemo(
    () =>
      available === null
        ? null
        : new Set(available.map((placeholder) => placeholder.name)),
    [available],
  );

  return {
    draft,
    set: (field, value) => setDraft((was) => ({ ...was, [field]: value })),
    toRequest: () => ({
      key: draft.key,
      kind: draft.kind,
      subject: draft.subject,
      body: draft.body,
      format: draft.format,
      fromName: draft.fromName.trim() === "" ? null : draft.fromName.trim(),
      fromLocal: FROM_LOCAL,
      fromDomain: FROM_DOMAIN,
      replyTo: draft.replyTo.trim() === "" ? null : draft.replyTo.trim(),
    }),
    used,
    resolves,
  };
}

export { FROM_DOMAIN, FROM_LOCAL };
