import assert from 'node:assert/strict';
import { spawn, spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { chromium } from 'playwright';
import { alive, delay, ownedAlive, portOpen, processTree, until } from './e2e-support.mjs';
import { nativeUi, probe, windows } from './native-ui-support.mjs';

const root=path.resolve(import.meta.dirname,'..');
const release=path.resolve(process.env.TASKMANAGER_FIRST_LAUNCH_RELEASE ?? path.join(root,'output/first-launch/TaskManager-win-x64'));
const output=path.join(root,'output/playwright/first-launch');
const temporary=await mkdtemp(path.join(tmpdir(),'taskmanager-first-launch-'));
const port=5321, cdp=9321, origin=`http://127.0.0.1:${port}`;
const checks=[], screenshots=[], geometry=[], errors=[], launches=[];
let current, failure, uiIntegrationWrites=0;
const check=(condition,message)=>{assert.ok(condition,message);checks.push(message);console.log('PASS '+message);};
const hash=value=>createHash('sha256').update(value).digest('hex');
const webIndex=await readFile(path.join(release,'wwwroot/index.html'));
const artifacts=[];
for(const relative of ['TaskManager.Host.exe','TaskManager.Host.dll','wwwroot/index.html',...[...webIndex.toString().matchAll(/(?:src|href)="([^\"]+\.(?:js|css))"/g)].map(match=>'wwwroot/'+match[1].replace(/^\//,''))]){
  const content=await readFile(path.join(release,relative));artifacts.push({path:relative,bytes:content.length,sha256:hash(content)});
}
const checkoutAtRun=spawnSync('git',['rev-parse','HEAD'],{cwd:root,encoding:'utf8',windowsHide:true}).stdout.trim();
function registry(){
  const command=String.raw`$base=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine,[Microsoft.Win32.RegistryView]::Registry64);function Read-Key($key){if(-not $key){return [ordered]@{exists=$false;values=@();subkeys=@()}};$values=@();foreach($name in @($key.GetValueNames()|Sort-Object -CaseSensitive)){$kind=$key.GetValueKind($name).ToString();$data=$key.GetValue($name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames);if($kind -in @('Binary','None')){$data=[Convert]::ToBase64String($data)}elseif($kind -in @('DWord','QWord')){$data=$data.ToString([Globalization.CultureInfo]::InvariantCulture)};$values += [ordered]@{name=$name;kind=$kind;data=$data}};$children=@();foreach($name in @($key.GetSubKeyNames()|Sort-Object -CaseSensitive)){$child=$key.OpenSubKey($name);try{$children += [ordered]@{name=$name;state=(Read-Key $child)}}finally{$child.Dispose()}};return [ordered]@{exists=$true;values=@($values);subkeys=@($children)}};$key=$base.OpenSubKey('SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\taskmgr.exe');try{Read-Key $key|ConvertTo-Json -Compress -Depth 50}finally{if($key){$key.Dispose()};$base.Dispose()}`;
  const result=spawnSync('powershell.exe',['-NoProfile','-Command',command],{windowsHide:true,encoding:'utf8',timeout:15000});
  assert.equal(result.status,0,result.stderr);return result.stdout.trim();
}
const before=registry(), beforeHash=hash(before);
async function api(route,body){
  const response=await fetch(`${origin}/api/${route}`,{method:body===undefined?'GET':'POST',headers:{Origin:origin,...(body===undefined?{}:{'Content-Type':'application/json','X-TaskManager-Token':current.token})},body:body===undefined?undefined:JSON.stringify(body),signal:AbortSignal.timeout(10000)});
  const value=await response.json();assert.ok(response.ok,`${route}: ${response.status}`);return value;
}
async function capture(name,native=false){
  const destination=path.join(output,name+'.png');
  const session=await current.page.context().newCDPSession(current.page);
  const result=await session.send('Page.captureScreenshot',{format:'png',fromSurface:true,captureBeyondViewport:false});await session.detach();
  await writeFile(destination,Buffer.from(result.data,'base64'));screenshots.push(name+'.png');
  if(native){const window=windows('蓝域').find(w=>current.tree.some(p=>p.pid===w.pid));assert.ok(window,'test-owned native window');nativeUi(window,'capture',path.join(output,name+'-native.png'));screenshots.push(name+'-native.png');}
}
async function open(directory,{desktop=true}={}){
  assert.ok(!await portOpen(port)&&!await portOpen(cdp),'dedicated test ports free');
  await mkdir(directory,{recursive:true});
  const host=spawn(path.join(release,'TaskManager.Host.exe'),[...(!desktop?['--headless']:[]),`--port=${port}`],{cwd:release,env:{...process.env,TASKMANAGER_DATA_DIR:directory,TASKMANAGER_CDP_PORT:String(cdp)},windowsHide:true,stdio:'ignore'});
  current={host,directory,desktop,tree:[],browser:null,page:null,token:null};
  await until(async()=>{const response=await fetch(`${origin}/api/health`,{signal:AbortSignal.timeout(1500)});return response.ok&&(await response.json()).ready;},'real production Host ready',45000);
  const bootstrap=await api('bootstrap');current.token=bootstrap.token;current.tree=processTree(host.pid);launches.push({pid:host.pid,fileTime:current.tree.find(p=>p.pid===host.pid)?.fileTime,desktop});
  check(probe('process-elevation',host.pid).elevated,'test Host has an actual elevated Windows token');
  if(desktop){
    await until(()=>portOpen(cdp),'real Electron CDP ready',45000);
    current.browser=await chromium.connectOverCDP(`http://127.0.0.1:${cdp}`);
    await until(()=>current.browser.contexts().flatMap(c=>c.pages()).some(p=>p.url().startsWith(origin)),'real Electron page',45000);
    current.page=current.browser.contexts().flatMap(c=>c.pages()).find(p=>p.url().startsWith(origin));
    await until(async()=>(await api('bootstrap')).desktop,'BrowserWindow registered');
    current.tree=processTree(host.pid);
  }else{current.browser=await chromium.launch({headless:true});current.page=await current.browser.newPage({viewport:{width:1380,height:860}});await current.page.goto(origin);}
  current.page.setDefaultTimeout(12000);current.page.on('pageerror',e=>errors.push(e.message));
  current.page.on('request',r=>{if(r.method()==='POST'&&r.url().endsWith('/api/desktop/task-manager-integration'))uiIntegrationWrites++;});
  await current.page.emulateMedia({reducedMotion:'reduce'});
  await current.page.getByRole('table',{name:'进程列表',exact:true}).waitFor();
  return current.page;
}
function ownedSessions(prefix){const result=spawnSync('logman.exe',['query','-ets'],{windowsHide:true,encoding:'utf8',timeout:15000});assert.equal(result.status,0,'independent ETW query');return [...new Set(result.stdout.match(/BlueDomain-Network-\d+-\d+-[a-f0-9]{32}/g)??[])].filter(name=>name.startsWith(prefix));}
async function close(){
  if(!current)return;
  const app=current;
  if(app.desktop&&alive(app.host.pid))await api('window/close',{}).catch(()=>{});
  if(app.desktop)await until(()=>app.host.exitCode!==null||app.host.signalCode!==null,'desktop Host exit',25000).catch(()=>{});
  await app.browser?.close().catch(()=>{});
  if(alive(app.host.pid)){
    const identity=probe('process-elevation',app.host.pid);const prefix=`BlueDomain-Network-${app.host.pid}-${identity.creationTimeFileTime}-`;
    app.host.kill();await until(()=>app.host.exitCode!==null||app.host.signalCode!==null,'owned Host exits');
    for(const name of ownedSessions(prefix)){const stopped=spawnSync('logman.exe',['stop',name,'-ets'],{windowsHide:true,encoding:'utf8',timeout:15000});assert.equal(stopped.status,0,'stop exact test-owned ETW session');}
  }
  await until(async()=>!await portOpen(port)&&!await portOpen(cdp),'test HTTP/CDP listeners released');
  check(app.tree.every(p=>!ownedAlive(p)),'test-owned Host and Electron creation identities exited');
  current=null;
}
const dialog=()=>current.page.getByRole('dialog',{name:'替换标准任务管理器？',exact:true});
async function prompt(){await dialog().waitFor();await until(async()=>!(await dialog().innerText()).includes('正在读取系统入口'),'first-launch actual integration status');}
async function saved(){await until(async()=>JSON.parse(await readFile(path.join(current.directory,'preferences.json'),'utf8')).ui.taskManagerPromptDismissed===true,'choice atomically persisted');check((await api('preferences')).preferences.taskManagerPromptDismissed===true,'choice reads back true through JSON.NET API');}
async function rearm(){const preferences=(await api('preferences')).preferences;await api('preferences',{...preferences,taskManagerPromptDismissed:false});await current.page.reload();await prompt();}
async function geometryCheck(name){
  const facts=await dialog().evaluate(el=>{const rect=node=>{const r=node.getBoundingClientRect();return {x:r.x,y:r.y,width:r.width,height:r.height,right:r.right,bottom:r.bottom};};const actions=el.querySelector('.modal-actions');return {viewport:[innerWidth,innerHeight],dpr:devicePixelRatio,dialog:rect(el),title:rect(el.querySelector('h2')),actions:rect(actions),buttons:[...actions.querySelectorAll('button')].map(rect),body:rect(el.querySelector('.first-launch-content')),documentOverflow:document.documentElement.scrollWidth>innerWidth+1,reducedMotion:matchMedia('(prefers-reduced-motion: reduce)').matches};});
  geometry.push({name,...facts});
  check(!facts.documentOverflow&&facts.dialog.x>=0&&facts.dialog.y>=0&&facts.dialog.right<=facts.viewport[0]+1&&facts.dialog.bottom<=facts.viewport[1]+1,`${name}: dialog fits actual logical viewport without page horizontal overflow`);
  check(facts.buttons.every(r=>r.x>=facts.dialog.x&&r.right<=facts.dialog.right+1&&r.y>=0&&r.bottom<=facts.viewport[1]+1)&&facts.title.y>=facts.dialog.y,`${name}: title and footer actions remain visible`);
  check(facts.reducedMotion,`${name}: reduced-motion enabled`);
}
await mkdir(output,{recursive:true});
await writeFile(path.join(output,'artifacts.json'),JSON.stringify({release,checkoutAtRun,artifacts,note:'Dedicated runtime includes the concurrent native-network working-tree baseline; artifact hashes identify exactly what was tested.'},null,2));
try{
  const browserDirectory=path.join(temporary,'browser');await mkdir(browserDirectory);
  await writeFile(path.join(browserDirectory,'preferences.json'),JSON.stringify({version:1,intervalMs:1000,ui:{version:1,density:'compact',sidebarCollapsed:false,processView:'flat',pages:{}}}));
  let page=await open(browserDirectory,{desktop:false});
  check((await api('preferences')).preferences.taskManagerPromptDismissed===false,'old version-1 preferences missing the marker load false');
  check(!(await api('bootstrap')).desktop&&await dialog().count()===0,'headless production Host and ordinary browser do not show first-launch prompt');
  check((await api('health')).nativeSource.includes('CsWin32')&&(await api('health')).json==='Newtonsoft.Json','real Windows CsWin32 and JSON.NET chain');
  await close();
  await writeFile(path.join(browserDirectory,'preferences.json'),'{broken preferences');
  page=await open(browserDirectory,{desktop:false});
  const recovered=await api('preferences');
  check(recovered.warning?.code==='preferencesRecovered'&&recovered.preferences.taskManagerPromptDismissed===false,'damaged preference file recovers with an explicit warning and incomplete choice');
  check(await dialog().count()===0,'ordinary browser remains unprompted after preference recovery');await close();

  const desktopDirectory=path.join(temporary,'desktop');page=await open(desktopDirectory);await prompt();
  const state=await api('desktop/task-manager-integration'), independent=JSON.parse(before);
  check(state.currentDebugger===(independent.values.find(v=>v.name==='Debugger')?.data??null),'native integration state matches independent HKLM64 Debugger');
  check(state.status==='conflict'&&state.canEnable===false&&await dialog().getByRole('button',{name:'前往设置接管现有入口',exact:true}).isEnabled(),'existing other-directory takeover offers an explicit settings takeover path');
  check((await dialog().innerText()).includes(state.conflictReason),'actual backend conflict reason is visible');
  const text=await dialog().innerText();check(['Ctrl+Shift+Esc','所有用户','管理员','取消接管','移动或删除'].every(t=>text.includes(t)),'prompt explains shortcuts, scope, authorization and restoration before moving');
  check(await dialog().getByRole('button',{name:'暂不替换',exact:true}).evaluate(el=>el===document.activeElement),'initial keyboard focus is on the safe defer action');
  await geometryCheck('1380x860');await capture('first-launch-1380');await dialog().screenshot({path:path.join(output,'first-launch-dialog.png')});screenshots.push('first-launch-dialog.png');
  const sample=(await api('snapshot')).sampleId;await delay(2200);check((await api('snapshot')).sampleId>sample,'native samples advance while first-launch dialog is open');
  await page.route('**/api/desktop/task-manager-integration',route=>route.abort('failed'));
  await dialog().getByRole('button',{name:'重新读取',exact:true}).click();await dialog().getByRole('alert').waitFor();
  check(await dialog().getByRole('button',{name:'替换标准任务管理器',exact:true}).isDisabled()&&(await api('preferences')).preferences.taskManagerPromptDismissed===false,'real transport read failure keeps replacement disabled and choice incomplete');
  await page.unroute('**/api/desktop/task-manager-integration');await dialog().getByRole('button',{name:'重新读取',exact:true}).click();await prompt();check((await dialog().innerText()).includes(state.conflictReason),'explicit reread recovers actual conflict state');
  for(let i=0;i<9;i++){await page.keyboard.press('Tab');check(await page.evaluate(()=>Boolean(document.activeElement?.closest('dialog[open]'))),`Tab ${i+1} remains within first-launch dialog`);}
  probe(current.tree.map(p=>`${p.pid}:${p.fileTime}`).join(','),'resize',960,640);await delay(800);await geometryCheck('960x640');await capture('first-launch-960');
  await page.keyboard.press('Control+=');await until(async()=>Math.abs((await api('desktop/window-state')).zoomFactor-1.25)<.001,'actual Electron zoom factor 1.25');
  await geometryCheck('960x640-at-125');await capture('first-launch-125',true);
  await page.keyboard.press('Control+0');await delay(350);
  await dialog().getByRole('button',{name:'暂不替换',exact:true}).click();await saved();
  check(uiIntegrationWrites===0&&hash(registry())===beforeHash,'defer performs no integration POST and preserves the entire existing IFEO configuration');
  for(const name of ['性能','应用历史记录','启动应用','用户','详细信息','服务','设置']){await page.getByRole('button',{name,exact:true}).click();await page.locator('.toolbar h1').filter({hasText:name}).waitFor();check(await dialog().count()===0,`${name}: navigation works and prompt remains completed`);}
  await page.getByRole('button',{name:'标准',exact:true}).click();await page.getByRole('combobox',{name:'采样间隔',exact:true}).selectOption('2000');
  await page.getByRole('button',{name:'恢复默认界面偏好…',exact:true}).click();await page.getByRole('dialog',{name:'恢复默认界面偏好？',exact:true}).getByRole('button',{name:'恢复默认界面偏好',exact:true}).click();
  await until(async()=>{const p=(await api('preferences')).preferences;return p.density==='compact'&&p.taskManagerPromptDismissed;},'reset visual defaults preserve completed choice');await saved();check(true,'reset restores visual defaults while preserving completed first-launch choice');
  await close();page=await open(desktopDirectory);await delay(900);check(await dialog().count()===0,'a real desktop restart does not repeat the saved first-launch prompt');
  check((await api('preferences')).preferences.taskManagerPromptDismissed===true&&(await api('snapshot')).intervalMs===2000,'window and sampling preferences preserve the completed marker across restart');

  await rearm();await page.keyboard.press('Escape');await saved();check(await dialog().count()===0,'Escape defers and saves without a system write');
  await rearm();await dialog().getByRole('button',{name:'关闭对话框',exact:true}).click();await saved();check(await dialog().count()===0,'close button defers and saves without a system write');
  await rearm();await dialog().getByRole('button',{name:state.status==='conflict'?'前往设置接管现有入口':'前往设置管理',exact:true}).click();await saved();
  await page.getByRole('heading',{name:'任务管理器入口',exact:true}).waitFor();check(await page.getByRole('searchbox').inputValue()==='','go to settings reveals the real integration section with cleared shared search');

  const preferences=(await api('preferences')).preferences;await api('preferences',{...preferences,taskManagerPromptDismissed:false});
  await page.route('**/api/preferences',route=>route.request().method()==='GET'?route.abort('failed'):route.continue());await page.reload();
  await page.getByRole('button',{name:'重新读取偏好',exact:true}).waitFor();check(await dialog().count()===0,'failed preference transport read never triggers the prompt from fallback defaults');
  await page.unroute('**/api/preferences');await page.getByRole('button',{name:'重新读取偏好',exact:true}).click();await prompt();check(true,'successful explicit preference reread reevaluates first-launch eligibility');
  const preferenceFile=path.join(desktopDirectory,'preferences.json'), fileBefore=await readFile(preferenceFile);await rm(preferenceFile);await mkdir(preferenceFile);
  await dialog().getByRole('button',{name:'暂不替换',exact:true}).click();await page.getByText('选择未保存，重启可能再次提示。请检查偏好保存提示。',{exact:true}).waitFor();
  check((await api('preferences')).preferences.taskManagerPromptDismissed===false,'real preference I/O failure does not claim the choice was persisted');await capture('first-launch-save-failure');
  await close();await rm(preferenceFile,{recursive:true});await writeFile(preferenceFile,fileBefore);
  page=await open(desktopDirectory);await prompt();check(true,'restart presents the prompt again after the choice could not be saved');await dialog().getByRole('button',{name:'暂不替换',exact:true}).click();await saved();
  check(uiIntegrationWrites===0&&errors.length===0,'all first-launch UI branches send no integration write and raise no renderer exception');
  await close();check(hash(registry())===beforeHash,'final independent recursive IFEO fingerprint exactly matches the initial state');
}catch(error){failure={message:error.message,stack:error.stack};if(current?.page)await capture('failure').catch(()=>{});}
finally{
  if(current)await close().catch(error=>{failure??={message:error.message,stack:error.stack};});
  const globalRegistryUnchanged=hash(registry())===beforeHash;if(!globalRegistryUnchanged)failure??={message:'global IFEO changed during read-only test'};
  await rm(temporary,{recursive:true,force:true,maxRetries:5,retryDelay:200});
  const report={result:failure?'FAIL':'PASS',checkedAtUtc:new Date().toISOString(),checkoutAtRun,artifacts,assertions:checks.length,checks,screenshots,geometry,launches,errors,uiIntegrationWrites,globalRegistryUnchanged,unexecuted:['F06 replacement success and restore: current global entry belongs to the installed directory; no replacement or fixtures permitted','F05 native legacy/no-write/missing-binary and integration write/readback failure branches: no corresponding real condition; not fabricated'],failure,source:'production self-contained Electron.NET → same-origin HTTP → JSON.NET → CsWin32; only transport failures and private preference-file I/O faults were induced'};
  await writeFile(path.join(output,'results.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));if(failure)process.exitCode=1;
}
