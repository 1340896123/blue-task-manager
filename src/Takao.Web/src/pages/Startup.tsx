import { useEffect, useState, type KeyboardEvent, type MouseEvent, type ReactNode } from 'react';
import { api, errorMessage } from '../api';
import ApplicationIcon from '../ApplicationIcon';
import { CommandMenu, DataTable, Modal, PageHeader, type Column } from '../components';
import { matches } from '../format';
import ContextMenu, { type MenuItem } from '../process/ContextMenu';
import type { PagePreference, Snapshot, StartupItem } from '../types';
import { col, nameCell, SlowFooter, sorted, type Notify, type Patch } from './pageShared';

type Props = {
  data: Snapshot; liveData: Snapshot; pref: PagePreference; patch: Patch; rowHeight: number;
  connected: boolean; windowRevision: number; notify: Notify; viewActions: ReactNode; scrollResetKey: string;
};
type StartupMenu = { target: StartupItem; x: number; y: number };

export default function Startup({ data, liveData, pref, patch, rowHeight, connected, windowRevision, notify, viewActions, scrollResetKey }: Props) {
  const [busy, setBusy] = useState(false);
  const [detailId, setDetailId] = useState<string | null>(null);
  const [menu, setMenu] = useState<StartupMenu | null>(null);
  const detail = data.startupItems.find(item => item.id === detailId);
  const sort = { key: pref.sortKey, direction: pref.sortDirection };
  const rows = sorted(data.startupItems.filter(row => matches(pref.query, row.name, row.command, row.source)), sort,
    (row, key) => key === 'source' ? row.source : key === 'enabled' ? row.enabled === null ? null : row.enabled ? 1 : 0 : key === 'command' ? row.command : key === 'location' ? row.locationPath : row.name);
  const selected = rows.find(row => row.id === pref.selectedIdentity);
  const current = (target: StartupItem) => [data.startupItems, liveData.startupItems].every(items =>
    items.some(item => item.id === target.id && item.command === target.command && item.source === target.source));

  useEffect(() => { setMenu(null); }, [pref.query, pref.sortKey, pref.sortDirection, windowRevision]);
  useEffect(() => { if (menu && !current(menu.target)) setMenu(null); }, [menu, data.startupItems, liveData.startupItems]);

  const systemReason = !connected ? '未连接本机服务，请恢复连接后重试。' : busy ? '正在处理 Windows 打开请求，请稍候。' : undefined;
  const locationReason = (target: StartupItem | undefined) => !target ? '请先选择启动项。' : systemReason ??
    (!current(target) ? '启动来源已变化，请刷新列表。' : !target.canOpenLocation ? target.locationReason ?? '该来源无法可靠定位。' : undefined);
  const location = async (value: string) => {
    if (systemReason) return;
    setBusy(true);
    try { const result = await api.post<{ message: string }>('desktop/open-system-location', { location: value }); notify(result.message); }
    catch (error) { notify(errorMessage(error), true); }
    finally { setBusy(false); }
  };
  const open = async (target: StartupItem) => {
    const unavailable = locationReason(target);
    if (unavailable) { notify(unavailable, true); return; }
    setBusy(true);
    try { const result = await api.post<{ message: string }>('startup/open-location', { id: target.id }); notify(result.message); }
    catch (error) { notify(errorMessage(error), true); }
    finally { setBusy(false); }
  };
  const copy = async (text: string) => {
    try { await navigator.clipboard.writeText(text); notify('已复制到剪贴板。'); }
    catch (error) { notify(`复制失败：${errorMessage(error)}`, true); }
  };
  const openMenu = (target: StartupItem, event: MouseEvent | KeyboardEvent) => {
    if (event.currentTarget instanceof HTMLElement) event.currentTarget.focus({ preventScroll: true });
    patch({ selectedIdentity: target.id });
    if (!current(target)) { notify('启动来源已变化，请刷新后重新打开菜单。', true); return; }
    const bounds = event.currentTarget.getBoundingClientRect();
    setMenu({ target, x: 'clientX' in event ? event.clientX : bounds.left, y: 'clientY' in event ? event.clientY : bounds.bottom });
  };
  const actions = (target: StartupItem): MenuItem[] => {
    const changed = current(target) ? undefined : '启动来源已变化，请刷新列表。';
    return [
      { label: '启动项详情', disabled: Boolean(changed), reason: changed, action: () => setDetailId(target.id) },
      { label: '打开来源位置', disabled: Boolean(locationReason(target)), reason: locationReason(target), action: () => void open(target) },
      { label: '复制启动命令', disabled: Boolean(changed), reason: changed, action: () => void copy(target.command) },
      { label: 'Windows 启动应用设置', disabled: Boolean(systemReason), reason: systemReason, action: () => void location('startupSettings') },
    ];
  };
  const columns: Column<StartupItem>[] = [
    { key: 'name', label: '名称', width: col('name', 272), render: row => nameCell(row.name, { kind: 'startup', id: row.id, revision: row.command }) },
    { key: 'enabled', label: '启用状态', width: col('text'), render: row => <span title={row.enabledReason} className="running-state">{startupState(row.enabled)}</span> },
    { key: 'source', label: '来源', width: col('name', 272), render: row => <span title={row.source}>{row.source}</span> },
    { key: 'command', label: '启动命令 / 路径', width: col('path', 420), render: row => <span title={row.command}>{row.command}</span> },
    { key: 'location', label: '可定位来源', width: col('path', 420), render: row => <span title={row.locationPath ?? row.locationReason ?? '来源不可定位'}>{row.locationPath ?? row.locationReason ?? '不可定位'}</span> },
  ];

  return <>
    <PageHeader title="启动应用">
      <button className="command-button" disabled={Boolean(systemReason)} title={systemReason} onClick={() => void location('startupSettings')}>Windows 启动应用设置</button>
      <CommandMenu label="更多启动应用操作">
        <button className="menu-action" data-menu-close disabled={!selected} onClick={() => selected && setDetailId(selected.id)}>启动项详情</button>
        <button className="menu-action" data-menu-close disabled={Boolean(locationReason(selected))} title={locationReason(selected)} onClick={() => selected && void open(selected)}>打开来源位置</button>
        <button className="menu-action" data-menu-close disabled={!selected} onClick={() => selected && void copy(selected.command)}>复制启动命令</button>
        <button className="menu-action" data-menu-close disabled={Boolean(systemReason)} title={systemReason} onClick={() => void location('userStartup')}>当前用户启动文件夹</button>
        <button className="menu-action" data-menu-close disabled={Boolean(systemReason)} title={systemReason} onClick={() => void location('commonStartup')}>公共启动文件夹</button>
        <p className="menu-caption">右键或 Shift+F10 打开选中启动项菜单。只读显示 Windows 启动批准状态；详情提供状态来源、默认启用或未知原因。最终执行仍受 Windows 策略等条件影响，可前往 Windows 设置管理。</p>
        <SlowFooter data={data}/>{viewActions}
      </CommandMenu>
    </PageHeader>
    <DataTable rows={rows} columns={columns} rowKey={row => row.id} selected={selected?.id} onSelect={row => patch({ selectedIdentity: row.id })} onActivate={row => setDetailId(row.id)} onContextMenu={openMenu}
      rowHeight={rowHeight} sort={sort} onSort={key => patch({ sortKey: key, sortDirection: pref.sortKey === key && pref.sortDirection === 'ascending' ? 'descending' : 'ascending', scrollTop: 0 })}
      columnWidths={pref.columnWidths} onColumnWidthsChange={columnWidths => patch({ columnWidths })} initialScrollTop={pref.scrollTop} onScrollTop={scrollTop => patch({ scrollTop }, true)} scrollResetKey={scrollResetKey}
      label="真实启动应用" emptyTitle={!data.slowDataCapturedAtUtc ? '正在读取Windows来源' : undefined} emptyDescription={!data.slowDataCapturedAtUtc ? '正在首次读取启动来源；基础进程采样继续。' : pref.query ? undefined : '当前Windows来源没有读取到启动项目。'}/>
    {menu && <ContextMenu key={menu.target.id} x={menu.x} y={menu.y} items={actions(menu.target)} label={`${menu.target.name} 启动项操作`} onClose={() => setMenu(null)}/>}
    {detail && <Modal title="启动项详情" wide onClose={() => setDetailId(null)}>
      <div className="detail-identity"><ApplicationIcon kind="startup" id={detail.id} revision={detail.command}/><strong>{detail.name}</strong></div>
      <dl className="process-details">{[['名称', detail.name], ['启用状态', startupState(detail.enabled)], ['状态说明', detail.enabledReason], ['来源', detail.source], ['启动命令', detail.command], ['来源位置', detail.locationPath ?? detail.locationReason ?? '不可定位']].map(([label, value]) => <div key={label}><dt>{label}</dt><dd className="selectable">{value}</dd></div>)}</dl>
    </Modal>}
  </>;
}

function startupState(enabled: StartupItem['enabled']) { return enabled === true ? '已启用' : enabled === false ? '已禁用' : '无法确定'; }
