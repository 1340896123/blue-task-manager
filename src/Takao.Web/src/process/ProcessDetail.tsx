import { useEffect, useState, type ReactNode } from 'react';
import { api, ApiError, errorMessage, identityRoute } from '../api';
import { DataTable, MissingValue, Modal, cssNumber, type Sort } from '../components';
import { compareValues } from '../sorting';
import { bytes, dateTime, percent, rate, reason, uptime } from '../format';
import type { Process, ProcessDetails, ConnectionsResult, Connection } from '../types';
import ProcessIcon from './ProcessIcon';
import ApplicationIcon from '../ApplicationIcon';
import { networkScope } from './network';
const tcpStates:Record<string,string>={MIB_TCP_STATE_CLOSED:'已关闭',MIB_TCP_STATE_LISTEN:'正在侦听',MIB_TCP_STATE_SYN_SENT:'正在发起连接',MIB_TCP_STATE_SYN_RCVD:'已收到连接请求',MIB_TCP_STATE_ESTAB:'已建立',MIB_TCP_STATE_FIN_WAIT1:'等待关闭确认',MIB_TCP_STATE_FIN_WAIT2:'等待对端关闭',MIB_TCP_STATE_CLOSE_WAIT:'等待本地关闭',MIB_TCP_STATE_CLOSING:'正在关闭',MIB_TCP_STATE_LAST_ACK:'等待最后确认',MIB_TCP_STATE_TIME_WAIT:'关闭后等待',MIB_TCP_STATE_DELETE_TCB:'正在删除'};
export default function ProcessDetail({ target, alive, connected, onClose, onAction, onLocateParent, onService }: { target: Process; alive: boolean; connected: boolean; onClose: () => void; onAction: (action: string, target: Process) => void; onLocateParent: (target: Process) => void; onService: (name: string) => void }) {
  const [tab, setTab] = useState('overview'); const [detail, setDetail] = useState<ProcessDetails | null>(null); const [connections, setConnections] = useState<ConnectionsResult | null>(null);
  const [error, setError] = useState<string | null>(null); const [exited, setExited] = useState(!alive); const [loading, setLoading] = useState(false); const [revision, setRevision] = useState(0);
  const [connectionSort, setConnectionSort] = useState<Sort>({ key: 'protocol', direction: 'ascending' });
  const [connectionWidths, setConnectionWidths] = useState<Record<string, number>>({});
  useEffect(() => { if (!alive) setExited(true); }, [alive]);
  useEffect(() => {
    if (!target.creationTimeFileTime || exited || !connected) return;
    let active = true; let timer: number; const controller = new AbortController();
    const read = async () => {
      setLoading(true);
      try {
        if (tab === 'connections') {
          const result = await api.get<ConnectionsResult>(identityRoute(target, 'connections'), controller.signal);
          if (active) setConnections(result);
        } else {
          const result = await api.get<ProcessDetails>(identityRoute(target, 'details'), controller.signal);
          if (active) setDetail(result);
        }
        if (active) setError(null);
      } catch (caught) { if (active) { setError(errorMessage(caught)); if (caught instanceof ApiError && [404, 409].includes(caught.status)) setExited(true); } }
      finally { if (active) { setLoading(false); timer = window.setTimeout(() => void read(), document.hidden ? 5000 : 2000); } }
    };
    void read(); return () => { active = false; clearTimeout(timer); controller.abort(); };
  }, [target.identity, tab, exited, connected, revision]);
  const p = detail?.process ?? target; const writable = alive && !exited && connected;
  const value = (key: keyof Process, display?: ReactNode) => p[key] == null ? <MissingValue reason={reason(p.unavailable, key)}/> : display ?? String(p[key]);
  const serviceTooOld = detail?.relatedServicesCapturedAtUtc && p.startedAtUtc && Date.parse(p.startedAtUtc) > Date.parse(detail.relatedServicesCapturedAtUtc);
  const endpoint = (address: string, port: number, family: string) => `${family === 'IPv6' ? `[${address}]` : address}:${port}`;
  const connectionRows = [...connections?.connections ?? []].sort((a, b) => {
    const compare = (x: string | number | null, y: string | number | null) => compareValues(x, y, connectionSort.direction);
    if (connectionSort.key === 'local') return compare(a.localAddress, b.localAddress) || compare(a.localPort, b.localPort);
    if (connectionSort.key === 'remote') return compare(a.remoteAddress, b.remoteAddress) || compare(a.remotePort, b.remotePort);
    if (connectionSort.key === 'state') return compare(a.state ? tcpStates[a.state] ?? a.state : null, b.state ? tcpStates[b.state] ?? b.state : null);
    return compare(a.protocol, b.protocol) || compare(a.addressFamily, b.addressFamily);
  });
  return <Modal title="进程信息" wide onClose={onClose}><div className="detail-identity"><ProcessIcon process={p}/><strong>{p.name}</strong><span className="status-badge">PID {p.pid}</span><span className={`status-badge ${!exited ? 'success' : 'warning'}`}>{exited ? '已退出 / 身份已变化' : '运行中'}</span></div>
    <p className="detail-note">{exited ? '已保留最后成功信息；所有写操作已停用。' : '此详情按固定进程身份刷新，不随列表排序改变目标。'} {loading && '正在读取…'}</p>
    {error && <div className="form-error" role="alert">{error}</div>}
    <div className="detail-tabs" role="tablist" aria-label="进程信息页签">{[['overview', '概览'], ['services', '关联服务'], ['connections', 'TCP / UDP']].map(([id, label]) => <button key={id} role="tab" id={`detail-tab-${id}`} aria-selected={tab === id} aria-controls={`detail-panel-${id}`} tabIndex={tab === id ? 0 : -1} onClick={() => setTab(id)} onKeyDown={event => {
      const tabs = ['overview', 'services', 'connections']; if (['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) { event.preventDefault(); const next = event.key === 'Home' ? 0 : event.key === 'End' ? 2 : (tabs.indexOf(tab) + (event.key === 'ArrowRight' ? 1 : -1) + 3) % 3; setTab(tabs[next]); document.getElementById(`detail-tab-${tabs[next]}`)?.focus(); }
    }}>{label}</button>)}</div>
    <div className="detail-content" role="tabpanel" id={`detail-panel-${tab}`} aria-labelledby={`detail-tab-${tab}`}>
      {tab === 'overview' && <><dl className="process-details">
        {([
          ['映像路径', value('path')], ['发布者（文件元数据）', value('publisher')], ['用户', value('userName')],
          ['父 PID', <button className="text-button" disabled={!alive} onClick={() => onLocateParent(p)}>{p.parentPid} · 定位父进程</button>], ['会话', value('sessionId')],
          ['创建时间', p.startedAtUtc ? dateTime(p.startedAtUtc) : '不可读取'], ['已运行', p.uptimeSeconds == null ? '不可读取' : uptime(p.uptimeSeconds)],
          ['线程 / 句柄', <>{p.threadCount} / {value('handleCount')}</>], ['工作集', value('workingSetBytes', bytes(p.workingSetBytes))], ['专用提交', value('privateBytes', bytes(p.privateBytes))],
          ['优先级', value('priority')], ['CPU（整机归一化）', value('cpuPercent', percent(p.cpuPercent))], ['I/O 读取', value('ioReadBytesPerSecond', rate(p.ioReadBytesPerSecond))], ['I/O 写入', value('ioWriteBytesPerSecond', rate(p.ioWriteBytesPerSecond))],
          ['网络接收', value('networkReceiveBytesPerSecond', rate(p.networkReceiveBytesPerSecond))], ['网络发送', value('networkSendBytesPerSecond', rate(p.networkSendBytesPerSecond))], ['网络合计（接收+发送）', value('networkBytesPerSecond', rate(p.networkBytesPerSecond))],
          ['网络采样时刻', p.networkCapturedAtUtc ? <time dateTime={p.networkCapturedAtUtc} title={p.networkCapturedAtUtc}>{dateTime(p.networkCapturedAtUtc)}</time> : <MissingValue reason={reason(p.unavailable, 'networkBytesPerSecond', '尚无有效网络窗口')}/>],
          ['GPU（最忙引擎）', value('gpuPercent', percent(p.gpuPercent))], ['GPU 引擎', value('gpuEngine')], ['身份 FILETIME', value('creationTimeFileTime')],
        ] as [string, ReactNode][]).map(([label, content]) => <div key={label}><dt>{label}</dt><dd>{content}</dd></div>)}
      </dl><p className="detail-note">原生字段：{detail ? dateTime(detail.capturedAtUtc) : '选中快照'} · CPU/I/O速率及元数据：{detail?.snapshotCapturedAtUtc ? dateTime(detail.snapshotCapturedAtUtc) : '选中快照'}<br/>GPU：{p.gpuCapturedAtUtc ? dateTime(p.gpuCapturedAtUtc) : '尚无有效GPU样本'}。专用提交不是专用工作集；I/O不是物理磁盘吞吐；发布者来自文件信息，未验证数字签名。</p><p className="detail-note">网络：{networkScope} 独立完整窗口无流量为0；预热或采集不完整时保留未知。</p></>}
      {tab === 'services' && <><p className="detail-note">服务来源：{detail?.relatedServicesCapturedAtUtc ? dateTime(detail.relatedServicesCapturedAtUtc) : '尚未读取'}；服务为慢来源，关联状态以该时刻为准。</p>{serviceTooOld || detail?.unavailable.relatedServices ? <p className="page-note">{detail?.unavailable.relatedServices?.message ?? '服务采样早于此进程创建，暂不关联旧PID；请刷新慢来源后再查看。'}</p> : detail?.relatedServices.length ? <div className="related-list">{detail.relatedServices.map(service => <button key={service.name} className="command-button bordered" onClick={() => onService(service.name)}><ApplicationIcon kind="service" id={service.name}/>{service.displayName} · {service.name} · {service.state} →</button>)}</div> : <p className="page-note">此来源时刻没有关联到该身份的服务。</p>}</>}
      {tab === 'connections' && <><p className="detail-note">IP Helper 所属PID端点；连接数不是网络吞吐，不执行DNS。{connections ? `读取于 ${dateTime(connections.capturedAtUtc)}` : '正在按需读取…'} {connections?.truncated && '条目已达到上限，结果已截断。'}</p>{connections && Object.entries(connections.unavailable).map(([key, missing]) => <p key={key} className="form-error">{key}：{missing.message}</p>)}<DataTable<Connection> rows={connectionRows} sort={connectionSort} onSort={key => setConnectionSort(previous => ({ key, direction: previous.key === key && previous.direction === 'ascending' ? 'descending' : 'ascending' }))} columnWidths={connectionWidths} onColumnWidthsChange={setConnectionWidths} label="进程TCP UDP连接" rowHeight={cssNumber('--height-row-compact', 32)} rowKey={row => [row.protocol, row.addressFamily, row.localAddress, row.localPort, row.remoteAddress, row.remotePort].join('|')} columns={[
        { key: 'protocol', label: '协议', width: cssNumber('--width-column-pid', 80), render: row => `${row.protocol} ${row.addressFamily}` },
        { key: 'local', label: '本地端点', width: cssNumber('--width-column-name-detail', 240), render: row => endpoint(row.localAddress, row.localPort, row.addressFamily) },
        { key: 'remote', label: '远端端点', width: cssNumber('--width-column-name-detail', 240), render: row => row.remoteAddress == null || row.remotePort == null ? <span title="UDP无固定远端">不适用（UDP）</span> : endpoint(row.remoteAddress, row.remotePort, row.addressFamily) },
        { key: 'state', label: '状态', width: cssNumber('--width-column-text', 180), render: row => row.state ? <span title={row.state}>{tcpStates[row.state] ?? row.state}</span> : '不适用（UDP）' },
      ]} emptyDescription={connections ? '该进程当前没有可读端点。' : '等待真实端点读取。'}/></>}
    </div>
    <div className="detail-actions"><button className="command-button bordered" disabled={!p.path || !writable} onClick={() => onAction('location', p)}>打开文件位置</button><button className="command-button bordered" disabled={!p.path || !writable} onClick={() => onAction('properties', p)}>Windows 属性</button><button className="command-button bordered" disabled={!writable || !p.canTerminate} onClick={() => onAction('priority', p)}>设置优先级</button><button className="command-button bordered" disabled={!writable || !p.canTerminate} onClick={() => onAction('efficiency', p)}>效率模式</button><button className="command-button" onClick={() => onAction('copyInfo', p)}>复制信息</button><button className="command-button" disabled={exited || !connected} onClick={() => setRevision(v => v + 1)}>刷新详情</button></div>
  </Modal>;
}
