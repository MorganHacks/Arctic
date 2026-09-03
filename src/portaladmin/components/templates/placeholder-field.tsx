"use client";

import {
  useCallback,
  useId,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
  type KeyboardEvent,
  type RefObject,
} from "react";
import { insert, matching, triggerAt, type Trigger } from "./placeholders";
import styles from "./templates.module.css";
import type { Placeholder } from "./types";

/**
 * A field that answers `{{`.
 *
 * The subject and the body are the same problem — both are sent through the
 * placeholder renderer, both had to be typed from memory — so both get this
 * rather than the body getting it and the subject silently not, which reads as
 * a bug rather than as a decision.
 *
 * It is a combobox in the ARIA sense: focus never leaves the field, the menu
 * is a listbox the field owns, and which row is current is said through
 * `aria-activedescendant` rather than by moving focus into the list. That
 * matters more here than in a picker — this is a document being written, and a
 * menu that took focus would put the next keystroke somewhere the author did
 * not mean it to go.
 *
 * Combobox semantics are applied only when there is a list to offer. Where the
 * API could not be read the field is an ordinary textarea again, because
 * telling a screen reader "combobox" for a popup that can never open is a
 * worse lie than saying nothing.
 *
 * The one deliberate departure from the pattern: `role="combobox"` implies a
 * single line, and the body is not one. `aria-multiline` says so. Announcing
 * the body as one line would be the more damaging of the two compromises,
 * because it is wrong every second of the day rather than only while a menu is
 * open.
 */
