import { useState, type FormEvent } from 'react';
import { FolderOpen } from 'lucide-react';
import { api, errorMessage } from '../api';
import { Modal } from '../components';
import type { ProcessResult } from '../types';
export default function RunDialog({ desktop, onClose, onCreated }: { desktop: boolean; onClose: () => void; onCreated: (result: ProcessResult) => void }) {
  const [executable, setExecutable] = useState(''); const [argumentsText, setArguments] = useState(''); const [busy, setBusy] = useState(false); const [error, setError] = useState<string | null>(null);
  const browse = async () => {
    if (busy) return; setBusy(true); setError(null);
    try { const result = await api.post<{ cancelled: boolean; path: string | null }>('desktop/browse-executable', {}); if (!result.cancelled && result.path) setExecutable(result.path); }
    catch (caught) { setError(errorMessage(caught)); } finally { setBusy(false); }
  };
  const run = async (event: FormEvent) => {
    event.preventDefault(); if (busy) return; setBusy(true); setError(null);
    try { const result = await api.post<ProcessResult>('processes/start', { executable: executable.trim(), arguments: argumentsText }); onCreated(result); onClose(); }
    catch (caught) { setError(errorMessage(caught)); } finally { setBusy(false); }
  };
  return <Modal title="运行新任务" description="输入程序名称或完整路径，由 Windows 直接创建进程。" onClose={onClose} busy={busy}><form onSubmit={event => void run(event)}>
    <label className="field-label" htmlFor="executable">程序名称或完整路径</label><div className="input-action"><input id="executable" className="form-input" required autoFocus value={executable} onChange={event => setExecutable(event.target.value)} disabled={busy} placeholder="例如 notepad.exe"/><button type="button" className="command-button bordered" disabled={!desktop || busy} title={!desktop ? '原生文件选择器需要 Electron 桌面模式' : undefined} onClick={() => void browse()}><FolderOpen size={16} aria-hidden="true"/>浏览…</button></div>
    <label className="field-label" htmlFor="arguments">参数（可选）</label><input id="arguments" className="form-input" value={argumentsText} onChange={event => setArguments(event.target.value)} disabled={busy}/><p className="field-hint">程序与参数分开传递；新任务默认继承本程序的管理员权限。取消浏览会保留原输入。</p>
    {error && <div className="form-error" role="alert">{error}</div>}<div className="modal-actions"><button type="button" className="command-button bordered" disabled={busy} onClick={onClose}>取消</button><button className="command-button primary" disabled={busy}>{busy ? '正在处理…' : '运行'}</button></div>
  </form></Modal>;
}
