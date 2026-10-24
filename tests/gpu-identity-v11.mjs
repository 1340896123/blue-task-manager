import assert from 'node:assert/strict';
import { spawn, spawnSync } from 'node:child_process';
import { mkdir, mkdtemp, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';
import { portOpen } from './e2e-support.mjs';

const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..'), framework='net10.0-windows10.0.19041.0';
const hostExe=path.join(root,'src/TaskManager.Host/bin/Release',framework,'TaskManager.Host.exe');
const probeExe=path.join(root,'tests/TaskManager.WindowProbe/bin/Release',framework,'TaskManager.WindowProbe.exe');
const port=Number(process.env.TASKMANAGER_GPU_IDENTITY_PORT??5201), origin=`http://127.0.0.1:${port}`;
const directory=await mkdtemp(path.join(tmpdir(),'taskmanager-gpu-identity-'));
const output=path.join(root,'output/playwright');await mkdir(output,{recursive:true});
if (await portOpen(port)) throw new Error('GPU identity test port already in use');
const begin=performance.now();
const host=spawn(hostExe,['--headless',`--port=${port}`],{env:{...process.env,TASKMANAGER_DATA_DIR:directory},windowsHide:true,stdio:'ignore'});
let browser, assertions=0;const checks=[];
const delay=ms=>new Promise(resolve=>setTimeout(resolve,ms));
function check(value,message){assert.ok(value,message);assertions++;checks.push(message);}
async function get(route){const response=await fetch(`${origin}/api/${route}`);assert.equal(response.status,200);return response.json();}
async function until(predicate,message){for(let i=0;i<150;i++){try{const value=await get('snapshot');if(predicate(value))return value;}catch{}await delay(100);}throw new Error(message);}
function run(command,args){const result=spawnSync(command,args,{encoding:'utf8',windowsHide:true,timeout:20000});assert.equal(result.status,0,String(result.error??'')+result.stderr);return JSON.parse(result.stdout.trim());}
try{
  const first=await until(s=>s.sampleId>0,'first snapshot');const basicReadyMs=performance.now()-begin;
  check(first.processes.length>0,'basic snapshot remains available independently of deferred GPU identity');
  const snapshot=await until(s=>s.gpuAdapters.length>0&&s.gpuAdapters.every(g=>g.description&&typeof g.isSoftware==='boolean'&&Number.isFinite(g.utilizationPercent))&&s.processes.some(p=>Number.isFinite(p.gpuPercent)),'real DXGI identity and GPU warmup');
  const independent=run(probeExe,['gpu-identities']);
  check(snapshot.gpuAdapters.every(g=>{const match=g.adapterLuid.match(/^luid_0x([\da-f]+)_0x([\da-f]+)$/i);if(!match)return false;const high=BigInt(`0x${match[1]}`),low=BigInt(`0x${match[2]}`);return independent.some(p=>BigInt.asUintN(32,BigInt(p.highPart))===high&&BigInt(p.lowPart)===low&&p.description.trim()===g.description&&p.software===g.isSoftware);}), 'independent DXGI probe matches exact native LUID high/low bits, description and software flag');
  const names=run('powershell.exe',['-NoProfile','-Command','ConvertTo-Json -InputObject @(Get-CimInstance Win32_VideoController | Select-Object -ExpandProperty Name) -Compress']);
  check(snapshot.gpuAdapters.filter(g=>g.isSoftware===false).every(g=>names.includes(g.description)),'independent Windows video-controller names match actual non-software GPU descriptions');
  check(snapshot.gpuAdapters.every(g=>g.identityCapturedAtUtc&&!g.unavailable.description&&!g.unavailable.isSoftware),'successful GPU identity has actual metadata time and no unavailable marker');
  check(snapshot.processes.some(p=>Number.isFinite(p.gpuPercent)&&p.gpuEngine)&&snapshot.system.gpuPercent===Math.max(...snapshot.gpuAdapters.map(g=>g.utilizationPercent)),'adding identity preserves real process and busiest-adapter GPU telemetry');
  const later=await until(s=>s.telemetry.sampleId>=snapshot.telemetry.sampleId+3,'subsequent PDH samples');
  check(snapshot.gpuAdapters.every(g=>later.gpuAdapters.find(a=>a.id===g.id)?.identityCapturedAtUtc===g.identityCapturedAtUtc),'stable GPU descriptions use cached metadata instead of per-frame DXGI enumeration');
  browser=await chromium.launch({headless:true});const page=await browser.newPage({viewport:{width:1380,height:860},reducedMotion:'reduce'});const errors=[];page.on('pageerror',e=>errors.push(e.message));
  await page.goto(origin);await page.getByRole('table',{name:'进程列表',exact:true}).waitFor();await page.getByRole('button',{name:'性能',exact:true}).click();
  for(let i=0;i<snapshot.gpuAdapters.length;i++){
    const gpu=snapshot.gpuAdapters[i];const choice=page.locator('.resource-tile').filter({has:page.getByText(gpu.description,{exact:true})});await choice.click();
    check((await choice.innerText()).includes(gpu.description)&&(await page.locator('.performance-heading').innerText()).includes(gpu.description),`real GPU ${i+1} Windows description appears in resource selector and heading`);
    check((await choice.getAttribute('title')).includes(gpu.adapterLuid)&&(await choice.getAttribute('aria-label')).includes(gpu.adapterLuid)&&(await page.locator('.resource-fact-grid').innerText()).includes(gpu.adapterLuid),`real GPU ${i+1} keeps its exact LUID visible for identity disambiguation`);
  }
  await page.screenshot({path:path.join(output,'v11-gpu-identity.png')});
  await page.setViewportSize({width:960,height:640});
  check(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),'long actual GPU names wrap without document overflow at 960px');
  await page.screenshot({path:path.join(output,'v11-gpu-identity-960.png')});check(errors.length===0,'GPU identity UI has no runtime errors');
  const report={result:'PASS',assertions,checks,basicReadyMs,gpus:snapshot.gpuAdapters.map(g=>({description:g.description,adapterLuid:g.adapterLuid,isSoftware:g.isSoftware})),source:'production CsWin32 DXGI+PDH; independent native DXGI probe and Windows CIM; real browser UI; no mocks'};
  await writeFile(path.join(output,'v11-gpu-identity-results.json'),JSON.stringify(report,null,2)+'\n');console.log(JSON.stringify(report,null,2));
}finally{await browser?.close();if(host.exitCode===null){const exit=new Promise(resolve=>host.once('exit',resolve));host.kill();await exit;}await rm(directory,{recursive:true,force:true});}
