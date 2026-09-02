import { redirect } from "next/navigation";
import { messageHistory, readableDate } from "@/lib/api";

/**
 * Which of the four words the API uses needs colour.
 *
 * Only the ones that change what the person does next. "Delivered" means stop
 * worrying; "could not be delivered" means check the address or the spam
 * folder. "Sending" means wait, which is not an action and not a problem, so
 * it stays neutral — colouring it would spend the reader's attention on
 * nothing.
 */
function tone(delivery: string): string {
  if (delivery === "Delivered") {
    return "pill delivered";
  }

  if (delivery.startsWith("Could not") || delivery.startsWith("Marked")) {
    return "pill failed";
  }

  return "pill";
}

/**
 * Every email we have sent, so "I never got it" has an answer.
 *
 * Subjects and outcomes only. The message bodies stay in lark and never cross
 * this line — a decision letter is in those columns, and this page exists to
 * say whether a message arrived, not to hand it back.
 */
export default async function Messages() {
  const messages = await messageHistory();
  if (!messages) {
    redirect("/portal/sign-in");
  }

  return (
    <>
      <h1>Emails we have sent you</h1>
      <p className="lede">
        Everything we have sent to the address you signed in with, newest
        first.
      </p>

      {messages.length === 0 ? (
        <div className="empty">
          We have not sent you anything yet.
        </div>
      ) : (
        <div className="panel">
          <ul className="messages">
            {messages.map((message) => (
              <li key={message.id}>
                <p className="messages__subject">{message.subject}</p>
                <div className="messages__meta">
                  <span>{readableDate(message.at)}</span>
                  <span className={tone(message.delivery)}>
                    {message.delivery}
                  </span>
                </div>
              </li>
            ))}
          </ul>
        </div>
      )}

      <p className="quiet">
        Nothing here that you were expecting? Check your spam folder first —
        that is where most of these end up. If it says an email could not be
        delivered, your address may have changed, and we cannot fix that from
        our side.
      </p>
    </>
  );
}
