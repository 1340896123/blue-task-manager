import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react';
import {
  Activity, AppWindow, ArrowRight, ChartNoAxesCombined, Check, CircleAlert, Clock3,
  Gauge, History, Info, List, Menu, Minus, Pause, Play, Plus, RefreshCw,
  Search, Settings2, Shield, Square, SquareTerminal, Users, X,
} from 'lucide-react';
import { api, errorMessage } from './api';
import { DataTable, Empty, MissingValue, Modal, type Column, type Sort } from './components';
import { bigBytes, bytes, cpuTime, dateTime, heat, matches, percent, rate, reason, time } from './format';
import Performance from './Performance';
import { useTelemetry } from './useTelemetry';
import type { Process, ProcessHistory, ProcessResult, Service, SessionUser, Snapshot, StartupItem } from './types';

const navigation = [
  { id: 'processes', label: '进程', icon: AppWindow }, { id: 'performance', label: '性能', icon: Gauge },
  { id: 'history', label: '应用历史记录', icon: History }, { id: 'startup', label: '启动应用', icon: Activity },
  { id: 'users', label: '用户', icon: Users }, { id: 'details', label: '详细信息', icon: List },
  { id: 'services', label: '服务', icon: Settings2 }, { id: 'settings', label: '设置', icon: Settings2 },
] as const;
type Page = typeof navigation[number]['id'];
type Dialog = 'run' | 'terminate' | 'details' | 'reset' | null;
function sorted<T>(rows: T[], sort: Sort, getValue: (row: T, key: string) => string | number | bigint | null) {
  return [...rows].sort((a, b) => {
    const left = getValue(a, sort.key); const right = getValue(b, sort.key);
    if (left === null) return right === null ? 0 : 1;
    if (right === null) return -1;
    const result = typeof left === 'string' && typeof right === 'string' ? left.localeCompare(right, 'zh-CN', { numeric: true }) : left < right ? -1 : left > right ? 1 : 0;
    return sort.direction === 'ascending' ? result : -result;
  });
}
const io = (row: Process) => row.ioReadBytesPerSecond !== null && row.ioWriteBytesPerSecond !== null ? row.ioReadBytesPerSecond + row.ioWriteBytesPerSecond : null;
function processValue(row: Process, key: string) { if (key === 'io') return io(row); const value = row[key as keyof Process]; return typeof value === 'number' || typeof value === 'string' ? value : null; }
function processMetric(row: Process, key: keyof Process, display: string) { return row[key] === null ? <MissingValue reason={reason(row.unavailable, key)}/> : display; }
const nameCell = (name: string, extra?: string) => <span className="process-name" title={extra ?? name}><span className="process-icon"><AppWindow size={15} aria-hidden="true"/></span><span>{name}</span></span>;
const serviceState = (state: string) => ({ SERVICE_RUNNING: '正在运行', SERVICE_STOPPED: '已停止', SERVICE_PAUSED: '已暂停', SERVICE_START_PENDING: '正在启动', SERVICE_STOP_PENDING: '正在停止', SERVICE_CONTINUE_PENDING: '正在恢复', SERVICE_PAUSE_PENDING: '正在暂停' })[state] ?? state;
const sessionState = (state: string) => ({ WTSActive: '活动', WTSConnected: '已连接', WTSDisconnected: '已断开', WTSListen: '侦听', WTSIdle: '空闲', WTSReset: '重置', WTSDown: '关闭', WTSInit: '初始化' })[state] ?? state;

