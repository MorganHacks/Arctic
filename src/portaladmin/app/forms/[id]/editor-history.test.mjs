import assert from "node:assert/strict";
import test from "node:test";
import { createEditorHistory, editorHistory } from "./editor-history.ts";
import { DEFAULT_FORM_THEME, formThemeStyle, resolveFormTheme } from "../../../../../libs/ui/form-theme.ts";
import { reorderFields } from "./reorder-fields.ts";

const initial = () => createEditorHistory({
  fields: [{ key: "name", label: "Name" }, { key: "email", label: "Email" }],
  theme: { ...DEFAULT_FORM_THEME },
});

test("undo and redo restore deleted questions, ordering and theme together", () => {
  const start = initial();
  let history = editorHistory(start, { type: "fields", update: fields => [...fields].reverse() });
  history = editorHistory(history, { type: "fields", update: fields => fields.slice(1) });
  history = editorHistory(history, { type: "theme", theme: { ...DEFAULT_FORM_THEME, accent: "#6750a4" } });
  const edited = history.present;
  for (let i = 0; i < 3; i++) history = editorHistory(history, { type: "undo" });
  assert.deepEqual(history.present, start.present);
  for (let i = 0; i < 3; i++) history = editorHistory(history, { type: "redo" });
  assert.deepEqual(history.present, edited);
  assert.deepEqual(start.present.fields.map(field => field.key), ["name", "email"]);
});

test("editing after undo discards the abandoned redo branch", () => {
  let history = editorHistory(initial(), { type: "fields", update: fields => fields.slice(1) });
  history = editorHistory(history, { type: "undo" });
  history = editorHistory(history, { type: "theme", theme: { ...DEFAULT_FORM_THEME, font: "serif" } });
  assert.equal(history.future.length, 0);
  assert.equal(editorHistory(history, { type: "redo" }), history);
  assert.equal(history.present.fields.length, 2);
});

test("unchanged edits do not create revisions and retained history is bounded", () => {
  let history = initial();
  assert.equal(editorHistory(history, { type: "fields", update: fields => [...fields] }), history);
  assert.equal(editorHistory(history, { type: "undo" }), history);
  for (let i = 0; i < 150; i++) {
    history = editorHistory(history, { type: "fields", update: fields => fields.map(field => ({ ...field, label: String(i) })) });
  }
  assert.equal(history.past.length, 100);
  assert.equal(history.revision, 150);
});

test("theme values are constrained and accent text contrasts with light and dark colors", () => {
  assert.deepEqual(resolveFormTheme({ accent: "url(invalid)", background: "bad", font: "bad", size: "bad" }), DEFAULT_FORM_THEME);
  assert.equal(formThemeStyle({ accent: "#ffffff" })["--form-accent-ink"], "#14161a");
  assert.equal(formThemeStyle({ accent: "#000000" })["--form-accent-ink"], "#ffffff");
});

test("dropping a question across a page break preserves every field and undoes as one move", () => {
  const fields = [{ key: "name" }, { key: "page_two", type: "section" }, { key: "email" }, { key: "school" }];
  const start = createEditorHistory({ fields, theme: DEFAULT_FORM_THEME });
  const moved = editorHistory(start, { type: "fields", update: current => reorderFields(current, "name", 3) });
  assert.deepEqual(moved.present.fields.map(field => field.key), ["page_two", "email", "school", "name"]);
  assert.equal(moved.present.fields[3], fields[0]);
  assert.equal(moved.past.length, 1);
  assert.deepEqual(editorHistory(moved, { type: "undo" }).present, start.present);
  assert.deepEqual(reorderFields(moved.present.fields, "page_two", 2).map(field => field.key), ["email", "school", "page_two", "name"]);
});

test("dropping at the original position or with an invalid target leaves the draft unchanged", () => {
  const { present } = initial();
  for (const [key, to] of [["name", 0], ["missing", 1], ["name", -1], ["name", 20]]) {
    assert.equal(reorderFields(present.fields, key, to), present.fields);
  }
});
