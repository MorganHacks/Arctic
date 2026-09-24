"use client";

import { useRouter } from "next/navigation";
import { useEffect, useRef, useState, useTransition } from "react";
import { addTemplate, autosaveTemplateDraft, discardTemplateDraft, editTemplate, saveTemplateSettings, type SaveResult } from "@/app/templates/actions";
import type { DraftHandle } from "./use-draft";
import { draftFingerprint as fingerprint } from "./draft-recovery";
import type { EditorStep, Template, TemplateDraft } from "./types";
import { validateDesign, validateSettings, type TemplateFieldErrors } from "./validation";

export function useSave(
  template: Template | null,
  handle: DraftHandle,
  canManage: boolean,
  onStep: (step: EditorStep) => void,
) {
  const router = useRouter();
  const [asked, setAsked] = useState(false);
  const [outcome, setOutcome] = useState<{ ok: boolean; text: string; conflict?: boolean } | null>(null);
  const [saving, startSaving] = useTransition();
  const [, startAutosaving] = useTransition();
  const [fieldErrors, setFieldErrors] = useState<TemplateFieldErrors>({});
  const [validationAttempt, setValidationAttempt] = useState(0);
  const [settingsComplete, setSettingsComplete] = useState(Boolean(template?.settingsComplete));
  const [designComplete, setDesignComplete] = useState(Boolean(template?.settingsComplete && template?.designComplete && !template.hasDraft));
  const [key, setKey] = useState(template?.key ?? null);
  const [version, setVersion] = useState(template?.version ?? 0);
  const request = handle.toRequest();
  const signature = fingerprint(request);
  const lastSaved = useRef(signature);
  const active = useRef<Promise<SaveResult> | null>(null);
  const manualPending = useRef(false);
  const revision = useRef(0);
  const mounted = useRef(true);
  const latest = useRef({ handle, request, signature });
  latest.current = { handle, request, signature };

  useEffect(() => {
    mounted.current = true;
    return () => { mounted.current = false; revision.current++; };
  }, []);

  useEffect(() => {
    if (!outcome?.ok) return;
    const timeout = window.setTimeout(() => setOutcome(null), 3000);
    return () => window.clearTimeout(timeout);
  }, [outcome]);

  useEffect(() => {
    const ticket = ++revision.current;
    if (!canManage || !handle.ready || saving || asked) return;
    if (handle.recoveryConflict) {
      setOutcome({ ok: false, conflict: true, text: "A newer template version is available. Your local edits have been restored. Copy any changes you want to keep before discarding this draft." });
      return;
    }
    if (signature === lastSaved.current) return;
    const timeout = window.setTimeout(() => {
      startAutosaving(async () => {
        if (active.current) await active.current;
        if (!mounted.current || manualPending.current || ticket !== revision.current) return;
        const current = latest.current;
        const draft = current.request;
        if (Object.keys(validateSettings(draft)).length) {
          setOutcome(current.handle.storageFailed
            ? { ok: false, text: "Your browser could not keep a recovery copy. Complete the required fields and save your draft." }
            : { ok: true, text: "Draft saved in this tab." });
          return;
        }
        const pending = autosaveTemplateDraft(draft).catch((): SaveResult => ({ ok: false, error: "Autosave could not reach the server." }));
        active.current = pending;
        const result = await pending;
        if (active.current === pending) active.current = null;
        if (!mounted.current) return;
        if (result.ok) {
          lastSaved.current = fingerprint(draft);
          latest.current.handle.recordSaved(result.key, result.version ?? version, draft);
          setKey(result.key);
          setVersion(result.version ?? version);
          setSettingsComplete(Object.keys(validateSettings(latest.current.request)).length === 0);
          setDesignComplete(false);
          if (latest.current.signature === lastSaved.current) setOutcome({ ok: true, text: "Changes autosaved." });
        } else {
          setOutcome({ ok: false, conflict: result.conflict, text: `${result.error}${latest.current.handle.storageFailed ? "" : " Your latest edits are kept in this tab."}` });
        }
      });
    }, 900);
    return () => window.clearTimeout(timeout);
  }, [signature, handle.ready, handle.recoveryConflict, handle.storageFailed, canManage, saving, asked]);

  function validate(stage: EditorStep) {
    if (!handle.ready || !canManage) return null;
    if (handle.recoveryConflict) {
      setOutcome({ ok: false, conflict: true, text: "A newer template version is available. Copy any local changes you want to keep before discarding this draft." });
      return null;
    }
    const draft = { ...handle.toRequest(), key: key ?? handle.toRequest().key };
    const settingsErrors = validateSettings(draft);
    const errors = { ...settingsErrors, ...(stage === "design" ? validateDesign(draft) : {}) };
    setFieldErrors(errors);
    if (Object.keys(errors).length) {
      if (Object.keys(settingsErrors).length) onStep("settings");
      setOutcome(null);
      setAsked(false);
      setValidationAttempt((attempt) => attempt + 1);
      return null;
    }
    return draft;
  }

  function move(savedKey: string, next: EditorStep) {
    if (!template) {
      handle.clearRecovery();
      const query = new URLSearchParams(window.location.search);
      query.set("step", next);
      router.push(`/templates/${encodeURIComponent(savedKey)}?${query.toString()}`);
    } else {
      onStep(next);
      router.refresh();
    }
  }

  function persist(draft: TemplateDraft, stage: "settings" | "design", advance = true) {
    manualPending.current = true;
    revision.current++;
    setOutcome(null);
    startSaving(async () => {
      try {
        if (active.current) await active.current;
        const result = stage === "settings"
          ? await saveTemplateSettings(draft)
          : key && version > 0
            ? await editTemplate(key, draft)
            : await addTemplate(draft);
        setAsked(false);

        if (!result.ok) {
          if (result.fieldErrors) {
            setFieldErrors(result.fieldErrors);
            if (Object.keys(result.fieldErrors).some((field) => field !== "body")) onStep("settings");
            setValidationAttempt((attempt) => attempt + 1);
          } else if (stage === "design" && /body|render/i.test(result.error)) {
            setFieldErrors({ body: result.error });
            setValidationAttempt((attempt) => attempt + 1);
          } else setOutcome({ ok: false, text: result.error, conflict: result.conflict });
          return;
        }

        if (result.name !== null) handle.set("name", result.name);
        handle.recordSaved(result.key, result.version ?? version, { ...draft, name: result.name ?? draft.name });
        lastSaved.current = fingerprint({ ...draft, name: result.name ?? draft.name });
        setKey(result.key);
        setVersion(result.version ?? version);
        setSettingsComplete(true);
        setDesignComplete(stage === "design");
        setFieldErrors({});
        if (advance) move(result.key, stage === "settings" ? "design" : "api");
        else {
          setOutcome({ ok: true, text: "Changes saved." });
          router.refresh();
        }
      } catch {
        setOutcome({ ok: false, text: "Your changes could not be saved to the server. Try again." });
      } finally {
        manualPending.current = false;
      }
    });
  }

  function requestSave(step: EditorStep) {
    if (step === "api") {
      router.push("/templates");
      return;
    }
    const draft = validate(step);
    if (!draft) return;
    setOutcome(null);
    if (step === "design" && version > 0) setAsked(true);
    else persist(draft, step);
  }

  function save() {
    const draft = validate("design");
    if (draft) persist(draft, "design");
  }

  function saveDraft() {
    const draft = validate("settings");
    if (draft) persist(draft, "settings", false);
  }

  function clearFieldError(field: keyof TemplateDraft) {
    setFieldErrors((errors) => {
      if (!errors[field]) return errors;
      const next = { ...errors };
      delete next[field];
      return next;
    });
    setOutcome(null);
    setDesignComplete(false);
    if (field !== "body" && field !== "format") setSettingsComplete(false);
  }

  function navigate(next: EditorStep) {
    if (saving) return;
    setAsked(false);
    setOutcome(null);
    onStep(next);
  }

  function reloadLatest() {
    if (!key) return;
    manualPending.current = true;
    revision.current++;
    startSaving(async () => {
      try {
        if (active.current) await active.current;
        const result = await discardTemplateDraft(key);
        if (result.ok) {
          handle.clearRecovery();
          window.location.assign(`/templates/${encodeURIComponent(key)}`);
        }
        else setOutcome({ ok: false, text: result.error, conflict: true });
      } finally {
        manualPending.current = false;
      }
    });
  }

  return { saving, asked, ask: setAsked, outcome, fieldErrors, validationAttempt, clearFieldError,
    requestSave, save, saveDraft, navigate, reloadLatest, settingsComplete, designComplete, key, version };
}
