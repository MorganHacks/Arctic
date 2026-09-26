"use client";

import { useEffect, useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import { Calendar03Icon, Cancel01Icon, LayoutGridIcon, LayoutListIcon, Link04Icon, Search01Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import { EmptyState } from "@/components/ui/empty-state";
import { Select } from "@/components/ui/select";
import { NewForm } from "@/app/forms/new-form";
import type { EventSummary, FormRow } from "@/lib/api";
import { FormsTable, formStatus } from "./forms-table";
import { FormsCards } from "./forms-cards";
import { NoForms } from "./no-forms";
import styles from "./forms-page.module.css";

type Filter = "all" | "live" | "draft" | "closed";
type View = "grid" | "list";
const viewStorageKey = "arctic:forms-view";

export function FormsList({ events, chosen, forms, now, canManage }: {
  events: EventSummary[]; chosen: EventSummary; forms: FormRow[]; now: number; canManage: boolean;
}) {
  const router = useRouter();
  const [switching, startTransition] = useTransition();
  const [query, setQuery] = useState("");
  const [filter, setFilter] = useState<Filter>("all");
  const [view, setView] = useState<View>("grid");

  useEffect(() => {
    try {
      const saved = localStorage.getItem(viewStorageKey);
      if (saved === "grid" || saved === "list") setView(saved);
    } catch {}
  }, []);

  function changeView(next: View) {
    setView(next);
    try { localStorage.setItem(viewStorageKey, next); } catch {}
  }

  const search = query.trim().toLocaleLowerCase();
  const filters: { value: Filter; label: string; count: number }[] = [
    { value: "all", label: "All forms", count: forms.length },
    { value: "live", label: "Live", count: forms.filter(form => formStatus(form, now) === "live").length },
    { value: "draft", label: "Drafts", count: forms.filter(form => formStatus(form, now) === "draft").length },
    { value: "closed", label: "Closed", count: forms.filter(form => formStatus(form, now) === "closed").length },
  ];
  const visible = forms.filter(form => (filter === "all" || formStatus(form, now) === filter)
    && (!search || `${form.name} ${form.code} ${form.kind}`.toLocaleLowerCase().includes(search)));

  return <div className={styles.page}>
    <header className={styles.header}>
      <div className={styles.heading}>
        <div><h1>Forms</h1><p>Applications, sign-ups and surveys. Everything your event needs to ask.</p></div>
        {canManage ? <NewForm eventId={chosen.id} eventName={chosen.name}
          hasApplication={forms.some(form => form.kind === "application")} disabled={switching} /> : null}
      </div>
      <div className={styles.toolbar}>
        <div className={styles.eventPicker}>
          <Icon icon={Calendar03Icon} size={18} />
          {events.length > 1 ? <Select aria-label="Event" value={chosen.id} disabled={switching}
            onChange={event => { const id = event.target.value; startTransition(() => router.push(`/forms?event=${encodeURIComponent(id)}`, { scroll: false })); }}>
            {events.map(event => <option key={event.id} value={event.id}>{event.name}</option>)}
          </Select> : <span>{chosen.name}</span>}
        </div>
        <div className={styles.search}>
          <Icon icon={Search01Icon} size={18} />
          <input type="search" aria-label="Search forms" autoComplete="off" placeholder="Search forms…" value={query} onChange={event => setQuery(event.target.value)} />
          {query ? <button type="button" aria-label="Clear search" onClick={() => setQuery("")}><Icon icon={Cancel01Icon} size={15} /></button> : null}
        </div>
      </div>
      <div className={styles.filterBar}>
        <div className={styles.filters} role="group" aria-label="Filter forms">
          {filters.map(item => <button key={item.value} type="button" aria-pressed={filter === item.value} onClick={() => setFilter(item.value)}>
            {item.label}<span>{item.count}</span>
          </button>)}
        </div>
        <div className={styles.viewControls}>
          <span className={styles.resultCount} role="status">{switching ? "Switching event…" : `${visible.length} ${visible.length === 1 ? "form" : "forms"}${search ? " found" : ""}`}</span>
          <div className={styles.viewToggle} role="group" aria-label="Form view">
            <button type="button" aria-label="Card view" title="Card view" aria-pressed={view === "grid"} onClick={() => changeView("grid")}>
              <Icon icon={LayoutGridIcon} size={17} />
            </button>
            <button type="button" aria-label="List view" title="List view" aria-pressed={view === "list"} onClick={() => changeView("list")}>
              <Icon icon={LayoutListIcon} size={17} />
            </button>
          </div>
        </div>
      </div>
    </header>
    <div className={styles.results} aria-busy={switching}>
      {forms.length === 0 ? <NoForms event={chosen.name} /> : visible.length === 0 ? <EmptyState size="page" title="No forms found"
        description={search ? "Try another form name or share code." : "There are no forms with this status yet."}
        action={<button type="button" className={styles.secondaryButton} onClick={() => { setFilter("all"); setQuery(""); }}>Clear filters</button>} /> : <>
        {view === "grid" ? <FormsCards forms={visible} now={now} canManage={canManage} /> : <FormsTable forms={visible} now={now} />}
        <p className={styles.linkHint}><Icon icon={Link04Icon} size={14} />Form links stay the same when you edit or publish a new version.</p>
      </>}
    </div>
  </div>;
}
