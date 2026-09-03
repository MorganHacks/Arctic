"use client";

import { useEffect, useRef, useState } from "react";
import type { Field } from "@/lib/api";

/** What the form holds once a resume is safely stored. */
export type Resume = { upload: string; name: string; size: number };

/**
 * Five mebibytes, the same number the API enforces.
 *
 * Checked here so nobody pushes five megabytes up campus wifi to be told no at
 * the end of it. That is the entire value of this copy — the API measures the
 * bytes it actually receives, and that is the check that decides anything.
 */
const MAX_BYTES = 5 * 1024 * 1024;

type State =
  | { phase: "empty" }
  | { phase: "uploading"; name: string; sent: number; total: number }
  | { phase: "done"; resume: Resume }
  | { phase: "failed"; message: string };

/**
 * The resume question.
 *
 * The only control on this form that does something before the form is
 * submitted. The file goes up the moment it is picked, so somebody on a phone
 * spends the upload while they are still answering questions rather than
 * watching a progress bar after pressing Submit — which on campus wifi is the
 * difference between a submission that lands and one somebody gives up on.
 *
 * What comes back is an id, not a location. The page never learns where the
 * file was put, which is deliberate: a key the browser could repeat would be a
 * caller naming a blob, and the id is something the API can check against a row
 * it wrote itself.
 */
export function ResumeField({
  field,
  id,
  code,
  describedBy,
  wrong,
  value,
  onChange,
  onBusy,
}: {
  field: Field;
  id: string;
  code: string;
  describedBy: string | undefined;
  wrong: boolean;
  value: Resume | undefined;
  onChange: (key: string, value: Resume | undefined) => void;
  onBusy: (key: string, busy: boolean) => void;
}) {
  const [state, setState] = useState<State>(
    value ? { phase: "done", resume: value } : { phase: "empty" },
  );

  const request = useRef<XMLHttpRequest | null>(null);
  const input = useRef<HTMLInputElement>(null);

  // An upload still in flight when the page goes away is a request nobody is
  // waiting for. Left running it finishes into a component that no longer
  // exists and stores bytes no application will ever point at.
  useEffect(() => () => request.current?.abort(), []);

  function fail(message: string) {
    request.current = null;
    onBusy(field.key, false);
    onChange(field.key, undefined);
    setState({ phase: "failed", message });
  }

  function upload(file: File) {
    // Both checks are courtesies, and both are worth having. The size one
    // saves a long upload that was always going to be refused; the type one
    // catches the ordinary mistake of picking the Word document instead of the
    // PDF, before it costs a round trip.
    if (file.size > MAX_BYTES) {
      fail(
        `That file is ${megabytes(file.size)} MB, and the limit is 5 MB. ` +
          "Export it again at a smaller size, or remove any images, and pick it once more.",
      );
      return;
    }

    if (!looksLikeAPdf(file)) {
      fail(
        "That file is not a PDF. Use “Export as PDF” or “Save as PDF” " +
          "in whatever you wrote it in, then pick the file it makes.",
      );
      return;
    }

    const body = new FormData();
    body.append("file", file);

    const xhr = new XMLHttpRequest();
    request.current = xhr;

    // Same origin, through the rewrite in next.config.ts, like the submit.
    xhr.open("POST", `/api/forms/${encodeURIComponent(code)}/resume`);
    xhr.responseType = "json";

    // The reason this is XMLHttpRequest and not fetch. Upload progress is the
    // one thing fetch still cannot report, and a five megabyte file on a phone
    // without a progress bar looks like a page that has frozen.
    xhr.upload.onprogress = (event) => {
      if (event.lengthComputable) {
        setState({
          phase: "uploading",
          name: file.name,
          sent: event.loaded,
          total: event.total,
        });
      }
    };

    xhr.onload = () => {
      request.current = null;
      onBusy(field.key, false);

      const body = xhr.response as
        | { upload?: string; name?: string; size?: number; error?: string }
        | null;

      if (xhr.status >= 200 && xhr.status < 300 && body?.upload) {
        const resume: Resume = {
          upload: body.upload,
          name: body.name ?? file.name,
          size: body.size ?? file.size,
        };

        onChange(field.key, resume);
        setState({ phase: "done", resume });
        return;
      }

      // The API's own wording. It is the side that inspected the bytes, and a
      // second copy of "that was not really a PDF" over here would be a worse
      // one that drifts.
      fail(body?.error ?? "That file could not be uploaded. Try picking it again.");
    };

    xhr.onerror = () =>
      fail("The upload did not get through. Check your connection and try again.");

    xhr.onabort = () => {
      request.current = null;
      onBusy(field.key, false);
    };

    onBusy(field.key, true);
    setState({ phase: "uploading", name: file.name, sent: 0, total: file.size });
    xhr.send(body);
  }

  function pick(file: File | undefined) {
    // A second pick replaces the first, so the one in flight is abandoned
    // rather than racing the new one to decide which answer wins.
    request.current?.abort();

    if (!file) {
      onChange(field.key, undefined);
      setState({ phase: "empty" });
      return;
    }

    upload(file);
  }

  function clear() {
    request.current?.abort();
    onChange(field.key, undefined);
    setState({ phase: "empty" });

    // Without this the same file cannot be picked again: the input still holds
    // it, so choosing it a second time fires no change event at all.
    if (input.current) {
      input.current.value = "";
    }

    input.current?.focus();
  }

  return (
    <div className="upload">
      <input
        ref={input}
        id={id}
        name={field.key}
        type="file"
        accept="application/pdf,.pdf"
        /* The limits are part of the question, not a note beside it. Somebody
           hearing the page has to be told what will be accepted before they go
           looking for a file, not after one has been refused. */
        aria-describedby={[describedBy, `${id}-limits`].filter(Boolean).join(" ")}
        aria-invalid={wrong || undefined}
        className={wrong ? "wrong" : undefined}
        onChange={(e) => pick(e.target.files?.[0])}
      />

      <p className="help" id={`${id}-limits`}>
        PDF, up to 5 MB.
      </p>

      {state.phase === "uploading" ? (
        <Progress name={state.name} sent={state.sent} total={state.total} />
      ) : null}

      {state.phase === "done" ? (
        <p className="upload-done">
          {/* Announced, because on a phone the row appears below the fold and
              the only other sign the upload worked is a bar that vanished. */}
          <span role="status">
            <strong>{state.resume.name}</strong> is attached
            {" · "}
            {megabytes(state.resume.size)} MB
          </span>
          <button type="button" className="quiet" onClick={clear}>
            Remove
          </button>
        </p>
      ) : null}

      {state.phase === "failed" ? (
        <strong className="wrong-note" role="alert">
          {state.message}
        </strong>
      ) : null}
    </div>
  );
}

