/**
 * Reading mode: the plumbing, with no React and no DOM.
 *
 * Shared by all three frontends because three copies of a cookie name and an
 * attribute name drift, and a drift here is invisible — one app quietly stops
 * remembering the reader's choice and nobody finds out until somebody complains.
 *
 * The shape of the thing:
 *
 * - The **cookie** is the source of truth. It is read on the server so the
 *   first byte the browser gets already carries `data-reading="on"`. A flash of
 *   the wrong typeface is the exact failure this feature exists to prevent, so
 *   nothing here is allowed to wait for hydration when a cookie is present.
 * - The **operating system preference** (`prefers-contrast: more`) is a
 *   fallback for somebody who has never touched the toggle. It is only ever
 *   consulted when the cookie is absent, and an explicit choice always beats
 *   it — otherwise turning reading mode off would silently not stick on a
 *   machine that has asked for more contrast.
 *
 * There is no detection of dyslexia anywhere in this file, because the platform
 * cannot do it. No browser API reports it, and inferring it from how somebody
 * uses the page would be both impossible and a privacy problem.
 */

/** The cookie. Named with the same `arctic_` prefix as the console's other one. */
export const READING_MODE_COOKIE = "arctic_reading_mode";

/** Set on `<html>` when reading mode is on. Every reading rule keys off this. */
export const READING_MODE_ATTRIBUTE = "data-reading";

/** Ten years. Long enough that nobody re-asks about a reading preference. */
const COOKIE_MAX_AGE = 60 * 60 * 24 * 365 * 10;

export type ReadingMode = "on" | "off";

/**
 * What a cookie value means.
 *
 * Anything unrecognised is `undefined` rather than `off`, and that distinction
 * is load-bearing. `undefined` means "this reader has never chosen", which is
 * the only state in which the OS preference gets a say. Reading a typo or a
 * truncated cookie as an explicit `off` would quietly overrule somebody who set
 * the OS preference and never touched the toggle.
 */
export function parseReadingMode(raw: string | undefined | null): ReadingMode | undefined {
  if (raw === "on") return "on";
  if (raw === "off") return "off";
  return undefined;
}

/**
 * Reading mode for the first paint.
 *
 * `undefined` means the server could not tell, and the client resolves it after
 * hydration from the OS preference. A returned boolean is final: the server had
 * a cookie, so the client must not second-guess it.
 */
export function initialReadingMode(cookie: string | undefined | null): boolean | undefined {
  const choice = parseReadingMode(cookie);
  return choice === undefined ? undefined : choice === "on";
}

/**
 * The fallback, for somebody who has never chosen.
 *
 * `prefers-contrast: more` is the closest thing a browser exposes to "I am
 * struggling to read this", and it is a real preference rather than a guess.
 *
 * `forced-colors` is deliberately not included. Windows High Contrast means the
 * operating system has already taken over every colour on the page, and
 * restyling the type underneath it fights a setting the reader set on purpose.
 *
 * Guarded rather than assumed: this runs in a browser and in a test runner, and
 * neither is guaranteed to have `matchMedia`.
 */
export function prefersReadingMode(): boolean {
  if (typeof window === "undefined" || typeof window.matchMedia !== "function") return false;
  return window.matchMedia("(prefers-contrast: more)").matches;
}

/**
 * The reader's stored choice, read from the browser.
 *
 * Used on the client to resolve the one case the server cannot: a visitor with
 * no cookie. Kept separate from `initialReadingMode()` so the server half of
 * this stays pure and testable without a `document`.
 */
export function storedReadingMode(): ReadingMode | undefined {
  if (typeof document === "undefined") return undefined;
  const found = document.cookie
    .split("; ")
    .find((part) => part.startsWith(`${READING_MODE_COOKIE}=`));
  return parseReadingMode(found?.slice(READING_MODE_COOKIE.length + 1));
}

/** The cookie string. `Path=/` so all three apps on a host share one choice. */
export function readingModeCookie(mode: ReadingMode): string {
  return `${READING_MODE_COOKIE}=${mode}; Path=/; Max-Age=${COOKIE_MAX_AGE}; SameSite=Lax`;
}

/**
 * Remember the choice.
 *
 * The cookie is what the server reads and is the part that matters. localStorage
 * is mirrored alongside it because it is what survives a browser that drops
 * cookies on close, and reading it costs nothing.
 *
 * Both writes are in `try/catch`. Safari throws on `localStorage` in private
 * browsing, and a thrown exception here would take the whole page down for a
 * reader who is only trying to make the text bigger.
 */
export function rememberReadingMode(mode: ReadingMode): void {
  try {
    document.cookie = readingModeCookie(mode);
  } catch {
    /* A browser that will not keep the cookie still gets the mode this session. */
  }

  try {
    localStorage.setItem(READING_MODE_COOKIE, mode);
  } catch {
    /* Private browsing. The attribute below is what the reader actually sees. */
  }

  applyReadingMode(mode === "on");
}

/** Flip the attribute on `<html>`, which is what every reading rule keys off. */
export function applyReadingMode(on: boolean): void {
  if (on) {
    document.documentElement.setAttribute(READING_MODE_ATTRIBUTE, "on");
  } else {
    document.documentElement.removeAttribute(READING_MODE_ATTRIBUTE);
  }
}