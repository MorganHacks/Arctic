import { siteConfig } from "@/site.config";
import { Countdown } from "./countdown";
import { Aurora, Constellation, Contours, STAR } from "./scene";

const { year, organizerFormUrl, deadline, contactEmail, socials, mlh } =
  siteConfig;

export default function Home() {
  return (
    <div className="screen">
      <Contours />
      <Constellation />
      <Aurora />

      {/* MLH require the badge hotlinked from their host and pinned to a top
          corner. Rendered as a plain <img> — it is an SVG, so next/image would
          add a remote-pattern config for nothing. */}
      <a
        className="mlh"
        href={mlh.href}
        target="_blank"
        rel="noopener noreferrer"
      >
        <img
          src={mlh.badge}
          alt={`Major League Hacking ${mlh.season} Hackathon Season`}
          width={80}
          height={139}
        />
      </a>

      <header className="topline">
        <span className="wordmark">
          <svg
            className="wordmark__star"
            viewBox="0 0 24 24"
            aria-hidden="true"
            focusable="false"
          >
            <path d={STAR} />
          </svg>
          <span className="wordmark__text">MorganHacks</span>
        </span>
      </header>

      <main className="copy">
        {/* Grouped so the three blocks can spread down the screen while each
            one stays internally tight. */}
        <div className="copy__intro">
          <p className="eyebrow">
            <span>MorganHacks {year}</span>
          </p>

          <h1 className="headline">
            Think you could <em>pull this off</em>?
          </h1>
        </div>

        <p className="dare">Only one way to find out.</p>

        <div className="copy__act">
          <div className="actions">
          <a
            className="cta"
            href={organizerFormUrl}
            target="_blank"
            rel="noopener noreferrer"
          >
            Apply now
            <span className="cta__arrow" aria-hidden="true">
              →
            </span>
            </a>
          </div>

          <Countdown
            iso={deadline.iso}
            date={deadline.date}
            time={deadline.time}
          />

          <p className="contact">
            Questions? <a href={`mailto:${contactEmail}`}>{contactEmail}</a>
          </p>
        </div>
      </main>

      <nav className="links">
        <a href={socials.instagram} target="_blank" rel="noopener noreferrer">
          Instagram
        </a>
        <a href={socials.linkedin} target="_blank" rel="noopener noreferrer">
          LinkedIn
        </a>
        <a href={socials.tiktok} target="_blank" rel="noopener noreferrer">
          TikTok
        </a>
      </nav>
    </div>
  );
}
