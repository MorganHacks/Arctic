import type { FormField } from "@/lib/api";
import type { FormTheme } from "../../../../../libs/ui/form-theme";

export type EditorDraft = { fields: FormField[]; theme: FormTheme };
type History = { past: EditorDraft[]; present: EditorDraft; future: EditorDraft[]; revision: number };
type Action =
  | { type: "fields"; update: (fields: FormField[]) => FormField[] }
  | { type: "theme"; theme: FormTheme }
  | { type: "restore"; draft: EditorDraft }
  | { type: "undo" }
  | { type: "redo" };

export function createEditorHistory(present: EditorDraft): History {
  return { past: [], present, future: [], revision: 0 };
}

export function editorHistory(state: History, action: Action): History {
  if (action.type === "undo") {
    if (!state.past.length) return state;
    return {
      past: state.past.slice(0, -1),
      present: state.past[state.past.length - 1],
      future: [state.present, ...state.future],
      revision: state.revision + 1,
    };
  }
  if (action.type === "redo") {
    if (!state.future.length) return state;
    return {
      past: [...state.past, state.present],
      present: state.future[0],
      future: state.future.slice(1),
      revision: state.revision + 1,
    };
  }
  const present = action.type === "restore" ? action.draft : action.type === "theme"
    ? { ...state.present, theme: action.theme }
    : { ...state.present, fields: action.update(state.present.fields) };
  if (JSON.stringify(present) === JSON.stringify(state.present)) return state;
  return { past: [...state.past.slice(-99), state.present], present, future: [], revision: state.revision + 1 };
}
