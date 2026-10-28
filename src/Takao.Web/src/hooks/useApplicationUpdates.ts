import { useCallback, useEffect, useRef, useState } from 'react';
import { api, errorMessage } from '../api';
import type { ApplicationUpdateState } from '../types';

type UpdateAction = 'check' | 'download' | 'cancel' | 'now' | 'nextStart';
const activeStatus = (status: ApplicationUpdateState['status'] | undefined) => status === 'checking' || status === 'downloading' || status === 'installing';

export function useApplicationUpdates(enabled: boolean) {
  const [state, setState] = useState<ApplicationUpdateState | null>(null);
  const [reading, setReading] = useState(false);
  const [action, setAction] = useState<UpdateAction | null>(null);
  const [transportError, setTransportError] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const value = useRef<ApplicationUpdateState | null>(null);
  const generation = useRef(0);
  const lifetime = useRef<AbortController | null>(null);
  const pendingRead = useRef<{ generation: number; promise: Promise<ApplicationUpdateState | null> } | null>(null);
  const pendingAction = useRef(false);
  const mutation = useRef(0);
  const pollSoon = useRef<() => void>(() => {});

  const read = useCallback((force = false): Promise<ApplicationUpdateState | null> => {
    const controller = lifetime.current, current = generation.current, beforeMutation = mutation.current;
    if (!controller || controller.signal.aborted) return Promise.resolve(null);
    if (pendingAction.current && !force) return Promise.resolve(value.current);
    if (pendingRead.current?.generation === current) return pendingRead.current.promise;
    setReading(true);
    const promise = api.get<ApplicationUpdateState>('updates', controller.signal).then(result => {
      if (current === generation.current && beforeMutation === mutation.current && !controller.signal.aborted) { value.current = result; setState(result); setTransportError(null); }
      return result;
    }).catch(error => {
      if (current === generation.current && beforeMutation === mutation.current && !controller.signal.aborted) setTransportError(errorMessage(error));
      return null;
    }).finally(() => {
      if (pendingRead.current?.promise === promise) pendingRead.current = null;
      if (current === generation.current && !controller.signal.aborted) setReading(false);
    });
    pendingRead.current = { generation: current, promise };
    return promise;
  }, []);

  useEffect(() => {
    const current = ++generation.current;
    const controller = new AbortController(); lifetime.current = controller;
    pendingAction.current = false; setAction(null);
    let timer: number | undefined;
    if (!enabled) { value.current = null; setState(null); setReading(false); setTransportError(null); setActionError(null); }
    else {
      const schedule = (delay: number) => { clearTimeout(timer); timer = window.setTimeout(() => { void poll(); }, delay); };
      const poll = async () => {
        const result = await read();
        if (current !== generation.current || controller.signal.aborted) return;
        schedule(result === null ? 5000 : activeStatus(value.current?.status) || pendingAction.current ? 1000 : 30000);
      };
      pollSoon.current = () => schedule(1000);
      void poll();
    }
    return () => { controller.abort(); clearTimeout(timer); pollSoon.current = () => {}; if (current === generation.current) generation.current++; };
  }, [enabled, read]);

  const perform = useCallback(async (command: UpdateAction): Promise<boolean> => {
    const controller = lifetime.current, current = generation.current, target = value.current;
    if (!enabled || !controller || controller.signal.aborted || pendingAction.current || !target) return false;
    const allowed = command === 'check' ? target.canCheck : command === 'download' ? target.canDownload : command === 'cancel' ? target.canCancel : target.canInstall;
    if (!allowed) return false;
    pendingAction.current = true; mutation.current++; setAction(command); setActionError(null);
    try {
      const route = command === 'now' || command === 'nextStart' ? 'install' : command;
      const body = command === 'check' ? {} : { expectedRevision: target.revision, ...(route === 'install' ? { mode: command } : {}) };
      const result = await api.post<ApplicationUpdateState>(`updates/${route}`, body, controller.signal, route === 'install' ? 60000 : 12000);
      if (current !== generation.current || controller.signal.aborted) return false;
      mutation.current++;
      value.current = result; setState(result); setTransportError(null);
      return true;
    } catch (error) {
      if (current !== generation.current || controller.signal.aborted) return false;
      const message = errorMessage(error);
      // A write may already have taken effect. Re-read once; only a new user
      // action may submit another download, scheduling or installation request.
      mutation.current++;
      if (pendingRead.current) await pendingRead.current.promise;
      await read(true);
      if (current === generation.current && !controller.signal.aborted) setActionError(message);
      return false;
    } finally {
      if (current === generation.current && !controller.signal.aborted) { pendingAction.current = false; setAction(null); pollSoon.current(); }
    }
  }, [enabled, read]);

  const refresh = useCallback(async () => { setActionError(null); return read(); }, [read]);
  return { state, reading, action, busy: action !== null, error: actionError ?? transportError, refresh,
    check: () => perform('check'), download: () => perform('download'), cancel: () => perform('cancel'), install: (mode: 'now' | 'nextStart') => perform(mode) };
}
export type ApplicationUpdatesController = ReturnType<typeof useApplicationUpdates>;
