import assert from "node:assert/strict";
import test from "node:test";
import { isIdentityField, respondentFor, submittedAt } from "./respondent.ts";
const column = (key, name) => ({ key, column: name, storage: "column" });
const item = { anonymous: true, respondent: null, answers: { first: " Ada ", last: "Lovelace", address: "ada@example.test" } };
const fields = [column("first", "first_name"), column("last", "last_name"), column("address", "email")];

test("mapped application answers identify respondents without requiring sign-in", () => {
  assert.deepEqual(respondentFor(item, fields), { name: "Ada Lovelace", email: "ada@example.test", secondary: "ada@example.test", identified: true });
  assert.ok(fields.every(isIdentityField));
});

test("a generic name question is not assumed to identify an anonymous respondent", () => {
  const fields = [{ key: "first", label: "Name of a friend", storage: "responses" }];
  assert.equal(isIdentityField(fields[0]), false);
  assert.equal(respondentFor(item, fields).name, "Anonymous response");
  assert.equal(respondentFor(item, fields).identified, false);
});

test("signed-in survey identity stays available without name questions", () => {
  const person = respondentFor({ ...item, anonymous: false, respondent: "organizer@example.test" }, []);
  assert.equal(person.name, "organizer@example.test");
  assert.equal(person.secondary, "Signed-in respondent");
});

test("response dates use Eastern time consistently across summer and winter", () => {
  assert.deepEqual(submittedAt("2026-09-24T09:25:00Z"), { date: "Sep 24, 2026", time: "5:25 AM EDT" });
  assert.deepEqual(submittedAt("2026-01-24T09:25:00Z"), { date: "Jan 24, 2026", time: "4:25 AM EST" });
});
