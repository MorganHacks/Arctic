import { redirect } from "next/navigation";
import { CampaignsTable } from "@/components/mail/campaigns-table";
import { NewCampaign } from "@/components/mail/new-campaign";
import { NoCampaigns } from "@/components/mail/no-campaigns";
import { currentPerson } from "@/lib/api";
import { Shell } from "../shell";
import { newCampaign } from "./actions";
import { readCampaigns, readForms } from "./api";

/**
 * Everything the registration team has mailed, or is about to.
 *
 * The list answers where each campaign got to and who it reached. Creating one
 * happens here because it is three fields; sending one does not, because it is
 * the thing that cannot be taken back and it belongs on a page of its own,
 * behind a preview.
 */
export default async function Mail() {
  const person = await currentPerson();
  if (!person) {
    redirect("/sign-in");
  }

  // Neither needs the other's answer. The forms are only for the segment
  // picker, and awaiting them in turn would make the screen twice as slow to
  // arrive for nothing.
  const [campaigns, forms] = await Promise.all([readCampaigns(), readForms()]);

  if (!campaigns.ok) {
    return (
      <Shell personId={person.personId}>
        <h1>Mail</h1>
        <div className="empty">
          {campaigns.status === 403 ? (
            <>
              You do not have <code>email.view_stats</code>. Ask an admin.
            </>
          ) : (
            campaigns.error
          )}
        </div>
      </Shell>
    );
  }

  // Cosmetic. The API refuses the write whether or not this panel rendered, so
  // hiding it is a courtesy to somebody who cannot use it rather than a
  // control over anything.
  const canSend = person.permissions.has("email.send_broadcast");

  return (
    <Shell personId={person.personId}>
      <h1>Mail</h1>
      <p className="lede">
        Campaigns to applicants and organizers. A campaign is created as a
        draft, and is sent from its own page once its recipients have been
        previewed.
      </p>

      {/* Scaffolding, and says so. Goes with the fixtures in api.ts the moment
          the endpoints land. */}
      {campaigns.mocked ? (
        <p className="error">
          Showing example data. The campaigns API is not available yet.
        </p>
      ) : null}

      {canSend ? <NewCampaign forms={forms} create={newCampaign} /> : null}

      {campaigns.items.length === 0 ? (
        <NoCampaigns />
      ) : (
        <CampaignsTable campaigns={campaigns.items} />
      )}
    </Shell>
  );
}
