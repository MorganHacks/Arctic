import { redirect } from "next/navigation";
import { currentPortal } from "@/lib/api";
import { ProfileForm } from "./form";

/**
 * Name, school, shirt size, food and access.
 *
 * Not the application. Everything a reviewer reads is fixed at submit —
 * rewriting an answer somebody has already read is a different feature with a
 * different audit story. What is here is the logistics: what to put on a
 * badge, what shirt to order, what somebody can eat, and what they need to
 * take part.
 */
export default async function ProfilePage() {
  const portal = await currentPortal();
  if (!portal) {
    redirect("/portal/sign-in");
  }

  const { application } = portal;

  if (!application) {
    return (
      <>
        <h1>Your details</h1>
        <div className="empty">
          There is nothing to edit until you have started an application.
        </div>
      </>
    );
  }

  return (
    <>
      <h1>Your details</h1>
      <p className="lede">
        We use these to print your badge, order shirts, and plan food and
        access. Nothing here is read as part of your application.
      </p>

      {/*
        Said, not just disabled. A greyed-out form with no explanation is what
        generates the email this portal exists to prevent, so the reason comes
        from the API — which is the side that knows it — and sits above the
        fields rather than hiding in a tooltip.
      */}
      {application.profileEditable ? null : (
        <div className="notice">
          <p>{application.profileLockedReason}</p>
        </div>
      )}

      <ProfileForm
        profile={application.profile}
        shirtSizes={application.shirtSizes}
        editable={application.profileEditable}
      />
    </>
  );
}
