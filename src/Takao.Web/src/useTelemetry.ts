import { useCallback, useEffect, useRef, useState } from 'react';
import { api, errorMessage } from './api';
import type { Bootstrap, Settings, Snapshot } from './types';
export function useTelemetry() {
  const [bootstrap, setBootstrap] = useState<Bootstrap | null>(null);
  const [snapshot, setSnapshot] = useState<Snapshot | null>(null);
  const [frozen, setFrozen] = useState<Snapshot | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [connected, setConnected] = useState(false); const [busy, setBusy] = useState(false); const [stale, setStale] = useState(false);
  const wake = useRef<() => void>(() => {}); const latest = useRef<Snapshot | null>(null);
  const settings = useRef<Settings>({ intervalMs: 1000, paused: false });
  useEffect(() => {
    let active = true; let timer: number; let running = false; let queued = false; let needsBootstrap = true; let etag: string | null = null;
    const controller = new AbortController();
    async function poll() {
      if (!active) return;
      if (running) { queued = true; return; }
      running = true; clearTimeout(timer);
      try {
        if (needsBootstrap) {
          const initial = await api.bootstrap(controller.signal); if (!active) return;
          setBootstrap(initial); settings.current = initial.settings; needsBootstrap = false;
        }
        const result = await api.snapshot(etag, controller.signal); if (!active) return;
        if (!result.unchanged) { etag = result.etag; latest.current = result.value; settings.current = { intervalMs: result.value.intervalMs, paused: result.value.paused }; setSnapshot(result.value); }
        setConnected(true); setError(null);
      } catch (caught) { if (active) { setError(errorMessage(caught)); setConnected(false); needsBootstrap = true; } }
      finally {
        running = false;
        if (active) { const delay = queued && !document.hidden ? 0 : document.hidden ? Math.max(5000, settings.current.intervalMs) : settings.current.intervalMs; queued = false; timer = window.setTimeout(poll, delay); }
      }
    }
    wake.current = () => { clearTimeout(timer); void poll(); };
    const visibility = () => { if (!document.hidden) wake.current(); else if (!running) { clearTimeout(timer); timer = window.setTimeout(poll, Math.max(5000, settings.current.intervalMs)); } };
    document.addEventListener('visibilitychange', visibility);
    // The clock only publishes a changed health state. A 304 must not repaint the whole app each second.
    const clock = window.setInterval(() => { const current = latest.current; setStale(Boolean(current && !current.paused && Date.now() - Date.parse(current.capturedAtUtc) > Math.max(3 * current.intervalMs, 5000))); }, 1000); void poll();
    return () => { active = false; clearTimeout(timer); clearInterval(clock); controller.abort(); document.removeEventListener('visibilitychange', visibility); wake.current = () => {}; };
  }, []);
  const retry = useCallback(() => wake.current(), []);
  const toggleFrozen = useCallback(() => setFrozen(previous => previous ? null : latest.current), []);
  const resumeDisplay = useCallback(() => setFrozen(null), []);
  const updateSettings = useCallback(async (next: Settings) => { setBusy(true); try { settings.current = await api.post<Settings>('settings', next); wake.current(); } finally { setBusy(false); } }, []);
  return { snapshot, displaySnapshot: frozen ?? snapshot, frozen: frozen !== null, frozenAt: frozen?.capturedAtUtc ?? null, toggleFrozen, resumeDisplay, bootstrap, error, connected, stale, busy, retry, updateSettings };
}
