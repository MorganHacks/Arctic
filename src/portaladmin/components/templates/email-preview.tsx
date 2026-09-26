"use client";

import { ErrorToast } from "@/components/ui/error-toast";

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
          <p>{pending ? "Rendering your email…" : "Your email will appear here"}</p>
        </div> : <iframe className={styles.previewFrame} title="Email preview" sandbox="" srcDoc={emailDocument(rendered.html)} />}
      </div>
    </div>
    {error ? <ErrorToast message={error} /> : null}
  </>;
}

export function emailDocument(html: string): string {
  return [
    "<!doctype html><html><head><meta charset=\"utf-8\">",
    "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">",
    "<style>:root{color-scheme:light}",
    `
      * { scrollbar-width: none; }
      *::-webkit-scrollbar { display: none; width: 0; height: 0; }
    `,
    "</style></head><body>",
    html,
    "</body></html>",
  ].join("");
}
