"use client";

import { ErrorToast } from "@/components/ui/error-toast";

import { useId, useLayoutEffect, useRef, useState } from "react";
import { Cancel01Icon, Delete02Icon, Image01Icon, PaintBoardIcon, PencilEdit02Icon, Tick02Icon, Undo03Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import { DEFAULT_FORM_THEME, formBackgroundColor, formThemeStyle, type FormTheme } from "../../../../../libs/ui/form-theme";
import shared from "./builder.module.css";
import styles from "./theme-picker.module.css";
import { prepareHeaderImage } from "./header-image";
import { LinkCardSettings } from "./link-card-settings";

const colors = [
  ["MorganHacks blue", "#003970"], ["Violet", "#6750a4"], ["Ocean", "#007f8b"],
  ["Forest", "#28734f"], ["Terracotta", "#ac563e"], ["Rose", "#ae4472"],
  ["Gold", "#c18c24"], ["Charcoal", "#34383e"],
];

const backgroundColors = [
  ["White", "#ffffff"], ["Light gray", "#f4f4f5"], ["Ice blue", "#eef3fa"],
  ["Lavender", "#f1eef8"], ["Sage", "#edf6f1"], ["Cream", "#fff6e8"],
  ["Blush", "#faeeee"], ["Dark gray", "#20242b"],
] as const;

export function ThemePicker({ theme, onChange, disabled }: {
  theme: FormTheme;
  onChange: (theme: FormTheme) => void;
  disabled: boolean;
}) {
  const id = useId();
  const trigger = useRef<HTMLButtonElement>(null);
  const panel = useRef<HTMLDivElement>(null);
  const fileInput = useRef<HTMLInputElement>(null);
  const chooseImageButton = useRef<HTMLButtonElement>(null);
  const latest = useRef({ theme, onChange });
  const [open, setOpen] = useState(false);
  const [section, setSection] = useState<"appearance" | "link">("appearance");
  const [preparing, setPreparing] = useState(false);
  const [imageError, setImageError] = useState<string | null>(null);
  const backgroundColor = formBackgroundColor(theme);
  const isDefault = Object.entries(DEFAULT_FORM_THEME).every(([key, value]) => key === "showMlhBadge" || key === "mlhBadgeColor" || key === "linkCard" || theme[key as keyof FormTheme] === value);

  useLayoutEffect(() => { latest.current = { theme, onChange }; });

  async function chooseImage(file: File) {
    setImageError(null);
    setPreparing(true);
    try {
      const headerImage = await prepareHeaderImage(file);
      latest.current.onChange({ ...latest.current.theme, headerImage });
    } catch (error) {
      setImageError(error instanceof Error ? error.message : "This image could not be added. Try again.");
    } finally {
      setPreparing(false);
    }
  }

  useLayoutEffect(() => {
    if (!open) return;
    const position = () => {
      if (!trigger.current || !panel.current) return;
      const rect = trigger.current.getBoundingClientRect();
      panel.current.style.left = `${Math.max(12, Math.min(rect.right - panel.current.offsetWidth, innerWidth - panel.current.offsetWidth - 12))}px`;
      panel.current.style.top = `${rect.bottom + 10}px`;
      panel.current.style.maxHeight = `${innerHeight - rect.bottom - 22}px`;
    };
    position();
    window.addEventListener("resize", position);
    window.addEventListener("scroll", position, true);
    return () => {
      window.removeEventListener("resize", position);
      window.removeEventListener("scroll", position, true);
    };
  }, [open]);

  return <>
    <button type="button" ref={trigger} className={shared.headerIcon} popoverTarget={id}
      aria-label="Customize theme" aria-haspopup="dialog" aria-controls={id} aria-expanded={open} disabled={disabled}>
      <Icon icon={PaintBoardIcon} size={20} />
    </button>
    <div id={id} ref={panel} popover="auto" className={styles.panel} role="dialog" style={formThemeStyle(theme)}
      aria-label="Customize form theme" onToggle={event => {
        if (event.target !== event.currentTarget) return;
        setOpen(event.newState === "open");
      }}>
      <div className={styles.heading}>
        <h2>Customize theme</h2>
        <button type="button" className={styles.close} aria-label="Close theme settings" popoverTarget={id} popoverTargetAction="hide"><Icon icon={Cancel01Icon} size={17} /></button>
      </div>

      <div className={styles.tabs} role="tablist" aria-label="Form design settings">
        {([['appearance', 'Appearance'], ['link', 'Link card']] as const).map(([key, label]) => <button
          key={key} type="button" role="tab" id={`${id}-${key}-tab`} aria-controls={`${id}-${key}-panel`}
          aria-selected={section === key} tabIndex={section === key ? 0 : -1} onClick={() => setSection(key)}
          onKeyDown={event => {
            if (!["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) return;
            event.preventDefault();
            const next = event.key === "Home" ? "appearance" : event.key === "End" ? "link" : key === "appearance" ? "link" : "appearance";
            setSection(next);
            document.getElementById(`${id}-${next}-tab`)?.focus();
          }}>{label}</button>)}
      </div>

      <div className={styles.panelBody}>
      <div role="tabpanel" id={`${id}-appearance-panel`} aria-labelledby={`${id}-appearance-tab`} hidden={section !== "appearance"}>
      <fieldset className={styles.group} disabled={disabled || preparing}>
        <legend>Header</legend>
        <div className={styles.headerRow}>
        {theme.headerImage ? <img className={styles.headerPreview} src={theme.headerImage} alt="Form header" /> : null}
        <div>
        <div className={styles.imageActions}>
          <button ref={chooseImageButton} type="button" className={styles.chooseImage} onClick={() => fileInput.current?.click()}>
            <Icon icon={Image01Icon} size={17} />{preparing ? "Preparing image…" : theme.headerImage ? "Change image" : "Choose image"}
          </button>
          {theme.headerImage ? <button type="button" className={styles.removeImage} aria-label="Remove header image"
            onClick={() => {
              setImageError(null);
              onChange({ ...theme, headerImage: null });
              chooseImageButton.current?.focus({ preventScroll: true });
            }}><Icon icon={Delete02Icon} size={15} />Remove</button> : null}
        </div>
        <input ref={fileInput} className={styles.fileInput} type="file" accept="image/jpeg,image/png,image/webp" aria-label="Choose header image"
          onChange={event => { const file = event.target.files?.[0]; event.target.value = ""; if (file) void chooseImage(file); }} />
        <p className={styles.imageHint}>Wide images work best · JPG, PNG or WebP</p>
        </div>
        </div>
        <ErrorToast title="Image could not be added" message={imageError} />
        <span className={styles.srOnly} role="status">{preparing ? "Preparing header image" : ""}</span>
      </fieldset>

      <div className={styles.paletteGrid}>
      <fieldset className={styles.group} disabled={disabled}>
        <legend className={styles.srOnly}>Theme color</legend>
        <div className={styles.colorHeading}>
          <span>Theme color</span>
          <label className={styles.customColor}>
            <span className={styles.customSwatch} aria-hidden="true" />
            <span aria-hidden="true">{theme.accent.toUpperCase()}</span>
            <Icon icon={PencilEdit02Icon} size={12} />
            <input aria-label="Custom color" type="color" value={theme.accent} onChange={event => onChange({ ...theme, accent: event.target.value })} />
          </label>
        </div>
        <div className={styles.swatches}>
          {colors.map(([name, accent]) => <button type="button" key={accent}
            aria-label={name} aria-pressed={theme.accent.toLowerCase() === accent}
            onClick={() => onChange({ ...theme, accent })}>
            <span style={{ background: accent, color: formThemeStyle({ ...theme, accent })["--form-accent-ink"] }}>
              {theme.accent.toLowerCase() === accent ? <Icon icon={Tick02Icon} size={16} /> : null}
            </span>
          </button>)}
        </div>
      </fieldset>

      <fieldset className={styles.group} disabled={disabled}>
        <legend className={styles.srOnly}>Background color</legend>
        <div className={styles.colorHeading}>
          <span>Background color</span>
          <label className={styles.customColor}>
            <span className={styles.customSwatch} style={{ background: backgroundColor }} aria-hidden="true" />
            <span aria-hidden="true">{backgroundColor.toUpperCase()}</span>
            <Icon icon={PencilEdit02Icon} size={12} />
            <input aria-label="Custom background color" type="color" value={backgroundColor}
              onChange={event => onChange({ ...theme, background: event.target.value as FormTheme["background"] })} />
          </label>
        </div>
        <div className={styles.swatches}>
          {backgroundColors.map(([name, color]) => <button type="button" key={color}
            aria-label={`${name} background`} aria-pressed={backgroundColor === color}
            onClick={() => onChange({ ...theme, background: color === "#ffffff" ? "white" : color })}>
            <span style={{ background: color, color: formThemeStyle({ ...theme, background: color })["--form-ink"] }}>
              {backgroundColor === color ? <Icon icon={Tick02Icon} size={16} /> : null}
            </span>
          </button>)}
        </div>
      </fieldset>

      </div>
      <div className={styles.typeGrid}>
      <fieldset className={styles.group} disabled={disabled}>
        <legend>Font style</legend>
        <div className={styles.fonts}>
          {([['sans', 'Modern'], ['serif', 'Classic'], ['mono', 'Mono']] as const).map(([font, label]) =>
            <button type="button" key={font} aria-label={label} aria-pressed={theme.font === font} onClick={() => onChange({ ...theme, font })}>
              <span className={styles.fontSample} style={{ fontFamily: formThemeStyle({ ...theme, font })["--form-font"] }} aria-hidden="true">Aa</span>
              <span>{label}</span>
              {theme.font === font ? <span className={styles.fontCheck} aria-hidden="true"><Icon icon={Tick02Icon} size={12} /></span> : null}
            </button>)}
        </div>
      </fieldset>

      <div className={styles.sizeRow}>
        <span id={`${id}-size`}>Text size</span>
        <div className={styles.sizes} role="group" aria-labelledby={`${id}-size`}>
          {([['small', 'Small'], ['medium', 'Default'], ['large', 'Large']] as const).map(([size, label]) =>
            <button type="button" key={size} disabled={disabled} aria-pressed={theme.size === size} onClick={() => onChange({ ...theme, size })}>{label}</button>)}
        </div>
      </div>
      </div>

      <div className={styles.footer}>
        <button type="button" disabled={disabled || preparing || isDefault} onClick={() => { setImageError(null); onChange({ ...DEFAULT_FORM_THEME, showMlhBadge: theme.showMlhBadge, mlhBadgeColor: theme.mlhBadgeColor, linkCard: theme.linkCard }); }}><Icon icon={Undo03Icon} size={14} />Reset theme</button>
        <span>Autosaves to draft</span>
      </div>
      </div>
      <div role="tabpanel" id={`${id}-link-panel`} aria-labelledby={`${id}-link-tab`} hidden={section !== "link"}>
        <LinkCardSettings card={theme.linkCard} onChange={linkCard => onChange({ ...theme, linkCard })} disabled={disabled}
          onOpen={() => panel.current?.hidePopover()} onClose={() => trigger.current?.focus({ preventScroll: true })} />
      </div>
      </div>
    </div>
  </>;
}
