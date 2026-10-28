import { api, ApiError, type ApplicationIconKind } from './api';

export type IconTarget = { kind: ApplicationIconKind; id: string; revision?: string };
type Listener = (url: string | null) => void;
type Entry = { key: string; target: IconTarget; url: string | null; retryAt: number; failures: number; successes: number; touched: number; listeners: Set<Listener>; controller?: AbortController };
const maximumEntries = 512, maximumConcurrent = 2;
const entries = new Map<string, Entry>();
let running = 0, timer: number | undefined;
export const iconKey = ({ kind, id, revision = '' }: IconTarget) => `${kind}:${id.length}:${id}:${revision}`;
export const cachedIcon = (key: string) => entries.get(key)?.url ?? null;
const notify = (entry: Entry) => { for (const listener of entry.listeners) listener(entry.url); };
const retryDelay = (failures: number) => Math.min(60000, 15000 * 2 ** Math.min(failures - 1, 2));
function replaceUrl(entry: Entry, url: string | null) {
  const previous = entry.url;
  entry.url = url; notify(entry);
  if (previous) URL.revokeObjectURL(previous);
}
function makeRoom() {
  if (entries.size < maximumEntries) return true;
  const oldest = [...entries.values()].filter(entry => !entry.listeners.size && !entry.controller).sort((a, b) => a.touched - b.touched)[0];
  if (!oldest) return false;
  if (oldest.url) URL.revokeObjectURL(oldest.url);
  entries.delete(oldest.key);
  return true;
}
function pump() {
  clearTimeout(timer); timer = undefined;
  const now = Date.now(); let next = Infinity;
  for (const entry of entries.values()) {
    if (!entry.listeners.size || entry.controller) continue;
    if (entry.retryAt > now) { next = Math.min(next, entry.retryAt); continue; }
    if (running >= maximumConcurrent) continue;
    const controller = new AbortController(); entry.controller = controller; running++;
    void api.applicationIcon(entry.target.kind, entry.target.id, controller.signal).then(blob => {
      if (controller.signal.aborted) return;
      if (blob && (blob.type !== 'image/png' || !blob.size)) throw new Error('Windows 图标响应无效');
      replaceUrl(entry, blob ? URL.createObjectURL(blob) : null);
      if (blob) {
        entry.failures = 0; entry.successes++;
        // The metadata worker can resolve a packaged app after its first exe icon.
        entry.retryAt = Date.now() + (entry.successes === 1 ? 15000 : 60000);
      } else { entry.failures++; entry.retryAt = Date.now() + retryDelay(entry.failures); }
    }).catch(error => {
      if (controller.signal.aborted) return;
      entry.failures++;
      entry.retryAt = error instanceof ApiError && error.status === 400 ? Infinity : Date.now() + retryDelay(entry.failures);
    }).finally(() => {
      entry.controller = undefined; running--;
      if (controller.signal.aborted) entry.retryAt = Date.now();
      pump();
    });
  }
  if (running < maximumConcurrent && Number.isFinite(next)) timer = window.setTimeout(pump, Math.max(50, next - Date.now()));
}
export function subscribeIcon(target: IconTarget, listener: Listener) {
  const key = iconKey(target); let current: Entry | undefined, capacityTimer: number | undefined, active = true;
  const connect = () => {
    if (!active) return;
    current = entries.get(key);
    // Keep a hard cache bound even if a very large tree preview is mounted.
    if (!current && !makeRoom()) { capacityTimer = window.setTimeout(connect, 1000); return; }
    if (!current) {
      current = { key, target, url: null, retryAt: 0, failures: 0, successes: 0, touched: Date.now(), listeners: new Set() };
      entries.set(key, current);
    }
    current.listeners.add(listener); current.touched = Date.now(); listener(current.url); pump();
  };
  connect();
  return () => {
    active = false; clearTimeout(capacityTimer);
    if (current) { current.listeners.delete(listener); current.touched = Date.now(); if (!current.listeners.size) current.controller?.abort(); }
    pump();
  };
}
export function rejectIcon(key: string, url: string) {
  const entry = entries.get(key);
  if (!entry || entry.url !== url) return;
  replaceUrl(entry, null); entry.failures++; entry.retryAt = Date.now() + retryDelay(entry.failures); pump();
}
window.addEventListener('beforeunload', () => {
  clearTimeout(timer);
  for (const entry of entries.values()) { entry.listeners.clear(); entry.controller?.abort(); if (entry.url) URL.revokeObjectURL(entry.url); }
  entries.clear();
});
