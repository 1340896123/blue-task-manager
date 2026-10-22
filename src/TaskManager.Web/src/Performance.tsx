import { useMemo, type ReactNode } from 'react';
import { Activity, Cpu, HardDrive, MemoryStick, Network, CircuitBoard } from 'lucide-react';
import { bytes, dateTime, percent, rate, time, uptime } from './format';
import { MissingValue } from './components';
import type { Availability, PagePreference, Snapshot } from './types';
type Point = { timestamp: string; values: (number | null)[] };
type Series = { label: string; color: string };
const primary = 'var(--color-chart-line)', secondary = 'var(--color-chart-secondary)';
function Chart({ points, series, label, intervalMs, percentage = true }: { points: Point[]; series: Series[]; label: string; intervalMs: number; percentage?: boolean }) {
  const width = 800, height = 200, pad = 3;
  const first = points.length ? Date.parse(points[0].timestamp) : 0, end = points.length ? Date.parse(points.at(-1)!.timestamp) : first;
  const span = Math.max(intervalMs, end - first), maximum = percentage ? 100 : Math.max(1024, ...points.flatMap(p => p.values.filter((v): v is number => v !== null)));
  const count = points.filter(p => p.values.some(v => v !== null)).length;
  return <div className="chart-wrap"><div className="chart-scale"><span>{percentage ? '使用率' : '字节 / 秒 · 按当前可见样本自动缩放'}</span><span>{percentage ? '100%' : rate(maximum)}</span></div>
    <svg className="resource-chart" viewBox={`0 0 ${width} ${height}`} preserveAspectRatio="none" role="img" aria-label={`${label}，${count} 个真实时间点，中断处保留缺口`}>
      {[0,1,2,3,4].map(n => <line key={`h${n}`} x1="0" x2={width} y1={n*50} y2={n*50} className="chart-grid"/>)}
      {[0,1,2,3,4,5,6,7,8].map(n => <line key={`v${n}`} x1={n*100} x2={n*100} y1="0" y2={height} className="chart-grid"/>)}
      {series.map((s, field) => {
        const segments: { x: number; y: number; timestamp: string; value: number }[][] = []; let current: typeof segments[number] = [];
        points.forEach((p, index) => { const value=p.values[field], timestamp=Date.parse(p.timestamp); const gap=index>0 && timestamp-Date.parse(points[index-1].timestamp)>Math.max(intervalMs*1.8,1500);
          if (value == null || gap) { if(current.length) segments.push(current); current=[]; } if(value==null)return;
          current.push({x:pad+(timestamp-first)/span*(width-pad*2),y:height-pad-value/maximum*(height-pad*2),timestamp:p.timestamp,value}); });
        if(current.length) segments.push(current);
        return <g key={s.label} className={field ? 'chart-series-secondary' : 'chart-series-primary'}>{segments.map((part,i)=><g key={i}>{part.length>1&&<path d={part.map((p,j)=>`${j?'L':'M'}${p.x.toFixed(2)},${p.y.toFixed(2)}`).join(' ')} fill="none" stroke={s.color} vectorEffect="non-scaling-stroke"/>}{part.map(p=><circle key={p.timestamp} cx={p.x} cy={p.y} r={part.length===1?3:1.3} fill={s.color}><title>{`${s.label} · ${time(p.timestamp)} · ${percentage?percent(p.value):rate(p.value)}`}</title></circle>)}</g>)}</g>;
      })}
    </svg>{!count&&<span className="chart-wait">等待有效采样，未知值保留为空</span>}<div className="chart-axis"><span>{points.length?time(points[0].timestamp):'等待采样'}</span><span>{points.length?time(points.at(-1)!.timestamp):''}</span></div><div className="chart-legend">{series.map((s,i)=><span key={s.label}><i className={i?'secondary':'primary'}/>{s.label}{i?'（虚线）':'（实线）'}</span>)}</div>
  </div>;
}
const resourceTypes = [{id:'cpu',label:'CPU',Icon:Cpu},{id:'memory',label:'内存',Icon:MemoryStick},{id:'network',label:'网络',Icon:Network},{id:'disk',label:'磁盘',Icon:HardDrive},{id:'gpu',label:'GPU',Icon:CircuitBoard}] as const;
function Reasons({ unavailable }: { unavailable: Availability }) { const messages=[...new Set(Object.values(unavailable).map(v=>v.message))]; return messages.length ? <div className="metric-reasons" role="status">{messages.map(m=><p key={m}>{m}</p>)}</div>:null; }
function Facts({ values }: { values: [string,ReactNode][] }) { return <dl className="resource-fact-grid">{values.map(([label,value])=><div key={label}><dt>{label}</dt><dd>{value}</dd></div>)}</dl>; }
const linkSpeed=(raw:string)=>{const n=Number(BigInt(raw)); return `${(n>=1e9?n/1e9:n/1e6).toFixed(1)} ${n>=1e9?'Gbit/s':'Mbit/s'}`;};
export default function Performance({ snapshot, pref, patch }: { snapshot: Snapshot; pref: PagePreference; patch: (patch:Partial<PagePreference>,silent?:boolean)=>void }) {
  const type = pref.resourceType && pref.resourceType !== 'system' ? pref.resourceType : 'cpu';
  const network = snapshot.networkAdapters;
  const choices = type==='network' ? network.map(a=>({id:a.id,name:a.alias,description:`${a.isHardware?'硬件':a.isLoopback?'回环':a.isTunnel?'隧道':'虚拟 / 软件'} · ${a.status==='IfOperStatusUp'?'已连接':a.status}`})) : type==='disk' ? snapshot.physicalDisks.map(d=>({id:d.id,name:d.name,description:'PhysicalDisk 原始实例'})) : type==='gpu' ? snapshot.gpuAdapters.map(g=>({id:g.id,name:g.description??`LUID ${g.adapterLuid}`,description:`${g.isSoftware===true?'软件适配器 · ':''}LUID ${g.adapterLuid} · phys 索引 ${g.physicalIndex} · ${g.engineCount} 个引擎`})) : [];
  const preferredNetwork=network.find(a=>a.isHardware&&a.status==='IfOperStatusUp')??network.find(a=>a.status==='IfOperStatusUp')??network[0];
  const savedMissing=Boolean(pref.resourceId&&!choices.some(c=>c.id===pref.resourceId));
  const selectedId=choices.find(c=>c.id===pref.resourceId)?.id??(type==='network'?preferredNetwork?.id:choices[0]?.id)??null;
  const adapter=network.find(a=>a.id===selectedId), disk=snapshot.physicalDisks.find(d=>d.id===selectedId), gpu=snapshot.gpuAdapters.find(g=>g.id===selectedId);
  const sys=snapshot.system; const isPdh=type==='disk'||type==='gpu';
  const points = useMemo(()=>{ const output:Point[]=[]; const seen=new Set<string>(); for(const p of snapshot.performanceHistory.slice(-120)) { const timestamp=isPdh?p.telemetryCapturedAtUtc:p.capturedAtUtc; if(!timestamp||seen.has(timestamp))continue;seen.add(timestamp);
    const resource=(type==='network'?p.networkAdapters:type==='disk'?p.physicalDisks:p.gpuAdapters).find(r=>r.id===selectedId);
    output.push({timestamp,values:type==='cpu'?[p.cpuPercent]:type==='memory'?[p.memoryPercent]:type==='network'?[resource?.readBytesPerSecond??null,resource?.writeBytesPerSecond??null]:type==='disk'?[resource?.percent??null,resource?.readBytesPerSecond??null,resource?.writeBytesPerSecond??null]:[resource?.percent??null]}); }return output; },[snapshot.performanceHistory,type,selectedId,isPdh]);
  const unavailable=type==='network'?adapter?.unavailable:type==='disk'?disk?.unavailable:type==='gpu'?gpu?.unavailable:Object.fromEntries(Object.entries(sys.unavailable).filter(([key])=>(type==='cpu'?['cpuPercent','threadCount','handleCount']:['memoryPercent','totalPhysicalBytes','availablePhysicalBytes','usedPhysicalBytes','commitBytes','commitLimitBytes','cacheBytes']).includes(key)));
  const sourceTime=isPdh?snapshot.telemetry?.capturedAtUtc:snapshot.capturedAtUtc;
  const title=type==='cpu'?'CPU 使用率':type==='memory'?'物理内存':type==='network'?(adapter?.alias??'网络适配器'):type==='disk'?`磁盘 ${disk?.name??'等待来源'}`:`GPU · ${gpu?(gpu.description??`LUID ${gpu.adapterLuid}`):'等待来源'}`;
  const value=type==='cpu'?percent(sys.cpuPercent):type==='memory'?percent(sys.memoryPercent):type==='network'?rate(adapter?.receiveBytesPerSecond??null):type==='disk'?percent(disk?.activePercent??null):percent(gpu?.utilizationPercent??null);
  return <div className="performance-workspace"><div className="resource-tabs" role="group" aria-label="资源类别">{resourceTypes.map(({id,label,Icon})=><button key={id} className="command-button" aria-pressed={type===id} onClick={()=>patch({resourceType:id,resourceId:null,scrollTop:0})}><Icon size={16} aria-hidden="true"/>{label}</button>)}</div><div className={`resource-layout ${['network','disk','gpu'].includes(type)?'has-resource-list':''}`}>
    {['network','disk','gpu'].includes(type)&&<aside className="resource-picker" aria-label="选择实际资源">{choices.length?choices.map(c=><button key={c.id} aria-pressed={selectedId===c.id} onClick={()=>patch({resourceId:c.id,scrollTop:0})}><strong>{c.name}</strong><small>{c.description}</small></button>):<p className="detail-note">等待真实资源枚举</p>}</aside>}
    <section key={`${type}:${selectedId}`} className="resource-content page-scroll" onScroll={event=>patch({scrollTop:event.currentTarget.scrollTop},true)} ref={node=>{if(node&&node.dataset.restored!=='true'){node.scrollTop=pref.scrollTop;node.dataset.restored='true';}}}>
      <div className="performance-heading"><div><h2>{title}</h2><p>{sourceTime?`来源时刻 ${dateTime(sourceTime)}`:'计数器正在初始化 / 预热'} · 最多120个真实点</p></div><strong className="resource-current">{value}</strong></div>
      {savedMissing&&<p className="page-note" role="status">原选资源已不在当前枚举中，暂显示可用资源；重新选择可保存新偏好。</p>}
      {unavailable&&<Reasons unavailable={unavailable}/>}{isPdh&&snapshot.telemetry&&<Reasons unavailable={snapshot.telemetry.unavailable}/>}
      {type==='network'?<article className="chart-panel"><h3>接口接收与发送</h3><Chart points={points} series={[{label:'接收',color:primary},{label:'发送',color:secondary}]} label="选中网卡吞吐" intervalMs={snapshot.intervalMs} percentage={false}/></article>:<article className="chart-panel"><h3>{type==='disk'?'活动时间（100% − 空闲时间）':type==='gpu'?'所选适配器最忙引擎使用率':title}</h3><Chart points={points.map(p=>({...p,values:[p.values[0]]}))} series={[{label:type==='disk'?'活动时间':type==='gpu'?'最忙引擎':'使用率',color:primary}]} label={title} intervalMs={snapshot.intervalMs}/></article>}
      {type==='disk'&&<article className="chart-panel"><h3>读取与写入</h3><Chart points={points.map(p=>({...p,values:p.values.slice(1)}))} series={[{label:'读取',color:primary},{label:'写入',color:secondary}]} label="物理磁盘吞吐" intervalMs={snapshot.intervalMs} percentage={false}/></article>}
      {type==='cpu'&&<Facts values={ [['逻辑处理器',sys.logicalProcessorCount],['进程',sys.processCount],['线程',sys.threadCount??<MissingValue reason="系统线程数当前不可读"/>],['句柄',sys.handleCount??<MissingValue reason="系统句柄数当前不可读"/>],['系统运行时间',uptime(sys.uptimeSeconds)]]}/>}
      {type==='memory'&&<Facts values={ [['已用物理内存',bytes(sys.usedPhysicalBytes)],['可用物理内存',bytes(sys.availablePhysicalBytes)],['总物理内存',bytes(sys.totalPhysicalBytes)],['系统提交 / 限制',`${bytes(sys.commitBytes)} / ${bytes(sys.commitLimitBytes)}`],['系统缓存',bytes(sys.cacheBytes)]]}/>}
      {type==='network'&&adapter&&<Facts values={ [['接口描述',adapter.description],['LUID / 索引',`${adapter.id} / ${adapter.interfaceIndex}`],['类型',adapter.isHardware?'硬件接口':adapter.isLoopback?'回环':adapter.isTunnel?'隧道':'虚拟 / 软件 / 过滤层'],['状态',adapter.status],['接收 / 发送',`${rate(adapter.receiveBytesPerSecond)} / ${rate(adapter.sendBytesPerSecond)}`],['接收 / 发送链路速度',`${linkSpeed(adapter.receiveLinkSpeedBitsPerSecond)} / ${linkSpeed(adapter.transmitLinkSpeedBitsPerSecond)}`]]}/>}
      {type==='disk'&&disk&&<Facts values={ [['原始实例',disk.name],['当前读取 / 写入',`${rate(disk.readBytesPerSecond)} / ${rate(disk.writeBytesPerSecond)}`],['整机最忙物理磁盘',percent(sys.diskActivePercent)],['所有非_Total实例读 / 写合计',`${rate(sys.diskReadBytesPerSecond)} / ${rate(sys.diskWriteBytesPerSecond)}`]]}/>}
      {type==='gpu'&&gpu&&<Facts values={ [['适配器描述',gpu.description??<MissingValue reason={gpu.unavailable.description?.message??'DXGI适配器描述暂不可用'}/>],['适配器 LUID',gpu.adapterLuid],['软件适配器',gpu.isSoftware===null?<MissingValue reason={gpu.unavailable.isSoftware?.message??'DXGI适配器类型暂不可用'}/>:gpu.isSoftware?'是':'否'],['PDH phys 索引',gpu.physicalIndex],['最忙引擎',gpu.busiestEngine??<MissingValue reason="当前未观察到有效引擎"/>],['引擎数',gpu.engineCount],['最忙适配器引擎（含软件适配器）',percent(sys.gpuPercent)]]}/>}
      <div className="capability-note"><Activity size={14} aria-hidden="true"/><p>{type==='network'?'此图仅显示所选GetIfTable2接口，速率单位为字节/秒，链路速度为位/秒。虚拟/过滤层可能经过同一流量；接口合计不能代表物理网线。':type==='disk'?'读写来自PhysicalDisk计数器，活动为100%减空闲时间；整机活动指最忙磁盘，不累加各盘百分比。进程I/O不是物理磁盘吞吐。':type==='gpu'?'显卡名称来自Windows；同名设备可按LUID区分。使用率取最忙引擎，不累加不同引擎的百分比。':type==='memory'?'物理内存来自GlobalMemoryStatusEx；系统提交、限制、缓存、线程和句柄来自GetPerformanceInfo。系统缓存与已用物理内存不可直接相加。':'CPU按全机逻辑处理器归一化。图表从本应用启动后开始，不补造之前的数据。'} 暂停、设备缺失、无效值保留缺口；PDH点按独立采样时刻去重。逐进程网络吞吐尚未接入，网络连接不是吞吐。</p></div>
    </section></div></div>;
}
