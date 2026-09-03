"use server";

import type { ItemResult, PageResult } from "@/components/responses/types";
import { readOne, readPage } from "./api";

/**
 * The two things the browser asks for after the page has loaded.
 *
 * Actions rather than a route handler, so the API's address, its paging
 * parameters and what its failures mean all stay on the server. Neither
 * revalidates anything: both answer a question about data the browser already
 * has on screen, and re-rendering the page would throw away the pages
 * somebody has loaded.
 *
 * Both are reachable by anybody with a session, as every action is. That is
 * not the gate — the API refuses the request on `applications.view` whoever
 * asks, and these forward its refusal rather than deciding anything.
 */

/** The next page of submissions. Never the first: that one arrives rendered. */
export async function loadResponses(
  formId: string,
  cursor: string,
): Promise<PageResult> {
  const read = await readPage(formId, cursor);

  return read.ok ? { ok: true, page: read.page } : { ok: false, error: read.error };
}

/**
 * One submission, in full, with a fresh resume link.
 *
 * Called when a response is opened rather than when the list loads, because
 * the link it carries expires in about five minutes. Reopening the same
 * response asks again for the same reason.
 */
export async function openResponse(
  formId: string,
  responseId: string,
): Promise<ItemResult> {
  const read = await readOne(formId, responseId);

  return read.ok ? { ok: true, item: read.item } : { ok: false, error: read.error };
}
