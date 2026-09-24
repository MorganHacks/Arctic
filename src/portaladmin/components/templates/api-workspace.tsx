"use client";

import { useEffect, useRef, useState, useTransition, type FormEvent } from "react";
import {
  CheckmarkCircle02Icon,
  Copy01Icon,
  MailSend01Icon,
  SourceCodeIcon,
} from "@hugeicons/core-free-icons";
import { sendTemplateTest } from "@/app/templates/actions";
import { Icon } from "@/components/ui/icon";
import { formatLabel, kindLabel, type TemplateDraft, type TemplateKind } from "./types";
import styles from "./api-workspace.module.css";

export function ApiWorkspace({
  templateKey,
  version,
  kind,
  placeholders,
  defaultRecipient,
  draft,
  canManage,
}: {
  templateKey: string;
  version: number;
  kind: TemplateKind;
  placeholders: readonly string[];
  defaultRecipient: string;
  draft: TemplateDraft;
  canManage: boolean;
}) {
  const request = useRef<{ fingerprint: string; id: string } | null>(null);
  const [copyStatus, setCopyStatus] = useState<"idle" | "copied" | "error">("idle");
  const [recipient, setRecipient] = useState(defaultRecipient);
  const [error, setError] = useState<string | null>(null);
  const [queued, setQueued] = useState(false);
  const [sending, startSending] = useTransition();
  const sender = `${draft.fromLocal}@${draft.fromDomain}`;

  useEffect(() => {
    if (copyStatus === "idle") return;
    const timer = window.setTimeout(() => setCopyStatus("idle"), 2500);
    return () => window.clearTimeout(timer);
  }, [copyStatus]);

  function sendTest(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const address = recipient.trim();
    const fingerprint = JSON.stringify({ draft, recipient: address });
    if (request.current?.fingerprint !== fingerprint) {
      request.current = { fingerprint, id: crypto.randomUUID() };
    }
    const requestId = request.current.id;
    setError(null);
    setQueued(false);
    startSending(async () => {
      const result = await sendTemplateTest(draft, address, requestId);
      if (result.ok) setQueued(true);
      else setError(result.error);
    });
  }

  async function copyKey() {
    try {
      await navigator.clipboard.writeText(templateKey);
      setCopyStatus("copied");
    } catch {
      setCopyStatus("error");
    }
  }

  return (
    <section className={styles.workspace} aria-labelledby="template-ready-title">
      <header className={styles.completion}>
        <div className={styles.completionCopy}>
          <p className={styles.status}><Icon icon={CheckmarkCircle02Icon} size={17} />Template saved</p>
          <h1 id="template-ready-title">{draft.name.trim() || "Your template is ready"}</h1>
          <p className={styles.completionText}>Your template is ready. Send a test to see it in your inbox.</p>
        </div>
        <div className={styles.metadata} aria-label="Template metadata">
          <span>{kindLabel(kind)}</span>
          <span>Version {version}</span>
        </div>
      </header>

      <div className={styles.grid}>
        <section className={styles.card} aria-labelledby="template-reference-title">
          <div className={styles.sectionHeading}>
            <span className={styles.sectionIcon}><Icon icon={SourceCodeIcon} size={21} /></span>
            <div>
              <h2 id="template-reference-title">Template reference</h2>
              <p>Everything you need to use this template.</p>
            </div>
          </div>

          <div className={styles.keyGroup} role="group" aria-labelledby="saved-template-key-label">
            <p id="saved-template-key-label">Template key</p>
            <div className={styles.keyField}>
              <code>{templateKey}</code>
              <button type="button" onClick={copyKey} disabled={!templateKey}
                aria-label={copyStatus === "copied" ? "Template key copied" : "Copy template key"}>
                <Icon icon={copyStatus === "copied" ? CheckmarkCircle02Icon : Copy01Icon} size={17} />
                {copyStatus === "copied" ? "Copied" : "Copy"}
              </button>
            </div>
            <span className={styles.copyStatus} role="status">{copyStatus === "copied" ? "Template key copied." : ""}</span>
            {copyStatus === "error" ? <p className={styles.error} role="alert">Could not copy. Select the key and copy it manually.</p> : null}
          </div>

          <dl className={styles.details}>
            <div className={styles.subject}><dt>Subject</dt><dd>{draft.subject}</dd></div>
            <div><dt>From</dt><dd>{draft.fromName || sender}{draft.fromName ? <span>{sender}</span> : null}</dd></div>
            <div><dt>Format</dt><dd>{formatLabel(draft.format)}</dd></div>
            {draft.replyTo ? <div className={styles.subject}><dt>Reply to</dt><dd>{draft.replyTo}</dd></div> : null}
          </dl>

          {placeholders.length > 0 ? (
            <div className={styles.fields}>
              <div className={styles.fieldsHeading}>
                <h3>Custom fields</h3>
                <span>{placeholders.length} {placeholders.length === 1 ? "field" : "fields"}</span>
              </div>
              <ul>{placeholders.map((placeholder) => <li key={placeholder}><code>{`{{${placeholder}}}`}</code></li>)}</ul>
            </div>
          ) : null}
        </section>

        <section className={`${styles.card} ${styles.testCard}`} aria-labelledby="send-test-title">
          <div className={styles.sectionHeading}>
            <span className={styles.sectionIcon}><Icon icon={MailSend01Icon} size={21} /></span>
            <div>
              <h2 id="send-test-title">Send a test email</h2>
              <p>A final look before your recipients see it.</p>
            </div>
          </div>

          <form className={styles.testForm} onSubmit={sendTest}>
            <div className={styles.field}>
              <label htmlFor="saved-template-test-recipient">Send to</label>
              <input id="saved-template-test-recipient" type="email" autoComplete="email" required maxLength={320}
                placeholder="you@example.com" value={recipient} disabled={sending || !canManage}
                onChange={(event) => { setRecipient(event.target.value); setError(null); setQueued(false); }} />
            </div>
            <button type="submit" className={`button primary ${styles.sendButton}`} disabled={sending || !canManage}>
              <Icon icon={MailSend01Icon} size={18} />
              {sending ? "Queueing…" : "Send test email"}
            </button>
            {placeholders.length > 0 ? <p className={styles.help}>Custom fields stay visible in the test so you can check their placement.</p> : null}
            {queued ? (
              <p className={styles.success} role="status">
                <Icon icon={CheckmarkCircle02Icon} size={17} />
                Test email queued for <strong>{recipient.trim()}</strong>.
              </p>
            ) : null}
            {error ? <p className={styles.error} role="alert">{error}</p> : null}
          </form>
        </section>
      </div>
    </section>
  );
}
