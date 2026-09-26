import assert from "node:assert/strict";
import test from "node:test";
import { DEFAULT_FORM_THEME, MLH_BADGE_OPTIONS, formBackgroundColor, formMlhBadgeColor, formThemeStyle, isFormBackground, isFormLinkCard, isFormLinkUrl, mlhBadgeImageUrl, resolveFormTheme } from "./form-theme.ts";

test("all official MLH badge choices survive resolution and override background matching", () => {
  for (const { value } of MLH_BADGE_OPTIONS) {
    for (const background of ["white", "#000000", "#fff6e8"]) {
      const theme = { mlhBadgeColor: value, background };
      assert.equal(resolveFormTheme(theme).mlhBadgeColor, value);
      assert.equal(formMlhBadgeColor(theme), value);
    }
    assert.equal(mlhBadgeImageUrl(2027, value), `https://logged-assets.s3.amazonaws.com/trust-badge/2027/mlh-trust-badge-2027-${value}.svg`);
  }
});

test("legacy and invalid MLH badge choices use Original regardless of background", () => {
  for (const mlhBadgeColor of [undefined, null, "auto", "purple", "../../other"]) {
    assert.equal(resolveFormTheme({ mlhBadgeColor }).mlhBadgeColor, "white");
    assert.equal(formMlhBadgeColor({ mlhBadgeColor, background: "white" }), "white");
    assert.equal(formMlhBadgeColor({ mlhBadgeColor, background: "#FFFFFF" }), "white");
    assert.equal(formMlhBadgeColor({ mlhBadgeColor, background: "#000000" }), "white");
    assert.equal(formMlhBadgeColor({ mlhBadgeColor, background: "#fff6e8" }), "white");
  }
});

test("new and unset themes use a white background", () => {
  assert.equal(DEFAULT_FORM_THEME.background, "white");
  assert.equal(formBackgroundColor(), "#ffffff");
  assert.equal(formThemeStyle({})["--form-background"], "#ffffff");
});

test("custom background colors survive resolution independently of the accent", () => {
  const theme = { accent: "#E2BA7E", background: "#DCE9F2" };
  assert.equal(resolveFormTheme(theme).background, "#DCE9F2");
  assert.equal(formBackgroundColor(theme), "#dce9f2");
  assert.equal(formThemeStyle(theme)["--form-accent"], "#E2BA7E");
});

test("existing neutral and tint backgrounds remain supported", () => {
  assert.equal(formBackgroundColor({ background: "neutral" }), "#f4f4f5");
  assert.equal(formBackgroundColor({ background: "tint", accent: "#000000" }), "#ededed");
});

test("invalid background colors fall back to white without entering CSS", () => {
  for (const background of ["red", "url(evil)", "#ffffff; color: red", "#12345", null]) {
    assert.equal(isFormBackground(background), false);
    assert.equal(formBackgroundColor({ background }), "#ffffff");
  }
});

test("dark backgrounds use light text and dark controls", () => {
  const styles = formThemeStyle({ background: "#182436" });
  assert.equal(styles["--form-background"], "#182436");
  assert.equal(styles["--form-ink"], "#ffffff");
  assert.equal(styles["--form-color-scheme"], "dark");
});

const linkCard = { label: "Morgan Hacks 2026", title: "Watch the recap", url: "https://example.com/recap?v=2026", image: null };

test("link cards are opt-in and survive theme resolution", () => {
  assert.equal(resolveFormTheme({}).linkCard, null);
  assert.deepEqual(resolveFormTheme({ linkCard }).linkCard, linkCard);
  assert.equal(isFormLinkCard({ ...linkCard, label: "" }), true);
});

test("link destinations only accept complete web links without credentials", () => {
  for (const url of ["javascript:alert(1)", "data:text/html,hello", "/recap", "//example.com", "https://", "https://me:secret@example.com", "https://example.com\n", "https://example.com\\recap", "https://example.com/" + "x".repeat(2048)]) {
    assert.equal(isFormLinkUrl(url), false, url);
    assert.equal(resolveFormTheme({ linkCard: { ...linkCard, url } }).linkCard, null);
  }
  assert.equal(isFormLinkUrl("HTTP://example.com/recap"), true);
});

test("invalid card content or oversized thumbnails are not rendered", () => {
  for (const patch of [{ title: " " }, { title: "x".repeat(81) }, { label: "x".repeat(61) }, { image: "https://example.com/image.png" }, { image: "data:image/svg+xml;base64,PHN2Zz4=" }, { image: "data:image/webp;base64,UklGR" + "A".repeat(80000) }]) {
    assert.equal(isFormLinkCard({ ...linkCard, ...patch }), false);
  }
});
