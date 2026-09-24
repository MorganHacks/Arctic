"use client";

import { useRef, useState } from "react";
import { ArrowRight01Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import { NavigationLink as Link } from "@/components/ui/navigation-link";
import { dateLabel, numbers, type ActivityDay } from "./home-analytics";
import { CustomDateRange } from "./home-date-range";
import styles from "./home-overview.module.css";

function emptyDays(): ActivityDay[] {
  const today = new Date().toISOString().slice(0, 10);
  return Array.from({ length: 90 }, (_, index) => {
    const date = new Date(`${today}T00:00:00Z`);
    date.setUTCDate(date.getUTCDate() - 89 + index);
    return { date: date.toISOString().slice(0, 10), started: 0, submitted: 0 };
  });
}

export function ResponseActivity({ activity, eventId, loading }: {
  activity: ActivityDay[]; eventId?: string; loading: boolean;
}) {
  const [range, setRange] = useState<7 | 14 | "custom">(14);
  const [custom, setCustom] = useState({ from: "", to: "" });
  const [active, setActive] = useState<number | null>(null);
  const bars = useRef<(HTMLButtonElement | null)[]>([]);
  const all = activity.length ? activity : emptyDays();
  const first = all[0].date;
  const last = all[all.length - 1].date;
  const from = custom.from || all[Math.max(0, all.length - (range === "custom" ? 14 : range))].date;
  const to = custom.to || last;
  const invalid = range === "custom" && (from > to || from < first || to > last);
  const days = range === "custom" ? all.filter((day) => day.date >= from && day.date <= to) : all.slice(-range);
  const submitted = days.reduce((sum, day) => sum + day.submitted, 0);
  const started = days.reduce((sum, day) => sum + day.started, 0);
  const peak = Math.max(0, ...days.map((day) => day.submitted));
  const magnitude = 10 ** Math.floor(Math.log10(Math.max(1, peak / 2)));
  const step = Math.max(1, Math.ceil(peak / 2 / magnitude) * magnitude);
  const ceiling = step * 2;
  const current = active === null ? null : days[active];

  return <section className={styles.activity} aria-labelledby="response-activity-title">
    <div className={styles.sectionHeading}>
      <h2 id="response-activity-title">Response activity</h2>
      <div className={styles.ranges} role="group" aria-label="Chart period">
        {([7, 14] as const).map((value) => <button key={value} type="button" aria-pressed={range === value}
          onClick={() => { setRange(value); setActive(null); }}>{value} days</button>)}
        <CustomDateRange value={{ from, to }} min={first} max={last} selected={range === "custom"} disabled={loading}
          onApply={(value) => { setCustom(value); setRange("custom"); setActive(null); }} />
      </div>
    </div>
    <div className={styles.activitySummary}>
      <strong>{loading ? "—" : numbers.format(submitted)}</strong>
      <span>{range === "custom" ? "responses in this period" : `responses in the last ${range} days`}</span>
    </div>
    {invalid ? <div className={styles.chartMessage} role="status">Choose dates between {dateLabel(first)} and {dateLabel(last)}.</div>
      : <div className={styles.chartWrap}>
        <div className={styles.axis} aria-hidden="true">
          {[2, 1, 0].map((tick) => <span key={tick}>{numbers.format(step * tick)}</span>)}
        </div>
        <div className={styles.chart}>
          <div className={styles.chartGrid} aria-hidden="true">{[0, 1, 2].map((tick) => <span key={tick} />)}</div>
          <div className={styles.dailyBars} role="group" aria-label="Daily responses"
            onPointerLeave={() => setActive(null)} onBlur={(event) => {
              if (!event.currentTarget.contains(event.relatedTarget)) setActive(null);
            }}>
            {days.map((day, index) => <div key={day.date} className={styles.barColumn} data-active={active === index}
              onPointerEnter={() => setActive(index)}>
              <button ref={(element) => { bars.current[index] = element; }} type="button"
                aria-label={`${dateLabel(day.date)}, ${day.date.slice(0, 4)}: ${numbers.format(day.submitted)} responses`}
                className={styles.dailyBar} data-empty={day.submitted === 0} disabled={loading}
                tabIndex={active === null ? index === 0 ? 0 : -1 : active === index ? 0 : -1}
                style={{ height: `max(3px, ${day.submitted / ceiling * 100}%)` }}
                onFocus={() => setActive(index)} onClick={() => setActive(index)}
                onKeyDown={(event) => {
                  const next = event.key === "ArrowRight" ? Math.min(days.length - 1, index + 1)
                    : event.key === "ArrowLeft" ? Math.max(0, index - 1)
                    : event.key === "Home" ? 0 : event.key === "End" ? days.length - 1 : null;
                  if (next === null) return;
                  event.preventDefault();
                  bars.current[next]?.focus();
                }} />
            </div>)}
          </div>
          {current ? <div className={styles.tooltip} role="status" style={{
            left: `clamp(52px, ${((active ?? 0) + .5) / days.length * 100}%, calc(100% - 52px))`,
            bottom: `calc(${current.submitted / ceiling * 100}% + 14px)`,
          }}>
            <span>{dateLabel(current.date)}, {current.date.slice(0, 4)}</span>
            <strong>{numbers.format(current.submitted)} responses</strong>
          </div> : null}
        </div>
        <div className={styles.dateAxis}>
          <span>{days[0] ? `${dateLabel(days[0].date)}, ${days[0].date.slice(0, 4)}` : ""}</span>
          <span>{days.at(-1)?.date === last ? "Today (UTC)" : days.at(-1) ? dateLabel(days.at(-1)!.date) : ""}</span>
        </div>
      </div>}
    <div className={styles.activityFooter}>
      <p>{loading ? "Loading activity…" : submitted === 0 ? "No responses during this period"
        : `${numbers.format(started)} started · Peak of ${numbers.format(peak)} responses in a day`}</p>
      <Link href={eventId ? `/forms?event=${encodeURIComponent(eventId)}` : "/forms"}>View forms<Icon icon={ArrowRight01Icon} size={15} /></Link>
    </div>
    {!invalid && !loading && eventId ? <details className={styles.dataTable}>
      <summary><Icon icon={ArrowRight01Icon} size={14} className={styles.detailsChevron} />View daily counts</summary>
      <div><table><caption className={styles.srOnly}>Daily application activity in UTC</caption>
        <thead><tr><th>Date (UTC)</th><th>Started</th><th>Responses</th></tr></thead>
        <tbody>{days.map((day) => <tr key={day.date}><th scope="row">{dateLabel(day.date)}</th>
          <td>{numbers.format(day.started)}</td><td>{numbers.format(day.submitted)}</td></tr>)}</tbody>
      </table></div>
    </details> : null}
  </section>;
}
