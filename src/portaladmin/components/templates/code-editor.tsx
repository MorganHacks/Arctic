"use client";

import { forwardRef, useEffect, useImperativeHandle, useLayoutEffect, useRef } from "react";
import { Annotation, Compartment, EditorState } from "@codemirror/state";
import { drawSelection, EditorView, highlightActiveLine, highlightActiveLineGutter, keymap, lineNumbers, placeholder } from "@codemirror/view";
import { defaultKeymap, history, historyKeymap } from "@codemirror/commands";
import { bracketMatching, HighlightStyle, indentOnInput, syntaxHighlighting } from "@codemirror/language";
import { html } from "@codemirror/lang-html";
import { markdown } from "@codemirror/lang-markdown";
import { autocompletion, closeBrackets, closeBracketsKeymap, completionKeymap, type CompletionContext } from "@codemirror/autocomplete";
import { tags } from "@lezer/highlight";
import type { Placeholder, TemplateFormat } from "./types";
import styles from "./design-workspace.module.css";

export type CodeEditorHandle = { insert: (text: string) => boolean; focus: () => void };

const externalChange = Annotation.define<boolean>();
const colors = HighlightStyle.define([
  { tag: [tags.tagName, tags.keyword], color: "#c5621b" },
  { tag: [tags.attributeName, tags.propertyName, tags.function(tags.variableName)], color: "#0073e6" },
  { tag: [tags.string, tags.attributeValue], color: "#27823b" },
  { tag: [tags.number, tags.bool, tags.typeName, tags.className], color: "#9463ce" },
  { tag: [tags.punctuation, tags.angleBracket, tags.operator], color: "#777771" },
  { tag: tags.comment, color: "#94948a", fontStyle: "italic" },
  { tag: tags.heading, color: "#c5621b", fontWeight: "600" },
  { tag: tags.strong, fontWeight: "700" },
  { tag: tags.emphasis, fontStyle: "italic" },
  { tag: tags.link, color: "#0073e6", textDecoration: "underline" },
]);

const theme = EditorView.theme({
  "&": { height: "100%", backgroundColor: "#fff", color: "#262624", fontSize: "13px" },
  "&.cm-focused": { outline: "none" },
  ".cm-scroller": { fontFamily: "var(--mono)", lineHeight: "1.8", overflow: "auto" },
  ".cm-content": { padding: "18px 0 32px", caretColor: "var(--accent)" },
  ".cm-line": { padding: "0 22px 0 12px" },
  ".cm-gutters": { backgroundColor: "#fff", color: "#aaa9a3", border: "none", padding: "0 5px 0 12px", fontSize: "12px" },
  ".cm-lineNumbers .cm-gutterElement": { minWidth: "28px", padding: "0 6px" },
  ".cm-activeLine, .cm-activeLineGutter": { backgroundColor: "#f6f6f3" },
  ".cm-activeLineGutter": { color: "#60605b" },
  ".cm-cursor, .cm-dropCursor": { borderLeftColor: "var(--accent)" },
  "&.cm-focused .cm-selectionBackground, .cm-selectionBackground, .cm-content ::selection": { backgroundColor: "#dfe7ff" },
  ".cm-matchingBracket": { backgroundColor: "#e9edfa", outline: "1px solid #cbd4f0" },
  ".cm-placeholder": { color: "#a09f99" },
  ".cm-tooltip": { border: "1px solid var(--line)", borderRadius: "10px", backgroundColor: "var(--raised)", boxShadow: "0 8px 24px #18181b12", overflow: "hidden" },
  ".cm-tooltip-autocomplete ul": { fontFamily: "var(--mono)", fontSize: "12px" },
  ".cm-tooltip-autocomplete ul li": { padding: "5px 10px" },
  ".cm-tooltip-autocomplete ul li[aria-selected]": { backgroundColor: "var(--accent-soft)", color: "var(--accent)" },
});

export const CodeEditor = forwardRef<CodeEditorHandle, {
  value: string;
  format: TemplateFormat;
  available: Placeholder[] | null;
  onChange: (value: string) => void;
  disabled: boolean;
  invalid: boolean;
}>(function CodeEditor({ value, format, available, onChange, disabled, invalid }, ref) {
  const host = useRef<HTMLDivElement>(null);
  const view = useRef<EditorView | null>(null);
  const language = useRef(new Compartment());
  const access = useRef(new Compartment());
  const current = useRef({ value, format, available, onChange, disabled, invalid });

  useLayoutEffect(() => { current.current = { value, format, available, onChange, disabled, invalid }; });

  useImperativeHandle(ref, () => ({
    insert(text) {
      const editor = view.current;
      if (!editor || current.current.disabled) return false;
      editor.dispatch(editor.state.replaceSelection(text), { scrollIntoView: true });
      editor.focus();
      return true;
    },
    focus() { view.current?.focus(); },
  }), []);

  useLayoutEffect(() => {
    if (!host.current) return;
    function completeFields(context: CompletionContext) {
      const match = context.matchBefore(/\{\{[\w.]*$/);
      if (!match || !current.current.available?.length) return null;
      const closing = context.state.sliceDoc(context.pos, context.pos + 2) === "}}";
      return {
        from: match.from + 2,
        options: current.current.available.map((field) => ({ label: field.name, type: "variable", info: field.description ?? undefined,
          apply: field.name + (closing ? "" : "}}") })),
        validFor: /^[\w.]*$/,
      };
    }
    const editor = new EditorView({
      parent: host.current,
      state: EditorState.create({
        doc: current.current.value,
        extensions: [
          lineNumbers(), highlightActiveLineGutter(), history(), drawSelection(), indentOnInput(), bracketMatching(), closeBrackets(),
          highlightActiveLine(), autocompletion(), theme, syntaxHighlighting(colors),
          placeholder("Start writing your email…"),
          keymap.of([...closeBracketsKeymap, ...completionKeymap, ...defaultKeymap, ...historyKeymap]),
          EditorState.languageData.of(() => [{ autocomplete: completeFields }]),
          language.current.of(current.current.format === "html" ? html() : markdown()),
          access.current.of(accessibility(current.current.disabled, current.current.invalid)),
          EditorView.updateListener.of((update) => {
            if (update.docChanged && !update.transactions.some((transaction) => transaction.annotation(externalChange)))
              current.current.onChange(update.state.doc.toString());
          }),
        ],
      }),
    });
    view.current = editor;
    return () => { editor.destroy(); view.current = null; };
  }, []);

  useEffect(() => {
    const editor = view.current;
    if (editor && value !== editor.state.doc.toString())
      editor.dispatch({ changes: { from: 0, to: editor.state.doc.length, insert: value }, annotations: externalChange.of(true) });
  }, [value]);

  useEffect(() => { view.current?.dispatch({ effects: language.current.reconfigure(format === "html" ? html() : markdown()) }); }, [format]);
  useEffect(() => { view.current?.dispatch({ effects: access.current.reconfigure(accessibility(disabled, invalid)) }); }, [disabled, invalid]);

  return <div ref={host} className={styles.codeEditor} />;
});

function accessibility(disabled: boolean, invalid: boolean) {
  return [EditorState.readOnly.of(disabled), EditorView.editable.of(!disabled), EditorView.contentAttributes.of({
    "aria-label": "Email code", "aria-invalid": String(invalid),
    ...(invalid ? { "aria-describedby": "template-body-error" } : {}),
    spellcheck: "false",
  })];
}
