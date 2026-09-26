"use client";

import { ErrorToast } from "@/components/ui/error-toast";

import { useId, useLayoutEffect, useRef, useState, type KeyboardEvent } from "react";
import { ArrowDown01Icon, ArrowLeft01Icon, ArrowRight01Icon, Calendar03Icon, Clock01Icon, PencilEdit02Icon, Tick02Icon } from "@hugeicons/core-free-icons";
import { Icon } from "./icon";
import { fromLocalInput, toLocalInput } from "../events/zone";
import styles from "./date-time-picker.module.css";

const months = Array.from({ length: 12 }, (_, month) => new Intl.DateTimeFormat("en-US", { month: "long", timeZone: "UTC" }).format(new Date(Date.UTC(2026, month, 1))));
const weekdays = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
const dayFormat = new Intl.DateTimeFormat("en-US", { weekday: "long", year: "numeric", month: "long", day: "numeric", timeZone: "UTC" });
const displayFormat = new Intl.DateTimeFormat("en-US", { year: "numeric", month: "short", day: "numeric", hour: "numeric", minute: "2-digit", timeZone: "UTC" });
function dateOf(year: number, month: number, day = 1) {
  const date = new Date(0);
  date.setUTCFullYear(year, month, day);
  date.setUTCHours(0, 0, 0, 0);
  return date;
}
function dateKey(date: Date) { return date.toISOString().slice(0, 10); }

