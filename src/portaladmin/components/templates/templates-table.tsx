"use client";

import { NavigationLink as Link } from "@/components/ui/navigation-link";
import { useEffect, useRef, useState, type CSSProperties } from "react";
import {
  ArrowDown01Icon,
  Calendar03Icon,
  Grid2x2Icon,
  Grid3x3Icon,
  LayoutGridIcon,
  LayoutListIcon,
  Mail01Icon,
  Search01Icon,
} from "@hugeicons/core-free-icons";
import { when } from "@/components/mail/types";
import { Icon } from "@/components/ui/icon";
import { emailDocument } from "./email-preview";
import styles from "./templates.module.css";
import { kindLabel, type TemplateRow } from "./types";

const updatedDate = new Intl.DateTimeFormat("en-US", {
  month: "short", day: "numeric", year: "numeric", timeZone: "UTC",
});

function TemplateThumbnail({ html, name }: { html: string; name: string }) {
  const paper = useRef<HTMLDivElement>(null);
  const [scale, setScale] = useState(0.5);

  useEffect(() => {
    if (!paper.current) return;
    const observer = new ResizeObserver(([entry]) => setScale(entry.contentRect.width / 600));
    observer.observe(paper.current);
    return () => observer.disconnect();
  }, []);

  return <div ref={paper} className={styles.templatePreviewPaper}>
    <iframe className={styles.templatePreviewFrame} title={`${name} email preview`}
      tabIndex={-1} loading="lazy" sandbox="" referrerPolicy="no-referrer"
      style={{ transform: `scale(${scale})` }} srcDoc={emailDocument(html, "desktop")} />
  </div>;
}

/**
 * Every template there is.
 *
 * The kind is a column rather than a footnote because it decides where a
 * template can be used: a campaign will not offer a transactional one, and
 * somebody looking for the announcement they wrote last week needs to be able
 * to see which lane it is in without opening it.
 */
