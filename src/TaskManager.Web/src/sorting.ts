import type { Sort } from './components';

export type SortValue = string | number | bigint | null | undefined;
export const textCollator = new Intl.Collator('zh-CN', { numeric: true });

// Missing values stay last in both directions; equal values retain their input order.
export function compareValues(a: SortValue, b: SortValue, direction: Sort['direction']) {
  if (a == null) return b == null ? 0 : 1;
  if (b == null) return -1;
  const result = typeof a === 'string' && typeof b === 'string' ? textCollator.compare(a, b) : a < b ? -1 : a > b ? 1 : 0;
  return direction === 'ascending' ? result : -result;
}

export function sorted<T>(rows: T[], sort: Sort, value: (row: T, key: string) => SortValue) {
  return [...rows].sort((a, b) => compareValues(value(a, sort.key), value(b, sort.key), sort.direction));
}
