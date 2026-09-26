"use client";

import { useEffect, useRef, useState } from "react";
import { Cancel01Icon, Download01Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import { createResponseSheet, loadGoogleOAuth, sheetsScope, type GoogleOAuth } from "./google-sheets";
import styles from "./responses.module.css";

export function GoogleSheetsButton({ formId, name }: { formId: string; name: string }) {
  const [open, setOpen] = useState(false);
  const [sheetUrl, setSheetUrl] = useState<string | null>(null);

  return (
    <>
      {sheetUrl ? (
        <a className={styles.sheetsButton} href={sheetUrl} target="_blank" rel="noopener noreferrer">
          <img src="/google-sheets.png" width={24} height={24} alt="" />View in Sheets
        </a>
      ) : (
        <button type="button" className={styles.sheetsButton} onClick={() => setOpen(true)}>
          <img src="/google-sheets.png" width={24} height={24} alt="" />View in Sheets
        </button>
      )}
      {open ? <SheetsDialog formId={formId} name={name} onCreated={setSheetUrl} onClose={() => setOpen(false)} /> : null}
    </>
  );
}

function SheetsDialog({ formId, name, onCreated, onClose }: {
  formId: string;
  name: string;
  onCreated: (url: string) => void;
  onClose: () => void;
}) {
  const panel = useRef<HTMLDialogElement>(null);
  const active = useRef(true);
  const upload = useRef<AbortController | null>(null);
  const [connection, setConnection] = useState<{ clientId: string; oauth: GoogleOAuth } | null>(null);
  const [loading, setLoading] = useState(true);
  const [configured, setConfigured] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [url, setUrl] = useState<string | null>(null);

  useEffect(() => {
    active.current = true;
    const dialog = panel.current;
    const previous = document.activeElement;
    const controller = new AbortController();
    dialog?.showModal();
    async function prepare() {
      try {
        const response = await fetch(`/api/admin/forms/${formId}/responses/sheets-config`, {
          credentials: "same-origin", cache: "no-store", signal: controller.signal,
        });
        if (!response.ok) throw new Error("Google Sheets could not be connected. Please try again.");
        const result: { clientId: string | null } = await response.json();
        if (controller.signal.aborted) return;
        if (!result.clientId) {
          if (active.current) setConfigured(false);
          return;
        }
        const oauth = await loadGoogleOAuth();
        if (active.current && !controller.signal.aborted) setConnection({ clientId: result.clientId, oauth });
      } catch (cause) {
        if (active.current && !controller.signal.aborted) setError(cause instanceof Error ? cause.message : "Google could not be connected.");
      } finally {
        if (active.current && !controller.signal.aborted) setLoading(false);
      }
    }
    void prepare();
    return () => {
      active.current = false;
      controller.abort();
      upload.current?.abort();
      dialog?.close();
      if (previous instanceof HTMLElement && previous.isConnected) previous.focus();
    };
  }, [formId]);

  function connect() {
    if (!connection || busy) return;
    setError(null);
    setBusy(true);
    try {
    const client = connection.oauth.initTokenClient({
      client_id: connection.clientId,
      scope: sheetsScope,
      include_granted_scopes: false,
      callback: async (token) => {
        if (!active.current) return;
        if (token.error || !token.access_token || !connection.oauth.hasGrantedAllScopes(token, sheetsScope)) {
          setBusy(false);
          setError("Allow spreadsheet creation in Google to continue, or download the CSV instead.");
          return;
        }
        const controller = new AbortController();
        upload.current = controller;
        try {
          const link = await createResponseSheet({ formId, name, accessToken: token.access_token,
            signal: AbortSignal.any([controller.signal, AbortSignal.timeout(90000)]) });
          if (!active.current) return;
          setUrl(link);
          onCreated(link);
        } catch (cause) {
          if (active.current) setError(cause instanceof Error && cause.name !== "TimeoutError"
            ? cause.message : "Google took too long to respond. Please try again or download the CSV.");
        } finally {
          if (active.current) setBusy(false);
          upload.current = null;
        }
      },
      error_callback: (failure) => {
        if (!active.current) return;
        setBusy(false);
        if (failure.type !== "popup_closed") setError("Google could not open. Allow pop-ups for this site and try again.");
      },
    });
    client.requestAccessToken({ prompt: "select_account" });
    } catch {
      setBusy(false);
      setError("Google could not open. Please try again.");
    }
  }

  return (
    <dialog ref={panel} className={styles.sheetsDialog} aria-labelledby="sheets-title"
      onCancel={onClose}>
      <div className={styles.panelHead}>
        <h2 id="sheets-title">{url ? "Your spreadsheet is ready" : "View in Google Sheets"}</h2>
        <button type="button" className={styles.iconButton} onClick={onClose} aria-label="Close Google Sheets">
          <Icon icon={Cancel01Icon} size={18} />
        </button>
      </div>
      <div className={styles.sheetsBody}>
        <img src="/google-sheets.png" width={40} height={40} alt="" />
        <p>{url
          ? "All responses have been copied to your Google Drive. This is a snapshot; new responses stay in your form."
          : configured
            ? "Create a spreadsheet with all responses in your Google Drive. Choose the account you want to save it to."
            : "Google Sheets isn’t connected for this workspace yet. You can still download the CSV and import it into a new spreadsheet."}</p>
        {!configured ? <ol><li>Download your responses below.</li><li>In Google Sheets, open <strong>File</strong>, select <strong>Import</strong>, then <strong>Upload</strong>.</li></ol> : null}
        {error ? <p className={styles.failed} role="alert">{error}</p> : null}
        <div className={styles.sheetsActions}>
          {url ? (
            <a className={styles.primaryButton} href={url} target="_blank" rel="noopener noreferrer" onClick={onClose}>
              Open spreadsheet
            </a>
          ) : configured && !error ? (
            <button type="button" className={styles.primaryButton} onClick={connect} disabled={loading || busy}>
              {busy ? "Creating spreadsheet…" : loading ? "Connecting…" : "Choose Google account"}
            </button>
          ) : (
            <>
              <a className={styles.secondaryButton} href={`/api/admin/forms/${formId}/responses.csv`}>
                <Icon icon={Download01Icon} size={16} />Download CSV
              </a>
              {!configured ? <a className={styles.primaryButton} href="https://sheets.google.com/create" target="_blank" rel="noopener noreferrer">Open Sheets</a> : connection ? <button type="button" className={styles.primaryButton} onClick={connect} disabled={busy}>{busy ? "Creating spreadsheet…" : "Try again"}</button> : null}
            </>
          )}
        </div>
        {configured && !url ? <p className={styles.sheetsNote}>Only files created by this app are accessible. Your existing spreadsheets stay private.</p> : null}
      </div>
    </dialog>
  );
}