export function TemplatesTable({ templates }: { templates: TemplateRow[] }) {
  const [query, setQuery] = useState("");
  const [kind, setKind] = useState<"all" | TemplateRow["kind"]>("all");
  const [status, setStatus] = useState<"all" | "draft" | "published">("all");
  const [sort, setSort] = useState<"newest" | "oldest" | "name">("newest");
  const [view, setView] = useState<"grid" | "list">("grid");
  const [density, setDensity] = useState(1);
  const term = query.trim().toLocaleLowerCase();
  const visible = templates.filter((template) => (
    (term === "" || [template.name, template.key, template.subject, kindLabel(template.kind)]
      .some((value) => value.toLocaleLowerCase().includes(term)))
    && (kind === "all" || template.kind === kind)
    && (status === "all" || (status === "draft" ? template.hasDraft : !template.hasDraft))
  )).sort((left, right) => {
    if (sort === "name") return (left.name || left.key).localeCompare(right.name || right.key);
    const leftTime = left.updatedAt ? Date.parse(left.updatedAt) : 0;
    const rightTime = right.updatedAt ? Date.parse(right.updatedAt) : 0;
    return sort === "newest" ? rightTime - leftTime : leftTime - rightTime;
  });
  const gridStyle = { "--template-columns": 4 - density } as CSSProperties;

  return (
    <>
      <div className={styles.templateToolbar} aria-label="Template controls">
        <label className={styles.templateSearch}>
          <Icon icon={Search01Icon} size={18} strokeWidth={1.8} />
          <span className={styles.visuallyHidden}>Search templates</span>
          <input type="search" placeholder="Search templates…" autoComplete="off" value={query}
            onChange={(event) => setQuery(event.target.value)} />
        </label>
        <p className={styles.templateCount} aria-live="polite">
          <strong>{visible.length}</strong> {visible.length === 1 ? "template" : "templates"}
        </p>
      </div>

      <div className={styles.templateViewToolbar} aria-label="Template view controls">
        <div className={styles.templateFilters}>
          <label className={styles.templateSelect}>
            <span className={styles.visuallyHidden}>Template kind</span>
            <select value={kind} onChange={(event) => setKind(event.target.value as typeof kind)}>
              <option value="all">All templates</option>
              <option value="broadcast">Broadcast</option>
              <option value="transactional">Transactional</option>
            </select>
            <Icon icon={ArrowDown01Icon} size={14} className={styles.templateSelectChevron} />
          </label>
          <label className={styles.templateSelect}>
            <span className={styles.visuallyHidden}>Template status</span>
            <select value={status} onChange={(event) => setStatus(event.target.value as typeof status)}>
              <option value="all">All status</option>
              <option value="draft">Draft</option>
              <option value="published">Published</option>
            </select>
            <Icon icon={ArrowDown01Icon} size={14} className={styles.templateSelectChevron} />
          </label>
        </div>

        <div className={styles.templateViewControls}>
          <div className={styles.templateDensity} aria-label="Grid size">
            <Icon icon={Grid3x3Icon} size={15} />
            <input type="range" min="0" max="2" step="1" value={density}
              aria-label="Grid size" disabled={view === "list"}
              aria-valuetext={`${4 - density} columns`}
              onChange={(event) => setDensity(Number(event.target.value))} />
            <Icon icon={Grid2x2Icon} size={15} />
          </div>
          <div className={styles.templateViewToggle}>
            <button type="button" aria-label="Grid view" aria-pressed={view === "grid"}
              onClick={() => setView("grid")}>
              <Icon icon={LayoutGridIcon} size={15} />
            </button>
            <button type="button" aria-label="List view" aria-pressed={view === "list"}
              onClick={() => setView("list")}>
              <Icon icon={LayoutListIcon} size={15} />
            </button>
          </div>
          <label className={`${styles.templateSelect} ${styles.templateSort}`}>
            <span className={styles.visuallyHidden}>Sort templates</span>
            <Icon icon={Calendar03Icon} size={15} className={styles.templateSelectIcon} />
            <select value={sort} onChange={(event) => setSort(event.target.value as typeof sort)}>
              <option value="newest">Latest first</option>
              <option value="oldest">Oldest first</option>
              <option value="name">Name</option>
            </select>
            <Icon icon={ArrowDown01Icon} size={14} className={styles.templateSelectChevron} />
          </label>
        </div>
      </div>

      {visible.length > 0 ? (
        <ul className={styles.templateGrid} data-view={view} style={gridStyle} aria-label="Email templates">
          {visible.map((template) => {
            const name = template.name || template.key;
            const updated = template.updatedAt ? new Date(template.updatedAt) : null;
            return (
              <li key={template.key} className={styles.templateGridItem}>
                <Link href={`/templates/${encodeURIComponent(template.key)}`} className={styles.templateCard}
                  aria-label={`Open ${name} template`}>
                  <div className={styles.templatePreview} aria-hidden="true">
                    {template.previewHtml ? (
                      <TemplateThumbnail html={template.previewHtml} name={name} />
                    ) : (
                      <span className={styles.previewUnavailable}>
                        <Icon icon={Mail01Icon} size={30} strokeWidth={1.4} />
                        <span>Your next email starts here</span>
                      </span>
                    )}
                  </div>

                  <div className={styles.templateCardCopy}>
                    <div className={styles.templateCardEyebrow}>
                      <span className={styles.templateKind}>{kindLabel(template.kind)}</span>
                      {template.hasDraft ? <span className={styles.draftBadge}>Draft</span> : null}
                    </div>
                    <strong className={styles.templateCardTitle}>{name}</strong>
                    <p>{template.subject}</p>
                    <div className={styles.templateMeta}>
                      {updated && !Number.isNaN(updated.getTime()) ? (
                        <time dateTime={template.updatedAt!} title={`${when(template.updatedAt)} UTC`}>
                          Updated {updatedDate.format(updated)}
                        </time>
                      ) : <span>No updates yet</span>}
                      <span>Version {template.version || "—"}</span>
                    </div>
                  </div>
                </Link>
              </li>
            );
          })}
        </ul>
      ) : (
        <div className={styles.noTemplateMatches} role="status">
          <Icon icon={Search01Icon} size={28} strokeWidth={1.4} />
          <strong>No templates found</strong>
          <p>{query.trim() ? `Try another search or adjust your filters for “${query.trim()}”.` : "Try a different template type or status."}</p>
          <button type="button" onClick={() => { setQuery(""); setKind("all"); setStatus("all"); }}>
            Clear filters
          </button>
        </div>
      )}
    </>
  );
}