export function PlaceholderField({
  id,
  value,
  onChange,
  available,
  multiline = false,
  className,
  spellCheck,
}: {
  id: string;
  value: string;
  onChange: (value: string) => void;
  /** What resolves. Null where the API could not say, which disables the menu. */
  available: Placeholder[] | null;
  multiline?: boolean;
  className?: string;
  spellCheck?: boolean;
}) {
  const listId = `${useId()}-placeholders`;

  const field = useRef<HTMLTextAreaElement | HTMLInputElement | null>(null);
  const menu = useRef<HTMLUListElement | null>(null);
  const wrap = useRef<HTMLDivElement | null>(null);

  const [trigger, setTrigger] = useState<Trigger | null>(null);
  const [point, setPoint] = useState<Point | null>(null);
  const [active, setActive] = useState(0);

  /*
   * The `{{` somebody pressed Escape on.
   *
   * Held by position rather than as a flag, so dismissing the menu dismisses
   * this one and not the feature: typing another `{{` further along opens it
   * again, and the author who wanted the popup gone stays rid of it for as
   * long as they are inside the one they were fighting.
   */
  const [dismissed, setDismissed] = useState<number | null>(null);

  /** Where the caret goes after React has written the inserted text back. */
  const pending = useRef<number | null>(null);

  /*
   * The trigger as it was last read.
   *
   * Kept so a re-read that finds the same `{{` and the same query can be
   * recognised as the same one. Every keystroke ends in a keyup, and a keyup
   * re-reads the field — without this, arrowing down to the second name and
   * letting go of the key put the highlight straight back on the first.
   */
  const last = useRef<Trigger | null>(null);

  const offered = available !== null && available.length > 0;

  const matches = useMemo(
    () =>
      trigger === null || available === null
        ? []
        : matching(available, trigger.query),
    [available, trigger],
  );

  const open =
    offered &&
    trigger !== null &&
    point !== null &&
    matches.length > 0 &&
    dismissed !== trigger.start;

  /**
   * Re-reads where the caret is and what it is inside.
   *
   * Called on every keystroke, every click into the text and every scroll of
   * the field, because all three can move the caret out of a `{{` or move the
   * `{{` out from under the menu. A selection that spans characters is not a
   * caret and never opens anything.
   */
  const sync = useCallback(() => {
    const element = field.current;
    if (!element || !offered) {
      return;
    }

    const caret = element.selectionStart ?? 0;
    const found =
      element.selectionEnd === caret ? triggerAt(element.value, caret) : null;

    const same =
      found !== null &&
      last.current !== null &&
      last.current.start === found.start &&
      last.current.query === found.query;

    // Only a genuinely different `{{` — or a different thing typed into the
    // same one — sends the highlight back to the top. Anything else would undo
    // the arrow key that was just pressed.
    if (!same) {
      setActive(0);
      last.current = found;
    }

    setTrigger(last.current);
    setPoint(found === null ? null : caretPoint(element, caret));

    if (found === null || (dismissed !== null && found.start !== dismissed)) {
      setDismissed(null);
    }
  }, [dismissed, offered]);

  /*
   * Where the menu is drawn, decided after it has been measured.
   *
   * Under the caret's line rather than over it. The whole complaint was about
   * not knowing the names, and a menu that hides the sentence being written
   * while it lists them has traded one blindness for another. Above only when
   * there is genuinely no room below and there is room above; otherwise below
   * and let it scroll, because a menu that jumps sides as the field fills up
   * is harder to use than one that is occasionally cramped.
   */
  useLayoutEffect(() => {
    const list = menu.current;
    const box = wrap.current;

    if (!open || !list || !box || point === null) {
      return;
    }

    const gap = 4;
    const height = list.offsetHeight;
    const below = point.top + point.line + gap;
    const above = point.top - height - gap;

    const roomBelow =
      box.getBoundingClientRect().top + below + height <= window.innerHeight - 8;

    list.style.top = `${!roomBelow && above >= 0 ? above : below}px`;
    list.style.left = `${Math.max(
      0,
      Math.min(point.left, box.clientWidth - list.offsetWidth),
    )}px`;
  }, [open, point, matches.length]);

  /* The current row kept in view without the list ever taking focus. */
  useLayoutEffect(() => {
    if (!open) {
      return;
    }

    menu.current?.children[active]?.scrollIntoView({ block: "nearest" });
  }, [active, open]);

  /*
   * React owns the value, so the caret has to be put back after it is written.
   * Without this the caret lands at the end of the body every time somebody
   * inserts a name into the middle of a paragraph.
   */
  useLayoutEffect(() => {
    if (pending.current === null) {
      return;
    }

    const at = pending.current;
    pending.current = null;

    field.current?.focus();
    field.current?.setSelectionRange(at, at);
  });

  function accept(choice: Placeholder) {
    const element = field.current;
    if (!element || trigger === null) {
      return;
    }

    const written = insert(
      element.value,
      trigger,
      element.selectionStart ?? 0,
      choice.name,
    );

    pending.current = written.caret;
    last.current = null;
    setTrigger(null);
    setPoint(null);
    setDismissed(null);
    onChange(written.value);
  }

  /**
   * The keys the menu takes, and only while it is showing.
   *
   * Everything else falls through untouched, which is the point: Enter is a
   * new paragraph, the arrows walk the text, and Escape belongs to whatever
   * else on the page wants it. The menu borrows five keys for as long as it is
   * open and gives them all back the moment it is not.
   */
  function onKeyDown(event: KeyboardEvent) {
    if (!open || trigger === null) {
      return;
    }

    if (event.key === "ArrowDown") {
      event.preventDefault();
      setActive((at) => (at + 1) % matches.length);
      return;
    }

    if (event.key === "ArrowUp") {
      event.preventDefault();
      setActive((at) => (at - 1 + matches.length) % matches.length);
      return;
    }

    if (event.key === "Enter") {
      event.preventDefault();
      accept(matches[active]);
      return;
    }

    if (event.key === "Escape") {
      event.preventDefault();
      event.stopPropagation();
      setDismissed(trigger.start);
    }
  }

  /*
   * The combobox half of the field, and nothing when there is no list.
   *
   * Spread rather than written twice: the subject is an input and the body is
   * a textarea, and two hand-kept copies of seven ARIA attributes is how one
   * of them ends up missing `aria-expanded` and announcing nothing.
   */
  const combobox = offered
    ? {
        role: "combobox",
        "aria-expanded": open,
        "aria-controls": listId,
        "aria-haspopup": "listbox" as const,
        "aria-autocomplete": "list" as const,
        "aria-multiline": multiline ? true : undefined,
        "aria-activedescendant": open ? optionId(listId, active) : undefined,
      }
    : {};

  const shared = {
    id,
    value,
    className,
    spellCheck,
    autoComplete: "off",
    onKeyUp: sync,
    onSelect: sync,
    onScroll: sync,
    onFocus: sync,
    onKeyDown,
    onBlur: () => {
      setTrigger(null);
      setPoint(null);
    },
    ...combobox,
  };

  function typed(next: string) {
    onChange(next);
    sync();
  }

  return (
    <div className={styles.completing} ref={wrap}>
      {multiline ? (
        <textarea
          {...shared}
          ref={field as RefObject<HTMLTextAreaElement | null>}
          onChange={(event) => typed(event.target.value)}
        />
      ) : (
        <input
          {...shared}
          type="text"
          ref={field as RefObject<HTMLInputElement | null>}
          onChange={(event) => typed(event.target.value)}
        />
      )}

      {/* In the tree whenever there is a list, so `aria-controls` always
          resolves to something, and hidden rather than unmounted so the menu
          does not have to be rebuilt between keystrokes. */}
      {offered ? (
        <ul
          id={listId}
          ref={menu}
          role="listbox"
          aria-label="Placeholders"
          className={styles.menu}
          hidden={!open}
        >
          {open
            ? matches.map((placeholder, index) => (
                <li
                  key={placeholder.name}
                  id={optionId(listId, index)}
                  role="option"
                  aria-selected={index === active}
                  className={
                    index === active
                      ? `${styles.option} ${styles.current}`
                      : styles.option
                  }
                  /* The field must not lose focus to a click on a row, or the
                     blur closes the menu before the click can land. */
                  onMouseDown={(event) => event.preventDefault()}
                  onMouseMove={() => setActive(index)}
                  onClick={() => accept(placeholder)}
                >
                  <span className={styles.name}>{placeholder.name}</span>
                  {placeholder.description ? (
                    <span className={styles.about}>
                      {placeholder.description}
                    </span>
                  ) : null}
                </li>
              ))
            : null}
        </ul>
      ) : null}
    </div>
  );
}

