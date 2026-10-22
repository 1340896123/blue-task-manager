const textCollator = new Intl.Collator('zh-CN', { numeric: true });
import { AppWindow, Clock3 } from 'lucide-react';
import { dateTime, matches } from '../format';
import { cssNumber, type Sort } from '../components';
import type { PagePreference, Snapshot } from '../types';
export type Patch = (patch: Partial<PagePreference>, silent?: boolean) => void;
export type Notify = (message: string, error?: boolean) => void;
export const col = (name: 'name'|'pid'|'number'|'path'|'text', fallback=180) => cssNumber(`--width-column-${name}`,fallback);
export function sorted<T>(rows: T[], sort: Sort, value: (row: T, key: string) => string | number | bigint | null) {
  return [...rows].sort((a,b)=>{const x=value(a,sort.key),y=value(b,sort.key);if(x===null)return y===null?0:1;if(y===null)return -1;const n=typeof x==='string'&&typeof y==='string'?textCollator.compare(x,y):x<y?-1:x>y?1:0;return sort.direction==='ascending'?n:-n;});
}
export const nameCell=(name:string)=><span className="process-name"><span className="process-icon"><AppWindow aria-hidden="true"/></span><span title={name}>{name}</span></span>;
export const serviceState=(state:string)=>({SERVICE_RUNNING:'正在运行',SERVICE_STOPPED:'已停止',SERVICE_PAUSED:'已暂停',SERVICE_START_PENDING:'正在启动',SERVICE_STOP_PENDING:'正在停止',SERVICE_CONTINUE_PENDING:'正在继续',SERVICE_PAUSE_PENDING:'正在暂停',unknown:'未知'})[state]??state;
export const sessionState=(state:string)=>({WTSActive:'活动',WTSConnected:'已连接',WTSDisconnected:'已断开',WTSListen:'侦听',WTSIdle:'空闲'})[state]??state;
export function pidMatches(query:string,pid:number,...text:(string|null)[]){return /^\d+$/.test(query.trim())?pid===Number(query.trim()):matches(query,...text,pid);}
export function SlowFooter({data}:{data:Snapshot}){return <div className="selection-strip"><Clock3 size={14} aria-hidden="true"/><span>列表来源：{data.slowDataCapturedAtUtc?dateTime(data.slowDataCapturedAtUtc):'尚未读取'} · 通常每15秒更新，可手动刷新</span></div>;}
export function ResetView({patch,onReset}:{patch:Patch;onReset?:()=>void}){return <button className="text-button" onClick={()=>{patch({query:'',sortKey:'name',sortDirection:'ascending',scrollTop:0,selectedIdentity:null,filters:null});onReset?.();}}>重置此页视图</button>;}
