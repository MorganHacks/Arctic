import { AnnouncementAttachment } from "@/components/announcement-content";
import { AnnouncementReactions } from "@/components/announcement-reactions";
import { redirect } from "next/navigation";
import { announcements } from "@/lib/api";
import { readableTime } from "../../../../../../libs/ui/zone";

/**
 * What the team has told everybody, newest first.
 *
 * A screen of its own rather than another section on /portal/messages, and the
 * two are close enough that the reason is worth writing down.
 *
 * Messages answers exactly one question — "you say you emailed me, did it
 * arrive" — about mail addressed to one person, and it deliberately withholds
 * the body of every line on it. Announcements are the opposite on all three
 * counts: identical for everybody, with no delivery state to report, and the
 * text is the entire point. Putting them on one page would produce a list
 * where some rows can be read and some are deliberately blanked, which is
 * exactly the screen that makes somebody ask why they are allowed to see one
 * and not the other.
 *
 * They are also read on completely different rhythms. This is a page somebody
 * pulls down on twice an hour across a weekend; that one is a page somebody
 * opens once, when something did not turn up. A screen you refresh and a
 * screen you visit once should not be the same screen.
 *
 * Every sentence this file writes is chrome — a heading, a line of context and
 * an empty state. The notices themselves are rendered exactly as an organizer
 * typed them: nothing here truncates, reflows or summarises, because a
 * schedule change cut off mid-sentence is worse than no notice at all.
 */
export default async function Announcements() {
  const posted = await announcements();
  if (!posted) {
    redirect("/portal/sign-in");
  }

  return (
    <>
      {/* COPY — needs sign-off. Every sentence on this page is invented. */}
      <h1>Announcements</h1>
      <p className="lede">
        Updates from the MorganHacks team, newest first. This page does not
        refresh by itself — reload it to see anything new.
      </p>

      {posted.length === 0 ? (
        <div className="empty">Nothing has been posted yet.</div>
      ) : (
        <div className="panel">
          <ul className="notices">
            {posted.map((announcement) => (
              <li key={announcement.id}>
                <p className="notices__body">{announcement.body}</p>
                <AnnouncementAttachment id={announcement.id} content={announcement.content} results={announcement.results} />
                <p className="notices__at">{readableTime(announcement.at)}</p>
                <AnnouncementReactions id={announcement.id} initial={announcement.reactions} />
              </li>
            ))}
          </ul>
        </div>
      )}
    </>
  );
}
