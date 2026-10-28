import { stopOwnedHost } from './etw-support.mjs';
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { chromium } from 'playwright';
import { delay, until, portOpen, processTree, ownedAlive } from './e2e-support.mjs';
import { probe, windows, nativeUi } from './native-ui-support.mjs';

const root=path.resolve(import.meta.dirname,'..'), desktop=process.env.TASKMANAGER_LAYOUT_DESKTOP==='1', mode=desktop?'desktop':'browser';
const release=path.join(root,'output/Takao-win-x64'), port=5296, cdp=9296, origin=`http://127.0.0.1:${port}`;
const output=path.join(root,'output/playwright/windows-layout'), temporary=await mkdtemp(path.join(tmpdir(),'taskmanager-layout-'));
await mkdir(output,{recursive:true});
assert.ok(!await portOpen(port)&&!await portOpen(cdp),'dedicated layout ports are free');
const checks=[], screenshots=[], errors=[], geometry=[], viewportFacts=[], imageSizes=[], controls=[];
const check=(ok,message)=>{assert.ok(ok,message);checks.push(message);};
const host=spawn(path.join(release,'Takao.Host.exe'),[...(desktop?[]:['--headless']),`--port=${port}`],{cwd:release,env:{...process.env,TASKMANAGER_DATA_DIR:temporary,TASKMANAGER_CDP_PORT:String(cdp)},stdio:'ignore',windowsHide:true});
let browser,page,token,tree=[],failure;
const get=async route=>(await fetch(`${origin}/api/${route}`,{signal:AbortSignal.timeout(10000)})).json();
async function post(route,body){const response=await fetch(`${origin}/api/${route}`,{method:'POST',headers:{Origin:origin,'Content-Type':'application/json','X-TaskManager-Token':token},body:JSON.stringify(body)});assert.ok(response.ok,`${route}: ${response.status}`);return response.json();}
const nav=async name=>{await page.getByRole('button',{name,exact:true}).click();await page.locator('.toolbar h1').filter({hasText:name}).waitFor();};
const more=()=>page.locator('.command-menu > summary');
async function openMore(){if(!await page.locator('.command-menu').evaluate(el=>el.open))await more().click();}
async function capture(name){
  const file=`${mode}-${name}.png`,destination=path.join(output,file);
  if(desktop){const session=await page.context().newCDPSession(page);const result=await session.send('Page.captureScreenshot',{format:'png',fromSurface:true,captureBeyondViewport:false});await session.detach();await writeFile(destination,Buffer.from(result.data,'base64'));}
  else await page.screenshot({path:destination});
  const bytes=await readFile(destination);imageSizes.push({file,width:bytes.readUInt32BE(16),height:bytes.readUInt32BE(20)});screenshots.push(file);
  if(desktop&&name.startsWith('125-')){const window=windows('Takao').find(w=>tree.some(p=>p.pid===w.pid));assert.ok(window,'native capture belongs to exact test application tree');const nativeFile=`${mode}-${name}-native.png`;nativeUi(window,'capture',path.join(output,nativeFile));const nativeBytes=await readFile(path.join(output,nativeFile));imageSizes.push({file:nativeFile,width:nativeBytes.readUInt32BE(16),height:nativeBytes.readUInt32BE(20)});screenshots.push(nativeFile);}
}
const tableColumns={
  '进程':['name','state','cpuPercent','workingSetBytes','io','networkBytesPerSecond','gpuPercent','pid'],
  '应用历史记录':['name','cpu','observedIoReadBytes','observedIoWriteBytes','pid','state','last'],
  '启动应用':['name','enabled','source','command','location'],
  '用户':['name','state','cpu','memory','session','station','count'],
  '详细信息':['name','pid','state','userName','cpuPercent','workingSetBytes','priority','threadCount','parentPid','sessionId','path'],
  '服务':['name','pid','display','state']
};
async function viewport(width,height){
  if(!desktop)await page.setViewportSize({width,height});
  else {probe(tree.map(p=>`${p.pid}:${p.fileTime}`).join(','),'resize',width,height);await delay(350);}
}
async function layout(name,size){
  await nav(name);await page.getByRole('searchbox').fill('');await page.locator('.toolbar h1').click();await delay(150);
  const facts=await page.evaluate(()=>{const rect=el=>{const r=el.getBoundingClientRect();return {x:r.x,y:r.y,width:r.width,height:r.height,right:r.right,bottom:r.bottom};};const actions=[...document.querySelectorAll('.toolbar-actions > *')].map(rect);return {viewport:[innerWidth,innerHeight],visualViewport:visualViewport?{width:visualViewport.width,height:visualViewport.height,scale:visualViewport.scale}:null,dpr:devicePixelRatio,windowControls:rect(document.querySelector('.window-controls')),settingsControls:[...document.querySelectorAll('.setting-row > :last-child')].map(rect),overflow:document.documentElement.scrollWidth>innerWidth+1,title:rect(document.querySelector('.titlebar')),toolbar:rect(document.querySelector('.toolbar')),h1:document.querySelectorAll('h1').length,actions,old:[...document.querySelectorAll('.page-actions,.selection-strip,.process-controls,.statusbar,.brand-version')].filter(el=>el.getBoundingClientRect().height).length,sidebar:rect(document.querySelector('.sidebar'))};});
  viewportFacts.push({name,size,...facts});
  check(!facts.overflow,`${size} ${name}: no document horizontal overflow`);
  check(facts.h1===1&&facts.old===0,`${size} ${name}: one page title and no legacy permanent bars`);
  check(Math.abs(facts.title.height-48)<1&&Math.abs(facts.toolbar.height-56)<1,`${size} ${name}: 48px titlebar and 56px command bar`);
  check(facts.actions.every(r=>r.height<=34&&r.bottom<=facts.toolbar.bottom+1),`${size} ${name}: commands remain one line`);
  check(facts.windowControls.right<=facts.viewport[0]+1&&facts.actions.every(r=>r.right<=facts.viewport[0]+1)&&facts.settingsControls.every(r=>r.right<=facts.viewport[0]+1),`${size} ${name}: native-window, command and settings controls stay inside logical viewport`);
  if(size==='960')check(facts.sidebar.width===48,`${name}: compact 48px navigation at minimum width`);
  if(tableColumns[name]){
    const data=await page.locator('.data-table').evaluate(table=>({columns:[...table.querySelectorAll('th')].map(th=>th.dataset.columnKey),header:table.querySelector('thead').getBoundingClientRect().height,cells:[...table.querySelectorAll('tr[data-row-key]')].slice(0,1).flatMap(tr=>[...tr.cells].map(td=>({left:td.getBoundingClientRect().left,width:td.getBoundingClientRect().width}))),heads:[...table.querySelectorAll('th')].map(th=>({left:th.getBoundingClientRect().left,width:th.getBoundingClientRect().width})),rows:table.querySelectorAll('tr[data-row-key]').length}));
    geometry.push({name,size,...data});
    check(JSON.stringify(data.columns)===JSON.stringify(tableColumns[name]),`${name}: standard default column order`);
    check(Math.abs(data.header-(['进程','用户'].includes(name)?58:36))<1,`${name}: matching metric/plain header height`);
    check(data.cells.length===0||data.cells.every((c,i)=>Math.abs(c.width-data.heads[i].width)<1.1&&Math.abs(c.left-data.heads[i].left)<1.1),`${name}: virtual cells align with independent header pixels`);
    check(data.rows<80,`${name}: rendered rows remain bounded`);
  }
  await capture(`${size}-${Object.keys(tableColumns).indexOf(name)>=0?name:name==='性能'?'performance':'settings'}`);
}
try{
  await until(async()=>(await get('health')).ready,'elevated production Host ready',45000);
  const health=await get('health'),bootstrap=await get('bootstrap');token=bootstrap.token;
  check(/CsWin32/.test(health.nativeSource)&&/Newtonsoft|JSON.NET/.test(health.json),'real Windows CsWin32 and JSON.NET production chain');
  await until(async()=>(await get('snapshot')).slowDataCapturedAtUtc&&(await get('snapshot')).telemetry?.sampleId>1,'real slow data and PDH warmup',45000);
  if(desktop){await until(()=>portOpen(cdp),'Electron CDP ready',45000);browser=await chromium.connectOverCDP(`http://127.0.0.1:${cdp}`);await until(()=>browser.contexts().flatMap(c=>c.pages()).some(p=>p.url().startsWith(origin)),'production Electron page',60000);page=browser.contexts().flatMap(c=>c.pages()).find(p=>p.url().startsWith(origin));await until(async()=>(await get('bootstrap')).desktop,'real BrowserWindow registration');}
  else {browser=await chromium.launch({headless:true});page=await browser.newPage({viewport:{width:1380,height:860},reducedMotion:'reduce'});await page.goto(origin);}
  page.setDefaultTimeout(10000);page.on('pageerror',e=>errors.push(e.message));page.on('request',r=>{if(r.method()==='POST'&&/\/services\/.*\/control$|\/desktop\/task-manager-integration$/.test(r.url()))controls.push(r.url());});
  await page.emulateMedia({reducedMotion:'reduce'});await page.getByRole('table',{name:'进程列表',exact:true}).waitFor();tree=processTree(host.pid);
  check((await get('bootstrap')).desktop===desktop,`${mode}: actual bootstrap mode`);
  const pages=['进程','性能','应用历史记录','启动应用','用户','详细信息','服务','设置'];
  check(JSON.stringify(await page.locator('.nav-item').evaluateAll(items=>items.map(el=>el.getAttribute('aria-label'))))===JSON.stringify(pages),'fixed Windows navigation order, settings last');
  for(const size of [[1380,860],[960,640]]){await viewport(...size);for(const name of pages)await layout(name,String(size[0]));}
  await writeFile(path.join(output,`${mode}-screenshots-ready.json`),JSON.stringify({screenshots},null,2));
  await viewport(1380,860);await nav('进程');
  for(const key of ['Enter','Space']){
    await more().focus();await more().press(key);
    check(await page.locator('.command-menu').evaluate(el=>el.open),`${key}: native more disclosure opens without selection`);
    check(await page.locator('.frozen-banner').count()===0,`${key}: disclosure does not invoke process freeze shortcut`);
    const filter=page.locator('.control-popover > summary').filter({hasText:'筛选'});await filter.focus();await filter.press(key);
    check(await filter.locator('..').evaluate(el=>el.open),`${key}: nested filter opens`);
    await page.getByLabel('信息读取').selectOption('running');check(await page.locator('.command-menu').evaluate(el=>el.open),'changing nested filter retains outer menu');
    await page.getByRole('button',{name:'清除筛选',exact:true}).click();await filter.press(key);
    const columns=page.locator('.control-popover > summary').filter({hasText:'显示列'});await columns.press(key);check(await columns.locator('..').evaluate(el=>el.open),`${key}: nested columns opens`);
    await columns.locator('..').getByLabel('发布者',{exact:true}).check();check(await page.locator('.command-menu').evaluate(el=>el.open),'changing nested columns retains outer menu');
    await columns.locator('..').getByRole('button',{name:'恢复默认列',exact:true}).click();await columns.press(key);check(!await columns.locator('..').evaluate(el=>el.open),`${key}: nested columns closes`);await page.keyboard.press('Escape');
    check(!await page.locator('.command-menu').evaluate(el=>el.open)&&await more().evaluate(el=>el===document.activeElement),`${key}: Escape closes menu and restores summary focus`);
  }
  await openMore();check((await page.locator('.command-status').innerText()).includes('上次采样'),'sampling status exposes actual time and interval');
  await page.getByRole('button',{name:'冻结显示',exact:true}).click();const first=(await get('snapshot')).sampleId;await page.locator('.frozen-banner').waitFor();await delay(2200);check((await get('snapshot')).sampleId>first,'freezing display keeps real sampler advancing');await capture('frozen');await page.getByRole('button',{name:'继续显示最新数据',exact:true}).click();
  await nav('设置');await page.getByRole('switch',{name:'自动采样',exact:true}).click();await until(async()=>(await get('snapshot')).paused,'pause applied');await delay(1200);const paused=(await get('snapshot')).sampleId;await delay(1200);check((await get('snapshot')).sampleId===paused,'pause stops native sampling');check((await page.locator('.connection-banner').innerText()).includes('上次采样'),'pause displays last actual sample time');await capture('paused');await page.getByRole('button',{name:'继续采集',exact:true}).click();
  check(await page.locator('.settings-disclosure').count()===2,'sources and capability groups are native disclosures');
  let integrationGets=0;page.on('request',r=>{if(r.method()==='GET'&&r.url().endsWith('/api/desktop/task-manager-integration'))integrationGets++;});
  await page.getByRole('searchbox').fill('CsWin32');check(await page.locator('.settings-disclosure[open]').count()>0,'settings search opens matching collapsed source group');await page.getByRole('searchbox').fill('');check(integrationGets===0,'settings filtering keeps IFEO component mounted without rereads');
  await delay(900);const preferenceFile=path.join(temporary,'preferences.json'), savedPreferences=await readFile(preferenceFile);
  await rm(preferenceFile);await mkdir(preferenceFile);await page.getByRole('button',{name:'标准',exact:true}).click();
  await page.locator('.connection-banner').filter({hasText:'界面偏好未能保存'}).waitFor();check((await get('preferences')).warning?.code==='preferencesWriteFailed','real isolated preference I/O failure is visible below the header');await capture('preference-save-failure');
  await rm(preferenceFile,{recursive:true});await writeFile(preferenceFile,savedPreferences);await page.getByRole('button',{name:'紧凑',exact:true}).click();
  await until(async()=>(await get('preferences')).warning===null,'preference write succeeds after restoring own fixture');await page.locator('.connection-banner').filter({hasText:'界面偏好未能保存'}).waitFor({state:'detached'});check(true,'preference recovery clears visible failure after real successful save');
  await nav('性能');const snapshot=await get('snapshot');
  check(await page.locator('.resource-tile').count()===3+snapshot.physicalDisks.filter(d=>!d.isTotal).length+snapshot.networkAdapters.length+snapshot.gpuAdapters.length,'resource list includes CPU, memory, HWiNFO source and each actual enumerated device');
  check(await page.getByRole('group',{name:'资源类别'}).count()===0&&await page.locator('.resource-picker').count()===0,'no horizontal category tabs or nested device sidebar');
  for(let i=0;i<await page.locator('.resource-tile').count();i++){const tile=page.locator('.resource-tile').nth(i);await tile.click();const sensor=(await tile.innerText()).includes('HWiNFO');check(await tile.getAttribute('aria-pressed')==='true'&&(sensor?await page.getByRole('table',{name:'HWiNFO 传感器',exact:true}).count()===1:await page.locator('.resource-content .resource-chart').count()>0),'each actual resource selects its chart/facts or HWiNFO sensor table');}
  await capture('performance-devices');
  await nav('服务');const serviceSnapshot=await get('snapshot');let candidate;
  for(const service of serviceSnapshot.services.filter(s=>s.pid>0).slice(0,50)){const detail=await get(`services/${encodeURIComponent(service.name)}/details`);if(detail.canStop){candidate=detail;break;}}
  if(candidate){
    await page.getByRole('searchbox').fill(candidate.name);await page.locator(`tr[data-row-key="${candidate.name}"]`).click();
    const stop=page.locator('.toolbar-actions').getByRole('button',{name:'停止',exact:true});await until(()=>stop.isEnabled(),'service command waits for real permission read');
    let reads=0;page.on('request',r=>{if(r.method()==='GET'&&r.url().endsWith(`/api/services/${encodeURIComponent(candidate.name)}/details`))reads++;});
    await stop.click();const dialog=page.getByRole('dialog',{name:'服务详情与操作',exact:true});await dialog.getByRole('button',{name:'确认停止',exact:true}).waitFor();
    check(reads>0&&(await dialog.innerText()).includes(candidate.name)&&(await dialog.innerText()).includes('固定状态'),'service primary command rereads native permissions and confirms fixed object');await capture('service-confirm-cancel');
    await dialog.getByRole('button',{name:'取消',exact:true}).click();await dialog.getByRole('button',{name:'关闭对话框',exact:true}).click();
    check(controls.length===0,'canceling user-service confirmation sends no control request');
  }else check(serviceSnapshot.services.length>0,'no stoppable service exposed; real read-only service enumeration preserved');
  await page.getByRole('searchbox').fill('');
  if(desktop){await nav('进程');await page.keyboard.press('Control+0');await delay(350);const base=await page.evaluate(()=>devicePixelRatio);await page.keyboard.press('Control+=');await until(async()=>Math.abs((await page.evaluate(()=>devicePixelRatio))/base-1.25)<.02,'actual Electron 125%');for(const name of pages)await layout(name,'125');await page.keyboard.press('Control+0');check(await page.evaluate(()=>matchMedia('(prefers-reduced-motion: reduce)').matches),'real Electron reduced-motion mode');}
  check(controls.length===0,'layout/search validation sends no IFEO writes or user-service controls');check(errors.length===0,'no UI runtime errors');
}catch(error){failure={message:error.message,stack:error.stack};}
finally{
  const identities=processTree(host.pid);
  if(desktop&&page&&!page.isClosed()){await post('window/close',{}).catch(()=>{});await until(()=>host.exitCode!==null,'actual window closes Host',25000).catch(()=>{});}
  await browser?.close().catch(()=>{});if(host.exitCode===null)await stopOwnedHost(host);await until(()=>host.exitCode!==null||host.signalCode!==null,'owned Host exits').catch(()=>{});
  const clean=!await portOpen(port)&&!await portOpen(cdp)&&identities.every(p=>!ownedAlive(p));if(!clean&&!failure)failure={message:'test-owned identities or listeners remain'};
  await rm(temporary,{recursive:true,force:true});
  const report={result:failure?'FAIL':'PASS',mode,assertions:checks.length,checks,screenshots,imageSizes,geometry,viewportFacts,errors,failure,clean,source:'production Release UI → HTTP → JSON.NET → CsWin32; no mock data or IFEO writes'};await writeFile(path.join(output,`${mode}-results.json`),JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));if(failure)process.exitCode=1;
}
