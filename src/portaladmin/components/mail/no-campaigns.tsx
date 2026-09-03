/**
 * Nothing has been mailed yet.
 *
 * The one thing worth saying is what creating a campaign does, because the
 * panel above this is where it is created and the useful thing to know before
 * pressing the button is that it does not send anything.
 */
export function NoCampaigns() {
  return (
    <div className="empty">
      No campaigns yet. A new one is a draft, and nothing goes out until its
      recipients have been previewed.
    </div>
  );
}
