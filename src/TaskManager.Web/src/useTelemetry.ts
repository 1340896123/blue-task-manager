import { useCallback, useEffect, useRef, useState } from 'react';
import { api, errorMessage } from './api';
import type { Bootstrap, Settings, Snapshot } from './types';

export function useTelemetry() {
  const [bootstrap, setBootstrap] = useState<Bootstrap | null>(null);
  const [snapshot, setSnapshot] = useState<Snapshot | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [connected, setConnected] = useState(false);
  const [busy, setBusy] = useState(false);
  const wake = useRef<() => void>(() => {});
  const settings = useRef<Settings>({ intervalMs: 1000, paused: false });
  useEffect(() => {
    let active = true;
    let timer: number;
    let running = false;
    let queued = false;
    let needsBootstrap = true;
    const controller = new AbortController();
    async function poll() {
      if (!active) return;
      if (running) { queued = true; return; }
      running = true;
      clearTimeout(timer);
      try {
        if (needsBootstrap) {
          const initial = await api.bootstrap(controller.signal);
          if (!active) return;
          setBootstrap(initial); settings.current = initial.settings; needsBootstrap = false;
        }
        const next = await api.get<Snapshot>('snapshot', controller.signal);
        if (!active) return;
        settings.current = { intervalMs: next.intervalMs, paused: next.paused };
        setSnapshot(next);
        setConnected(true); setError(null);
      } catch (caught) {
        if (active) { setError(errorMessage(caught)); setConnected(false); needsBootstrap = true; }
      } finally {
        running = false;
        if (active) { const delay = queued ? 0 : Math.min(settings.current.intervalMs, 2000); queued = false; timer = window.setTimeout(poll, delay); }
      }
    }
    wake.current = () => { clearTimeout(timer); void poll(); };
    void poll();
    return () => { active = false; clearTimeout(timer); controller.abort(); wake.current = () => {}; };
  }, []);
  const retry = useCallback(() => wake.current(), []);
  const updateSettings = useCallback(async (next: Settings) => {
    setBusy(true);
    try {
      const applied = await api.post<Settings>('settings', next);
      settings.current = applied;
      setSnapshot(previous => previous ? { ...previous, ...applied } : previous);
      wake.current();
    } finally { setBusy(false); }
  }, []);
  return { snapshot, bootstrap, error, connected, busy, retry, updateSettings };
}
