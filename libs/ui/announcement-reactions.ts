export const announcementReactions = [
  { key: "love", emoji: "🥰", label: "Love" },
  { key: "wow", emoji: "🤩", label: "Wow" },
  { key: "confused", emoji: "😕", label: "Confused" },
  { key: "support", emoji: "🥺", label: "Support" },
  { key: "happy", emoji: "😄", label: "Happy" },
] as const;

export type AnnouncementReaction = typeof announcementReactions[number]["key"];
export type AnnouncementReactions = {
  counts: Record<AnnouncementReaction, number>;
  choice: AnnouncementReaction | null;
  total: number;
};

export function emptyReactions(): AnnouncementReactions {
  return { counts: { love: 0, wow: 0, confused: 0, support: 0, happy: 0 }, choice: null, total: 0 };
}
