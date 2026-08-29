import { siteConfig } from "@/site.config";

export default function Home() {
  return (
    <main className="hero">
      <div className="hero__inner">
        {/* Text wordmark. Swap for next/image if a logo file lands. */}
        <span className="wordmark">MorganHacks</span>

        <div className="hero__block">
          <h1 className="hero__title">MorganHacks</h1>

          <p className="hero__subline">
            Morgan State&rsquo;s student hackathon. We&rsquo;re building the team
            that runs the next one.
          </p>

          <a
            className="cta"
            href={siteConfig.organizerFormUrl}
            target="_blank"
            rel="noopener noreferrer"
          >
            Join the organizing team
          </a>
        </div>
      </div>
    </main>
  );
}
