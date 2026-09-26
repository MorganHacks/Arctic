import type { FormField } from "@/lib/api";

export function previewPages(fields: FormField[]) {
  const pages: { section: FormField | null; fields: FormField[] }[] = [];
  let current: (typeof pages)[number] = { section: null, fields: [] };
  for (const field of fields) {
    if (field.type === "section") {
      if (current.section || current.fields.length) pages.push(current);
      current = { section: field, fields: [] };
    } else {
      current.fields.push(field);
    }
  }
  if (current.section || current.fields.length || !pages.length) pages.push(current);
  return pages;
}

export function previewProblem(field: FormField, data: FormData): string | null {
  if (field.type === "section") return null;
  const entries = data.getAll(field.key);
  const answer = entries[0];
  const value = typeof answer === "string" ? answer.trim() : "";
  const present = field.type === "file"
    ? typeof answer === "object" && answer.size > 0
    : entries.some(entry => typeof entry === "string" && entry.trim().length > 0);
  if (!present) {
    return field.required
      ? field.type === "consent" ? "Please agree to continue." : "This is a required question."
      : null;
  }
  if (field.type === "file" && typeof answer === "object") {
    if (!answer.name.toLowerCase().endsWith(".pdf")) return "Choose a PDF file.";
    if (answer.size > 5 * 1024 * 1024) return "Choose a PDF smaller than 5 MB.";
  }
  if (field.type === "email" && !/^[^\s@]+@[^\s@]+$/.test(value)) return "Enter a valid email address.";
  if (field.type === "number") {
    const number = Number(value);
    if (!Number.isFinite(number)) return "Enter a number.";
    if (field.min != null && number < field.min) return `Enter ${field.min} or higher.`;
    if (field.max != null && number > field.max) return `Enter ${field.max} or lower.`;
  }
  if (["shortText", "paragraph", "email", "phone"].includes(field.type)) {
    if (field.minLength != null && value.length < field.minLength) return `Use at least ${field.minLength} characters.`;
    const max = field.maxLength ?? (field.type === "paragraph" ? 5000 : 500);
    if (value.length > max) return `Use no more than ${max} characters.`;
  }
  return null;
}
