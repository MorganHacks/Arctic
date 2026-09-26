import assert from "node:assert/strict";
import test from "node:test";
import { createResponseSheet } from "./google-sheets.ts";

const args = { formId: "form-id", name: "Applications", accessToken: "test-token" };
const csv = () => new Response('name,email\r\nAda,ada@example.test\r\n', { headers: { "content-type": "text/csv; charset=utf-8" } });
const start = () => new Response(null, { headers: { location: "https://www.googleapis.com/upload/drive/v3/files?upload_id=example" } });

test("Sheets export uses every response from the permission-gated CSV and preserves the file bytes", async (t) => {
  const calls = [];
  t.mock.method(globalThis, "fetch", async (url, options) => {
    calls.push({ url, options });
    return [csv(), start(), Response.json({ id: "sheet_123-abc" })][calls.length - 1];
  });
  assert.equal(await createResponseSheet(args), "https://docs.google.com/spreadsheets/d/sheet_123-abc/edit");
  assert.equal(calls[0].url, "/api/admin/forms/form-id/responses.csv");
  assert.equal(calls[0].options.credentials, "same-origin");
  assert.equal(calls[1].options.headers.Authorization, "Bearer test-token");
  assert.equal(JSON.parse(calls[1].options.body).mimeType, "application/vnd.google-apps.spreadsheet");
  assert.equal(calls[2].options.method, "PUT");
  assert.equal(calls[2].options.headers.Authorization, undefined);
  assert.equal(await calls[2].options.body.text(), await csv().text());
});

for (const status of [401, 403, 500]) {
  test(`failed CSV export (${status}) never sends data to Google`, async (t) => {
    let count = 0;
    t.mock.method(globalThis, "fetch", async () => { count++; return new Response(null, { status }); });
    await assert.rejects(createResponseSheet(args));
    assert.equal(count, 1);
  });
}

test("HTML sign-in responses are not exported as spreadsheet data", async (t) => {
  const mocked = t.mock.method(globalThis, "fetch", async () => new Response("Sign in", { headers: { "content-type": "text/html" } }));
  await assert.rejects(createResponseSheet(args), /refresh/);
  assert.equal(mocked.mock.callCount(), 1);
});

test("an upload redirect outside Google never receives responses or tokens", async (t) => {
  let count = 0;
  t.mock.method(globalThis, "fetch", async () => ++count === 1 ? csv() : new Response(null, { headers: { location: "https://untrusted.example/upload" } }));
  await assert.rejects(createResponseSheet(args), /could not start/);
  assert.equal(count, 2);
});

test("a denied Google upload does not return a pretend spreadsheet link", async (t) => {
  let count = 0;
  t.mock.method(globalThis, "fetch", async () => ++count === 1 ? csv() : new Response(null, { status: 403 }));
  await assert.rejects(createResponseSheet(args), /access was granted/);
});

test("malformed spreadsheet IDs do not create an external navigation target", async (t) => {
  let count = 0;
  t.mock.method(globalThis, "fetch", async () => [csv(), start(), Response.json({ id: "../../other" })][count++]);
  await assert.rejects(createResponseSheet(args), /did not return/);
});