export function DateTimePicker({ id, label, value, onChange, disabled, describedBy, min }: {
  id: string; label: string; value: string; onChange: (value: string) => void;
  disabled?: boolean; describedBy?: string; min?: string;
}) {
  const popoverId = useId();
  const trigger = useRef<HTMLButtonElement>(null);
  const panel = useRef<HTMLDivElement>(null);
  const grid = useRef<HTMLDivElement>(null);
  const hourInput = useRef<HTMLInputElement>(null);
  const minuteInput = useRef<HTMLInputElement>(null);
  const editField = useRef<"hour" | "minute">("hour");
  const timeToggle = useRef<HTMLButtonElement>(null);
  const returnToTime = useRef(false);
  const [open, setOpen] = useState(false);
  const [editingTime, setEditingTime] = useState(false);
  const [monthView, setMonthView] = useState(false);
  const [month, setMonth] = useState(0);
  const [year, setYear] = useState(2026);
  const [day, setDay] = useState("");
  const [focusDay, setFocusDay] = useState("");
  const [hour, setHour] = useState("09");
  const [minute, setMinute] = useState("00");
  const [period, setPeriod] = useState("AM");
  const [today, setToday] = useState("");
  const hours = Number(hour);
  const minutes = Number(minute);
  const timeValid = /^\d{1,2}$/.test(hour) && /^\d{1,2}$/.test(minute) && hours >= 1 && hours <= 12 && minutes >= 0 && minutes <= 59;
  const draft = `${day}T${String(hours % 12 + (period === "PM" ? 12 : 0)).padStart(2, "0")}:${minute.padStart(2, "0")}`;
  const instant = timeValid && day ? fromLocalInput(draft) : null;
  const valid = !!instant && toLocalInput(instant) === draft && (!min || draft >= min);
  const first = dateOf(year, month);
  const weekCount = Math.ceil((first.getUTCDay() + dateOf(year, month + 1, 0).getUTCDate()) / 7);
  const days = Array.from({ length: weekCount * 7 }, (_, i) => dateOf(year, month, i - first.getUTCDay() + 1));
  const displayDate = value ? new Date(`${value}:00Z`) : null;
  const display = displayDate && !Number.isNaN(displayDate.getTime()) ? displayFormat.format(displayDate) : "Choose date and time";

  function begin() {
    const now = toLocalInput(new Date().toISOString());
    const initial = value || `${now.slice(0, 10)}T09:00`;
    const selected = initial.slice(0, 10);
    setToday(now.slice(0, 10));
    setDay(selected);
    setFocusDay(selected);
    setYear(Number(selected.slice(0, 4)));
    setMonth(Number(selected.slice(5, 7)) - 1);
    const initialHour = Number(initial.slice(11, 13));
    setHour(String(initialHour % 12 || 12).padStart(2, "0"));
    setMinute(initial.slice(14, 16));
    setPeriod(initialHour >= 12 ? "PM" : "AM");
    setMonthView(false);
    setEditingTime(false);
  }

  useLayoutEffect(() => {
    if (!open) return;
    function position(event?: Event) {
      if (!trigger.current || !panel.current) return;
      if (event?.target === panel.current) return;
      const rect = trigger.current.getBoundingClientRect();
      const viewport = window.visualViewport;
      const left = viewport?.offsetLeft ?? 0;
      const top = viewport?.offsetTop ?? 0;
      const width = viewport?.width ?? window.innerWidth;
      const height = viewport?.height ?? window.innerHeight;
      const popup = panel.current;
      popup.style.width = `${Math.min(296, width - 24)}px`;
      const fullHeight = popup.scrollHeight + popup.offsetHeight - popup.clientHeight;
      const below = Math.max(0, top + height - rect.bottom - 20);
      const above = Math.max(0, rect.top - top - 20);
      const placeBelow = below >= fullHeight || below >= above;
      popup.style.maxHeight = `${placeBelow ? below : above}px`;
      popup.style.left = `${Math.max(left + 12, Math.min(rect.right - popup.offsetWidth, left + width - popup.offsetWidth - 12))}px`;
      popup.style.top = `${placeBelow ? rect.bottom + 8 : rect.top - popup.offsetHeight - 8}px`;
      popup.dataset.placement = placeBelow ? "bottom" : "top";
    }
    position();
    window.addEventListener("resize", position);
    window.addEventListener("scroll", position, true);
    window.visualViewport?.addEventListener("resize", position);
    window.visualViewport?.addEventListener("scroll", position);
    return () => {
      window.removeEventListener("resize", position);
      window.removeEventListener("scroll", position, true);
      window.visualViewport?.removeEventListener("resize", position);
      window.visualViewport?.removeEventListener("scroll", position);
    };
  }, [open, monthView, weekCount, valid]);

  useLayoutEffect(() => {
    if (editingTime) {
      const input = editField.current === "hour" ? hourInput.current : minuteInput.current;
      input?.focus({ preventScroll: true }); input?.select();
    } else if (returnToTime.current) {
      timeToggle.current?.focus({ preventScroll: true });
      returnToTime.current = false;
    }
  }, [editingTime]);

  useLayoutEffect(() => {
    if (open && !monthView) grid.current?.querySelector<HTMLButtonElement>(`[data-day="${focusDay}"]`)?.focus({ preventScroll: true });
  }, [open, monthView, focusDay, month, year]);

  function moveMonth(amount: number) {
    const next = dateOf(year, month + amount);
    if (next.getUTCFullYear() < 1 || next.getUTCFullYear() > 9999) return;
    setYear(next.getUTCFullYear());
    setMonth(next.getUTCMonth());
    setFocusDay(dateKey(next));
  }
  function chooseDay(date: Date) {
    const key = dateKey(date);
    setDay(key); setFocusDay(key); setYear(date.getUTCFullYear()); setMonth(date.getUTCMonth());
  }
  function navigateDay(event: KeyboardEvent<HTMLButtonElement>, date: Date) {
    const offsets: Record<string, number> = { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -7, ArrowDown: 7, Home: -date.getUTCDay(), End: 6 - date.getUTCDay() };
    if (event.key in offsets) {
      event.preventDefault();
      const next = dateOf(date.getUTCFullYear(), date.getUTCMonth(), date.getUTCDate() + offsets[event.key]);
      const key = dateKey(next);
      if (min && key < min.slice(0, 10)) return;
      setFocusDay(key); setYear(next.getUTCFullYear()); setMonth(next.getUTCMonth());
    } else if (event.key === "PageUp" || event.key === "PageDown") {
      event.preventDefault(); moveMonth(event.key === "PageUp" ? -1 : 1);
    }
  }
  function close() { panel.current?.hidePopover(); trigger.current?.focus(); }
  function editTime(field: "hour" | "minute" = "hour") {
    editField.current = field;
    setEditingTime(true);
  }
  function confirmTime() {
    if (!timeValid) return;
    setHour(String(hours).padStart(2, "0"));
    setMinute(String(minutes).padStart(2, "0"));
    returnToTime.current = true;
    setEditingTime(false);
  }
  return <>
    <button ref={trigger} id={id} type="button" className={styles.trigger} disabled={disabled}
      aria-label={`${label}: ${display}`} aria-describedby={describedBy} aria-haspopup="dialog" aria-expanded={open} aria-controls={popoverId}
      popoverTarget={popoverId} onClick={begin} onKeyDown={event => {
        if (event.key === "ArrowDown") { event.preventDefault(); begin(); panel.current?.showPopover(); }
      }}>
      <span data-placeholder={!value}>{display}</span><Icon icon={Calendar03Icon} size={19} strokeWidth={1.7} />
    </button>
    <div ref={panel} id={popoverId} popover="auto" role="dialog" aria-label={`Choose ${label.toLowerCase()}`} className={styles.picker}
      onToggle={event => setOpen(event.newState === "open")}
      onBlurCapture={event => {
        if (event.relatedTarget && !event.currentTarget.contains(event.relatedTarget) && event.relatedTarget !== trigger.current) panel.current?.hidePopover();
      }}
      onKeyDown={event => {
        if (event.key === "Escape") { event.preventDefault(); event.stopPropagation(); close(); }
      }}>
      <div className={styles.heading}>
        <button type="button" className={styles.monthTitle} onClick={() => setMonthView(view => !view)} aria-label={monthView ? "Show calendar" : "Choose month and year"}>
          {monthView ? year : <>{months[month]}<span className={styles.year}>{year}</span></>}<Icon icon={ArrowDown01Icon} size={14} />
        </button>
        <div className={styles.navigation}>
          <button type="button" aria-label={monthView ? "Previous year" : "Previous month"} disabled={year === 1 && (monthView || month === 0)} onClick={() => monthView ? setYear(y => y - 1) : moveMonth(-1)}><Icon icon={ArrowLeft01Icon} size={18} /></button>
          <button type="button" aria-label={monthView ? "Next year" : "Next month"} disabled={year === 9999 && (monthView || month === 11)} onClick={() => monthView ? setYear(y => y + 1) : moveMonth(1)}><Icon icon={ArrowRight01Icon} size={18} /></button>
        </div>
      </div>
      {monthView ? <div className={styles.months}>{months.map((name, index) => <button type="button" key={name} aria-pressed={index === month} onClick={() => { setMonth(index); setMonthView(false); setFocusDay(dateKey(dateOf(year, index))); }}>{name.slice(0, 3)}</button>)}</div> : <div ref={grid} className={styles.calendar} role="grid" aria-label={`${months[month]} ${year}`}>
        <div className={styles.week} role="row">{weekdays.map(name => <span key={name} role="columnheader" aria-label={name}>{name.slice(0, 2)}</span>)}</div>
        {Array.from({ length: weekCount }, (_, row) => <div className={styles.week} role="row" key={row}>{days.slice(row * 7, row * 7 + 7).map(date => {
          const key = dateKey(date);
          const unavailable = date.getUTCFullYear() < 1 || date.getUTCFullYear() > 9999 || !!min && key < min.slice(0, 10);
          return <div role="gridcell" aria-selected={day === key} key={key}><button type="button" data-day={key} data-outside={date.getUTCMonth() !== month} data-today={key === today} data-selected={day === key}
            aria-label={dayFormat.format(date)} aria-current={key === today ? "date" : undefined} disabled={unavailable} tabIndex={key === focusDay ? 0 : -1}
            onClick={() => chooseDay(date)} onKeyDown={event => navigateDay(event, date)}>{date.getUTCDate()}</button></div>;
        })}</div>)}
      </div>}
      <div className={styles.timeSection} data-editing={editingTime}>
        <div className={styles.timeHeading}><span><Icon icon={Clock01Icon} size={14} />Time</span><span>Eastern Time</span></div>
        <button ref={timeToggle} className={styles.timeReadout} type="button" hidden={editingTime} aria-label={`Edit time: ${hour}:${minute} ${period}`} onClick={() => editTime()}>
          <span>{hour}<span className={styles.colon}>:</span>{minute}</span><span className={styles.readoutPeriod}>{period}</span><Icon icon={PencilEdit02Icon} size={15} />
        </button>
        <div className={styles.timePill} hidden={!editingTime} onKeyDown={event => {
          if (event.key === "Enter" && editingTime) { event.preventDefault(); confirmTime(); }
        }}>
          <label className={styles.timeSegment} onClick={() => editTime("hour")}>
            <input ref={hourInput} type="number" min={1} max={12} inputMode="numeric" aria-label="Hour" value={hour} readOnly={!editingTime} tabIndex={editingTime ? 0 : -1}
              onChange={event => setHour(event.target.value)} onFocus={event => event.target.select()} onBlur={() => { if (hours >= 1 && hours <= 12) setHour(String(hours).padStart(2, "0")); }} />
          </label>
          <span className={styles.colon}>:</span>
          <label className={styles.timeSegment} onClick={() => editTime("minute")}>
            <input ref={minuteInput} type="number" min={0} max={59} inputMode="numeric" aria-label="Minute" value={minute} readOnly={!editingTime} tabIndex={editingTime ? 0 : -1}
              onChange={event => setMinute(event.target.value)} onFocus={event => event.target.select()} onBlur={() => { if (minutes >= 0 && minutes <= 59 && minute !== "") setMinute(String(minutes).padStart(2, "0")); }} />
          </label>
          <div className={styles.period} role="group" aria-label="Time period">{["AM", "PM"].map(item => <button key={item} type="button" aria-pressed={period === item} onClick={() => setPeriod(item)}>{item}</button>)}</div>
          <button className={styles.timeToggle} type="button" aria-label="Confirm time" disabled={!timeValid}
            onClick={confirmTime}><Icon icon={Tick02Icon} size={17} /></button>
        </div>
      </div>
      <ErrorToast message={open && !valid ? !timeValid ? "Enter a valid hour and minute." : min && draft < min ? "Choose a future date and time." : "This time does not exist in Eastern Time. Choose another time." : null} />
      <div className={styles.footer}>
        <button type="button" className={styles.today} onClick={() => chooseDay(new Date(`${today}T00:00:00Z`))}>Today</button>
        <button type="button" className={styles.cancel} onClick={close}>Cancel</button>
        <button type="button" className={styles.done} disabled={!valid} onClick={() => { onChange(draft); close(); }}>Done</button>
      </div>
    </div>
  </>;
}
