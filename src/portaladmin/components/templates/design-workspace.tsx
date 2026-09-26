"use client";

import { ErrorToast } from "@/components/ui/error-toast";

import { useId, useLayoutEffect, useRef, useState, type CSSProperties } from "react";
import Image from "next/image";
import { SourceCodeIcon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import { CodeEditor, type CodeEditorHandle } from "./code-editor";
import { EmailPreview } from "./email-preview";
import { SubjectCustomFields } from "./subject-custom-fields";
import type { PreviewDevice } from "./design-toolbar";
import type { DraftHandle } from "./use-draft";
import type { Preview } from "./use-preview";
import type { Placeholder } from "./types";
import styles from "./design-workspace.module.css";

export function DesignWorkspace({ handle, available, preview, device, disabled, error, validationAttempt, saveNotice }: {
  handle: DraftHandle;
  available: Placeholder[] | null;
  preview: Preview;
  device: PreviewDevice;
  disabled: boolean;
  error?: string;
  validationAttempt: number;
  saveNotice: string | null;
}) {
  const paneId = useId();
  const split = useRef<HTMLDivElement>(null);
  const editor = useRef<CodeEditorHandle>(null);
  const [percent, setPercent] = useState(50);
  const [minPercent, setMinPercent] = useState(25);
  const [dragging, setDragging] = useState(false);
  const [mobilePane, setMobilePane] = useState<"code" | "preview">("code");

  useLayoutEffect(() => {
    if (error) editor.current?.focus();
  }, [error, validationAttempt]);

  useLayoutEffect(() => {
    if (!split.current) return;
    const observer = new ResizeObserver(([entry]) => {
      const min = Math.min(45, Math.max(25, 240 / Math.max(1, entry.contentRect.width - 7) * 100));
      setMinPercent(min);
      setPercent((value) => Math.max(min, Math.min(100 - min, value)));
    });
    observer.observe(split.current);
    return () => observer.disconnect();
  }, []);

  function resize(value: number) { setPercent(Math.max(minPercent, Math.min(100 - minPercent, value))); }

  return <div className={styles.workspace}>
    <div className={styles.mobileTabs} role="tablist" aria-label="Design panels">
      {(["code", "preview"] as const).map((pane) => <button key={pane} type="button" role="tab"
        aria-selected={mobilePane === pane} aria-controls={`${paneId}-${pane}`} onClick={() => setMobilePane(pane)}>
        {pane === "code" ? "Code" : "Preview"}
      </button>)}
    </div>
    <div ref={split} className={styles.split} data-dragging={dragging || undefined}
      style={{ "--editor-size": `${percent}fr`, "--preview-size": `${100 - percent}fr` } as CSSProperties}>
      <section id={`${paneId}-code`} className={styles.codePane} data-active={mobilePane === "code"} aria-label="Email editor">
        <div className={styles.paneHeader}>
          <span className={styles.fileName}>
            {handle.draft.format === "html"
              ? <Image className={styles.fileIcon} src="/icons/material/html.svg" width={20} height={20} alt="" aria-hidden="true" draggable={false} unoptimized />
              : <Icon icon={SourceCodeIcon} size={17} />}
            {handle.draft.format === "html" ? "email.html" : "email.md"}
          </span>
          <fieldset className={styles.tools} disabled={disabled}>
            <SubjectCustomFields available={available} label="Email body" onOpen={() => {}}
              onSelect={(text) => editor.current?.insert(text) ?? false} error="" />
          </fieldset>
        </div>
        <CodeEditor ref={editor} value={handle.draft.body} format={handle.draft.format} available={available}
          onChange={(body) => handle.set("body", body)} disabled={disabled} invalid={Boolean(error)} />
        {error ? <ErrorToast descriptionId="template-body-error" message={error} /> : null}
      </section>
      <div role="separator" aria-label="Resize editor and preview" aria-orientation="vertical" aria-controls={`${paneId}-code`}
        aria-valuenow={Math.round(percent)} aria-valuemin={Math.ceil(minPercent)} aria-valuemax={Math.floor(100 - minPercent)}
        aria-valuetext={`${Math.round(percent)} percent editor width`} tabIndex={0}
        className={`${styles["spawn-resizer"]} ${dragging ? styles.active : ""}`}
        onDoubleClick={() => resize(50)}
        onPointerDown={(event) => {
          if (event.button !== 0) return;
          event.preventDefault();
          event.currentTarget.focus();
          event.currentTarget.setPointerCapture(event.pointerId);
          setDragging(true);
        }}
        onPointerMove={(event) => {
          if (!event.currentTarget.hasPointerCapture(event.pointerId) || !split.current) return;
          const rect = split.current.getBoundingClientRect();
          resize((event.clientX - rect.left - 3.5) / (rect.width - 7) * 100);
        }}
        onPointerUp={(event) => {
          if (event.currentTarget.hasPointerCapture(event.pointerId)) event.currentTarget.releasePointerCapture(event.pointerId);
          setDragging(false);
        }}
        onLostPointerCapture={() => setDragging(false)}
        onKeyDown={(event) => {
          const step = event.shiftKey ? 10 : 2;
          const next = event.key === "ArrowLeft" ? percent - step : event.key === "ArrowRight" ? percent + step
            : event.key === "Home" ? minPercent : event.key === "End" ? 100 - minPercent : event.key === "Enter" ? 50 : null;
          if (next !== null) { event.preventDefault(); resize(next); }
        }}>
        <div aria-hidden="true" className={styles["spawn-resizer-track"]} />
        <div aria-hidden="true" className={styles["spawn-resizer-handle"]} />
      </div>
      <section id={`${paneId}-preview`} className={styles.previewPane} data-active={mobilePane === "preview"} aria-label="Email preview">
        <EmailPreview {...preview} device={device} />
      </section>
    </div>
    <div role="status" aria-live="polite" aria-atomic="true">
      {saveNotice ? <div className={styles.saveToast}>{saveNotice}</div> : null}
    </div>
  </div>;
}
