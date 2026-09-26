"use client";

import { ErrorToast } from "@/components/ui/error-toast";

import { EmojiPicker, type EmojiData, type SkinTone } from "frimousse";
import emojis from "emojibase-data/en/data.json";
import messages from "emojibase-data/en/messages.json";
import { Search01Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import styles from "./subject-field.module.css";

const skinTones = ["light", "medium-light", "medium", "medium-dark", "dark"] as const;
const data: EmojiData = {
  locale: "en",
  emojis: emojis
    .filter((emoji) => emoji.group !== undefined && emoji.group !== 2)
    .sort((a, b) => (a.order ?? 0) - (b.order ?? 0))
    .map((emoji) => ({
      emoji: emoji.emoji,
      label: emoji.label,
      category: emoji.group!,
      version: emoji.version,
      tags: emoji.tags ?? [],
      skins: emoji.skins ? Object.fromEntries(skinTones.map((tone, index) => [
        tone,
        emoji.skins?.find((skin) => skin.tone === index + 1)?.emoji ?? emoji.emoji,
      ])) as Record<Exclude<SkinTone, "none">, string> : undefined,
    })),
  categories: messages.groups
    .filter((group) => group.key !== "component")
    .map((group) => ({ index: group.order, label: group.message })),
  skinTones: Object.fromEntries(messages.skinTones.map((tone) => [tone.key, tone.message])) as EmojiData["skinTones"],
};

const resolveEmojiData = () => data;

export default function SubjectEmojiPicker({ onSelect, error }: {
  onSelect: (emoji: string) => void;
  error: string;
}) {
  return (
    <EmojiPicker.Root
      className={styles.picker}
      columns={8}
      resolveEmojiData={resolveEmojiData}
      onEmojiSelect={({ emoji }) => onSelect(emoji)}
    >
      <div className={styles.searchWrap}>
        <Icon icon={Search01Icon} size={17} />
        <EmojiPicker.Search autoFocus aria-label="Search emoji" placeholder="Search emoji" className={styles.search} />
      </div>
      <EmojiPicker.Viewport className={styles.viewport}>
        <EmojiPicker.Loading className={styles.empty}>Loading emoji…</EmojiPicker.Loading>
        <EmojiPicker.Empty className={styles.empty}>No emoji found.</EmojiPicker.Empty>
        <EmojiPicker.List className={styles.list} />
      </EmojiPicker.Viewport>
      <div className={styles.footer}>
        <EmojiPicker.ActiveEmoji>
          {({ emoji }) => <span className={styles.emojiLabel}>{emoji?.label ?? "Choose an emoji"}</span>}
        </EmojiPicker.ActiveEmoji>
        <EmojiPicker.SkinToneSelector className={styles.skinTone} />
      </div>
      {error ? <ErrorToast message={error} /> : null}
    </EmojiPicker.Root>
  );
}
