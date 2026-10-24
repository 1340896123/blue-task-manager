import { useCallback, useEffect, useRef, useState } from 'react';
import { api, errorMessage } from '../api';
import type { Page, PagePreference, PreferencesResult, UiPreferences } from '../types';
export const defaultPage: PagePreference = { query: '', sortKey: 'name', sortDirection: 'ascending', visibleColumns: null, columnWidths: null, filters: null, scrollTop: 0, selectedIdentity: null, resourceType: null, resourceId: null, cpuGraphMode: 'total' };
export const defaultPreferences: UiPreferences = { version: 1, density: 'compact', sidebarCollapsed: false, processView: 'flat', searchQuery: '', taskManagerPromptDismissed: false, pages: {} };
export function usePreferences() {
  const [preferences, setPreferences] = useState<UiPreferences>(defaultPreferences); const [ready, setReady] = useState(false); const [warning, setWarning] = useState<string | null>(null);
  const [loadRevision, setLoadRevision] = useState(0); const [loadedSuccessfully, setLoadedSuccessfully] = useState(false);
  const value = useRef(preferences); const loaded = useRef(false); const timer = useRef<number | undefined>(undefined);
  const dirty = useRef(false); const active = useRef(true); const finishedReading = useRef(false); const pending = useRef<Promise<boolean> | null>(null); const promptChoicePending = useRef(false);
  const save = useCallback((): Promise<boolean> => {
    if (pending.current) return pending.current;
    if (!loaded.current) return Promise.resolve(false);
    if (!dirty.current) return Promise.resolve(true);
    dirty.current = false; let failed = false;
    const operation = (async () => {
      try {
        const { window: _window, ...body } = value.current;
        const result = await api.post<PreferencesResult>('preferences', body);
        if (body.taskManagerPromptDismissed) promptChoicePending.current = false;
        if (active.current) setWarning(result.warning?.message ?? null);
        return true;
      } catch (error) {
        dirty.current = true; failed = true;
        if (active.current) setWarning(`${promptChoicePending.current ? '选择未保存，重启可能再次提示。' : ''}${errorMessage(error)}`);
        return false;
      } finally {
        pending.current = null;
        if (dirty.current && active.current && !failed) timer.current = window.setTimeout(() => { void save(); }, 600);
      }
    })();
    pending.current = operation; return operation;
  }, []);
  useEffect(() => {
    active.current = true; setLoadedSuccessfully(false); let alive = true; const controller = new AbortController();
    void api.get<PreferencesResult>('preferences', controller.signal).then(result => {
      if (!alive) return;
      const next = { ...defaultPreferences, ...result.preferences, searchQuery: result.preferences.searchQuery ?? result.preferences.pages?.processes?.query ?? '', pages: result.preferences.pages ?? {} };
      value.current = next; setPreferences(next); setWarning(result.warning?.message ?? null); loaded.current = true; finishedReading.current = true; setLoadedSuccessfully(true); setReady(true);
    }).catch(error => { if (alive) { setWarning(`${errorMessage(error)} 当前界面更改暂不保存，可重试读取偏好。`); finishedReading.current = true; setReady(true); } });
    const flush = () => { clearTimeout(timer.current); void save(); };
    window.addEventListener('pagehide', flush);
    return () => { alive = false; active.current = false; controller.abort(); clearTimeout(timer.current); window.removeEventListener('pagehide', flush); };
  }, [save, loadRevision]);
  const update = useCallback((mutate: (previous: UiPreferences) => UiPreferences, publish = true) => {
    if (!finishedReading.current) return;
    const next = mutate(value.current); value.current = next; if (publish) setPreferences(next);
    if (loaded.current) { dirty.current = true; clearTimeout(timer.current); timer.current = window.setTimeout(() => { void save(); }, 600); }
  }, [save]);
  const updatePage = useCallback((page: Page, patch: Partial<PagePreference>, publish = true) => update(previous => {
    const { query, ...pagePatch } = patch;
    const changed = query !== undefined && query !== previous.searchQuery;
    const pages = changed ? Object.fromEntries(Object.entries(previous.pages ?? {}).map(([key, value]) => [key, { ...value, scrollTop: 0 }])) : previous.pages;
    return { ...previous, searchQuery: query ?? previous.searchQuery ?? '', pages: { ...pages, [page]: { ...defaultPage, ...pages?.[page], ...pagePatch, ...(changed ? { scrollTop: 0 } : {}) } } };
  }, publish || patch.query !== undefined), [update]);
  const reset = useCallback(() => { loaded.current = true; finishedReading.current = true; update(previous => ({ ...defaultPreferences, taskManagerPromptDismissed: previous.taskManagerPromptDismissed })); }, [update]);
  const flush = useCallback(async () => {
    clearTimeout(timer.current);
    if (pending.current && !await pending.current) return false;
    clearTimeout(timer.current);
    while (dirty.current) { if (!await save()) return false; clearTimeout(timer.current); }
    return loaded.current;
  }, [save]);
  const completeTaskManagerPrompt = useCallback(async () => {
    promptChoicePending.current = true;
    update(previous => ({ ...previous, taskManagerPromptDismissed: true }));
    return flush();
  }, [update, flush]);
  return { preferences, ready, loadedSuccessfully, warning, update, updatePage, reset, flush, completeTaskManagerPrompt, retry: () => setLoadRevision(v => v + 1), page: (page: Page) => ({ ...defaultPage, ...value.current.pages?.[page], query: value.current.searchQuery ?? '' }) };
}
export type PreferencesController = ReturnType<typeof usePreferences>;
