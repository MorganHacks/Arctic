"use client";

import { useEffect, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import type { ResumeOnFile } from "@/lib/api";

/**
 * Picking a file and replacing what we hold.
 *
 * Not a server action, and that is the one structural decision on this screen.
 * A server action would serialise the file into an action payload, which
 * carries a 1 MB body limit by default — well under the 5 MB the API accepts,
 * and it fails as an opaque framework error rather than as the sentence the
 * API would have given. Posting to `/api/portal/resume` instead goes through
 * the rewrite in next.config.ts, which streams the body on to harbor rather
 * than buffering it, and keeps the browser talking to one origin so the
 * SameSite=Lax session cookie is still sent. It is exactly what the
 * application form already does with its own upload.
 *
 * XMLHttpRequest rather than fetch for the same reason the application form
 * uses it: upload progress is the one thing fetch still cannot report, and a
 * five megabyte file on a phone without a progress bar looks like a page that
 * has frozen.
 *
 * Nothing here is a control. Every rule that decides whether this upload is
 * accepted — the size, the type, and whether the application is still open —
 * is checked again on the write, so somebody who edits this component out of
 * the page gets exactly the same refusals.
 */
export function ResumeUpload({
  resume,
  editable,
  maxBytes,
  accepts,
}: {
  resume: ResumeOnFile | null;
  editable: boolean;
  maxBytes: number;
  accepts: string;
}) {
  const router = useRouter();

  // Seeded from the server render and then owned here, so the row above the
  // picker changes the moment an upload lands rather than after a refresh
  // completes. The refresh still happens; this is what makes it invisible.
  const [onFile, setOnFile] = useState<ResumeOnFile | null>(resume);
  const [progress, setProgress] = useState<number | null>(null);
  const [problem, setProblem] = useState<string | null>(null);

  const request = useRef<XMLHttpRequest | null>(null);
  const input = useRef<HTMLInputElement>(null);

  // An upload still in flight when the page goes away is a request nobody is
  // waiting for. Left running it finishes into a component that no longer
  // exists and stores bytes no application will ever point at.
  useEffect(() => () => request.current?.abort(), []);

  function fail(message: string) {
    request.current = null;
    setProgress(null);
    setProblem(message);
  }

  function send(file: File) {
    // Both checks are courtesies and both are worth having. The size one saves
    // somebody pushing five megabytes up campus wifi to be told no at the end
    // of it; the type one catches the ordinary mistake of picking the Word
    // document instead of the PDF before it costs a round trip. Neither is a
    // limit — the API measures the bytes it actually receives.
    if (file.size > maxBytes) {
      /* COPY: needs sign-off. */
      fail(
        `That file is ${megabytes(file.size)} MB, and the limit is ` +
          `${megabytes(maxBytes)} MB. Export it again at a smaller size, or ` +
          "remove any images, and choose it once more.",
      );
      return;
    }

    if (!looksLikeAPdf(file, accepts)) {
      /* COPY: needs sign-off. */
      fail(
        "That file is not a PDF. Use “Export as PDF” or “Save as PDF” in " +
          "whatever you wrote it in, then choose the file it makes.",
      );
      return;
    }

    const body = new FormData();
    body.append("file", file);

    const xhr = new XMLHttpRequest();
    request.current = xhr;

    // Same origin, through the rewrite in next.config.ts. The session cookie
    // is SameSite=Lax and only travels because of that — pointed at the API's
    // own hostname this request would arrive signed out.
    xhr.open("POST", "/api/portal/resume");
    xhr.responseType = "json";

    xhr.upload.onprogress = (event) => {
      if (event.lengthComputable) {
        setProgress(Math.round((event.loaded / event.total) * 100));
      }
    };

    xhr.onload = () => {
      request.current = null;
      setProgress(null);

      // The session went while the page was open. Sending them back to sign in
      // is the only thing that helps, and it is the same thing every server
      // action on this app does with a 401.
      if (xhr.status === 401) {
        window.location.href = "/portal/sign-in";
        return;
      }

      const body = xhr.response as
        | { resume?: ResumeOnFile; error?: string }
        | null;

      if (xhr.status >= 200 && xhr.status < 300 && body?.resume) {
        setProblem(null);
        setOnFile(body.resume);

        // The server component above this holds the same answer and would
        // otherwise keep showing the old file until something else redrew it.
        router.refresh();

        // Without this the same file cannot be chosen again: the input still
        // holds it, so picking it a second time fires no change event at all.
        if (input.current) {
          input.current.value = "";
        }

        return;
      }

      // The API's own wording. It is the side that inspected the bytes and the
      // side that knows whether the application is still open, and a second
      // copy of either sentence over here would be a worse one that drifts.
      /* COPY: needs sign-off — the fallback only, not the API's sentences. */
      fail(body?.error ?? "That file could not be uploaded. Try choosing it again.");
    };

    /* COPY: needs sign-off. */
    xhr.onerror = () =>
      fail("The upload did not get through. Check your connection and try again.");

    xhr.onabort = () => {
      request.current = null;
      setProgress(null);
    };

    setProblem(null);
    setProgress(0);
    xhr.send(body);
  }

  function pick(file: File | undefined) {
    // A second pick replaces the first, so the one in flight is abandoned
    // rather than racing the new one to decide which file wins.
    request.current?.abort();

    if (file) {
      send(file);
    }
  }

  return (
    <div className="panel">
      {problem ? (
        <div className="notice problem">
          {/* Announced, because on a phone the picker is above the fold and
              the message is not. */}
          <p role="alert">{problem}</p>
        </div>
      ) : null}

      {onFile ? (
        <p className="upload__held" role="status">
          <strong>{onFile.filename}</strong>
          {onFile.size === null ? null : ` · ${megabytes(onFile.size)} MB`}
          {onFile.uploadedAt === null
            ? null
            : /* COPY: needs sign-off. */
              ` · added ${readable(onFile.uploadedAt)}`}
        </p>
      ) : (
        <p className="upload__held upload__held--none">
          {/* COPY: needs sign-off. */}
          We do not have a resume for you yet.
        </p>
      )}

      {editable ? (
        <div className="field upload">
          <label htmlFor="resume">
            {/* COPY: needs sign-off. */}
            {onFile ? "Replace it" : "Add one"}
          </label>

          <input
            ref={input}
            id="resume"
            name="resume"
            type="file"
            accept={`${accepts},.pdf`}
            disabled={progress !== null}
            /* The limits are part of the question, not a note beside it.
               Somebody hearing the page has to be told what will be accepted
               before they go looking for a file, not after one is refused. */
            aria-describedby="resume-limits"
            onChange={(e) => pick(e.target.files?.[0])}
          />

          <p className="hint" id="resume-limits">
            {/* COPY: needs sign-off. */}
            PDF, up to {megabytes(maxBytes)} MB. It uploads as soon as you
            choose it, and replaces whatever we have.
          </p>

          {progress === null ? null : <Progress percent={progress} />}
        </div>
      ) : null}
    </div>
  );
}

/**
 * How far along the upload is.
 *
 * A percentage and a bar, not a spinner. A spinner says "something is
 * happening"; on a slow connection what somebody needs to know is whether it
 * is worth waiting, and only a number answers that.
 */
function Progress({ percent }: { percent: number }) {
  return (
    <div className="progress">
      <div
        className="progress__track"
        role="progressbar"
        /* COPY: needs sign-off. */
        aria-label="Uploading your resume"
        aria-valuenow={percent}
        aria-valuemin={0}
        aria-valuemax={100}
      >
        <div className="progress__bar" style={{ width: `${percent}%` }} />
      </div>
      {/* COPY: needs sign-off. */}
      <p className="hint">Uploading… {percent}%</p>
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
function looksLikeAPdf(file: File, accepts: string): boolean {
  return file.type === accepts || file.name.toLowerCase().endsWith(".pdf");
}

function megabytes(bytes: number): string {
  return (bytes / (1024 * 1024)).toFixed(1);
}

/**
 * The day a file arrived.
 *
 * Formatted in UTC and to the day, like every other date in this app. A date
 * rendered on the client's clock mismatches on hydration, and the day is the
 * precision this actually carries — "you gave us this one in October" is the
 * question somebody is asking.
 */
function readable(iso: string): string {
  return new Date(iso).toLocaleDateString("en-US", {
    year: "numeric",
    month: "long",
    day: "numeric",
    timeZone: "UTC",
  });
}
