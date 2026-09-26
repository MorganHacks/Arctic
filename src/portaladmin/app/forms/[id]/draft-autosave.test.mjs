import assert from "node:assert/strict";
import test from "node:test";
import { createDraftAutosave, draftFingerprint, encodeRecovery, readRecovery, recoveryAction, recoveryKey } from "./draft-autosave.ts";
import { createEditorHistory, editorHistory } from "./editor-history.ts";

const ok = { ok: true, problems: [] };
const draft = (label = "Name") => ({
  fields: [{ key: "name", type: "shortText", label, required: false, options: [], storage: "responses" }],
  theme: { accent: "#003970", background: "neutral", font: "sans", size: "medium", headerImage: null },
});
const deferred = () => {
  let resolve;
  const promise = new Promise(done => { resolve = done; });
  return { promise, resolve };
};

test("MLH badge changes are saved and recovered independently of theme styling", async () => {
  const original = draft();
  const changed = { ...original, theme: { ...original.theme, showMlhBadge: true } };
  assert.notEqual(draftFingerprint(original), draftFingerprint(changed));
  assert.equal(draftFingerprint(original), draftFingerprint({ ...original, theme: { ...original.theme, showMlhBadge: false } }));
  const calls = [];
  const saver = createDraftAutosave({ draft: original, revision: 0 }, async next => { calls.push(next); return ok; });
  saver.update(changed, 1);
  assert.equal(JSON.parse(encodeRecovery(saver.getSnapshot())).draft.theme.showMlhBadge, true);
  await saver.flush();
  assert.equal(calls.length, 1);
  assert.equal(calls[0].theme.showMlhBadge, true);
  assert.equal(saver.getSnapshot().status, "saved");
});

test("slow saves serialize later edits and never mark an older revision as saved", async () => {
  const first = deferred();
  const second = deferred();
  const calls = [];
  const saver = createDraftAutosave({ draft: draft(), revision: 0 }, next => {
    calls.push(next);
    return calls.length === 1 ? first.promise : second.promise;
  });
  saver.update(draft("First edit"), 1);
  const saving = saver.flush();
  await Promise.resolve();
  saver.update(draft("Intermediate edit"), 2);
  saver.update(draft("Latest edit"), 3);
  assert.equal(saver.flush(), saving);
  assert.equal(calls.length, 1);
  first.resolve(ok);
  await Promise.resolve();
  assert.equal(saver.getSnapshot().status, "saving");
  assert.equal(saver.pending(), true);
  assert.equal(calls.length, 2);
  assert.equal(calls[1].fields[0].label, "Latest edit");
  second.resolve(ok);
  assert.equal((await saving).ok, true);
  assert.equal(saver.getSnapshot().status, "saved");
  assert.equal(saver.getSnapshot().saved.revision, 3);
  assert.equal(saver.pending(), false);
  await saver.flush();
  assert.equal(calls.length, 2);
});

test("failed requests stay pending and retry the latest complete draft", async () => {
  const calls = [];
  const saver = createDraftAutosave({ draft: draft(), revision: 0 }, async next => {
    calls.push(next);
    return calls.length === 1 ? { ok: false, problems: [], retryable: true } : ok;
  });
  saver.update(draft("Offline edit"), 1);
  await saver.flush();
  assert.equal(saver.getSnapshot().status, "failed");
  assert.equal(saver.getSnapshot().saved.revision, 0);
  assert.equal(saver.pending(), true);
  const latest = draft("Latest while offline");
  latest.theme.accent = "#c18c24";
  saver.update(latest, 2);
  await saver.flush();
  assert.deepEqual(calls[1], latest);
  assert.equal(saver.pending(), false);
});

test("rejected server actions become retryable failures instead of unhandled rejections", async () => {
  let unavailable = true;
  const saver = createDraftAutosave({ draft: draft(), revision: 0 }, async () => {
    if (unavailable) throw new Error("Connection closed");
    return ok;
  });
  saver.update(draft("Keep me"), 1);
  assert.equal((await saver.flush()).retryable, true);
  assert.equal(saver.pending(), true);
  unavailable = false;
  await saver.flush();
  assert.equal(saver.getSnapshot().status, "saved");
});

test("refresh recovery retains questions, ordering, page breaks and the complete theme before a save", () => {
  const original = draft();
  const edited = draft("Latest wording");
  edited.fields.unshift({ key: "section", type: "section", label: "Introduction", required: false, options: [], storage: "responses" });
  edited.theme = { accent: "#c18c24", background: "tint", font: "serif", size: "large", headerImage: "data:image/webp;base64,UklGRfake" };
  const saver = createDraftAutosave({ draft: original, revision: 0 }, async () => ok);
  saver.update(edited, 1);
  const recovery = readRecovery(encodeRecovery(saver.getSnapshot()));
  assert.deepEqual(recovery.draft, edited);
  assert.equal(recoveryAction(recovery, original), "restore");
  let history = editorHistory(createEditorHistory(original), { type: "restore", draft: recovery.draft });
  assert.deepEqual(history.present, edited);
  assert.equal(history.revision, 1);
  history = editorHistory(history, { type: "undo" });
  assert.deepEqual(history.present, original);
});

test("refresh during an unacknowledged save recognizes the written base without discarding newer edits", async () => {
  const response = deferred();
  const base = draft();
  const inFlight = draft("Already reached server");
  const latest = draft("Newer local edit");
  const saver = createDraftAutosave({ draft: base, revision: 0 }, async () => response.promise);
  saver.update(inFlight, 1);
  const saving = saver.flush();
  await Promise.resolve();
  saver.update(latest, 2);
  const recovery = readRecovery(encodeRecovery(saver.getSnapshot()));
  assert.equal(recoveryAction(recovery, inFlight), "restore");
  assert.equal(recoveryAction(recovery, base), "restore");
  assert.equal(recoveryAction(recovery, latest), "saved");
  assert.equal(recoveryAction(recovery, draft("Someone else's edit")), "conflict");
  response.resolve(ok);
  await saving;
});

test("recovery compares equivalent API fields despite property order and omitted optional values", () => {
  const initial = draft();
  const fromServer = structuredClone(initial);
  fromServer.fields = fromServer.fields.map(field => ({ max: null, help: null, ...Object.fromEntries(Object.entries(field).reverse()), min: null }));
  fromServer.theme = Object.fromEntries(Object.entries(fromServer.theme).reverse());
  assert.equal(draftFingerprint(initial), draftFingerprint(fromServer));
  const saver = createDraftAutosave({ draft: initial, revision: 0 }, async () => ok);
  saver.update(draft("Latest"), 1);
  assert.equal(recoveryAction(readRecovery(encodeRecovery(saver.getSnapshot())), fromServer), "restore");
});

test("recovery is scoped to the user, form and draft version and ignores corrupt storage", () => {
  assert.equal(new Set([
    recoveryKey("one", "form", 1), recoveryKey("two", "form", 1),
    recoveryKey("one", "other", 1), recoveryKey("one", "form", 2),
  ]).size, 4);
  for (const raw of [null, "{", "null", "{}", JSON.stringify({ draft: { fields: [null] }, bases: ["base"] })]) {
    assert.equal(readRecovery(raw), null);
  }
});
