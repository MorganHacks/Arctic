import type { FieldType, FormField } from "@/lib/api";

/**
 * The question types, in the order they are offered.
 *
 * Ordered by how often they get used rather than alphabetically or by the
 * enum. Somebody building a registration form reaches for short text and
 * paragraph over and over, and a file upload once.
 */
export const TYPES: { value: FieldType; label: string }[] = [
  { value: "shortText", label: "Short text" },
  { value: "paragraph", label: "Paragraph" },
  { value: "select", label: "Dropdown" },
  { value: "radio", label: "Choice" },
  { value: "checkboxes", label: "Checkboxes" },
  { value: "email", label: "Email" },
  { value: "phone", label: "Phone" },
  { value: "number", label: "Number" },
  { value: "date", label: "Date" },
  { value: "consent", label: "Agreement" },
  { value: "file", label: "File upload" },
];

export const TYPE_NAMES = new Map(TYPES.map((type) => [type.value, type.label]));

/** The three that need something to choose from. */
export const CHOICE_TYPES = new Set<FieldType>(["select", "radio", "checkboxes"]);

/**
 * A key for a question that has just been added.
 *
 * Generated once, here, and never again. This is what an answer is filed
 * under, so regenerating it — on a rename, on a save, on anything — would
 * orphan every answer already given and nothing on screen would look wrong
 * while it happened.
 *
 * Random rather than derived from the label, because a question is added
 * before it is worded: there is nothing to derive from at the only moment the
 * key is allowed to be decided. The randomness is for uniqueness and not for
 * secrecy — these keys become column headers in an export, and are meant to be
 * read.
 */
export function newKey(): string {
  const alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
  const bytes = new Uint8Array(6);
  crypto.getRandomValues(bytes);

  let suffix = "";
  for (const byte of bytes) {
    suffix += alphabet[byte % alphabet.length];
  }

  return `question_${suffix}`;
}

/** A question as it starts life: keyed, typed, and otherwise blank. */
export function blankField(type: FieldType): FormField {
  return {
    key: newKey(),
    type,
    label: "",
    help: null,
    required: false,
    // A choice question with nothing to choose from cannot be published, so it
    // starts with one option rather than with an empty list and a complaint.
    options: CHOICE_TYPES.has(type) ? [{ value: "option_1", label: "Option 1" }] : [],
    storage: "responses",
    column: null,
    locked: false,
  };
}

/**
 * A copy of a question, ready to sit beside the original.
 *
 * A new key, always. The key is what an answer is filed under, so two
 * questions sharing one make their answers indistinguishable afterwards —
 * which is why the API refuses to publish a form that has them.
 *
 * The copy is never locked and never keeps a column. MLH's questions own the
 * columns they write to, and a second question pointed at the same one would
 * overwrite the first's answer. Options are copied rather than shared, so
 * editing one question's list does not reword the other's.
 */
export function copyOf(field: FormField): FormField {
  return {
    ...field,
    key: newKey(),
    locked: false,
    storage: "responses",
    column: null,
    options: field.options.map((option) => ({ ...option })),
  };
}

/**
 * A value for a new option, unique within its question.
 *
 * The value is what gets stored and the label is what gets shown, so two
 * options sharing a value make their answers indistinguishable afterwards —
 * which is a thing no reporting can recover from and which the API refuses to
 * publish.
 */
export function nextOptionValue(existing: { value: string }[]): string {
  const taken = new Set(existing.map((option) => option.value));

  let n = existing.length + 1;
  while (taken.has(`option_${n}`)) {
    n += 1;
  }

  return `option_${n}`;
}
