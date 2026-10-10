"use server";

import { apiFetch } from "@/lib/api";

/**
 * The door's half of check-in, in the console.
 *
 * One screen hitting the one route atlas already serves —
 * POST /admin/check-in/scan, gated on checkin.scan. No camera yet:
 * the code is typed (or pasted) by hand, which is also the fallback
 * a cracked screen or bad light ends at anyway.
 *
 * The API's sentences are shown verbatim. They were written for
 * somebody standing in a doorway with a queue behind them, so this
 * file does not reword them and does not invent its own.
 */

export type ScanOutcome =
  | "checkedIn"
  | "alreadyCheckedIn"
  | "notConfirmed"
  | "unknownCode";

export type ScanState = {
  error?: string;
  outcome?: ScanOutcome;
  /** The API's sentence for the 200s. */
  message?: string;
  /** Who the code belongs to — read it while looking at who handed over the phone. */
  name?: string | null;
  checkedInAt?: string | null;
  /** Echoed so the screen can say which code this answer is about. */
  code?: string;
};

function text(form: FormData, field: string): string {
  const value = form.get(field);
  return typeof value === "string" ? value.trim() : "";
}

export async function scanCheckIn(
  _previous: ScanState,
  form: FormData,
): Promise<ScanState> {
  const code = text(form, "code");

  if (code === "") {
    return { error: "Scan or type a code." };
  }

  let response: Response;
  try {
    response = await apiFetch("/admin/check-in/scan", {
      method: "POST",
      body: JSON.stringify({ code }),
      headers: { "content-type": "application/json" },
    });
  } catch {
    return { error: "The API could not be reached. Try again.", code };
  }

  if (response.status === 401) {
    return { error: "Your session has ended. Sign in again.", code };
  }

  if (response.status === 403) {
    return { error: "You do not have checkin.scan. Ask an admin.", code };
  }

  // 200 (checkedIn / alreadyCheckedIn), 409 (notConfirmed),
  // 404 (unknownCode) and 400 all carry their own sentence.
  try {
    const body = (await response.json()) as {
      outcome?: ScanOutcome;
      message?: string;
      error?: string;
      name?: string | null;
      checkedInAt?: string | null;
    };

    if (!response.ok) {
      return {
        outcome: body.outcome,
        error: body.error ?? "That did not work.",
        name: body.name ?? null,
        code,
      };
    }

    return {
      outcome: body.outcome,
      message: body.message,
      name: body.name ?? null,
      checkedInAt: body.checkedInAt ?? null,
      code,
    };
  } catch {
    return { error: "That did not work.", code };
  }
}
