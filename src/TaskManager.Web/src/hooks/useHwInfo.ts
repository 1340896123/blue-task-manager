import { useCallback, useEffect, useRef, useState } from 'react';
import { api, errorMessage } from '../api';
import type { HwInfoSensors } from '../types';

// Mounted by the selected resource only; completion schedules the next read.
export function useHwInfo(intervalMs: number) {
  const [data, setData] = useState<HwInfoSensors | null>(null);
  const [error, setError] = useState<string | null>(null);
  const interval = useRef(intervalMs); interval.current = intervalMs;
  const wake = useRef<() => void>(() => {});
  useEffect(() => {
    let active = true, running = false, queued = false;
    let timer: number | undefined;
    const controller = new AbortController();
    async function poll() {
      if (!active) return;
      if (running) { queued = true; return; }
      running = true; clearTimeout(timer);
      try {
        const result = await api.hwInfoSensors(controller.signal);
        if (active) { setData(result); setError(null); }
      } catch (caught) { if (active) setError(errorMessage(caught)); }
      finally {
        running = false;
        if (active) { timer = window.setTimeout(poll, queued && !document.hidden ? 0 : document.hidden ? Math.max(5000, interval.current) : interval.current); queued = false; }
      }
    }
    wake.current = () => { clearTimeout(timer); void poll(); };
    const visible = () => { if (!document.hidden) wake.current(); };
    document.addEventListener('visibilitychange', visible); void poll();
    return () => { active = false; clearTimeout(timer); controller.abort(); wake.current = () => {}; document.removeEventListener('visibilitychange', visible); };
  }, []);
  return { data, error, retry: useCallback(() => wake.current(), []) };
}
