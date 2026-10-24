import { useEffect, useMemo, useState, type KeyboardEvent, type MouseEvent, type ReactNode } from 'react';
import { ChevronDown, ChevronRight, Download, Filter, Leaf, ListTree, Pause, Play, Plus, RefreshCw, SquareTerminal } from 'lucide-react';
import { api, errorMessage } from '../api';
import { CommandMenu, DataTable, MissingValue, PageHeader, cssNumber, type Column } from '../components';
import { bytes, dateTime, heat, percent, rate, reason } from '../format';
import type { Bootstrap, Page, PageFilters, Process, Snapshot } from '../types';
import type { PreferencesController } from '../hooks/usePreferences';
import ContextMenu, { type MenuItem } from './ContextMenu';
import ProcessIcon from './ProcessIcon';
import ProcessDetail from './ProcessDetail';
import ProcessActionDialog from './ProcessActionDialog';
import { exportProcesses, processIo, processRows, type ProcessRow } from './processModel';
import { networkDescription, networkScope, networkSourceText } from './network';
export type LocateRequest = { identity: string; sequence: number } | null;
export default function ProcessWorkspace({ page, snapshot, liveSnapshot, bootstrap, preferences, connected, frozen, toggleFrozen, onRun, refresh, notify, onService, locateRequest, onLocateConsumed, samplingStatus }: {
  page: Page; snapshot: Snapshot; liveSnapshot: Snapshot; bootstrap: Bootstrap | null; preferences: PreferencesController; connected: boolean; frozen: boolean; toggleFrozen: () => void; onRun: () => void; refresh: () => void;
  notify: (text: string, error?: boolean) => void; onService: (name: string) => void; locateRequest: LocateRequest; onLocateConsumed: (sequence: number) => void;
  samplingStatus: ReactNode;
}) {
  const pref = preferences.page(page); const filters: PageFilters = pref.filters ?? { currentUser: false, sessionId: null, state: 'all' };
  const searching = Boolean(pref.query.trim() || filters.currentUser || filters.sessionId != null || filters.state !== 'all');
  const sort = { key: pref.sortKey, direction: pref.sortDirection }; const tree = preferences.preferences.processView === 'tree' && page === 'processes';
  const [scrollReset, setScrollReset] = useState(0);
  const [collapsed, setCollapsed] = useState(new Set<string>()); const [reveal, setReveal] = useState<string | null>(null);
  const [detailTarget, setDetailTarget] = useState<Process | null>(null); const [action, setAction] = useState<{ action: string; target: Process } | null>(null);
  const [menu, setMenu] = useState<{ target: Process; x: number; y: number } | null>(null); const [fileBusy, setFileBusy] = useState(false);
  const model = useMemo(() => processRows(snapshot.processes, pref.query, pref.filters, bootstrap?.currentUserName, sort, tree, collapsed), [snapshot.processes, pref.query, pref.filters, bootstrap?.currentUserName, sort.key, sort.direction, tree, collapsed]);
  const selected = model.rows.find(row => row.process.identity === pref.selectedIdentity)?.process ?? null;
  const liveIds = useMemo(() => new Set(liveSnapshot.processes.map(p => p.identity)), [liveSnapshot.processes]);
  useEffect(() => { const identities = new Set(snapshot.processes.map(p => p.identity)); setCollapsed(previous => { const retained = [...previous].filter(id => identities.has(id)).slice(0, 20000); return retained.length === previous.size ? previous : new Set(retained); }); }, [snapshot.processes]);
  const patch = (value: Parameters<PreferencesController['updatePage']>[1]) => preferences.updatePage(page, value);
  const locate = (identity: string, search = false) => {
    const target = snapshot.processes.find(p => p.identity === identity);
    if (!target) { notify('该进程尚未进入当前显示快照，或已退出。请继续显示最新数据后重试。', true); return; }
    patch({ query: search ? String(target.pid) : '', filters: null, selectedIdentity: identity, scrollTop: 0 }); setCollapsed(new Set()); setReveal(`${identity}`);
  };
  useEffect(() => { if (locateRequest) { patch({ query: String(Number(locateRequest.identity.split(':')[0])), filters: null, selectedIdentity: locateRequest.identity, scrollTop: 0 }); setCollapsed(new Set()); setReveal(locateRequest.identity); onLocateConsumed(locateRequest.sequence); } }, [locateRequest?.sequence]);
  const locateParent = (target: Process) => {
    const full = processRows(snapshot.processes, '', null, bootstrap?.currentUserName, sort, false, new Set());
    const parent = full.parents.get(target.identity);
    if (parent) { setDetailTarget(null); locate(parent, true); } else notify('父进程已退出或创建身份无法确认，无法可靠定位。', true);
  };
  const runAction = async (command: string, target: Process) => {
    if (command.startsWith('copy')) {
      const text = command === 'copyPid' ? String(target.pid) : command === 'copyPath' ? target.path : `${target.name}\r\nPID ${target.pid}\r\n身份 ${target.identity}\r\n路径 ${target.path ?? '不可读取'}\r\n用户 ${target.userName ?? '不可读取'}\r\n网络（接收+发送） ${target.networkBytesPerSecond === null ? `—（${reason(target.unavailable, 'networkBytesPerSecond')}）` : rate(target.networkBytesPerSecond)}\r\n${networkDescription(target)}\r\n显示采样 ${snapshot.capturedAtUtc}`;
      if (!text) return; try { await navigator.clipboard.writeText(text); notify('已复制到剪贴板。'); } catch (error) { notify(errorMessage(error), true); } return;
    }
    if (command === 'details') { setDetailTarget(target); return; }
    if (command === 'parent') { locateParent(target); return; }
    if (!liveIds.has(target.identity) || !connected) { notify('目标已退出或本机连接不可用；写操作未发送。', true); return; }
    if (command === 'location' || command === 'properties') {
      if (fileBusy) return; setFileBusy(true);
      try { const result = await api.post<{ message: string }>(`processes/${target.pid}/file-action`, { creationTimeFileTime: target.creationTimeFileTime, action: command === 'location' ? 'openLocation' : 'properties' }); notify(result.message); }
      catch (error) { notify(errorMessage(error), true); } finally { setFileBusy(false); }
      return;
    }
    setDetailTarget(null); setAction({ action: command, target });
  };
  const openMenu = (target: Process, event: MouseEvent | KeyboardEvent) => {
    const bounds = event.currentTarget.getBoundingClientRect(); patch({ selectedIdentity: target.identity });
    setMenu({ target, x: 'clientX' in event ? event.clientX : bounds.left, y: 'clientY' in event ? event.clientY : bounds.bottom });
  };
  useEffect(() => {
    const key = (event: globalThis.KeyboardEvent) => {
      if (event.defaultPrevented || document.querySelector('dialog[open]') || menu || (event.target instanceof Element && event.target.closest('input,textarea,select,[contenteditable="true"],button,a,summary,[role="menu"],[role="separator"]'))) return;
      if (event.key === 'Delete' && selected?.canTerminate && liveIds.has(selected.identity) && connected) { event.preventDefault(); setAction({ action: 'terminate', target: selected }); }
      if (event.code === 'Space' && !event.ctrlKey && !event.altKey) { event.preventDefault(); toggleFrozen(); }
      if (event.key === 'Enter' && selected && event.target === document.body) { event.preventDefault(); setDetailTarget(selected); }
    };
    window.addEventListener('keydown', key); return () => window.removeEventListener('keydown', key);
  });
  const maximumMemory = Math.max(1, ...model.rows.map(row => row.process.workingSetBytes ?? 0));
  const maximumNetwork = Math.max(1, ...model.rows.flatMap(row => row.process.networkBytesPerSecond === null ? [] : [row.process.networkBytesPerSecond]));
  const metric = (p: Process, key: keyof Process, text: string) => p[key] === null ? <MissingValue reason={reason(p.unavailable, key)}/> : text;
  const w = (name: string, fallback: number) => cssNumber(`--width-column-${name}`, fallback);
  const definitions: Column<ProcessRow>[] = [
    { key: 'name', label: '名称', width: w(page === 'details' ? 'name-detail' : 'name', 272), sortable: true, render: row => <span className={`process-name ${row.context ? 'ancestor-context' : ''}`} style={{ paddingLeft: tree ? `calc(var(--space-tree-indent) * ${Math.min(12, row.depth)})` : undefined }}>
      {tree && (row.hasChildren ? <button className="tree-toggle" aria-label={`${searching || !collapsed.has(row.process.identity) ? '折叠' : '展开'} ${row.process.name}`} title={searching ? '筛选期间保持祖先展开，清除筛选后可折叠' : undefined} disabled={searching} aria-expanded={searching || !collapsed.has(row.process.identity)} onClick={event => { event.stopPropagation(); setCollapsed(previous => { const next = new Set(previous); if (next.has(row.process.identity)) next.delete(row.process.identity); else next.add(row.process.identity); return next; }); }}>{collapsed.has(row.process.identity) && !searching ? <ChevronRight/> : <ChevronDown/>}</button> : <span className="tree-spacer"/>)}<ProcessIcon process={row.process}/><span title={`${row.process.path ?? row.process.name}${row.context ? ' · 搜索结果祖先上下文' : ''}`}>{row.process.name}{row.context && <small className="context-tag">祖先</small>}</span></span> },
    { key: 'pid', label: 'PID', width: w('pid', 80), numeric: true, sortable: true, render: row => row.process.pid },
    { key: 'state', label: '状态', width: w('number', 112), render: row => <span className={`status-badge ${row.process.state === 'restricted' ? 'warning' : ''}`} title={row.process.state === 'restricted' ? '进程仍在运行；Windows拒绝访问详细信息。父子关系无法确认时，会在树中单独显示。' : undefined}>{row.process.state === 'restricted' ? '访问受限' : '运行中'}</span> },
    { key: 'parentPid', label: '父 PID', width: w('pid', 80), sortable: true, numeric: true, render: row => <button className="text-button" onClick={event => { event.stopPropagation(); locateParent(row.process); }}>{row.process.parentPid}</button> },
    { key: 'sessionId', label: '会话', width: w('pid', 80), sortable: true, numeric: true, render: row => metric(row.process, 'sessionId', String(row.process.sessionId)) },
    { key: 'threadCount', label: '线程', width: w('pid', 80), sortable: true, numeric: true, render: row => row.process.threadCount },
    { key: 'handleCount', label: '句柄', width: w('pid', 80), sortable: true, numeric: true, render: row => metric(row.process, 'handleCount', String(row.process.handleCount)) },
    { key: 'priority', label: '优先级', width: w('number', 112), render: row => metric(row.process, 'priority', row.process.priority ?? '—') },
    { key: 'cpuPercent', label: 'CPU', width: w('number', 112), sortable: true, numeric: true, metric: percent(snapshot.system.cpuPercent), render: row => metric(row.process, 'cpuPercent', percent(row.process.cpuPercent)), cellStyle: row => ({ backgroundColor: heat(row.process.cpuPercent) }) },
    { key: 'workingSetBytes', label: '内存（工作集）', width: w('number', 112), sortable: true, numeric: true, metric: percent(snapshot.system.memoryPercent), render: row => metric(row.process, 'workingSetBytes', bytes(row.process.workingSetBytes)), cellStyle: row => ({ backgroundColor: heat(row.process.workingSetBytes == null ? null : row.process.workingSetBytes / maximumMemory * 100) }) },
    { key: 'privateBytes', label: '专用提交', width: w('number', 112), sortable: true, numeric: true, render: row => metric(row.process, 'privateBytes', bytes(row.process.privateBytes)) },
    { key: 'io', label: 'I/O', width: w('number', 112), sortable: true, numeric: true, title: '进程读取+写入，含缓存/文件/设备，不是物理磁盘吞吐', metric: <span className="metric-caption">读 + 写</span>, render: row => processIo(row.process) === null ? <MissingValue reason={reason(row.process.unavailable, 'ioReadBytesPerSecond')}/> : rate(processIo(row.process)) },
    { key: 'networkBytesPerSecond', label: '网络', width: w('number', 112), numeric: true, title: `接收+发送，字节/秒。${networkScope}`, metric: <span className="metric-caption">收 + 发</span>, render: row => row.process.networkBytesPerSecond === null ? <MissingValue reason={reason(row.process.unavailable, 'networkBytesPerSecond')}/> : <span title={networkDescription(row.process)}>{rate(row.process.networkBytesPerSecond)}</span>, cellStyle: row => ({ backgroundColor: heat(row.process.networkBytesPerSecond === null ? null : row.process.networkBytesPerSecond / maximumNetwork * 100) }) },
    { key: 'gpuPercent', label: 'GPU', width: w('number', 112), sortable: true, numeric: true, metric: percent(snapshot.system.gpuPercent), title: '最忙GPU引擎；GPU有独立采样时间', render: row => metric(row.process, 'gpuPercent', percent(row.process.gpuPercent)), cellStyle: row => ({ backgroundColor: heat(row.process.gpuPercent) }) },
    { key: 'gpuEngine', label: 'GPU 引擎', width: w('text', 180), render: row => metric(row.process, 'gpuEngine', row.process.gpuEngine ?? '—') },
    { key: 'userName', label: '用户', width: w('text', 180), render: row => metric(row.process, 'userName', row.process.userName ?? '—') },
    { key: 'publisher', label: '发布者', width: w('text', 180), render: row => metric(row.process, 'publisher', row.process.publisher ?? '—') },
    { key: 'path', label: '映像路径', width: w('path', 420), render: row => <span title={row.process.path ?? undefined}>{metric(row.process, 'path', row.process.path ?? '—')}</span> },
  ];
  const defaults = page === 'details' ? ['name', 'pid', 'state', 'userName', 'cpuPercent', 'workingSetBytes', 'priority', 'threadCount', 'parentPid', 'sessionId', 'path'] : ['name', 'state', 'cpuPercent', 'workingSetBytes', 'io', 'networkBytesPerSecond', 'gpuPercent', 'pid'];
  const visible = pref.visibleColumns ?? defaults;
  const order = [...new Set([...defaults.filter(key => ['name', 'pid'].includes(key) || visible.includes(key)), ...visible, 'name', 'pid'])];
  const columns = order.flatMap(key => { const column = definitions.find(column => column.key === key); return column ? [{ ...column, label: key === 'workingSetBytes' && page === 'processes' ? '内存' : column.label }] : []; });
  const alive = (target: Process) => liveIds.has(target.identity);
  const actionsFor = (target: Process): MenuItem[] => [
    { label: '进程信息', action: () => void runAction('details', target) },
    { label: '定位父进程', action: () => void runAction('parent', target) },
    { label: '打开文件位置', disabled: !target.path || !alive(target) || !connected || fileBusy, action: () => void runAction('location', target) },
    { label: 'Windows 属性', disabled: !target.path || !alive(target) || !connected || fileBusy, action: () => void runAction('properties', target) },
    { label: '复制 PID', action: () => void runAction('copyPid', target) }, { label: '复制路径', disabled: !target.path, action: () => void runAction('copyPath', target) }, { label: '复制信息', action: () => void runAction('copyInfo', target) },
    ...[['priority', '设置优先级'], ['efficiency', '效率模式'], ['terminate', '结束任务…'], ['tree', '结束进程树…']].map(([command, label]) => ({ label, danger: command === 'terminate' || command === 'tree', disabled: !target.canTerminate || !alive(target) || !connected, reason: !alive(target) ? '目标已退出' : !target.canTerminate ? '受保护或身份/权限不可用' : undefined, action: () => void runAction(command, target) })),
  ];
  return <>
    <PageHeader title={page === 'details' ? '详细信息' : '进程'}>
      <button className="command-button" disabled={!connected} onClick={onRun}><Plus size={16} aria-hidden="true"/><span>运行新任务</span></button>
      <button className="command-button end-task" title={!selected ? '请先选择进程' : !selected.canTerminate ? '受保护或身份/权限不可用' : !alive(selected) ? '目标已退出' : undefined} disabled={!selected?.canTerminate || !selected || !alive(selected) || !connected} onClick={() => selected && setAction({ action: 'terminate', target: selected })}><SquareTerminal size={16} aria-hidden="true"/><span>结束任务</span></button>
      {page === 'processes' && <button className="command-button efficiency-command" title={!selected ? '请先选择进程' : '读取目标实际效率模式状态后确认'} disabled={!selected?.canTerminate || !selected || !alive(selected) || !connected} onClick={() => selected && void runAction('efficiency', selected)}><Leaf size={16} aria-hidden="true"/><span>效率模式</span></button>}
      <CommandMenu label="更多进程操作">
        {selected && <div className="menu-section"><strong className="menu-caption">{selected.name} · PID {selected.pid}</strong>{actionsFor(selected).map(item => <button className="menu-action" data-menu-close key={item.label} disabled={item.disabled} title={item.reason} onClick={item.action}>{item.label}</button>)}</div>}
        <div className="menu-section">
      {page === 'processes' && <div className="segmented" aria-label="进程显示模式"><button aria-pressed={!tree} onClick={() => preferences.update(p => ({ ...p, processView: 'flat' }))}>平铺</button><button aria-pressed={tree} onClick={() => preferences.update(p => ({ ...p, processView: 'tree' }))}><ListTree size={16} aria-hidden="true"/>进程树</button></div>}
      <details className="control-popover"><summary><Filter size={16} aria-hidden="true"/>筛选{(filters.currentUser || filters.sessionId != null || filters.state !== 'all') && ' · 已应用'}</summary><div className="popover-content"><label><input type="checkbox" checked={filters.currentUser} disabled={!bootstrap?.currentUserName} onChange={event => patch({ filters: { ...filters, currentUser: event.target.checked }, scrollTop: 0 })}/>当前用户 {bootstrap?.currentUserName}</label><label>会话筛选<select aria-label="会话筛选" value={filters.sessionId??'all'} onChange={event=>patch({filters:{...filters,sessionId:event.target.value==='all'?null:Number(event.target.value)},scrollTop:0})}><option value="all">全部会话</option>{[...new Set([...snapshot.users.map(u=>u.sessionId),...snapshot.processes.flatMap(p=>p.sessionId===null?[]:[p.sessionId]),...(filters.sessionId===null?[]:[filters.sessionId])])].sort((a,b)=>a-b).map(id=><option key={id} value={id}>会话 {id}{id===bootstrap?.currentSessionId?'（本应用）':''}</option>)}</select></label><p className="detail-note">当前用户或会话筛选会排除无法读取对应信息的进程。父子关系无法确认时，进程树会将其单独显示。</p><label>信息读取<select aria-label="信息读取" value={filters.state} onChange={event => patch({ filters: { ...filters, state: event.target.value as PageFilters['state'] }, scrollTop: 0 })}><option value="all">全部进程</option><option value="running">基本查询可用</option><option value="restricted">访问受限</option></select></label><button className="text-button" onClick={() => patch({ filters: null })}>清除筛选</button></div></details>
      <details className="control-popover"><summary>显示列</summary><div className="popover-content column-options">{definitions.map(column => <label key={column.key}><input type="checkbox" checked={visible.includes(column.key) || ['name', 'pid'].includes(column.key)} disabled={['name', 'pid'].includes(column.key)} onChange={event => patch({ visibleColumns: event.target.checked ? [...new Set([...visible, column.key])] : visible.filter(key => key !== column.key) })}/>{column.label}</label>)}<button className="text-button" onClick={() => patch({ visibleColumns: null })}>恢复默认列</button></div></details>
      <button className="menu-action" data-menu-close onClick={() => { const exported = tree ? processRows(snapshot.processes, pref.query, pref.filters, bootstrap?.currentUserName, sort, true, new Set()).rows : model.rows; exportProcesses(exported, snapshot.capturedAtUtc, tree); notify(`已导出 ${exported.length} 行全部筛选结果${tree ? '（含祖先上下文与折叠子节点）' : ''}。`); }}><Download size={16} aria-hidden="true"/>导出 CSV</button><span className="menu-caption">{model.matched} 个匹配{tree ? ` · ${model.rows.length} 行含祖先` : ''}</span><button className="menu-action" data-menu-close onClick={() => { patch({ query: '', filters: null, sortKey: 'name', sortDirection: 'ascending', columnWidths: null, scrollTop: 0 }); setScrollReset(n=>n+1); }}>重置视图</button>
          <button className="menu-action" data-menu-close onClick={toggleFrozen} aria-pressed={frozen}>{frozen ? <Play size={16} aria-hidden="true"/> : <Pause size={16} aria-hidden="true"/>}{frozen ? '继续显示' : '冻结显示'}</button>
          <button className="menu-action" data-menu-close onClick={refresh}><RefreshCw size={16} aria-hidden="true"/>刷新数据</button>
        </div>
        {samplingStatus}
        <div className="command-status" aria-label="逐进程网络采样状态"><strong>逐进程网络采样状态</strong><span>{networkSourceText(snapshot.processNetwork, snapshot.paused)}</span><span>{networkScope}</span></div>
      </CommandMenu>
    </PageHeader>
    <DataTable metricHeader={page === 'processes'} key={page} columnWidths={pref.columnWidths} onColumnWidthsChange={columnWidths => patch({ columnWidths })} scrollResetKey={`${pref.query}:${filters.currentUser}:${filters.sessionId}:${filters.state}:${pref.sortKey}:${pref.sortDirection}:${scrollReset}`} rows={model.rows} columns={columns} rowKey={row => row.process.identity} emptyDescription="请使用可执行文件名或PID搜索，或点击“重置视图”清除筛选；文件说明名称暂不参与搜索。" label={page === 'details' ? '进程详细信息' : '进程列表'} getHierarchy={tree ? row => ({ level: row.depth + 1, expanded: row.hasChildren ? !collapsed.has(row.process.identity) || searching : undefined }) : undefined} rowHeight={cssNumber(preferences.preferences.density === 'compact' ? '--height-row-compact' : '--height-row-standard', 32)} selected={selected?.identity??null} sort={sort} onSort={key => patch({ sortKey: key, sortDirection: pref.sortKey === key && pref.sortDirection === 'ascending' ? 'descending' : 'ascending', scrollTop: 0 })} onSelect={row => patch({ selectedIdentity: row.process.identity })} onActivate={row => setDetailTarget(row.process)} onContextMenu={(row, event) => openMenu(row.process, event)} onRowKey={(row, event) => {
      if (tree && !searching && ['ArrowLeft', 'ArrowRight'].includes(event.key)) { event.preventDefault(); if (event.key === 'ArrowLeft' && (collapsed.has(row.process.identity) || !row.hasChildren) && row.parent) locate(row.parent); else setCollapsed(previous => { const next = new Set(previous); if (event.key === 'ArrowRight') next.delete(row.process.identity); else next.add(row.process.identity); return next; }); }
    }} initialScrollTop={pref.scrollTop} onScrollTop={top => preferences.updatePage(page, { scrollTop: top }, false)} revealKey={reveal}/>
    {menu && <ContextMenu x={menu.x} y={menu.y} items={actionsFor(menu.target)} label={`${menu.target.name} PID ${menu.target.pid} 操作`} onClose={() => setMenu(null)}/>}
    {detailTarget && <ProcessDetail target={detailTarget} alive={alive(detailTarget)} connected={connected} onClose={() => setDetailTarget(null)} onAction={(command, target) => void runAction(command, target)} onLocateParent={locateParent} onService={name => { setDetailTarget(null); onService(name); }}/ >}
    {action && <ProcessActionDialog key={`${action.target.identity}|${action.action}`} action={action.action} target={action.target} alive={alive(action.target)} connected={connected} onClose={() => setAction(null)} onChanged={refresh}/>}
    {frozen && <span className="sr-only">显示冻结于 {dateTime(snapshot.capturedAtUtc)}</span>}
  </>;
}
