import type { Bootstrap } from './types';

export class ApiError extends Error {
  constructor(message: string, public code: string, public status: number) { super(message); }
}
let token: string | null = null;
async function request<T>(route: string, init: RequestInit = {}, signal?: AbortSignal): Promise<T> {
  const controller = new AbortController();
  const stop = () => controller.abort();
  signal?.addEventListener('abort', stop, { once: true });
  if (signal?.aborted) controller.abort();
  const timeout = window.setTimeout(() => controller.abort(new Error('本机服务响应超时，请重试。')), 9000);
  try {
    const response = await fetch(`/api/${route}`, { ...init, cache: 'no-store', signal: controller.signal });
    const value = await response.json();
    if (!response.ok) throw new ApiError(value.error?.message ?? '本机服务请求失败。', value.error?.code ?? 'error', response.status);
    return value as T;
  } finally { clearTimeout(timeout); signal?.removeEventListener('abort', stop); }
}
export const api = {
  async bootstrap(signal?: AbortSignal) { const result = await request<Bootstrap>('bootstrap', {}, signal); token = result.token; return result; },
  get<T>(route: string, signal?: AbortSignal) { return request<T>(route, {}, signal); },
  async post<T>(route: string, body: unknown, signal?: AbortSignal): Promise<T> {
    if (!token) await api.bootstrap(signal);
    const send = () => request<T>(route, { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-TaskManager-Token': token! }, body: JSON.stringify(body) }, signal);
    try { return await send(); }
    catch (error) {
      if (error instanceof ApiError && error.code === 'invalidToken') { await api.bootstrap(signal); return send(); }
      throw error;
    }
  },
};
export function errorMessage(error: unknown) { return error instanceof TypeError ? '无法连接本机服务，请检查主机状态后重试。' : error instanceof Error ? error.message : '无法连接本机服务，请重试。'; }
