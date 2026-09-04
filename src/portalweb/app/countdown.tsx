"use client";

import { useEffect, useState } from "react";

/**
 * Days remaining until applications close.
 *
 * The count is computed after mount, never during render — a server-rendered
 * count would be stale the moment it was cached, and would mismatch on
 * hydration. Until it resolves, the block still shows the date, which is the
 * part that has to be right.
 */
export function Countdown({
  iso,
  date,
  time,
}: {
  iso: string;
  date: string;
  time: string;
}) {
  const [days, setDays] = useState<number | null>(null);

  useEffect(() => {
    const tick = () => {
      const ms = new Date(iso).getTime() - Date.now();
      // floor, not ceil: with 15.2 days to go you have 15 whole days left,
      // and the final day falls through to the "Last day" branch.
      setDays(Math.floor(ms / 86_400_000));
    };
    tick();
    // A minute is plenty; this only ever changes at midnight.
    const id = setInterval(tick, 60_000);
    return () => clearInterval(id);
  }, [iso]);

  const closed = days !== null && days < 0;

  return (
    <div className="clock">
      {closed ? (
        <p className="clock__closed">Applications are closed</p>
      ) : (
        <>
          <span className="clock__lead">
            {days === null ? null : days === 0 ? (
              <span className="clock__today">Last day</span>
            ) : (
              <>
                <span className="clock__count">{days}</span>
                <span className="clock__unit">
                  {days === 1 ? "day" : "days"}
                  <br />
                  left
                </span>
              </>
            )}
          </span>

          <span className="clock__when">
            <span className="clock__label">Applications close</span>
            <span className="clock__date">
              {date}, {time}
            </span>
          </span>
        </>
      )}
    </div>
  );
}