export default function App() {
  const telemetry = useTelemetry();
  const { snapshot, bootstrap, connected } = telemetry;
  const [page, setPage] = useState<Page>('processes');
  const [query, setQuery] = useState('');
  const [collapsed, setCollapsed] = useState(false);
  const [density, setDensity] = useState(() => { try { return localStorage.getItem('taskmanager-density') === 'standard' ? 'standard' : 'compact'; } catch { return 'compact'; } });
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [sort, setSort] = useState<Sort>({ key: 'name', direction: 'ascending' });
  const [dialog, setDialog] = useState<Dialog>(null);
  const [dialogTarget, setDialogTarget] = useState<Process | null>(null);
  const [actionBusy, setActionBusy] = useState(false);
  const [dialogError, setDialogError] = useState<string | null>(null);
  const [notice, setNotice] = useState<{ text: string; error: boolean } | null>(null);
  const [now, setNow] = useState(Date.now());
  const searchInput = useRef<HTMLInputElement>(null);
  const [executable, setExecutable] = useState('');
  const [argumentsText, setArgumentsText] = useState('');
  const selected = snapshot?.processes.find(row => row.identity === selectedId) ?? null;
  const rowHeight = density === 'compact' ? 32 : 40;
  const title = navigation.find(item => item.id === page)!.label;
  const searchable = !['performance', 'settings'].includes(page);
  const paused = snapshot?.paused ?? bootstrap?.settings.paused ?? false;
  const intervalMs = snapshot?.intervalMs ?? bootstrap?.settings.intervalMs ?? 1000;
  const stale = snapshot && !paused && now - Date.parse(snapshot.capturedAtUtc) > Math.max(3 * intervalMs, 5000);
  const status = !connected ? snapshot ? '连接中断 · 保留上次数据' : '正在连接本机服务' : paused ? '已暂停自动采样' : stale ? '采样已过期' : '已连接 · 实时采样';
  useEffect(() => { const timer = setInterval(() => setNow(Date.now()), 1000); return () => clearInterval(timer); }, []);
  useEffect(() => { if (!notice) return; const timer = setTimeout(() => setNotice(null), 7000); return () => clearTimeout(timer); }, [notice]);
  useEffect(() => {
    const keydown = (event: KeyboardEvent) => {
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'f') { event.preventDefault(); if (searchable) searchInput.current?.focus(); }
      if (event.ctrlKey && bootstrap?.desktop && ['=', '+', '-', '0'].includes(event.key)) {
        event.preventDefault(); void windowAction(event.key === '0' ? 'zoom-reset' : event.key === '-' ? 'zoom-out' : 'zoom-in');
      }
      if (event.key === 'F5') { event.preventDefault(); void refresh(); }
    };
    window.addEventListener('keydown', keydown); return () => window.removeEventListener('keydown', keydown);
  });
  const openDialog = (next: Dialog) => { setDialogError(null); setDialogTarget(selected); setDialog(next); };
  const notify = (text: string, error = false) => setNotice({ text, error });
  async function refresh() {
    if (!connected) { telemetry.retry(); return; }
    try { const result = await api.post<{ message: string }>('refresh', {}); telemetry.retry(); notify(paused ? '采样已暂停；慢速列表将在继续后更新。' : result.message); }
    catch (error) { notify(errorMessage(error), true); }
  }
  async function changeSettings(nextInterval: number, nextPaused: boolean) {
    try { await telemetry.updateSettings({ intervalMs: nextInterval, paused: nextPaused }); }
    catch (error) { notify(errorMessage(error), true); }
  }
  async function windowAction(action: string) { try { await api.post(`window/${action}`, {}); } catch (error) { notify(errorMessage(error), true); } }
  async function submitDialog(event: FormEvent) {
    event.preventDefault(); if (actionBusy) return;
    setActionBusy(true); setDialogError(null);
    try {
      if (dialog === 'run') {
        const result = await api.post<ProcessResult>('processes/start', { executable: executable.trim(), arguments: argumentsText });
        notify(`已运行新任务 · PID ${result.pid}`); setExecutable(''); setArgumentsText('');
      } else if (dialog === 'terminate' && dialogTarget?.creationTimeFileTime) {
        const result = await api.post<ProcessResult>(`processes/${dialogTarget.pid}/terminate`, { creationTimeFileTime: dialogTarget.creationTimeFileTime });
        notify(result.message); setSelectedId(null);
      } else if (dialog === 'reset') {
        const result = await api.post<{ message: string }>('history/reset', {}); notify(result.message);
      }
      setDialog(null); telemetry.retry();
    } catch (error) { setDialogError(errorMessage(error)); telemetry.retry(); }
    finally { setActionBusy(false); }
  }
  const onSort = (key: string) => setSort(previous => ({ key, direction: previous.key === key && previous.direction === 'ascending' ? 'descending' : 'ascending' }));
  const processRows = useMemo(() => sorted((snapshot?.processes ?? []).filter(row => matches(query, row.name, row.pid, row.path, row.publisher)), sort, processValue), [snapshot?.processes, query, sort]);
  const maximumWorkingSet = Math.max(1, ...processRows.flatMap(row => row.workingSetBytes === null ? [] : [row.workingSetBytes]));
  function processColumns(detailed: boolean): Column<Process>[] {
    const columns: Column<Process>[] = [
      { key: 'name', label: '名称', width: detailed ? 240 : 272, sortable: true, metric: <span className="metric-caption">{processRows.length} 个进程</span>, render: row => nameCell(row.name, row.path ?? row.name) },
      { key: 'pid', label: 'PID', width: 72, numeric: true, sortable: true, render: row => row.pid },
    ];
    if (detailed) columns.push(
      { key: 'parentPid', label: '父 PID', width: 82, numeric: true, sortable: true, render: row => row.parentPid },
      { key: 'sessionId', label: '会话', width: 72, numeric: true, sortable: true, render: row => processMetric(row, 'sessionId', String(row.sessionId)) },
      { key: 'threadCount', label: '线程', width: 72, numeric: true, sortable: true, render: row => row.threadCount },
      { key: 'priority', label: '优先级', width: 130, render: row => processMetric(row, 'priority', row.priority ?? '—') },
    );
    else columns.push({ key: 'state', label: '状态', width: 96, render: row => row.state === 'restricted' ? <span className="status-badge warning" title="部分字段因权限或进程状态无法读取"><Shield size={12} aria-hidden="true"/>受限</span> : <span className="running-state">运行中</span> });
    columns.push(
      { key: 'cpuPercent', label: 'CPU', width: 94, numeric: true, sortable: true, metric: percent(snapshot?.system.cpuPercent ?? null), render: row => processMetric(row, 'cpuPercent', percent(row.cpuPercent)), cellStyle: row => ({ backgroundColor: heat(row.cpuPercent) }) },
      { key: 'workingSetBytes', label: '内存', width: 114, numeric: true, sortable: true, metric: percent(snapshot?.system.memoryPercent ?? null), title: '表头为物理内存总览；每行是工作集，蓝色深度相对当前筛选结果最大值', render: row => processMetric(row, 'workingSetBytes', bytes(row.workingSetBytes)), cellStyle: row => ({ backgroundColor: heat(row.workingSetBytes === null ? null : row.workingSetBytes / maximumWorkingSet * 100) }) },
    );
    if (detailed) columns.push(
      { key: 'userName', label: '用户', width: 180, render: row => processMetric(row, 'userName', row.userName ?? '—') },
      { key: 'path', label: '映像路径', width: 420, render: row => row.path ? <span title={row.path}>{row.path}</span> : <MissingValue reason={reason(row.unavailable, 'path')}/> },
    );
    else columns.push(
      { key: 'io', label: 'I/O', width: 118, numeric: true, sortable: true, title: '读写速率合计：包括缓存、文件、设备等，并非物理磁盘吞吐', metric: <span className="metric-caption">读 + 写</span>, render: row => io(row) === null ? <MissingValue reason={reason(row.unavailable, 'ioReadBytesPerSecond', 'I/O 计数不可用或正在预热')}/> : rate(io(row)), cellStyle: row => ({ backgroundColor: io(row) === null ? undefined : 'var(--color-heat-0)' }) },
      { key: 'networkBytesPerSecond', label: '网络', width: 96, numeric: true, metric: <span className="metric-caption">未接入</span>, render: row => <MissingValue reason={reason(row.unavailable, 'networkBytesPerSecond', '未接入逐进程网络采集')}/> },
      { key: 'gpuPercent', label: 'GPU', width: 80, numeric: true, metric: <span className="metric-caption">未接入</span>, render: row => <MissingValue reason={reason(row.unavailable, 'gpuPercent', '未接入逐进程 GPU 采集')}/> },
    );
    return columns;
  }
  function listContent(data: Snapshot) {
    if (page === 'processes' || page === 'details') return <>
      <DataTable rows={processRows} columns={processColumns(page === 'details')} rowKey={row => row.identity} label={page === 'details' ? '进程详细信息' : '进程列表'} rowHeight={rowHeight} sort={sort} onSort={onSort} selected={selectedId} onSelect={row => setSelectedId(row.identity)}/>
      <div className="selection-strip">{selected ? <><span className="selection-name">{selected.name}</span><span>PID {selected.pid}</span><span className="selection-path" title={selected.path ?? undefined}>{selected.path ?? '映像路径不可读取'}</span><button className="text-button" onClick={() => openDialog('details')}>进程信息<ArrowRight size={14} aria-hidden="true"/></button></> : <><Info size={14} aria-hidden="true"/><span>选择一个进程以查看信息或结束任务</span><span className="strip-right">I/O 为进程读写，非物理磁盘吞吐</span></>}</div>
    </>;
    if (page === 'performance') return <Performance snapshot={data}/>;
    if (page === 'history') {
      const rows = sorted(data.history.filter(row => matches(query, row.name, row.pid)), sort, (row, key) => key === 'cpu' ? row.observedCpuTime100ns === null ? null : BigInt(row.observedCpuTime100ns) : key === 'read' ? row.observedIoReadBytes === null ? null : BigInt(row.observedIoReadBytes) : key === 'write' ? row.observedIoWriteBytes === null ? null : BigInt(row.observedIoWriteBytes) : key === 'pid' ? row.pid : row.name);
      const columns: Column<ProcessHistory>[] = [
        { key: 'name', label: '名称', width: 280, sortable: true, render: row => nameCell(row.name) },
        { key: 'pid', label: 'PID', width: 80, numeric: true, sortable: true, render: row => row.pid },
        { key: 'state', label: '状态', width: 100, render: row => <span className={`status-badge ${row.isRunning ? 'success' : ''}`}>{row.isRunning ? '运行中' : '已退出'}</span> },
        { key: 'cpu', label: '观测 CPU 时间', width: 150, numeric: true, sortable: true, render: row => row.observedCpuTime100ns === null ? <MissingValue reason="尚无有效累计观测，或该进程不可读取"/> : cpuTime(row.observedCpuTime100ns) },
        { key: 'read', label: 'I/O 读取累计', width: 150, numeric: true, sortable: true, render: row => row.observedIoReadBytes === null ? <MissingValue reason="尚无有效 I/O 观测，或该进程不可读取"/> : bigBytes(row.observedIoReadBytes) },
        { key: 'write', label: 'I/O 写入累计', width: 150, numeric: true, sortable: true, render: row => row.observedIoWriteBytes === null ? <MissingValue reason="尚无有效 I/O 观测，或该进程不可读取"/> : bigBytes(row.observedIoWriteBytes) },
        { key: 'last', label: '最后观测', width: 130, render: row => time(row.lastObservedAtUtc) },
      ];
      return <><div className="page-note"><Clock3 size={16} aria-hidden="true"/><span><strong>本次会话</strong> · 起于 {dateTime(data.sessionStartedAtUtc)} · 只累计相邻观测增量，非 Windows 系统完整历史</span></div><DataTable rows={rows} columns={columns} rowKey={row => row.identity} label="本次会话应用历史记录" rowHeight={rowHeight} sort={sort} onSort={onSort} emptyDescription="本次会话尚无观测记录，等待下一次真实采样。"/></>;
    }
    if (page === 'startup') {
      const rows = sorted(data.startupItems.filter(row => matches(query, row.name, row.command, row.source)), sort, (row, key) => key === 'source' ? row.source : row.name);
      const columns: Column<StartupItem>[] = [
        { key: 'name', label: '名称', width: 260, sortable: true, render: row => nameCell(row.name) },
        { key: 'source', label: '来源', width: 260, sortable: true, render: row => <span title={row.source}>{row.source}</span> },
        { key: 'enabled', label: '启用状态', width: 130, render: row => <span title={row.enabledReason} className="status-badge warning"><Info size={12} aria-hidden="true"/>无法确定</span> },
        { key: 'command', label: '启动命令 / 路径', width: 500, render: row => <span title={row.command}>{row.command}</span> },
      ];
      return <><div className="page-note"><Info size={16} aria-hidden="true"/><span>只读查看 Run / RunOnce 与 Startup 目录项目；最终启用状态可能受 Windows 策略影响。</span></div><DataTable rows={rows} columns={columns} rowKey={row => row.id} label="真实启动应用" rowHeight={rowHeight} sort={sort} onSort={onSort} emptyDescription={query ? undefined : '当前 Windows 来源中没有读取到启动项目。'}/>{slowFooter(data)}</>;
    }
    if (page === 'users') {
      const rows = sorted(data.users.filter(row => matches(query, row.userName, row.sessionId, row.stationName)), sort, (row, key) => key === 'cpu' ? row.cpuPercent : key === 'memory' ? row.workingSetBytes : key === 'session' ? row.sessionId : row.userName);
      const columns: Column<SessionUser>[] = [
        { key: 'name', label: '用户', width: 270, sortable: true, render: row => <span className="process-name"><span className="process-icon"><Users size={15} aria-hidden="true"/></span>{row.userName ?? <MissingValue reason="该会话用户名不可读取或尚无用户登录"/>}</span> },
        { key: 'session', label: '会话 ID', width: 90, numeric: true, sortable: true, render: row => row.sessionId },
        { key: 'station', label: '会话名称', width: 150, render: row => row.stationName },
        { key: 'state', label: '状态', width: 110, render: row => sessionState(row.state) },
        { key: 'count', label: '进程数', width: 90, numeric: true, render: row => row.processCount },
        { key: 'cpu', label: 'CPU', width: 110, numeric: true, sortable: true, render: row => row.cpuPercent === null ? <MissingValue reason={reason(row.unavailable, 'cpuPercent')}/> : percent(row.cpuPercent) },
        { key: 'memory', label: '内存', width: 130, numeric: true, sortable: true, render: row => row.workingSetBytes === null ? <MissingValue reason={reason(row.unavailable, 'workingSetBytes')}/> : bytes(row.workingSetBytes) },
      ];
      return <><div className="page-note"><Shield size={16} aria-hidden="true"/><span>按已确认的用户身份与会话聚合；权限受限的字段保留未知，不把未归属进程算入当前用户。</span></div><DataTable rows={rows} columns={columns} rowKey={row => String(row.sessionId)} label="Windows 登录会话" rowHeight={rowHeight} sort={sort} onSort={onSort}/>{slowFooter(data)}</>;
    }
    if (page === 'services') {
      const rows = sorted(data.services.filter(row => matches(query, row.name, row.displayName, row.pid)), sort, (row, key) => key === 'pid' ? row.pid : key === 'state' ? row.state : row.name);
      const columns: Column<Service>[] = [
        { key: 'name', label: '名称', width: 260, sortable: true, render: row => <span className="process-name"><span className="process-icon"><Settings2 size={15} aria-hidden="true"/></span><span title={row.name}>{row.name}</span></span> },
        { key: 'display', label: '显示名称', width: 380, render: row => <span title={row.displayName}>{row.displayName}</span> },
        { key: 'state', label: '状态', width: 150, sortable: true, render: row => <span className={`status-badge ${row.state === 'SERVICE_RUNNING' ? 'success' : ''}`} title={row.state}>{serviceState(row.state)}</span> },
        { key: 'pid', label: 'PID', width: 100, numeric: true, sortable: true, render: row => row.pid },
      ];
      return <><div className="page-note"><Shield size={16} aria-hidden="true"/><span>来自 Windows 服务控制管理器（SCM）的真实状态。本版只读，服务启停请使用 Windows 服务管理器。</span></div><DataTable rows={rows} columns={columns} rowKey={row => row.name} label="Windows 服务" rowHeight={rowHeight} sort={sort} onSort={onSort}/>{slowFooter(data)}</>;
    }
    return <div className="settings-page page-scroll">
      <div className="settings-heading"><Settings2 size={25} aria-hidden="true"/><div><h2>按你的节奏，观察系统</h2><p>更新偏好立即生效，监控数据始终来自本机 Windows。</p></div></div>
      <section className="settings-section"><h3>采样与显示</h3>
        <div className="setting-row"><div><strong>更新速度</strong><p>决定真实采样周期；不会生成间隔内的额外数据。</p></div><label className="sr-only" htmlFor="interval">采样间隔</label><select id="interval" value={intervalMs} disabled={telemetry.busy || !connected} onChange={event => void changeSettings(Number(event.target.value), paused)}><option value={1000}>每 1 秒</option><option value={2000}>每 2 秒</option><option value={5000}>每 5 秒</option></select></div>
        <div className="setting-row"><div><strong>自动采样</strong><p>暂停后保留最后快照；继续时以新采样恢复，不补历史曲线。</p></div><button className="toggle" role="switch" aria-label="自动采样" aria-checked={!paused} disabled={telemetry.busy || !connected} onClick={() => void changeSettings(intervalMs, !paused)}><span/></button></div>
        <div className="setting-row"><div><strong>表格密度</strong><p>显示偏好保存在此设备，不影响实际采样内容。</p></div><div className="segmented" aria-label="表格密度"><button aria-pressed={density === 'compact'} onClick={() => changeDensity('compact')}>紧凑</button><button aria-pressed={density === 'standard'} onClick={() => changeDensity('standard')}>标准</button></div></div>
      </section>
      <section className="settings-section"><h3>本机数据来源</h3><dl className="about-list"><div><dt>原生接口</dt><dd>Microsoft.Windows.CsWin32 · Win32</dd></div><div><dt>应用通信</dt><dd>本机同源 HTTP · JSON.NET</dd></div><div><dt>桌面模式</dt><dd>{bootstrap?.desktop ? 'Electron.NET' : '浏览器 / 无窗口主机'}</dd></div><div><dt>版本</dt><dd>{bootstrap?.version ?? '正在读取'}</dd></div></dl></section>
      <section className="settings-section"><h3>能力与采集边界</h3><div className="capability-list">{Object.entries(data.capabilities).map(([key, capability]) => <div key={key}><span className={`status-badge ${capability.available ? 'success' : 'warning'}`}>{capability.available ? <Check size={12} aria-hidden="true"/> : <Info size={12} aria-hidden="true"/>}{capability.available ? '可用' : '未接入'}</span><span><strong>{({ processes: '进程', system: '系统资源', network: '网卡吞吐', services: '服务', users: '用户会话', startup: '启动项目', history: '本次历史', processNetwork: '逐进程网络', processGpu: '逐进程 GPU', diskActivity: '物理磁盘活动' })[key] ?? key}</strong><small>{capability.reason ?? capability.source}</small></span></div>)}</div></section>
    </div>;
  }
  function changeDensity(value: string) { setDensity(value); try { localStorage.setItem('taskmanager-density', value); } catch { /* Storage restrictions do not affect monitoring. */ } }
  function slowFooter(data: Snapshot) { return <div className="selection-strip"><Clock3 size={14} aria-hidden="true"/><span>列表采集：{data.slowDataCapturedAtUtc ? dateTime(data.slowDataCapturedAtUtc) : '尚未读取'} · 通常每 15 秒更新，可手动刷新</span></div>; }
  return <div className={`app-shell bg-canvas text-main-text ${collapsed ? 'sidebar-collapsed' : ''}`} data-density={density}>
    <header className="titlebar"><div className="brand"><span className="brand-mark"><ChartNoAxesCombined size={19} aria-hidden="true"/></span><span>任务管理器</span><span className="brand-edition">蓝域</span></div>
      <div className="global-search"><Search size={16} aria-hidden="true"/><label className="sr-only" htmlFor="global-search">搜索名称、发布者、路径或 PID</label><input ref={searchInput} id="global-search" type="search" value={query} disabled={!searchable} onChange={event => setQuery(event.target.value)} placeholder={searchable ? '搜索名称、发布者、路径或 PID' : '此页面不需要搜索'} autoComplete="off" spellCheck={false}/>{query && <button className="search-clear" aria-label="清空搜索" onClick={() => { setQuery(''); searchInput.current?.focus(); }}><X size={14} aria-hidden="true"/></button>}<kbd>Ctrl F</kbd></div>
      <div className="window-controls" title={!bootstrap?.desktop ? '浏览器模式：窗口控制仅在 Electron 桌面中可用' : undefined}><button aria-label="最小化窗口" disabled={!bootstrap?.desktop} onClick={() => void windowAction('minimize')}><Minus size={15} aria-hidden="true"/></button><button aria-label="最大化或还原窗口" disabled={!bootstrap?.desktop} onClick={() => void windowAction('maximize')}><Square size={12} aria-hidden="true"/></button><button aria-label="关闭窗口" disabled={!bootstrap?.desktop} onClick={() => void windowAction('close')}><X size={16} aria-hidden="true"/></button></div>
    </header>
    <aside className="sidebar"><button className="menu-button icon-button" aria-label={collapsed ? '展开导航' : '折叠导航'} aria-expanded={!collapsed} onClick={() => setCollapsed(value => !value)}><Menu size={20} aria-hidden="true"/></button>
      <nav aria-label="主导航">{navigation.filter(item => item.id !== 'settings').map(item => <button key={item.id} className="nav-item" aria-label={item.label} title={item.label} aria-current={page === item.id ? 'page' : undefined} onClick={() => { setPage(item.id); setQuery(''); setSort({ key: 'name', direction: 'ascending' }); }}><item.icon size={19} aria-hidden="true"/><span>{item.label}</span></button>)}</nav>
      <div className="sidebar-bottom"><div className="sidebar-system"><Shield size={15} aria-hidden="true"/><span>本机 · Windows</span></div><button className="nav-item" aria-label="设置" title="设置" aria-current={page === 'settings' ? 'page' : undefined} onClick={() => { setPage('settings'); setQuery(''); }}><Settings2 size={19} aria-hidden="true"/><span>设置</span></button></div>
    </aside>
    <main className="workspace" aria-label={title}>
      <div className="toolbar"><div className="page-title"><h1>{title}</h1>{page === 'processes' && snapshot && <span className="page-count">{snapshot.system.processCount}</span>}{page === 'history' && <span className="page-count">本次会话</span>}</div><div className="toolbar-actions">
        {(page === 'processes' || page === 'details') && <><button className="command-button primary" disabled={!connected} onClick={() => openDialog('run')}><Plus size={16} aria-hidden="true"/><span>运行新任务</span></button><button className="command-button end-task" title={!selected ? '先选择一个进程' : !selected.canTerminate ? '此进程受保护，或身份 / 权限不可用' : `结束 ${selected.name}（PID ${selected.pid}）`} disabled={!selected?.canTerminate || !connected} onClick={() => openDialog('terminate')}><SquareTerminal size={16} aria-hidden="true"/><span>结束任务</span></button><span className="toolbar-divider"/></>}
        {page === 'history' && <button className="command-button" disabled={!connected || !snapshot?.history.length} onClick={() => openDialog('reset')}><History size={16} aria-hidden="true"/><span>清除本次统计</span></button>}
        <button className="command-button pause-command" disabled={!connected || telemetry.busy} onClick={() => void changeSettings(intervalMs, !paused)}>{paused ? <Play size={16} aria-hidden="true"/> : <Pause size={16} aria-hidden="true"/>}<span>{paused ? '继续采样' : '暂停采样'}</span></button><button className="icon-button refresh-command" aria-label="刷新数据" title="刷新数据（F5）" onClick={() => void refresh()}><RefreshCw size={17} aria-hidden="true"/></button>
      </div></div>
      {(telemetry.error || stale || snapshot?.warnings.length) ? <div className={`connection-banner ${!connected ? 'error' : ''}`} role="status"><CircleAlert size={16} aria-hidden="true"/><span>{telemetry.error ?? (stale ? '采样未更新，正在显示最后一次成功数据。' : snapshot?.warnings.map(warning => warning.message).join('；'))}</span><button className="text-button" onClick={telemetry.retry}>重新连接</button></div> : null}
      {snapshot ? listContent(snapshot) : <Empty title={telemetry.error ? '暂时无法连接本机服务' : '正在读取 Windows 数据'} description={telemetry.error ?? '首次采样需要片刻；界面仅展示本机的真实数据。'}/>}
    </main>
    <footer className="statusbar"><div className={`connection-status ${!connected ? 'offline' : paused || stale ? 'paused' : ''}`} role="status"><span className="status-dot"/>{status}</div><span className="status-divider"/><span>{snapshot ? `${snapshot.system.processCount} 个进程` : '等待首个快照'}</span><div className="status-right">{snapshot && <><span>上次采样 {time(snapshot.capturedAtUtc)}</span><span className="status-divider"/></>}<span>{intervalMs / 1000} 秒 / 次</span><span className="native-source">Windows 原生采集</span></div></footer>
    {notice && <div className={`toast ${notice.error ? 'error' : ''}`} role="status">{notice.error ? <CircleAlert size={18} aria-hidden="true"/> : <Check size={18} aria-hidden="true"/>}<span>{notice.text}</span><button className="icon-button" aria-label="关闭通知" onClick={() => setNotice(null)}><X size={15} aria-hidden="true"/></button></div>}
    {dialog && <Modal title={dialog === 'run' ? '运行新任务' : dialog === 'terminate' ? '结束此进程？' : dialog === 'reset' ? '清除本次统计？' : '进程信息'} description={dialog === 'run' ? '输入程序名称或完整路径，由 Windows 直接创建进程。' : dialog === 'terminate' ? '未保存的工作可能会丢失。只结束下面这一条进程，不会结束子进程。' : dialog === 'reset' ? '清除本应用自启动以来的观测累计；不会修改 Windows 系统历史。' : undefined} busy={actionBusy} onClose={() => setDialog(null)}>
      {dialog === 'details' && dialogTarget ? <><div className="detail-identity">{nameCell(dialogTarget.name)}<span className="status-badge">PID {dialogTarget.pid}</span></div><dl className="process-details">{[['映像路径', dialogTarget.path], ['发布者（文件 CompanyName）', dialogTarget.publisher], ['用户', dialogTarget.userName], ['父 PID', dialogTarget.parentPid], ['会话', dialogTarget.sessionId], ['线程', dialogTarget.threadCount], ['优先级', dialogTarget.priority], ['进程创建 FILETIME', dialogTarget.creationTimeFileTime]].map(([label, value]) => <div key={label}><dt>{label}</dt><dd>{value ?? '不可读取'}</dd></div>)}</dl><p className="detail-note">CompanyName 为文件元数据，不代表已验证数字签名。信息来自选中时的真实快照。</p><div className="modal-actions"><button className="command-button primary" onClick={() => setDialog(null)}>完成</button></div></> : <form onSubmit={event => void submitDialog(event)}>
        {dialog === 'run' ? <><label className="field-label" htmlFor="executable">程序名称或完整路径</label><input id="executable" className="form-input" required autoFocus value={executable} onChange={event => setExecutable(event.target.value)} placeholder="例如 notepad.exe" disabled={actionBusy}/><label className="field-label" htmlFor="arguments">参数 <span>（可选）</span></label><input id="arguments" className="form-input" value={argumentsText} onChange={event => setArgumentsText(event.target.value)} placeholder="直接传递给程序的启动参数" disabled={actionBusy}/><p className="field-hint">程序与参数分开传递，不自动申请管理员权限。</p></> : dialog === 'terminate' && dialogTarget ? <div className="terminate-target"><span className="process-icon"><AppWindow size={20} aria-hidden="true"/></span><div><strong>{dialogTarget.name}</strong><span>PID {dialogTarget.pid}</span><small>{dialogTarget.path ?? '路径不可读取'}</small></div></div> : null}
        {dialogError && <div className="form-error" role="alert"><CircleAlert size={16} aria-hidden="true"/><span>{dialogError}</span></div>}
        <div className="modal-actions"><button type="button" className="command-button bordered" disabled={actionBusy} onClick={() => setDialog(null)}>取消</button><button type="submit" className={`command-button ${dialog === 'terminate' ? 'danger' : 'primary'}`} disabled={actionBusy}>{actionBusy ? '正在处理…' : dialog === 'run' ? '运行' : dialog === 'terminate' ? '结束进程' : '清除统计'}</button></div>
      </form>}
    </Modal>}
  </div>;
}
