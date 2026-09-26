export function reorderFields<T extends { key: string }>(fields: T[], key: string, to: number): T[] {
  const from = fields.findIndex(field => field.key === key);
  if (from < 0 || !Number.isInteger(to) || to < 0 || to >= fields.length || from === to) return fields;
  const next = [...fields];
  const [field] = next.splice(from, 1);
  next.splice(to, 0, field);
  return next;
}
