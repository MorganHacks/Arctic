"use client";

import { ErrorToast } from "@/components/ui/error-toast";

import { useCallback, useRef, useState } from "react";
import { ArrowDown01Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import type { FormField } from "@/lib/api";
import { columnsFor } from "./columns";
import { ResponseDetail } from "./response-detail";
import { ResponseColumns } from "./response-columns";
import { ResponsesTable } from "./responses-table";
import { isIdentityField } from "./respondent";
import styles from "./responses.module.css";
import type { ItemResult, PageResult, ResponseItem } from "./types";

/**
 * The responses screen, once there is something on it.
 *
 * Holds the loaded rows and nothing else about them. The first page arrives
 * rendered from the server; every page after it is appended here, so loading
 * page four does not re-fetch pages one to three — which matters more than it
 * sounds, because re-fetching from the start after registration closes would
 * mean five hundred rows crossing the wire to add fifty.
 *
 * Opening a response asks the API for that one response again rather than
 * showing the copy already in the table. That second request is the point: it
 * is what mints the resume link, which is signed and lives about five minutes,
 * and which therefore cannot be handed out with a list.
 */
export function Responses({
  fields,
  initialItems,
  initialCursor,
  loadMore,
  openResponse,
  responseCount,
  canViewResume,
}: {
  fields: FormField[];
  initialItems: ResponseItem[];
  initialCursor: string | null;

  /** Bound to this form on the server. Returns the next page, never the first. */
  loadMore: (cursor: string) => Promise<PageResult>;

  /** Bound to this form on the server. Returns one response, with its resume link. */
  openResponse: (responseId: string) => Promise<ItemResult>;

  responseCount: number;

  canViewResume: boolean;
}) {
  const [items, setItems] = useState(initialItems);
  const [cursor, setCursor] = useState(initialCursor);
  const [loading, setLoading] = useState(false);
  const [failed, setFailed] = useState<string | null>(null);
  const columns = columnsFor(fields, items);
  const [visibleColumns, setVisibleColumns] = useState(() => new Set(
    columns.filter((column) => !isIdentityField(column.field)).slice(0, 3).map((column) => column.key),
  ));

  const [openId, setOpenId] = useState<string | null>(null);
  const [detail, setDetail] = useState<ResponseItem | null>(null);
  const [detailFailed, setDetailFailed] = useState<string | null>(null);

  async function more() {
    if (cursor === null || loading) {
      return;
    }

    setLoading(true);
    setFailed(null);

    let result: PageResult;
    try {
      result = await loadMore(cursor);
    } catch {
      setFailed("Could not load more responses. Please try again.");
      setLoading(false);
      return;
    }

    if (!result.ok) {
      // The cursor is kept. A failed page is a page to try again, not the end
      // of the list, and dropping it would leave no way back to the rest.
      setFailed(result.error);
      setLoading(false);
      return;
    }

    setItems((current) => {
      // The same response arriving twice would render with a duplicate key and
      // be counted twice. Cheap to rule out, and the alternative is a bug that
      // only appears when somebody submits while a page is being turned.
      const seen = new Set(current.map((item) => item.id));
      return [...current, ...result.page.items.filter((item) => !seen.has(item.id))];
    });

    setCursor(result.page.nextCursor);
    setLoading(false);
  }

  /**
   * The response the panel is currently waiting on.
   *
   * A ref rather than state, because it is only ever read after an await to
   * decide whether the answer still describes what is on screen. Somebody who
   * clicked three rows in a row has three requests in flight, and only the
   * last one is allowed to speak.
   */
  const wanted = useRef<string | null>(null);

  const open = useCallback(
    async (id: string) => {
      wanted.current = id;
      setOpenId(id);
      setDetail(null);
      setDetailFailed(null);

      let result: ItemResult;
      try {
        result = await openResponse(id);
      } catch {
        if (wanted.current === id) setDetailFailed("Could not open this response. Please try again.");
        return;
      }

      if (wanted.current !== id) {
        return;
      }

      if (result.ok) {
        setDetail(result.item);
      } else {
        setDetailFailed(result.error);
      }
    },
    [openResponse],
  );

  const close = useCallback(() => {
    wanted.current = null;
    setOpenId(null);
    setDetail(null);
    setDetailFailed(null);
  }, []);

  return (
    <section className={styles.workspace} aria-label="Form responses">
      <div className={styles.tableFrame}>
        <div className={styles.toolbar}>
          <div className={styles.summary}>
            <h2>All responses <span className={styles.totalCount}>{responseCount.toLocaleString("en-US")}</span></h2>
          </div>
          <div className={styles.tableActions}>
            <span className={styles.sortNote}>Newest first</span>
            <ResponseColumns columns={columns} selected={visibleColumns} onChange={setVisibleColumns} />
          </div>
        </div>

        <ResponsesTable
          fields={fields}
          items={items}
          openId={openId}
          onOpen={open}
          showResume={canViewResume}
          columns={columns.filter((column) => visibleColumns.has(column.key))}
        />

        <div className={styles.foot}>
          <span className={styles.note} role="status">
            Showing <strong>{items.length.toLocaleString("en-US")}</strong> of {Math.max(responseCount, items.length).toLocaleString("en-US")} responses
          </span>
          <ErrorToast message={failed} />
          {cursor !== null ? (
            <button type="button" className={styles.moreButton} onClick={more} disabled={loading}>
              {loading ? "Loading…" : "Load more"}
              <Icon icon={ArrowDown01Icon} size={16} />
            </button>
          ) : <span className={styles.note}>All caught up</span>}
        </div>
      </div>

      {openId !== null ? (
        <ResponseDetail
          fields={fields}
          item={detail}
          loading={detail === null && detailFailed === null}
          error={detailFailed}
          onClose={close}
        />
      ) : null}
    </section>
  );
}
