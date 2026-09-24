import { CampaignsTable } from "@/components/mail/campaigns-table";
import { NewCampaign } from "@/components/mail/new-campaign";
import { NoCampaigns } from "@/components/mail/no-campaigns";
import { readPageData } from "@/lib/page-data";
import { Shell } from "../shell";
import { newCampaign } from "./actions";
import { readBroadcastTemplates, readCampaigns, readForms } from "./api";

/**
 * Everything the registration team has mailed, or is about to.
 *
 * The list answers where each campaign got to and who it reached. Creating one
 * happens here because it is three fields; sending one does not, because it is
 * the thing that cannot be taken back and it belongs on a page of its own,
 * behind a preview.
 */
export default async function Mail() {
  // None of the three needs another's answer. The forms are for the segment
  // picker and the templates for the dropdown beside it, and awaiting them in
  // turn would make the screen three times as slow to arrive for nothing.
  const { person, data: [campaigns, choices, templates] } = await readPageData(() => Promise.all([
    readCampaigns(),
    readForms(),
    readBroadcastTemplates(),
  ]));
  const { forms, events } = choices;

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
  //
  // Two permissions, because atlas splits the work into two acts and this
  // screen used to check the wrong one for the first of them. Drafting a
  // campaign -- picking a template and an audience -- is authoring, and
  // POST /admin/campaigns is gated on email.manage_templates. Sending it is
  // the act that cannot be taken back, and POST .../send is gated on
  // email.send_broadcast.
  //
  // Checking send_broadcast for both meant somebody who could draft was shown
  // no way to, and somebody who could send but not author was shown a form
  // whose Create button the API would refuse. Nobody hit it because comms
  // holds both -- which is exactly how a mismatch like this survives until an
  // event weekend when somebody is given one of them.
  const canCompose = person.permissions.has("email.manage_templates");
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

      {canCompose ? (
        <NewCampaign
          forms={forms}
          events={events}
          templates={templates.templates}
          templatesError={templates.error}
          create={newCampaign}
        />
      ) : null}

      {campaigns.items.length === 0 ? (
        <NoCampaigns />
      ) : (
        <CampaignsTable campaigns={campaigns.items} />
      )}
    </Shell>
  );
}
