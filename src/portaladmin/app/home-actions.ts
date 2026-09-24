"use server";

import { apiFetch } from "@/lib/api";
import { schoolBreakdown } from "@/lib/schools";
import type { AnalyticsResult, AnalyticsView } from "./home-analytics";

export async function loadApplicantAnalytics(eventId?: string): Promise<AnalyticsResult> {
  try {
    const query = eventId ? `?${new URLSearchParams({ eventId })}` : "";
    const response = await apiFetch(`/admin/analytics/applicants${query}`, { signal: AbortSignal.timeout(10000) });
    if (!response.ok) return { error: response.status === 403
      ? "You need access to applications to view these analytics."
      : response.status === 401 ? "Your session has expired. Sign in again to view analytics."
      : "Analytics couldn’t load. Please try again." };
    const data = await response.json() as AnalyticsView;
    if (data.analytics) data.analytics.schools = schoolBreakdown(data.analytics.schools, process.env.LOGO_DEV_PUBLISHABLE_KEY);
    return { data, updatedAt: new Date().toISOString() };
  } catch {
    return { error: "Analytics couldn’t load. Please try again." };
  }
}
