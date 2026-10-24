import { AppWindow, Clock3 } from 'lucide-react';
import { dateTime, matches } from '../format';
import { cssNumber } from '../components';
export { sorted } from '../sorting';
import type { PagePreference, Snapshot } from '../types';
export type Patch = (patch: Partial<PagePreference>, silent?: boolean) => void;
export type Notify = (message: string, error?: boolean) => void;
export const col = (name: 'name'|'pid'|'number'|'path'|'text', fallback=180) => cssNumber(`--width-column-${name}`,fallback);
export const nameCell=(name:string)=><span className="process-name"><span className="process-icon"><AppWindow aria-hidden="true"/></span><span title={name}>{name}</span></span>;
export const serviceState=(state:string)=>({SERVICE_RUNNING:'正在运行',SERVICE_STOPPED:'已停止',SERVICE_PAUSED:'已暂停',SERVICE_START_PENDING:'正在启动',SERVICE_STOP_PENDING:'正在停止',SERVICE_CONTINUE_PENDING:'正在继续',SERVICE_PAUSE_PENDING:'正在暂停',unknown:'未知'})[state]??state;
export const sessionState=(state:string)=>({WTSActive:'活动',WTSConnected:'已连接',WTSDisconnected:'已断开',WTSListen:'侦听',WTSIdle:'空闲'})[state]??state;
export function pidMatches(query:string,pid:number,...text:(string|null)[]){return /^\d+$/.test(query.trim())?pid===Number(query.trim()):matches(query,...text,pid);}
export function SlowFooter({data}:{data:Snapshot}){return <p className="menu-caption"><Clock3 size={14} aria-hidden="true"/>列表来源：{data.slowDataCapturedAtUtc?dateTime(data.slowDataCapturedAtUtc):'尚未读取'} · 每15秒更新，可手动刷新</p>;}
export function ResetView({patch,onReset}:{patch:Patch;onReset?:()=>void}){return <button className="menu-action" data-menu-close onClick={()=>{patch({query:'',sortKey:'name',sortDirection:'ascending',columnWidths:null,scrollTop:0,selectedIdentity:null,filters:null});onReset?.();}}>重置此页视图</button>;}
