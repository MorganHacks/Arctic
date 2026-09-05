// Two files can claim the same URL, and nothing tells you.
//
// app/page.tsx and app/(site)/page.tsx both resolve to "/". A route group is
// organisational only: the parentheses group files for layout purposes and are
// stripped out of the URL. Next picked one, and because the one it picked sat
// outside the group, that group's layout never ran -- and the layout was the
// only place globals.css was imported. The public page shipped with no
// stylesheet at all. The browser was happy, the build was happy, CI was happy.
//
// This runs as part of `npm run typecheck`, so it rides along with the check CI
// already performs. No dependencies on purpose.

import { readdirSync } from "node:fs";
import { join, relative } from "node:path";
import { fileURLToPath } from "node:url";

const APP = join(fileURLToPath(new URL("../", import.meta.url)), "app");

/** Every page and route handler under app/, as paths relative to it. */
function entries(dir) {
    const found = [];
    for (const item of readdirSync(dir, { withFileTypes: true })) {
        const full = join(dir, item.name);
        if (item.isDirectory()) {
            if (item.name === "node_modules" || item.name.startsWith(".")) continue;
            found.push(...entries(full));
        } else if (/^(page|route)\.(tsx?|jsx?)$/.test(item.name)) {
            found.push(relative(APP, full));
        }
    }
    return found;
}

/**
 * The URL a file answers on.
 *
 * Route groups "(name)" are dropped, because that is exactly what Next does and
 * exactly the subtlety that caused this. Parallel routes "@slot" are dropped
 * too, but they are a real feature -- several slots legitimately render at one
 * URL -- so they are excluded from the comparison rather than folded into it.
 */
function route(file) {
    const segments = file.split(/[/\\]/).slice(0, -1);
    if (segments.some((s) => s.startsWith("@"))) return null;
    const kept = segments.filter((s) => !(s.startsWith("(") && s.endsWith(")")));
    return "/" + kept.join("/");
}

const byRoute = new Map();
for (const file of entries(APP)) {
    const url = route(file);
    if (url === null) continue;
    byRoute.set(url, [...(byRoute.get(url) ?? []), file]);
}

const clashes = [...byRoute.entries()].filter(([, files]) => files.length > 1);

if (clashes.length > 0) {
    console.error("\nMore than one file resolves to the same route.\n");
    for (const [url, files] of clashes) {
        console.error(`  ${url}`);
        for (const f of files) console.error(`      app/${f}`);
    }
    console.error(
        "\nA route group's parentheses do not change the URL. Whichever file " +
        "Next picks,\nthe layouts around the other one never run -- including " +
        "any stylesheet they import.\n",
    );
    process.exit(1);
}
