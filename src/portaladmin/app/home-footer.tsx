"use client";

import { useEffect, useRef, useState } from "react";
import { ArrowUpRight01Icon, PauseIcon, PlayIcon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import styles from "./home-footer.module.css";

export function HomeFooter() {
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

  return (
    <footer className={styles.footer} aria-label="Morgan Hacks community recap">
      <div className={styles.badge}>
        <button type="button" className={styles.mark} data-paused={paused}
          aria-label={paused ? "Play recap preview" : "Pause recap preview"}
          onClick={() => {
            const preview = video.current;
            if (!preview) return;
            if (preview.paused) void preview.play().catch(() => {});
            else preview.pause();
          }}>
          <video ref={video} muted loop playsInline preload="metadata" aria-hidden="true"
            src="https://res.cloudinary.com/kardusers/video/upload/v1790236201/reference-02b08846cc82817c0074d6612e6b3e70cf4ce12a2afa7016b603c8e68dbfa4ac_xdbiqx.mp4"
            onPlay={() => setPaused(false)} onPause={() => setPaused(true)} />
          <span className={styles.playback}><Icon icon={paused ? PlayIcon : PauseIcon} size={14} /></span>
        </button>
        <div className={styles.credit}>
          <span className={styles.label}>Morgan Hacks 2026</span>
          <span className={styles.name}>
            Watch the recap
            <a href="https://www.instagram.com/p/DXE7YimkZVk/" target="_blank" rel="noopener noreferrer"
              className={styles.arrow} aria-label="Watch the recap on Instagram (opens in a new tab)">
              <Icon icon={ArrowUpRight01Icon} size={11} strokeWidth={2} />
            </a>
          </span>
        </div>
      </div>
    </footer>
  );
}
