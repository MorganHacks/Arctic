import type { FieldType } from "@/lib/api";

/**
 * The builder's icons, drawn rather than fetched.
 *
 * Inline SVG and no icon font. A font would be a request to somebody else's
 * CDN on a console that sits behind a login, and until it answered every
 * control on this screen would be a blank square — the failure mode of an icon
 * font is a toolbar of nothing, which is worse than any icon it could deliver.
 *
 * `currentColor` throughout, so an icon is coloured by the control it sits in
 * and never by itself. That is what keeps the palette's one rule — colour
 * carries meaning — true of a file full of little pictures.
 *
 * Every icon is decorative: each one sits beside a word, or inside a button
 * whose accessible name is set by an `aria-label`. `aria-hidden` on all of
 * them, so a screen reader reads the name once rather than the name and a
 * shrug.
 */
function Icon({
  children,
  size = 16,
}: {
  children: React.ReactNode;
  size?: number;
}) {
  return (
    <svg
      aria-hidden="true"
      focusable="false"
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.75"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      {children}
    </svg>
  );
}

export function ArrowUp() {
  return (
    <Icon>
      <path d="M12 19V5M5 12l7-7 7 7" />
    </Icon>
  );
}

export function ArrowDown() {
  return (
    <Icon>
      <path d="M12 5v14M19 12l-7 7-7-7" />
    </Icon>
  );
}

export function Duplicate() {
  return (
    <Icon>
      <rect x="9" y="9" width="12" height="12" rx="2" />
      <path d="M5 15H4a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1h10a1 1 0 0 1 1 1v1" />
    </Icon>
  );
}

export function Trash() {
  return (
    <Icon>
      <path d="M3 6h18M8 6V4h8v2M6 6l1 14h10l1-14" />
    </Icon>
  );
}

export function Cross() {
  return (
    <Icon>
      <path d="M18 6 6 18M6 6l12 12" />
    </Icon>
  );
}

export function Plus() {
  return (
    <Icon>
      <path d="M12 5v14M5 12h14" />
    </Icon>
  );
}

export function Lock() {
  return (
    <Icon size={12}>
      <rect x="4" y="10" width="16" height="11" rx="2" />
      <path d="M8 10V7a4 4 0 0 1 8 0v3" />
    </Icon>
  );
}

export function Info() {
  return (
    <Icon size={14}>
      <circle cx="12" cy="12" r="9" />
      <path d="M12 11v5M12 8h.01" />
    </Icon>
  );
}

export function Warning() {
  return (
    <Icon size={13}>
      <circle cx="12" cy="12" r="9" />
      <path d="M12 7v6M12 16h.01" />
    </Icon>
  );
}

export function Copy() {
  return (
    <Icon size={14}>
      <rect x="9" y="9" width="12" height="12" rx="2" />
      <path d="M5 15H4a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1h10a1 1 0 0 1 1 1v1" />
    </Icon>
  );
}

export function Save() {
  return (
    <Icon>
      <path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2Z" />
      <path d="M17 21v-8H7v8M7 3v5h8" />
    </Icon>
  );
}

export function Publish() {
  return (
    <Icon>
      <path d="M12 16V4M7 9l5-5 5 5" />
      <path d="M4 17v2a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-2" />
    </Icon>
  );
}

export function PageBreakIcon({ size = 18 }: { size?: number }) {
  return (
    <Icon size={size}>
      <path d="M3 12h3M9 12h6M18 12h3" />
      <rect x="5" y="3" width="14" height="5" rx="1" />
      <rect x="5" y="16" width="14" height="5" rx="1" />
    </Icon>
  );
}

export function Questions() {
  return (
    <Icon size={15}>
      <path d="M4 6h11M4 11h11M4 16h7" />
      <path d="M17 16l2 2 3-4" />
    </Icon>
  );
}

export function Chart() {
  return (
    <Icon size={15}>
      <path d="M4 20V10M10 20V4M16 20v-7M22 20H2" />
    </Icon>
  );
}

/**
 * One icon per question type.
 *
 * Not decoration on the type picker. Twelve labels in a grid are twelve things
 * to read; twelve shapes are twelve things to recognise, and somebody who has
 * added forty questions this week stops reading the grid entirely and aims at
 * the shape. The words stay beside them — the shape is the shortcut, never the
 * only way to tell two buttons apart.
 */
export function TypeIcon({ type }: { type: FieldType }) {
  switch (type) {
    case "paragraph":
      return (
        <Icon>
          <path d="M4 6h16M4 12h16M4 18h10" />
        </Icon>
      );

    case "email":
      return (
        <Icon>
          <rect x="3" y="5" width="18" height="14" rx="2" />
          <path d="m3 7 9 6 9-6" />
        </Icon>
      );

    case "phone":
      return (
        <Icon>
          <rect x="6" y="2" width="12" height="20" rx="2" />
          <path d="M11 18h2" />
        </Icon>
      );

    case "number":
      return (
        <Icon>
          <path d="M9 4 7 20M17 4l-2 16M4 9h16M3 15h16" />
        </Icon>
      );

    case "date":
      return (
        <Icon>
          <rect x="3" y="5" width="18" height="16" rx="2" />
          <path d="M8 3v4M16 3v4M3 11h18" />
        </Icon>
      );

    case "select":
      return (
        <Icon>
          <rect x="3" y="6" width="18" height="12" rx="2" />
          <path d="m9 11 3 3 3-3" />
        </Icon>
      );

    case "radio":
      return (
        <Icon>
          <circle cx="12" cy="12" r="9" />
          <circle cx="12" cy="12" r="3.5" fill="currentColor" stroke="none" />
        </Icon>
      );

    case "checkboxes":
      return (
        <Icon>
          <rect x="3" y="3" width="18" height="18" rx="3" />
          <path d="m8 12 3 3 5-6" />
        </Icon>
      );

    case "consent":
      return (
        <Icon>
          <path d="M6 3h8l5 5v13H6z" />
          <path d="M14 3v5h5" />
          <path d="m9 15 2 2 4-4" />
        </Icon>
      );

    case "file":
      return (
        <Icon>
          <path d="M12 16V4M7 9l5-5 5 5" />
          <path d="M4 16v3a1 1 0 0 0 1 1h14a1 1 0 0 0 1-1v-3" />
        </Icon>
      );

    case "section":
      return <PageBreakIcon size={16} />;

    // Short text, and anything a future type has not taught this file about.
    default:
      return (
        <Icon>
          <path d="M4 9h16M4 15h9" />
        </Icon>
      );
  }
}
