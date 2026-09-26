"use client";

import { NavigationLink as Link } from "@/components/ui/navigation-link";
import { useCallback, useEffect, useRef, useState, useTransition } from "react";
import { setTemplateVisibility } from "@/app/templates/actions";
import {
  Add01Icon,
  ArrowDown01Icon,
  Calendar03Icon,
  Cancel01Icon,
  CheckmarkSquare02Icon,
  Tick02Icon,
  Delete02Icon,
  Download01Icon,
  LayoutGridIcon,
  LayoutListIcon,
  Mail01Icon,
  Search01Icon,
  ViewIcon,
  ViewOffIcon,
} from "@hugeicons/core-free-icons";
import { when } from "@/components/mail/types";
import { Icon } from "@/components/ui/icon";
import { EmptyState } from "@/components/ui/empty-state";
import { emailDocument } from "./email-preview";
import { DeleteTemplateDialog } from "./delete-template-dialog";
import { NoTemplates } from "./no-templates";
import styles from "./templates.module.css";
import { kindLabel, type TemplateRow } from "./types";

const updatedDate = new Intl.DateTimeFormat("en-US", {
  month: "short", day: "numeric", year: "numeric", timeZone: "UTC",
});

function TemplateThumbnail({ html, name }: { html: string; name: string }) {
  const paper = useRef<HTMLDivElement>(null);
  const [scale, setScale] = useState(0.5);
  const [visible, setVisible] = useState(false);

  useEffect(() => {
    if (!paper.current) return;
    const observer = new ResizeObserver(([entry]) => setScale(entry.contentRect.width / 600));
    observer.observe(paper.current);
    const intersection = new IntersectionObserver(([entry]) => {
      if (!entry.isIntersecting) return;
      setVisible(true);
      intersection.disconnect();
    }, { rootMargin: "200px" });
    intersection.observe(paper.current);
    return () => {
      observer.disconnect();
      intersection.disconnect();
    };
  }, []);

  return <div ref={paper} className={styles.templatePreviewPaper}>
    {visible ? <iframe className={styles.templatePreviewFrame} title={`${name} email preview`}
      tabIndex={-1} loading="lazy" sandbox="" referrerPolicy="no-referrer"
      style={{ transform: `scale(${scale})` }} srcDoc={emailDocument(html)} /> : null}
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
export function TemplatesTable({ templates, initialHiddenKeys, canDelete, canManage, personId, mocked }: {
  templates: TemplateRow[];
  initialHiddenKeys: string[];
  canDelete: boolean;
  canManage: boolean;
  personId: string;
  mocked: boolean;
}) {
  const selectButton = useRef<HTMLButtonElement>(null);
  const [selectionMode, setSelectionMode] = useState(false);
  const [selectedKeys, setSelectedKeys] = useState<Set<string>>(new Set());
  const [hiddenKeys, setHiddenKeys] = useState<Set<string>>(() => new Set(initialHiddenKeys));
  const [removedKeys, setRemovedKeys] = useState<Set<string>>(new Set());
  const [hiddenReady, setHiddenReady] = useState(false);
  const [savingVisibility, startVisibilityTransition] = useTransition();
  const legacyImport = useRef<ReturnType<typeof setTemplateVisibility> | null>(null);
  const [visibility, setVisibility] = useState<"visible" | "hidden">("visible");
  const [deleting, setDeleting] = useState<{ templates: TemplateRow[]; protectedCount: number } | null>(null);
  const [notice, setNotice] = useState("");
  const [query, setQuery] = useState("");
  const [kind, setKind] = useState<"all" | TemplateRow["kind"]>("all");
  const [status, setStatus] = useState<"all" | "draft" | "published">("all");
  const [sort, setSort] = useState<"newest" | "oldest" | "name">("newest");
  const [view, setView] = useState<"grid" | "list">("grid");
  const storageKey = `arctic:hidden-templates:${personId}`;

  useEffect(() => {
    setHiddenKeys(new Set(initialHiddenKeys));
  }, [initialHiddenKeys]);

  useEffect(() => {
    let active = true;
    const importHidden = async () => {
      let keys: string[] = [];
      try {
        const saved: unknown = JSON.parse(localStorage.getItem(storageKey) || "[]");
        keys = Array.isArray(saved) ? [...new Set(saved.filter((key): key is string =>
          typeof key === "string" && templates.some((template) => template.key === key)))] : [];
      } catch {
        if (active) setHiddenReady(true);
        return;
      }
      if (keys.length > 0 && !mocked) {
        try {
          legacyImport.current ??= setTemplateVisibility(keys, true);
          const result = await legacyImport.current;
          if (result.ok) {
            if (active) setHiddenKeys(new Set(result.hiddenKeys));
            try { localStorage.removeItem(storageKey); } catch {}
          } else if (active) {
            setNotice("Your browser's hidden templates could not be moved to your account. Reload to retry.");
          }
        } catch {
          if (active) setNotice("Your browser's hidden templates could not be moved to your account. Reload to retry.");
        }
      }
      if (active) setHiddenReady(true);
    };
    void importHidden();
    return () => { active = false; };
  }, [storageKey, templates, mocked]);

  const cancelSelection = useCallback(() => {
    setSelectionMode(false);
    setSelectedKeys(new Set());
    requestAnimationFrame(() => selectButton.current?.focus());
  }, []);

  useEffect(() => {
    if (!selectionMode || deleting || savingVisibility) return;
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key !== "Escape" || event.defaultPrevented) return;
      event.preventDefault();
      cancelSelection();
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [selectionMode, deleting, savingVisibility, cancelSelection]);

  function toggleSelected(key: string) {
    setSelectedKeys((current) => {
      const next = new Set(current);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  }

  const term = query.trim().toLocaleLowerCase();
  const visible = templates.filter((template) => (
    !removedKeys.has(template.key)
    && (visibility === "hidden" ? hiddenKeys.has(template.key) : !hiddenKeys.has(template.key))
    && (term === "" || [template.name, template.key, template.subject, kindLabel(template.kind)]
      .some((value) => value.toLocaleLowerCase().includes(term)))
    && (kind === "all" || template.kind === kind)
    && (status === "all" || (status === "draft" ? template.hasDraft : !template.hasDraft))
  )).sort((left, right) => {
    if (sort === "name") return (left.name || left.key).localeCompare(right.name || right.key);
    const leftTime = left.updatedAt ? Date.parse(left.updatedAt) : 0;
    const rightTime = right.updatedAt ? Date.parse(right.updatedAt) : 0;
    return sort === "newest" ? rightTime - leftTime : leftTime - rightTime;
  });
  const selected = visible.filter((template) => selectedKeys.has(template.key));
  const removable = selected.filter((template) => !["magic_link", "organizer_welcome"].includes(template.key));
  const hiddenCount = templates.filter((template) => hiddenKeys.has(template.key) && !removedKeys.has(template.key)).length;

  function changeVisibility() {
    if (savingVisibility || mocked) return;
    const keys = selected.map((template) => template.key);
    const hidden = visibility !== "hidden";
    setNotice("");
    startVisibilityTransition(async () => {
      try {
        const result = await setTemplateVisibility(keys, hidden);
        if (!result.ok) {
          setNotice(result.error);
          return;
        }
        setHiddenKeys(new Set(result.hiddenKeys));
        setNotice(`${keys.length} ${keys.length === 1 ? "template" : "templates"} ${hidden ? "hidden. Find them in Hidden templates." : "restored."}`);
        cancelSelection();
      } catch {
        setNotice("Your hidden templates could not be saved. Try again.");
      }
    });
  }

  function downloadSelected() {
    let downloaded = 0;
    for (const template of selected) {
      if (!template.previewHtml) continue;
      const html = /<html[\s>]/i.test(template.previewHtml) ? template.previewHtml
        : `<!doctype html><html><head><meta charset="utf-8"></head><body>${template.previewHtml}</body></html>`;
      const url = URL.createObjectURL(new Blob([html], { type: "text/html;charset=utf-8" }));
      const link = document.createElement("a");
      link.href = url;
      link.download = `${template.key.replace(/[^a-z0-9_-]/gi, "-")}.html`;
      document.body.appendChild(link);
      link.click();
      link.remove();
      window.setTimeout(() => URL.revokeObjectURL(url), 1000);
      downloaded++;
    }
    const skipped = selected.length - downloaded;
    setNotice(`${downloaded} HTML ${downloaded === 1 ? "download" : "downloads"} started.${skipped ? ` ${skipped} ${skipped === 1 ? "empty template was" : "empty templates were"} skipped.` : ""}`);
  }

  return (
    <div className={styles.templateGallery} data-selecting={selectionMode}>
      <div className={styles.templateHeader}>
      <div className="form-head">
        <div>
          <h1>Templates</h1>
          <p className="lede" style={{ margin: 0 }}>The subject, body and sending address of every email this system can send.</p>
        </div>
        <div className={styles.templateHeaderActions}>
          {selectionMode ? <>
            <button type="button" className={styles.selectAction} onClick={cancelSelection} disabled={savingVisibility}>
              Cancel <span className={styles.selectionKeycap}>Esc</span>
            </button>
            <button type="button" className={styles.selectAction} disabled={savingVisibility || visible.length === 0}
              onClick={() => setSelectedKeys(new Set(visible.map((template) => template.key)))}>Select all</button>
          </> : <button ref={selectButton} type="button" className={styles.selectAction} aria-pressed={false}
            disabled={!hiddenReady || visible.length === 0} onClick={() => { setNotice(""); setSelectionMode(true); }}>
            <Icon icon={CheckmarkSquare02Icon} size={16} />Select
          </button>}
          {canManage ? <Link href="/templates/new" className={`button primary ${styles.newTemplateButton}`}>
            <Icon icon={Add01Icon} size={16} /><span>New template</span>
          </Link> : null}
        </div>
      </div>
      {mocked ? <p className="error">Showing example data. The templates API is not available yet.</p> : null}
      {notice ? <p className={styles.selectionNotice} role="status">{notice}</p> : null}
      {deleting ? <DeleteTemplateDialog templates={deleting.templates} protectedCount={deleting.protectedCount}
        onClose={() => setDeleting(null)} onDeleted={(keys) => {
          setRemovedKeys((current) => new Set([...current, ...keys]));
          setSelectedKeys((current) => new Set([...current].filter((key) => !keys.includes(key))));
          const remaining = deleting.templates.filter((template) => !keys.includes(template.key));
          setDeleting(remaining.length ? { ...deleting, templates: remaining } : null);
          setNotice(`${keys.length} ${keys.length === 1 ? "template" : "templates"} deleted.`);
          if (remaining.length === 0 && selectionMode) cancelSelection();
        }} /> : null}
      {selected.length > 0 ? <div className={styles.selectionBar} role="toolbar" aria-label="Selected template actions">
        <div className={styles.selectionCount} role="status" aria-live="polite">
          <strong key={selected.length}>{selected.length}</strong><span>selected</span>
        </div>
        <div className={styles.selectionActions}>
          <button type="button" onClick={changeVisibility} disabled={savingVisibility || mocked || !hiddenReady}
            title={visibility === "hidden" ? "Restore to your gallery" : "Hide from your gallery"}>
            <Icon icon={visibility === "hidden" ? ViewIcon : ViewOffIcon} size={14} />
            <span>{savingVisibility ? "Saving…" : visibility === "hidden" ? "Unhide" : "Hide"}</span>
          </button>
          <button type="button" onClick={downloadSelected} disabled={!selected.some((template) => template.previewHtml)}
            title="Download email HTML">
            <Icon icon={Download01Icon} size={14} /><span>Download</span>
          </button>
          {canDelete ? <button type="button" className={styles.selectionDanger} disabled={savingVisibility || removable.length === 0}
            title={removable.length === 0 ? "Account-access templates cannot be deleted" : "Delete selected templates"}
            onClick={() => setDeleting({ templates: removable, protectedCount: selected.length - removable.length })}>
            <Icon icon={Delete02Icon} size={14} /><span>Delete</span>
          </button> : null}
          <button type="button" className={styles.selectionClose} aria-label="Clear selection" onClick={cancelSelection} disabled={savingVisibility}>
            <Icon icon={Cancel01Icon} size={14} />
          </button>
        </div>
      </div> : null}
      {templates.length > 0 ? <>
      <div className={styles.templateToolbar} aria-label="Template controls">
        <label className={styles.templateSearch}>
          <Icon icon={Search01Icon} size={18} strokeWidth={1.8} />
          <span className={styles.visuallyHidden}>Search templates</span>
          <input type="search" placeholder="Search templates…" autoComplete="off" value={query}
            onChange={(event) => { setQuery(event.target.value); setSelectedKeys(new Set()); }} />
        </label>
        <p className={styles.templateCount} aria-live="polite">
          <strong>{visible.length}</strong> {visible.length === 1 ? "template" : "templates"}
        </p>
      </div>

      <div className={styles.templateViewToolbar} aria-label="Template view controls">
        <div className={styles.templateFilters}>
          <label className={styles.templateSelect}>
            <span className={styles.visuallyHidden}>Template kind</span>
            <select value={kind} onChange={(event) => { setKind(event.target.value as typeof kind); setSelectedKeys(new Set()); }}>
              <option value="all">All templates</option>
              <option value="broadcast">Broadcast</option>
              <option value="transactional">Transactional</option>
            </select>
            <Icon icon={ArrowDown01Icon} size={14} className={styles.templateSelectChevron} />
          </label>
          <label className={styles.templateSelect}>
            <span className={styles.visuallyHidden}>Template status</span>
            <select value={status} onChange={(event) => { setStatus(event.target.value as typeof status); setSelectedKeys(new Set()); }}>
              <option value="all">All status</option>
              <option value="draft">Draft</option>
              <option value="published">Published</option>
            </select>
            <Icon icon={ArrowDown01Icon} size={14} className={styles.templateSelectChevron} />
          </label>
          {hiddenCount > 0 || visibility === "hidden" ? <label className={styles.templateSelect}>
            <span className={styles.visuallyHidden}>Template visibility</span>
            <select value={visibility} onChange={(event) => { setVisibility(event.target.value as typeof visibility); setSelectedKeys(new Set()); }}>
              <option value="visible">Visible templates</option>
              <option value="hidden">Hidden templates ({hiddenCount})</option>
            </select>
            <Icon icon={ArrowDown01Icon} size={14} className={styles.templateSelectChevron} />
          </label> : null}
        </div>

        <div className={styles.templateViewControls}>
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
      </> : null}
      </div>
      <div className={styles.templateScroll} role="region" aria-label="Template gallery" tabIndex={0}>
      {templates.length === 0 ? <NoTemplates /> : visible.length > 0 ? (
        <ul className={styles.templateGrid} data-view={view} aria-label="Email templates">
          {visible.map((template) => {
            const name = template.name || template.key;
            const updated = template.updatedAt ? new Date(template.updatedAt) : null;
            const isSelected = selectedKeys.has(template.key);
            const content = <>
                  <div className={styles.templatePreview} aria-hidden="true">
                    {template.previewHtml && view === "grid" ? (
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
                </>;
            return (
              <li key={template.key} className={styles.templateGridItem} data-selected={selectionMode && isSelected}>
                {selectionMode ? <button type="button" className={styles.templateCard} aria-pressed={isSelected}
                  disabled={savingVisibility}
                  aria-label={`${isSelected ? "Deselect" : "Select"} ${name} template`} onClick={() => toggleSelected(template.key)}>
                  {content}
                  <span className={styles.selectIndicator} aria-hidden="true">
                    {isSelected ? <Icon icon={Tick02Icon} size={15} /> : null}
                  </span>
                </button> : <Link href={`/templates/${encodeURIComponent(template.key)}`} className={styles.templateCard}
                  aria-label={`Open ${name} template`}>{content}</Link>}
                {!selectionMode && canDelete && !["magic_link", "organizer_welcome"].includes(template.key) ? (
                  <button type="button" className={styles.templateDelete}
                    aria-label={`Delete ${name} template`} title="Delete template"
                    onClick={() => { setNotice(""); setDeleting({ templates: [template], protectedCount: 0 }); }}>
                    <Icon icon={Delete02Icon} size={17} />
                  </button>
                ) : null}
              </li>
            );
          })}
        </ul>
      ) : (
        <div className={styles.noTemplateMatches} role="status">
          <EmptyState variant="canvas" size="page" title={visibility === "hidden" ? "No hidden templates found" : "No templates found"}
            description={query.trim() ? `Try another search or adjust your filters for “${query.trim()}”.`
              : hiddenCount > 0 && visibility === "visible" ? "Your hidden templates are available in the Hidden templates filter." : "Try a different template type or status."}
            action={<button type="button" onClick={() => { setQuery(""); setKind("all"); setStatus("all");
            setSelectedKeys(new Set()); setVisibility(hiddenCount > 0 && visibility === "visible" ? "hidden" : "visible"); }}>
            {hiddenCount > 0 && visibility === "visible" ? "View hidden templates" : "Clear filters"}
          </button>} />
        </div>
      )}
      </div>
    </div>
  );
}
