export type ErrorNotification = {
  id: string;
  title: string;
  message: string;
  target: HTMLDialogElement | null;
};

const empty: ErrorNotification[] = [];
let notifications = empty;
const listeners = new Set<() => void>();

export const errorNotifications = {
  subscribe(listener: () => void) {
    listeners.add(listener);
    return () => { listeners.delete(listener); };
  },
  snapshot: () => notifications,
  serverSnapshot: () => empty,
  show(notification: ErrorNotification) {
    notifications = [...notifications.filter(item => item.id !== notification.id), notification];
    listeners.forEach(listener => listener());
  },
  dismiss(id: string) {
    if (!notifications.some(item => item.id === id)) return;
    notifications = notifications.filter(item => item.id !== id);
    listeners.forEach(listener => listener());
  },
};

export function summarizeErrors(messages: string[], subject = "answers"): string {
  const unique = [...new Set(messages.filter(Boolean))];
  if (unique.length <= 3) return unique.join("\n");
  return `${unique.slice(0, 3).join("\n")}\n${unique.length - 3} more ${subject} need attention.`;
}
