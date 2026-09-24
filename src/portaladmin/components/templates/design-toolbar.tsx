"use client";

import { ComputerIcon, FloppyDiskIcon, Link04Icon, MailSend01Icon, SmartPhone01Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import styles from "./design-toolbar.module.css";

export type PreviewDevice = "desktop" | "mobile";

export function DesignToolbar({ device, onDevice, onImport, onSave, onTest, busy, hasContent }: {
  device: PreviewDevice;
  onDevice: (device: PreviewDevice) => void;
  onImport: () => void;
  onSave: () => void;
  onTest: () => void;
  busy: boolean;
  hasContent: boolean;
}) {
  return <div className={styles.toolbar} role="group" aria-label="Email design tools">
    <div className={styles.devices} role="group" aria-label="Preview device">
      {(["desktop", "mobile"] as const).map((option) => <button key={option} type="button"
        className={styles.device} aria-label={`${option === "desktop" ? "Desktop" : "Mobile"} preview`}
        aria-pressed={device === option} onClick={() => onDevice(option)} data-tooltip={`${option === "desktop" ? "Desktop" : "Mobile"} preview`}>
        <Icon icon={option === "desktop" ? ComputerIcon : SmartPhone01Icon} size={18} />
      </button>)}
    </div>
    <span className={styles.divider} aria-hidden="true" />
    <button type="button" className={styles.iconButton} aria-label="Import from URL" data-tooltip="Import from URL"
      disabled={busy} onClick={onImport}>
      <Icon icon={Link04Icon} size={18} />
    </button>
    <button type="button" className={styles.iconButton} aria-label="Save changes" data-tooltip="Save changes"
      disabled={busy} onClick={onSave}>
      <Icon icon={FloppyDiskIcon} size={18} />
    </button>
    <button type="button" className={styles.testButton} aria-label="Send test email" title="Send test email"
      disabled={busy || !hasContent} onClick={onTest}>
      <Icon icon={MailSend01Icon} size={18} />
      <span>Send test email</span>
    </button>
  </div>;
}
