import { AppWindow, Check, Clock3, Info, Settings2, Shield, Users } from 'lucide-react';
import { DataTable, MissingValue, type Column, type Sort } from './components';
import { bigBytes, bytes, cpuTime, dateTime, matches, percent, reason, time } from './format';
import Performance from './Performance';
import type { ProcessHistory, Service, SessionUser, Snapshot, StartupItem, Page, PagePreference, Bootstrap } from './types';
import type { useTelemetry } from './useTelemetry';
export function sorted<T>(rows: T[], sort: Sort, getValue: (row: T, key: string) => string | number | bigint | null) {
 return [...rows].sort((a,b) => { const x=getValue(a,sort.key), y=getValue(b,sort.key); if(x===null) return y===null?0:1; if(y===null)return -1; const result=typeof x==='string' && typeof y==='string'?x.localeCompare(y,'zh-CN',{numeric:true}):x<y?-1:x>y?1:0; return sort.direction==='ascending'?result:-result; });
}
const nameCell = (name: string) => <span className="process-name"><span className="process-icon"><AppWindow size={15} aria-hidden="true"/></span><span>{name}</span></span>;
const serviceState = (state: string) => ({ SERVICE_RUNNING: '正在运行', SERVICE_STOPPED: '已停止', SERVICE_PAUSED: '已暂停', SERVICE_START_PENDING: '正在启动', SERVICE_STOP_PENDING: '正在停止' })[state] ?? state;
const sessionState = (state: string) => ({ WTSActive: '活动', WTSConnected: '已连接', WTSDisconnected: '已断开', WTSListen: '侦听', WTSIdle: '空闲' })[state] ?? state;
export default function LegacyPages({ data, page, pref, patch, rowHeight, telemetry, bootstrap, connected, density, changeDensity, changeSettings }: {
 data: Snapshot; page: Page; pref: PagePreference; patch: (patch: Partial<PagePreference>, silent?: boolean) => void; rowHeight: number; telemetry: ReturnType<typeof useTelemetry>; bootstrap: Bootstrap | null; connected: boolean; density: string; changeDensity: (density: 'compact' | 'standard') => void; changeSettings: (interval: number, paused: boolean) => Promise<void>;
}) {
 const query=pref.query; const sort: Sort={key:pref.sortKey,direction:pref.sortDirection}; const paused=data.paused; const intervalMs=data.intervalMs;
 const onSort=(key:string)=>patch({sortKey:key,sortDirection:pref.sortKey===key&&pref.sortDirection==='ascending'?'descending':'ascending'});
 function slowFooter(snapshot: Snapshot) { return <div className="selection-strip"><Clock3 size={14} aria-hidden="true"/><span>列表采集：{snapshot.slowDataCapturedAtUtc ? dateTime(snapshot.slowDataCapturedAtUtc) : '尚未读取'} · 通常每15秒更新，可手动刷新</span></div>; }
    if (page === 'performance') return <Performance snapshot={data}/>;
    if (page === 'history') {
      const rows = sorted(data.history.filter(row => matches(query, row.name, row.pid)), sort, (row, key) => key === 'cpu' ? row.observedCpuTime100ns === null ? null : BigInt(row.observedCpuTime100ns) : key === 'observedIoReadBytes' ? row.observedIoReadBytes === null ? null : BigInt(row.observedIoReadBytes) : key === 'observedIoWriteBytes' ? row.observedIoWriteBytes === null ? null : BigInt(row.observedIoWriteBytes) : key === 'pid' ? row.pid : row.name);
      const columns: Column<ProcessHistory>[] = [
        { key: 'name', label: '名称', width: 280, sortable: true, render: row => nameCell(row.name) },
        { key: 'pid', label: 'PID', width: 80, numeric: true, sortable: true, render: row => row.pid },
        { key: 'state', label: '状态', width: 100, render: row => <span className={`status-badge ${row.isRunning ? 'success' : ''}`}>{row.isRunning ? '运行中' : '已退出'}</span> },
        { key: 'cpu', label: '观测 CPU 时间', width: 150, numeric: true, sortable: true, render: row => row.observedCpuTime100ns === null ? <MissingValue reason="尚无有效累计观测，或该进程不可读取"/> : cpuTime(row.observedCpuTime100ns) },
        { key: 'observedIoReadBytes', label: 'I/O 读取累计', width: 150, numeric: true, sortable: true, render: row => row.observedIoReadBytes === null ? <MissingValue reason="尚无有效 I/O 观测，或该进程不可读取"/> : bigBytes(row.observedIoReadBytes) },
        { key: 'observedIoWriteBytes', label: 'I/O 写入累计', width: 150, numeric: true, sortable: true, render: row => row.observedIoWriteBytes === null ? <MissingValue reason="尚无有效 I/O 观测，或该进程不可读取"/> : bigBytes(row.observedIoWriteBytes) },
        { key: 'last', label: '最后观测', width: 130, render: row => time(row.lastObservedAtUtc) },
      ];
      return <><div className="page-note"><Clock3 size={16} aria-hidden="true"/><span><strong>本次会话</strong> · 起于 {dateTime(data.sessionStartedAtUtc)} · 只累计相邻观测增量，非 Windows 系统完整历史</span></div><DataTable initialScrollTop={pref.scrollTop} onScrollTop={top => patch({ scrollTop: top }, true)} rows={rows} columns={columns} rowKey={row => row.identity} label="本次会话应用历史记录" rowHeight={rowHeight} sort={sort} onSort={onSort} emptyDescription="本次会话尚无观测记录，等待下一次真实采样。"/></>;
    }
    if (page === 'startup') {
      const rows = sorted(data.startupItems.filter(row => matches(query, row.name, row.command, row.source)), sort, (row, key) => key === 'source' ? row.source : row.name);
      const columns: Column<StartupItem>[] = [
        { key: 'name', label: '名称', width: 260, sortable: true, render: row => nameCell(row.name) },
        { key: 'source', label: '来源', width: 260, sortable: true, render: row => <span title={row.source}>{row.source}</span> },
        { key: 'enabled', label: '启用状态', width: 130, render: row => <span title={row.enabledReason} className="status-badge warning"><Info size={12} aria-hidden="true"/>无法确定</span> },
        { key: 'command', label: '启动命令 / 路径', width: 500, render: row => <span title={row.command}>{row.command}</span> },
      ];
      return <><div className="page-note"><Info size={16} aria-hidden="true"/><span>只读查看 Run / RunOnce 与 Startup 目录项目；最终启用状态可能受 Windows 策略影响。</span></div><DataTable initialScrollTop={pref.scrollTop} onScrollTop={top => patch({ scrollTop: top }, true)} rows={rows} columns={columns} rowKey={row => row.id} label="真实启动应用" rowHeight={rowHeight} sort={sort} onSort={onSort} emptyDescription={query ? undefined : '当前 Windows 来源中没有读取到启动项目。'}/>{slowFooter(data)}</>;
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
      return <><div className="page-note"><Shield size={16} aria-hidden="true"/><span>按已确认的用户身份与会话聚合；权限受限的字段保留未知，不把未归属进程算入当前用户。</span></div><DataTable initialScrollTop={pref.scrollTop} onScrollTop={top => patch({ scrollTop: top }, true)} rows={rows} columns={columns} rowKey={row => String(row.sessionId)} label="Windows 登录会话" rowHeight={rowHeight} sort={sort} onSort={onSort}/>{slowFooter(data)}</>;
    }
    if (page === 'services') {
      const rows = sorted(data.services.filter(row => matches(query, row.name, row.displayName, row.pid)), sort, (row, key) => key === 'pid' ? row.pid : key === 'state' ? row.state : row.name);
      const columns: Column<Service>[] = [
        { key: 'name', label: '名称', width: 260, sortable: true, render: row => <span className="process-name"><span className="process-icon"><Settings2 size={15} aria-hidden="true"/></span><span title={row.name}>{row.name}</span></span> },
        { key: 'display', label: '显示名称', width: 380, render: row => <span title={row.displayName}>{row.displayName}</span> },
        { key: 'state', label: '状态', width: 150, sortable: true, render: row => <span className={`status-badge ${row.state === 'SERVICE_RUNNING' ? 'success' : ''}`} title={row.state}>{serviceState(row.state)}</span> },
        { key: 'pid', label: 'PID', width: 100, numeric: true, sortable: true, render: row => row.pid },
      ];
      return <><div className="page-note"><Shield size={16} aria-hidden="true"/><span>来自 Windows 服务控制管理器（SCM）的真实状态。本版只读，服务启停请使用 Windows 服务管理器。</span></div><DataTable initialScrollTop={pref.scrollTop} onScrollTop={top => patch({ scrollTop: top }, true)} rows={rows} columns={columns} rowKey={row => row.name} label="Windows 服务" rowHeight={rowHeight} sort={sort} onSort={onSort}/>{slowFooter(data)}</>;
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
