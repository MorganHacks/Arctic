"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import type { Preview, Segment } from "@/components/mail/types";
import {
  cancelCampaign,
  createCampaign,
  previewCampaign,
  sendCampaign,
} from "./api";

/**
 * The four things somebody can do to a campaign.
 *
 * Actions rather than route handlers, so the API's address and what its
 * failures mean stay on the server. None of them is a gate: the API refuses
 * the send on `email.send_broadcast` whoever asks, and these forward its
 * refusal rather than deciding anything themselves.
 */

/** What a form got back. Empty is the state before anything was submitted. */
export type FormState = { error?: string };

export type PreviewResult =
  | { ok: true; preview: Preview }
  | { ok: false; error: string };

/**
 * A send that did not happen, and why.
 *
 * `preview` comes back with the refusal when the reason is that the recipients
 * moved: the screen has to show the new number, not just say that there is
 * one.
 */
export type SendResult =
  | { ok: true; recipientCount: number }
  | { ok: false; error: string; preview?: Preview };

export type CancelResult = { ok: true } | { ok: false; error: string };

function text(form: FormData, field: string): string {
  const value = form.get(field);
  return typeof value === "string" ? value.trim() : "";
}

/**
 * The addresses somebody pasted in, one per line or comma-separated.
 *
 * Deduplicated, because a list pasted out of a spreadsheet has the same person
 * on it twice and nobody wants two copies of the same email.
 */
function addressList(raw: string): string[] {
  const seen = new Set<string>();

  for (const part of raw.split(/[\n,;]/)) {
    const address = part.trim();
    if (address !== "") {
      seen.add(address);
    }
  }

  return [...seen];
}

/** The segment the compose form describes, or the sentence saying it does not. */
function readSegment(form: FormData): Segment | string {
  const kind = text(form, "segmentKind");

  if (kind === "applicants") {
    const status = text(form, "status");
    return status === "" ? "Pick a status." : { kind, status };
  }

  if (kind === "form") {
    const formId = text(form, "formId");
    return formId === "" ? "Pick a form." : { kind, formId };
  }

  if (kind === "addresses") {
    const addresses = addressList(text(form, "addresses"));

    if (addresses.length === 0) {
      return "Add at least one address.";
    }

    if (addresses.some((address) => !address.includes("@"))) {
      return "Every line must be an email address.";
    }

    return { kind, addresses };
  }

  return "Pick who this goes to.";
}

/**
 * Starts a campaign and opens it.
 *
 * A draft, always. Creating one sends nothing — the send is a separate act on
 * the campaign's own page, behind the preview, which is the only place it can
 * be done at all.
 */
export async function newCampaign(
  _previous: FormState,
  form: FormData,
): Promise<FormState> {
  const name = text(form, "name");
  if (name === "") {
    return { error: "A name is required." };
  }

  const templateKey = text(form, "templateKey");
  if (templateKey === "") {
    return { error: "A template key is required." };
  }

  const segment = readSegment(form);
  if (typeof segment === "string") {
    return { error: segment };
  }

  const created = await createCampaign({ name, templateKey, segment });
  if (!created.ok) {
    return { error: created.error };
  }

  revalidatePath("/mail");
  // Outside the checks above on purpose: redirect works by throwing, so it
  // must never sit where a catch could swallow it.
  redirect(`/mail/${created.id}`);
}

/** Resolves who the campaign would go to, now. */
export async function previewRecipients(id: string): Promise<PreviewResult> {
  return previewCampaign(id);
}

/**
 * Sends, if the recipients are still the ones that were previewed.
 *
 * `seen` is the count the person actually had in front of them. The recipients
 * are resolved again here and the two are compared, so the gate is not the
 * disabled button — someone who reloads, or whose segment gained forty people
 * while they read the sample, is stopped by the server rather than by the
 * screen. The button is the courtesy; this is the control.
 */
export async function sendNow(id: string, seen: number): Promise<SendResult> {
  const preview = await previewCampaign(id);
  if (!preview.ok) {
    return { ok: false, error: preview.error };
  }

  if (preview.preview.recipientCount !== seen) {
    return {
      ok: false,
      error: "The recipients changed since you previewed. Check them again.",
      preview: preview.preview,
    };
  }

  if (preview.preview.recipientCount === 0) {
    return { ok: false, error: "Nobody matches this segment." };
  }

  const sent = await sendCampaign(id);
  if (!sent.ok) {
    return { ok: false, error: sent.error };
  }

  revalidatePath("/mail");
  revalidatePath(`/mail/${id}`);
  return { ok: true, recipientCount: sent.recipientCount };
}

/** Stops a queued campaign. */
export async function stopSending(id: string): Promise<CancelResult> {
  const cancelled = await cancelCampaign(id);
  if (!cancelled.ok) {
    return { ok: false, error: cancelled.error };
  }

  revalidatePath("/mail");
  revalidatePath(`/mail/${id}`);
  return { ok: true };
}
