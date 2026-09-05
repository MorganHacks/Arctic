"use server";

import { revalidatePath } from "next/cache";
import type {
  Rendered,
  TemplateDraft,
  TemplateFormat,
} from "@/components/templates/types";
import { createTemplate, renderPreview, updateTemplate } from "./api";

/**
 * The two things somebody can do to a template, and the one that only looks.
 *
 * Actions rather than route handlers, so the API's address and what its
 * failures mean stay on the server. Neither of the writes is a gate: the API
 * refuses on `email.manage_templates` whoever asks, and these forward its
 * refusal rather than deciding anything themselves.
 */

/**
 * A write that landed, in the API's own terms.
 *
 * `version` is what the API says the template is now at, and `note` is
 * anything it wanted said about having written it. Both are repeated to the
 * person rather than interpreted — this screen does not know what an edit
 * means for a campaign that already used the key, and guessing would be worse
 * than showing the answer.
 */
export type SaveResult =
  | { ok: true; key: string; version: number | null; note: string | null }
  | { ok: false; error: string };

export type PreviewResult =
  | { ok: true; rendered: Rendered }
  | { ok: false; error: string };

/** Everything that must be there before the API is asked. */
function checked(draft: TemplateDraft): string | null {
  if (draft.key === "") {
    return "A key is required.";
  }

  if (draft.subject === "") {
    return "A subject is required.";
  }

  if (draft.body === "") {
    return "A body is required.";
  }

  if (draft.fromLocal === "" || draft.fromDomain === "") {
    return "A from address is required.";
  }

  return null;
}

function trimmed(draft: TemplateDraft): TemplateDraft {
  const replyTo = draft.replyTo?.trim() ?? "";

  return {
    key: draft.key.trim(),
    kind: draft.kind,
    subject: draft.subject.trim(),
    // Not trimmed to the edge on purpose: leading whitespace can be a list's
    // indentation in Markdown or an indented tag in HTML, and the API is the
    // thing that decides what the body means.
    body: draft.body.replace(/\s+$/, ""),
    format: draft.format,
    fromLocal: draft.fromLocal.trim(),
    fromDomain: draft.fromDomain.trim(),
    replyTo: replyTo === "" ? null : replyTo,
  };
}

/** Writes a template that does not exist yet. */
export async function addTemplate(draft: TemplateDraft): Promise<SaveResult> {
  const body = trimmed(draft);

  const wrong = checked(body);
  if (wrong) {
    return { ok: false, error: wrong };
  }

  const saved = await createTemplate(body);
  if (!saved.ok) {
    return saved;
  }

  revalidatePath("/templates");
  revalidatePath("/mail");
  return saved;
}

/**
 * Writes over a template that already exists.
 *
 * `key` is passed separately from the body because the key is the address of
 * the thing being written to, and a screen that let it drift would be creating
 * a second template while looking like it was editing the first.
 */
export async function editTemplate(
  key: string,
  draft: TemplateDraft,
): Promise<SaveResult> {
  const body = { ...trimmed(draft), key };

  const wrong = checked(body);
  if (wrong) {
    return { ok: false, error: wrong };
  }

  const saved = await updateTemplate(key, body);
  if (!saved.ok) {
    return saved;
  }

  revalidatePath("/templates");
  revalidatePath(`/templates/${key}`);
  return saved;
}

/** What the subject and body would come out as, rendered by the sender. */
export async function previewBody(input: {
  subject: string;
  body: string;
  format: TemplateFormat;
}): Promise<PreviewResult> {
  return renderPreview(input);
}
