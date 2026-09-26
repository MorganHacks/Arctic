"use client";

import { useState } from "react";
import { Calendar03Icon, Cancel01Icon, Clock01Icon, Megaphone01Icon, PencilEdit02Icon, Search01Icon, UserGroupIcon } from "@hugeicons/core-free-icons";
import { NavigationLink as Link } from "@/components/ui/navigation-link";
import { Icon } from "@/components/ui/icon";
import { EmptyState } from "@/components/ui/empty-state";
import { NewEvent } from "./new-event";
import { NoEvents } from "./no-events";
import { registrationState, type EventRow } from "./types";
import { compact, ZONE } from "./zone";
import styles from "./events-list.module.css";

type Stage = "live" | "upcoming" | "unscheduled" | "past";
type Filter = "all" | "upcoming" | "past";

const stages: { key: Stage; label: string }[] = [
  { key: "live", label: "Happening now" },
  { key: "upcoming", label: "Upcoming" },
  { key: "unscheduled", label: "Dates to be decided" },
  { key: "past", label: "Past events" },
];
const day = new Intl.DateTimeFormat("en-US", { timeZone: ZONE, day: "numeric" });
const month = new Intl.DateTimeFormat("en-US", { timeZone: ZONE, month: "short" });
const date = new Intl.DateTimeFormat("en-US", { timeZone: ZONE, month: "short", day: "numeric", year: "numeric" });
const time = new Intl.DateTimeFormat("en-US", { timeZone: ZONE, hour: "numeric", minute: "2-digit", timeZoneName: "short" });
const number = new Intl.NumberFormat("en-US");

function instant(value: string | null) {
  const parsed = value ? Date.parse(value) : NaN;
  return Number.isNaN(parsed) ? null : parsed;
}

function stageOf(event: EventRow, now: number): Stage {
  const start = instant(event.startsAt);
  const end = instant(event.endsAt);
  if (end !== null && end <= now) return "past";
  if (start === null) return "unscheduled";
  if (start > now) return "upcoming";
  return end !== null ? "live" : "past";
}

function dateRange(event: EventRow) {
  const start = instant(event.startsAt);
  const end = instant(event.endsAt);
  if (start === null) return "Dates to be decided";
  return (end !== null && end >= start ? date.formatRange(start, end) : date.format(start)).replace(/\s+/gu, " ");
}

export function EventsList({ events, now, canManage }: { events: EventRow[]; now: number; canManage: boolean }) {
  const [filter, setFilter] = useState<Filter>("all");
  const [query, setQuery] = useState("");
  const search = query.trim().toLocaleLowerCase();
  const visible = events.filter((event) => (filter === "all" || stageOf(event, now) === filter)
    && (!search || `${event.name} ${event.slug}`.toLocaleLowerCase().includes(search)));
  const filters: { key: Filter; label: string; count: number }[] = [
    { key: "all", label: "All events", count: events.length },
    { key: "upcoming", label: "Upcoming", count: events.filter((event) => stageOf(event, now) === "upcoming").length },
    { key: "past", label: "Past", count: events.filter((event) => stageOf(event, now) === "past").length },
  ];

  return <div className={styles.page}>
    <header className={styles.header}>
      <div>
        <h1>Events</h1>
        <p>Bring your next event together. Manage registration, dates and capacity in one place.</p>
      </div>
      {canManage ? <NewEvent /> : null}
    </header>

    {events.length === 0 ? <NoEvents canManage={canManage} /> : <>
      <div className={styles.toolbar}>
        <div className={styles.filters} role="group" aria-label="Filter events">
          {filters.map((item) => <button key={item.key} type="button" aria-pressed={filter === item.key}
            className={styles.filter} onClick={() => setFilter(item.key)}>
            {item.label}<span>{item.count}</span>
          </button>)}
        </div>
        <div className={styles.search}>
          <Icon icon={Search01Icon} size={18} />
          <input type="search" aria-label="Search events" placeholder="Search events…" value={query}
            onChange={(event) => setQuery(event.target.value)} autoComplete="off" />
          {query ? <button type="button" onClick={() => setQuery("")} aria-label="Clear search">
            <Icon icon={Cancel01Icon} size={15} />
          </button> : null}
        </div>
      </div>

      <p className={styles.resultCount} role="status" aria-live="polite">
        {visible.length} {visible.length === 1 ? "event" : "events"}{search ? ` matching “${query.trim()}”` : ""}
      </p>
      {visible.length === 0 ? <EmptyState variant="canvas" size="page" title="No events found"
        description={search ? "Try another name or event slug." : "There are no events in this view yet."}
        action={<button type="button" className={styles.secondaryButton} onClick={() => { setFilter("all"); setQuery(""); }}>Clear filters</button>} /> : stages.map((stage) => {
        const group = visible.filter((event) => stageOf(event, now) === stage.key).sort((a, b) => {
          const first = instant(a.startsAt) ?? instant(a.endsAt) ?? 0;
          const second = instant(b.startsAt) ?? instant(b.endsAt) ?? 0;
          return stage.key === "past" ? second - first : first - second;
        });
        return group.length > 0 ? <section className={styles.group} key={stage.key} aria-labelledby={`events-${stage.key}`}>
          <div className={styles.groupHeading}>
            <h2 id={`events-${stage.key}`}>{stage.label}</h2><span>{group.length}</span>
          </div>
          <ul className={styles.list}>
            {group.map((event) => <EventItem key={event.id} event={event} now={now} canManage={canManage} />)}
          </ul>
        </section> : null;
      })}
      <p className={styles.timezone}><Icon icon={Clock01Icon} size={14} />All times shown in Eastern Time.</p>
    </>}
  </div>;
}

