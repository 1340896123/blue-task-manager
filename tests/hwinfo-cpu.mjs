import assert from 'node:assert/strict';
import { spawn, spawnSync } from 'node:child_process';
import { mkdir, mkdtemp, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { delay, until, portOpen, processTree, ownedAlive } from './e2e-support.mjs';
import { probe } from './native-ui-support.mjs';

const root=path.resolve(import.meta.dirname,'..'), mode=process.argv.includes('--desktop')?'desktop':process.argv.includes('--browser')?'browser':process.env.TASKMANAGER_HWINFO_MODE??'native';
assert.ok(['native','browser','desktop'].includes(mode));
const desktop=mode==='desktop', framework='net10.0-windows10.0.19041.0';
const release=desktop?path.join(root,'output/TaskManager-win-x64'):path.join(root,'src/TaskManager.Host/bin/Release',framework);
const port=5306,cdp=9306,origin=`http://127.0.0.1:${port}`,output=path.join(root,'output/playwright/hwinfo-cpu');
const directory=await mkdtemp(path.join(tmpdir(),'taskmanager-hwinfo-cpu-'));
await mkdir(output,{recursive:true});
await writeFile(path.join(directory,'preferences.json'),JSON.stringify({version:1,intervalMs:1000,ui:{version:1,density:'compact',sidebarCollapsed:false,processView:'flat',pages:{performance:{query:'',sortKey:'name',sortDirection:'ascending',visibleColumns:null,filters:null,scrollTop:0,selectedIdentity:null,resourceType:'cpu',resourceId:null,columnWidths:null}}}}));
const checks=[],screenshots=[];let host,tree=[],browser,page,token,target;
const check=(condition,message)=>{assert.ok(condition,message);checks.push(message);};
const get=async route=>{const response=await fetch(`${origin}/api/${route}`,{signal:AbortSignal.timeout(10000)});assert.ok(response.ok,`${route}: ${response.status}`);return response.json();};
async function post(route,body,expected=200){const response=await fetch(`${origin}/api/${route}`,{method:'POST',headers:{Origin:origin,'Content-Type':'application/json','X-TaskManager-Token':token},body:JSON.stringify(body),signal:AbortSignal.timeout(20000)});assert.equal(response.status,expected,route);return response.json();}
async function start(){assert.ok(!await portOpen(port)&&!await portOpen(cdp),'dedicated ports free');host=spawn(path.join(release,'TaskManager.Host.exe'),[...(desktop?[]:['--headless']),`--port=${port}`],{cwd:release,windowsHide:true,stdio:'ignore',env:{...process.env,TASKMANAGER_DATA_DIR:directory,TASKMANAGER_CDP_PORT:String(cdp)}});await until(async()=>(await get('health')).ready,'real elevated Host ready',45000);token=(await get('bootstrap')).token;tree=processTree(host.pid);}
async function stop(){tree=processTree(host.pid);if(desktop&&page&&!page.isClosed())await page.getByRole('button',{name:'关闭窗口',exact:true}).click();else if(host?.exitCode===null&&host.signalCode===null)host.kill();await until(()=>host.exitCode!==null||host.signalCode!==null,'owned Host exit',20000);await until(async()=>!await portOpen(port)&&!await portOpen(cdp),'owned ports released',15000);check(tree.every(entry=>!ownedAlive(entry)),'exact owned application identities exited');await browser?.close().catch(()=>{});browser=null;page=null;}
async function connectUi(){const {chromium}=await import('playwright');if(desktop){await until(()=>portOpen(cdp),'real Electron CDP listener',45000);browser=await chromium.connectOverCDP(`http://127.0.0.1:${cdp}`);await until(()=>browser.contexts().flatMap(context=>context.pages()).some(p=>p.url().startsWith(origin)),'real Electron page',45000);page=browser.contexts().flatMap(context=>context.pages()).find(p=>p.url().startsWith(origin));}else{browser=await chromium.launch({headless:true});page=await browser.newPage({viewport:{width:1380,height:860},reducedMotion:'reduce'});}page.setDefaultTimeout(12000);await page.goto(origin);await page.getByRole('table',{name:'进程列表',exact:true}).waitFor();await page.emulateMedia({reducedMotion:'reduce'});tree=processTree(host.pid);}
async function capture(name){const destination=path.join(output,`${mode}-${name}.png`);if(desktop){const session=await page.context().newCDPSession(page);const result=await session.send('Page.captureScreenshot',{format:'png',fromSurface:true,captureBeyondViewport:false});await session.detach();await writeFile(destination,Buffer.from(result.data,'base64'));}else await page.screenshot({path:destination});screenshots.push(path.basename(destination));}
const nav=async name=>{await page.getByRole('button',{name,exact:true}).click();await page.locator('.toolbar h1').filter({hasText:name}).waitFor();};
let observed;
try{
  if(mode==='native'){
    const probe=spawnSync('dotnet',['run','--file',path.join(root,'tests/HwInfoLayoutProbe.cs'),'--',path.join(release,'TaskManager.Host.dll')],{cwd:root,encoding:'utf8',windowsHide:true,timeout:60000});
    assert.equal(probe.status,0,probe.stdout+probe.stderr);const parserChecks=probe.stdout.split(/\r?\n/).filter(line=>line.startsWith('PASS\t'));check(parserChecks.length===13,'13 isolated malformed-layout checks reject unsafe inputs through the production parser');
    await writeFile(path.join(output,'malformed-layout.txt'),parserChecks.join('\n')+'\n');
  }
  await start();
  const health=await get('health');check(/CsWin32/.test(health.nativeSource)&&/Newtonsoft/.test(health.json),'production CsWin32 and JSON.NET chain');
  const preferences=(await get('preferences')).preferences;check(!preferences.pages.performance.cpuGraphMode,'legacy preferences without graph mode remain readable');
  await until(async()=>{observed=await get('snapshot');return observed.logicalProcessors.length>0&&observed.logicalProcessors.every(cpu=>Number.isFinite(cpu.percent));},'real per-CPU PDH warmup',45000);
  check(observed.logicalProcessors.every(cpu=>cpu.id===`${cpu.group},${cpu.index}`&&cpu.index<64&&cpu.percent>=0&&cpu.percent<=100),'real processor-group/index identities and valid 0–100 percentages');
  check(new Set(observed.logicalProcessors.map(cpu=>cpu.id)).size===observed.logicalProcessors.length,'logical CPU identities are unique');
  check(Boolean(observed.telemetry.cpuCapturedAtUtc)&&observed.performanceHistory.some(point=>point.cpuTelemetryCapturedAtUtc&&point.logicalProcessors.length===observed.logicalProcessors.length),'independent CPU timestamps and bounded matching history reach HTTP');
  check(observed.performanceHistory.length<=120&&!('sensors' in observed.hwInfo)&&observed.performanceHistory.every(point=>!('hwInfo' in point)),'full HWiNFO sensor rows stay outside snapshots and 120-point history');
  const sensors=await get('hwinfo/sensors');
  if(sensors.status==='unavailable')check(sensors.unavailable.source?.message&&sensors.sensors.every(sensor=>sensor.readings.every(reading=>reading.value===null&&reading.unavailable.value)),'missing real HWiNFO source has explicit reason and no invented current readings');
  else check(['available','stale'].includes(sensors.status)&&sensors.pollTimeUtc&&sensors.sensors.length===sensors.sensorCount,'actual HWiNFO cache carries publication time and sensor groups');
  check(observed.capabilities.hwInfo.available&&observed.capabilities.logicalProcessors.available,'both implemented sources are discoverable independently of current availability');
  if(mode==='native'){
    const counterFile=path.join(directory,'independent-cpu.ps1');
    await writeFile(counterFile,"$ErrorActionPreference='Stop'\n$frames=@(Get-Counter -Counter '\\Processor Information(*)\\% Processor Time' -SampleInterval 1 -MaxSamples 2 -ErrorAction SilentlyContinue)\nif($frames.Count -ne 2){throw 'Missing independent CPU samples'}\n$rows=@($frames[-1].CounterSamples | Where-Object {$_.InstanceName -match '^\\d+,\\d+$' -and $_.Status -eq 0} | ForEach-Object {[pscustomobject]@{id=$_.InstanceName;value=$_.CookedValue}})\nConvertTo-Json -InputObject $rows -Compress\n");
    const independent=spawnSync('powershell.exe',['-NoProfile','-File',counterFile],{encoding:'utf8',windowsHide:true,timeout:30000});assert.equal(independent.status,0,independent.stderr);
    const rows=JSON.parse(independent.stdout.trim());check(rows.length===observed.logicalProcessors.length&&rows.every(row=>observed.logicalProcessors.some(cpu=>cpu.id===row.id)&&Number.isFinite(row.value)&&row.value>=0&&row.value<=100),'independent Windows Get-Counter confirms processor instances and valid percentages');
    target=await post('processes/start',{executable:path.join(root,'tests/TaskManager.TestWorkload/bin/Release',framework,'TaskManager.TestWorkload.exe'),arguments:`"${path.join(directory,'workload')}" 60`});
    await until(async()=>{observed=await get('snapshot');return observed.processes.some(row=>row.identity===target.identity&&row.cpuPercent>0)&&new Set(observed.logicalProcessors.map(cpu=>cpu.percent?.toFixed(3))).size>1;},'owned CPU workload produces independently varying processor values',20000);
    check(observed.logicalProcessors.some(cpu=>cpu.percent>0)&&new Set(observed.logicalProcessors.map(cpu=>cpu.percent?.toFixed(3))).size>1,'real per-CPU values vary under the owned workload instead of copying a total');
    await post(`processes/${target.pid}/terminate`,{creationTimeFileTime:target.creationTimeFileTime});target=null;
  }
  const saved={...preferences,pages:{...preferences.pages,performance:{...preferences.pages.performance,cpuGraphMode:'logicalProcessors'}}};
  await post('preferences',saved);check((await get('preferences')).preferences.pages.performance.cpuGraphMode==='logicalProcessors','logical CPU graph preference round trips through JSON.NET');
  await post('preferences',{...saved,pages:{...saved.pages,performance:{...saved.pages.performance,cpuGraphMode:'physicalCores'}}},400);check((await get('preferences')).preferences.pages.performance.cpuGraphMode==='logicalProcessors','invalid graph mode is rejected without replacing saved preference');
  if(mode!=='native'){
    await post('preferences',preferences);await connectUi();const errors=[];page.on('pageerror',error=>errors.push(error.message));
    let requests=0,inFlight=0,peak=0;const activeRequests=new Set();page.on('request',request=>{if(request.url().includes('/api/hwinfo/sensors')){requests++;activeRequests.add(request);inFlight++;peak=Math.max(peak,inFlight);}});const done=request=>{if(activeRequests.delete(request))inFlight--;};page.on('requestfinished',done);page.on('requestfailed',done);
    await nav('性能');const select=()=>page.getByRole('combobox',{name:'CPU 图表模式',exact:true});
    check(await select().inputValue()==='total','legacy mode opens as total CPU in the production UI');check(requests===0,'unselected HWiNFO does not poll its full sensor list');
    await until(async()=>{const value=await get('snapshot');return value.performanceHistory.length>=4;},'real CPU history accumulates');
    await post('settings',{intervalMs:1000,paused:true});await delay(1200);const frozen=await get('snapshot');
    await capture('cpu-total');
    await page.locator('.cpu-chart-interaction').dblclick();check(await select().inputValue()==='logicalProcessors','double click switches total to per-logical-CPU graphs');
    const cpuIds=await page.locator('[data-cpu-id]').evaluateAll(elements=>elements.map(element=>element.dataset.cpuId));
    check(JSON.stringify(cpuIds)===JSON.stringify(frozen.logicalProcessors.slice(0,48).map(cpu=>cpu.id)),'rendered CPU graphs match actual processor identities and remain at most 48 per page');
    check(await page.locator('.logical-cpu-chart svg[role="img"]').count()===cpuIds.length,'each actual logical CPU has its own accessible fixed-percentage graph');
    await capture('cpu-logical');
    await page.locator('.cpu-chart-interaction').dblclick();check(await select().inputValue()==='total','double click switches per-CPU graphs back to total');
    await page.locator('.cpu-chart-interaction').focus();await page.keyboard.press('Enter');check(await select().inputValue()==='logicalProcessors','Enter switches the focused CPU graph');
    await page.keyboard.press('Space');check(await select().inputValue()==='total'&&await page.locator('.frozen-banner').count()===0,'Space switches CPU mode without freezing display');
    await select().selectOption('logicalProcessors');check((await get('snapshot')).performanceHistory.length===frozen.performanceHistory.length,'mode changes retain the same real history while collection is paused');
    await nav('进程');await nav('性能');check(await select().inputValue()==='logicalProcessors','CPU graph mode survives page navigation');
    for(const [width,height] of [[960,640],[1380,860]]){if(desktop){probe(tree.map(entry=>`${entry.pid}:${entry.fileTime}`).join(','),'resize',width,height);await delay(350);}else await page.setViewportSize({width,height});check(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth&&document.documentElement.scrollHeight<=innerHeight),`logical CPU graphs remain inside the document at ${width}x${height}`);await capture(`cpu-logical-${width}`);}
    if(desktop){await page.keyboard.press('Control+=');await until(async()=>(await get('desktop/window-state')).zoomFactor===1.25,'actual Electron zoom readback');check(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),'logical CPU graphs fit at actual Electron125%');await capture('cpu-logical-125');await page.keyboard.press('Control+0');}
    const tile=()=>page.locator('.resource-tile').filter({has:page.getByText('HWiNFO 传感器',{exact:true})});await tile().click();await page.getByRole('table',{name:'HWiNFO 传感器',exact:true}).waitFor();await until(()=>requests>0,'selected resource reads real HWiNFO cache');
    check(await tile().locator('.resource-chart').count()===0&&await page.locator('.resource-content .resource-chart').count()===0,'HWiNFO uses its sensor table without a fabricated chart');
    if(sensors.status==='unavailable')check((await page.locator('.hwinfo-source').innerText()).includes('Shared Memory Support')&&(await page.locator('.hwinfo-source').innerText()).includes(sensors.unavailable.source.message),'real HWiNFO unavailable reason and recovery instructions are visible');
    await page.getByRole('button',{name:'按当前排序',exact:true}).click();const resizer=page.getByRole('separator',{name:'调整当前列宽',exact:true});await resizer.focus();const before=Number(await resizer.getAttribute('aria-valuenow'));await resizer.press('ArrowRight');check(Number(await resizer.getAttribute('aria-valuenow'))>before,'sensor column supports keyboard resizing');
    await until(async()=>{const pref=(await get('preferences')).preferences.pages.performance;return pref.sortKey==='value'&&pref.columnWidths?.value>before;},'sensor sort and width persist through the production backend');
    await page.getByRole('searchbox').fill('HWiNFO');check(await page.getByRole('table',{name:'HWiNFO 传感器',exact:true}).count()===1,'shared search retains the selected HWiNFO sensor view');await page.getByRole('searchbox').fill('');
    await capture('hwinfo');await nav('进程');await delay(500);const left=requests;await delay(1400);check(requests===left&&peak<=1,'leaving HWiNFO cancels polling and requests remain serial');
    check(errors.length===0,'renderer reports no runtime errors');await nav('性能');await page.locator('.resource-tile').filter({has:page.getByText('CPU',{exact:true})}).click();
    await until(async()=>(await get('preferences')).preferences.pages.performance.resourceType==='cpu','CPU selection is saved for restart');
  }
  await post('settings',{intervalMs:1000,paused:true});const paused=await get('snapshot'),pausedSensors=await get('hwinfo/sensors');await delay(1400);
  check((await get('snapshot')).sampleId===paused.sampleId&&(await get('hwinfo/sensors')).capturedAtUtc===pausedSensors.capturedAtUtc,'accepted pause stops base history and HWiNFO cache publication');
  await post('settings',{intervalMs:1000,paused:false});await until(async()=>(await get('snapshot')).sampleId>paused.sampleId,'real sampling resumes');
  if(mode!=='native'){await until(async()=>await page.locator('.logical-cpu-chart').first().locator('.chart-series-primary > g').count()>=2,'CPU history renders a real time gap after paused sampling',20000);check(true,'resuming collection draws separate per-CPU segments across the real paused interval');}
  await stop();await start();check((await get('preferences')).preferences.pages.performance.cpuGraphMode==='logicalProcessors','graph mode survives a real Host restart with the same isolated preferences');if(mode!=='native'){await connectUi();await nav('性能');check(await page.getByRole('combobox',{name:'CPU 图表模式',exact:true}).inputValue()==='logicalProcessors','restarted UI restores the logical CPU graph mode');}await stop();
  const report={result:'PASS',mode,checkedAtUtc:new Date().toISOString(),assertions:checks.length,checks,logicalProcessorCount:observed.logicalProcessors.length,hwInfo:{status:sensors.status,reason:sensors.unavailable.source?.code??null},notExecuted:sensors.status==='available'?['independent HWiNFO application comparison and publisher stop/restart']:['H05 real HWiNFO success','H06 HWiNFO publisher stale/stop/recovery (no real publisher mapping)'],screenshots};
  await writeFile(path.join(output,`${mode}.json`),JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));
}catch(error){await writeFile(path.join(output,`${mode}-failure.json`),JSON.stringify({result:'FAIL',mode,message:error.message,checks},null,2));throw error;}
finally{if(target&&host?.exitCode===null&&host.signalCode===null)await post(`processes/${target.pid}/terminate`,{creationTimeFileTime:target.creationTimeFileTime}).catch(()=>{});if(host?.exitCode===null&&host.signalCode===null){const owned=processTree(host.pid);for(const entry of owned.reverse())if(ownedAlive(entry)){try{process.kill(entry.pid);}catch{}}}await browser?.close().catch(()=>{});await delay(300);await rm(directory,{recursive:true,force:true});}
