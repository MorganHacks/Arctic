import { redirect } from "next/navigation";
import { currentPerson } from "@/lib/api";
import { CHECK_IN_DESK, isOn } from "@/lib/features";
import { Shell } from "../shell";
import { ScanForm } from "./form";

/**
 * Check-in desk, for logistics and volunteers on event day.
 *
 * Manual entry first: the 12-char code from the hacker's
 * /portal/check-in screen, typed or pasted here and redeemed through
 * POST /admin/check-in/scan. The fallback for no code is the
 * Applicants list, linked under the form — not a second flow on
 * this screen.
 *
 * Refusals keep the API's rule: only confirmed may become checked
 * in. Anything else names an organizer rather than offering a
 * confirm-at-door button, which would need a new API path.
 */
export default async function CheckIn() {
  const person = await currentPerson();
  if (!person) {
    redirect("/sign-in");
  }

  // Behind the flag until logistics signs off the flow. The nav hides the
  // tab while this is off, so a direct visit says so rather than 404ing.
  if (!isOn(CHECK_IN_DESK)) {
    return (
      <Shell personId={person.personId}>
        <h1>Check-in</h1>
        <div className="empty">Check-in is not enabled yet.</div>
      </Shell>
    );
  }

  if (!person.permissions.has("checkin.scan")) {
    return (
      <Shell personId={person.personId}>
        <h1>Check-in</h1>
        <div className="empty">
          You do not have <code>checkin.scan</code>. Ask an admin.
        </div>
      </Shell>
    );
  }

  return (
    <Shell personId={person.personId}>
      <h1>Check-in</h1>
      <p>Type the code from the hacker&apos;s portal. Check the name on the answer against the person in front of you.</p>
      <ScanForm />
    </Shell>
  );
}
