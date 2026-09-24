import type { ReactNode } from "react";
import { loadApplicantAnalytics } from "./home-actions";
import { HomeOverview } from "./home-overview";

export async function HomeInsights({ email, bestEmails, greeting }: { email?: ReactNode; bestEmails?: ReactNode; greeting: ReactNode }) {
  const result = await loadApplicantAnalytics();
  return <HomeOverview key={JSON.stringify(result)} initial={result} email={email} bestEmails={bestEmails} greeting={greeting} />;
}
