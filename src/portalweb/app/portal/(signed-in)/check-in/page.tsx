import { redirect } from "next/navigation";
import { checkInPass, type QrSymbol } from "@/lib/api";

/**
 * How much light space surrounds the symbol, in modules.
 *
 * Four is what the standard asks for and it is not decoration: a reader finds
 * the symbol by the contrast at its edge, and a QR pressed against the edge of
 * its container is one a phone hunts for. The plate behind it is drawn this
 * much larger rather than the page being trusted to be light, because in dark
 * mode it is not.
 */
const QUIET_ZONE = 4;

/**
 * The symbol, as one filled path.
 *
 * One path rather than a rectangle per module, and the reason is not node
 * count. Separate rectangles are antialiased separately, so at some zoom
 * levels a hairline of background shows between two dark modules that should
 * be touching, and that hairline is exactly the kind of edge a decoder reads
 * as a module boundary. Subpaths of a single fill have no seam between them.
 *
 * Hidden from assistive technology on purpose. It carries nothing the code
 * printed underneath it does not, and a screen reader announcing a picture of
 * the thing it just read out is noise.
 */
function Symbol({ qr }: { qr: QrSymbol }) {
  const span = qr.size + QUIET_ZONE * 2;

  const path = qr.rows
    .flatMap((row, y) =>
      [...row].map((module, x) =>
        module === "1"
          ? `M${x + QUIET_ZONE} ${y + QUIET_ZONE}h1v1h-1z`
          : "",
      ),
    )
    .join("");

  return (
    <svg
      className="pass__qr"
      viewBox={`0 0 ${span} ${span}`}
      shapeRendering="crispEdges"
      aria-hidden="true"
    >
      <path d={path} fill="var(--scan-ink)" />
    </svg>
  );
}

/**
 * The screen somebody holds up at the door.
 *
 * Built for one moment: a phone at arm's length, in whatever light the queue
 * is standing in, held by somebody who has been awake since five. So the code
 * is the largest thing on the page, the square above it is as big as the
 * screen allows, and there is nothing else competing for the eye.
 *
 * The words all come from the API. That is the same rule the status screen
 * keeps, and it matters more here: this page has a state where showing a code
 * would be a lie, and the sentence explaining that state is one the team signs
 * off rather than one this file invents.
 */
export default async function CheckIn() {
  const pass = await checkInPass();
  if (!pass) {
    redirect("/portal/sign-in");
  }

  // Pulled out so the check below narrows all three at once. They are set
  // together on the API side and are absent together, and reading them off
  // `pass` would leave this file asserting that at every use.
  const { display, qr } = pass;

  return (
    <>
      <h1>{pass.heading}</h1>

      {display !== null && qr !== null ? (
        <>
          <p className="lede">{pass.explanation}</p>

          <div className={pass.checkedIn ? "pass pass--used" : "pass"}>
            <div className="pass__plate">
              <Symbol qr={qr} />
            </div>

            {/*
              The fallback, and not a small one. A camera that will not focus,
              a cracked screen or a scanner nobody has written yet all end at
              somebody reading these twelve characters out loud, which is why
              they are set in three groups and why the alphabet has no letters
              that sound like digits.
            */}
            <p className="pass__code">{display}</p>
          </div>

          {pass.hint ? <p className="quiet pass__hint">{pass.hint}</p> : null}
        </>
      ) : (
        /*
          Degraded honestly. Showing a code to somebody the door would refuse
          sends them to the front of a queue to be turned away, and the whole
          point of this page is that they find out here instead.
        */
        <div className="empty">
          <p style={{ marginBottom: 0 }}>{pass.explanation}</p>
        </div>
      )}
    </>
  );
}
