"use client";

import { useEffect, useRef, useState } from "react";
import { ArrowUpRight01Icon, PauseIcon, PlayIcon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import { MORGAN_HACKS_RECAP } from "../../../libs/ui/form-link-media";
import cardStyles from "../../../libs/ui/form-link-card.module.css";

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
    <footer className={cardStyles.dock} aria-label="Morgan Hacks community recap">
      <div className={cardStyles.card}>
        <button type="button" className={`${cardStyles.image} ${cardStyles.mark}`} data-paused={paused}
          aria-label={paused ? "Play recap preview" : "Pause recap preview"}
          onClick={() => {
            const preview = video.current;
            if (!preview) return;
            if (preview.paused) void preview.play().catch(() => {});
            else preview.pause();
          }}>
          <video ref={video} muted loop playsInline preload="metadata" aria-hidden="true"
            src={MORGAN_HACKS_RECAP.video}
            onPlay={() => setPaused(false)} onPause={() => setPaused(true)} />
          <span className={cardStyles.playback}><Icon icon={paused ? PlayIcon : PauseIcon} size={14} /></span>
        </button>
        <div className={cardStyles.copy}>
          <span className={cardStyles.label}>Morgan Hacks 2026</span>
          <span className={cardStyles.titleRow}>
            <span className={cardStyles.title}>Watch the recap</span>
            <a href={MORGAN_HACKS_RECAP.url} target="_blank" rel="noopener noreferrer"
              className={cardStyles.arrow} aria-label="Watch the recap on Instagram (opens in a new tab)">
              <Icon icon={ArrowUpRight01Icon} size={11} strokeWidth={2} />
            </a>
          </span>
        </div>
      </div>
    </footer>
  );
}
