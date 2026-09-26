import { formBackgroundColor, formThemeStyle, type FormTheme } from "../../../../libs/ui/form-theme";

export function PageBackground({ theme }: { theme?: Partial<FormTheme> | null }) {
  const background = formBackgroundColor(theme);
  const colorScheme = formThemeStyle(theme)["--form-color-scheme"];
  return <style>{`html, body { background-color: ${background}; color-scheme: ${colorScheme}; }`}</style>;
}
