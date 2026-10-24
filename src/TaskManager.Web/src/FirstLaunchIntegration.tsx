import { useEffect, useState } from 'react';
import { RefreshCw, ShieldCheck } from 'lucide-react';
import { api, errorMessage } from './api';
import { Modal } from './components';
import type { PreferencesController } from './hooks/usePreferences';
import type { TaskManagerIntegrationResult, TaskManagerIntegrationState } from './types';
import type { Notify } from './pages/pageShared';

const route = 'desktop/task-manager-integration';
export default function FirstLaunchIntegration({ preferences, connected, notify, onSettings }: { preferences: PreferencesController; connected: boolean; notify: Notify; onSettings: () => void }) {
  const required = !preferences.preferences.taskManagerPromptDismissed;
  const [state, setState] = useState<TaskManagerIntegrationState | null>(null);
  const [reading, setReading] = useState(true), [writing, setWriting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    if (!required) return;
    const controller = new AbortController();
    setReading(true); setState(null); setError(null);
    void api.get<TaskManagerIntegrationState>(route, controller.signal).then(setState).catch(caught => {
      if (!controller.signal.aborted) setError(errorMessage(caught));
    }).finally(() => { if (!controller.signal.aborted) setReading(false); });
    return () => controller.abort();
  }, [required]);
  const refresh = async () => {
    setReading(true); setState(null); setError(null);
    try { setState(await api.get<TaskManagerIntegrationState>(route)); }
    catch (caught) { setError(errorMessage(caught)); }
    finally { setReading(false); }
  };
  const complete = async (settings = false, message?: string) => {
    const saved = await preferences.completeTaskManagerPrompt();
    if (settings) onSettings();
    if (!saved) notify(`${message ? `${message} ` : ''}选择未保存，重启可能再次提示。请检查偏好保存提示。`, true);
    else if (message) notify(message);
  };
  const replace = async () => {
    if (!state?.canEnable || state.canWrite !== true || !state.executableAvailable || writing || reading || !connected) return;
    setWriting(true); setError(null);
    try {
      const result = await api.post<TaskManagerIntegrationResult>(route, { enabled: true, expectedRevision: state.revision });
      setState(result.state);
      if (result.state.status !== 'enabled' || result.state.enabled !== true) {
        setError('尚未读回确认替换成功。请重新读取实际状态，再决定下一步。'); return;
      }
      await complete(false, '已替换标准任务管理器，可在设置中取消接管。');
    } catch (caught) {
      const message = errorMessage(caught); setError(message); setState(null);
      // A failed or timed-out write may already have taken effect. Only re-read;
      // another write always requires a new, explicit user action.
      try { setState(await api.get<TaskManagerIntegrationState>(route)); }
      catch (readError) { setError(`${message} 重新核验失败：${errorMessage(readError)}`); }
    } finally { setWriting(false); }
  };
  if (!required) return null;
  const label = reading ? '正在读取系统入口…' : state ? ({ disabled: '尚未替换标准任务管理器', enabled: '本目录程序已替换标准任务管理器', migrationRequired: '旧接管需要修复', conflict: '已有其他工具或目录接管此入口', unavailable: '暂时无法确认系统入口' })[state.status] : '暂时无法确认系统入口';
  const canReplace = Boolean(connected && !reading && !writing && state?.status === 'disabled' && state.canEnable && state.canWrite === true && state.executableAvailable);
  return <Modal title="替换标准任务管理器？" description="让 Windows 的任务管理器入口打开蓝域任务管理器。" className="first-launch-modal" busy={writing} onClose={() => { if (!writing) void complete(); }}>
    <div className="first-launch-content">
      <p>替换后，任务栏右键的“任务管理器”、Ctrl+Shift+Esc 等入口将打开蓝域。</p>
      <p>影响当前计算机的所有用户；以后从这些入口启动时，Windows 会请求管理员授权。</p>
      <p>可在“设置 → 任务管理器入口”取消接管，恢复标准入口。</p>
      <p className="first-launch-location-note">请先将完整程序目录放在固定、其他使用账户可读取的位置。移动或删除前，先取消接管。</p>
      <div className="first-launch-state" role="status"><ShieldCheck size={20} aria-hidden="true"/><span>{label}</span></div>
      {state?.migrationRequired && <p className="metric-reasons">请前往设置，明确选择修复或取消旧接管。</p>}
      {state?.conflictReason && <p className="metric-reasons">{state.conflictReason}</p>}
      {state?.readError && <p className="form-error" role="alert">{state.readError.message}</p>}
      {state && state.canWrite !== true && <p className="metric-reasons">{state.writeError?.message ?? '当前写权限未能确认，请重新读取实际状态。'}</p>}
      {state && !state.executableAvailable && <p className="metric-reasons">当前目录缺少启动器或主程序，请先恢复完整程序目录。</p>}
      {state && <details className="first-launch-location"><summary>当前程序位置</summary><p className="integration-path">{state.executablePath}</p></details>}
      {error && <p className="form-error" role="alert">{error}</p>}
      <div className="first-launch-links"><button className="text-button" disabled={reading || writing || !connected} onClick={() => void refresh()}><RefreshCw size={14} aria-hidden="true"/>重新读取</button><button className="text-button" disabled={writing} onClick={() => void complete(true)}>前往设置管理</button></div>
    </div>
    <div className="modal-actions"><button autoFocus className="command-button bordered" disabled={writing} onClick={() => void complete()}>{state?.status === 'enabled' ? '继续使用' : '暂不替换'}</button>{state?.status !== 'enabled' && <button className="command-button primary" disabled={!canReplace} onClick={() => void replace()}>{writing ? '正在替换并核验…' : '替换标准任务管理器'}</button>}</div>
  </Modal>;
}
