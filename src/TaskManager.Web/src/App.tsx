import { useEffect, useRef, useState } from 'react';
import { Activity, AppWindow, Check, CircleAlert, Gauge, History, List, Menu, Minus, Pause, Plus, RefreshCw, Search, Settings2, Shield, Square, Users, X } from 'lucide-react';
import { api, errorMessage } from './api';
import { Empty, Modal, cssNumber } from './components';
import { dateTime, time } from './format';
import { useTelemetry } from './useTelemetry';
import { usePreferences } from './hooks/usePreferences';
import ProcessWorkspace, { type LocateRequest } from './process/ProcessWorkspace';
import RunDialog from './process/RunDialog';
import PageWorkspace from './PageWorkspace';
import type { Page, ProcessResult } from './types';
const navigation = [
  { id: 'processes', label: '进程', icon: AppWindow }, { id: 'performance', label: '性能', icon: Gauge },
  { id: 'history', label: '应用历史记录', icon: History }, { id: 'startup', label: '启动应用', icon: Activity },
  { id: 'users', label: '用户', icon: Users }, { id: 'details', label: '详细信息', icon: List },
  { id: 'services', label: '服务', icon: Settings2 }, { id: 'settings', label: '设置', icon: Settings2 },
] as const;
export default function App() {
  const telemetry = useTelemetry(); const preferences = usePreferences();
  const { snapshot, displaySnapshot, bootstrap, connected, stale } = telemetry;
  const [page, setPage] = useState<Page>('processes'); const [runOpen, setRunOpen] = useState(false); const [resetOpen, setResetOpen] = useState(false); const [helpOpen, setHelpOpen] = useState(false);
  const [windowRevision, setWindowRevision] = useState(0);
  const [preferencesResetOpen, setPreferencesResetOpen] = useState(false);
  const [resetBusy, setResetBusy] = useState(false); const [resetError, setResetError] = useState<string | null>(null);
  const [locateRequest, setLocateRequest] = useState<LocateRequest>(null); const [notice, setNotice] = useState<{ text: string; error: boolean } | null>(null);
  const searchInput = useRef<HTMLInputElement>(null);
  const pref = preferences.page(page); const { density, sidebarCollapsed: collapsed } = preferences.preferences;
  const searchable = !['performance', 'settings'].includes(page); const processPage = page === 'processes' || page === 'details';
  const searchLabel = processPage ? '搜索名称、发布者、路径或 PID' : ({ services: '搜索服务名称、显示名称或 PID', users: '搜索用户名、会话名称或会话 ID', startup: '搜索启动名称、来源或命令', history: '搜索进程名称或 PID', performance: '此页面不需要搜索', settings: '此页面不需要搜索' } as const)[page as Exclude<Page, 'processes' | 'details'>];
  const paused = snapshot?.paused ?? bootstrap?.settings.paused ?? false; const intervalMs = snapshot?.intervalMs ?? bootstrap?.settings.intervalMs ?? 1000;
  const title = navigation.find(item => item.id === page)!.label;
  const notify = (text: string, error = false) => setNotice({ text, error });
  const windowAction = async (command: string) => { try { if (command === 'close') await preferences.flush(); await api.post(`window/${command}`, {}); setWindowRevision(v => v + 1); } catch (error) { notify(errorMessage(error), true); } };
  const refresh = async () => { telemetry.retry(); if (!connected) return; try { const result = await api.post<{ message: string }>('refresh', {}); telemetry.retry(); if (!processPage) notify(result.message); } catch (error) { notify(errorMessage(error), true); } };
  const created = (result: ProcessResult) => {
    telemetry.resumeDisplay(); setPage('processes'); if (result.identity) setLocateRequest({ identity: result.identity, sequence: Date.now() });
    telemetry.retry(); notify(`已运行新任务 · PID ${result.pid} · 已按Windows返回身份定位`);
  };
  // Shared entry point for the next page stage: callers provide a verified current identity.
  const locateProcess = (identity: string) => { telemetry.resumeDisplay(); setPage('processes'); setLocateRequest({ identity, sequence: Date.now() }); };
  const showSession = (sessionId: number) => { telemetry.resumeDisplay(); setLocateRequest(null); preferences.updatePage('processes', { query: '', filters: { currentUser: false, sessionId, state: 'all' }, selectedIdentity: null, scrollTop: 0 }); setPage('processes'); };
  const showService = (name: string) => { setPage('services'); preferences.updatePage('services', { query: name, selectedIdentity: name, scrollTop: 0 }); };
  useEffect(() => { if (!notice) return; const timer = setTimeout(() => setNotice(null), 9000); return () => clearTimeout(timer); }, [notice]);
  useEffect(() => {
    const keydown = (event: KeyboardEvent) => {
      const dialogOpen = Boolean(document.querySelector('dialog[open]'));
      if (!dialogOpen && (event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'f') { event.preventDefault(); if (searchable) searchInput.current?.focus(); }
      if (!dialogOpen && event.ctrlKey && event.key.toLowerCase() === 'n') { event.preventDefault(); if (connected) setRunOpen(true); }
      if (!dialogOpen && event.key === 'F5') { event.preventDefault(); void refresh(); }
      if (!dialogOpen && event.key === 'F1') { event.preventDefault(); setHelpOpen(true); }
      if (event.ctrlKey && bootstrap?.desktop && ['=', '+', '-', '0'].includes(event.key)) { event.preventDefault(); void windowAction(event.key === '0' ? 'zoom-reset' : event.key === '-' ? 'zoom-out' : 'zoom-in'); }
    };
    window.addEventListener('keydown', keydown); return () => window.removeEventListener('keydown', keydown);
  });
  const status = !connected ? snapshot ? '连接中断 · 保留上次数据' : '正在连接本机服务' : paused ? '采集已暂停' : stale ? '采样已过期' : '已连接 · 实时采样';
  const warning = telemetry.error ?? (stale ? '采样未更新，正在保留最后一次成功数据。' : preferences.warning ?? snapshot?.warnings.map(w => w.message).join('；'));
  return <div className={`app-shell bg-canvas text-main-text ${collapsed ? 'sidebar-collapsed' : ''}`} data-density={density}>
    <header className="titlebar"><div className="brand"><img className="brand-mark" src="/icon.svg" alt="" aria-hidden="true" width="28" height="28"/><span>任务管理器</span><span className="brand-edition">蓝域</span></div><div className="global-search"><Search size={16} aria-hidden="true"/><label className="sr-only" htmlFor="global-search">{searchLabel}</label><input id="global-search" ref={searchInput} type="search" value={pref.query} disabled={!searchable || !preferences.ready} onChange={event => preferences.updatePage(page, { query: event.target.value.slice(0, 256), scrollTop: 0 })} placeholder={searchLabel} autoComplete="off" spellCheck={false}/>{pref.query && <button className="search-clear" aria-label="清空搜索" onClick={() => { preferences.updatePage(page, { query: '', scrollTop: 0 }); searchInput.current?.focus(); }}><X size={14} aria-hidden="true"/></button>}<kbd>Ctrl F</kbd></div><div className="window-controls" title={!bootstrap?.desktop ? '窗口控制仅在Electron桌面中可用' : undefined}><button aria-label="最小化窗口" disabled={!bootstrap?.desktop} onClick={() => void windowAction('minimize')}><Minus size={15} aria-hidden="true"/></button><button aria-label="最大化或还原窗口" disabled={!bootstrap?.desktop} onClick={() => void windowAction('maximize')}><Square size={12} aria-hidden="true"/></button><button aria-label="关闭窗口" disabled={!bootstrap?.desktop} onClick={() => void windowAction('close')}><X size={16} aria-hidden="true"/></button></div></header>
    <aside className="sidebar"><button className="menu-button icon-button" aria-label={collapsed ? '展开导航' : '折叠导航'} aria-expanded={!collapsed} disabled={!preferences.ready} onClick={() => preferences.update(p => ({ ...p, sidebarCollapsed: !p.sidebarCollapsed }))}><Menu size={20} aria-hidden="true"/></button><nav aria-label="主导航">{navigation.filter(item => item.id !== 'settings').map(item => <button key={item.id} className="nav-item" aria-label={item.label} title={item.label} aria-current={page === item.id ? 'page' : undefined} onClick={() => setPage(item.id)}><item.icon size={19} aria-hidden="true"/><span>{item.label}</span></button>)}</nav><div className="sidebar-bottom"><div className="sidebar-system"><Shield size={15} aria-hidden="true"/><span>本机 · Windows</span></div><button className="nav-item" aria-label="设置" aria-current={page === 'settings' ? 'page' : undefined} onClick={() => setPage('settings')}><Settings2 size={19} aria-hidden="true"/><span>设置</span></button></div></aside>
    <main className="workspace" aria-label={title}>
      {warning && <div className={`connection-banner ${!connected ? 'error' : ''}`} role="status"><CircleAlert size={16} aria-hidden="true"/><span>{warning}</span>{preferences.warning && <button className="text-button" onClick={preferences.retry}>重新读取偏好</button>}<button className="text-button" onClick={telemetry.retry}>重试连接</button></div>}
      {telemetry.frozen && <div className="connection-banner frozen-banner" role="status"><Pause size={16} aria-hidden="true"/><span>列表显示已冻结 · {telemetry.frozenAt ? dateTime(telemetry.frozenAt) : ''}。后台采样、历史与性能曲线继续更新。</span><button className="text-button" onClick={telemetry.resumeDisplay}>继续显示最新数据</button></div>}
      {snapshot && displaySnapshot && preferences.ready ? processPage ? <ProcessWorkspace key={page} page={page} snapshot={displaySnapshot} liveSnapshot={snapshot} bootstrap={bootstrap} preferences={preferences} connected={connected} frozen={telemetry.frozen} toggleFrozen={telemetry.toggleFrozen} onRun={() => setRunOpen(true)} refresh={() => void refresh()} notify={notify} onService={showService} locateRequest={locateRequest}/> : <>
        <div className="toolbar"><div className="page-title"><h1>{title}</h1>{page === 'history' && <span className="page-count">本次会话</span>}</div><div className="toolbar-actions">{page === 'history' && <button className="command-button" disabled={!connected || !snapshot.history.length} onClick={() => { setResetError(null); setResetOpen(true); }}>清除本次统计</button>}<button className="command-button" disabled={!connected} onClick={() => setRunOpen(true)}><Plus size={16} aria-hidden="true"/>运行新任务</button><button className="icon-button" aria-label="刷新数据" onClick={() => void refresh()}><RefreshCw size={16} aria-hidden="true"/></button></div></div>
        <PageWorkspace windowRevision={windowRevision} key={page} data={page === 'performance' || page === 'settings' ? snapshot : displaySnapshot} liveData={snapshot} page={page} pref={pref} patch={(patch, silent) => preferences.updatePage(page, patch, !silent)} rowHeight={cssNumber(density === 'compact' ? '--height-row-compact' : '--height-row-standard', 32)} telemetry={telemetry} bootstrap={bootstrap} connected={connected} preferences={preferences} locate={locateProcess} showSession={showSession} notify={notify} refresh={telemetry.retry} onHelp={() => setHelpOpen(true)} onReset={() => setPreferencesResetOpen(true)}/>
      </> : <Empty title={!preferences.ready ? '正在读取界面偏好' : telemetry.error ? '暂时无法连接本机服务' : '正在读取 Windows 数据'} description={telemetry.error ?? '首次读取需要片刻；只展示本机真实数据。'}/>}
    </main>
    <footer className="statusbar"><div className={`connection-status ${!connected ? 'offline' : paused || stale ? 'paused' : ''}`} role="status"><span className="status-dot"/>{status}</div><span className="status-divider"/><span>{snapshot ? `${snapshot.system.processCount} 个进程` : '等待首个快照'}</span><div className="status-right">{snapshot && <span>上次采样 {time(snapshot.capturedAtUtc)}</span>}<span>{intervalMs / 1000} 秒 / 次</span><span className="native-source">Windows 原生采集</span></div></footer>
    {notice && <div className={`toast ${notice.error ? 'error' : ''}`} role="status">{notice.error ? <CircleAlert size={18} aria-hidden="true"/> : <Check size={18} aria-hidden="true"/>}<span>{notice.text}</span><button className="icon-button" aria-label="关闭通知" onClick={() => setNotice(null)}><X size={15} aria-hidden="true"/></button></div>}
    {runOpen && <RunDialog desktop={bootstrap?.desktop ?? false} onClose={() => setRunOpen(false)} onCreated={created}/>}
    {resetOpen && <Modal title="清除本次统计？" description="只清除本应用观测累计，不会修改Windows系统历史。" busy={resetBusy} onClose={() => setResetOpen(false)}>{resetError && <p className="form-error" role="alert">{resetError}</p>}<div className="modal-actions"><button className="command-button bordered" disabled={resetBusy} onClick={() => setResetOpen(false)}>取消</button><button className="command-button primary" disabled={resetBusy} onClick={() => { setResetBusy(true); void api.post<{ message: string }>('history/reset', {}).then(result => { notify(result.message); setResetOpen(false); telemetry.retry(); }).catch(error => setResetError(errorMessage(error))).finally(() => setResetBusy(false)); }}>清除统计</button></div></Modal>}
    {preferencesResetOpen && <Modal title="恢复默认界面偏好？" description="将恢复各页搜索、排序、选择、滚动、资源选择、列、密度、树和侧栏偏好。采集历史、采样周期与窗口状态不会被更改。" onClose={() => setPreferencesResetOpen(false)}><div className="modal-actions"><button className="command-button bordered" onClick={() => setPreferencesResetOpen(false)}>取消</button><button className="command-button primary" onClick={() => { preferences.reset(); void preferences.flush().then(() => notify('界面偏好已恢复默认；保存失败会显示在偏好提示中。')); setPreferencesResetOpen(false); }}>恢复默认界面偏好</button></div></Modal>}
    {helpOpen && <Modal title="快捷键帮助" onClose={() => setHelpOpen(false)}><dl className="process-details">{[['Ctrl+F', '搜索当前页'], ['Ctrl+N', '运行新任务'], ['F5', '刷新真实数据'], ['Enter / 双击', '打开选中进程详情'], ['Shift+F10 / 右键', '固定目标操作菜单'], ['Delete', '确认结束选中进程'], ['Space', '冻结/继续显示（输入框内不拦截）'], ['方向键 / Home / End', '移动表格选择；树中左右键折叠/展开'], ['Escape', '关闭最上层菜单或对话框'], ['Ctrl + / − / 0', '桌面缩放/恢复']].map(([key, value]) => <div key={key}><dt>{key}</dt><dd>{value}</dd></div>)}</dl></Modal>}
  </div>;
}
