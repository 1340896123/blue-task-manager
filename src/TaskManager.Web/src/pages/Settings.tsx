import { Children, Fragment, isValidElement, useEffect, useRef, useState, type ReactNode } from 'react';
import { Check, Info } from 'lucide-react';
import { api, errorMessage } from '../api';
import { Empty } from '../components';
import { matches } from '../format';
import type { Bootstrap, Snapshot, PagePreference } from '../types';
import type { PreferencesController } from '../hooks/usePreferences';
import type { useTelemetry } from '../useTelemetry';
import type { Notify, Patch } from './pageShared';
import TaskManagerIntegration from './TaskManagerIntegration';
import { networkSourceText } from '../process/network';
import { UpdateSettingsRow, updateStatusText } from '../ApplicationUpdates';
import type { ApplicationUpdatesController } from '../hooks/useApplicationUpdates';
type WindowState={available:boolean;alwaysOnTop:boolean;zoomFactor:number};
// Search the same labels and descriptions that are rendered, including unavailable controls.
function textOf(node: ReactNode): string {
  return Children.toArray(node).map(child => typeof child === 'string' || typeof child === 'number' ? String(child) : isValidElement<{ children?: ReactNode }>(child) ? textOf(child.props.children) : '').join(' ');
}
const names:Record<string,string>={processes:'进程',system:'系统资源',network:'网络接口合计',services:'服务',users:'用户会话',startup:'启动项目',history:'本次历史',processNetwork:'逐进程网络吞吐',processGpu:'逐进程 GPU',diskActivity:'物理磁盘活动',diskIdentity:'磁盘型号与类型',networkAdapters:'逐网络适配器',gpuAdapters:'GPU适配器',gpuIdentity:'显卡名称与身份',logicalProcessors:'逐逻辑处理器使用率',hwInfo:'HWiNFO 传感器',processDetails:'进程详情',processConnections:'TCP / UDP端点',processPriority:'进程优先级',processTree:'进程树',processIcons:'Windows图标',serviceControl:'服务控制',shellLocations:'Windows位置与属性',efficiency:'效率模式'};
export default function Settings({data,bootstrap,connected,telemetry,preferences,updates,onUpdates,notify,onHelp,onReset,pref,patch,windowRevision}:{data:Snapshot;bootstrap:Bootstrap|null;connected:boolean;telemetry:ReturnType<typeof useTelemetry>;preferences:PreferencesController;updates:ApplicationUpdatesController;onUpdates:()=>void;notify:Notify;onHelp:()=>void;onReset:()=>void;pref:PagePreference;patch:Patch;windowRevision:number}){
  const [windowState,setWindowState]=useState<WindowState|null>(null),[windowError,setWindowError]=useState<string|null>(null),[busy,setBusy]=useState(false);
  const scroll = useRef<HTMLDivElement>(null), previousQuery = useRef(pref.query);
  useEffect(() => { if (previousQuery.current !== pref.query) { previousQuery.current = pref.query; if (scroll.current) scroll.current.scrollTop = 0; } }, [pref.query]);
  const readWindow=async()=>{try{setWindowState(await api.get<WindowState>('desktop/window-state'));setWindowError(null);}catch(e){setWindowError(errorMessage(e));}};
  useEffect(()=>{let active=true;const controller=new AbortController();void api.get<WindowState>('desktop/window-state',controller.signal).then(s=>{if(active)setWindowState(s);}).catch(e=>{if(active)setWindowError(errorMessage(e));});return()=>{active=false;controller.abort();};},[windowRevision]);
  const action=async(command:string)=>{setBusy(true);try{await api.post(`window/${command}`,{});await readWindow();}catch(e){setWindowError(errorMessage(e));}finally{setBusy(false);}};
  const settings=async(intervalMs:number,paused:boolean)=>{try{await telemetry.updateSettings({intervalMs,paused});notify(paused?'真实采集已暂停。':'真实采集设置已应用。');}catch(e){notify(errorMessage(e),true);}};
  const p=preferences.preferences;const available=Boolean(bootstrap?.desktop&&windowState?.available&&connected&&!busy);
  const section = (title: string, children: ReactNode, collapsible = false) => {
    const content = isValidElement<{ children?: ReactNode }>(children) && children.type === Fragment ? children.props.children : children;
    const rows = Children.toArray(content).filter(child => matches(pref.query, title) || matches(pref.query, textOf(child)));
    return rows.length ? <section className="settings-section">{collapsible ? <details className="settings-disclosure" open={pref.query.trim() ? true : undefined}><summary><h2>{title}</h2></summary>{rows}</details> : <><h2>{title}</h2>{rows}</>}</section> : null;
  };
  const sampling = section('常规', <><div className="setting-row"><div><strong>更新速度</strong><p>改变真实采样周期，不生成间隔内的额外点。当前 {data.intervalMs/1000} 秒。</p></div><select aria-label="采样间隔" value={data.intervalMs} disabled={telemetry.busy||!connected} onChange={e=>void settings(Number(e.target.value),data.paused)}><option value={1000}>每1秒</option><option value={2000}>每2秒</option><option value={5000}>每5秒</option></select></div>
      <div className="setting-row"><div><strong>自动采样</strong><p>暂停会停止真实采集和历史增长；继续后保留时间缺口。</p></div><button className="toggle" role="switch" aria-label="自动采样" aria-checked={!data.paused} disabled={!connected||telemetry.busy} onClick={()=>void settings(data.intervalMs,!data.paused)}><span/></button></div>
      <div className="setting-row"><div><strong>冻结列表显示</strong><p>仅保留所见列表与时刻，后台采集及性能曲线继续运行。</p></div><button className="command-button bordered" onClick={telemetry.toggleFrozen}>{telemetry.frozen?'继续显示最新列表':'冻结列表显示'}</button></div>
      <div className="setting-row"><div><strong>始终置顶</strong><p>{windowState?.available?`实际读回：${windowState.alwaysOnTop?'已置顶':'未置顶'}。关闭后会保存，下次桌面启动恢复。`:'桌面窗口不可用。'}</p></div><button className="toggle" role="switch" aria-label="始终置顶" aria-checked={windowState?.alwaysOnTop??false} disabled={!available} onClick={()=>void action('always-on-top')}><span/></button></div>
      <div className="setting-row"><div><strong>界面缩放</strong><p>{windowState?.available?`${Math.round(windowState.zoomFactor*100)}% · Ctrl + / − / 0；下次桌面启动恢复。`:'仅Electron桌面可调节。'} 窗口正常尺寸也会自动记住，最大化不覆盖正常尺寸。</p></div><div className="segmented"><button disabled={!available||(windowState?.zoomFactor??1)<=.75} aria-label="缩小界面" onClick={()=>void action('zoom-out')}>−</button><button disabled={!available} onClick={()=>void action('zoom-reset')}>100%</button><button disabled={!available||(windowState?.zoomFactor??1)>=2} aria-label="放大界面" onClick={()=>void action('zoom-in')}>+</button></div></div>
    </>);
  const view = section('视图', <>
      <div className="setting-row"><div><strong>表格密度</strong><p>紧凑模式显示更多行，标准模式提供更宽的行间距。</p></div><div className="segmented" aria-label="表格密度">{(['compact','standard'] as const).map(d=><button key={d} aria-pressed={p.density===d} onClick={()=>preferences.update(v=>({...v,density:d}))}>{d==='compact'?'紧凑':'标准'}</button>)}</div></div>
      <div className="setting-row"><div><strong>默认进程视图</strong><p>仅改变进程页的组织方式，详细信息页仍为平铺。</p></div><select aria-label="默认进程视图" value={p.processView} onChange={e=>preferences.update(v=>({...v,processView:e.target.value as 'flat'|'tree'}))}><option value="flat">平铺</option><option value="tree">进程树</option></select></div>
      <div className="setting-row"><div><strong>折叠侧栏</strong><p>窄窗口自动使用图标导航，所有入口仍有名称提示。</p></div><button className="toggle" role="switch" aria-label="折叠侧栏" aria-checked={p.sidebarCollapsed} onClick={()=>preferences.update(v=>({...v,sidebarCollapsed:!v.sidebarCollapsed}))}><span/></button></div>
    </>);
  const help = section('偏好与帮助', <div className="setting-row"><div><strong>界面偏好</strong><p>共用搜索在切页时自动应用；各页排序、选择、滚动、资源与进程列分别记住。恢复默认不清除采集历史或改变运行进程。</p>{preferences.warning&&<p className="metric-reasons">{preferences.warning}</p>}</div><div className="setting-buttons"><button className="command-button bordered" onClick={onHelp}>快捷键帮助（F1）</button><button className="command-button bordered" onClick={preferences.retry}>重新读取偏好</button><button className="command-button bordered" onClick={onReset}>恢复默认界面偏好…</button></div></div>);
  const sources = section('关于 / 本机数据来源', <dl className="about-list">{[['运行权限','启动必需管理员权限；新任务默认继承管理员权限'],['原生接口','Microsoft.Windows.CsWin32 · Win32'],['应用通信','本机同源HTTP · JSON.NET'],['逐逻辑处理器','Windows PDH Processor Information · 处理器组 / 组内索引'],['逐进程网络',`Windows ETW TCP/UDP IPv4/IPv6 · ${networkSourceText(data.processNetwork,data.paused)}`],['硬件传感器','HWiNFO SM2 只读共享内存；在性能页查看实际分组与会话统计'],['桌面模式',bootstrap?.desktop?'Electron.NET':'浏览器 / 无窗口主机'],['版本',bootstrap?.version??'正在读取']].filter(([k,v])=>matches(pref.query,'关于 / 本机数据来源')||matches(pref.query,k,v)).map(([k,v])=><div key={k}><dt>{k}</dt><dd>{v}</dd></div>)}</dl>, true);
  const capabilities = section('能力与采集边界', <div className="capability-list">{Object.entries(data.capabilities).filter(([key,c])=>matches(pref.query,'能力与采集边界')||matches(pref.query,names[key]??key,c.reason,c.source,c.available?'已接入':'未接入')).map(([key,c])=><div key={key}><span className={`status-badge ${c.available?'success':'warning'}`}>{c.available?<Check size={12} aria-hidden="true"/>:<Info size={12} aria-hidden="true"/>}{c.available?'已接入':'未接入'}</span><span><strong>{names[key]??key}</strong><small>{c.reason??c.source}</small></span></div>)}</div>, true);
  const integrationVisible = matches(pref.query, '任务栏右键中的任务管理器 接管原任务管理器入口 Windows IFEO Ctrl+Shift+Esc 当前计算机 所有用户 管理员 授权 注册 取消 默认关闭 启用 接管 修复 旧接管 请求的操作需要提升 刷新入口实际状态 完整程序目录 固定 读取 移动 删除 Debugger 权限 状态 影响');
  const updatesVisible = matches(pref.query, '应用更新 GitHub Release 检查更新 后台下载 下载更新 立即更新 下次启动自动更新 版本 进度 安装 校验', updateStatusText(updates.state, Boolean(bootstrap?.desktop)), updates.state?.candidateVersion ?? null, updates.state?.currentReleaseTag ?? updates.state?.currentVersion ?? null, updates.state?.installReason ?? null, updates.error ?? updates.state?.error?.message ?? null);
  return <div className="settings-page page-scroll" onScroll={e=>patch({scrollTop:e.currentTarget.scrollTop},true)} ref={node=>{scroll.current=node;if(node&&node.dataset.restored!=='true'){node.scrollTop=pref.scrollTop;node.dataset.restored='true';}}}>
    {sampling}{view}{windowError&&<p className="form-error" role="alert">{windowError}<button className="text-button" onClick={()=>void readWindow()}>重新读取窗口状态</button></p>}
    <div hidden={!integrationVisible} style={integrationVisible?undefined:{display:'none'}}><TaskManagerIntegration connected={connected} notify={notify}/></div>
    {updatesVisible && <section className="settings-section" aria-labelledby="application-updates-title"><h2 id="application-updates-title">应用更新</h2><UpdateSettingsRow updates={updates} desktop={Boolean(bootstrap?.desktop)} connected={connected} onOpen={onUpdates} notify={notify}/></section>}
    {help}{sources}{capabilities}
    {!sampling&&!view&&!integrationVisible&&!updatesVisible&&!help&&!sources&&!capabilities&&<Empty title="没有匹配的设置" description="请修改或清空顶部共用搜索，查看设置、说明、数据来源与能力。"/>}
  </div>;
}
