import type { FormField } from "@/lib/api";
import type { ResponseItem } from "./types";

const identityColumns = new Set(["first_name", "last_name", "email"]);

export function isIdentityField(field: FormField | null): boolean {
  return field?.storage === "column" && identityColumns.has(field.column ?? field.key);
}

export function respondentFor(item: ResponseItem, fields: FormField[]) {
  const answer = (column: string) => {
    const field = fields.find((candidate) =>
      candidate.storage === "column" && (candidate.column ?? candidate.key) === column,
    );
    const value = field ? item.answers[field.key] : null;
    return typeof value === "string" ? value.trim() : "";
  };
  const name = [answer("first_name"), answer("last_name")].filter(Boolean).join(" ");
  const email = item.respondent?.trim() || answer("email");

  return {
    name: name || email || (item.anonymous ? "Anonymous response" : "Respondent"),
    email,
    secondary: name && email ? email : item.anonymous ? "Submitted without signing in" : "Signed-in respondent",
    identified: Boolean(name || email),
  };
}

const date = new Intl.DateTimeFormat("en-US", {
  month: "short", day: "numeric", year: "numeric", timeZone: "America/New_York",
});
const time = new Intl.DateTimeFormat("en-US", {
  hour: "numeric", minute: "2-digit", timeZoneName: "short", timeZone: "America/New_York",
});

export function submittedAt(iso: string) {
  const value = new Date(iso);
  return Number.isNaN(value.getTime())
    ? { date: iso, time: "" }
    : { date: date.format(value), time: time.format(value) };
}
