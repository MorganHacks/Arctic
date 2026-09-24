export const analyticsColors = {
  violet: "#9180c9",
  teal: "#55a496",
  sky: "#6e9fc5",
  amber: "#cca05c",
  rose: "#c487a2",
  slate: "#a4acb9",
};

export type AnalyticsTone = keyof typeof analyticsColors;

const categories = [analyticsColors.violet, analyticsColors.teal, analyticsColors.sky, analyticsColors.amber, analyticsColors.rose];

export function categoryColor(label: string) {
  if (label === "Not provided") return analyticsColors.slate;
  const hash = Array.from(label.toLowerCase()).reduce((value, letter) => (value * 31 + letter.charCodeAt(0)) >>> 0, 0);
  return categories[hash % categories.length];
}

export const applicationColors: Record<string, string> = {
  Incomplete: analyticsColors.slate,
  Submitted: analyticsColors.violet,
  "Under review": analyticsColors.amber,
  Accepted: analyticsColors.teal,
  Waitlisted: analyticsColors.amber,
  Rejected: "#bb797c",
  Confirmed: analyticsColors.teal,
  Declined: analyticsColors.rose,
  Expired: analyticsColors.slate,
  "Checked in": "#428f84",
  Withdrawn: analyticsColors.slate,
};
