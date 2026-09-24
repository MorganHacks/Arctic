"use client";

import { useLayoutEffect, useMemo, useRef, useState } from "react";
import { draftFingerprint, readRecovery, recoveryKey } from "./draft-recovery";
import { STARTER_HTML } from "./starter-html";
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
  name: string;
  kind: TemplateKind;
  subject: string;
  previewText: string;
  clickTracking: boolean;
  body: string;
  format: TemplateFormat;
  fromName: string;
  replyTo: string;
};

export type DraftHandle = {
  draft: Draft;
  ready: boolean;
  storageFailed: boolean;
  recoveryConflict: boolean;
  recordSaved: (key: string, version: number, snapshot: TemplateDraft) => void;
  clearRecovery: () => void;
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
  owner: { id: string; email: string; canManage: boolean },
): DraftHandle {
  const bodyEdited = useRef(false);
  const [draft, setDraft] = useState<Draft>(() => ({
    name: template?.name ?? template?.key ?? "",
    // Broadcast only, here. The API still serves both kinds — the sign-in link
    // is a transactional template and is sent by atlas, not written here — but
    // the console has no reason to offer a kind nobody composes by hand.
    kind: template?.kind ?? "broadcast",
    subject: template?.subject ?? "",
    previewText: template?.previewText ?? "",
    clickTracking: template?.clickTracking ?? false,
    body: template?.body ?? template?.html ?? "",
    // A new template is prose until somebody decides it is a design, and prose
    // is the one that cannot be got wrong: there is no way to write a
    // stylesheet in Markdown and therefore no way to lose one.
    format: template?.body === null ? "html" : template?.format ?? "markdown",
    fromName: template?.fromName ?? DEFAULT_FROM_NAME,
    replyTo: template?.replyTo ?? "",
  }));
  const current = useRef(draft);
  const savedFingerprint = useRef(draftFingerprint(draft));
  const savedKey = useRef(template?.key ?? null);
  const baseVersion = useRef(template?.version ?? 0);
  const [ready, setReady] = useState(false);
  const [storageFailed, setStorageFailed] = useState(false);
  const [recoveryConflict, setRecoveryConflict] = useState(false);
  const storage = recoveryKey(owner.id, template?.key ?? null);
  const upgradeStorage = `mh-template-upgrade:${owner.email}:${template?.key ?? "new"}`;

  function remember(next: Draft) {
    if (!owner.canManage) return false;
    try {
      const snapshot = JSON.stringify({ draft: next, key: savedKey.current, version: baseVersion.current,
        bodyEdited: bodyEdited.current, dirty: draftFingerprint(next) !== savedFingerprint.current });
      sessionStorage.setItem(storage, snapshot);
      if (savedKey.current && !template) {
        sessionStorage.setItem(recoveryKey(owner.id, savedKey.current), snapshot);
      }
      setStorageFailed(false);
      return true;
    } catch {
      setStorageFailed(true);
      return false;
    }
  }

  useLayoutEffect(() => {
    if (owner.canManage) {
      try {
        const restored = readRecovery(sessionStorage.getItem(storage))
          ?? readRecovery(sessionStorage.getItem(upgradeStorage));
        if (restored && (restored.dirty || !template)) {
          current.current = restored.draft;
          setDraft(restored.draft);
          savedKey.current = template?.key ?? restored.key;
          baseVersion.current = restored.version;
          if (!restored.dirty) savedFingerprint.current = draftFingerprint(restored.draft);
          bodyEdited.current = restored.bodyEdited === true || Boolean(restored.draft.body);
          setRecoveryConflict(Boolean(template && restored.version !== template.version));
        }
        if (!savedKey.current) savedKey.current = `template_${crypto.randomUUID().replaceAll("-", "")}`;
        if (remember(current.current)) sessionStorage.removeItem(upgradeStorage);
      } catch {
        setStorageFailed(true);
      }
    }
    setReady(true);
  }, [storage, upgradeStorage, owner.canManage, template?.key]);

  /**
   * Derived while rendering rather than kept in a second piece of state.
   *
   * A placeholder list held in state is a list that can disagree with the body
   * it came from for one render, and the disagreement is invisible: the screen
   * simply says a name is unknown that is not, or stays quiet about one that
   * is.
   */
  const used = useMemo(
    () => placeholdersIn(draft.subject, draft.body, draft.previewText),
    [draft.subject, draft.body, draft.previewText],
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
    ready,
    storageFailed,
    recoveryConflict,
    recordSaved: (key, version, snapshot) => {
      savedKey.current = key;
      baseVersion.current = version;
      savedFingerprint.current = draftFingerprint(snapshot);
      remember(current.current);
    },
    clearRecovery: () => {
      try {
        sessionStorage.removeItem(storage);
        if (savedKey.current) sessionStorage.removeItem(recoveryKey(owner.id, savedKey.current));
        sessionStorage.removeItem(upgradeStorage);
      } catch {}
    },
    set: (field, value) => {
      if (field === "body") bodyEdited.current = true;
      const addStarter =
        template === null &&
        field === "format" &&
        value === "html" &&
        !bodyEdited.current;
      const next = {
        ...current.current,
        [field]: value,
        ...(addStarter && !current.current.body.trim() ? { body: STARTER_HTML } : {}),
      };
      current.current = next;
      remember(next);
      setDraft(next);
    },
    toRequest: () => ({
      key: savedKey.current ?? undefined,
      name: draft.name.trim(),
      kind: draft.kind,
      subject: draft.subject,
      previewText: draft.previewText.trim() || null,
      clickTracking: draft.clickTracking,
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
