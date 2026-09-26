export const MLH_BADGE_OPTIONS = [
  { value: "white", label: "Original" },
  { value: "black", label: "Black" },
  { value: "gray", label: "Gray" },
  { value: "red", label: "Red" },
  { value: "blue", label: "Blue" },
  { value: "yellow", label: "Yellow" },
] as const;

export type MlhBadgeColor = typeof MLH_BADGE_OPTIONS[number]["value"];

export type FormLinkCard = {
  label: string;
  title: string;
  url: string;
  image: string | null;
};

export type FormTheme = {
  accent: string;
  background: "neutral" | "tint" | "white" | `#${string}`;
  font: "sans" | "serif" | "mono";
  size: "small" | "medium" | "large";
  headerImage: string | null;
  showMlhBadge: boolean;
  mlhBadgeColor: "auto" | MlhBadgeColor;
  linkCard: FormLinkCard | null;
};

export const MAX_HEADER_IMAGE_LENGTH = 350_000;
export const MAX_LINK_CARD_IMAGE_LENGTH = 80_000;

export function isFormLinkUrl(value: unknown): value is string {
  if (typeof value !== "string" || value.length > 2048 || !/^https?:\/\//i.test(value) || /[\s\\]/.test(value)) return false;
  try {
    const url = new URL(value);
    return !!url.hostname && !url.username && !url.password;
  } catch {
    return false;
  }
}

export function isFormLinkCard(value: unknown): value is FormLinkCard {
  if (!value || typeof value !== "object") return false;
  const card = value as Partial<FormLinkCard>;
  return typeof card.label === "string" && card.label.length <= 60
    && typeof card.title === "string" && card.title.trim().length > 0 && card.title.length <= 80
    && isFormLinkUrl(card.url)
    && (card.image === null || isFormHeaderImage(card.image) && card.image.length <= MAX_LINK_CARD_IMAGE_LENGTH);
}

export function isFormHeaderImage(value: unknown): value is string {
  return typeof value === "string" && value.length <= MAX_HEADER_IMAGE_LENGTH
    && /^data:image\/webp;base64,UklGR[A-Za-z0-9+/]+={0,2}$/.test(value);
}

export const DEFAULT_FORM_THEME: FormTheme = {
  accent: "#003970",
  background: "white",
  font: "sans",
  size: "medium",
  headerImage: null,
  showMlhBadge: false,
  mlhBadgeColor: "white",
  linkCard: null,
};

export function resolveFormTheme(value?: Partial<FormTheme> | null): FormTheme {
  return {
    accent: /^#[0-9a-f]{6}$/i.test(value?.accent ?? "") ? value!.accent! : DEFAULT_FORM_THEME.accent,
    background: isFormBackground(value?.background) ? value.background : DEFAULT_FORM_THEME.background,
    font: value?.font === "serif" || value?.font === "mono" ? value.font : "sans",
    size: value?.size === "small" || value?.size === "large" ? value.size : "medium",
    headerImage: isFormHeaderImage(value?.headerImage) ? value.headerImage : null,
    showMlhBadge: value?.showMlhBadge === true,
    mlhBadgeColor: formMlhBadgeColor(value),
    linkCard: isFormLinkCard(value?.linkCard) ? value.linkCard : null,
  };
}

export function isFormBackground(value: unknown): value is FormTheme["background"] {
  return value === "white" || value === "neutral" || value === "tint"
    || typeof value === "string" && /^#[0-9a-f]{6}$/i.test(value);
}

export function formBackgroundColor(value?: Partial<FormTheme> | null): string {
  const theme = resolveFormTheme(value);
  if (theme.background === "white") return "#ffffff";
  if (theme.background === "neutral") return "#f4f4f5";
  if (theme.background === "tint") return `#${[1, 3, 5].map(start =>
    Math.round(parseInt(theme.accent.slice(start, start + 2), 16) * 0.07 + 255 * 0.93).toString(16).padStart(2, "0")
  ).join("")}`;
  return theme.background.toLowerCase();
}

export function formMlhBadgeColor(value?: Partial<FormTheme> | null): MlhBadgeColor {
  return MLH_BADGE_OPTIONS.find(option => option.value === value?.mlhBadgeColor)?.value ?? "white";
}

export function mlhBadgeImageUrl(season: number, color: MlhBadgeColor): string {
  return `https://logged-assets.s3.amazonaws.com/trust-badge/${season}/mlh-trust-badge-${season}-${color}.svg`;
}

const fonts = {
  sans: 'Inter, Arial, sans-serif',
  serif: 'Georgia, "Times New Roman", serif',
  mono: 'ui-monospace, "SF Mono", Menlo, monospace',
};

function luminance(color: string): number {
  const channels = [1, 3, 5].map(start => {
    const channel = parseInt(color.slice(start, start + 2), 16) / 255;
    return channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4;
  });
  return channels[0] * 0.2126 + channels[1] * 0.7152 + channels[2] * 0.0722;
}

export function formThemeStyle(value?: Partial<FormTheme> | null): Record<string, string> {
  const theme = resolveFormTheme(value);
  const background = formBackgroundColor(theme);
  const dark = luminance(background) < 0.179;
  return {
    "--form-accent": theme.accent,
    "--form-accent-ink": luminance(theme.accent) > 0.179 ? "#14161a" : "#ffffff",
    "--form-background": background,
    "--form-ink": dark ? "#ffffff" : "#20242b",
    "--form-muted": dark ? "#bec5d0" : "#68707d",
    "--form-faint": dark ? "#a7b1bf" : "#848b96",
    "--form-paper": dark ? "#20242b" : "#ffffff",
    "--form-color-scheme": dark ? "dark" : "light",
    "--form-selection-ink": dark ? "#14161a" : "#ffffff",
    "--form-font": fonts[theme.font],
    "--form-font-size": `${{ small: 13, medium: 14, large: 16 }[theme.size]}px`,
  };
}
