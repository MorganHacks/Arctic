export const COUNTDOWN_WINDOW_MS = 24 * 60 * 60 * 1000;

export function deadlineSeconds(closesAt: string, now: number): number | null {
  const remaining = Date.parse(closesAt) - now;
  if (!Number.isFinite(remaining) || remaining > COUNTDOWN_WINDOW_MS) return null;
  return Math.max(0, Math.ceil(remaining / 1000));
}

export function countdownParts(seconds: number): [string, string, string] {
  return [
    Math.floor(seconds / 3600),
    Math.floor((seconds % 3600) / 60),
    seconds % 60,
  ].map(value => String(value).padStart(2, "0")) as [string, string, string];
}
