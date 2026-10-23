import { useEffect, useState } from 'react';
import { Info, RefreshCw, ShieldCheck } from 'lucide-react';
import { api, ApiError, errorMessage } from '../api';
import { Modal } from '../components';
import { dateTime } from '../format';
import type { TaskManagerIntegrationResult, TaskManagerIntegrationState } from '../types';
import type { Notify } from './pageShared';

const route = 'desktop/task-manager-integration';
export default function TaskManagerIntegration({ connected, notify }: { connected: boolean; notify: Notify }) {
  const [state, setState] = useState<TaskManagerIntegrationState | null>(null);
  const [busy, setBusy] = useState(false), [error, setError] = useState<string | null>(null);
  const [confirmation, setConfirmation] = useState<{ enabled: boolean; target: TaskManagerIntegrationState } | null>(null);
  const [dialogError, setDialogError] = useState<string | null>(null);
  useEffect(() => {
    const controller = new AbortController();
    void api.get<TaskManagerIntegrationState>(route, controller.signal).then(setState).catch(e => { if (!controller.signal.aborted) setError(errorMessage(e)); });
    return () => controller.abort();
  }, []);
  const refresh = async () => {
    setBusy(true);
    try { setState(await api.get<TaskManagerIntegrationState>(route)); setError(null); }
    catch (e) { setError(errorMessage(e)); }
    finally { setBusy(false); }
  };
  const open = (enabled: boolean) => { if (state) { setDialogError(null); setConfirmation({ enabled, target: state }); } };
  const apply = async () => {
    if (!confirmation) return;
    setBusy(true); setDialogError(null);
    try {
      const result = await api.post<TaskManagerIntegrationResult>(route, { enabled: confirmation.enabled, expectedRevision: confirmation.target.revision });
      setState(result.state); setError(null); setConfirmation(null); notify(result.message);
    } catch (e) {
      const message = errorMessage(e); setDialogError(message); setError(message);
      try { setState(await api.get<TaskManagerIntegrationState>(route)); } catch { /* Preserve the last read state with the explicit failed operation. */ }
      if (e instanceof ApiError && e.status === 409) { setConfirmation(null); notify(message, true); }
    } finally { setBusy(false); }
  };
  const label = state ? ({ disabled: '未接管', enabled: '已由本目录程序接管', conflict: '存在其他或特殊配置', unavailable: '实际状态不可读取' })[state.status] : '正在读取实际状态';
  const adminReason = state?.writeError?.message ?? '注册和取消需要当前计算机的管理员写权限；请以管理员身份运行本程序后重新读取，不会自动提权。';
  const allowWrite = Boolean(state?.canWrite && connected && !busy);
  return <>
    <section className="settings-section" aria-labelledby="task-manager-integration-title">
      <h3 id="task-manager-integration-title">任务栏右键中的任务管理器</h3>
      <div className="setting-row integration-setting">
        <div>
          <strong>接管原“任务管理器”入口</strong>
          <p>这会让屏幕底部任务栏右键菜单中的“任务管理器”打开蓝域，同时影响 Ctrl+Shift+Esc 等启动系统任务管理器的方式，不是新增一个任意菜单项。</p>
          <p>范围：当前计算机（所有用户） · 注册和取消需要管理员写权限 · 默认关闭，不自动提权。</p>
          <p><span className={`status-badge ${state?.status === 'enabled' ? 'success' : state?.status === 'conflict' || state?.status === 'unavailable' ? 'warning' : ''}`}><ShieldCheck aria-hidden="true"/>{label}</span></p>
          {state && <p>最近核验：{dateTime(state.checkedAtUtc)} · {state.canWrite === true ? '当前账户具有实际写权限' : state.canWrite === false ? '当前账户没有写权限' : '写权限未能确认'}</p>}
          {state?.conflictReason && <p className="metric-reasons">{state.conflictReason}</p>}
          {state?.readError && <p className="form-error" role="alert">{state.readError.message}</p>}
          {state?.canWrite !== true && state && <p className="metric-reasons">{adminReason}</p>}
          {state && !state.executableAvailable && <p className="metric-reasons">当前目录缺少 TaskManager.Host.exe，不能启用接管；请恢复完整程序目录。</p>}
          <p>启用前请把完整程序目录放在固定、且其他需要使用的账户可读取的位置。移动或删除目录前，先在原目录取消接管，避免 Windows 入口指向失效路径。</p>
          {state && <p className="integration-path">本目录程序：<span>{state.executablePath}</span></p>}
          {state?.currentDebugger && !state.ownsDebugger && <p className="integration-path">当前其他接管命令：<span>{state.currentDebugger}</span></p>}
          {error && <p className="form-error" role="alert">{error}</p>}
        </div>
        <div className="setting-buttons">
          <button className="command-button bordered" disabled={!connected || busy} onClick={() => void refresh()}><RefreshCw aria-hidden="true"/>{busy ? '正在核验…' : '刷新入口实际状态'}</button>
          {state?.ownsDebugger ? <button className="command-button bordered" disabled={!state.canDisable || !allowWrite} title={!allowWrite ? adminReason : undefined} onClick={() => open(false)}>取消接管…</button> : <button className="command-button primary" disabled={!state?.canEnable || !allowWrite} title={!allowWrite ? adminReason : state?.conflictReason ?? undefined} onClick={() => open(true)}>启用接管…</button>}
          <button className="text-button" disabled={!state || busy} onClick={() => open(!state?.ownsDebugger)}>{state?.ownsDebugger ? '查看取消接管的影响' : '查看启用接管的影响'}</button>
        </div>
      </div>
    </section>
    {confirmation && <Modal title={confirmation.enabled ? '启用任务管理器入口接管？' : '取消任务管理器入口接管？'} description="此操作改变的是Windows原有任务管理器入口，不是新增菜单项。" onClose={() => { setConfirmation(null); setDialogError(null); }} busy={busy} wide>
      <div className="integration-confirmation">
        <p><Info aria-hidden="true"/>{confirmation.enabled ? '任务栏右键中的“任务管理器”、Ctrl+Shift+Esc 等入口将打开本目录中的蓝域任务管理器。' : '仅撤销本目录程序的接管；其余Windows与其他工具配置保持不变，实际入口以刷新结果为准。'}</p>
        <p>影响当前计算机的所有用户，需要管理员写权限；不会自动请求UAC或覆盖其他工具。</p>
        <p>启用前请把完整目录放在固定、且其他需要使用的账户可读取的位置。移动或删除程序目录前，务必先在原目录取消接管；仅撤销本程序的Debugger，保留其他配置。</p>
        <p className="integration-path">{confirmation.target.executablePath}</p>
        {state?.canWrite !== true && <p className="metric-reasons">{adminReason}</p>}
        {state?.conflictReason && <p className="metric-reasons">{state.conflictReason}</p>}
        {dialogError && <p className="form-error" role="alert">{dialogError}</p>}
      </div>
      <div className="modal-actions"><button className="command-button bordered" disabled={busy} onClick={() => setConfirmation(null)}>暂不更改</button><button className="command-button primary" disabled={busy || !connected || !(confirmation.enabled ? state?.canEnable : state?.canDisable)} onClick={() => void apply()}>{busy ? '正在写入并读回…' : confirmation.enabled ? '确认启用接管' : '确认取消接管'}</button></div>
    </Modal>}
  </>;
}
