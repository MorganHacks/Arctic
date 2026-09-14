"use client";

import { useEffect, useRef, useState } from "react";
import { previewBody } from "@/app/templates/actions";
import type { Rendered, Template, TemplateFormat } from "./types";

/** How long to wait after the last keystroke before rendering. */
const DEBOUNCE_MS = 600;

/**
 * The message on the right, kept in step with what is being typed.
 *
 * Its own hook because it depends on exactly three fields. As part of the
 * editor component it re-ran its effect when any field changed, so typing a
 * reply-to address scheduled a render of a body that had not moved.
 *
 * The rendering is the API's, never this file's. Two renderers agree until the
 * day somebody types the thing they disagree about, and the one that matters
 * is the one that sends.
 */
export type Preview = {
  rendered: Rendered | null;
  pending: boolean;
  error: string | null;
};

export function usePreview(
  template: Template | null,
  subject: string,
  body: string,
  format: TemplateFormat,
): Preview {
  /**
   * Seeded from what the API already rendered.
   *
   * An existing template arrives with its html and text on it, so the message
   * is drawn on the first paint rather than after a round trip that would
   * render exactly what the server already sent.
   */
  const [rendered, setRendered] = useState<Rendered | null>(
    template
      ? {
          subject: template.subject,
          html: template.html,
          text: template.text,
          notes: template.notes,
        }
      : null,
  );

  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  /**
   * The render this hook is waiting on.
   *
   * Debouncing makes overlapping renders rare rather than impossible. A slow
   * one and a fast one started after it can land out of order, and the older
   * answer would then be the email on screen. Only the newest may speak.
   */
  const attempt = useRef(0);

  useEffect(() => {
    if (body.trim() === "" && subject.trim() === "") {
      setRendered(null);
      setError(null);
      return;
    }

    const timer = setTimeout(() => {
      const mine = (attempt.current += 1);
      setPending(true);

      void previewBody({ subject, body, format }).then((result) => {
        if (mine !== attempt.current) {
          return;
        }

        setPending(false);

        if (result.ok) {
          setRendered(result.rendered);
          setError(null);
        } else {
          setError(result.error);
        }
      });
    }, DEBOUNCE_MS);

    return () => clearTimeout(timer);
  }, [subject, body, format]);

  return { rendered, pending, error };
}
