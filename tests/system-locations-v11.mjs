import { stopOwnedHost } from './etw-support.mjs';
import { clickCommand } from './layout-ui-support.mjs';
import {spawn,spawnSync} from 'node:child_process';
import {mkdtemp,mkdir,rm,writeFile} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {chromium} from 'playwright';
import {until,delay,processTree,ownedAlive,portOpen,check,assertions} from './e2e-support.mjs';
import {windows,nativeUi} from './native-ui-support.mjs';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..'),port=5202,cdp=9228,origin=`http://127.0.0.1:${port}`;
if(await portOpen(port)||await portOpen(cdp))throw new Error('System location test ports busy');
const temporary=await mkdtemp(path.join(tmpdir(),'blue-system-locations-'));
const host=spawn(path.join(root,'output/TaskManager-win-x64/TaskManager.Host.exe'),[`--port=${port}`],{cwd:tmpdir(),env:{...process.env,TASKMANAGER_DATA_DIR:temporary,TASKMANAGER_CDP_PORT:String(cdp)},windowsHide:true,stdio:'ignore'});
let browser,page,tree=[];const created=[],results=[];
const activate=async()=>{const own=windows('蓝域').find(w=>tree.some(p=>p.pid===w.pid));nativeUi(own,'foreground');await page.bringToFront();};
// Standard Shell automation independently exposes the actual folder URL even
// when Windows 11's breadcrumb UI Automation only exposes the localized name.
function shellFolderMatches(window, expected) {
 const script='$ErrorActionPreference="Stop"; [Console]::OutputEncoding=[Text.UTF8Encoding]::new($false); $shell=New-Object -ComObject Shell.Application; @($shell.Windows() | Where-Object { [string]$_.HWND -eq "'+window.hwnd+'" } | ForEach-Object { $_.LocationURL }) | ConvertTo-Json -Compress';
 const result=spawnSync('powershell',['-NoProfile','-NonInteractive','-Command',script],{encoding:'utf8',windowsHide:true,timeout:10000});
 if(result.status!==0)return false;
 const urls=JSON.parse(result.stdout||'[]');return (Array.isArray(urls)?urls:[urls]).some(url=>{try{return fileURLToPath(url).toLowerCase()===expected.toLowerCase();}catch{return false;}});
}
try{
 await until(async()=>(await(await fetch(origin+'/api/health')).json()).ready&&await portOpen(cdp),'system location release ready',45000);
 browser=await chromium.connectOverCDP(`http://127.0.0.1:${cdp}`);await until(()=>browser.contexts()[0]?.pages().length,'system location window');page=browser.contexts()[0].pages()[0];tree=processTree(host.pid);await activate();await page.getByRole('button',{name:'启动应用',exact:true}).click();
 for(const [label,expected] of [['当前用户启动文件夹',path.join(process.env.APPDATA,'Microsoft/Windows/Start Menu/Programs/Startup')],['公共启动文件夹',path.join(process.env.ProgramData,'Microsoft/Windows/Start Menu/Programs/Startup')]]){
  const before=new Set(windows('class:CabinetWClass',true).map(w=>w.hwnd));const [response]=await Promise.all([page.waitForResponse(r=>r.url().endsWith('/desktop/open-system-location')),clickCommand(page,label)]);check(response.ok(),label+' accepted by real Windows Shell');let matched;
  try{await until(()=>{for(const w of windows('class:CabinetWClass').filter(w=>!before.has(w.hwnd)||/Startup|启动/.test(w.title))){if(shellFolderMatches(w,expected)){matched=w;return true;}}return false;},label+' actual address',12000);}catch{}
  results.push({location:label,accepted:true,actualWindowVerified:Boolean(matched),evidence:matched?'Visible Explorer HWND matches exact official Startup path through standard Shell automation LocationURL':'Shell accepted; exact Windows address not independently exposed in this run'});
  if(matched&&!before.has(matched.hwnd)){created.push(matched);nativeUi(matched,'close');created.pop();}await activate();
 }
 const settingsBefore=new Set(windows('设置',true).map(w=>w.hwnd));const response=page.waitForResponse(r=>r.url().endsWith('/desktop/open-system-location'));await page.getByRole('button',{name:'Windows 启动应用设置',exact:true}).click();check((await response).ok(),'official ms-settings startupapps URI accepted');let settings;
 try{await until(()=>{for(const w of windows('设置').filter(w=>w.title==='设置')){const info=nativeUi(w,'inspect');if(info.elements.some(e=>/启动/.test(e.name))){settings=w;return true;}}return false;},'actual Startup apps Settings heading',15000);}catch{}
 results.push({location:'Windows启动应用设置',accepted:true,actualWindowVerified:Boolean(settings),evidence:settings?'Actual Windows Settings UI contains Startup heading':'Shell accepted; exact Windows page not independently exposed in this run'});
 if(settings&&!settingsBefore.has(settings.hwnd)){created.push(settings);nativeUi(settings,'close');created.pop();}await activate();await page.getByRole('button',{name:'服务',exact:true}).click();
 const serviceBefore=new Set(windows('class:MMCMainFrame',true).map(w=>w.hwnd));const serviceResponse=page.waitForResponse(r=>r.url().endsWith('/desktop/open-system-location'));await clickCommand(page,'Windows 服务管理器');check((await serviceResponse).ok(),'fixed Windows services.msc accepted');let service;
 try{await until(()=>{service=windows('class:MMCMainFrame').filter(w=>/服务/.test(w.title)).sort((a,b)=>Number(serviceBefore.has(a.hwnd))-Number(serviceBefore.has(b.hwnd)))[0];return Boolean(service);},'actual Services MMC window',15000);}catch{}
 results.push({location:'Windows服务管理器',accepted:true,actualWindowVerified:Boolean(service),evidence:service?'Actual Services MMC native window title/class verified; no service operations':'Shell accepted; Services MMC window not independently visible in this run'});
 if(service&&!serviceBefore.has(service.hwnd)){created.push(service);nativeUi(service,'close');created.pop();}await activate();
 const runtimeDirectory=path.join(root,'output/TaskManager-win-x64').toLowerCase()+path.sep;const descendants=processTree(host.pid);const exact=descendants.filter(p=>p.pid===host.pid||p.imagePath?.toLowerCase().startsWith(runtimeDirectory));check(exact.length>=3,'exact portable host/Electron identities observed for cleanup');console.log('system entry outcomes:',JSON.stringify(results));await page.getByRole('button',{name:'关闭窗口',exact:true}).click();await until(()=>host.exitCode!==null,'system location native close',20000);await until(async()=>!await portOpen(port)&&!await portOpen(cdp),'system location ports released');check(exact.every(p=>!ownedAlive(p)),'system entry test app identities cleaned');
 const report={result:results.every(v=>v.actualWindowVerified)?'PASS':'PARTIAL_WINDOWS_OBSERVABILITY',checkedAtUtc:new Date().toISOString(),assertions:assertions.length,checks:assertions,locations:results,cleanup:'only newly created exact verified Shell/Settings/MMC windows closed; prior windows preserved; no service changed'};const output=path.join(root,'output/playwright');await mkdir(output,{recursive:true});await writeFile(path.join(output,'system-locations-v11-results.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));
}finally{for(const w of created){try{nativeUi(w,'close');}catch{}}if(host.exitCode===null){const remaining=processTree(host.pid);await stopOwnedHost(host);for(const p of remaining.reverse())if(ownedAlive(p)){try{process.kill(p.pid);}catch{}}}await browser?.close().catch(()=>{});await delay(400);await rm(temporary,{recursive:true,force:true});}