/**
 * How far along the upload is.
 *
 * A percentage and a bar, not a spinner. A spinner says "something is
 * happening"; on a slow connection what somebody needs to know is whether it is
 * worth waiting, and only a number answers that.
 */
function Progress({
  name,
  sent,
  total,
}: {
  name: string;
  sent: number;
  total: number;
}) {
  const percent = total > 0 ? Math.round((sent / total) * 100) : 0;

  return (
    <div className="progress">
      <div
        className="progress-track"
        role="progressbar"
        aria-label={`Uploading ${name}`}
        aria-valuenow={percent}
        aria-valuemin={0}
        aria-valuemax={100}
      >
        <div className="progress-bar" style={{ width: `${percent}%` }} />
      </div>
      <p className="footnote">Uploading… {percent}%</p>
    </div>
  );
}

/**
 * Whether this is worth sending at all.
 *
 * Either signal is enough. A browser that reports no type for a perfectly good
 * PDF is common enough that requiring both would refuse real files, and the
 * bytes are checked properly on the other side regardless.
 */
function looksLikeAPdf(file: File): boolean {
  return (
    file.type === "application/pdf" || file.name.toLowerCase().endsWith(".pdf")
  );
}

function megabytes(bytes: number): string {
  return (bytes / (1024 * 1024)).toFixed(1);
}
