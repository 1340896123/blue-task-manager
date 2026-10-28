import { useId, useMemo, useState, type ReactNode } from 'react';
import { bytes, dateTime, matches, percent, rate, time, uptime } from './format';
import { Empty, MissingValue } from './components';
import type { Availability, PagePreference, Snapshot } from './types';
import HwInfo from './HwInfo';
type Point = { timestamp: string; values: (number | null)[] };
type Series = { label: string; color: string };
const primary = 'var(--color-chart-line)', secondary = 'var(--color-chart-secondary)';
function Chart({ points, series, label, intervalMs, percentage = true, mini = false, dense = false }: { points: Point[]; series: Series[]; label: string; intervalMs: number; percentage?: boolean; mini?: boolean; dense?: boolean }) {
  const width = 800, height = 200, pad = 3;
  const first = points.length ? Date.parse(points[0].timestamp) : 0, end = points.length ? Date.parse(points.at(-1)!.timestamp) : first;
  const span = Math.max(intervalMs, end - first), maximum = percentage ? 100 : Math.max(1024, ...points.flatMap(p => p.values.filter((v): v is number => v !== null)));
  const count = points.filter(p => p.values.some(v => v !== null)).length;
  return <div className={`chart-wrap ${mini ? 'mini-chart' : ''} ${dense ? 'dense-chart' : ''}`}>{!mini && !dense && <div className="chart-scale"><span>{percentage ? '使用率' : '字节 / 秒 · 按当前可见样本自动缩放'}</span><span>{percentage ? '100%' : rate(maximum)}</span></div>}
    <svg className="resource-chart" viewBox={`0 0 ${width} ${height}`} preserveAspectRatio="none" role={mini ? undefined : "img"} aria-hidden={mini || undefined} aria-label={`${label}，${count} 个真实时间点，中断处保留缺口`}>
      {!mini && [0,1,2,3,4].map(n => <line key={`h${n}`} x1="0" x2={width} y1={n*50} y2={n*50} className="chart-grid"/>)}
      {!mini && [0,1,2,3,4,5,6,7,8].map(n => <line key={`v${n}`} x1={n*100} x2={n*100} y1="0" y2={height} className="chart-grid"/>)}
      {series.map((s, field) => {
        const segments: { x: number; y: number; timestamp: string; value: number }[][] = []; let current: typeof segments[number] = [];
        points.forEach((p, index) => { const value=p.values[field], timestamp=Date.parse(p.timestamp); const gap=index>0 && timestamp-Date.parse(points[index-1].timestamp)>Math.max(intervalMs*1.8,1500);
          if (value == null || gap) { if(current.length) segments.push(current); current=[]; } if(value==null)return;
          current.push({x:pad+(timestamp-first)/span*(width-pad*2),y:height-pad-value/maximum*(height-pad*2),timestamp:p.timestamp,value}); });
        if(current.length) segments.push(current);
        return <g key={s.label} className={field ? 'chart-series-secondary' : 'chart-series-primary'}>{segments.map((part,i)=><g key={i}>{part.length>1&&<path d={part.map((p,j)=>`${j?'L':'M'}${p.x.toFixed(2)},${p.y.toFixed(2)}`).join(' ')} fill="none" stroke={s.color} vectorEffect="non-scaling-stroke"/>}{((!mini && !dense) || part.length===1) && part.map(p=><circle key={p.timestamp} cx={p.x} cy={p.y} r={part.length===1?3:1.3} fill={s.color}>{!mini && <title>{`${s.label} · ${time(p.timestamp)} · ${percentage?percent(p.value):rate(p.value)}`}</title>}</circle>)}</g>)}</g>;
      })}
    </svg>{!mini && !count&&<span className="chart-wait">{dense ? '等待有效采样' : '等待有效采样，未知值保留为空'}</span>}{!mini && !dense && <><div className="chart-axis"><span>{points.length?time(points[0].timestamp):'等待采样'}</span><span>{points.length?time(points.at(-1)!.timestamp):''}</span></div><div className="chart-legend">{series.map((s,i)=><span key={s.label}><i className={i?'secondary':'primary'}/>{s.label}{i?'（虚线）':'（实线）'}</span>)}</div></>}
  </div>;
}
function Reasons({ unavailable }: { unavailable: Availability }) { const messages=[...new Set(Object.values(unavailable).map(v=>v.message))]; return messages.length ? <div className="metric-reasons" role="status">{messages.map(m=><p key={m}>{m}</p>)}</div>:null; }
function Facts({ values }: { values: [string,ReactNode,boolean?][] }) { return <dl className="resource-fact-grid">{values.map(([label,value,primary])=><div key={label}><dt>{label}</dt><dd className={primary?'primary-metric':undefined}>{value}</dd></div>)}</dl>; }
function CpuGraphs({ snapshot, points, pref, patch }: { snapshot: Snapshot; points: Point[]; pref: PagePreference; patch: (patch: Partial<PagePreference>) => void }) {
  const mode = pref.cpuGraphMode === 'logicalProcessors' ? 'logicalProcessors' : 'total';
  const [page, setPage] = useState(0); const hint = useId();
  const perPage = 48, pageCount = Math.max(1, Math.ceil(snapshot.logicalProcessors.length / perPage));
  const activePage = Math.min(page, pageCount - 1);
  const processors = snapshot.logicalProcessors.slice(activePage * perPage, (activePage + 1) * perPage);
  const histories = useMemo(() => {
    if (mode !== 'logicalProcessors') return new Map<string, Point[]>();
    const result = new Map(processors.map(cpu => [cpu.id, [] as Point[]]));
    const seen = new Set<string>();
    for (const point of snapshot.performanceHistory.slice(-120)) {
      const timestamp = point.cpuTelemetryCapturedAtUtc;
      if (!timestamp || seen.has(timestamp)) continue;
      seen.add(timestamp);
      const values = new Map<string, number | null>();
      for (const cpu of point.logicalProcessors) if (result.has(cpu.id)) values.set(cpu.id, cpu.percent);
      for (const [id, history] of result) history.push({ timestamp, values: [values.get(id) ?? null] });
    }
    return result;
  }, [snapshot.performanceHistory, snapshot.logicalProcessors, activePage, mode]);
  const toggle = () => patch({ cpuGraphMode: mode === 'total' ? 'logicalProcessors' : 'total' });
  return <article className="chart-panel cpu-charts">
    <div className="cpu-graph-controls"><label>CPU 图表模式<select aria-label="CPU 图表模式" value={mode} onChange={event => patch({ cpuGraphMode: event.target.value as 'total' | 'logicalProcessors' })}><option value="total">总体使用率</option><option value="logicalProcessors">逻辑处理器</option></select></label></div>
    <p id={hint} className="cpu-graph-hint">双击 CPU 图表切换模式；聚焦图表后也可按 Enter 或空格。</p>
    <div className="cpu-chart-interaction" role="group" tabIndex={0} aria-label={`CPU 图表，${mode === 'total' ? '总体使用率' : '逻辑处理器'}模式`} aria-describedby={hint} onDoubleClick={toggle} onKeyDown={event => { if (event.target === event.currentTarget && (event.key === 'Enter' || event.key === ' ')) { event.preventDefault(); toggle(); } }}>
      {mode === 'total' ? <Chart points={points} series={[{ label: '总体使用率', color: primary }]} label="CPU 总体使用率" intervalMs={snapshot.intervalMs}/> : <>
        <p className="cpu-grid-caption">每图 0–100% · Windows 处理器组 / 组内逻辑处理器索引</p>
        {snapshot.telemetry?.unavailable.logicalProcessors && <Reasons unavailable={{ logicalProcessors: snapshot.telemetry.unavailable.logicalProcessors }}/>}
        {processors.length ? <div className="logical-cpu-grid">{processors.map(cpu => <article className="logical-cpu-chart" key={cpu.id} data-cpu-id={cpu.id}>
          <header><h3>组 {cpu.group} · CPU {cpu.index}</h3><span>{cpu.percent === null ? <MissingValue reason={cpu.unavailable.percent?.message ?? '此逻辑处理器尚无有效采样'}/> : percent(cpu.percent)}</span></header>
          <Chart dense points={histories.get(cpu.id) ?? []} series={[{ label: `组 ${cpu.group} CPU ${cpu.index}`, color: primary }]} label={`组 ${cpu.group} 逻辑处理器 ${cpu.index}，0 到 100%`} intervalMs={snapshot.intervalMs}/>
        </article>)}</div> : <Empty title="逻辑处理器正在初始化或暂不可用" description={snapshot.telemetry?.unavailable.logicalProcessors?.message ?? '等待 Windows PDH 提供真实处理器组及索引，不用总体使用率代替。'}/>}
      </>}
    </div>
    {mode === 'logicalProcessors' && pageCount > 1 && <nav className="cpu-chart-pagination" aria-label="逻辑处理器图表分页"><button className="command-button bordered" disabled={activePage === 0} onClick={() => setPage(activePage - 1)}>上一组图表</button><span>第 {activePage + 1} / {pageCount} 页 · {activePage * perPage + 1}–{Math.min((activePage + 1) * perPage, snapshot.logicalProcessors.length)} / {snapshot.logicalProcessors.length} 个逻辑处理器</span><button className="command-button bordered" disabled={activePage + 1 === pageCount} onClick={() => setPage(activePage + 1)}>下一组图表</button></nav>}
  </article>;
}
const linkSpeed=(raw:string)=>{const n=Number(BigInt(raw)); return `${(n>=1e9?n/1e9:n/1e6).toFixed(1)} ${n>=1e9?'Gbit/s':'Mbit/s'}`;};
type ResourceType = 'cpu'|'memory'|'disk'|'network'|'gpu'|'hwinfo';
type ResourceChoice = { type: ResourceType; id: string|null; name: string; description: string; detail?: string; summary: string; rates?: string[]; points: Point[] };
function historyPoints(snapshot: Snapshot, type: ResourceType, id: string|null): Point[] {
  if (type === 'hwinfo') return [];
  const output:Point[]=[]; const seen=new Set<string>();
  for(const point of snapshot.performanceHistory.slice(-120)) {
    const timestamp=type==='disk'||type==='gpu'?point.telemetryCapturedAtUtc:point.capturedAtUtc;
    if(!timestamp||seen.has(timestamp))continue;seen.add(timestamp);
    const resource=(type==='network'?point.networkAdapters:type==='disk'?point.physicalDisks:point.gpuAdapters).find(r=>r.id===id);
    output.push({timestamp,values:type==='cpu'?[point.cpuPercent]:type==='memory'?[point.memoryPercent]:type==='network'||type==='disk'?[resource?.readBytesPerSecond??null,resource?.writeBytesPerSecond??null]:[resource?.percent??null]});
  }
  return output;
}
export default function Performance({ snapshot, pref, patch, rowHeight }: { snapshot: Snapshot; pref: PagePreference; patch: (patch:Partial<PagePreference>,silent?:boolean)=>void; rowHeight: number }) {
  const sys=snapshot.system;
  const resources=useMemo(()=>{
    const order=<T extends {id:string}>(items:T[])=>[...items].sort((a,b)=>a.id.localeCompare(b.id,undefined,{numeric:true}));
    const choices:Omit<ResourceChoice,'points'>[]=[
      {type:'cpu',id:null,name:'CPU',description:'中央处理器',summary:percent(sys.cpuPercent)},
      {type:'memory',id:null,name:'内存',description:'物理内存',summary:`${bytes(sys.usedPhysicalBytes)} / ${bytes(sys.totalPhysicalBytes)} (${percent(sys.memoryPercent)})`},
      ...order(snapshot.physicalDisks.filter(d=>!d.isTotal)).map(d=>({type:'disk' as const,id:d.id,name:`磁盘 ${d.name}`,description:[d.model,d.mediaType,d.busType].filter(Boolean).join(' · ')||d.name,detail:[d.mediaType,d.busType].filter(Boolean).join(' · '),summary:`读 ${rate(d.readBytesPerSecond)}  写 ${rate(d.writeBytesPerSecond)}`,rates:[`读 ${rate(d.readBytesPerSecond)}`,`写 ${rate(d.writeBytesPerSecond)}`]})),
      ...order(snapshot.networkAdapters).map(a=>({type:'network' as const,id:a.id,name:a.alias,description:a.description,summary:`收 ${rate(a.receiveBytesPerSecond)}  发 ${rate(a.sendBytesPerSecond)}`})),
      ...order(snapshot.gpuAdapters).map(g=>({type:'gpu' as const,id:g.id,name:g.description??'GPU',description:g.description??`LUID ${g.adapterLuid}`,summary:percent(g.utilizationPercent)})),
    ];
    if(!snapshot.physicalDisks.some(d=>!d.isTotal))choices.splice(2,0,{type:'disk',id:null,name:'磁盘',description:'物理磁盘',summary:snapshot.telemetry?.capturedAtUtc?'当前不可用':'正在初始化'});
    if(!snapshot.networkAdapters.length)choices.splice(choices.findIndex(r=>r.type==='gpu')<0?choices.length:choices.findIndex(r=>r.type==='gpu'),0,{type:'network',id:null,name:'网络',description:'硬件网络适配器',summary:'暂未获取硬件网络适配器'});
    if(!snapshot.gpuAdapters.length)choices.push({type:'gpu',id:null,name:'GPU',description:'显卡适配器',summary:snapshot.telemetry?.capturedAtUtc?'当前不可用':'正在初始化'});
    choices.push({type:'hwinfo',id:null,name:'HWiNFO 传感器',description:'HWiNFO SM2 共享内存',summary:snapshot.hwInfo.status==='available'?`${snapshot.hwInfo.readingCount} 个读取项`:snapshot.hwInfo.status==='stale'?'数据已过期':'暂不可用'});
    return choices.map(choice=>({...choice,points:historyPoints(snapshot,choice.type,choice.id)}));
  },[snapshot]);
  const searching=Boolean(pref.query.trim());
  const categories:Record<ResourceType,string>={cpu:'CPU 中央处理器',memory:'内存 memory 物理内存',disk:'磁盘 disk 物理磁盘',network:'网络 network 硬件网络适配器',gpu:'GPU 显卡适配器',hwinfo:'HWiNFO 传感器 共享内存 温度 功耗 电压 风扇'};
  const visible=resources.filter(resource=>(resource.type==='hwinfo'&&pref.resourceType==='hwinfo')||matches(pref.query,categories[resource.type],resource.name,resource.description,resource.id));
  const savedType=pref.resourceType&&pref.resourceType!=='system'?pref.resourceType:'cpu';
  const preferredNetwork=snapshot.networkAdapters.find(a=>a.status==='IfOperStatusUp')??snapshot.networkAdapters[0];
  const saved=visible.find(r=>r.type===savedType&&r.id===pref.resourceId)??(!pref.resourceId?visible.find(r=>r.type===savedType&&(r.type!=='network'||r.id===preferredNetwork?.id))??visible.find(r=>r.type===savedType):undefined);
  const resource=saved??visible.find(r=>r.type===savedType&&(r.type!=='network'||r.id===preferredNetwork?.id))??visible.find(r=>r.type===savedType)??visible[0];
  const savedMissing=Boolean(!searching&&pref.resourceId&&!resources.some(r=>r.type===savedType&&r.id===pref.resourceId));
  if(!resource)return <Empty title="没有匹配的性能资源" description="请修改或清空顶部共用搜索，查看资源类别与本机实际设备。"/>;
  const {type,id:selectedId,points}=resource;
  const adapter=snapshot.networkAdapters.find(a=>a.id===selectedId),disk=snapshot.physicalDisks.find(d=>d.id===selectedId),gpu=snapshot.gpuAdapters.find(g=>g.id===selectedId);
  const isPdh=type==='disk'||type==='gpu';
  const unavailable=type==='network'?adapter?.unavailable??Object.fromEntries(Object.entries(sys.unavailable).filter(([key])=>['networkReceiveBytesPerSecond','networkSendBytesPerSecond'].includes(key))):type==='disk'?Object.fromEntries(Object.entries(disk?.unavailable??{}).filter(([key])=>key!=='activePercent')):type==='gpu'?gpu?.unavailable:Object.fromEntries(Object.entries(sys.unavailable).filter(([key])=>(type==='cpu'?['cpuPercent','threadCount','handleCount']:['memoryPercent','totalPhysicalBytes','availablePhysicalBytes','usedPhysicalBytes','commitBytes','commitLimitBytes','cacheBytes']).includes(key)));
  const resourceUnavailable:Availability={...unavailable};
  if((type==='gpu'||type==='disk'&&!disk)&&snapshot.telemetry?.unavailable[type])resourceUnavailable.telemetry=snapshot.telemetry.unavailable[type];
  const sourceTime=isPdh?snapshot.telemetry?.capturedAtUtc:snapshot.capturedAtUtc;
  const title=type==='cpu'?'CPU':type==='memory'?'内存':type==='network'?(adapter?.alias??'网络'):type==='disk'?`磁盘${disk?` ${disk.name}`:''}`:type==='hwinfo'?'HWiNFO 传感器':'GPU';
  const description=type==='network'?adapter?.description:type==='gpu'?gpu?.description:type==='disk'?disk?.model:undefined;
  return <div className="performance-workspace"><div className="resource-layout">
    <aside className="resource-list" aria-label="性能资源">{visible.map(choice=><button key={`${choice.type}:${choice.id??''}`} className={`resource-tile${choice.type==='disk'?' resource-tile-disk':''}`} aria-label={`${choice.type==='gpu'?'GPU · ':''}${choice.name}${choice.type==='disk'&&choice.description!==choice.name?` · ${choice.description}`:''} · ${choice.summary}${choice.type==='gpu'&&choice.id?` · ${snapshot.gpuAdapters.find(g=>g.id===choice.id)?.adapterLuid??choice.id}`:''}`} title={`${choice.name}${choice.description!==choice.name?` · ${choice.description}`:''}${choice.id?` · ${choice.type==='gpu'?snapshot.gpuAdapters.find(g=>g.id===choice.id)?.adapterLuid??choice.id:choice.id}`:''}`} aria-pressed={type===choice.type&&selectedId===choice.id} onClick={()=>patch({resourceType:choice.type,resourceId:choice.id,scrollTop:0})}>
      {choice.type!=='hwinfo'&&<Chart mini points={choice.points} series={choice.type==='network'?[{label:'接收',color:primary},{label:'发送',color:secondary}]:choice.type==='disk'?[{label:'读取',color:primary},{label:'写入',color:secondary}]:[{label:'使用率',color:primary}]} label={choice.name} intervalMs={snapshot.intervalMs} percentage={choice.type!=='network'&&choice.type!=='disk'}/>}
      <span className="resource-tile-text"><strong>{choice.type==='gpu'?'GPU':choice.name}</strong>{choice.type==='gpu'&&<span className="resource-device-name">{choice.name}</span>}{choice.detail&&<span className="resource-device-name">{choice.detail}</span>}<small>{choice.rates?choice.rates.map(value=><span key={value}>{value}</span>):choice.summary}</small></span>
    </button>)}</aside>
    <section key={`${type}:${selectedId}:${type==='hwinfo'?'':pref.query}`} className={`resource-content ${type==='hwinfo'?'hwinfo-content':'page-scroll'}`} onScroll={event=>{if(type!=='hwinfo')patch({scrollTop:event.currentTarget.scrollTop},true);}} ref={node=>{if(type!=='hwinfo'&&node&&node.dataset.restored!=='true'){node.scrollTop=pref.scrollTop;node.dataset.restored='true';}}}>
      <div className={`performance-heading${type==='disk'?' disk-heading':''}`}><h2>{title}</h2>{description&&<span title={description}>{description}</span>}</div>
      {type==='hwinfo'?<HwInfo summary={snapshot.hwInfo} intervalMs={snapshot.intervalMs} pref={pref} patch={patch} rowHeight={rowHeight}/>:<>
      {savedMissing&&<p className="metric-reasons" role="status">{savedType==='network'?'原选接口不在当前硬件网络适配器列表中，暂按当前硬件列表显示；断开硬件保留状态，没有硬件时显示空态。':'原选资源已不在当前枚举中，暂显示可用资源。'}重新选择可保存新偏好。</p>}
      <Reasons unavailable={resourceUnavailable}/>
      {!selectedId&&['disk','network','gpu'].includes(type)&&<p className="metric-reasons">{resource.summary}，尚无可显示的实际设备。</p>}
      {type==='cpu'?<CpuGraphs snapshot={snapshot} points={points} pref={pref} patch={patch}/>:type==='network'?<article className="chart-panel"><h3>接口接收与发送</h3><Chart points={points} series={[{label:'接收',color:primary},{label:'发送',color:secondary}]} label="选中网卡吞吐" intervalMs={snapshot.intervalMs} percentage={false}/></article>:type==='disk'?<article className="chart-panel"><h3>读取与写入</h3><Chart points={points} series={[{label:'读取',color:primary},{label:'写入',color:secondary}]} label="物理磁盘吞吐" intervalMs={snapshot.intervalMs} percentage={false}/></article>:<article className="chart-panel">{type==='gpu'&&<h3>最忙引擎使用率</h3>}<Chart points={points.map(p=>({...p,values:[p.values[0]]}))} series={[{label:type==='gpu'?'最忙引擎':'使用率',color:primary}]} label={title} intervalMs={snapshot.intervalMs}/></article>}
      {type==='cpu'&&<Facts values={ [['使用率',sys.cpuPercent===null?<MissingValue reason={sys.unavailable.cpuPercent?.message??'CPU采样尚未有效'}/>:percent(sys.cpuPercent),true],['逻辑处理器',sys.logicalProcessorCount],['进程',sys.processCount],['线程',sys.threadCount??<MissingValue reason="系统线程数当前不可读"/>],['句柄',sys.handleCount??<MissingValue reason="系统句柄数当前不可读"/>],['系统运行时间',uptime(sys.uptimeSeconds)]]}/>}
      {type==='memory'&&<Facts values={ [['已用物理内存',bytes(sys.usedPhysicalBytes),true],['可用物理内存',bytes(sys.availablePhysicalBytes),true],['系统提交 / 限制',`${bytes(sys.commitBytes)} / ${bytes(sys.commitLimitBytes)}`,true],['系统缓存',bytes(sys.cacheBytes)],['总物理内存',bytes(sys.totalPhysicalBytes)]]}/>}
      {type==='network'&&adapter&&<Facts values={ [['接收',rate(adapter.receiveBytesPerSecond),true],['发送',rate(adapter.sendBytesPerSecond),true],['接收 / 发送链路速度',`${linkSpeed(adapter.receiveLinkSpeedBitsPerSecond)} / ${linkSpeed(adapter.transmitLinkSpeedBitsPerSecond)}`],['接口描述',adapter.description],['LUID / 索引',`${adapter.id} / ${adapter.interfaceIndex}`],['类型 / 状态',`硬件网络适配器 · ${adapter.status}`]]}/>}
      {type==='disk'&&disk&&<Facts values={ [['读取',disk.readBytesPerSecond===null?<MissingValue reason={disk.unavailable.readBytesPerSecond?.message??'此磁盘读取速率尚不可用'}/>:rate(disk.readBytesPerSecond),true],['写入',disk.writeBytesPerSecond===null?<MissingValue reason={disk.unavailable.writeBytesPerSecond?.message??'此磁盘写入速率尚不可用'}/>:rate(disk.writeBytesPerSecond),true],['型号',disk.model??<MissingValue reason={disk.unavailable.model?.message??'Windows尚未提供此磁盘的型号'}/>],['类型',disk.mediaType??<MissingValue reason={disk.unavailable.mediaType?.message??'Windows尚未提供可确认的HDD/SSD类型'}/>],['总线类型',disk.busType??<MissingValue reason={disk.unavailable.busType?.message??'Windows尚未提供此磁盘的总线类型'}/>],['磁盘编号',disk.deviceNumber??<MissingValue reason={disk.unavailable.deviceNumber?.message??'尚未核验此磁盘的原生设备编号'}/>],['原始实例',disk.name],['身份读取时间',disk.identityCapturedAtUtc?dateTime(disk.identityCapturedAtUtc):<MissingValue reason={disk.unavailable.deviceNumber?.message??'尚未读取此磁盘的身份信息'}/>]]}/>}
      {type==='gpu'&&gpu&&<Facts values={ [['使用率',percent(gpu.utilizationPercent),true],['最忙引擎',gpu.busiestEngine??<MissingValue reason="当前未观察到有效引擎"/>],['引擎数',gpu.engineCount],['适配器描述',gpu.description??<MissingValue reason={gpu.unavailable.description?.message??'DXGI适配器描述暂不可用'}/>],['适配器 LUID',gpu.adapterLuid],['软件适配器',gpu.isSoftware===null?<MissingValue reason={gpu.unavailable.isSoftware?.message??'DXGI适配器类型暂不可用'}/>:gpu.isSoftware?'是':'否'],['PDH phys 索引',gpu.physicalIndex]]}/>}
      <details className="resource-explanation"><summary>资源说明与采样状态</summary><p>来源时刻：{type==='cpu'&&pref.cpuGraphMode==='logicalProcessors'?(snapshot.telemetry?.cpuCapturedAtUtc?dateTime(snapshot.telemetry.cpuCapturedAtUtc):'正在初始化 / 预热'):sourceTime?dateTime(sourceTime):'正在初始化 / 预热'} · 最多120个真实时间点。</p><p>{type==='network'?'此图仅显示Windows GetIfTable2报告的硬件网络适配器，排除虚拟、过滤、隧道和回环接口。断开的硬件仍保留实际状态与不可用原因；Windows报告的隐藏硬件也可能出现。吞吐为字节/秒，链路速度为位/秒；系统网络合计仅汇总活动硬件适配器。':type==='disk'?'型号、总线来自Windows存储设备描述符；HDD/SSD按Windows报告的寻道惩罚判定，复合、虚拟或不支持查询的设备类型保持未知。介质类型与总线类型分别显示。身份信息定期重新核验，时间为最近实际读取时刻。读取与写入来自PhysicalDisk吞吐计数器，单位为字节/秒。进程I/O不代表物理磁盘吞吐。':type==='gpu'?'显卡名称来自Windows，同名设备可按LUID区分；使用率取最忙引擎，不累加不同引擎百分比。':type==='memory'?'物理内存来自GlobalMemoryStatusEx；提交、限制、缓存、线程和句柄来自GetPerformanceInfo。缓存与已用物理内存不可直接相加。':'总体 CPU 来自 GetSystemTimes；逐逻辑处理器来自 Windows PDH Processor Information，按处理器组与组内索引标识，不代表物理核心。图表从本应用启动后开始，不补造之前的数据。'} 暂停、设备缺失、无效值保留缺口；PDH点按独立采样时刻去重。</p></details>
      </>}
    </section></div></div>;
}
