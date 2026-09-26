import assert from "node:assert/strict";
import test from "node:test";
import { previewPages, previewProblem } from "./preview-answers.ts";

const field = (key, type = "shortText", required = true, extra = {}) => ({ key, type, required, label: key, options: [], storage: "responses", ...extra });
const answers = (...values) => {
  const data = new FormData();
  for (const [key, value] of values) data.append(key, value);
  return data;
};

test("blank and whitespace answers only block required questions", () => {
  assert.ok(previewProblem(field("name"), answers(["name", "  "])));
  assert.equal(previewProblem(field("name", "shortText", false), answers()), null);
  assert.equal(previewProblem(field("name"), answers(["name", "A name"])), null);
});

test("checkbox groups and agreements require an actual selection", () => {
  for (const type of ["checkboxes", "radio", "consent", "select"]) {
    assert.ok(previewProblem(field("choice", type), answers()));
    assert.equal(previewProblem(field("choice", type), answers(["choice", "yes"])), null);
  }
  assert.equal(previewProblem(field("choice", "checkboxes"), answers(["choice", "a"], ["choice", "b"])), null);
});

test("preview checks email and configured text and number limits", () => {
  assert.ok(previewProblem(field("email", "email"), answers(["email", "invalid"])));
  assert.equal(previewProblem(field("email", "email"), answers(["email", "preview@example.com"])), null);
  assert.ok(previewProblem(field("age", "number", false, { min: 18 }), answers(["age", "17"])));
  assert.ok(previewProblem(field("age", "number", false, { max: 100 }), answers(["age", "101"])));
  assert.ok(previewProblem(field("bio", "paragraph", false, { minLength: 10 }), answers(["bio", "short"])));
  assert.ok(previewProblem(field("bio", "paragraph", false, { maxLength: 3 }), answers(["bio", "long"])));
});

test("file checks operate on the local selection without an upload", () => {
  assert.ok(previewProblem(field("resume", "file"), answers(["resume", new File([], "")])));
  assert.ok(previewProblem(field("resume", "file"), answers(["resume", new File(["text"], "notes.txt")])));
  assert.equal(previewProblem(field("resume", "file"), answers(["resume", new File(["%PDF"], "resume.pdf")])), null);
});

test("a leading section starts page one and later sections split pages", () => {
  const fields = [field("intro", "section"), field("name"), field("details", "section"), field("email", "email")];
  const pages = previewPages(fields);
  assert.equal(pages.length, 2);
  assert.deepEqual(pages.map(page => page.fields.map(item => item.key)), [["name"], ["email"]]);
  assert.equal(pages[0].section.key, "intro");
  assert.equal(previewProblem(fields[0], answers()), null);
});

test("questions before a section and empty section pages are retained", () => {
  const pages = previewPages([field("name"), field("intro", "section"), field("details", "section")]);
  assert.equal(pages.length, 3);
  assert.equal(pages[0].section, null);
  assert.equal(pages[1].fields.length, 0);
  assert.equal(previewPages([]).length, 1);
});
