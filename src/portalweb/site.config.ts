/**
 * The one file next year's team edits.
 *
 * Every fact the page states lives here. Nothing in `app/` hardcodes a date,
 * an address or a URL. Figures come from the 2027 recruitment deck — if they
 * change there, change them here.
 */
export const siteConfig = {
  url: "https://morganhacks.com",

  /** The edition being recruited for. */
  year: "2027",

  /** The organizer application form. Verified live 2026-08-30. */
  organizerFormUrl: "https://forms.gle/SbJBfwhE3sPAARE66",

  /**
   * Applications close 2026-09-13 at 11:59 PM EST. Verified as a Sunday.
   * After that date this page needs new copy — it is not self-expiring.
   */
  deadline: {
    date: "September 13",
    weekday: "Sunday",
    /** Label as published on the recruitment flyer. */
    time: "11:59 PM EST",
    /**
     * The same instant, for the countdown. Note September 13 falls inside
     * daylight saving, so the real offset is EDT (UTC-4) even though the
     * flyer says EST. 23:59 EDT = 03:59Z the next day.
     */
    iso: "2026-09-14T03:59:00Z",
  },

  contactEmail: "info@morganhacks.com",

  /**
   * MLH trust badge. Hotlinked from MLH's own S3, same as the 2026 site did —
   * MLH expect the badge served from their host, not vendored.
   * Season tracks the event, so this is the 2027 badge, not 2026's.
   */
  mlh: {
    season: "2027",
    badge:
      "https://s3.amazonaws.com/logged-assets/trust-badge/2027/mlh-trust-badge-2027-white.svg",
    href: "https://mlh.io/na?utm_source=na-hackathon&utm_medium=TrustBadge&utm_campaign=2027-season&utm_content=white",
  },

  socials: {
    instagram: "https://www.instagram.com/morgan.hacks",
    linkedin: "https://www.linkedin.com/company/morganhacks",
    tiktok: "https://www.tiktok.com/@morganhacks2026",
  },
} as const;
