export type FormTheme = {
  accent: string;
  background: "neutral" | "tint" | "white";
  font: "sans" | "serif" | "mono";
  size: "small" | "medium" | "large";
  headerImage: string | null;
  showMlhBadge: boolean;
};

export const MAX_HEADER_IMAGE_LENGTH = 350_000;

export function isFormHeaderImage(value: unknown): value is string {
  return typeof value === "string" && value.length <= MAX_HEADER_IMAGE_LENGTH
    && /^data:image\/webp;base64,UklGR[A-Za-z0-9+/]+={0,2}$/.test(value);
}

export const DEFAULT_FORM_THEME: FormTheme = {
  accent: "#003970",
  background: "neutral",
  font: "sans",
  size: "medium",
  headerImage: null,
  showMlhBadge: false,
};

export function resolveFormTheme(value?: Partial<FormTheme> | null): FormTheme {
  return {
    accent: /^#[0-9a-f]{6}$/i.test(value?.accent ?? "") ? value!.accent! : DEFAULT_FORM_THEME.accent,
    background: value?.background === "tint" || value?.background === "white" ? value.background : "neutral",
    font: value?.font === "serif" || value?.font === "mono" ? value.font : "sans",
    size: value?.size === "small" || value?.size === "large" ? value.size : "medium",
    headerImage: isFormHeaderImage(value?.headerImage) ? value.headerImage : null,
    showMlhBadge: value?.showMlhBadge === true,
  };
}

const fonts = {
  sans: 'Inter, Arial, sans-serif',
  serif: 'Georgia, "Times New Roman", serif',
  mono: 'ui-monospace, "SF Mono", Menlo, monospace',
};

export function formThemeStyle(value?: Partial<FormTheme> | null): Record<string, string> {
  const theme = resolveFormTheme(value);
  const channels = [1, 3, 5].map(start => {
    const channel = parseInt(theme.accent.slice(start, start + 2), 16) / 255;
    return channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4;
  });
  const luminance = channels[0] * 0.2126 + channels[1] * 0.7152 + channels[2] * 0.0722;
  return {
    "--form-accent": theme.accent,
    "--form-accent-ink": luminance > 0.179 ? "#14161a" : "#ffffff",
    "--form-background": theme.background === "white" ? "var(--raised)" : theme.background === "tint"
      ? `color-mix(in srgb, ${theme.accent} 7%, var(--paper))`
      : "light-dark(#f4f4f5, #23252a)",
    "--form-font": fonts[theme.font],
    "--form-font-size": `${{ small: 13, medium: 14, large: 16 }[theme.size]}px`,
  };
}
