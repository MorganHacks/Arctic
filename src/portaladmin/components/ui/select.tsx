import type { ComponentProps } from "react";
import { ArrowDown01Icon } from "@hugeicons/core-free-icons";
import { Icon } from "./icon";
import styles from "./select.module.css";

export function Select({ fullWidth = true, ...props }: ComponentProps<"select"> & { fullWidth?: boolean }) {
  if (props.multiple || props.size) return <select {...props} />;

  return <span className={styles.control} data-full-width={fullWidth}>
    <select {...props} />
    <Icon icon={ArrowDown01Icon} size={16} className={styles.chevron} />
  </span>;
}
