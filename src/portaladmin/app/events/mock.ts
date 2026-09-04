/*
 * A stand-in for the events endpoints, for development only.
 *
 * Temporary. The three endpoints this answers are being built in a parallel
 * session, and until they land every request from these screens is a 404 —
 * which makes the list, the create form and the edit form impossible to look
 * at, including the empty state that is the whole point of the screen.
 *
 * Reached only from the one marked block in ./api.ts, which will not call it
 * outside development and will not call it without EVENTS_MOCK=1 set by hand.
 * Deleting this file and that block removes the mock entirely; nothing else in
 * the console imports it.
 *
 * It answers with a Response rather than a value so the seam is the fetch
 * boundary and not the parsing: every screen behind it runs the same code
 * against the mock as against the API, including the failure paths.
 *
 * The store starts empty. A fresh environment has no event, and that is the
 * state these screens exist to get somebody out of, so it is the state the
 * mock opens in. Nothing here invents a date, a name or a capacity.
 */

import type { EventRow } from "@/components/events/types";

const events: EventRow[] = [];

type Body = Record<string, unknown>;

/** The answer, or null for a path this mock does not stand in for. */
export function mockEvents(path: string, init?: RequestInit): Response | null {
  const method = (init?.method ?? "GET").toUpperCase();
  const body = readBody(init);

  if (path === "/admin/events" && method === "GET") {
    return json({ events });
  }

  if (path === "/admin/events" && method === "POST") {
    const slug = typeof body.slug === "string" ? body.slug.trim() : "";
    const name = typeof body.name === "string" ? body.name.trim() : "";

    if (slug === "" || name === "") {
      return json({ error: "A slug and a name are both needed." }, 400);
    }

    if (events.some((event) => event.slug === slug)) {
      return json({ error: "That slug is already in use." }, 409);
    }

    const created: EventRow = {
      id: crypto.randomUUID(),
      slug,
      name,
      startsAt: null,
      endsAt: null,
      registrationOpensAt: null,
      registrationClosesAt: null,
      decisionsAnnouncedAt: null,
      capacity: null,
    };

    events.unshift(created);
    return json({ event: created }, 201);
  }

  const edited = path.match(/^\/admin\/events\/([^/]+)$/);
  if (edited && method === "PUT") {
    const event = events.find((candidate) => candidate.id === edited[1]);
    if (!event) {
      return json({ error: "That event does not exist." }, 404);
    }

    for (const key of [
      "startsAt",
      "endsAt",
      "registrationOpensAt",
      "registrationClosesAt",
      "decisionsAnnouncedAt",
    ] as const) {
      if (key in body) {
        event[key] = typeof body[key] === "string" ? (body[key] as string) : null;
      }
    }

    if ("name" in body && typeof body.name === "string") {
      event.name = body.name;
    }

    if ("capacity" in body) {
      event.capacity = typeof body.capacity === "number" ? body.capacity : null;
    }

    return json({ event });
  }

  return null;
}

function readBody(init?: RequestInit): Body {
  if (typeof init?.body !== "string") {
    return {};
  }

  try {
    const parsed: unknown = JSON.parse(init.body);
    return typeof parsed === "object" && parsed !== null ? (parsed as Body) : {};
  } catch {
    return {};
  }
}

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json" },
  });
}
