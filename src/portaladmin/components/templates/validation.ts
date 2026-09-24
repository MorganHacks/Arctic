import type { TemplateDraft } from "./types";

export type TemplateFieldErrors = Partial<Record<keyof TemplateDraft, string>>;

export function validateDesign(draft: TemplateDraft): TemplateFieldErrors {
  if (!draft.body.trim()) return { body: "Add your email content before continuing." };
  const maximum = draft.format === "html" ? 200_000 : 50_000;
  if (draft.body.length > maximum) return { body: `Keep the email content to ${maximum.toLocaleString("en-US")} characters or fewer.` };
  return {};
}

export function validateSettings(draft: TemplateDraft): TemplateFieldErrors {
  const errors: TemplateFieldErrors = {};
  const name = draft.name.trim();
  const subject = draft.subject.trim();

  if (name.length > 200 || /[\u0000-\u001f\u007f-\u009f]/.test(name)) {
    errors.name = "Use a campaign name of 200 characters or fewer, without control characters.";
  }

  if (!subject) {
    errors.subject = "Email subject is required.";
  } else if (subject.length > 200) {
    errors.subject = "Keep the email subject to 200 characters or fewer.";
  }

  if ((draft.previewText?.trim().length ?? 0) > 200) {
    errors.previewText = "Keep email preview text to 200 characters or fewer.";
  }

  const sender = draft.fromName?.trim() ?? "";
  if (!sender) {
    errors.fromName = "Sender name is required.";
  } else if (sender.length > 64 || /[^\x20-\x7e]|["<>]/.test(sender)) {
    errors.fromName = "Use plain text, up to 64 characters, without quotes or angle brackets.";
  }

  const replyTo = draft.replyTo?.trim() ?? "";
  if (replyTo) {
    const at = replyTo.lastIndexOf("@");
    const local = replyTo.slice(0, at);
    const domain = replyTo.slice(at + 1);
    if (at <= 0 || local.length > 64 || domain.length > 255
      || !/^[A-Za-z0-9._%+-]+$/.test(local)
      || !/^[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?)+$/.test(domain)) {
      errors.replyTo = "Enter a valid reply-to email address.";
    }
  }

  return errors;
}
