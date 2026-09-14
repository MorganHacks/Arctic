"use client";

import { useRouter } from "next/navigation";
import { useState, useTransition } from "react";
import { addTemplate, editTemplate } from "@/app/templates/actions";
import type { Template, TemplateDraft } from "./types";

/**
 * Writing a version, and the two different things that means.
 *
 * Creating navigates: everything after a create — the version number, the key
 * it answers to — belongs to the page that edits it. Editing stays put and
 * says what happened, because the author is mid-sentence and being moved would
 * lose their place.
 *
 * Separate from the draft because it depends on all of it at once and on none
 * of it while idle. Held in the editor component, every keystroke re-created
 * the closure that saves.
 */
export type SaveHandle = {
  saving: boolean;
  /** Whether the confirmation is showing. Only an edit asks. */
  asked: boolean;
  ask: (asked: boolean) => void;
  outcome: { ok: boolean; text: string } | null;
  save: () => void;
};

export function useSave(
  template: Template | null,
  toRequest: () => TemplateDraft,
): SaveHandle {
  const router = useRouter();
  const [asked, setAsked] = useState(false);
  const [outcome, setOutcome] = useState<{ ok: boolean; text: string } | null>(
    null,
  );
  const [saving, startSaving] = useTransition();

  function save() {
    setOutcome(null);

    startSaving(async () => {
      const result = template
        ? await editTemplate(template.key, toRequest())
        : await addTemplate(toRequest());

      setAsked(false);

      if (!result.ok) {
        setOutcome({ ok: false, text: result.error });
        return;
      }

      if (!template) {
        router.push(`/templates/${encodeURIComponent(result.key)}`);
        router.refresh();
        return;
      }

      setOutcome({
        ok: true,
        text: [
          result.version === null
            ? "Saved."
            : `Saved. Now version ${result.version}.`,
          result.note,
        ]
          .filter(Boolean)
          .join(" "),
      });
      router.refresh();
    });
  }

  return { saving, asked, ask: setAsked, outcome, save };
}
