import assert from "node:assert/strict";
import test from "node:test";
import { resolveSchool, schoolBreakdown } from "./schools.ts";

test("resolves full names, punctuation and explicit university aliases", () => {
  assert.equal(resolveSchool("  MORGAN STATE UNIVERSITY ")?.domains[0], "morgan.edu");
  assert.equal(resolveSchool("Morgan State")?.name, "Morgan State University");
  assert.equal(resolveSchool("UMBC")?.domains[0], "umbc.edu");
  assert.equal(resolveSchool("University of Maryland Baltimore County")?.domains[0], "umbc.edu");
  assert.equal(resolveSchool("MSU"), null);
  assert.equal(resolveSchool("USC"), null);
  assert.equal(resolveSchool("Unknown High School"), null);
  assert.equal(resolveSchool("https://morgan.edu.evil.test"), null);
});

test("combines aliases without losing counts or merging separate campuses", () => {
  const schools = schoolBreakdown([
    { label: "Morgan State", count: 2 }, { label: "Morgan State University", count: 3 },
    { label: "University of Maryland, Baltimore", count: 2 }, { label: "UMBC", count: 4 },
    { label: "Not provided", count: 20 },
  ], "pk_test");
  assert.equal(schools.length, 4);
  assert.equal(schools[0].count, 5);
  assert.equal(schools.reduce((sum, school) => sum + school.count, 0), 31);
  assert.equal(schools.at(-1).label, "Not provided");
  const logo = new URL(schools[0].logoUrl);
  assert.equal(logo.host, "img.logo.dev");
  assert.equal(logo.pathname, "/morgan.edu");
  assert.equal(logo.searchParams.get("fallback"), "404");
});

test("never exposes a secret key or sends unknown schools to the logo service", () => {
  for (const key of ["", "sk_secret", "pk_test&token=sk_secret"]) {
    assert.equal(schoolBreakdown([{ label: "Morgan State", count: 1 }], key)[0].logoUrl, undefined);
  }
  assert.equal(schoolBreakdown([{ label: "A school not in the directory", count: 1 }], "pk_test")[0].logoUrl, undefined);
});
