import { redirect } from "next/navigation";
import { currentResume } from "@/lib/api";
import { ResumeUpload } from "./upload";

/**
 * The resume we hold, and the way to replace it.
 *
 * Its own screen rather than a field on the profile form, and the split is not
 * cosmetic. The profile is six text boxes saved together by one button; this
 * is a file that uploads the moment it is chosen, and it is governed by a
 * different rule — the profile locks when a decision lands and this does not.
 * Putting them on one page would mean one screen with two save models and two
 * lock states, which is how somebody comes to believe their name was saved
 * because their resume was.
 *
 * There is deliberately no link to download the file. An applicant is the
 * person who uploaded it and has it already; the organizers' screens mint a
 * signed link because a reviewer does not. Every link this platform issues is
 * five more minutes in which a resume can be pasted somewhere, and the
 * cheapest one to not issue is the one nobody needed.
 */
export default async function ResumePage() {
  const screen = await currentResume();
  if (!screen) {
    redirect("/portal/sign-in");
  }

  return (
    <>
      {/* COPY: needs sign-off. */}
      <h1>Your resume</h1>

      {/* COPY: needs sign-off. */}
      <p className="lede">
        Sponsors read these at the event. If yours has changed since you
        applied, replace it here — we will use whatever is on this page.
      </p>

      {!screen.started ? (
        <div className="empty">
          {/* COPY: needs sign-off. */}
          There is nothing to attach a resume to until you have started an
          application.
        </div>
      ) : (
        <>
          {/*
            Said, not just disabled. A greyed-out control with no explanation
            is what generates the email this portal exists to prevent, so the
            reason comes from the API — which is the side that knows it — and
            sits above the picker rather than hiding in a tooltip.
          */}
          {screen.editable || screen.lockedReason === null ? null : (
            <div className="notice">
              <p>{screen.lockedReason}</p>
            </div>
          )}

          <ResumeUpload
            resume={screen.resume}
            editable={screen.editable}
            maxBytes={screen.maxBytes}
            accepts={screen.accepts}
          />
        </>
      )}
    </>
  );
}
