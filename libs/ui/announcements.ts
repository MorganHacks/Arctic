export type AnnouncementKind = "image" | "imagePoll" | "poll" | "quiz" | "video";
export type AnnouncementContent = {
  kind: AnnouncementKind;
  media?: { url: string; alt?: string | null }[] | null;
  options?: { text: string; imageUrl?: string | null }[] | null;
  correctOption?: number | null;
  explanation?: string | null;
};
export type AnnouncementResults = { total: number; counts: number[] | null; choice: number | null };
export const announcementLabels: Record<AnnouncementKind, string> = {
  image: "Image", imagePoll: "Image poll", poll: "Text poll", quiz: "Quiz", video: "Video",
};

export function mediaUrl(value: string): boolean {
  try {
    const url = new URL(value);
    return value.length <= 2048 && url.protocol === "https:" && !url.username && !url.password;
  } catch { return false; }
}

export function videoEmbed(value: string): string | null {
  if (!mediaUrl(value)) return null;
  const url = new URL(value);
  const host = url.hostname.toLowerCase();
  if (["youtube.com", "www.youtube.com", "m.youtube.com", "youtu.be"].includes(host)) {
    const id = host === "youtu.be" ? url.pathname.slice(1) : url.searchParams.get("v") ?? url.pathname.match(/^\/(?:embed|shorts)\/([^/]+)$/)?.[1];
    return id && /^[\w-]{11}$/.test(id) ? `https://www.youtube-nocookie.com/embed/${id}` : null;
  }
  if (["vimeo.com", "www.vimeo.com", "player.vimeo.com"].includes(host)) {
    const id = url.pathname.match(/^\/(?:video\/)?(\d+)$/)?.[1];
    return id ? `https://player.vimeo.com/video/${id}` : null;
  }
  return null;
}

export function emptyAnnouncementContent(kind: AnnouncementKind): AnnouncementContent {
  return kind === "image" || kind === "video" ? { kind, media: [{ url: "", alt: "" }] } : {
    kind, options: [{ text: "", ...(kind === "imagePoll" ? { imageUrl: "" } : {}) },
      { text: "", ...(kind === "imagePoll" ? { imageUrl: "" } : {}) }],
    ...(kind === "quiz" ? { correctOption: null, explanation: "" } : {}),
  };
}

export function announcementError(content: AnnouncementContent | null): string | null {
  if (!content) return null;
  if (content.kind === "image" || content.kind === "video") {
    if (!content.media?.length || content.media.some(item => !mediaUrl(item.url.trim())))
      return content.kind === "image" ? "Add a public HTTPS image link." : "Add a YouTube, Vimeo, or direct HTTPS video link.";
    return null;
  }
  const options = content.options ?? [];
  if (options.length < 2 || options.some(option => !option.text.trim())) return "Add at least two choices.";
  if (new Set(options.map(option => option.text.trim().toLowerCase())).size !== options.length) return "Give each choice a different label.";
  if (content.kind === "imagePoll" && options.some(option => !mediaUrl(option.imageUrl?.trim() ?? ""))) return "Add a public HTTPS image link for every choice.";
  if (content.kind === "quiz" && content.correctOption == null) return "Mark the correct answer for your quiz.";
  return null;
}

export function cleanAnnouncementContent(content: AnnouncementContent | null): AnnouncementContent | null {
  if (!content) return null;
  return {
    ...content,
    media: content.media?.map(item => ({ url: item.url.trim(), alt: item.alt?.trim() || null })),
    options: content.options?.map(option => ({ text: option.text.trim(), imageUrl: option.imageUrl?.trim() || null })),
    explanation: content.explanation?.trim() || null,
  };
}
