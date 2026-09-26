import { test } from "node:test";
import assert from "node:assert/strict";
import { COUNTDOWN_WINDOW_MS, countdownParts, deadlineSeconds } from "./deadline-time.ts";

const closesAt = "2027-04-03T13:00:00.000Z";
const deadline = Date.parse(closesAt);

test("the countdown appears exactly 24 hours before the deadline", () => {
  assert.equal(deadlineSeconds(closesAt, deadline - COUNTDOWN_WINDOW_MS - 1), null);
  assert.equal(deadlineSeconds(closesAt, deadline - COUNTDOWN_WINDOW_MS), 86400);
  assert.deepEqual(countdownParts(86400), ["24", "00", "00"]);
});

test("the final second remains visible until the deadline", () => {
  assert.equal(deadlineSeconds(closesAt, deadline - 1), 1);
  assert.equal(deadlineSeconds(closesAt, deadline), 0);
  assert.equal(deadlineSeconds(closesAt, deadline + 5000), 0);
});

test("the countdown uses the absolute deadline across time zones", () => {
  const now = Date.parse("2027-04-03T06:59:59-04:00");
  assert.equal(deadlineSeconds(closesAt, now), 7201);
  assert.equal(deadlineSeconds("2027-04-03T09:00:00-04:00", now), 7201);
  assert.deepEqual(countdownParts(7201), ["02", "00", "01"]);
});

test("elapsed time is recalculated after a suspended tab resumes", () => {
  assert.equal(deadlineSeconds(closesAt, deadline - 3600000), 3600);
  assert.equal(deadlineSeconds(closesAt, deadline - 60000), 60);
  assert.deepEqual(countdownParts(60), ["00", "01", "00"]);
});

test("invalid dates do not show a countdown", () => {
  assert.equal(deadlineSeconds("not a date", deadline), null);
});