function optionId(listId: string, index: number): string {
  return `${listId}-${index}`;
}

/** Where the caret is, in pixels down and across the field's own box. */
type Point = { left: number; top: number; line: number };

/*
 * The properties that decide where a character lands.
 *
 * Anything that moves text has to be copied or the measurement is of a
 * different paragraph than the one on screen — the font, the spacing, the
 * padding the first line starts after, and the width the wrap happens at.
 */
const MIRRORED = [
  "box-sizing",
  "padding-top",
  "padding-right",
  "padding-bottom",
  "padding-left",
  "border-top-width",
  "border-right-width",
  "border-bottom-width",
  "border-left-width",
  "font-family",
  "font-size",
  "font-weight",
  "font-style",
  "font-variant",
  "letter-spacing",
  "line-height",
  "text-indent",
  "text-transform",
  "word-spacing",
  "tab-size",
];

/**
 * Where the caret is drawn, measured rather than guessed.
 *
 * A textarea will not say where its caret is, so the text is laid out a second
 * time in a hidden div wearing the field's own typography, with a marker at
 * the caret, and the marker is asked where it ended up. It is the long-known
 * way to do this and there is no short one: any arithmetic on line counts is
 * wrong the first time a line wraps.
 *
 * The field's own scroll is taken off at the end, so the answer is where the
 * caret appears rather than where it is in the document — a caret scrolled out
 * of sight gives a negative top, and the menu is placed against a caret nobody
 * can see. It resolves itself on the next keystroke, which scrolls the caret
 * back into view.
 */
function caretPoint(
  element: HTMLTextAreaElement | HTMLInputElement,
  index: number,
): Point {
  const computed = window.getComputedStyle(element);
  const multiline = element instanceof HTMLTextAreaElement;

  const mirror = document.createElement("div");
  mirror.style.position = "absolute";
  mirror.style.top = "0";
  mirror.style.left = "-9999px";
  mirror.style.visibility = "hidden";
  mirror.style.height = "auto";

  for (const property of MIRRORED) {
    mirror.style.setProperty(property, computed.getPropertyValue(property));
  }

  if (multiline) {
    mirror.style.width = computed.width;
    mirror.style.whiteSpace = "pre-wrap";
    mirror.style.overflowWrap = "break-word";
  } else {
    // A single-line field scrolls rather than wraps, so the mirror must be
    // free to grow sideways or every long value measures as wrapped.
    mirror.style.whiteSpace = "pre";
  }

  mirror.textContent = element.value.slice(0, index);

  const marker = document.createElement("span");
  // Something has to be in it or an empty inline box has no position. The rest
  // of the value is used where there is any, so the marker sits on the line
  // the caret is actually on rather than on a line of its own.
  marker.textContent = element.value.slice(index) || ".";
  mirror.appendChild(marker);

  document.body.appendChild(mirror);

  // offsetTop is measured from the mirror's padding edge, so the border has to
  // be added back to get a position inside the field's border box — which is
  // the box the menu is positioned against.
  const point: Point = {
    left:
      marker.offsetLeft +
      number(computed.borderLeftWidth) -
      element.scrollLeft,
    top: marker.offsetTop + number(computed.borderTopWidth) - element.scrollTop,
    line: number(computed.lineHeight) || number(computed.fontSize) * 1.3,
  };

  mirror.remove();

  return point;
}

/** A computed length as a number. `normal` and the like come back as 0. */
function number(value: string): number {
  const parsed = Number.parseFloat(value);
  return Number.isFinite(parsed) ? parsed : 0;
}
