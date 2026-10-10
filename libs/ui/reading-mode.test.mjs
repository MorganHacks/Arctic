import assert from "node:assert/strict";
import { test } from "node:test";
import {
  initialReadingMode,
  parseReadingMode,
  prefersReadingMode,
  readingModeCookie,
  READING_MODE_COOKIE,
} from "./reading-mode.ts";

/*
 * docs/feature-flags.md is explicit that a test covering only the off case
 * passes just as well when the route is broken, so both positions are here.
 *
 * These are the pure half: what a cookie value means, and what the server
 * concludes from it. The half that touches the DOM — applyReadingMode,
 * rememberReadingMode, storedReadingMode — needs a browser, and pretending
 * otherwise in a test runner would test the stubs rather than the code.
 */

test("a stored choice is honoured exactly as written", () => {
  assert.equal(parseReadingMode("on"), "on");
  assert.equal(parseReadingMode("off"), "off");
});

test("an absent or malformed cookie is no choice rather than an off", () => {
  // The distinction is load-bearing. `undefined` is the only state in which the
  // OS preference gets a say; reading a typo as an explicit "off" would
  // overrule somebody who set the OS preference and never touched the toggle.
  assert.equal(parseReadingMode(undefined), undefined);
  assert.equal(parseReadingMode(null), undefined);
  assert.equal(parseReadingMode(""), undefined);
  assert.equal(parseReadingMode("true"), undefined);
  assert.equal(parseReadingMode("ON"), undefined);
  assert.equal(parseReadingMode("1"), undefined);
});

test("an explicit choice survives to the first paint", () => {
  assert.equal(initialReadingMode("on"), true);
  assert.equal(initialReadingMode("off"), false);
});

test("no cookie is left for the client to resolve, not decided early", () => {
  // False here would be a bug that looks like a feature: the server would paint
  // the default face and the OS preference could never turn it on.
  assert.equal(initialReadingMode(undefined), undefined);
  assert.equal(initialReadingMode(null), undefined);
  assert.equal(initialReadingMode("nonsense"), undefined);
});

test("the cookie is host-wide, lax, and long-lived", () => {
  // Path=/ so all three apps on one host share a choice; SameSite=Lax because
  // that is what the session cookie already uses and this is not a credential.
  const cookie = readingModeCookie("on");
  assert.equal(cookie, `${READING_MODE_COOKIE}=on; Path=/; Max-Age=315360000; SameSite=Lax`);
  assert.ok(!cookie.includes("Secure"), "a Secure cookie would be dropped on the local http origin");
});

test("the OS fallback is false without a matchMedia, not a crash", () => {
  // Runs in a test runner and in a server render. Neither has matchMedia, and
  // a thrown TypeError here would take down a page over a reading preference.
  assert.equal(prefersReadingMode(), false);
});

test("the OS fallback reads the contrast preference and nothing else", () => {
  // forced-colors is deliberately absent: under Windows High Contrast the OS
  // has already taken over every colour, and restyling the type underneath it
  // fights a setting the reader chose on purpose.
  const seen = [];
  globalThis.window = {
    matchMedia: (query) => {
      seen.push(query);
      return { matches: false };
    },
  };
  try {
    assert.equal(prefersReadingMode(), false);
    assert.deepEqual(seen, ["(prefers-contrast: more)"]);
    assert.ok(!seen.some((q) => q.includes("forced-colors")));
  } finally {
    delete globalThis.window;
  }
});

test("a reader who asks for more contrast gets reading mode by default", () => {
  globalThis.window = {
    matchMedia: (query) => ({ matches: query.includes("prefers-contrast: more") }),
  };
  try {
    assert.equal(prefersReadingMode(), true);
  } finally {
    delete globalThis.window;
  }
});