import type { EventSummary } from "@/lib/api";

export type AnalyticsBucket = { label: string; count: number };
export type SchoolBucket = AnalyticsBucket & { domain?: string; logoUrl?: string };
export type ActivityDay = { date: string; started: number; submitted: number };
export type ApplicantAnalytics = {
  totalApplicants: number;
  submittedApplications: number;
  statuses: Record<string, number>;
  schools: SchoolBucket[];
  activity: ActivityDay[];
  demographics: {
    genderCollected: boolean;
    gender: AnalyticsBucket[];
    education: AnalyticsBucket[];
    experience: AnalyticsBucket[];
  } | null;
};
export type AnalyticsView = {
  events: EventSummary[];
  chosen: EventSummary | null;
  analytics: ApplicantAnalytics | null;
  canViewResponses: boolean;
};
export type AnalyticsResult = { data: AnalyticsView; updatedAt: string; error?: never }
  | { data?: never; updatedAt?: never; error: string };

export const numbers = new Intl.NumberFormat("en-US");
export const percentage = (count: number, total: number) => total > 0
  ? new Intl.NumberFormat("en-US", { style: "percent", maximumFractionDigits: 1 }).format(count / total) : "0%";
export const dateLabel = (date: string) => new Intl.DateTimeFormat("en-US", {
  month: "short", day: "numeric", timeZone: "UTC",
}).format(new Date(`${date}T00:00:00Z`));
export const statusLabels: Record<string, string> = {
  incomplete: "Incomplete", submitted: "Submitted", under_review: "Under review",
  accepted: "Accepted", waitlisted: "Waitlisted", rejected: "Rejected", confirmed: "Confirmed",
  declined: "Declined", expired: "Expired", checked_in: "Checked in", withdrawn: "Withdrawn",
};

export function applicantsUrl(eventId?: string, statuses: string[] = []) {
  const params = new URLSearchParams();
  if (eventId) params.set("event", eventId);
  statuses.forEach((status) => params.append("status", status));
  return `/applicants${params.size ? `?${params}` : ""}`;
}
