import { Activity, ArrowDown, ArrowUp, Cpu, MemoryStick, Network, Timer } from 'lucide-react';
import { bytes, dateTime, percent, rate, time, uptime } from './format';
import { MissingValue } from './components';
import type { HistoryPoint, Snapshot } from './types';

function Chart({ points, field, label, maximum = 100, intervalMs, color = 'var(--color-chart-line)' }: {
  points: HistoryPoint[]; field: 'cpuPercent' | 'memoryPercent' | 'networkReceiveBytesPerSecond'; label: string; maximum?: number; intervalMs: number; color?: string;
}) {
  const width = 800; const height = 200; const pad = 2;
  const first = points[0] ? Date.parse(points[0].capturedAtUtc) : 0;
  const end = points.length > 1 ? Date.parse(points[points.length - 1].capturedAtUtc) : first;
  const span = Math.max(intervalMs, end - first);
  const segments: { path: string; x: number; y: number; timestamp: string; value: number }[][] = [];
  let segment: typeof segments[number] = [];
  points.forEach((point, index) => {
    const value = point[field]; const timestamp = Date.parse(point.capturedAtUtc);
    const prior = points[index - 1];
    // A timestamp gap beyond the active sampling cadence is a discontinuity, never interpolated telemetry.
    const gap = prior && timestamp - Date.parse(prior.capturedAtUtc) > Math.max(intervalMs * 1.8, 1500);
    if (value === null || gap) { if (segment.length) segments.push(segment); segment = []; }
    if (value === null) return;
    const x = pad + ((timestamp - first) / span) * (width - pad * 2);
    const y = height - pad - Math.min(1, value / Math.max(1, maximum)) * (height - pad * 2);
    segment.push({ path: `${segment.length ? 'L' : 'M'}${x.toFixed(2)},${y.toFixed(2)}`, x, y, timestamp: point.capturedAtUtc, value });
  });
  if (segment.length) segments.push(segment);
  const count = points.filter(point => point[field] !== null).length;
  return <div className="chart-wrap">
    <svg className="resource-chart" viewBox={`0 0 ${width} ${height}`} preserveAspectRatio="none" role="img" aria-label={`${label}，${count} 个真实采样点，按实际时间绘制，采样中断处保留缺口`}>
      {[0, 1, 2, 3, 4].map(n => <line key={`h${n}`} x1="0" x2={width} y1={n * 50} y2={n * 50} className="chart-grid"/>)}
      {[0, 1, 2, 3, 4, 5, 6, 7, 8].map(n => <line key={`v${n}`} x1={n * 100} x2={n * 100} y1="0" y2={height} className="chart-grid"/>)}
      {segments.map((part, index) => <g key={index}>
        {part.length > 1 && <path d={part.map(point => point.path).join(' ')} fill="none" stroke={color} strokeWidth="1.8" vectorEffect="non-scaling-stroke"/>}
        {part.map(point => <circle key={point.timestamp} cx={point.x} cy={point.y} r={part.length === 1 ? 3 : 1.3} fill={color}><title>{`${time(point.timestamp)} · ${field.includes('Bytes') ? rate(point.value) : percent(point.value)}`}</title></circle>)}
      </g>)}
    </svg>
    {!count && <span className="chart-wait">等待有效采样，不填充历史数据</span>}
    <div className="chart-axis"><span>{points.length ? time(points[0].capturedAtUtc) : '等待采样'}</span><span>{points.length ? time(points[points.length - 1].capturedAtUtc) : ''}</span></div>
  </div>;
}
export default function Performance({ snapshot }: { snapshot: Snapshot }) {
  const system = snapshot.system;
  const maxNetwork = Math.max(1024, ...snapshot.performanceHistory.flatMap(point => point.networkReceiveBytesPerSecond === null ? [] : [point.networkReceiveBytesPerSecond]));
  return <div className="performance-page page-scroll">
    <div className="performance-heading"><div><h2>系统资源</h2><p>最近 {snapshot.performanceHistory.length} 个真实样本 · 中断处保留时间缺口</p></div><span className="source-chip"><Activity size={14} aria-hidden="true"/> CsWin32 采样</span></div>
    <div className="performance-layout">
      <aside className="resource-summary" aria-label="当前系统资源">
        <div className="resource-item active"><Cpu size={22} aria-hidden="true"/><div><span>CPU</span><strong>{percent(system.cpuPercent)}</strong><small>{system.logicalProcessorCount} 个逻辑处理器</small></div></div>
        <div className="resource-item"><MemoryStick size={22} aria-hidden="true"/><div><span>内存</span><strong>{percent(system.memoryPercent)}</strong><small>{bytes(system.usedPhysicalBytes)} / {bytes(system.totalPhysicalBytes)}</small></div></div>
        <div className="resource-item"><Network size={22} aria-hidden="true"/><div><span>网络接收</span><strong>{rate(system.networkReceiveBytesPerSecond)}</strong><small>{system.networkInterfaceCount} 个活动接口</small></div></div>
        <div className="resource-facts"><Timer size={16} aria-hidden="true"/><span>系统运行时间<strong>{uptime(system.uptimeSeconds)}</strong></span></div>
        <div className="resource-facts"><Activity size={16} aria-hidden="true"/><span>进程<strong>{system.processCount}</strong></span></div>
      </aside>
      <section className="chart-stack" aria-label="系统性能图表">
        <article className="chart-panel"><div className="chart-title"><h3>CPU 使用率</h3><strong>{percent(system.cpuPercent)}</strong></div><div className="chart-scale"><span>整机逻辑处理器归一化</span><span>100%</span></div><Chart points={snapshot.performanceHistory} field="cpuPercent" label="CPU 使用率" intervalMs={snapshot.intervalMs}/></article>
        <article className="chart-panel"><div className="chart-title"><h3>物理内存</h3><strong>{percent(system.memoryPercent)}</strong></div><div className="chart-scale"><span>已用 {bytes(system.usedPhysicalBytes)} · 可用 {bytes(system.availablePhysicalBytes)}</span><span>100%</span></div><Chart points={snapshot.performanceHistory} field="memoryPercent" label="物理内存使用率" intervalMs={snapshot.intervalMs} color="var(--color-chart-secondary)"/></article>
        <article className="chart-panel"><div className="chart-title"><h3>网卡吞吐</h3><span className="network-current"><span><ArrowDown size={14} aria-hidden="true"/>{rate(system.networkReceiveBytesPerSecond)}</span><span><ArrowUp size={14} aria-hidden="true"/>{rate(system.networkSendBytesPerSecond)}</span></span></div><div className="chart-scale"><span>蓝线：接收 · 活动接口合计，可能包含虚拟网卡</span><span>{rate(maxNetwork)}</span></div><Chart points={snapshot.performanceHistory} field="networkReceiveBytesPerSecond" label="网卡接收速率" maximum={maxNetwork} intervalMs={snapshot.intervalMs}/></article>
      </section>
    </div>
    <div className="capability-note"><strong>关于采集范围</strong><p>图表从本次应用启动后开始，按 UTC 时间戳展示。逐进程网络与 GPU <MissingValue reason={snapshot.capabilities.processGpu?.reason ?? '未接入逐进程 GPU 采集'}/>；本版不采集物理磁盘活动时间。最后采样：{dateTime(snapshot.capturedAtUtc)}。</p></div>
  </div>;
}
