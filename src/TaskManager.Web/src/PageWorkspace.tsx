import { useState, type ReactNode } from 'react';
import { Plus, RefreshCw } from 'lucide-react';
import { api, errorMessage } from './api';
import { CommandMenu, DataTable, MissingValue, Modal, PageHeader, type Column } from './components';
import { bigBytes, bytes, cpuTime, dateTime, matches, percent, reason, time } from './format';
import Performance from './Performance';
import Services from './pages/Services';
import Settings from './pages/Settings';
import { col, nameCell, pidMatches, ResetView, sessionState, SlowFooter, sorted, userCell, type Notify, type Patch } from './pages/pageShared';
import ApplicationIcon from './ApplicationIcon';
import type { ProcessHistory, SessionUser, Snapshot, StartupItem, Page, PagePreference, Bootstrap } from './types';
import type { useTelemetry } from './useTelemetry';
import type { PreferencesController } from './hooks/usePreferences';
import type { ApplicationUpdatesController } from './hooks/useApplicationUpdates';
export default function PageWorkspace({data,liveData,page,pref,patch,rowHeight,telemetry,bootstrap,connected,preferences,locate,showSession,notify,refresh,onHelp,onReset,windowRevision,onRun,onClearHistory,samplingStatus,updates,onUpdates}:{data:Snapshot;liveData:Snapshot;page:Page;pref:PagePreference;patch:Patch;rowHeight:number;telemetry:ReturnType<typeof useTelemetry>;bootstrap:Bootstrap|null;connected:boolean;preferences:PreferencesController;locate:(identity:string)=>void;showSession:(sessionId:number)=>void;notify:Notify;refresh:()=>void;onHelp:()=>void;onReset:()=>void;windowRevision:number;onRun:()=>void;onClearHistory:()=>void;samplingStatus:ReactNode;updates:ApplicationUpdatesController;onUpdates:()=>void}){
  const [busy,setBusy]=useState(false); const [scrollReset,setScrollReset]=useState(0);
  const [startupDetailId,setStartupDetailId]=useState<string|null>(null);
  const startupDetail=data.startupItems.find(item=>item.id===startupDetailId);
  const sort={key:pref.sortKey,direction:pref.sortDirection};
  const onSort=(key:string)=>patch({sortKey:key,sortDirection:pref.sortKey===key&&pref.sortDirection==='ascending'?'descending':'ascending',scrollTop:0});
  const common={rowHeight,sort,onSort,columnWidths:pref.columnWidths,onColumnWidthsChange:(columnWidths:Record<string,number>)=>patch({columnWidths}),initialScrollTop:pref.scrollTop,onScrollTop:(scrollTop:number)=>patch({scrollTop},true),scrollResetKey:`${pref.query}:${pref.sortKey}:${pref.sortDirection}:${scrollReset}`};
  const location=async(value:string)=>{setBusy(true);try{const r=await api.post<{message:string}>('desktop/open-system-location',{location:value});notify(r.message);}catch(e){notify(errorMessage(e),true);}finally{setBusy(false);}};
  const copy=async(text:string)=>{try{await navigator.clipboard.writeText(text);notify('已复制到剪贴板。');}catch(e){notify(`复制失败：${errorMessage(e)}`,true);}};
  const run=<button className="command-button" disabled={!connected} onClick={onRun}><Plus size={16} aria-hidden="true"/>运行新任务</button>;
  const viewActions=<><button className="menu-action" data-menu-close onClick={refresh}><RefreshCw size={16} aria-hidden="true"/>刷新数据</button><button className="menu-action" data-menu-close aria-pressed={telemetry.frozen} onClick={telemetry.toggleFrozen}>{telemetry.frozen?'继续显示':'冻结显示'}</button><ResetView patch={patch} onReset={()=>setScrollReset(n=>n+1)}/>{samplingStatus}</>;
  if(page==='performance')return <><PageHeader title="性能">{run}<CommandMenu label="更多性能操作"><button className="menu-action" data-menu-close onClick={refresh}>刷新数据</button><ResetView patch={patch} onReset={()=>patch({cpuGraphMode:'total'})}/><p className="menu-caption">CPU 图表双击切换总体 / 逻辑处理器，也可使用图表模式选择器。曲线仅记录真实采样，缺失和中断保留缺口。</p>{samplingStatus}</CommandMenu></PageHeader><Performance snapshot={data} pref={pref} patch={patch} rowHeight={rowHeight}/></>;
  if(page==='services')return <Services samplingStatus={samplingStatus} liveData={liveData} data={data} pref={pref} patch={patch} rowHeight={rowHeight} connected={connected} locate={locate} notify={notify} refresh={refresh}/>;
  if(page==='settings')return <><PageHeader title="设置"><CommandMenu label="更多设置操作"><button className="menu-action" data-menu-close onClick={onHelp}>快捷键帮助（F1）</button><button className="menu-action" data-menu-close onClick={onReset}>恢复默认界面偏好…</button>{samplingStatus}</CommandMenu></PageHeader><Settings windowRevision={windowRevision} pref={pref} patch={patch} data={liveData} bootstrap={bootstrap} connected={connected} telemetry={telemetry} preferences={preferences} updates={updates} onUpdates={onUpdates} notify={notify} onHelp={onHelp} onReset={onReset}/></>;
  if(page==='history'){
    const liveIdentities=new Set(liveData.processes.map(p=>p.identity));
    const rows=sorted(data.history.filter(r=>pidMatches(pref.query,r.pid,r.name)),sort,(r,k)=>k==='cpu'?r.observedCpuTime100ns===null?null:BigInt(r.observedCpuTime100ns):k==='observedIoReadBytes'?r.observedIoReadBytes===null?null:BigInt(r.observedIoReadBytes):k==='observedIoWriteBytes'?r.observedIoWriteBytes===null?null:BigInt(r.observedIoWriteBytes):k==='pid'?r.pid:k==='state'?liveIdentities.has(r.identity)?'运行中':'已退出':k==='last'?Date.parse(r.lastObservedAtUtc):r.name);
    const selected=rows.find(r=>r.identity===pref.selectedIdentity);
    const go=(r:ProcessHistory)=>{if(liveIdentities.has(r.identity))locate(r.identity);else notify('此观测身份已退出，不能定位到复用相同PID的其他进程。',true);};
    const columns:Column<ProcessHistory>[]=[
      {key:'name',label:'名称',width:col('name',272),render:r=>nameCell(r.name,{kind:'history',id:r.identity})},
      {key:'cpu',label:'观测 CPU 时间',width:col('text'),numeric:true,render:r=>r.observedCpuTime100ns===null?<MissingValue reason="尚无有效累计观测，或该进程不可读取"/>:cpuTime(r.observedCpuTime100ns)},
      {key:'observedIoReadBytes',label:'I/O 读取累计',width:col('text'),numeric:true,render:r=>r.observedIoReadBytes===null?<MissingValue reason="尚无有效I/O累计观测"/>:bigBytes(r.observedIoReadBytes)},
      {key:'observedIoWriteBytes',label:'I/O 写入累计',width:col('text'),numeric:true,render:r=>r.observedIoWriteBytes===null?<MissingValue reason="尚无有效I/O累计观测"/>:bigBytes(r.observedIoWriteBytes)},
      {key:'pid',label:'PID',width:col('pid',80),numeric:true,render:r=>r.pid},
      {key:'state',label:'状态',width:col('number',112),render:r=><span className="running-state">{liveIdentities.has(r.identity)?'运行中':'已退出'}</span>},
      {key:'last',label:'最后观测',width:col('text'),render:r=><span title={dateTime(r.lastObservedAtUtc)}>{time(r.lastObservedAtUtc)}</span>}];
    return <><PageHeader title="应用历史记录">{run}<button className="command-button" disabled={!connected||!data.history.length} onClick={onClearHistory}>清除本次统计</button><CommandMenu label="更多历史记录操作"><button className="menu-action" data-menu-close disabled={!selected||!liveIdentities.has(selected.identity)} title={!selected?'请先选择记录':!liveIdentities.has(selected.identity)?'该身份已退出':undefined} onClick={()=>selected&&go(selected)}>定位仍运行的进程</button><p className="menu-caption">只累计本应用的观测；已退出身份保留统计，无法定位到复用PID的进程。</p>{viewActions}</CommandMenu></PageHeader><p className="history-session">本次会话起于 {dateTime(data.sessionStartedAtUtc)}</p><DataTable {...common} rows={rows} columns={columns} rowKey={r=>r.identity} selected={selected?.identity} onSelect={r=>patch({selectedIdentity:r.identity})} onActivate={go} label="本次会话应用历史记录" emptyDescription={pref.query.trim()?'请修改或清空顶部共用搜索，查看本次会话的观测记录。':'本次会话尚无观测记录，等待下一次真实采样。'}/></>;
  }
  if(page==='users'){
    const rows=sorted(data.users.filter(r=>pidMatches(pref.query,r.sessionId,r.userName,r.stationName)),sort,(r,k)=>k==='cpu'?r.cpuPercent:k==='memory'?r.workingSetBytes:k==='session'?r.sessionId:k==='station'?r.stationName:k==='state'?sessionState(r.state):k==='count'?r.processCount:r.userName||null);
    const selected=rows.find(r=>String(r.sessionId)===pref.selectedIdentity);
    const columns:Column<SessionUser>[]=[
      {key:'name',label:'用户',width:col('name',272),render:r=>r.userName?userCell(r.userName):<MissingValue reason="该会话用户名不可读取或尚无用户登录"/>},
      {key:'state',label:'状态',width:col('number',112),render:r=>sessionState(r.state)},
      {key:'cpu',label:'CPU',metric:percent(data.system.cpuPercent),width:col('number',112),numeric:true,render:r=>r.cpuPercent===null?<MissingValue reason={reason(r.unavailable,'cpuPercent')}/>:percent(r.cpuPercent)},
      {key:'memory',label:'内存',metric:percent(data.system.memoryPercent),width:col('number',112),numeric:true,render:r=>r.workingSetBytes===null?<MissingValue reason={reason(r.unavailable,'workingSetBytes')}/>:bytes(r.workingSetBytes)},
      {key:'session',label:'会话 ID',width:col('pid',80),numeric:true,render:r=>r.sessionId},
      {key:'station',label:'会话名称',width:col('text'),render:r=>r.stationName},
      {key:'count',label:'进程数',width:col('number',112),numeric:true,render:r=>r.processCount}];
    return <><PageHeader title="用户"><button className="command-button" disabled={!selected} title={!selected?'请先选择会话':undefined} onClick={()=>selected&&showSession(selected.sessionId)}>查看此会话的进程</button><CommandMenu label="更多用户操作"><p className="menu-caption">按Windows真实会话聚合。查看进程按会话ID筛选，包含该会话内其他账户的进程。当前账户：{bootstrap?.currentUserName??'正在读取'}。</p><SlowFooter data={data}/>{viewActions}</CommandMenu></PageHeader><DataTable {...common} metricHeader rows={rows} columns={columns} rowKey={r=>String(r.sessionId)} selected={selected?String(selected.sessionId):null} onSelect={r=>patch({selectedIdentity:String(r.sessionId)})} onActivate={r=>showSession(r.sessionId)} label="Windows 登录会话" emptyTitle={!data.slowDataCapturedAtUtc?'正在读取Windows来源':undefined} emptyDescription={!data.slowDataCapturedAtUtc?'正在首次读取Windows会话；基础进程采样继续。':undefined}/></>;
  }
  const rows=sorted(data.startupItems.filter(r=>matches(pref.query,r.name,r.command,r.source)),sort,(r,k)=>k==='source'?r.source:k==='enabled'?r.enabled===null?null:r.enabled?1:0:k==='command'?r.command:k==='location'?r.locationPath:r.name);
  const selected=rows.find(r=>r.id===pref.selectedIdentity);
  const open=async()=>{if(!selected?.canOpenLocation)return;const target=selected;setBusy(true);try{const r=await api.post<{message:string}>('startup/open-location',{id:target.id});notify(r.message);}catch(e){notify(errorMessage(e),true);}finally{setBusy(false);}};
  const columns:Column<StartupItem>[]=[
    {key:'name',label:'名称',width:col('name',272),render:r=>nameCell(r.name,{kind:'startup',id:r.id,revision:r.command})},
    {key:'enabled',label:'启用状态',width:col('text'),render:r=><span title={r.enabledReason} className="running-state">{startupState(r.enabled)}</span>},
    {key:'source',label:'来源',width:col('name',272),render:r=><span title={r.source}>{r.source}</span>},
    {key:'command',label:'启动命令 / 路径',width:col('path',420),render:r=><span title={r.command}>{r.command}</span>},
    {key:'location',label:'可定位来源',width:col('path',420),render:r=><span title={r.locationPath??r.locationReason??'来源不可定位'}>{r.locationPath??r.locationReason??'不可定位'}</span>}];
  return <><PageHeader title="启动应用"><button className="command-button" disabled={!connected||busy} onClick={()=>void location('startupSettings')}>Windows 启动应用设置</button><CommandMenu label="更多启动应用操作"><button className="menu-action" data-menu-close disabled={!selected} onClick={()=>selected&&setStartupDetailId(selected.id)}>启动项详情</button><button className="menu-action" data-menu-close disabled={!selected?.canOpenLocation||busy||!connected} title={selected?.locationReason??(!selected?'请先选择启动项':undefined)} onClick={()=>void open()}>打开来源位置</button><button className="menu-action" data-menu-close disabled={!selected} onClick={()=>selected&&void copy(selected.command)}>复制启动命令</button><button className="menu-action" data-menu-close disabled={!connected||busy} onClick={()=>void location('userStartup')}>当前用户启动文件夹</button><button className="menu-action" data-menu-close disabled={!connected||busy} onClick={()=>void location('commonStartup')}>公共启动文件夹</button><p className="menu-caption">只读显示Windows启动批准状态；详情提供状态来源、默认启用或未知原因。最终执行仍受Windows策略等条件影响，可前往Windows设置管理。</p><SlowFooter data={data}/>{viewActions}</CommandMenu></PageHeader><DataTable {...common} rows={rows} columns={columns} rowKey={r=>r.id} selected={selected?.id} onSelect={r=>patch({selectedIdentity:r.id})} onActivate={r=>setStartupDetailId(r.id)} label="真实启动应用" emptyTitle={!data.slowDataCapturedAtUtc?'正在读取Windows来源':undefined} emptyDescription={!data.slowDataCapturedAtUtc?'正在首次读取启动来源；基础进程采样继续。':pref.query?undefined:'当前Windows来源没有读取到启动项目。'}/>{startupDetail&&<Modal title="启动项详情" wide onClose={()=>setStartupDetailId(null)}><div className="detail-identity"><ApplicationIcon kind="startup" id={startupDetail.id} revision={startupDetail.command}/><strong>{startupDetail.name}</strong></div><dl className="process-details">{[['名称',startupDetail.name],['启用状态',startupState(startupDetail.enabled)],['状态说明',startupDetail.enabledReason],['来源',startupDetail.source],['启动命令',startupDetail.command],['来源位置',startupDetail.locationPath??startupDetail.locationReason??'不可定位']].map(([label,value])=><div key={label}><dt>{label}</dt><dd className="selectable">{value}</dd></div>)}</dl></Modal>}</>;
}

function startupState(enabled: StartupItem['enabled']) { return enabled===true?'已启用':enabled===false?'已禁用':'无法确定'; }
