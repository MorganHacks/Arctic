"use client";

import { Mail01Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import type { Rendered } from "./types";
import type { PreviewDevice } from "./design-toolbar";
import styles from "./design-workspace.module.css";

export function EmailPreview({ rendered, pending, error, device }: {
  rendered: Rendered | null;
  pending: boolean;
  error: string | null;
  device: PreviewDevice;
}) {
  return <>
    <div className={styles.previewCanvas} data-preview-device={device}>
      <div className={styles.previewDocument} data-preview-device={device} aria-busy={pending}>
        {!rendered ? <div className={styles.emptyPreview}>
          <Icon icon={Mail01Icon} size={28} strokeWidth={1.25} />
          <p>Your email will appear here</p>
        </div> : <iframe className={styles.previewFrame} title="Email preview" sandbox="" srcDoc={emailDocument(rendered.html, device)} />}
      </div>
    </div>
    {error ? <p className={styles.error} role="alert">{error}</p> : null}
  </>;
}

export function emailDocument(html: string, device: PreviewDevice): string {
  return [
    "<!doctype html><html><head><meta charset=\"utf-8\">",
    "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">",
    "<style>:root{color-scheme:light}",
    device === "mobile" ? `
      html { overflow-y: scroll; scrollbar-gutter: stable; }
      ::-webkit-scrollbar { width: 8px; height: 8px; }
      ::-webkit-scrollbar-track { background: transparent; margin-block: 2px; }
      ::-webkit-scrollbar-thumb { background: #b8b8b5; border: 2px solid transparent; border-radius: 999px; background-clip: content-box; }
      ::-webkit-scrollbar-thumb:hover { background-color: #999995; }
      ::-webkit-scrollbar-corner { background: transparent; }
      @supports not selector(::-webkit-scrollbar) {
        html { scrollbar-width: thin; scrollbar-color: #b8b8b5 transparent; }
      }
    ` : "",
    "</style></head><body>",
    html,
    "</body></html>",
  ].join("");
}
