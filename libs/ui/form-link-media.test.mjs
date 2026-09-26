import assert from "node:assert/strict";
import test from "node:test";
import { cachedFormLinkMedia, forgetFormLinkMedia, MORGAN_HACKS_RECAP, publicMediaUrl, videoLinkMedia, loadFormLinkMedia } from "./form-link-media.ts";

function storage(t) {
  const values = new Map();
  const previous = Object.getOwnPropertyDescriptor(globalThis, "sessionStorage");
  Object.defineProperty(globalThis, "sessionStorage", { configurable: true, value: {
    getItem: key => values.get(key) ?? null,
    setItem: (key, value) => values.set(key, value),
  } });
  t.after(() => {
    if (previous) Object.defineProperty(globalThis, "sessionStorage", previous);
    else delete globalThis.sessionStorage;
  });
  return values;
}

test("recap links and the original video resolve to the same playable preview without a metadata request", async t => {
  t.mock.method(globalThis, "fetch", () => { throw new Error("Metadata should not be requested"); });
  for (const url of [
    MORGAN_HACKS_RECAP.url,
    "https://instagram.com/reel/DXE7YimkZVk?igsh=example",
    "https://res.cloudinary.com/kardusers/video/upload/v1790236201/reference-02b08846cc82817c0074d6612e6b3e70cf4ce12a2afa7016b603c8e68dbfa4ac_xdbiqx.mp4",
  ]) {
    assert.deepEqual(await loadFormLinkMedia(url, new AbortController().signal), { image: MORGAN_HACKS_RECAP.image, video: MORGAN_HACKS_RECAP.video });
  }
  assert.equal(videoLinkMedia("https://instagram.com/p/anotherPost/"), null);
  assert.equal(videoLinkMedia("https://instagram.com.example.com/p/DXE7YimkZVk/"), null);
});

test("direct video links preserve signed query parameters", () => {
  const video = "https://media.example.com/recap.mp4?token=example&v=2";
  assert.deepEqual(videoLinkMedia(video), { image: null, video });
  assert.equal(videoLinkMedia("https://media.example.com/recap.mp4/page"), null);
});

test("YouTube watch, short, embed, and Shorts links use the same thumbnail", () => {
  for (const url of ["https://youtu.be/jNQXAC9IVRw", "https://www.youtube.com/watch?v=jNQXAC9IVRw", "https://youtube.com/shorts/jNQXAC9IVRw", "https://www.youtube-nocookie.com/embed/jNQXAC9IVRw"]) {
    assert.deepEqual(videoLinkMedia(url), { image: "https://i.ytimg.com/vi/jNQXAC9IVRw/hqdefault.jpg", video: null });
  }
  assert.equal(videoLinkMedia("https://youtube.com.example.com/watch?v=jNQXAC9IVRw"), null);
  assert.equal(videoLinkMedia("https://youtu.be/invalid"), null);
});

test("unsafe media URLs are never loaded", () => {
  for (const url of ["javascript:alert(1)", "file:///clip.mp4", "http://localhost/clip.mp4", "http://127.0.0.1/clip.mp4", "http://2130706433/clip.mp4", "http://[::1]/clip.mp4", "http://private.local/clip.mp4", "https://user:secret@example.com/clip.mp4", "https://example.com\\clip.mp4"]) {
    assert.equal(publicMediaUrl(url), false, url);
    assert.equal(videoLinkMedia(url), null, url);
  }
});

test("provider responses cannot inject non-web media into the card", async t => {
  storage(t);
  t.mock.method(globalThis, "fetch", async () => new Response(JSON.stringify({ image: "javascript:alert(1)", video: "https://media.example.com/clip.mp4" }), { headers: { "Content-Type": "application/json" } }));
  assert.deepEqual(await loadFormLinkMedia("https://example.com/recap", new AbortController().signal), { image: null, video: "https://media.example.com/clip.mp4" });
});

test("resolved previews survive a module reload without another request", async t => {
  storage(t);
  const media = { image: "https://media.example.com/cover.jpg", video: "https://media.example.com/recap.mp4" };
  const request = t.mock.method(globalThis, "fetch", async () => new Response(JSON.stringify(media)));
  const url = "https://example.com/saved-recap";
  assert.deepEqual(await loadFormLinkMedia(url, new AbortController().signal), media);
  const reloaded = await import("./form-link-media.ts?reload");
  assert.deepEqual(reloaded.cachedFormLinkMedia(url), media);
  assert.deepEqual(await reloaded.loadFormLinkMedia(url, new AbortController().signal), media);
  assert.equal(request.mock.callCount(), 1);
  forgetFormLinkMedia(url);
  assert.equal(cachedFormLinkMedia(url), null);
});

test("expired and unsafe cached previews are discarded", t => {
  const values = storage(t);
  const url = "https://example.com/recap";
  for (const entry of [
    { url, media: { image: null, video: "https://media.example.com/recap.mp4" }, expires: Date.now() - 1 },
    { url, media: { image: "javascript:alert(1)", video: null }, expires: Date.now() + 60_000 },
  ]) {
    values.set("arctic:form-link-media:v1", JSON.stringify([entry]));
    assert.equal(cachedFormLinkMedia(url), null);
  }
});

test("previews still load when browser storage is unavailable", async t => {
  const values = storage(t);
  t.mock.method(globalThis.sessionStorage, "setItem", () => { throw new Error("Storage blocked"); });
  const media = { image: null, video: "https://media.example.com/recap.mp4" };
  t.mock.method(globalThis, "fetch", async () => new Response(JSON.stringify(media)));
  assert.deepEqual(await loadFormLinkMedia("https://example.com/recap", new AbortController().signal), media);
  assert.equal(values.size, 0);
});
