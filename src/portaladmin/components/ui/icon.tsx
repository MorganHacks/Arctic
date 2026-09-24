import { HugeiconsIcon, type HugeiconsIconProps } from "@hugeicons/react";

export type { IconSvgElement } from "@hugeicons/react";

/** Decorative icons inherit their control's color and accessible label. */
export function Icon({
  size = 20,
  strokeWidth = 1.75,
  ...props
}: HugeiconsIconProps) {
  return (
    <HugeiconsIcon
      size={size}
      strokeWidth={strokeWidth}
      aria-hidden="true"
      focusable="false"
      {...props}
    />
  );
}
