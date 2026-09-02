/**
 * The night scene behind the page.
 *
 * All inline SVG — no image assets to ship, and everything recolours with the
 * theme because strokes and fills reference `currentColor` or the tokens.
 * Purely decorative, so all of it is hidden from assistive tech.
 */

/* -------------------------------------------------------------------------
   Aurora — soft ribbons of light along the horizon
   ------------------------------------------------------------------------- */

/** Each ribbon is a curve closed to the bottom edge, stacked back to front. */
const RIBBONS = [
  { d: "M0 232C214 186 366 258 596 216S1010 140 1236 206 1398 240 1440 222V320H0Z", o: 0.3 },
  { d: "M0 258C238 212 404 288 668 246S1064 190 1440 252V320H0Z", o: 0.4 },
  { d: "M0 282C286 250 542 308 852 278S1232 244 1440 286V320H0Z", o: 0.52 },
  { d: "M0 302C320 280 600 320 900 300S1264 278 1440 306V320H0Z", o: 0.66 },
];

/** Thin crests that catch the light on top of the ribbons. */
const CRESTS = [
  "M0 232C214 186 366 258 596 216S1010 140 1236 206 1398 240 1440 222",
  "M0 258C238 212 404 288 668 246S1064 190 1440 252",
];

export function Aurora() {
  return (
    <svg
      className="scene__aurora"
      viewBox="0 0 1440 320"
      preserveAspectRatio="xMidYMax slice"
      aria-hidden="true"
      focusable="false"
    >
      <defs>
        <linearGradient id="ribbon" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor="var(--glow)" stopOpacity="0" />
          <stop offset="55%" stopColor="var(--glow)" stopOpacity="0.75" />
          <stop offset="100%" stopColor="var(--glow)" stopOpacity="1" />
        </linearGradient>
        <linearGradient id="crest" x1="0" y1="0" x2="1" y2="0">
          <stop offset="0%" stopColor="var(--glow)" stopOpacity="0" />
          <stop offset="35%" stopColor="var(--glow)" stopOpacity="0.9" />
          <stop offset="72%" stopColor="var(--glow)" stopOpacity="0.5" />
          <stop offset="100%" stopColor="var(--glow)" stopOpacity="0" />
        </linearGradient>
      </defs>

      {RIBBONS.map((r) => (
        <path key={r.d} d={r.d} fill="url(#ribbon)" opacity={r.o} />
      ))}
      {CRESTS.map((d) => (
        <path
          key={d}
          d={d}
          fill="none"
          stroke="url(#crest)"
          strokeWidth="2"
          opacity="0.95"
        />
      ))}
    </svg>
  );
}

/* -------------------------------------------------------------------------
   Contours — nested lines across the field, like a topographic map
   ------------------------------------------------------------------------- */

/**
 * Nine curves at descending heights. The offsets are derived from the index
 * rather than hand-tuned so the lines drift apart and bunch together instead
 * of running perfectly parallel.
 */
const CONTOURS = Array.from({ length: 9 }, (_, i) => {
  const y = 74 + i * 68;
  const a = y - 78 + (i % 3) * 20;
  const b = y + 58 - (i % 2) * 24;
  const c = y - 18 + (i % 4) * 15;
  const d = y - 96 + (i % 3) * 26;
  const e = y - 28 + (i % 2) * 19;
  return `M0 ${y}C240 ${a} 480 ${b} 720 ${c}S1160 ${d} 1440 ${e}`;
});

/** A four-point sparkle with concave sides. Used only by the wordmark. */
export const STAR =
  "M12 0c1.2 6.6 4.2 9.6 12 12-7.8 2.4-10.8 5.4-12 12-1.2-6.6-4.2-9.6-12-12 7.8-2.4 10.8-5.4 12-12z";

export function Contours() {
  return (
    <svg
      className="scene__contours"
      viewBox="0 0 1440 700"
      preserveAspectRatio="xMidYMid slice"
      aria-hidden="true"
      focusable="false"
    >
      {CONTOURS.map((d, i) => (
        <path
          key={d}
          d={d}
          fill="none"
          stroke="var(--glow)"
          strokeWidth="1.25"
          // Nearer lines read slightly stronger, so the field has depth.
          opacity={0.17 + i * 0.032}
        />
      ))}
    </svg>
  );
}

/* -------------------------------------------------------------------------
   Constellation — the M of MorganHacks, drawn in the sky
   ------------------------------------------------------------------------- */

/** Vertices of a capital M, in the constellation's own 120×90 box. */
const M_POINTS = [
  [10, 78],
  [26, 14],
  [56, 52],
  [86, 14],
  [104, 78],
] as const;

export function Constellation() {
  const line = M_POINTS.map(([x, y]) => `${x},${y}`).join(" ");

  return (
    <svg
      className="scene__constellation"
      viewBox="0 0 120 90"
      aria-hidden="true"
      focusable="false"
    >
      <polyline points={line} fill="none" stroke="var(--glow)" strokeWidth="1" />
      {M_POINTS.map(([x, y]) => (
        <circle key={`${x}-${y}`} cx={x} cy={y} r="2.6" fill="var(--glow)" />
      ))}
    </svg>
  );
}
