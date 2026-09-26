import assert from "node:assert/strict";
import test from "node:test";
import { formShareContent, formShareMetadata, formShareOrigin } from "./form-sharing.ts";

const form = {
  code: "example",
  name: "MorganHacks 2027",
  kind: "application",
  open: true,
  requiresSignIn: false,
  access: "open",
  closesAt: "2027-04-03T13:00:00Z",
  version: 1,
  fields: [
    { type: "section", label: "Join us", help: "Build something together at Morgan State University." },
    { type: "email", label: "Email", required: true },
  ],
};

test("sharing uses absolute canonical and image URLs for the form's environment", () => {
  for (const [headers, expected] of [
    [{ host: "localhost:3002" }, "http://localhost:3002"],
    [{ host: "internal:3000", "x-forwarded-host": "forms-stg.morganhacks.com", "x-forwarded-proto": "https" }, "https://forms-stg.morganhacks.com"],
    [{ host: "forms.morganhacks.com" }, "https://forms.morganhacks.com"],
  ]) {
    const origin = formShareOrigin(new Headers(headers));
    assert.equal(origin, expected);
    const metadata = formShareMetadata(form, origin);
    assert.equal(metadata.alternates.canonical, `${expected}/example`);
    assert.equal(metadata.openGraph.url, metadata.alternates.canonical);
    assert.equal(metadata.openGraph.images[0].width, 1200);
    assert.equal(metadata.openGraph.images[0].height, 630);
    assert.match(metadata.openGraph.images[0].url, new RegExp(`^${expected}/example/share-image\\?v=[a-f0-9]{16}$`));
    assert.equal(metadata.twitter.images[0].url, metadata.openGraph.images[0].url);
    assert.equal(metadata.twitter.card, "summary_large_image");
    assert.equal(metadata.description, form.fields[0].help);
  }
  assert.equal(formShareOrigin(new Headers({ host: "invalid/path" })), "https://forms.morganhacks.com");
  assert.equal(formShareOrigin(new Headers({ host: "localhost:3002" }), "https://forms.morganhacks.com"), "https://forms.morganhacks.com");
});

test("published changes refresh the preview URL without including applicant data", () => {
  const image = value => formShareMetadata(value, "https://forms.morganhacks.com").openGraph.images[0].url;
  const original = image(form);
  for (const update of [
    { version: 2 },
    { name: "Updated form" },
    { theme: { background: "#000000" } },
    { open: false, access: "closed" },
    { closesAt: null },
  ]) assert.notEqual(image({ ...form, ...update }), original);
  const personalized = { ...form, you: { name: "Private person", email: "private@example.com" }, prefill: { email: "private@example.com" }, fixed: ["email"] };
  assert.equal(image(personalized), original);
  assert.doesNotMatch(JSON.stringify(formShareContent(personalized)), /Private person|private@example/);
});

test("gated and closed forms never expose their questions or section descriptions", () => {
  for (const update of [
    { requiresSignIn: true, access: "signIn" },
    { requiresSignIn: true, access: "open" },
    { open: false, access: "closed" },
  ]) {
    const content = formShareContent({ ...form, ...update });
    assert.doesNotMatch(JSON.stringify(content), /Build something|Join us|Email/);
  }
  assert.equal(formShareContent({ ...form, open: false }).description, "Submissions for MorganHacks 2027 are closed.");
  assert.equal(formShareContent({ ...form, requiresSignIn: true }).description, "Sign in to complete MorganHacks 2027.");
});

test("preview copy stays bounded and empty introductions get useful descriptions", () => {
  const long = formShareContent({ ...form, name: "A long name ".repeat(40), fields: [{ type: "section", label: "Intro", help: "Long description ".repeat(40) }] });
  assert.ok(long.title.length <= 120);
  assert.ok(long.description.length <= 200);
  const empty = formShareContent({ ...form, fields: [] });
  assert.equal(empty.description, "Complete MorganHacks 2027 with MorganHacks.");
});
