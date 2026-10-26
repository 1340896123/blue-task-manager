import { useEffect, useRef } from 'react';
import { CheckCircle2, CircleAlert, Download, LoaderCircle, RefreshCw } from 'lucide-react';
import { Modal } from './components';
import { bytes, dateTime } from './format';
import type { ApplicationUpdateState } from './types';
import type { ApplicationUpdatesController } from './hooks/useApplicationUpdates';
import type { Notify } from './pages/pageShared';

export function updateStatusText(state: ApplicationUpdateState | null, desktop: boolean) {
  if (!desktop) return '应用更新仅在 Electron 桌面中可用。';
  if (!state) return '正在读取更新状态…';
  const version = state.candidateVersion;
  return ({ checking: '正在检查 GitHub Releases…', upToDate: '当前已是最新可确认版本。', available: `发现新版本 ${version ?? ''}`,
    downloading: `正在后台下载 ${version ?? ''}`, ready: `${version ?? '更新'} 已下载并通过校验。`, scheduled: `${version ?? '更新'} 已下载，下次启动自动更新。`,
    installing: '正在准备更新，应用即将关闭并重启…', error: '更新未完成，请查看原因后重试。', unavailable: '应用更新暂不可用。' })[state.status];
}
const progressText = (state: ApplicationUpdateState) => state.progress === null ? `${bytes(state.downloadedBytes)} 已下载` : `${Math.floor(state.progress)}% · ${bytes(state.downloadedBytes)} / ${bytes(state.totalBytes)}`;

export function UpdateSidebarButton({ updates, onOpen, notify, connected }: { updates: ApplicationUpdatesController; onOpen: () => void; notify: Notify; connected: boolean }) {
  const state = updates.state;
  if (!state?.candidateVersion || !(state.canDownload || state.canInstall || ['downloading', 'ready', 'scheduled', 'installing'].includes(state.status))) return null;
  const downloading = state.status === 'downloading', installing = state.status === 'installing';
  const downloaded = state.status === 'ready' || state.status === 'scheduled' || state.canInstall;
  const startsDownload = state.canDownload && !downloaded && !downloading;
  const updateError = updates.error ?? state.error?.message;
  const label = state.status === 'error' ? `${state.candidateVersion} 更新未完成，${startsDownload ? '重新下载' : '查看原因并重试'}${updateError ? `：${updateError}` : ''}`
    : downloading ? `正在下载更新 ${state.candidateVersion}，${progressText(state)}，查看下载进度`
    : state.status === 'scheduled' ? `${state.candidateVersion} 已下载，下次启动自动更新`
      : installing ? `正在准备更新 ${state.candidateVersion}` : downloaded ? `查看已下载更新 ${state.candidateVersion}` : `下载更新 ${state.candidateVersion}`;
  return <button className={`icon-button update-sidebar-button ${state.status === 'error' ? 'failed' : downloaded ? 'downloaded' : ''}`} title={label} aria-label={label} disabled={!connected || updates.busy || installing} onClick={() => {
    if (!startsDownload) { onOpen(); return; }
    void updates.download().then(started => { notify(started ? `正在后台下载 ${state.candidateVersion}，可以继续使用任务管理器。` : '未能开始下载，请在设置 → 应用更新查看原因。', !started); });
  }}>
    {downloading || installing ? <LoaderCircle size={20} className="update-loader" aria-hidden="true"/> : state.status === 'error' ? <CircleAlert size={20} aria-hidden="true"/> : downloaded ? <CheckCircle2 size={20} aria-hidden="true"/> : <Download size={20} aria-hidden="true"/>}
    {downloading && state.progress !== null && <span className="update-icon-percent" aria-hidden="true">{Math.floor(state.progress)}%</span>}
  </button>;
}

export function UpdateSettingsRow({ updates, onOpen, notify, desktop, connected }: { updates: ApplicationUpdatesController; onOpen: () => void; notify: Notify; desktop: boolean; connected: boolean }) {
  const state = updates.state, pending = state && (state.status === 'downloading' || state.status === 'ready' || state.status === 'scheduled' || state.canInstall);
  const error = updates.error ?? state?.error?.message;
  return <div className="setting-row update-setting">
    <div><strong>GitHub Release 更新</strong><p>{updateStatusText(state, desktop)}</p>
      {state && <p>当前版本：{state.currentReleaseTag ?? state.currentVersion}{state.checkedAtUtc ? ` · 最近检查：${dateTime(state.checkedAtUtc)}` : ' · 尚未完成检查'}</p>}
      {state?.status === 'downloading' && <p className="numeric">{progressText(state)}</p>}
      {state?.installReason && <p className="metric-reasons">{state.installReason}</p>}
      {error && <p className="form-error" role="alert">{error}</p>}
    </div>
    <div className="setting-buttons">
      <button className="command-button bordered" disabled={!desktop || !connected || updates.busy || !state?.canCheck} onClick={() => void updates.check().then(started => { if (started) notify('正在检查 GitHub Releases。'); })}><RefreshCw size={16} aria-hidden="true"/>{state?.status === 'checking' ? '正在检查…' : '检查更新'}</button>
      {pending ? <button className="command-button bordered" disabled={!connected || updates.busy} onClick={onOpen}>{state?.status === 'downloading' ? '查看下载进度' : '查看已下载更新'}</button>
        : state?.canDownload && <button className="command-button primary" disabled={!connected || updates.busy} onClick={() => void updates.download().then(started => { if (started) notify(`正在后台下载 ${state.candidateVersion}。`); })}><Download size={16} aria-hidden="true"/>下载更新</button>}
      {updates.error && <button className="text-button" disabled={!desktop || !connected || updates.busy || updates.reading} onClick={() => void updates.refresh()}>重新读取状态</button>}
    </div>
  </div>;
}

