import { Tabs } from "./tabs";

/**
 * The chrome only a signed-in applicant sees.
 *
 * A route group rather than a path segment, so these pages are still
 * `/portal`, `/portal/profile` and `/portal/messages` — the addresses that go
 * in emails and get bookmarked — while `/portal/sign-in` sits outside and gets
 * no tabs.
 *
 * The tabs are chrome, not a gate. Each page checks the session for itself,
 * because a layout that redirected would be a check somebody could route
 * around by adding a page beside it.
 */
export default function SignedInLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  return (
    <>
      <Tabs />
      <main>{children}</main>
    </>
  );
}
