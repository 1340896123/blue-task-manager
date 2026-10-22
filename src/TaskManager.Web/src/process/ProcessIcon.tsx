import { useEffect, useState } from 'react';
import { AppWindow } from 'lucide-react';
import { api } from '../api';
import type { Process } from '../types';
type Entry = { key: string; target: Process; url: string | null; settled: boolean; touched: number; listeners: Set<(url: string | null) => void>; controller?: AbortController };
const entries = new Map<string, Entry>(); const queue: Entry[] = []; let running = 0;
function trim() {
  if (entries.size <= 256) return;
  for (const entry of [...entries.values()].sort((a, b) => a.touched - b.touched)) {
    if (entries.size <= 256) break;
    if (entry.listeners.size || entry.controller) continue;
    if (entry.url) URL.revokeObjectURL(entry.url); entries.delete(entry.key);
  }
}
function pump() {
  while (running < 2 && queue.length) {
    const entry = queue.shift()!;
    if (!entry.listeners.size || entry.settled || entry.controller) continue;
    running++; const controller = new AbortController(); entry.controller = controller;
    void api.icon(entry.target, controller.signal).then(blob => {
      if (controller.signal.aborted) return;
      entry.url = blob ? URL.createObjectURL(blob) : null; entry.settled = true;
      for (const listener of entry.listeners) listener(entry.url);
    }).catch(() => { if (!controller.signal.aborted) entry.settled = true; }).finally(() => {
      running--; entry.controller = undefined;
      if (controller.signal.aborted && !entry.settled && entry.listeners.size) queue.push(entry);
      trim(); pump();
    });
  }
}
export default function ProcessIcon({ process }: { process: Process }) {
  const key = `${process.identity}|${process.path?.toLocaleLowerCase() ?? ''}`;
  const [image, setImage] = useState<{ key: string; url: string | null }>({ key, url: entries.get(key)?.url ?? null });
  useEffect(() => {
    if (!process.creationTimeFileTime || !process.path) return;
    let entry = entries.get(key);
    if (!entry) { entry = { key, target: process, url: null, settled: false, touched: Date.now(), listeners: new Set() }; entries.set(key, entry); }
    const current = entry; const listener = (url: string | null) => setImage({ key, url }); current.listeners.add(listener); current.touched = Date.now();
    listener(current.url); if (!current.settled && !current.controller && !queue.includes(current) && queue.length < 256) queue.push(current); pump(); trim();
    return () => { current.listeners.delete(listener); if (!current.listeners.size) { current.controller?.abort(); for (let index = queue.length - 1; index >= 0; index--) if (queue[index] === current) queue.splice(index, 1); } trim(); };
  }, [key]);
  const url = image.key === key ? image.url : entries.get(key)?.url;
  return <span className="process-icon" aria-hidden="true">{url ? <img src={url} alt="" draggable={false} onError={() => setImage({ key, url: null })}/> : <AppWindow/>}</span>;
}