export function UpdateDialog({ updates, onClose, notify, connected }: { updates: ApplicationUpdatesController; onClose: () => void; notify: Notify; connected: boolean }) {
  const nextStartButton = useRef<HTMLButtonElement>(null);
  const previousStatus = useRef(updates.state?.status);
  const state = updates.state;
  useEffect(() => {
    if (state?.status === 'ready' && previousStatus.current === 'downloading') nextStartButton.current?.focus();
    previousStatus.current = state?.status;
  }, [state?.status]);
  if (!state?.candidateVersion) return null;
  const downloading = state.status === 'downloading', scheduled = state.status === 'scheduled', installing = state.status === 'installing';
  const busy = updates.busy || installing;
  const title = downloading ? '正在下载更新' : scheduled ? '已安排下次启动更新' : installing ? '正在准备更新' : state.status === 'error' ? '更新未完成' : state.canInstall ? '更新已下载' : '下载更新';
  const install = async (mode: 'now' | 'nextStart') => {
    if (await updates.install(mode) && mode === 'nextStart') { onClose(); notify(`${state.candidateVersion} 将在下次正常启动时自动更新。`); }
  };
  const cancel = async () => {
    if (await updates.cancel()) {
      onClose(); notify(downloading ? '已请求取消后台下载。' : scheduled ? '已取消下次启动自动更新，安装包仍可使用。' : '已清除下载的更新，可重新检查或下载。');
    }
  };
  return <Modal title={title} description={state.candidateVersion} onClose={onClose} busy={busy} className="update-modal" initialFocusRef={nextStartButton}>
    <div className="update-dialog-content">
      {downloading ? <><p>更新正在后台下载，你可以关闭此窗口继续使用任务管理器。</p><progress className="update-progress" max={100} value={state.progress ?? undefined} aria-label="更新下载进度"/><p className="numeric">{progressText(state)}</p></>
        : installing ? <p>正在校验安装包并启动更新。应用关闭后将完成安装，成功后自动重启。</p>
          : state.canInstall ? <><p>安装包已下载并完成校验。</p><p>{scheduled ? '已选择下次正常启动时自动更新。你也可以现在更新，或取消这次安排。' : '立即更新会关闭当前任务管理器，完成安装后自动重启；选择下次启动自动更新可继续使用。'}</p></>
            : <p>下载尚未完成。请查看错误原因，再重新下载。</p>}
      {state.installReason && <p className="metric-reasons">{state.installReason}</p>}
      {(updates.error ?? state.error?.message) && <p className="form-error" role="alert">{updates.error ?? state.error?.message}</p>}
    </div>
    <div className="modal-actions">
      {downloading ? <><button ref={nextStartButton} className="command-button bordered" disabled={busy} onClick={onClose}>后台继续下载</button><button className="command-button" disabled={!connected || busy || !state.canCancel} onClick={() => void cancel()}>取消下载</button></>
        : scheduled ? <><button ref={nextStartButton} className="command-button bordered" disabled={busy} onClick={onClose}>继续使用</button><button className="command-button" disabled={!connected || busy || !state.canCancel} onClick={() => void cancel()}>取消下次安排</button><button className="command-button primary" disabled={!connected || busy || !state.canInstall} onClick={() => void install('now')}>立即更新</button></>
          : state.canInstall || installing ? <>{state.status === 'error' && state.canDownload && <button className="command-button" disabled={!connected || busy} onClick={() => void updates.download()}>重新下载</button>}<button className="command-button primary" disabled={!connected || busy || !state.canInstall} onClick={() => void install('now')}>{busy ? '正在准备更新…' : '立即更新'}</button><button ref={nextStartButton} className="command-button bordered" disabled={!connected || busy || !state.canInstall} onClick={() => void install('nextStart')}>下次启动自动更新</button></>
            : <><button ref={nextStartButton} className="command-button bordered" disabled={busy} onClick={onClose}>稍后</button>{state.canDownload && <button className="command-button primary" disabled={!connected || busy} onClick={() => void updates.download()}>重新下载</button>}</>}
    </div>
  </Modal>;
}