function EventItem({ event, now, canManage }: { event: EventRow; now: number; canManage: boolean }) {
  const start = instant(event.startsAt);
  const state = registrationState(event, now);
  const registrationLabel = { open: "Registration open", closed: "Registration closed", upcoming: "Not open yet", undecided: "Registration not set" }[state];
  const registrationDate = instant(state === "upcoming" ? event.registrationOpensAt : event.registrationClosesAt);
  const href = `/events/${encodeURIComponent(event.id)}`;

  return <li className={styles.event} data-past={stageOf(event, now) === "past"}>
    <div className={styles.eventTop}>
      <div className={styles.dateMarker} aria-hidden="true">
        {start === null ? <Icon icon={Calendar03Icon} size={28} strokeWidth={1.5} /> : <>
          <span>{month.format(start)}</span><strong>{day.format(start)}</strong>
        </>}
      </div>
      <div className={styles.identity}>
        <div className={styles.eventTitle}>
          <h3><Link href={href}>{event.name}</Link></h3>
          <span className={styles.registrationBadge} data-state={state}><span />{registrationLabel}</span>
        </div>
        <p className={styles.eventDates}>{dateRange(event)}</p>
        <p className={styles.slug}>{event.slug}</p>
      </div>
      <Link href={href} className={styles.manageButton} aria-label={`${canManage ? "Manage" : "View"} ${event.name}`}>
        {canManage ? <Icon icon={PencilEdit02Icon} size={16} /> : null}{canManage ? "Manage event" : "View event"}
      </Link>
    </div>
    <dl className={styles.details}>
      <div>
        <dt><Icon icon={Calendar03Icon} size={15} />Schedule</dt>
        <dd>{compact(event.startsAt) || "Start date to be decided"}</dd>
        <dd className={styles.secondaryDetail}>{event.endsAt ? `Ends ${compact(event.endsAt)}` : "End date to be decided"}</dd>
      </div>
      <div>
        <dt><Icon icon={Clock01Icon} size={15} />Registration</dt>
        <dd>{state === "undecided" ? "Opening date to be decided" : registrationDate === null ? "No closing date set"
          : `${state === "upcoming" ? "Opens" : state === "closed" ? "Closed" : "Closes"} ${date.format(registrationDate)}`}</dd>
        {registrationDate !== null ? <dd className={styles.secondaryDetail}>
          {state === "undecided" ? `Closes ${compact(event.registrationClosesAt)}` : time.format(registrationDate).replace(/\s+/gu, " ")}
        </dd> : null}
      </div>
      <div>
        <dt><Icon icon={UserGroupIcon} size={15} />Capacity</dt>
        <dd>{event.capacity === null ? "To be decided" : <><strong>{number.format(event.capacity)}</strong> attendees</>}</dd>
      </div>
      <div>
        <dt><Icon icon={Megaphone01Icon} size={15} />Decisions</dt>
        <dd>{compact(event.decisionsAnnouncedAt) || "To be decided"}</dd>
      </div>
    </dl>
  </li>;
}
