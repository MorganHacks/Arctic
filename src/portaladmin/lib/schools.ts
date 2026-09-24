import directory from "./data/universities/us.json" with { type: "json" };

type University = { name: string; domains: string[] };
export type SchoolCount = { label: string; count: number; domain?: string; logoUrl?: string };

function normalize(value: string) {
  return value.normalize("NFKD").replace(/\p{M}/gu, "").toLowerCase()
    .replace(/[’']/g, "").replace(/&/g, " and ").replace(/[^\p{L}\p{N}]+/gu, " ").trim();
}

const names = new Map<string, University | null>();
for (const university of directory.universities) {
  const name = normalize(university.name);
  const existing = names.get(name);
  names.set(name, existing === undefined || existing?.domains[0] === university.domains[0] ? university : null);
}

const aliases: Record<string, string> = {
  "morgan state": "Morgan State University",
  "howard": "Howard University",
  "bowie state": "Bowie State University",
  "coppin state": "Coppin State University",
  "towson": "Towson University",
  "umbc": "University of Maryland, Baltimore County",
  "umd": "University of Maryland, College Park",
  "university of maryland college park": "University of Maryland, College Park",
  "umes": "University of Maryland Eastern Shore",
  "jhu": "Johns Hopkins University",
  "mit": "Massachusetts Institute of Technology",
  "penn state": "Pennsylvania State University",
  "uc berkeley": "University of California, Berkeley",
  "nc a and t": "North Carolina A&T State University",
};

export function resolveSchool(label: string): University | null {
  const name = normalize(label);
  return names.get(normalize(aliases[name] ?? name)) ?? null;
}

export function schoolBreakdown(items: SchoolCount[], publishableKey = ""): SchoolCount[] {
  const counts = new Map<string, SchoolCount>();
  for (const item of items) {
    const university = resolveSchool(item.label);
    const label = university?.name ?? item.label;
    const key = normalize(label);
    const existing = counts.get(key);
    if (existing) {
      existing.count += item.count;
      continue;
    }
    const domain = university?.domains.find((value) => /^(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.)+[a-z]{2,}$/i.test(value));
    const logoUrl = domain && /^pk_[a-zA-Z0-9_-]+$/.test(publishableKey)
      ? `https://img.logo.dev/${domain}?${new URLSearchParams({ token: publishableKey, size: "64", format: "webp", theme: "light", fallback: "404" })}`
      : undefined;
    counts.set(key, { label, count: item.count, ...(domain ? { domain } : {}), ...(logoUrl ? { logoUrl } : {}) });
  }
  return [...counts.values()].sort((a, b) => Number(a.label === "Not provided") - Number(b.label === "Not provided")
    || b.count - a.count || a.label.localeCompare(b.label));
}
