import { createHash } from "node:crypto";
import type { Metadata } from "next";
import type { PublicForm } from "./api";

export function shareText(text: string, limit: number): string {
  const value = text.replace(/\s+/g, " ").trim();
  return value.length > limit ? `${value.slice(0, limit - 1).trimEnd()}…` : value;
}

export function formShareContent(form: PublicForm) {
  const fields = form.open && !form.requiresSignIn && form.access === "open" ? form.fields ?? [] : [];
  const introduction = fields[0]?.type === "section" ? fields[0] : null;
  const description = !form.open
    ? `Submissions for ${form.name} are closed.`
    : form.requiresSignIn
      ? `Sign in to complete ${form.name}.`
      : introduction?.help?.trim() || `Complete ${form.name} with MorganHacks.`;

  return {
    title: shareText(form.name, 120),
    description: shareText(description, 200),
  };
}

export function formShareOrigin(requestHeaders: Pick<Headers, "get">, configuredOrigin?: string): string {
  if (configuredOrigin) {
    const configured = new URL(configuredOrigin);
    if (["http:", "https:"].includes(configured.protocol)) return configured.origin;
  }

  const host = (requestHeaders.get("x-forwarded-host") || requestHeaders.get("host") || "").split(",")[0].trim();
  const forwardedProtocol = requestHeaders.get("x-forwarded-proto")?.split(",")[0].trim();
  const protocol = forwardedProtocol === "http" || forwardedProtocol === "https"
    ? forwardedProtocol
    : /^(localhost|127\.0\.0\.1|\[::1\])(:\d+)?$/.test(host) ? "http" : "https";

  try {
    const url = new URL(`${protocol}://${host}`);
    if (url.host === host && !url.username && !url.password) return url.origin;
  } catch {}

  return "https://forms.morganhacks.com";
}

export function formShareMetadata(form: PublicForm, origin: string): Metadata {
  const content = formShareContent(form);
  const url = new URL(`/${encodeURIComponent(form.code)}`, origin);
  const revision = createHash("sha256").update(JSON.stringify({
    layout: 4,
    content,
    theme: form.theme,
    closesAt: form.closesAt,
    version: form.version,
  })).digest("hex").slice(0, 16);
  const image = new URL(`${url.pathname}/share-image?v=${revision}`, origin);
  const alt = `${content.title} — form preview`;

  return {
    title: `${content.title} — MorganHacks`,
    description: content.description,
    alternates: { canonical: url.href },
    openGraph: {
      type: "website",
      locale: "en_US",
      siteName: "MorganHacks",
      title: content.title,
      description: content.description,
      url: url.href,
      images: [{ url: image.href, width: 1200, height: 630, type: "image/png", alt }],
    },
    twitter: {
      card: "summary_large_image",
      title: content.title,
      description: content.description,
      images: [{ url: image.href, alt }],
    },
  };
}
