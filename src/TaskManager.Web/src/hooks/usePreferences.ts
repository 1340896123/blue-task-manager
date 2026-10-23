import { useCallback, useEffect, useRef, useState } from 'react';
import { api, errorMessage } from '../api';
import type { Page, PagePreference, PreferencesResult, UiPreferences } from '../types';
export const defaultPage: PagePreference = { query: '', sortKey: 'name', sortDirection: 'ascending', visibleColumns: null, columnWidths: null, filters: null, scrollTop: 0, selectedIdentity: null, resourceType: null, resourceId: null };
export const defaultPreferences: UiPreferences = { version: 1, density: 'compact', sidebarCollapsed: false, processView: 'flat', pages: {} };
export function usePreferences() {
  const [preferences, setPreferences] = useState<UiPreferences>(defaultPreferences); const [ready, setReady] = useState(false); const [warning, setWarning] = useState<string | null>(null);
  const [loadRevision, setLoadRevision] = useState(0);
  const value = useRef(preferences); const loaded = useRef(false); const timer = useRef<number | undefined>(undefined);
  const running = useRef(false); const dirty = useRef(false); const active = useRef(true); const finishedReading = useRef(false); const pending = useRef<Promise<void> | null>(null);
  const save = useCallback(async () => {
    if (!loaded.current || running.current || !dirty.current) return;
    running.current = true; dirty.current = false; let failed = false;
    try {
      const { window: _window, ...body } = value.current;
      const result = await api.post<PreferencesResult>('preferences', body);
      if (active.current) setWarning(result.warning?.message ?? null);
    } catch (error) { dirty.current = true; failed = true; if (active.current) setWarning(errorMessage(error)); }
    finally { running.current = false; if (dirty.current && active.current && !failed) timer.current = window.setTimeout(() => { pending.current = save(); }, 600); }
  }, []);
  useEffect(() => {
    active.current = true; let alive = true; const controller = new AbortController();
    void api.get<PreferencesResult>('preferences', controller.signal).then(result => {
      if (!alive) return;
      const next = { ...defaultPreferences, ...result.preferences, pages: result.preferences.pages ?? {} };
      value.current = next; setPreferences(next); setWarning(result.warning?.message ?? null); loaded.current = true; finishedReading.current = true; setReady(true);
    }).catch(error => { if (alive) { setWarning(`${errorMessage(error)} 当前界面更改暂不保存，可重试读取偏好。`); finishedReading.current = true; setReady(true); } });
    const flush = () => { clearTimeout(timer.current); void save(); };
    window.addEventListener('pagehide', flush);
    return () => { alive = false; active.current = false; controller.abort(); clearTimeout(timer.current); window.removeEventListener('pagehide', flush); };
  }, [save, loadRevision]);
  const update = useCallback((mutate: (previous: UiPreferences) => UiPreferences, publish = true) => {
    if (!finishedReading.current) return;
    const next = mutate(value.current); value.current = next; if (publish) setPreferences(next);
    if (loaded.current) { dirty.current = true; clearTimeout(timer.current); timer.current = window.setTimeout(() => { pending.current = save(); }, 600); }
  }, [save]);
  const updatePage = useCallback((page: Page, patch: Partial<PagePreference>, publish = true) => update(previous => ({ ...previous, pages: { ...previous.pages, [page]: { ...defaultPage, ...previous.pages?.[page], ...patch } } }), publish), [update]);
  const reset = useCallback(() => { loaded.current = true; finishedReading.current = true; update(() => defaultPreferences); }, [update]);
  const flush = useCallback(async () => { clearTimeout(timer.current); if (pending.current) await pending.current; if (dirty.current) { pending.current = save(); await pending.current; } }, [save]);
  return { preferences, ready, warning, update, updatePage, reset, flush, retry: () => setLoadRevision(v => v + 1), page: (page: Page) => ({ ...defaultPage, ...value.current.pages?.[page] }) };
}
export type PreferencesController = ReturnType<typeof usePreferences>;
