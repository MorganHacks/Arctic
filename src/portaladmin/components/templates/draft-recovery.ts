import type { Draft } from "./use-draft";
import type { TemplateDraft } from "./types";

export type DraftRecovery = {
  draft: Draft;
  key: string | null;
  version: number;
  bodyEdited?: boolean;
  dirty?: boolean;
};

export function draftFingerprint(draft: Draft | TemplateDraft) {
  return JSON.stringify({ name: draft.name.trim(), kind: draft.kind, subject: draft.subject.trim(),
    previewText: draft.previewText?.trim() || null, clickTracking: draft.clickTracking,
    body: draft.body.replace(/\s+$/, ""), format: draft.format,
    fromName: draft.fromName?.trim() || null, replyTo: draft.replyTo?.trim() || null });
}

export function recoveryKey(owner: string, key: string | null) {
  return `mh-template-draft:${owner}:${key ?? "new"}`;
}

export function readRecovery(raw: string | null): DraftRecovery | null {
  if (!raw) return null;
  try {
    const value = JSON.parse(raw) as Partial<DraftRecovery>;
    const draft = value.draft;
    if (!draft || typeof draft !== "object" || !Number.isInteger(value.version)
      || (value.key != null && (typeof value.key !== "string" || !/^[a-z0-9][a-z0-9_-]{0,63}$/.test(value.key)))) return null;
    for (const field of ["name", "subject", "previewText", "body", "fromName", "replyTo"] as const) {
      if (typeof draft[field] !== "string") return null;
    }
    if (typeof draft.clickTracking !== "boolean"
      || !["markdown", "html"].includes(draft.format)
      || !["broadcast", "transactional"].includes(draft.kind)) return null;
    return { draft, key: value.key ?? null, version: value.version!, bodyEdited: value.bodyEdited === true, dirty: value.dirty !== false };
  } catch {
    return null;
  }
}
