/**
 * Whether a feature is on.
 *
 * Server-side only. It reads process.env by a name computed at runtime, which
 * works in a server component or a route handler and does not work in the
 * browser: Next replaces `process.env.SOMETHING` in client bundles by matching
 * the literal text, so a computed lookup finds an empty object there and every
 * flag would read as its file default no matter what the environment says.
 * Importing this into a "use client" file is the one way to get that wrong.
 *
 * features.json holds the default, and an environment variable of the same name
 * upper-cased overrides it -- the same arrangement the .NET services use, with
 * the same key names, so a flag can be found by searching for one string across
 * the whole repository.
 */
import defaults from "@/features.json";

/** The applicant portal. Off means /portal sends people to the public site. */
export const HACKER_PORTAL = "enable_hacker_portal_feature";

/**
 * Reading mode: OpenDyslexic plus the spacing that makes it work.
 *
 * The application form is the longest stretch of reading an applicant does in
 * this system, which is why this app reads the flag as well as the other two.
 * Off means the toggle does not render and nothing carries `data-reading`; see
 * docs/feature-flags.md.
 */
export const READING_MODE = "enable_reading_mode_feature";

export function isOn(flag: string): boolean {
  // Explicitly compared to "true" rather than treated as truthy. An unset
  // variable is undefined, but a variable set to "false" is a non-empty string
  // and would otherwise turn the feature on while appearing to turn it off.
  const override = process.env[flag.toUpperCase()];
  if (override !== undefined) {
    return override === "true";
  }

  const fallback = (defaults as Record<string, boolean | undefined>)[flag];

  // An unknown flag is off. A typo in a flag name should hide a feature rather
  // than expose one, since the first is noticed and the second is not.
  return fallback ?? false;
}
