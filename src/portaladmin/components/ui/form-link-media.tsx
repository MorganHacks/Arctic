"use client";

import { useEffect, useLayoutEffect, useRef, useState } from "react";
import { cachedFormLinkMedia, forgetFormLinkMedia, loadFormLinkMedia, publicMediaUrl, videoLinkMedia, type FormLinkMedia as Media } from "../../../../libs/ui/form-link-media";
import styles from "../../../../libs/ui/form-link-card.module.css";

export function FormLinkMedia({ image, url }: { image: string | null; url: string }) {
  const [resolved, setResolved] = useState<{ url: string; media: Media | null } | null>(null);
  const [failed, setFailed] = useState<{ key: string; video?: boolean; image?: boolean } | null>(null);
  const key = image ?? url;
  const media = image ? { image, video: null } : videoLinkMedia(url) ?? (resolved?.url === url ? resolved.media : null);
  const videoSource = failed?.key === key && failed.video ? null : media?.video;
  const imageSource = failed?.key === key && failed.image ? null : media?.image;

  useLayoutEffect(() => {
    if (image || videoLinkMedia(url) || !publicMediaUrl(url)) return;
    const cached = cachedFormLinkMedia(url);
    if (cached) {
      setResolved({ url, media: cached });
      return;
    }
    const controller = new AbortController();
    const timer = window.setTimeout(() => {
      loadFormLinkMedia(url, controller.signal).then(media => {
        if (!controller.signal.aborted) setResolved({ url, media });
      }).catch(() => { if (!controller.signal.aborted) setResolved({ url, media: null }); });
    }, 300);
    return () => { window.clearTimeout(timer); controller.abort(); };
  }, [image, url]);

  function fail(kind: "video" | "image") {
    forgetFormLinkMedia(url);
    setFailed(current => ({ ...(current?.key === key ? current : {}), key, [kind]: true }));
  }

  if (videoSource) return <VideoPreview key={videoSource} src={videoSource} poster={imageSource ?? undefined} onError={() => fail("video")} />;
  return imageSource ? <img className={styles.image} src={imageSource} alt="" width={92} height={46} referrerPolicy="no-referrer" onError={() => fail("image")} /> : null;
}

function VideoPreview({ src, poster, onError }: { src: string; poster?: string; onError: () => void }) {
  const video = useRef<HTMLVideoElement>(null);
  const [paused, setPaused] = useState(true);

  useEffect(() => {
    const preview = video.current;
    if (!preview) return;
    const motion = window.matchMedia("(prefers-reduced-motion: reduce)");
    if (!motion.matches) void preview.play().catch(() => {});
    const respectMotion = () => { if (motion.matches) preview.pause(); };
    motion.addEventListener("change", respectMotion);
    return () => {
      motion.removeEventListener("change", respectMotion);
      preview.pause();
    };
  }, []);

  return <button type="button" className={`${styles.image} ${styles.mark}`} data-paused={paused}
    aria-label={paused ? "Play video preview" : "Pause video preview"}
    onClick={() => {
      const preview = video.current;
      if (!preview) return;
      if (preview.paused) void preview.play().catch(() => {});
      else preview.pause();
    }}>
    <video ref={video} src={src} poster={poster} muted loop playsInline preload="metadata" aria-hidden="true"
      width={92} height={46} onError={onError} onPlay={() => setPaused(false)} onPause={() => setPaused(true)}
      onLoadedMetadata={event => {
        const preview = event.currentTarget;
        if (preview.paused && preview.currentTime === 0 && preview.duration > .1) preview.currentTime = .1;
      }} />
    <span className={styles.playback} aria-hidden="true">
      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinejoin="round" focusable="false">
        {paused ? <path d="M18.8906 12.846C18.5371 14.189 16.8667 15.138 13.5257 17.0361C10.296 18.8709 8.6812 19.7884 7.37983 19.4196C6.8418 19.2671 6.35159 18.9776 5.95624 18.5787C5 17.6139 5 15.7426 5 12C5 8.2574 5 6.3861 5.95624 5.42132C6.35159 5.02245 6.8418 4.73288 7.37983 4.58042C8.6812 4.21165 10.296 5.12907 13.5257 6.96393C16.8667 8.86197 18.5371 9.811 18.8906 11.154C19.0365 11.7084 19.0365 12.2916 18.8906 12.846Z" /> : <>
          <path d="M4 7C4 5.58579 4 4.87868 4.43934 4.43934C4.87868 4 5.58579 4 7 4C8.41421 4 9.12132 4 9.56066 4.43934C10 4.87868 10 5.58579 10 7V17C10 18.4142 10 19.1213 9.56066 19.5607C9.12132 20 8.41421 20 7 20C5.58579 20 4.87868 20 4.43934 19.5607C4 19.1213 4 18.4142 4 17V7Z" />
          <path d="M14 7C14 5.58579 14 4.87868 14.4393 4.43934C14.8787 4 15.5858 4 17 4C18.4142 4 19.1213 4 19.5607 4.43934C20 4.87868 20 5.58579 20 7V17C20 18.4142 20 19.1213 19.5607 19.5607C19.1213 20 18.4142 20 17 20C15.5858 20 14.8787 20 14.4393 19.5607C14 19.1213 14 18.4142 14 17V7Z" />
        </>}
      </svg>
    </span>
  </button>;
}
