"use client";

import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState, useSyncExternalStore } from "react";
import { saveDraft } from "../actions";
import type { EditorDraft } from "./editor-history";
import { createDraftAutosave, encodeRecovery, readRecovery, recoveryAction, recoveryKey, type DraftRecovery } from "./draft-autosave";

export function useFormAutosave({ ownerId, formId, version, draft, revision, enabled, onRestore }: {
  ownerId: string;
  formId: string;
  version: number;
  draft: EditorDraft;
  revision: number;
  enabled: boolean;
  onRestore: (draft: EditorDraft) => void;
}) {
  const storage = recoveryKey(ownerId, formId, version);
  const saver = useMemo(() => createDraftAutosave({ draft, revision }, next => saveDraft(formId, next.fields, next.theme)), [storage, formId]);
  const state = useSyncExternalStore(saver.subscribe, saver.getSnapshot, saver.getSnapshot);
  const initialized = useRef<typeof saver | null>(null);
  const blocked = useRef(false);
  const retries = useRef(0);
  const [conflict, setConflict] = useState<DraftRecovery | null>(null);
  const [storageFailed, setStorageFailed] = useState(false);

  const remember = useCallback(() => {
    if (!enabled || blocked.current) return;
    try {
      if (saver.pending()) sessionStorage.setItem(storage, encodeRecovery(saver.getSnapshot()));
      else sessionStorage.removeItem(storage);
      setStorageFailed(false);
    } catch {
      setStorageFailed(true);
    }
  }, [enabled, saver, storage]);

  useLayoutEffect(() => {
    if (initialized.current !== saver) {
      initialized.current = saver;
      blocked.current = false;
      setConflict(null);
      if (enabled) {
        try {
          const recovery = readRecovery(sessionStorage.getItem(storage));
          if (recovery) {
            const action = recoveryAction(recovery, saver.getSnapshot().saved.draft);
            if (action === "restore") {
              saver.update(recovery.draft, saver.getSnapshot().latest.revision + 1);
              onRestore(recovery.draft);
            } else if (action === "conflict") {
              blocked.current = true;
              setConflict(recovery);
            }
          }
        } catch {
          setStorageFailed(true);
        }
      }
    }
    remember();
    return saver.subscribe(remember);
  }, [enabled, onRestore, remember, saver, storage]);

  useLayoutEffect(() => {
    if (enabled && !blocked.current) saver.update(draft, revision);
  }, [draft, enabled, revision, saver]);

  useEffect(() => {
    if (!enabled || conflict || !saver.pending()) return;
    const timer = setTimeout(() => { void saver.flush(); }, 700);
    return () => clearTimeout(timer);
  }, [enabled, conflict, revision, saver]);

  useEffect(() => {
    if (state.status === "saved") retries.current = 0;
    if (!enabled || conflict || state.status !== "failed" || !state.result.retryable) return;
    const delay = Math.min(2000 * 2 ** retries.current++, 30_000);
    const timer = setTimeout(() => { void saver.flush(); }, delay);
    return () => clearTimeout(timer);
  }, [enabled, conflict, saver, state.status, state.result]);

  useEffect(() => {
    if (!enabled) return;
    const flush = () => {
      if (!blocked.current && (saver.getSnapshot().status !== "failed" || saver.getSnapshot().result.retryable)) void saver.flush();
    };
    const beforeUnload = (event: BeforeUnloadEvent) => {
      if (!saver.pending() && !blocked.current) return;
      remember();
      flush();
      event.preventDefault();
      event.returnValue = "";
    };
    const visibility = () => {
      if (document.visibilityState === "hidden") { remember(); flush(); }
    };
    window.addEventListener("beforeunload", beforeUnload);
    window.addEventListener("online", flush);
    document.addEventListener("visibilitychange", visibility);
    return () => {
      window.removeEventListener("beforeunload", beforeUnload);
      window.removeEventListener("online", flush);
      document.removeEventListener("visibilitychange", visibility);
      flush();
    };
  }, [enabled, remember, saver]);

  const resolveRecovery = (restore: boolean) => {
    if (!conflict) return;
    blocked.current = false;
    setConflict(null);
    if (restore) {
      saver.update(conflict.draft, revision + 1);
      onRestore(conflict.draft);
    }
    remember();
  };

  return { status: state.status, result: state.result, conflict, resolveRecovery, storageFailed, save: saver.flush };
}
