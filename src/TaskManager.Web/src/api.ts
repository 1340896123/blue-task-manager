import type { Bootstrap, HwInfoSensors, Process, Snapshot } from './types';
export class ApiError extends Error {
  constructor(message: string, public code: string, public status: number, public nativeErrorCode?: number | null) { super(message); }
}
export type RequestOptions = { signal?: AbortSignal; timeoutMs?: number | null };
export type ApplicationIconKind = 'process' | 'history' | 'startup' | 'service';
let token: string | null = null;
async function request<T>(route: string, init: RequestInit = {}, options: RequestOptions = {}, kind: 'json' | 'blob' | 'snapshot' = 'json'): Promise<{ value: T; etag: string | null; unchanged: boolean }> {
  const controller = new AbortController();
  const stop = () => controller.abort(options.signal?.reason);
  options.signal?.addEventListener('abort', stop, { once: true });
  if (options.signal?.aborted) stop();
  const timeoutMs = options.timeoutMs === undefined ? 9000 : options.timeoutMs;
  const timeout = timeoutMs === null ? undefined : window.setTimeout(() => controller.abort(new Error('本机服务响应超时。操作可能已生效，请检查实际状态后再操作。')), timeoutMs);
  try {
    const response = await fetch(`/api/${route}`, { ...init, cache: 'no-store', signal: controller.signal });
    const etag = response.headers.get('ETag');
    if (response.status === 304 && kind === 'snapshot') return { value: null as T, etag, unchanged: true };
    if (!response.ok) {
      const value = await response.json().catch(() => null);
      throw new ApiError(value?.error?.message ?? `本机服务请求失败（${response.status}）。`, value?.error?.code ?? 'error', response.status, value?.error?.nativeErrorCode);
    }
    const value = response.status === 204 ? null : kind === 'blob' ? await response.blob() : await response.json();
    return { value: value as T, etag, unchanged: false };
  } finally { if (timeout !== undefined) clearTimeout(timeout); options.signal?.removeEventListener('abort', stop); }
}
export const identityRoute = (target: Pick<Process, 'pid' | 'creationTimeFileTime'>, suffix: string) => `processes/${target.pid}/${suffix}?creationTimeFileTime=${encodeURIComponent(target.creationTimeFileTime ?? '')}`;
export const api = {
  async bootstrap(signal?: AbortSignal) { const { value } = await request<Bootstrap>('bootstrap', {}, { signal }); token = value.token; return value; },
  async get<T>(route: string, signal?: AbortSignal, timeoutMs?: number | null) { return (await request<T>(route, {}, { signal, timeoutMs })).value; },
  snapshot(etag: string | null, signal?: AbortSignal) { return request<Snapshot>('snapshot', { headers: etag ? { 'If-None-Match': etag } : {} }, { signal }, 'snapshot'); },
  async hwInfoSensors(signal: AbortSignal) { return (await request<HwInfoSensors>('hwinfo/sensors', {}, { signal })).value; },
  async applicationIcon(kind: ApplicationIconKind, id: string, signal: AbortSignal) { return (await request<Blob | null>(`application-icon?kind=${kind}&id=${encodeURIComponent(id)}`, {}, { signal, timeoutMs: 5000 }, 'blob')).value; },
  async post<T>(route: string, body: unknown, signal?: AbortSignal, timeoutMs?: number | null): Promise<T> {
    if (!token) await api.bootstrap(signal);
    const inferred = route === 'desktop/browse-executable' ? null : /\/(control|terminate-tree)$/.test(route) ? 20000 : undefined;
    const send = async () => (await request<T>(route, { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-TaskManager-Token': token! }, body: JSON.stringify(body) }, { signal, timeoutMs: timeoutMs === undefined ? inferred : timeoutMs })).value;
    try { return await send(); }
    catch (error) {
      // Token rejection happens before execution. Uncertain writes are never retried.
      if (error instanceof ApiError && error.code === 'invalidToken') { await api.bootstrap(signal); return send(); }
      throw error;
    }
  },
};
export function errorMessage(error: unknown) { return error instanceof TypeError ? '无法连接本机服务，请检查主机状态后重试。' : error instanceof Error ? error.message : '无法连接本机服务，请重试。'; }
