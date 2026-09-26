import assert from "node:assert/strict";
import { test } from "node:test";
import { errorNotifications, summarizeErrors } from "./error-notifications.ts";

test("independent errors stack and dismiss without removing other errors", () => {
  const first = { id: "save", title: "Save failed", message: "Try again", target: null };
  const second = { id: "upload", title: "Upload failed", message: "Use a PDF", target: null };
  errorNotifications.show(first);
  errorNotifications.show(second);
  assert.deepEqual(errorNotifications.snapshot(), [first, second]);
  errorNotifications.dismiss("save");
  assert.deepEqual(errorNotifications.snapshot(), [second]);
  errorNotifications.dismiss("upload");
});

test("a later attempt can show an identical dismissed error", () => {
  const error = { id: "retry", title: "Save failed", message: "Try again", target: null };
  errorNotifications.show(error);
  errorNotifications.dismiss(error.id);
  errorNotifications.show(error);
  assert.deepEqual(errorNotifications.snapshot(), [error]);
  assert.deepEqual(errorNotifications.serverSnapshot(), []);
  errorNotifications.dismiss(error.id);
});

test("subscribers receive updates and detach cleanly", () => {
  let calls = 0;
  const unsubscribe = errorNotifications.subscribe(() => calls++);
  errorNotifications.show({ id: "notify", title: "Error", message: "Failed", target: null });
  assert.equal(calls, 1);
  unsubscribe();
  errorNotifications.dismiss("notify");
  assert.equal(calls, 1);
});

test("validation summaries keep the toast compact and omit duplicates", () => {
  assert.equal(summarizeErrors(["Email: Required", "", "Email: Required"]), "Email: Required");
  assert.equal(summarizeErrors(["A", "B", "C", "D", "E"]), "A\nB\nC\n2 more answers need attention.");
});
