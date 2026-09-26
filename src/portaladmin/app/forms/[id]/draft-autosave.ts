import type { SaveResult } from "../actions";
import type { EditorDraft } from "./editor-history";

export type SaveStatus = "dirty" | "saving" | "saved" | "failed";
type Snapshot = { draft: EditorDraft; revision: number };
type SaveState = {
  status: SaveStatus;
  latest: Snapshot;
  saved: Snapshot;
  writing: Snapshot | null;
  result: SaveResult;
};

const SUCCESS: SaveResult = { ok: true, problems: [] };

export function createDraftAutosave(initial: Snapshot, save: (draft: EditorDraft) => Promise<SaveResult>) {
  let state: SaveState = { status: "saved", latest: initial, saved: initial, writing: null, result: SUCCESS };
  let flight: Promise<SaveResult> | null = null;
  const listeners = new Set<() => void>();
  const emit = (next: SaveState) => {
    state = next;
    listeners.forEach(listener => listener());
  };
  const pending = () => state.latest.revision !== state.saved.revision;

  return {
    getSnapshot: () => state,
    subscribe: (listener: () => void) => {
      listeners.add(listener);
      return () => { listeners.delete(listener); };
    },
    pending,
    update(draft: EditorDraft, revision: number) {
      if (revision <= state.latest.revision) return;
      emit({ ...state, latest: { draft, revision }, status: flight ? "saving" : "dirty", result: SUCCESS });
    },
    flush(): Promise<SaveResult> {
      if (flight) return flight;
      if (!pending()) return Promise.resolve(state.result);
      flight = Promise.resolve().then(async () => {
        while (pending()) {
          const writing = state.latest;
          emit({ ...state, writing, status: "saving", result: SUCCESS });
          let result: SaveResult;
          try {
            result = await save(writing.draft);
          } catch {
            result = { ok: false, error: "Your changes could not be saved. Retrying automatically…", problems: [], retryable: true };
          }
          if (!result.ok) {
            emit({ ...state, status: "failed", result });
            return result;
          }
          const more = state.latest.revision !== writing.revision;
          emit({ ...state, saved: writing, writing: null, status: more ? "saving" : "saved", result: more ? SUCCESS : result });
        }
        return state.result;
      }).finally(() => { flight = null; });
      return flight;
    },
  };
}

export function draftFingerprint(draft: EditorDraft): string {
  return JSON.stringify({
    fields: draft.fields.map(field => ({
      key: field.key, type: field.type, label: field.label, help: field.help ?? null,
      required: field.required, options: field.options.map(option => ({ value: option.value, label: option.label })),
      storage: field.storage, column: field.column ?? null,
      minLength: field.minLength ?? null, maxLength: field.maxLength ?? null,
      min: field.min ?? null, max: field.max ?? null,
    })),
    theme: {
      accent: draft.theme.accent.toLowerCase(), background: draft.theme.background,
      font: draft.theme.font, size: draft.theme.size, headerImage: draft.theme.headerImage ?? null,
      showMlhBadge: draft.theme.showMlhBadge === true,
    },
  });
}

export type DraftRecovery = { draft: EditorDraft; bases: string[] };

export function recoveryKey(ownerId: string, formId: string, version: number): string {
  return `mh-form-draft:${ownerId}:${formId}:${version}`;
}

export function encodeRecovery(state: SaveState): string {
  return JSON.stringify({
    draft: state.latest.draft,
    bases: [state.saved, ...(state.writing ? [state.writing] : [])].map(snapshot => draftFingerprint(snapshot.draft)),
  } satisfies DraftRecovery);
}

export function readRecovery(raw: string | null): DraftRecovery | null {
  if (!raw) return null;
  try {
    const value = JSON.parse(raw);
    if (!value || !Array.isArray(value.bases) || !value.bases.length || value.bases.some((base: unknown) => typeof base !== "string")) return null;
    const draft = value.draft;
    if (!draft || !Array.isArray(draft.fields)) return null;
    const keys = new Set<string>();
    for (const field of draft.fields) {
      if (!field || typeof field.key !== "string" || keys.has(field.key) || typeof field.label !== "string"
        || typeof field.required !== "boolean" || !["column", "responses"].includes(field.storage)
        || !["shortText", "paragraph", "select", "radio", "checkboxes", "email", "phone", "number", "date", "consent", "file", "section"].includes(field.type)
        || !Array.isArray(field.options) || field.options.some((option: { value?: unknown; label?: unknown } | null) => !option || typeof option.value !== "string" || typeof option.label !== "string")) return null;
      for (const key of ["help", "column"]) if (field[key] != null && typeof field[key] !== "string") return null;
      for (const key of ["minLength", "maxLength", "min", "max"]) if (field[key] != null && (typeof field[key] !== "number" || !Number.isFinite(field[key]))) return null;
      keys.add(field.key);
    }
    const theme = draft.theme;
    if (!theme || typeof theme.accent !== "string" || !/^#[0-9a-f]{6}$/i.test(theme.accent)
      || !["neutral", "tint", "white"].includes(theme.background) || !["sans", "serif", "mono"].includes(theme.font)
      || !["small", "medium", "large"].includes(theme.size)
      || theme.headerImage != null && (typeof theme.headerImage !== "string" || !theme.headerImage.startsWith("data:image/webp;base64,"))) return null;
    return value as DraftRecovery;
  } catch {
    return null;
  }
}

export function recoveryAction(recovery: DraftRecovery, serverDraft: EditorDraft): "saved" | "restore" | "conflict" {
  const server = draftFingerprint(serverDraft);
  if (draftFingerprint(recovery.draft) === server) return "saved";
  return recovery.bases.includes(server) ? "restore" : "conflict";
}
