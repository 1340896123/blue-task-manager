import type { Availability } from './types';
export const percent = (value: number | null) => value === null ? '—' : `${value.toFixed(1)}%`;
export function bytes(value: number | null, digits = 1): string {
  if (value === null) return '—';
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  const rank = Math.max(0, Math.min(4, Math.floor(Math.log(Math.max(value, 1)) / Math.log(1024))));
  return `${(value / 1024 ** rank).toFixed(rank === 0 ? 0 : digits)} ${units[rank]}`;
}
export const rate = (value: number | null) => value === null ? '—' : `${bytes(value)}/秒`;
export function bigBytes(value: string | null): string {
  if (value === null) return '—';
  const count = BigInt(value);
  // Divide in integer space before conversion; raw cumulative counters may exceed Number.MAX_SAFE_INTEGER.
  if (count > 1024n ** 4n) return `${Number(count / (1024n ** 3n)) / 1024} TB`;
  if (count > 1024n ** 3n) return `${(Number(count / (1024n ** 2n)) / 1024).toFixed(1)} GB`;
  return bytes(Number(count));
}
export const cpuTime = (value: string | null) => value === null ? '—' : `${(Number(BigInt(value) / 10000n) / 1000).toFixed(2)} 秒`;
const timeFormatter = new Intl.DateTimeFormat('zh-CN', { hour: 'numeric', minute: 'numeric', second: 'numeric', hour12: false });
const dateTimeFormatter = new Intl.DateTimeFormat('zh-CN', { year: 'numeric', month: 'numeric', day: 'numeric', hour: 'numeric', minute: 'numeric', second: 'numeric', hour12: false });
export const time = (value: string) => timeFormatter.format(new Date(value));
export const dateTime = (value: string) => dateTimeFormatter.format(new Date(value));
export function uptime(seconds: number) { const s = Math.floor(seconds); return `${Math.floor(s / 86400)} 天 ${Math.floor(s % 86400 / 3600)}:${String(Math.floor(s % 3600 / 60)).padStart(2, '0')}:${String(s % 60).padStart(2, '0')}`; }
export const reason = (unavailable: Availability, key: string, fallback = '当前字段不可读取') => unavailable[key]?.message ?? fallback;
export function heat(value: number | null) { if (value === null) return undefined; return `var(--color-heat-${value >= 80 ? 4 : value >= 50 ? 3 : value >= 20 ? 2 : value >= 5 ? 1 : 0})`; }
export const matches = (query: string, ...values: (string | number | null)[]) => !query || values.some(value => value !== null && String(value).toLocaleLowerCase().includes(query.toLocaleLowerCase().trim()));
