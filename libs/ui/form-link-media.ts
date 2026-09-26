export type FormLinkMedia = { image: string | null; video: string | null };

const recapAsset = "v1790236201/reference-02b08846cc82817c0074d6612e6b3e70cf4ce12a2afa7016b603c8e68dbfa4ac_xdbiqx";

export const MORGAN_HACKS_RECAP = {
  url: "https://www.instagram.com/p/DXE7YimkZVk/",
  video: `https://res.cloudinary.com/kardusers/video/upload/c_limit,w_480,vc_h264,q_auto/${recapAsset}.mp4`,
  image: `https://res.cloudinary.com/kardusers/video/upload/so_0,c_limit,w_480/${recapAsset}.jpg`,
};

const cacheKey = "arctic:form-link-media:v1";
type CachedMedia = { url: string; media: FormLinkMedia; expires: number };

function cachedEntries(): CachedMedia[] {
  try {
    const entries: unknown = JSON.parse(sessionStorage.getItem(cacheKey) ?? "[]");
    return Array.isArray(entries) ? entries.filter((entry): entry is CachedMedia =>
      !!entry && publicMediaUrl(entry.url) && typeof entry.expires === "number" && entry.expires > Date.now()
      && !!entry.media && (entry.media.image === null || publicMediaUrl(entry.media.image))
      && (entry.media.video === null || publicMediaUrl(entry.media.video))).slice(-32) : [];
  } catch { return []; }
}

export function cachedFormLinkMedia(url: string): FormLinkMedia | null {
  return cachedEntries().find(entry => entry.url === url)?.media ?? null;
}

export function forgetFormLinkMedia(url: string): void {
  try { sessionStorage.setItem(cacheKey, JSON.stringify(cachedEntries().filter(entry => entry.url !== url))); } catch {}
}

export function publicMediaUrl(value: unknown): value is string {
  if (typeof value !== "string" || value.length > 2048 || /[\s\\]/.test(value)) return false;
  try {
    const url = new URL(value);
    return ["https:", "http:"].includes(url.protocol) && !url.username && !url.password && !url.port
      && url.hostname.includes(".") && !/^(?:\d+\.){3}\d+$/.test(url.hostname)
      && !/(?:^|\.)(?:localhost|local)$/.test(url.hostname);
  } catch { return false; }
}

export function videoLinkMedia(value: string): FormLinkMedia | null {
  if (!publicMediaUrl(value)) return null;
  const url = new URL(value);
  if ((["instagram.com", "www.instagram.com"].includes(url.hostname)
    && /^\/(?:p|reel)\/DXE7YimkZVk\/?$/.test(url.pathname))
    || (url.hostname === "res.cloudinary.com" && url.pathname === `/kardusers/video/upload/${recapAsset}.mp4`)) {
    return { image: MORGAN_HACKS_RECAP.image, video: MORGAN_HACKS_RECAP.video };
  }
  if (/\.(mp4|webm|ogv|mov)$/i.test(url.pathname)) return { image: null, video: url.href };
  if (["youtube.com", "www.youtube.com", "m.youtube.com", "music.youtube.com", "youtube-nocookie.com", "www.youtube-nocookie.com", "youtu.be"].includes(url.hostname)) {
    const id = url.hostname === "youtu.be" ? url.pathname.slice(1)
      : url.pathname === "/watch" ? url.searchParams.get("v") : url.pathname.match(/^\/(?:embed|shorts|live)\/([^/]+)\/?$/)?.[1];
    if (id && /^[\w-]{11}$/.test(id)) return { image: `https://i.ytimg.com/vi/${id}/hqdefault.jpg`, video: null };
  }
  return null;
}

export async function loadFormLinkMedia(url: string, signal: AbortSignal): Promise<FormLinkMedia | null> {
  if (!publicMediaUrl(url)) return null;
  const cached = videoLinkMedia(url) ?? cachedFormLinkMedia(url);
  if (cached) return cached;
  const response = await fetch(`/api/forms/link-preview?url=${encodeURIComponent(url)}`, { signal, credentials: "omit" });
  if (!response.ok) return null;
  const media: unknown = await response.json();
  if (!media || typeof media !== "object") return null;
  const value = media as Partial<FormLinkMedia>;
  const result = { image: publicMediaUrl(value.image) ? value.image : null, video: publicMediaUrl(value.video) ? value.video : null };
  if (!signal.aborted && (result.image || result.video)) {
    try {
      const entries = cachedEntries().filter(entry => entry.url !== url).slice(-31);
      entries.push({ url, media: result, expires: Date.now() + 30 * 60 * 1000 });
      sessionStorage.setItem(cacheKey, JSON.stringify(entries));
    } catch {}
  }
  return result;
}
